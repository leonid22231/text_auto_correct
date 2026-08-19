namespace TextAutoCorrect.Core.Models;

public sealed class CorrectionRequest
{
    public required string Text { get; init; }
    public PromptMode Mode { get; init; } = PromptMode.Auto;
    public string? DetectedLanguage { get; init; }
}
