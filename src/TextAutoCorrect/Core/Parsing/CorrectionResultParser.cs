using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Core.Parsing;

public static class CorrectionResultParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static CorrectionResult Parse(string content)
    {
        var json = ExtractJson(content);
        var dto = JsonSerializer.Deserialize<AiResponseDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse AI JSON response.");

        if (string.IsNullOrWhiteSpace(dto.Primary))
            throw new InvalidOperationException("AI response is missing primary correction.");

        var normalized = dto.Confidence?.Trim().ToLowerInvariant();
        var confidence = normalized switch
        {
            "high" or "высокая" or "high_confidence" => ConfidenceLevel.High,
            "medium" or "средняя" or "middle" => ConfidenceLevel.Medium,
            "low" or "низкая" => ConfidenceLevel.Low,
            _ => ParseNumericConfidence(normalized)
        };

        return new CorrectionResult
        {
            Confidence = confidence,
            Primary = dto.Primary.Trim(),
            Alternatives = dto.Alternatives?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray() ?? [],
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        };
    }

    private static string ExtractJson(string content)
    {
        var trimmed = content.Trim();
        var fenced = Regex.Match(trimmed, @"```(?:json)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (fenced.Success)
            trimmed = fenced.Groups[1].Value.Trim();

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }

    private static ConfidenceLevel ParseNumericConfidence(string? value)
    {
        if (!double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
            return ConfidenceLevel.Medium;

        if (number > 1)
            number /= 100d;

        return number switch
        {
            >= 0.8 => ConfidenceLevel.High,
            >= 0.5 => ConfidenceLevel.Medium,
            _ => ConfidenceLevel.Low
        };
    }

    private sealed class AiResponseDto
    {
        public string? Confidence { get; set; }
        public string? Primary { get; set; }
        public List<string>? Alternatives { get; set; }
        public string? Notes { get; set; }
    }
}
