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

    /// <summary>The sheet name for this model and colour, falling back to the model's default; null if none.</summary>
    public static string? Resolve(string modelId, string colour, IReadOnlySet<string> available)
    {
        var exact = $"{modelId}_{colour}";
        if (available.Contains(exact)) return exact;
        return available.Contains(modelId) ? modelId : null;
    }
}
