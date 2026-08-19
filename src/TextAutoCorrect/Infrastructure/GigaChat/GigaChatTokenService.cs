using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;

namespace TextAutoCorrect.Infrastructure.GigaChat;

public sealed class GigaChatTokenService
{
    public const string HttpClientName = "GigaChat";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AppSettings> _options;
    private readonly ILogger<GigaChatTokenService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _accessToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;
    private string? _cachedCredentials;

    public GigaChatTokenService(
        IHttpClientFactory httpClientFactory,
        IOptions<AppSettings> options,
        ILogger<GigaChatTokenService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var settings = _options.Value.Ai;
        var credentials = settings.AuthorizationKey;
        if (string.IsNullOrWhiteSpace(credentials))
            throw new InvalidOperationException("GigaChat authorization key is not configured.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(_cachedCredentials, credentials, StringComparison.Ordinal))
            {
                _accessToken = null;
                _expiresAt = DateTimeOffset.MinValue;
                _cachedCredentials = credentials;
            }

            if (!string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow < _expiresAt)
                return _accessToken;

            _accessToken = await RequestTokenAsync(settings, cancellationToken);
            return _accessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        _accessToken = null;
        _expiresAt = DateTimeOffset.MinValue;
    }

    private async Task<string> RequestTokenAsync(AiSettings settings, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.AuthUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", settings.AuthorizationKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("RqUID", Guid.NewGuid().ToString());
        if (!string.IsNullOrWhiteSpace(settings.ClientId))
            request.Headers.TryAddWithoutValidation("X-Client-ID", settings.ClientId);

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["scope"] = settings.Scope
        });

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while requesting a GigaChat access token.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                "Network or TLS error while requesting a GigaChat token. Install the Russian Trusted Root CA or enable IgnoreSslErrors.",
                ex);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("GigaChat OAuth returned {StatusCode}: {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"GigaChat OAuth error {(int)response.StatusCode}: {body}");
        }

        var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("GigaChat OAuth returned an empty token.");

        if (string.IsNullOrWhiteSpace(token.AccessToken))
            throw new InvalidOperationException("GigaChat OAuth response is missing access_token.");

        _expiresAt = ParseExpiry(token.ExpiresAt).Subtract(TimeSpan.FromMinutes(1));
        _logger.LogInformation("GigaChat access token refreshed, expires at {ExpiresAt:u}.", _expiresAt);
        return token.AccessToken;
    }

    private static DateTimeOffset ParseExpiry(long expiresAt)
    {
        if (expiresAt <= 0)
            return DateTimeOffset.UtcNow.AddMinutes(25);

        return expiresAt > 1_000_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(expiresAt)
            : DateTimeOffset.FromUnixTimeSeconds(expiresAt);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_at")]
        public long ExpiresAt { get; set; }
    }
}
