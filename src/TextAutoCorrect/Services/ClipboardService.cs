using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using TextAutoCorrect.Core.Interfaces;

namespace TextAutoCorrect.Services;

internal sealed class ClipboardSnapshot : IClipboardSnapshot
{
    public string? Text { get; init; }
}

public sealed class ClipboardService : IClipboardService
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.Singleline);

    private readonly IClipboardMonitor _clipboardMonitor;

    public ClipboardService(IClipboardMonitor clipboardMonitor)
    {
        _clipboardMonitor = clipboardMonitor;
    }

    public Task<IClipboardSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default)
    {
        return RunOnUiAsync(() =>
        {
            return (IClipboardSnapshot?)new ClipboardSnapshot { Text = ReadClipboardText() };
        }, cancellationToken);
    }

    public Task RestoreSnapshotAsync(IClipboardSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot is not ClipboardSnapshot { Text: not null } concrete)
            return Task.CompletedTask;

        return RunOnUiAsync(() =>
        {
            using (_clipboardMonitor.SuppressNotifications())
                Clipboard.SetText(concrete.Text);

            return true;
        }, cancellationToken);
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        return RunOnUiAsync(ReadClipboardText, cancellationToken);
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        return RunOnUiAsync(() =>
        {
            using (_clipboardMonitor.SuppressNotifications())
                Clipboard.SetText(text);

            return true;
        }, cancellationToken);
    }

    private static string? ReadClipboardText()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText(System.Windows.TextDataFormat.UnicodeText);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            if (Clipboard.ContainsData(DataFormats.Html))
            {
                var html = Clipboard.GetData(DataFormats.Html) as string;
                var plain = ExtractPlainTextFromHtml(html);
                if (!string.IsNullOrWhiteSpace(plain))
                    return plain;
            }

            if (Clipboard.ContainsData(DataFormats.Text))
            {
                var text = Clipboard.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }
        catch
        {
            // Clipboard may be locked by another app.
        }

        return null;
    }

    private static string? ExtractPlainTextFromHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var startFragment = html.IndexOf("StartFragment:", StringComparison.OrdinalIgnoreCase);
        if (startFragment >= 0)
        {
            var fragmentStart = html.IndexOf(':', startFragment) + 1;
            var fragmentEnd = html.IndexOf("EndFragment:", StringComparison.OrdinalIgnoreCase);
            if (fragmentEnd > fragmentStart)
                html = html[fragmentStart..fragmentEnd];
        }

        html = html.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase);

        var text = HtmlTagRegex.Replace(html, string.Empty);
        text = System.Net.WebUtility.HtmlDecode(text);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static Task<T> RunOnUiAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF dispatcher is not available.");

        return dispatcher.InvokeAsync(action, DispatcherPriority.Send).Task;
    }
}
