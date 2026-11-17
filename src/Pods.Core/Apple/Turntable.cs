namespace Pods.Core.Apple;

/// <summary>
/// Spinning device artwork: one sprite sheet per model, optionally per colour
/// (<c>&lt;model&gt;_&lt;colour&gt;</c>), 180 frames in a 12 × 15 grid.
/// </summary>
public static class Turntable
{
    public const int Columns = 12;
    public const int Rows = 15;
    public const int FrameCount = Columns * Rows;

    /// <summary>One full turn, matching the iPhone case-open card's pace.</summary>
    public static readonly TimeSpan TurnDuration = TimeSpan.FromSeconds(5.8);

    /// <summary>
    /// Where to cut each frame between the earbuds (left) and the case (right), in frame pixels.
    /// Sheets of earbuds with a case show both side by side, each spinning in its own half with an
    /// empty gap between them in every frame; the cut is the middle of that gap. Sheets not listed
    /// show a single object (over-ear headphones).
    /// </summary>
    static readonly Dictionary<string, int> Cuts = new(StringComparer.Ordinal)
    {
        ["0b20"] = 97, ["0b20_00"] = 98, ["0e20"] = 103, ["0f20"] = 111, ["1120"] = 92, ["1120_22"] = 93,
        ["1120_66"] = 94, ["1220"] = 93, ["1220_33"] = 92, ["1320"] = 101, ["1420"] = 103, ["1620"] = 93,
        ["1920"] = 102, ["1b20"] = 102, ["1d20"] = 96, ["1d20_02"] = 96, ["2420"] = 103, ["2720"] = 96,
        ["2f20"] = 95, ["2f20_bb"] = 95, ["3020"] = 102, ["3620"] = 102,
    };

    /// <summary>The x of the cut between earbuds and case for this sheet, or null if it shows a single object.</summary>
    public static int? Cut(string sheet) => Cuts.TryGetValue(sheet, out var x) ? x : null;

    /// <summary>The sheet name for this model and colour, falling back to the model's default; null if none.</summary>
    public static string? Resolve(string modelId, string colour, IReadOnlySet<string> available)
    {
        var exact = $"{modelId}_{colour}";
        if (available.Contains(exact)) return exact;
        return available.Contains(modelId) ? modelId : null;
    }
}
