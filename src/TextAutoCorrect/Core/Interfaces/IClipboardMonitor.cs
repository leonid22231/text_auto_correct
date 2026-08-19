namespace TextAutoCorrect.Core.Interfaces;

public interface IClipboardMonitor : IDisposable
{
    void Start();
    string? GetRecentText(TimeSpan maxAge);
    IDisposable SuppressNotifications();
}
