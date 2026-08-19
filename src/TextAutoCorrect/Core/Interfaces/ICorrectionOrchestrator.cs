using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Interfaces;

public interface ICorrectionOrchestrator
{
    Task ProcessSelectedTextAsync(CancellationToken cancellationToken = default);
    bool IsBusy { get; }
}

public interface INotificationService
{
    void ShowInfo(string message, string? title = null);
    void ShowError(string message, string? title = null);
}

public interface ICorrectionPreviewService
{
    void ShowCorrection(string original, string corrected, string? notes = null);
}
