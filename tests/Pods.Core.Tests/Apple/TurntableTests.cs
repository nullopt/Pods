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

    [Fact]
    public void Parts_SplitSheet() => Assert.Equal(new TurntableParts(2, 96, 111, 218), Turntable.Parts("1420"));

    [Fact]
    public void Parts_BeatsSheetsGone() => Assert.Null(Turntable.Parts("1120_66"));

    [Fact]
    public void Parts_SingleObjectSheetHasNone()
    {
        Assert.Null(Turntable.Parts("0a20"));
        Assert.Null(Turntable.Parts("0a20_0f"));
    }

    [Fact]
    public void Parts_EarbudsLeftOfCase_NoOverlap()
    {
        foreach (var sheet in new[] { "0e20", "0f20", "1320", "1420", "1920", "1b20", "2420", "2720", "3020", "3620" })
        {
            var p = Turntable.Parts(sheet)!.Value;
            Assert.True(p.BudsStart < p.BudsEnd && p.BudsEnd <= p.CaseStart && p.CaseStart < p.CaseEnd, sheet);
        }
    }
}
