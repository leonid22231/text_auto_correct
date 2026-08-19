using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Services;

public sealed class CorrectionOrchestrator : ICorrectionOrchestrator
{
    private readonly ITextReplacementService _textReplacementService;
    private readonly IAiCorrectionProvider _aiProvider;
    private readonly ICorrectionHistoryService _correctionHistory;
    private readonly IPopupService _popupService;
    private readonly AiSettings _aiSettings;
    private readonly ILogger<CorrectionOrchestrator> _logger;
    private int _busy;

    public CorrectionOrchestrator(
        ITextReplacementService textReplacementService,
        IAiCorrectionProvider aiProvider,
        ICorrectionHistoryService correctionHistory,
        IPopupService popupService,
        IOptions<AppSettings> options,
        ILogger<CorrectionOrchestrator> logger)
    {
        _textReplacementService = textReplacementService;
        _aiProvider = aiProvider;
        _correctionHistory = correctionHistory;
        _popupService = popupService;
        _aiSettings = options.Value.Ai;
        _logger = logger;
    }

    public bool IsBusy => Volatile.Read(ref _busy) > 0;

    public async Task ProcessSelectedTextAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            _popupService.ShowInfo("Исправление уже выполняется.");
            return;
        }

        using var workflow = _popupService.BeginWorkflow();

        try
        {
            workflow.SetStage(CorrectionWorkflowStage.SearchingText);

            var capture = await _textReplacementService.CaptureTextAsync(workflow, cancellationToken);
            if (capture is null || string.IsNullOrWhiteSpace(capture.Text))
            {
                workflow.ShowError("Текст не найден. Выделите текст и нажмите hotkey снова.");
                return;
            }

            workflow.SetStage(
                CorrectionWorkflowStage.TextFound,
                FormatFoundDetail(capture.Text, capture.Source));

            await Task.Delay(350, cancellationToken);

            workflow.SetStage(CorrectionWorkflowStage.Correcting);

            CorrectionResult result;
            try
            {
                result = await _aiProvider.CorrectAsync(new CorrectionRequest
                {
                    Text = capture.Text,
                    Mode = _aiSettings.DefaultPromptMode
                }, cancellationToken);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "AI correction timed out.");
                workflow.ShowError(ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI correction failed.");
                workflow.ShowError(ex.Message);
                return;
            }

            var finalText = result.Primary;
            if (result.RequiresUserChoice)
            {
                var chosen = await workflow.PickVariantAsync(capture.Text, result, cancellationToken);
                if (string.IsNullOrWhiteSpace(chosen))
                    return;

                finalText = chosen;
            }

            if (string.Equals(finalText, capture.Text, StringComparison.Ordinal))
            {
                workflow.ShowNoChanges(capture.Text);
                return;
            }

            workflow.SetStage(CorrectionWorkflowStage.Correcting, "Заменяю текст...");

            try
            {
                await _textReplacementService.ReplaceTextAsync(finalText, capture.Source, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Text replacement failed.");
                workflow.ShowError(ex.Message);
                return;
            }

            _correctionHistory.MarkCorrected(capture.Text);

            await Task.Delay(100, cancellationToken);
            workflow.ShowResult(capture.Text, finalText, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected correction workflow error.");
            workflow.ShowError(ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static string FormatFoundDetail(string text, TextCaptureSource source)
    {
        var preview = PopupPlacementHelperCompact(text);
        return source == TextCaptureSource.RecentClipboard
            ? $"{preview} (из буфера)"
            : preview;
    }

    private static string PopupPlacementHelperCompact(string text)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }
}
