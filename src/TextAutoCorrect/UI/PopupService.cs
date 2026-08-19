using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.UI;

public sealed class PopupService : IPopupService, INotificationService, ICorrectionPreviewService
{
    private readonly UiSettings _settings;
    private readonly ILogger<PopupService> _logger;
    private readonly object _gate = new();
    private Window? _activePopup;
    private ProgressPopup? _progressPopup;

    public PopupService(IOptions<AppSettings> options, ILogger<PopupService> logger)
    {
        _settings = options.Value.Ui;
        _logger = logger;
    }

    public ICorrectionWorkflowPopup BeginWorkflow()
    {
        WorkflowPopup? popup = null;
        RunOnUi(() =>
        {
            lock (_gate)
            {
                CloseActivePopupLocked();
                popup = new WorkflowPopup(_settings);
                _activePopup = popup;
                WireClosed(popup);
                popup.SetStage(CorrectionWorkflowStage.SearchingText);
                popup.ShowNearPointer();
            }
        });

        return new WorkflowSession(this, popup!);
    }

    public IDisposable ShowProgress(string message = "Исправляю текст...")
    {
        RunOnUi(() =>
        {
            lock (_gate)
            {
                CloseActivePopupLocked();
                _progressPopup = new ProgressPopup(message);
                _activePopup = _progressPopup;
                WireClosed(_progressPopup);
                _progressPopup.ShowNearPointer();
            }
        });

        return new ProgressScope(this);
    }

    public Task<string?> PickVariantAsync(string original, CorrectionResult result, CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        RunOnUi(() =>
        {
            try
            {
                lock (_gate)
                {
                    CloseActivePopupLocked();
                    var popup = new CorrectionResultPopup(original, result, CorrectionPopupMode.Select, null, _settings, tcs);
                    _activePopup = popup;
                    WireClosed(popup);
                    popup.Show();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show variant popup.");
                tcs.TrySetResult(null);
            }
        });

        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => tcs.TrySetResult(null));

        return tcs.Task;
    }

    public void ShowResult(string original, string applied, CorrectionResult result)
    {
        if (!_settings.ShowCorrectionPreview)
            return;

        RunOnUi(() =>
        {
            try
            {
                lock (_gate)
                {
                    CloseActivePopupLocked();
                    var popup = new CorrectionResultPopup(original, result, CorrectionPopupMode.Preview, applied, _settings);
                    _activePopup = popup;
                    WireClosed(popup);
                    popup.Show();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show correction result popup.");
            }
        });
    }

    void ICorrectionPreviewService.ShowCorrection(string original, string corrected, string? notes) =>
        ShowResult(original, corrected, new CorrectionResult
        {
            Confidence = ConfidenceLevel.High,
            Primary = corrected,
            Notes = notes
        });

    public void ShowNoChanges() => ShowStatus(StatusPopupKind.NoChanges, string.Empty, dismissible: true);

    void IPopupService.ShowInfo(string message) => ShowStatus(StatusPopupKind.Info, message, dismissible: true);

    void IPopupService.ShowError(string message) => ShowStatus(StatusPopupKind.Error, message, dismissible: true);

    void INotificationService.ShowInfo(string message, string? title) => ShowStatus(StatusPopupKind.Info, message, dismissible: true);

    void INotificationService.ShowError(string message, string? title) => ShowStatus(StatusPopupKind.Error, message, dismissible: true);

    internal void ReleaseWorkflow(WorkflowPopup popup)
    {
        RunOnUi(() =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activePopup, popup))
                    _activePopup = null;
            }
        });
    }

    private void ShowStatus(StatusPopupKind kind, string message, bool dismissible)
    {
        RunOnUi(() =>
        {
            try
            {
                lock (_gate)
                {
                    CloseActivePopupLocked();
                    var popup = new StatusToastPopup(kind, message, _settings.ToastAutoCloseSeconds, dismissible);
                    _activePopup = popup;
                    WireClosed(popup);
                    popup.Show();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to show status popup.");
            }
        });
    }

    private void HideProgress()
    {
        RunOnUi(() =>
        {
            lock (_gate)
            {
                if (_progressPopup is null)
                    return;

                if (ReferenceEquals(_activePopup, _progressPopup))
                {
                    try { _progressPopup.Close(); } catch { /* ignore */ }
                    _activePopup = null;
                }

                _progressPopup = null;
            }
        });
    }

    private void CloseActivePopupLocked()
    {
        if (_activePopup is null)
            return;

        try { _activePopup.Close(); } catch { /* ignore */ }
        _activePopup = null;
        _progressPopup = null;
    }

    private void WireClosed(Window popup)
    {
        popup.Closed += (_, _) =>
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activePopup, popup))
                    _activePopup = null;

                if (ReferenceEquals(_progressPopup, popup))
                    _progressPopup = null;
            }
        };
    }

    internal void RunOnUiThread(Action action) => RunOnUi(action);

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    private sealed class ProgressScope(PopupService service) : IDisposable
    {
        public void Dispose() => service.HideProgress();
    }

    private sealed class WorkflowSession : ICorrectionWorkflowPopup
    {
        private readonly PopupService _service;
        private readonly WorkflowPopup _popup;
        private bool _disposed;

        public WorkflowSession(PopupService service, WorkflowPopup popup)
        {
            _service = service;
            _popup = popup;
        }

        public void Report(CorrectionWorkflowStage stage, string? detail = null) =>
            SetStage(stage, detail);

        public void SetStage(CorrectionWorkflowStage stage, string? detail = null) =>
            _service.RunOnUiThread(() => _popup.SetStage(stage, detail));

        public Task<string?> PickVariantAsync(string original, CorrectionResult result, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

            _service.RunOnUiThread(() =>
            {
                try
                {
                    _popup.BeginVariantSelection(original, result, tcs);
                }
                catch
                {
                    tcs.TrySetResult(null);
                }
            });

            if (cancellationToken.CanBeCanceled)
                cancellationToken.Register(() => tcs.TrySetResult(null));

            return tcs.Task;
        }

        public void ShowResult(string original, string applied, CorrectionResult result) =>
            _service.RunOnUiThread(() => _popup.ShowResult(original, applied, result));

        public void ShowNoChanges(string original) =>
            _service.RunOnUiThread(() => _popup.ShowNoChanges(original));

        public void ShowError(string message) =>
            _service.RunOnUiThread(() => _popup.ShowError(message));

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _service.ReleaseWorkflow(_popup);
        }
    }
}
