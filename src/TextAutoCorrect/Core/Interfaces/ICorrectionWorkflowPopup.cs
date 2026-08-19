using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Interfaces;

public interface ICaptureProgressReporter
{
    void Report(CorrectionWorkflowStage stage, string? detail = null);
}

public interface ICorrectionWorkflowPopup : ICaptureProgressReporter, IDisposable
{
    void SetStage(CorrectionWorkflowStage stage, string? detail = null);
    Task<string?> PickVariantAsync(string original, CorrectionResult result, CancellationToken cancellationToken = default);
    void ShowResult(string original, string applied, CorrectionResult result);
    void ShowNoChanges(string original);
    void ShowError(string message);
}
