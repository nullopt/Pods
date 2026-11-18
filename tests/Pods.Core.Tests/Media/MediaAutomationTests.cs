using Pods.Core.Apple;
using Pods.Core.Devices;
using Pods.Core.Media;
using Pods.Core.Settings;

namespace Pods.Core.Tests.Media;

public sealed class MediaAutomationTests
{
    sealed class FakeMedia : IMediaControl
    {
        int tokens;
        public bool Playing;
        public bool ThrowNext;
        public readonly List<string> Log = [];

        public Task<bool> IsPlayingAsync()
        {
            MaybeThrow();
            return Task.FromResult(Playing);
        }

        public Task<object?> PauseAllAsync()
        {
            MaybeThrow();
            if (!Playing) return Task.FromResult<object?>(null);
            Playing = false;
            Log.Add("pause");
            return Task.FromResult<object?>($"t{++tokens}");
        }

        public Task ResumeAsync(object paused)
        {
            MaybeThrow();
            Log.Add($"resume:{paused}");
            Playing = true;
            return Task.CompletedTask;
        }

        public void PressPlayPause()
        {
            Log.Add("key");
            Playing = true;
        }

        void MaybeThrow()
        {
            if (!ThrowNext) return;
            ThrowNext = false;
            throw new InvalidOperationException("media unavailable");
        }
    }

    static readonly MediaSettings On = new(true, true, false);
    readonly FakeMedia media = new();
    readonly MediaAutomation auto;
    readonly List<(string, MediaActionKind)> acted = [];
    readonly List<Exception> failed = [];

    public MediaAutomationTests()
    {
        auto = new MediaAutomation(media);
        auto.Acted += (k, a) => acted.Add((k, a));
        auto.Failed += failed.Add;
    }

    static HeadphoneSnapshot Snap(DeviceState state, Playback? playback, string key = "k1") =>
        new(key, "AirPods Pro 2", "1420", "00", state, null, playback, false);

    static HeadphoneSnapshot Connected(Playback? playback, string key = "k1") => Snap(DeviceState.Connected, playback, key);

    async Task Feed(MediaSettings settings, params HeadphoneSnapshot[] snapshots)
    {
        auto.Update(snapshots, _ => settings);
        await auto.WhenIdle();
    }

    Task Feed(params HeadphoneSnapshot[] snapshots) => Feed(On, snapshots);

    [Fact]
    public async Task FirstSnapshot_NoAction()
    {
        media.Playing = true;
        await Feed(Connected(Playback.Partial));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task EarOut_Playing_Pauses()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        Assert.Equal(["pause"], media.Log);
        Assert.Equal([("k1", MediaActionKind.PausedForEar)], acted);
    }

    [Fact]
    public async Task EarOut_NothingPlaying_NoPause()
    {
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        Assert.Empty(media.Log);
        Assert.Empty(acted);
    }

