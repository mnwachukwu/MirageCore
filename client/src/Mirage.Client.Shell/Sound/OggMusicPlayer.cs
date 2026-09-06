using Microsoft.Xna.Framework.Audio;
using NVorbis;
using System.Collections.Concurrent;

namespace Mirage.Client.Shell.Sound;

/// <summary>
/// Streams a looping OGG track through a <see cref="DynamicSoundEffectInstance"/>. We own the PCM
/// queue: at the loop point the decoder rewinds and keeps filling the SAME buffer, so the seam falls
/// mid-buffer and OpenAL plays it contiguously with no gap. (MonoGame's <c>Song</c>/
/// <c>MediaPlayer.IsRepeating</c> stops and restarts the track instead, which is audibly gappy and
/// cannot skip to an arbitrary loop point.)
/// </summary>
/// <remarks>
/// Honors the RPG-Maker-style loop tags in the file's Vorbis comments, in sample frames:
/// <c>LOOPSTART</c> (frame to loop back to) and optional <c>LOOPLENGTH</c> (loop body length; loop
/// end = LOOPSTART + LOOPLENGTH, defaulting to end-of-file when absent). A track with no valid tags
/// simply loops in full, so untagged files need no changes.
/// </remarks>
public sealed class OggMusicPlayer : IMusicPlayer, IDisposable
{
    // ~186ms of audio per submitted buffer at 44.1kHz.
    private const int FramesPerChunk = 8192;
    // Buffers kept QUEUED AT THE DEVICE. This is the real cushion: eight chunks is ~1.5s the game loop
    // can stall for before the stream runs dry, and a login is the longest such stall.
    private const int TargetPending = 8;
    // Decoded and waiting to be handed over, so a top-up never waits on the decoder.
    private const int QueueDepth = TargetPending + 2;

    private VorbisReader? _reader;
    private DynamicSoundEffectInstance? _sfx;
    private string? _currentPath;
    private int _channels;
    private long _loopStart;          // per-channel frame to loop back to
    private long _loopEnd;            // per-channel frame the loop body ends at (exclusive)
    private float _volume = 1f;

    // Decoding runs on its own thread and hands finished PCM to the audio callback through this queue.
    // BufferNeeded is raised from FrameworkDispatcher on the GAME thread, so decoding there ties the
    // stream's survival to the frame rate: any stall longer than the queued audio is an audible gap.
    // Taking a ready buffer is O(1), so a stalled game loop now costs frames and not sound.
    private BlockingCollection<byte[]>? _ready;
    private readonly ConcurrentBag<byte[]> _spare = new();
    private CancellationTokenSource? _cts;
    private Thread? _decoder;

