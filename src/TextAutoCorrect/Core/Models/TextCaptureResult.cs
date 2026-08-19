namespace TextAutoCorrect.Core.Models;

public enum TextCaptureSource
{
    Selection,
    RecentClipboard
}

public sealed class TextCaptureResult
{
    public required string Text { get; init; }
    public TextCaptureSource Source { get; init; }
}
