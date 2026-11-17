using Pods.Core.Apple;
namespace Pods.Core.Tests.Apple;

public class FixtureTests
{
    public static TheoryData<string, int?, int?, int?, bool, bool, string> Rows()
    {
        var data = new TheoryData<string, int?, int?, int?, bool, bool, string>();
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "adverts.tsv")))
        {
            if (line.StartsWith('#') || line.Length == 0) continue;
            var c = line.Split('\t');
            static int? N(string s) => s.Length == 0 ? null : int.Parse(s);
            data.Add(c[0], N(c[1]), N(c[2]), N(c[3]), c[4] == "1", c[5] == "1", c[6]);
        }
        return data;
    }

    [Theory, MemberData(nameof(Rows))]
    public void RealAdvertDecodesAsObserved(string hex, int? l, int? r, int? c, bool inL, bool inR, string playback)
    {
        var a = AdvertDecoder.Decode(Convert.FromHexString(hex), -50, 1)!;
        Assert.Equal((l, r, c), (a.Battery.Left, a.Battery.Right, a.Battery.Case));
        Assert.Equal((inL, inR), (a.LeftInEar, a.RightInEar));
        Assert.Equal(playback, a.Playback?.ToString() ?? "");
    }
}
