using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;
using TextAutoCorrect.Core.Parsing;

namespace TextAutoCorrect.Infrastructure.OpenAi;

public sealed class OpenAiCorrectionProvider : IAiCorrectionProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly HttpClient _httpClient;
    private readonly AiSettings _settings;
    private readonly IPromptProvider _promptProvider;
    private readonly ILogger<OpenAiCorrectionProvider> _logger;

    public OpenAiCorrectionProvider(
        HttpClient httpClient,
        IOptions<AppSettings> options,
        IPromptProvider promptProvider,
        ILogger<OpenAiCorrectionProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value.Ai;
        _promptProvider = promptProvider;
        _logger = logger;
    }

    public string ProviderName => _settings.Provider;

    public async Task<CorrectionResult> CorrectAsync(
        CorrectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(_settings.ApiKey) ? _settings.AuthorizationKey : _settings.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("AI API key is not configured.");

        var mode = _promptProvider.ResolveMode(request.Mode, request.Text);
        var systemPrompt = _promptProvider.BuildSystemPrompt(mode);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));

        var payload = new ChatCompletionRequest
        {
            Model = _settings.Model,
            Temperature = _settings.Temperature,
            MaxTokens = _settings.MaxTokens,
            ResponseFormat = new ResponseFormat { Type = "json_object" },
            Messages =
            [
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage
                {
                    Role = "user",
                    Content = $"Correct this text:\n\n{request.Text}"
                }
            ]
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
        httpRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, timeoutCts.Token);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"AI request timed out after {_settings.TimeoutSeconds} seconds.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException("Network error while contacting AI provider.", ex);
        }

        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("AI provider returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"AI provider error {(int)response.StatusCode}: {body}");
        }

        var completion = JsonSerializer.Deserialize<ChatCompletionResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("AI provider returned an empty response.");

        var content = completion.Choices.FirstOrDefault()?.Message.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("AI provider returned empty content.");

        return CorrectionResultParser.Parse(content);
    }

    private sealed class ChatCompletionRequest
    {
        public required string Model { get; set; }
        public required List<ChatMessage> Messages { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public ResponseFormat ResponseFormat { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        public required string Role { get; set; }
        public required string Content { get; set; }
    }

    private sealed class ResponseFormat
    {
        public string Type { get; set; } = "json_object";
    }

    private sealed class ChatCompletionResponse
    {
        public List<ChatChoice> Choices { get; set; } = [];
    }

    private sealed class ChatChoice
    {
        public ChatMessage Message { get; set; } = new() { Role = "assistant", Content = string.Empty };
    }
}
