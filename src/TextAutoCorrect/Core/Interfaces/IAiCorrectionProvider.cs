using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Interfaces;

public interface IAiCorrectionProvider
{
    string ProviderName { get; }
    Task<CorrectionResult> CorrectAsync(CorrectionRequest request, CancellationToken cancellationToken = default);
}
