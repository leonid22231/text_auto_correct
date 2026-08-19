using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;
using TextAutoCorrect.Core.Parsing;

namespace TextAutoCorrect.Infrastructure.GigaChat;

public sealed class GigaChatCorrectionProvider : IAiCorrectionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GigaChatTokenService _tokenService;
    private readonly IOptions<AppSettings> _options;
    private readonly IPromptProvider _promptProvider;
    private readonly ILogger<GigaChatCorrectionProvider> _logger;

    public GigaChatCorrectionProvider(
        IHttpClientFactory httpClientFactory,
        GigaChatTokenService tokenService,
        IOptions<AppSettings> options,
        IPromptProvider promptProvider,
        ILogger<GigaChatCorrectionProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;
        _options = options;
        _promptProvider = promptProvider;
        _logger = logger;
    }

    public string ProviderName => "GigaChat";

    public async Task<CorrectionResult> CorrectAsync(
        CorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = _options.Value.Ai;
        var mode = _promptProvider.ResolveMode(request.Mode, request.Text);
        var systemPrompt = _promptProvider.BuildSystemPrompt(mode);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        var content = await SendChatAsync(settings, systemPrompt, request.Text, includeJsonSchema: true, timeoutCts.Token);
        return CorrectionResultParser.Parse(content);
    }

    private async Task<string> SendChatAsync(
        AiSettings settings,
        string systemPrompt,
        string text,
        bool includeJsonSchema,
        CancellationToken cancellationToken)
    {
        var accessToken = await _tokenService.GetAccessTokenAsync(cancellationToken);
        var client = _httpClientFactory.CreateClient(GigaChatTokenService.HttpClientName);
        var url = $"{settings.BaseUrl.TrimEnd('/')}/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Request-ID", Guid.NewGuid().ToString());
        if (!string.IsNullOrWhiteSpace(settings.ClientId))
            request.Headers.TryAddWithoutValidation("X-Client-ID", settings.ClientId);

        var payload = CreatePayload(settings, systemPrompt, text, includeJsonSchema);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"GigaChat request timed out after {settings.TimeoutSeconds} seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Network or TLS error while contacting GigaChat.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized)
        {
            _tokenService.Invalidate();
            throw new InvalidOperationException("GigaChat access token was rejected. Try again.");
        }

        if (includeJsonSchema &&
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            _logger.LogWarning("GigaChat rejected json_schema, retrying without it: {Body}", body);
            return await SendChatAsync(settings, systemPrompt, text, includeJsonSchema: false, cancellationToken);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GigaChat returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"GigaChat error {(int)response.StatusCode}: {body}");
        }

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("GigaChat returned an empty response.");

        var content = completion.Choices.FirstOrDefault()?.Message.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("GigaChat returned empty content.");

        return content;
    }

    private static Dictionary<string, object?> CreatePayload(
        AiSettings settings,
        string systemPrompt,
        string text,
        bool includeJsonSchema)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.Model,
            ["temperature"] = Math.Max(settings.Temperature, 0.001),
            ["max_tokens"] = settings.MaxTokens,
            ["stream"] = false,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = "Исправь этот текст и верни только JSON по заданной схеме:\n\n" + text }
            }
        };

        if (includeJsonSchema)
        {
            payload["response_format"] = new
            {
                type = "json_schema",
                schema = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["confidence"] = new
                        {
                            type = "string",
                            description = "Only one of: high, medium, low"
                        },
                        ["primary"] = new { type = "string", description = "Best corrected text" },
                        ["alternatives"] = new { type = "array", items = new { type = "string" } },
                        ["notes"] = new { type = "string" }
                    },
                    required = new[] { "confidence", "primary", "alternatives" }
                },
                strict = true
            };
        }

        return payload;
    }

    private sealed class ChatCompletionResponse
    {
        public List<ChatChoice> Choices { get; set; } = [];
    }

    private sealed class ChatChoice
    {
        public ChatMessage Message { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        public string Role { get; set; } = "assistant";
        public string Content { get; set; } = string.Empty;
    }
}
