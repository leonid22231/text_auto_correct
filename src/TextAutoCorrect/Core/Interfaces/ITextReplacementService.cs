using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Interfaces;

public interface ITextReplacementService
{
    Task<TextCaptureResult?> CaptureTextAsync(
        ICaptureProgressReporter? progress = null,
        CancellationToken cancellationToken = default);

    Task ReplaceTextAsync(string text, TextCaptureSource source, CancellationToken cancellationToken = default);
}
