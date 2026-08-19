using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Interfaces;

public interface IPromptProvider
{
    string BuildSystemPrompt(PromptMode mode);
    PromptMode ResolveMode(PromptMode requestedMode, string text);
}
