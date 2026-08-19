using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Native;

namespace TextAutoCorrect.UI;

public sealed class TrayApplicationHost : IDisposable
{
    private readonly IServiceProvider _services;
    private readonly AppSettings _settings;
    private readonly string _settingsPath;
    private readonly ILogger<TrayApplicationHost> _logger;
    private NotifyIcon? _notifyIcon;
    private IHotkeyService? _hotkeyService;
    private ICorrectionOrchestrator? _orchestrator;

    private readonly ForegroundWindowHolder _foregroundWindowHolder;
    private IClipboardMonitor? _clipboardMonitor;

    public TrayApplicationHost(IServiceProvider services, AppSettings settings, string settingsPath)
    {
        _services = services;
        _settings = settings;
        _settingsPath = settingsPath;
        _logger = services.GetRequiredService<ILogger<TrayApplicationHost>>();
        _foregroundWindowHolder = services.GetRequiredService<ForegroundWindowHolder>();
    }

    public void Start()
    {
        _logger.LogInformation("Starting tray host.");

        _clipboardMonitor = _services.GetRequiredService<IClipboardMonitor>();
        _clipboardMonitor.Start();
        _logger.LogDebug("Clipboard monitor started.");

        _hotkeyService = _services.GetRequiredService<IHotkeyService>();
        _orchestrator = _services.GetRequiredService<ICorrectionOrchestrator>();
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        if (!_hotkeyService.Register())
        {
            _logger.LogError("Hotkey registration failed at startup.");
            var popup = _services.GetRequiredService<IPopupService>();
            popup.ShowError("Не удалось зарегистрировать hotkey. Откройте настройки.");
        }

        if (_settings.Ui.ShowTrayIcon)
        {
            CreateTrayIcon();
            ShowStartupNotification();
        }
    }

    private void CreateTrayIcon()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "Text Auto Correct",
            Visible = true,
            Icon = System.Drawing.SystemIcons.Application
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, (_, _) => OpenSettings());
        menu.Items.Add("Open logs folder", null, (_, _) => OpenLogsFolder());
        menu.Items.Add("Exit", null, (_, _) => Application.Current.Shutdown());
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();
        _logger.LogDebug("Tray icon created.");
    }

    private void ShowStartupNotification()
    {
        if (_notifyIcon is null)
            return;

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        var versionText = version is null ? "1.0" : $"{version.Major}.{version.Minor}";
        var model = string.Equals(_settings.Ai.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? "OpenAI"
            : "GigaChat";
        var autoStart = _settings.Ui.AutoStartOnWindowsStartup ? "вкл" : "выкл";
        var command = FormatHotkey(_settings.Hotkey);

        var message =
            $"Приложение запущено: Версия {versionText}{Environment.NewLine}" +
            $"Автозапуск: {autoStart}{Environment.NewLine}" +
            $"Модель: {model}{Environment.NewLine}" +
            $"Команда для работы: {command}";

        _notifyIcon.BalloonTipTitle = "Text Auto Correct";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(6000);
        _logger.LogInformation("Startup notification shown: {Message}", message.Replace(Environment.NewLine, " | "));
    }

    private static string FormatHotkey(HotkeySettings hotkey)
    {
        var parts = new List<string>();
        if (hotkey.Control) parts.Add("Ctrl");
        if (hotkey.Alt) parts.Add("Alt");
        if (hotkey.Shift) parts.Add("Shift");
        if (hotkey.Win) parts.Add("Win");
        if (!string.IsNullOrWhiteSpace(hotkey.Key)) parts.Add(hotkey.Key.ToUpperInvariant());
        return string.Join(" + ", parts);
    }

    private void OpenLogsFolder()
    {
        var logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextAutoCorrect",
            "logs");
        Directory.CreateDirectory(logsDir);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logsDir) { UseShellExecute = true });
    }

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_orchestrator is null)
            return;

        _logger.LogInformation("Hotkey workflow started.");
        _foregroundWindowHolder.Capture();
        try
        {
            await _orchestrator.ProcessSelectedTextAsync();
            _logger.LogInformation("Hotkey workflow finished.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hotkey workflow failed.");
            var popup = _services.GetRequiredService<IPopupService>();
            popup.ShowError(ex.Message);
        }
        finally
        {
            _foregroundWindowHolder.Clear();
        }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_settings, _settingsPath);
        window.ShowDialog();
    }

    public void Dispose()
    {
        if (_hotkeyService is not null)
            _hotkeyService.HotkeyPressed -= OnHotkeyPressed;

        _hotkeyService?.Dispose();
        _clipboardMonitor?.Dispose();
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _logger.LogDebug("Tray host disposed.");
    }
}
