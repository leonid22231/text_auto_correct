namespace TextAutoCorrect.Core.Interfaces;

public interface IClipboardService
{
    Task<IClipboardSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default);
    Task RestoreSnapshotAsync(IClipboardSnapshot snapshot, CancellationToken cancellationToken = default);
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}

public interface IClipboardSnapshot
{
}