    public void Play(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        // Already streaming this track — don't restart it (map re-entry, volume toggles, etc.).
        if (_currentPath == fullPath && _sfx is not null)
            return;
        Stop();
        // A missing audio device throws NoAudioHardwareException here; callers catch it and swap in
        // the NullMusicPlayer, so let it propagate.
        _reader = new VorbisReader(fullPath);
        _channels = _reader.Channels;
        ResolveLoopRegion();
        _sfx = new DynamicSoundEffectInstance(_reader.SampleRate,
            _channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo)
        { Volume = _volume };
        _currentPath = fullPath;

        _ready = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), QueueDepth);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _decoder = new Thread(() => DecodeLoop(token))
        {
            IsBackground = true,   // a stuck decoder must never hold the process open
            Name = "music-decode",
        };
        _decoder.Start();

        // Fill the device before starting, so the cushion is there from the first note. The timeout is a
        // guard against a decoder that never produces: silence beats a hung menu.
        for (int i = 0; i < TargetPending; i++)
        {
            if (!_ready.TryTake(out byte[]? first, millisecondsTimeout: 2000)) break;
            _sfx.SubmitBuffer(first, 0, first.Length);
            _spare.Add(first);
        }
        _sfx.BufferNeeded += OnBufferNeeded;
        _sfx.Play();
    }

    /// <summary>
    /// Reads the optional loop-point Vorbis comments (sample frames) into the loop region, falling
    /// back to the whole file when they are absent or nonsensical. Loop end comes from
    /// <c>LOOPLENGTH</c> (body length) or, failing that, <c>LOOPEND</c> (absolute end frame) — both
    /// exclusive; if both are present LOOPLENGTH wins.
    /// </summary>
    private void ResolveLoopRegion()
    {
        long total = _reader!.TotalSamples;
        _loopStart = 0;
        _loopEnd = total;
        long start = ReadFrameTag("LOOPSTART");
        // LOOPSTART must land inside the track; LOOPSTART=0 is valid (loop from the very start while
        // still honoring a shorter length that trims a non-looping outro).
        if (start < 0 || start >= total) return;
        _loopStart = start;
        long len = ReadFrameTag("LOOPLENGTH");
        long end = len > 0 ? start + len : ReadFrameTag("LOOPEND");
        if (end > start) _loopEnd = Math.Min(end, total);
    }

    /// <summary>Parses a non-negative integer Vorbis comment; returns -1 if absent or malformed.</summary>
    private long ReadFrameTag(string key) =>
        long.TryParse(_reader!.Tags.GetTagSingle(key, false), out var v) && v >= 0 ? v : -1;

    /// <summary>Tops the audio device back up to <see cref="TargetPending"/> buffers. Runs on the game
    /// thread, so it only moves bytes; an empty queue means the decoder fell behind and is left to catch
    /// up. Submitting to the target rather than one-per-event is what sets the stall the stream can
    /// survive — the device plays from what it HOLDS, and it is only refilled while the loop is running,
    /// so decoded-but-unsubmitted audio buys nothing during a freeze.</summary>
    private void OnBufferNeeded(object? sender, EventArgs e)
    {
        if (_sfx is null || _ready is null) return;
        while (_sfx.PendingBufferCount < TargetPending && _ready.TryTake(out byte[]? pcm))
        {
            _sfx.SubmitBuffer(pcm, 0, pcm.Length);
            _spare.Add(pcm);   // SubmitBuffer copies, so the array is free to refill immediately
        }
    }

    /// <summary>Decodes ahead of the play head until cancelled, blocking once the queue is full.
    /// Owns <see cref="_reader"/> for its lifetime; <see cref="Stop"/> joins before disposing it.</summary>
    private void DecodeLoop(CancellationToken token)
    {
        var floatBuf = new float[FramesPerChunk * _channels];
        int byteCount = floatBuf.Length * sizeof(short);
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = FillLooping(floatBuf);
                if (read == 0) return;            // empty or degenerate file — nothing more will come
                read -= read % _channels;         // keep the tail frame-aligned so L/R never desync
                if (!_spare.TryTake(out byte[]? pcm) || pcm.Length != byteCount) pcm = new byte[byteCount];
                int bytes = 0;
                for (int i = 0; i < read; i++)
                {
                    short s = (short)Math.Clamp((int)MathF.Round(floatBuf[i] * short.MaxValue),
                        short.MinValue, short.MaxValue);
                    pcm[bytes++] = (byte)s;
                    pcm[bytes++] = (byte)(s >> 8);
                }
                _ready!.Add(pcm, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }   // queue completed while adding
        finally
        {
            // Marks the queue done so a waiting prefill gives up at once instead of sitting out its
            // timeout — a file that decodes to nothing should start silent, not stall the menu.
            try { _ready?.CompleteAdding(); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> completely, rewinding to <see cref="_loopStart"/> whenever the
    /// decoder reaches the loop end. Wrapping within a single buffer is what makes the loop seamless.
    /// </summary>
    private int FillLooping(float[] buffer)
    {
        if (_loopEnd <= _loopStart) return 0; // empty/degenerate file — nothing to play
        int total = 0;
        while (total < buffer.Length)
        {
            long framesLeft = _loopEnd - _reader!.SamplePosition;
            if (framesLeft <= 0)
            {
                _reader.SeekTo(_loopStart);
                continue;
            }
            // Read no further than the loop end; ReadSamples counts interleaved values, positions are
            // per-channel frames, hence the *_channels.
            int want = (int)Math.Min(buffer.Length - total, framesLeft * _channels);
            int got = _reader.ReadSamples(buffer, total, want);
            if (got == 0)
            {
                _reader.SeekTo(_loopStart);
                continue;
            }  // reader hit EOF early (bad tag)
            total += got;
        }
        return total;
    }

    public void Stop()
    {
        if (_sfx is not null)
        {
            _sfx.BufferNeeded -= OnBufferNeeded;
            _sfx.Stop();
            _sfx.Dispose();
            _sfx = null;
        }
        // Cancel first, then join: the decoder may be parked in Add on a full queue, and the token is
        // what releases it. Joining before disposing the reader is what makes the reader single-owner.
        _cts?.Cancel();
        _decoder?.Join(TimeSpan.FromSeconds(2));
        _decoder = null;
        _cts?.Dispose();
        _cts = null;
        _ready?.Dispose();
        _ready = null;
        _spare.Clear();
        _reader?.Dispose();
        _reader = null;
        _currentPath = null;
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_sfx is not null) _sfx.Volume = _volume;
        }
    }

    public void Dispose() => Stop();
}
