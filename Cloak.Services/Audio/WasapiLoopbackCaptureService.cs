using System;
using System.Buffers;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Cloak.Services.Audio
{
    public sealed class WasapiLoopbackCaptureService : IAudioCaptureService
    {
        private WasapiLoopbackCapture? _capture;

        public Task StartAsync(Action<ReadOnlyMemory<float>> onSamples)
        {
            if (_capture != null) return Task.CompletedTask;

            var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _capture = new WasapiLoopbackCapture(device);
            _capture.DataAvailable += (_, e) =>
            {
                try
                {
                    // Convert to float[] mono at 16kHz
                    var srcFormat = _capture.WaveFormat;
                var srcSampleRate = srcFormat.SampleRate;
                var srcChannels = srcFormat.Channels;
                var bytesPerSample = srcFormat.BitsPerSample / 8;

                int frameCount = e.BytesRecorded / (bytesPerSample * srcChannels);
                if (frameCount <= 0) return;

                var temp = ArrayPool<float>.Shared.Rent(frameCount * srcChannels);
                try
                {
                    if (bytesPerSample == 4)
                    {
                        // 32-bit float
                        Buffer.BlockCopy(e.Buffer, 0, temp, 0, frameCount * srcChannels * 4);
                    }
                    else if (bytesPerSample == 2)
                    {
                        // 16-bit PCM
                        int ti = 0;
                        for (int i = 0; i < e.BytesRecorded; i += 2)
                        {
                            short s16 = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                            temp[ti++] = s16 / 32768f;
                        }
                    }
                    else return;

                    // Downmix to mono
                    var mono = ArrayPool<float>.Shared.Rent(frameCount);
                    try
                    {
                        if (srcChannels == 1)
                        {
                            Array.Copy(temp, mono, frameCount);
                        }
                        else
                        {
                            int idx = 0;
                            for (int f = 0; f < frameCount; f++)
                            {
                                float sum = 0f;
                                for (int c = 0; c < srcChannels; c++) sum += temp[idx++];
                                mono[f] = sum / srcChannels;
                            }
                        }

                        // Resample to 16 kHz using linear interpolation
                        const int targetRate = 16000;
                        if (srcSampleRate == targetRate)
                        {
                            onSamples(new ReadOnlyMemory<float>(mono, 0, frameCount));
                            float sum = 0f;
                            for (int i = 0; i < frameCount; i++) { var v = mono[i]; sum += v * v; }
                            var rms = (float)Math.Sqrt(sum / frameCount);
                            AudioLevels.ReportSystem(rms * 2f);
                        }
                        else
                        {
                            int outCount = (int)Math.Round((double)frameCount * targetRate / srcSampleRate);
                            var resampled = ArrayPool<float>.Shared.Rent(outCount);
                            try
                            {
                                double ratio = (double)(frameCount - 1) / (outCount - 1);
                                for (int i = 0; i < outCount; i++)
                                {
                                    double srcPos = i * ratio;
                                    int i0 = (int)srcPos;
                                    int i1 = Math.Min(i0 + 1, frameCount - 1);
                                    double frac = srcPos - i0;
                                    resampled[i] = (float)((1 - frac) * mono[i0] + frac * mono[i1]);
                                }
                                onSamples(new ReadOnlyMemory<float>(resampled, 0, outCount));
                                float sum = 0f;
                                for (int i = 0; i < outCount; i++) { var v = resampled[i]; sum += v * v; }
                                var rms = (float)Math.Sqrt(sum / outCount);
                                AudioLevels.ReportSystem(rms * 2f);
                            }
                            finally
                            {
                                ArrayPool<float>.Shared.Return(resampled);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<float>.Shared.Return(mono);
                    }
                }
                finally
                {
                    ArrayPool<float>.Shared.Return(temp);
                }
                }
                catch (Exception ex)
                {
                    AudioLevels.ReportSystem(0f);
                    System.Diagnostics.Debug.WriteLine($"WasapiLoopbackCaptureService error: {ex}");
                }
            };

            _capture.StartRecording();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            if (_capture == null) return Task.CompletedTask;
            try { _capture.StopRecording(); }
            finally { _capture.Dispose(); _capture = null; }
            return Task.CompletedTask;
        }
    }
}


