using System;

namespace Cloak.Services.Transcription
{
    public interface ITranscriptionService
    {
        event EventHandler<string>? TranscriptReceived;
        event EventHandler<string>? MicTranscriptReceived;
        event EventHandler<string>? SystemTranscriptReceived;
        void PushMicAudio(ReadOnlyMemory<float> samples);
        void PushSystemAudio(ReadOnlyMemory<float> samples);
        void Flush();
    }
}

