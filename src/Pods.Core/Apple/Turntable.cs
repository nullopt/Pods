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
    /// Where the earbuds and the case sit in each frame, in frame pixels. Sheets of earbuds with a case
    /// show both side by side, each spinning in its own place with an empty gap between them; each
    /// span is that part's extent over all frames, so a part cropped to it is centred on its axis.
    /// Sheets not listed show a single object (headphones without a case).
    /// </summary>
    static readonly Dictionary<string, TurntableParts> PartsBySheet = new(StringComparer.Ordinal)
    {
        ["0b20"] = new(0, 86, 109, 220),
        ["0b20_00"] = new(1, 86, 111, 219),
        ["0e20"] = new(2, 96, 111, 218),
        ["0f20"] = new(4, 91, 132, 216),
        ["1120"] = new(1, 90, 96, 218),
        ["1120_22"] = new(6, 90, 97, 213),
        ["1120_66"] = new(7, 92, 98, 215),
        ["1220"] = new(3, 90, 97, 218),
        ["1220_33"] = new(1, 89, 97, 218),
        ["1320"] = new(10, 85, 119, 213),
        ["1420"] = new(2, 96, 111, 218),
        ["1620"] = new(5, 91, 97, 213),
        ["1920"] = new(6, 86, 119, 212),
        ["1b20"] = new(6, 86, 119, 212),
        ["1d20"] = new(1, 86, 108, 219),
        ["1d20_02"] = new(1, 86, 108, 219),
        ["2420"] = new(2, 96, 111, 218),
        ["2720"] = new(2, 83, 110, 218),
        ["2f20"] = new(2, 90, 101, 218),
        ["2f20_bb"] = new(2, 90, 101, 218),
        ["3020"] = new(6, 86, 119, 212),
        ["3620"] = new(6, 86, 119, 212),
    };

    /// <summary>Where the earbuds and case sit in this sheet's frames, or null if it shows a single object.</summary>
    public static TurntableParts? Parts(string sheet) => PartsBySheet.TryGetValue(sheet, out var parts) ? parts : null;

    /// <summary>The sheet name for this model and colour, falling back to the model's default; null if none.</summary>
    public static string? Resolve(string modelId, string colour, IReadOnlySet<string> available)
    {
        var exact = $"{modelId}_{colour}";
        if (available.Contains(exact)) return exact;
        return available.Contains(modelId) ? modelId : null;
    }
}

/// <summary>Horizontal extents [start, end) of the earbuds and the case within a turntable frame.</summary>
public readonly record struct TurntableParts(int BudsStart, int BudsEnd, int CaseStart, int CaseEnd);
