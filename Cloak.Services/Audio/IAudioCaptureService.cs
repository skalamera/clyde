using System;
using System.Threading.Tasks;

namespace Cloak.Services.Audio
{
    public interface IAudioCaptureService
    {
        Task StartAsync(Action<ReadOnlyMemory<float>> onSamples);
        Task StopAsync();
    }
}

