using Pods.Core.Aap;
using Pods.Core.Apple;

namespace Pods.Core.Tests.Aap;

public class AapSessionTests
{
    static readonly AppleModel Pro2 = ModelCatalog.Find("1420")!;
    static readonly AppleModel Pro = ModelCatalog.Find("0e20")!;
    static readonly AppleModel AirPods3 = ModelCatalog.Find("1320")!;

    readonly FakeAapChannel channel = new();

    static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(5, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [Fact]
    public async Task Run_SendsHandshakeThenNotificationsThenExtendedForAdaptiveModels()
    {
        var session = new AapSession(channel, Pro2);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        await Until(() => session.State.Ready);
        Assert.Equal([AapFrames.Handshake.ToArray(), AapFrames.EnableNotifications.ToArray(), AapFrames.ExtendedInit.ToArray()], channel.Written);
        channel.Close();
        await run;
    }

    [Fact]
    public async Task Run_NoExtendedInitForOlderModels()
    {
        var session = new AapSession(channel, Pro);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        await Until(() => session.State.Ready);
        Assert.Equal(2, channel.Written.Count);
        channel.Close();
        await run;
    }

    [Fact]
    public async Task Notifications_UpdateState()
    {
        var session = new AapSession(channel, Pro2);
        var changes = 0;
        session.Changed += () => Interlocked.Increment(ref changes);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        channel.Receive("04 00 04 00 04 00 02  02 01 57 02 01  04 01 5B 01 01");
        channel.Receive("04 00 04 00 06 00 00 01");
        channel.Receive("04 00 04 00 09 00 0D 03 00 00 00");
        channel.Receive("04 00 04 00 2B 00 00 00");   // unknown: ignored
        channel.Close();
        await run;
        Assert.Equal(new AapState(true, new PodBattery(91, 87, null, true, false, false), Playback.Partial, NoiseControlMode.Transparency), session.State);
        Assert.Equal(4, changes);   // ready, battery, ear, noise control
    }

    [Fact]
    public async Task NoiseControl_IgnoredForModelsWithout()
    {
        var session = new AapSession(channel, AirPods3);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        channel.Receive("04 00 04 00 09 00 0D 02 00 00 00");
        channel.Close();
        await run;
        Assert.Null(session.State.NoiseControl);
    }

    [Fact]
    public async Task SetNoiseControl_SendsAndShowsAtOnce()
    {
        var session = new AapSession(channel, Pro2);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        await Until(() => session.State.Ready);
        await session.SetNoiseControlAsync(NoiseControlMode.Adaptive, TestContext.Current.CancellationToken);
        Assert.Equal(AapFrames.SetNoiseControl(NoiseControlMode.Adaptive), channel.Written[^1]);
        Assert.Equal(NoiseControlMode.Adaptive, session.State.NoiseControl);
        channel.Close();
        await run;
    }

    [Fact]
    public async Task SetNoiseControl_AdaptiveNotSentToModelWithoutIt()
    {
        var session = new AapSession(channel, Pro);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        await Until(() => session.State.Ready);
        await session.SetNoiseControlAsync(NoiseControlMode.Adaptive, TestContext.Current.CancellationToken);
        Assert.Equal(2, channel.Written.Count);
        channel.Close();
        await run;
    }

    [Fact]
    public async Task SetNoiseControl_BeforeHandshakeDoesNothing()
    {
        var session = new AapSession(channel, Pro2);
        await session.SetNoiseControlAsync(NoiseControlMode.Off, TestContext.Current.CancellationToken);
        Assert.Empty(channel.Written);
    }

    [Fact]
    public async Task Run_HandshakeFailureThrows()
    {
        channel.WriteError = new IOException("gone");
        var session = new AapSession(channel, Pro2);
        await Assert.ThrowsAsync<IOException>(() => session.RunAsync(TestContext.Current.CancellationToken));
        Assert.False(session.State.Ready);
    }

    [Fact]
    public async Task Run_ReadFailureThrows()
    {
        var session = new AapSession(channel, Pro2);
        var run = session.RunAsync(TestContext.Current.CancellationToken);
        channel.Fail(new IOException("dropped"));
        await Assert.ThrowsAsync<IOException>(() => run);
    }
}
