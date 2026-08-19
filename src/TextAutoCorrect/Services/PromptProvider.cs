using Microsoft.Extensions.Options;
using TextAutoCorrect.Core.Configuration;
using TextAutoCorrect.Core.Interfaces;
using TextAutoCorrect.Core.Models;

namespace TextAutoCorrect.Services;

public sealed class PromptProvider : IPromptProvider
{
    private readonly AiSettings _settings;

    public PromptProvider(IOptions<AppSettings> options)
    {
        _settings = options.Value.Ai;
    }

    public PromptMode ResolveMode(PromptMode requestedMode, string text)
    {
        if (requestedMode != PromptMode.Auto)
            return requestedMode;

        if (text.Length >= _settings.LongTextMinLength)
            return PromptMode.LongText;

        if (text.Length <= _settings.ShortTextMaxLength)
            return PromptMode.ShortText;

        return _settings.DefaultPromptMode == PromptMode.Auto
            ? PromptMode.ShortText
            : _settings.DefaultPromptMode;
    }

    public string BuildSystemPrompt(PromptMode mode)
    {
        var modeHint = mode switch
        {
            PromptMode.ShortText => "Текст короткий. Минимальные правки, сохраняй тон.",
            PromptMode.LongText => "Текст длинный. Сохраняй структуру абзацев и форматирование.",
            PromptMode.BusinessStyle => "Деловой стиль, смысл исходного текста не меняй.",
            PromptMode.ConversationalStyle => "Разговорный тон, исправляй только явные ошибки.",
            _ => "Консервативные правки без изменения смысла."
        };

        return $$"""
            Ты строгий корректор русского и английского текста.

            Правила:
            1. Сохраняй исходный смысл точно.
            2. Исправляй только орфографию, пунктуацию, грамматику и очевидные стилистические ошибки.
            3. Не добавляй пояснения внутрь исправленного текста.
            4. Не переписывай текст, если это не нужно для исправления ошибки.
            5. Если правка неоднозначна, снижай confidence и давай alternatives.
            6. Сохраняй язык исходного текста.
            7. Отвечай ТОЛЬКО валидным JSON по схеме:
            {
              "confidence": "high|medium|low",
              "primary": "исправленный текст",
              "alternatives": ["вариант 1", "вариант 2"],
              "notes": "краткое объяснение только если нужно"
            }
            Поле confidence должно быть строго одним из: high, medium, low.

            Confidence:
            - high: однозначно лучший вариант
            - medium: есть правдоподобные альтернативы
            - low: неопределённость, нужны несколько вариантов

            Режим: {{modeHint}}
            """;
    }
}
