using Pods.Core.Apple;

namespace Pods.Core.Tests.Apple;

public class TurntableTests
{
    static readonly HashSet<string> Available = ["1420", "0a20", "0a20_0f", "0a20_31"];

    [Theory]
    [InlineData("0a20", "0f", "0a20_0f")]  // exact colour
    [InlineData("0a20", "31", "0a20_31")]
    [InlineData("0a20", "02", "0a20")]     // colour without its own sheet → model default
    [InlineData("1420", "00", "1420")]
    public void Resolve_PicksColourThenModel(string model, string colour, string expected) =>
        Assert.Equal(expected, Turntable.Resolve(model, colour, Available));

    [Fact]
    public void Resolve_UnknownModelHasNone() => Assert.Null(Turntable.Resolve("2720", "00", Available));
}
