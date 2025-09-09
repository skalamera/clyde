using System;
using System.Buffers;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Cloak.Services.Audio
{
    public sealed class WasapiAudioCaptureService : IAudioCaptureService
    {
        private WaveInEvent? _waveIn;
        private readonly int _sampleRate = 16000;
        private readonly int _channels = 1;

        public Task StartAsync(Action<ReadOnlyMemory<float>> onSamples)
        {
            if (_waveIn != null) return Task.CompletedTask;

            _waveIn = new WaveInEvent
            {
                BufferMilliseconds = 50,
                NumberOfBuffers = 3,
                WaveFormat = new WaveFormat(_sampleRate, 16, _channels)
            };

            _waveIn.DataAvailable += (_, args) =>
            {
                int sampleCount = args.BytesRecorded / 2;
                var floatBuffer = ArrayPool<float>.Shared.Rent(sampleCount);
                try
                {
                    int outIndex = 0;
                    for (int i = 0; i < args.BytesRecorded; i += 2)
                    {
                        short sample16 = (short)(args.Buffer[i] | (args.Buffer[i + 1] << 8));
                        floatBuffer[outIndex++] = sample16 / 32768f;
                    }
                    onSamples(new ReadOnlyMemory<float>(floatBuffer, 0, sampleCount));
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(floatBuffer);
                }
            };

            _waveIn.StartRecording();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (_waveIn == null) return Task.CompletedTask;
            try
            {
                _waveIn.StopRecording();
            }
            finally
            {
                _waveIn.Dispose();
                _waveIn = null;
            }
            return Task.CompletedTask;
        }
    }
}

