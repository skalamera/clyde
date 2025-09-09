# Cloak (Windows Meeting Copilot)

This is a minimal WPF scaffold for a Windows meeting copilot. It currently uses placeholder services for audio, transcription, and assistant logic so you can run the app without external dependencies.

## Prerequisites
- .NET SDK 8
- Windows 10/11

## Build
```
dotnet build
```

## Run
```
dotnet run --project Cloak.App
```

Use Start/Stop to simulate streaming. Mock transcripts and suggestions will appear.

## Next steps
- Implement WASAPI mic capture (NAudio) in `WasapiAudioCaptureService`
- Integrate Azure Speech SDK in `PlaceholderTranscriptionService`
- Add LLM + RAG logic to `PlaceholderAssistantService`
- Add settings UI for API keys and hotkeys

## Structure
- `Cloak.App` – WPF UI
- `Cloak.Services` – audio, transcription, assistant services

