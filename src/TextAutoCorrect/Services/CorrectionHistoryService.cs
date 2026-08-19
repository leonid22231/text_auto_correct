using TextAutoCorrect.Core.Interfaces;

namespace TextAutoCorrect.Services;

public sealed class CorrectionHistoryService : ICorrectionHistoryService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);

    public bool WasRecentlyCorrected(string text, TimeSpan maxAge)
    {
        var key = Normalize(text);
        if (key.Length == 0)
            return false;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var correctedAt))
                return false;

            if (DateTimeOffset.UtcNow - correctedAt > maxAge)
            {
                _entries.Remove(key);
                return false;
            }

            return true;
        }
    }

    public void MarkCorrected(string originalText)
    {
        var key = Normalize(originalText);
        if (key.Length == 0)
            return;

        lock (_gate)
        {
            _entries[key] = DateTimeOffset.UtcNow;
            TrimOldEntriesLocked();
        }
    }

    private void TrimOldEntriesLocked()
    {
        var threshold = DateTimeOffset.UtcNow.AddHours(-2);
        foreach (var pair in _entries.Where(x => x.Value < threshold).Select(x => x.Key).ToList())
            _entries.Remove(pair);
    }

    private static string Normalize(string text) =>
        text.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
