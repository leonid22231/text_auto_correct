using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Infrastructure;
using TextAutoCorrect.Infrastructure.GigaChat;
using TextAutoCorrect.Infrastructure.Logging;
using TextAutoCorrect.Infrastructure.OpenAi;
using TextAutoCorrect.Native;
using TextAutoCorrect.Services;
using TextAutoCorrect.UI;

namespace TextAutoCorrect;

public partial class App : System.Windows.Application
{
    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private TrayApplicationHost? _host;
    private FileLoggerProvider? _fileLoggerProvider;
    private System.Threading.Mutex? _singleInstanceMutex;
    // "Global\" требует дополнительных прав (SeCreateGlobalPrivilege) и может блокировать запуск.
    // Для обычного пользовательского запуска используем "без префикса".
    private const string SingleInstanceMutexName = @"TextAutoCorrect_SingleInstance";

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Prevent multiple instances (clones).
        var createdNew = false;
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, SingleInstanceMutexName, out createdNew);
        }
        catch (Exception ex)
        {
            // Если mutex недоступен из-за прав/политик — не блокируем запуск всего приложения.
            MessageBox.Show(
                "Не удалось включить защиту от повторных запусков.\n" +
                ex.Message,
                "Text Auto Correct",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            createdNew = true;
        }
        if (!createdNew)
        {
            MessageBox.Show(
                "Приложение уже запущено.",
                "Text Auto Correct",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextAutoCorrect");
        var settingsPath = Path.Combine(appDataDir, "appsettings.json");
        var logDirectory = Path.Combine(appDataDir, "logs");

        EnsureUserSettings(settingsPath);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile(settingsPath, optional: true, reloadOnChange: true)
            .Build();

        _fileLoggerProvider = new FileLoggerProvider(logDirectory);

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.AddProvider(_fileLoggerProvider);
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        services.Configure<AppSettings>(configuration);
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);
        services.AddSingleton<KeyboardSimulator>();
        services.AddSingleton<ForegroundWindowHolder>();
        services.AddSingleton<IClipboardMonitor, ClipboardMonitor>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<ICorrectionHistoryService, CorrectionHistoryService>();
        services.AddSingleton<ITextReplacementService, TextReplacementService>();
        services.AddSingleton<IPromptProvider, PromptProvider>();
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<PopupService>();
        services.AddSingleton<IPopupService>(sp => sp.GetRequiredService<PopupService>());
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<PopupService>());
        services.AddSingleton<ICorrectionPreviewService>(sp => sp.GetRequiredService<PopupService>());
        services.AddSingleton<ICorrectionOrchestrator, CorrectionOrchestrator>();
        services.AddSingleton<GigaChatTokenService>();
        services.AddSingleton<GigaChatCorrectionProvider>();
        services.AddSingleton<IAiCorrectionProvider, AiCorrectionProviderRouter>();

        services.AddHttpClient(GigaChatTokenService.HttpClientName, (sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value.Ai;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds + 5, 30));
        }).ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var handler = new HttpClientHandler();
            if (sp.GetRequiredService<IOptions<AppSettings>>().Value.Ai.IgnoreSslErrors)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        });

        services.AddHttpClient<OpenAiCorrectionProvider>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value.Ai;
            client.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(Math.Max(settings.TimeoutSeconds + 5, 30));
        });

        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<ILogger<App>>();
        GlobalExceptionLogger.Attach(this, logger);
        logger.LogInformation("TextAutoCorrect starting. Logs: {LogDirectory}", logDirectory);

        var appSettings = Services.GetRequiredService<AppSettings>();

        // Auto-start on Windows login.
        TextAutoCorrect.Infrastructure.AutoStart.WindowsAutoStartManager.SetEnabled(
            appSettings.Ui.AutoStartOnWindowsStartup);

        _host = new TrayApplicationHost(Services, appSettings, settingsPath);
        _host.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Services?.GetService<ILogger<App>>()?.LogInformation("TextAutoCorrect exiting.");
        _host?.Dispose();
        if (Services is IDisposable disposable)
            disposable.Dispose();

        _fileLoggerProvider?.Dispose();
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }

    private static void EnsureUserSettings(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);

        if (!File.Exists(settingsPath))
        {
            var created = new AppSettings();
            ApplyGigaChatDefaults(created);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(created, SettingsJson));
            return;
        }

        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath), SettingsJson)
                       ?? new AppSettings();

        var changed = false;
        var isLegacyOpenAi = settings.Ai.Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase) &&
                             string.IsNullOrWhiteSpace(settings.Ai.ApiKey);

        if (isLegacyOpenAi ||
            (settings.Ai.Provider.Equals("GigaChat", StringComparison.OrdinalIgnoreCase) &&
             string.IsNullOrWhiteSpace(settings.Ai.AuthorizationKey)))
        {
            ApplyGigaChatDefaults(settings);
            changed = true;
        }

        if (settings.Ai.Model.Equals("GigaChat", StringComparison.OrdinalIgnoreCase))
        {
            settings.Ai.Model = "GigaChat-2";
            changed = true;
        }

        if (IsLegacyFunctionHotkey(settings.Hotkey))
        {
            settings.Hotkey.Control = true;
            settings.Hotkey.Shift = false;
            settings.Hotkey.Alt = true;
            settings.Hotkey.Win = false;
            settings.Hotkey.Key = "K";
            changed = true;
        }

        if (changed)
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, SettingsJson));
    }

    private static bool IsLegacyFunctionHotkey(HotkeySettings hotkey)
    {
        var key = hotkey.Key.Trim();
        return key.Length >= 2 &&
               key.StartsWith('F') &&
               key[1..].All(char.IsDigit);
    }

    private static void ApplyGigaChatDefaults(AppSettings settings)
    {
        settings.Ai.Provider = "GigaChat";
        settings.Ai.BaseUrl = string.IsNullOrWhiteSpace(settings.Ai.BaseUrl) || settings.Ai.BaseUrl.Contains("openai", StringComparison.OrdinalIgnoreCase)
            ? "https://api.giga.chat/v1"
            : settings.Ai.BaseUrl;
        settings.Ai.AuthUrl = string.IsNullOrWhiteSpace(settings.Ai.AuthUrl)
            ? "https://ngw.devices.sberbank.ru:9443/api/v2/oauth"
            : settings.Ai.AuthUrl;
        settings.Ai.Scope = string.IsNullOrWhiteSpace(settings.Ai.Scope) ? "GIGACHAT_API_PERS" : settings.Ai.Scope;
        settings.Ai.Model = string.IsNullOrWhiteSpace(settings.Ai.Model) || settings.Ai.Model.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) || settings.Ai.Model.Equals("GigaChat", StringComparison.OrdinalIgnoreCase)
            ? "GigaChat-2"
            : settings.Ai.Model;
        settings.Ai.ClientId ??= string.Empty;
        settings.Ai.AuthorizationKey ??= string.Empty;
        settings.Ai.IgnoreSslErrors = true;
    }
}
