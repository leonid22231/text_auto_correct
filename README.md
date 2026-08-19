# TextAutoCorrect

Windows tray application that corrects selected text in any application using [GigaChat API](https://developers.sber.ru/docs/ru/gigachat/guides/main). OpenAI-compatible providers remain available as a fallback.

## Features

- Global hotkey (default: **Ctrl+Alt+K**)
- Copy → AI correction → paste fallback for any text field
- Russian and English proofreading with strict meaning preservation
- Automatic apply on high confidence; variant picker on medium/low
- GigaChat OAuth token (30 minutes) with automatic refresh
- Configurable provider, authorization key, model, temperature, timeout, prompt mode
- Pluggable `IAiCorrectionProvider` for GigaChat, OpenAI, or a custom backend

## Quick start

1. Install [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. Credentials for GigaChat are stored in `%AppData%\TextAutoCorrect\appsettings.json` and can be edited from tray **Settings**
3. Build and run:

```powershell
dotnet build src/TextAutoCorrect/TextAutoCorrect.csproj
dotnet run --project src/TextAutoCorrect/TextAutoCorrect.csproj
```

4. Select text in any app and press **Ctrl+Alt+K**

GigaChat uses the [Russian Trusted Root CA](https://developers.sber.ru/docs/ru/gigachat/certificates). Until that certificate is installed, keep `Ai.IgnoreSslErrors` enabled.

## Configuration

User settings are stored in:

`%AppData%\TextAutoCorrect\appsettings.json`

| Section | Purpose |
|---------|---------|
| `Hotkey` | Modifiers and key |
| `Ai` | Provider, GigaChat OAuth, model, temperature, timeouts |
| `Clipboard` | Copy/paste timing and clipboard restore |
| `Ui` | Tray icon and dialog options |

GigaChat fields:

- `AuthorizationKey` — Base64 Client ID + Client Secret from Sber Studio
- `Scope` — `GIGACHAT_API_PERS` for individuals
- `AuthUrl` — `https://ngw.devices.sberbank.ru:9443/api/v2/oauth`
- `BaseUrl` — `https://api.giga.chat/v1`
- `Model` — `GigaChat-2` (доступны также `GigaChat-2-Pro`, `GigaChat-2-Max`, `GigaChat-3-Ultra`)

## Architecture

```
UI (Tray, dialogs)
    └── CorrectionOrchestrator
            ├── HotkeyService
            ├── TextReplacementService → ClipboardService + KeyboardSimulator
            └── IAiCorrectionProvider
                    ├── GigaChatCorrectionProvider (OAuth + chat/completions)
                    └── OpenAiCorrectionProvider
```

## Project layout

```
src/TextAutoCorrect/
├── Core/           Models, interfaces, configuration
├── Services/       Hotkey, clipboard, orchestration, prompts
├── Infrastructure/
│   ├── GigaChat/   OAuth token + GigaChat chat client
│   └── OpenAi/     OpenAI-compatible provider
├── Native/         Win32 P/Invoke
└── UI/             Tray, settings, variant selection
```
