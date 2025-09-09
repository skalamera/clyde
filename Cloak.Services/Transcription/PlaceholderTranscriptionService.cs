using System;

namespace Cloak.Services.Transcription
{
    public sealed class PlaceholderTranscriptionService : ITranscriptionService
    {
        public event EventHandler<string>? TranscriptReceived;
        public event EventHandler<string>? MicTranscriptReceived;
        public event EventHandler<string>? SystemTranscriptReceived;

        private int _counter;

        public void PushMicAudio(ReadOnlyMemory<float> samples)
        {
            _counter++;
            if (_counter % 10 == 0)
            {
                var msg = $"[mock mic] Heard {_counter * samples.Length} samples...";
                MicTranscriptReceived?.Invoke(this, msg);
                TranscriptReceived?.Invoke(this, msg);
            }
        }

        public void PushSystemAudio(ReadOnlyMemory<float> samples)
        {
            _counter++;
            if (_counter % 10 == 0)
            {
                var msg = $"[mock system] Heard {_counter * samples.Length} samples...";
                SystemTranscriptReceived?.Invoke(this, msg);
                TranscriptReceived?.Invoke(this, msg);
            }
        }

        public void Flush() {}
    }
}

