namespace TextAutoCorrect.Core.Models;

public enum CorrectionWorkflowStage
{
    SearchingText,
    NotFound,
    TryingCopy,
    TextFound,
    Correcting,
    SelectingVariant,
    Result,
    NoChanges,
    Error
}
