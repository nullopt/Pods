using System.Runtime.InteropServices;
using Pods.Core.Media;
using Windows.Media.Control;

namespace Pods.App.Media;

/// <summary>
/// Media playback through Windows' media sessions (the ones in the volume flyout); play on connect
/// presses the media Play/Pause key instead.
/// </summary>
internal sealed partial class SmtcMediaControl : IMediaControl
{
    Task<GlobalSystemMediaTransportControlsSessionManager>? manager;

    Task<GlobalSystemMediaTransportControlsSessionManager> Manager() =>
        manager ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();

    public async Task<bool> IsPlayingAsync() => (await Manager()).GetSessions().Any(IsPlaying);

    public async Task<object?> PauseAllAsync()
    {
        List<GlobalSystemMediaTransportControlsSession> paused = [];
        foreach (var session in (await Manager()).GetSessions().Where(IsPlaying))
        {
            if (await session.TryPauseAsync()) paused.Add(session);
        }
        return paused.Count > 0 ? paused : null;
    }

    public async Task ResumeAsync(object paused)
    {
        var sessions = (await Manager()).GetSessions();
        foreach (var held in (List<GlobalSystemMediaTransportControlsSession>)paused)
        {
            // Only the very session paused earlier: a browser shares one app id across its tabs.
            // The projection hands back the same wrapper for the same session, so identity is enough.
            if (sessions.FirstOrDefault(s => ReferenceEquals(s, held)) is not { } session) continue;
            if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) continue;
            await session.TryPlayAsync();
        }
    }

    public void PressPlayPause()
    {
        keybd_event(VkMediaPlayPause, 0, 0, 0);
        keybd_event(VkMediaPlayPause, 0, KeyEventKeyUp, 0);
    }

    static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
        session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    const byte VkMediaPlayPause = 0xB3;
    const uint KeyEventKeyUp = 0x2;

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte vk, byte scan, uint flags, nuint extraInfo);
}