    [Fact]
    public async Task EarBackIn_ResumesWhatWePaused()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        await Feed(Connected(Playback.AllIn));
        Assert.Equal(["pause", "resume:t1"], media.Log);
        Assert.Equal(MediaActionKind.Resumed, acted[^1].Item2);
    }

    [Fact]
    public async Task EarBackIn_WithoutOurPause_NoResume()
    {
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        await Feed(Connected(Playback.AllIn));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task SecondPodOut_PausesOnce()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        media.Playing = true;   // even if something else started meanwhile
        await Feed(Connected(Playback.NoneIn));
        Assert.Equal(["pause"], media.Log);
    }

    [Fact]
    public async Task PodIntoCase_StaysPausedUntilBackInEar()
    {
        var bothIn = AdvertDecoder.Decode(Convert.FromHexString("07190114202ba98f110004ee21a5091ebf3feea28983c42bb78592"), -50, 1)!;
        var leftInCase = AdvertDecoder.Decode(Convert.FromHexString("071901142013aaa91100046b8e7cb7e727e61b8114c14c1f9c2b51"), -50, 1)!;
        Assert.Equal(Playback.AllIn, bothIn.Playback);
        Assert.Equal(Playback.Partial, leftInCase.Playback);

        media.Playing = true;
        await Feed(Connected(bothIn.Playback));
        await Feed(Connected(Playback.Partial));   // left taken out
        await Feed(Connected(leftInCase.Playback)); // and put in the case
        Assert.Equal(["pause"], media.Log);
    }

    [Fact]
    public async Task EarDetectionOff_NoPause()
    {
        media.Playing = true;
        var off = On with { EarDetection = false };
        await Feed(off, Connected(Playback.AllIn));
        await Feed(off, Connected(Playback.Partial));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task EarSettingOffAfterPause_StillResumes()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        await Feed(On with { EarDetection = false }, Connected(Playback.AllIn));
        Assert.Equal(["pause", "resume:t1"], media.Log);
    }

    [Fact]
    public async Task NotConnected_NoEarAction()
    {
        media.Playing = true;
        await Feed(Snap(DeviceState.Disconnected, Playback.AllIn));
        await Feed(Snap(DeviceState.Disconnected, Playback.Partial));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task PlaybackChangesWithConnect_NoEarAction()
    {
        media.Playing = true;
        await Feed(Snap(DeviceState.Disconnected, Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task Disconnect_Playing_PausesOnce()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Snap(DeviceState.Disconnected, Playback.AllIn));
        media.Playing = true;
        await Feed(Snap(DeviceState.Disconnected, Playback.AllIn));
        Assert.Equal(["pause"], media.Log);
        Assert.Equal([("k1", MediaActionKind.PausedOnDisconnect)], acted);
    }

    [Fact]
    public async Task Disconnect_SettingOff_NoPause()
    {
        media.Playing = true;
        var off = On with { PauseOnDisconnect = false };
        await Feed(off, Connected(Playback.AllIn));
        await Feed(off, Snap(DeviceState.Disconnected, Playback.AllIn));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task Disconnect_AfterEarPause_NoSecondPause_AndForgets()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        media.Playing = true;   // the user started something else; the disconnect must not pause it
        await Feed(Snap(DeviceState.Disconnected, Playback.Partial));
        media.Playing = false;
        await Feed(Connected(Playback.Partial));
        await Feed(Connected(Playback.AllIn));
        Assert.Equal(["pause"], media.Log);
    }

    [Fact]
    public async Task Reconnect_PausesAgain()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Snap(DeviceState.Disconnected, Playback.AllIn));
        await Feed(Connected(Playback.AllIn));
        media.Playing = true;
        await Feed(Snap(DeviceState.Searching, Playback.AllIn));
        Assert.Equal(["pause", "pause"], media.Log);
    }

    [Fact]
    public async Task Connect_PlayOnConnect_PressesKey()
    {
        var play = On with { PlayOnConnect = true };
        await Feed(play, Snap(DeviceState.Disconnected, Playback.NoneIn));
        await Feed(play, Connected(Playback.NoneIn));
        Assert.Equal(["key"], media.Log);
        Assert.Equal([("k1", MediaActionKind.PlayedOnConnect)], acted);
    }

    [Fact]
    public async Task Connect_AlreadyPlaying_NoKey()
    {
        media.Playing = true;
        var play = On with { PlayOnConnect = true };
        await Feed(play, Snap(DeviceState.Disconnected, Playback.NoneIn));
        await Feed(play, Connected(Playback.NoneIn));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task Connect_PlayOnConnectOff_NoKey()
    {
        await Feed(Snap(DeviceState.Disconnected, Playback.NoneIn));
        await Feed(Connected(Playback.NoneIn));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task StartupConnected_NoPlay()
    {
        await Feed(On with { PlayOnConnect = true }, Connected(Playback.NoneIn));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task DeviceRemoved_CountsAsDisconnect()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn));
        await Feed();
        Assert.Equal(["pause"], media.Log);
    }

    [Fact]
    public async Task DeviceRemoved_ThenBack_IsBaselineAgain()
    {
        await Feed(Connected(Playback.AllIn));
        await Feed();
        media.Playing = true;
        await Feed(Connected(Playback.Partial));
        Assert.Empty(media.Log);
    }

    [Fact]
    public async Task TwoDevices_TokensKeptApart()
    {
        media.Playing = true;
        await Feed(Connected(Playback.AllIn, "k1"), Connected(Playback.AllIn, "k2"));
        await Feed(Connected(Playback.Partial, "k1"), Connected(Playback.AllIn, "k2"));
        await Feed(Connected(Playback.Partial, "k1"), Connected(Playback.Partial, "k2"));
        await Feed(Connected(Playback.Partial, "k1"), Connected(Playback.AllIn, "k2"));
        Assert.Equal(["pause"], media.Log);
        await Feed(Connected(Playback.AllIn, "k1"), Connected(Playback.AllIn, "k2"));
        Assert.Equal(["pause", "resume:t1"], media.Log);
    }

    [Fact]
    public async Task ControlThrows_LaterEventsStillAct()
    {
        media.Playing = true;
        media.ThrowNext = true;
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        Assert.Single(failed);
        await Feed(Connected(Playback.AllIn));
        await Feed(Connected(Playback.Partial));
        Assert.Equal(["pause"], media.Log);
    }
}
