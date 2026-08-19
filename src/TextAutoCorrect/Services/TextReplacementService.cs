using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Services;

public sealed class TextReplacementService : ITextReplacementService
{
    private readonly IClipboardService _clipboardService;
    private readonly IClipboardMonitor _clipboardMonitor;
    private readonly ICorrectionHistoryService _correctionHistory;
    private readonly KeyboardSimulator _keyboardSimulator;
    private readonly ClipboardSettings _settings;
    private readonly ILogger<TextReplacementService> _logger;

    public TextReplacementService(
        IClipboardService clipboardService,
        IClipboardMonitor clipboardMonitor,
        ICorrectionHistoryService correctionHistory,
        KeyboardSimulator keyboardSimulator,
        IOptions<AppSettings> options,
        ILogger<TextReplacementService> logger)
    {
        _clipboardService = clipboardService;
        _clipboardMonitor = clipboardMonitor;
        _correctionHistory = correctionHistory;
        _keyboardSimulator = keyboardSimulator;
        _settings = options.Value.Clipboard;
        _logger = logger;
    }

    public Task<TextCaptureResult?> CaptureTextAsync(
        ICaptureProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _keyboardSimulator.RunWithInputFocusAsync(async () =>
        {
            var snapshot = await _clipboardService.CaptureSnapshotAsync(cancellationToken);
            var cooldown = TimeSpan.FromSeconds(_settings.CorrectedTextCooldownSeconds);

            try
            {
                progress?.Report(CorrectionWorkflowStage.SearchingText);

                var beforeCopy = await _clipboardService.GetTextAsync(cancellationToken);
                _logger.LogDebug("Capture start. Clipboard before copy length={Length}", beforeCopy?.Length ?? 0);

                var firstAttempt = await CopyAndPollAsync(beforeCopy, extended: false, cancellationToken);
                if (TryCreateCapture(firstAttempt, cooldown, out var fromSelection))
                {
                    _logger.LogInformation("Captured text from selection via Ctrl+C.");
                    progress?.Report(CorrectionWorkflowStage.TextFound, CompactPreview(fromSelection!.Text));
                    return fromSelection;
                }

                var maxAge = TimeSpan.FromSeconds(_settings.RecentClipboardMaxAgeSeconds);
                var recent = _clipboardMonitor.GetRecentText(maxAge);
                if (!string.IsNullOrWhiteSpace(recent) && !_correctionHistory.WasRecentlyCorrected(recent, cooldown))
                {
                    _logger.LogInformation(
                        "Using recent clipboard text copied within {Seconds}s.",
                        _settings.RecentClipboardMaxAgeSeconds);
                    progress?.Report(CorrectionWorkflowStage.TextFound, $"{CompactPreview(recent)} (из буфера)");
                    return new TextCaptureResult
                    {
                        Text = recent.Trim(),
                        Source = TextCaptureSource.RecentClipboard
                    };
                }

                progress?.Report(CorrectionWorkflowStage.NotFound);
                progress?.Report(CorrectionWorkflowStage.TryingCopy);

                _logger.LogInformation("Retrying Ctrl+C with extended wait (browser/editor fallback).");
                await Task.Delay(_settings.ForceCopyDelayMs, cancellationToken);
                var beforeForceCopy = await _clipboardService.GetTextAsync(cancellationToken);
                var forcedAttempt = await CopyAndPollAsync(beforeForceCopy, extended: true, cancellationToken);

                if (TryCreateCapture(forcedAttempt, cooldown, out var forcedCapture))
                {
                    _logger.LogInformation("Captured text after forced Ctrl+C.");
                    progress?.Report(CorrectionWorkflowStage.TextFound, CompactPreview(forcedCapture!.Text));
                    return forcedCapture;
                }

                _logger.LogWarning(
                    "Capture failed. Before={BeforeLen}, After={AfterLen}, Changed={Changed}",
                    beforeCopy?.Length ?? 0,
                    forcedAttempt.Text?.Length ?? 0,
                    forcedAttempt.Changed);

                return null;
            }
            finally
            {
                if (snapshot is not null)
                    await _clipboardService.RestoreSnapshotAsync(snapshot, cancellationToken);
            }
        }, cancellationToken);
    }

    public Task ReplaceTextAsync(string text, TextCaptureSource source, CancellationToken cancellationToken = default)
    {
        return _keyboardSimulator.RunWithInputFocusAsync(async () =>
        {
            IClipboardSnapshot? snapshot = null;
            if (_settings.RestoreClipboardAfterPaste)
                snapshot = await _clipboardService.CaptureSnapshotAsync(cancellationToken);

            await _clipboardService.SetTextAsync(text, cancellationToken);
            await _keyboardSimulator.SendPasteAsync(cancellationToken);
            await Task.Delay(_settings.KeyDelayMs, cancellationToken);

            if (_settings.RestoreClipboardAfterPaste && snapshot is not null)
                await _clipboardService.RestoreSnapshotAsync(snapshot, cancellationToken);
        }, cancellationToken);
    }

    private async Task<CopyPollResult> CopyAndPollAsync(
        string? beforeCopy,
        bool extended,
        CancellationToken cancellationToken)
    {
        await _keyboardSimulator.SendCopyAsync(cancellationToken);
        await Task.Delay(extended ? _settings.ForceCopyDelayMs : _settings.CopySettleDelayMs, cancellationToken);

        var retryCount = extended ? _settings.CopyRetryCount * 2 : _settings.CopyRetryCount;
        var pollInterval = extended ? Math.Max(_settings.CopyPollIntervalMs, 100) : _settings.CopyPollIntervalMs;

        string? captured = null;
        for (var attempt = 0; attempt < retryCount; attempt++)
        {
            await Task.Delay(pollInterval, cancellationToken);
            captured = await _clipboardService.GetTextAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(captured) &&
                !string.Equals(captured, beforeCopy, StringComparison.Ordinal))
            {
                return new CopyPollResult(captured, Changed: true);
            }
        }

        if (!string.IsNullOrWhiteSpace(captured) && string.IsNullOrWhiteSpace(beforeCopy))
            return new CopyPollResult(captured, Changed: true);

        return new CopyPollResult(captured, Changed: false);
    }

    private bool TryCreateCapture(
        CopyPollResult copyResult,
        TimeSpan cooldown,
        out TextCaptureResult? result)
    {
        result = null;
        if (!copyResult.Changed || string.IsNullOrWhiteSpace(copyResult.Text))
            return false;

        if (_correctionHistory.WasRecentlyCorrected(copyResult.Text, cooldown))
        {
            _logger.LogDebug("Skipping recently corrected clipboard text.");
            return false;
        }

        result = new TextCaptureResult
        {
            Text = copyResult.Text.Trim(),
            Source = TextCaptureSource.Selection
        };
        return true;
    }

    private static string CompactPreview(string text)
    {
        var normalized = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= 80 ? normalized : normalized[..77] + "...";
    }

    private readonly record struct CopyPollResult(string? Text, bool Changed);
}
