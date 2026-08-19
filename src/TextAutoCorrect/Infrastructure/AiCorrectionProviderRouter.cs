using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;
using TextAutoCorrect.Infrastructure.GigaChat;
using TextAutoCorrect.Infrastructure.OpenAi;

namespace TextAutoCorrect.Infrastructure;

public sealed class AiCorrectionProviderRouter : IAiCorrectionProvider
{
    private readonly IOptions<AppSettings> _options;
    private readonly GigaChatCorrectionProvider _gigaChat;
    private readonly OpenAiCorrectionProvider _openAi;

    public AiCorrectionProviderRouter(
        IOptions<AppSettings> options,
        GigaChatCorrectionProvider gigaChat,
        OpenAiCorrectionProvider openAi)
    {
        _options = options;
        _gigaChat = gigaChat;
        _openAi = openAi;
    }

    public string ProviderName => Resolve().ProviderName;

    public Task<CorrectionResult> CorrectAsync(
        CorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        return Resolve().CorrectAsync(request, cancellationToken);
    }

    private IAiCorrectionProvider Resolve()
    {
        return _options.Value.Ai.Provider.Equals("GigaChat", StringComparison.OrdinalIgnoreCase)
            ? _gigaChat
            : _openAi;
    }
}
