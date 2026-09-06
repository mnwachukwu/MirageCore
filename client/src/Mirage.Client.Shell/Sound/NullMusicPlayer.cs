namespace Mirage.Client.Shell.Sound;

/// <summary>No-op <see cref="IMusicPlayer"/> used when audio initialization fails, so the rest of
/// the client can call music methods unconditionally.</summary>
internal sealed class NullMusicPlayer : IMusicPlayer
{
    public void Play(string filePath) { }
    public void Stop() { }
    public float Volume { get; set; }
}
