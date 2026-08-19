namespace TextAutoCorrect.Core.Interfaces;

public interface ICorrectionHistoryService
{
    bool WasRecentlyCorrected(string text, TimeSpan maxAge);
    void MarkCorrected(string originalText);
}
