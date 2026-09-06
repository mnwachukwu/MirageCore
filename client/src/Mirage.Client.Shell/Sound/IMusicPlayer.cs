namespace Mirage.Client.Shell.Sound;

/// <summary>Background music playback, abstracted so the client can fall back to
/// <see cref="NullMusicPlayer"/> when no audio device is available.</summary>
public interface IMusicPlayer
{
    /// <summary>Start (or restart) looping the track at <paramref name="filePath"/>.</summary>
    void Play(string filePath);
    /// <summary>Stop playback and release the current track.</summary>
    void Stop();
    /// <summary>Playback volume in [0,1].</summary>
    float Volume { get; set; }
}
