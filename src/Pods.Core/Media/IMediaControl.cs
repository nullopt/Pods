namespace Pods.Core.Media;

/// <summary>The PC's media playback, as far as the automation needs it.</summary>
public interface IMediaControl
{
    /// <summary>Whether any media session is playing.</summary>
    Task<bool> IsPlayingAsync();

    /// <summary>Pauses every playing session; returns what it paused, or null if nothing was playing.</summary>
    Task<object?> PauseAllAsync();

    /// <summary>Plays each session in <paramref name="paused"/> that still exists and is still paused.</summary>
    Task ResumeAsync(object paused);

    /// <summary>Presses the media Play/Pause key.</summary>
    void PressPlayPause();
}
