using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Interfaces;

public interface IPopupService
{
    ICorrectionWorkflowPopup BeginWorkflow();
    IDisposable ShowProgress(string message = "Исправляю текст...");
    Task<string?> PickVariantAsync(string original, CorrectionResult result, CancellationToken cancellationToken = default);
    void ShowResult(string original, string applied, CorrectionResult result);
    void ShowNoChanges();
    void ShowInfo(string message);
    void ShowError(string message);
}
