using System;
using System.Buffers;
using System.Threading.Tasks;

namespace Cloak.Services.Audio
{
    public sealed class DualAudioCaptureService : IAudioCaptureService
    {
        private readonly IAudioCaptureService _mic;
        private readonly IAudioCaptureService _loopback;

        public DualAudioCaptureService(IAudioCaptureService mic, IAudioCaptureService loopback)
        {
            _mic = mic;
            _loopback = loopback;
        }

        public async Task StartAsync(Action<ReadOnlyMemory<float>> onSamples)
        {
            void OnMic(ReadOnlyMemory<float> s) => onSamples(s);
            void OnLoop(ReadOnlyMemory<float> s) => onSamples(s);

            await _mic.StartAsync(OnMic);
            await _loopback.StartAsync(OnLoop);
        }

        public async Task StopAsync()
        {
            await _mic.StopAsync();
            await _loopback.StopAsync();
        }
    }
}


