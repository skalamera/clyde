using System;
using System.Buffers;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Cloak.Services.Audio
{
    public sealed class WasapiMicCaptureService : IAudioCaptureService
    {
        private WasapiCapture? _capture;

        public Task StartAsync(Action<ReadOnlyMemory<float>> onSamples)
        {
            if (_capture != null) return Task.CompletedTask;

            var enumerator = new MMDeviceEnumerator();
            MMDevice device;
            try
            {
                // Prefer Communications endpoint during calls
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            }
            catch
            {
                device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            }
            _capture = new WasapiCapture(device);
            _capture.DataAvailable += (_, e) =>
            {
                try
                {
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
                        Buffer.BlockCopy(e.Buffer, 0, temp, 0, frameCount * srcChannels * 4);
                    }
                    else if (bytesPerSample == 2)
                    {
                        int ti = 0;
                        for (int i = 0; i < e.BytesRecorded; i += 2)
                        {
                            short s16 = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                            temp[ti++] = s16 / 32768f;
                        }
                    }
                    else return;

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

                        const int targetRate = 16000;
                        if (srcSampleRate == targetRate)
                        {
                            onSamples(new ReadOnlyMemory<float>(mono, 0, frameCount));
                            // Report RMS
                            float sum = 0f;
                            for (int i = 0; i < frameCount; i++) { var v = mono[i]; sum += v * v; }
                            var rms = (float)Math.Sqrt(sum / frameCount);
                            AudioLevels.ReportMic(rms * 2f);
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
                                AudioLevels.ReportMic(rms * 2f);
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
                    AudioLevels.ReportMic(0f);
                    System.Diagnostics.Debug.WriteLine($"WasapiMicCaptureService error: {ex}");
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


