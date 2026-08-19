using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Configuration;

public sealed class AppSettings
{
    public HotkeySettings Hotkey { get; set; } = new();
    public AiSettings Ai { get; set; } = new();
    public ClipboardSettings Clipboard { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
}

public sealed class HotkeySettings
{
    public bool Control { get; set; } = true;
    public bool Shift { get; set; } = false;
    public bool Alt { get; set; } = true;
    public bool Win { get; set; } = false;
    public string Key { get; set; } = "K";
}

public sealed class AiSettings
{
    public string Provider { get; set; } = "GigaChat";
    public string BaseUrl { get; set; } = "https://api.giga.chat/v1";
    public string AuthUrl { get; set; } = "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";
    public string ApiKey { get; set; } = "";
    public string AuthorizationKey { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Scope { get; set; } = "GIGACHAT_API_PERS";
    public string Model { get; set; } = "GigaChat-2";
    public double Temperature { get; set; } = 0.2;
    public int MaxTokens { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 30;
    public bool IgnoreSslErrors { get; set; } = true;
    public int ShortTextMaxLength { get; set; } = 200;
    public int LongTextMinLength { get; set; } = 800;
    public PromptMode DefaultPromptMode { get; set; } = PromptMode.Auto;
}

public sealed class ClipboardSettings
{
    public int CopyRetryCount { get; set; } = 12;
    public int CopyPollIntervalMs { get; set; } = 100;
    public int CopySettleDelayMs { get; set; } = 80;
    public int ForceCopyDelayMs { get; set; } = 200;
    public int ForegroundPrepareDelayMs { get; set; } = 120;
    public int KeyDelayMs { get; set; } = 30;
    public int RecentClipboardMaxAgeSeconds { get; set; } = 15;
    public int CorrectedTextCooldownSeconds { get; set; } = 60;
    public bool RestoreClipboardAfterPaste { get; set; } = true;
}

public sealed class UiSettings
{
    public bool ShowTrayIcon { get; set; } = true;
    public bool ShowNotesInDialog { get; set; } = true;
    public bool ShowCorrectionPreview { get; set; } = true;
    public bool AutoStartOnWindowsStartup { get; set; } = false;
    public int PreviewAutoCloseSeconds { get; set; } = 5;
    public int ToastAutoCloseSeconds { get; set; } = 3;
}
