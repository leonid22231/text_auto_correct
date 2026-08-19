namespace TextAutoCorrect.Core.Models;

public sealed class CorrectionResult
{
    public required ConfidenceLevel Confidence { get; init; }
    public required string Primary { get; init; }
    public IReadOnlyList<string> Alternatives { get; init; } = [];
    public string? Notes { get; init; }

    public bool RequiresUserChoice =>
        Confidence is ConfidenceLevel.Medium or ConfidenceLevel.Low;

    public IEnumerable<string> AllOptions()
    {
        yield return Primary;
        foreach (var alternative in Alternatives)
        {
            if (!string.Equals(alternative, Primary, StringComparison.Ordinal))
                yield return alternative;
        }
    }
}
