using System.Runtime.InteropServices;
using Pods.Core.Media;
using Serilog;
using Windows.Media.Control;

namespace Pods.App.Media;

/// <summary>
/// Media playback through Windows' media sessions (the ones in the volume flyout); play on connect
/// presses the media Play/Pause key instead.
/// </summary>
/// <remarks>
/// Every call to Windows or to a player is capped at <see cref="CallTimeout"/>: the automation runs its
/// actions one at a time, so one frozen player must not hold up the rest.
/// </remarks>
internal sealed partial class SmtcMediaControl : IMediaControl
{
    static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    Task<GlobalSystemMediaTransportControlsSessionManager>? manager;

    /// <summary>The session manager, requested once; a failed request is tried again next time.</summary>
    async Task<GlobalSystemMediaTransportControlsSessionManager> Manager()
    {
        var request = manager ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
        try
        {
            return await request.WaitAsync(CallTimeout);
        }
        catch
        {
            if (manager == request && !request.IsCompletedSuccessfully) manager = null;
            throw;
        }
    }

    public async Task<bool> IsPlayingAsync() => (await Manager()).GetSessions().Any(IsPlaying);

    public async Task<object?> PauseAllAsync()
    {
        List<GlobalSystemMediaTransportControlsSession> paused = [];
        foreach (var session in (await Manager()).GetSessions().Where(IsPlaying))
        {
            if (await Try(() => session.TryPauseAsync().AsTask(), session)) paused.Add(session);
        }
        return paused.Count > 0 ? paused : null;
    }

    public async Task ResumeAsync(object paused)
    {
        var sessions = (await Manager()).GetSessions();
        foreach (var held in (List<GlobalSystemMediaTransportControlsSession>)paused)
        {
            // Only the very session paused earlier (a browser shares one app id across its tabs); the
            // projection hands back the same wrapper for the same session, so identity is enough.
            if (sessions.FirstOrDefault(s => ReferenceEquals(s, held)) is not { } session) continue;
            if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) continue;
            await Try(() => session.TryPlayAsync().AsTask(), session);
        }
    }

    public void PressPlayPause()
    {
        keybd_event(VkMediaPlayPause, 0, 0, 0);
        keybd_event(VkMediaPlayPause, 0, KeyEventKeyUp, 0);
    }

    /// <summary>One player's answer; a player that fails or does not answer in time is skipped, not the rest.</summary>
    static async Task<bool> Try(Func<Task<bool>> call, GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return await call().WaitAsync(CallTimeout);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Media session {App} did not respond", session.SourceAppUserModelId);
            return false;
        }
    }

    static bool IsPlaying(GlobalSystemMediaTransportControlsSession session) =>
        session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    const byte VkMediaPlayPause = 0xB3;
    const uint KeyEventKeyUp = 0x2;

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte vk, byte scan, uint flags, nuint extraInfo);
}
