using System;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace Cloak.Services.Transcription
{
    public sealed class AzureSpeechTranscriptionService : ITranscriptionService, IAsyncDisposable, IDisposable
    {
        public event EventHandler<string>? TranscriptReceived;
        public event EventHandler<string>? MicTranscriptReceived;
        public event EventHandler<string>? SystemTranscriptReceived;

        private readonly SpeechConfig _config;
        private readonly PushAudioInputStream _micStream;
        private readonly PushAudioInputStream _systemStream;
        private readonly AudioConfig _micAudioConfig;
        private readonly AudioConfig _systemAudioConfig;
        private SpeechRecognizer _micRecognizer;
        private SpeechRecognizer _systemRecognizer;
        private readonly object _sync = new object();
        private bool _disposed;

        public AzureSpeechTranscriptionService(string subscriptionKey, string region)
        {
            _config = SpeechConfig.FromSubscription(subscriptionKey, region);
            _config.SpeechRecognitionLanguage = "en-US";
            _config.SetProperty(PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs, "30000");
            _micStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
            _systemStream = AudioInputStream.CreatePushStream(AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1));
            _micAudioConfig = AudioConfig.FromStreamInput(_micStream);
            _systemAudioConfig = AudioConfig.FromStreamInput(_systemStream);
            _micRecognizer = CreateRecognizer(_micAudioConfig, isMic:true);
            _systemRecognizer = CreateRecognizer(_systemAudioConfig, isMic:false);
            _ = _micRecognizer.StartContinuousRecognitionAsync();
            _ = _systemRecognizer.StartContinuousRecognitionAsync();
        }

        public void PushMicAudio(ReadOnlyMemory<float> samples)
        {
            if (_disposed) return;
            
            try
            {
                var buffer = new byte[samples.Length * 2];
                int outIndex = 0;
                foreach (var s in samples.Span)
                {
                    var clamped = Math.Clamp(s, -1f, 1f);
                    short sample16 = (short)(clamped * 32767);
                    buffer[outIndex++] = (byte)(sample16 & 0xFF);
                    buffer[outIndex++] = (byte)((sample16 >> 8) & 0xFF);
                }
                _micStream?.Write(buffer);
            }
            catch (ObjectDisposedException)
            {
                // Stream was disposed, ignore silently
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    TranscriptReceived?.Invoke(this, $"[mic push error] {ex.Message}");
                }
            }
        }

        public void PushSystemAudio(ReadOnlyMemory<float> samples)
        {
            if (_disposed) return;
            
            try
            {
                var buffer = new byte[samples.Length * 2];
                int outIndex = 0;
                foreach (var s in samples.Span)
                {
                    var clamped = Math.Clamp(s, -1f, 1f);
                    short sample16 = (short)(clamped * 32767);
                    buffer[outIndex++] = (byte)(sample16 & 0xFF);
                    buffer[outIndex++] = (byte)((sample16 >> 8) & 0xFF);
                }
                _systemStream?.Write(buffer);
            }
            catch (ObjectDisposedException)
            {
                // Stream was disposed, ignore silently
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    TranscriptReceived?.Invoke(this, $"[system push error] {ex.Message}");
                }
            }
        }

        public void Flush() { }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            
            // Stop recognition first and wait for completion
            try 
            { 
                await _micRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); 
            } 
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Error stopping mic recognizer: {ex.Message}"); 
            }
            
            try 
            { 
                await _systemRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); 
            } 
            catch (Exception ex) 
            { 
                System.Diagnostics.Debug.WriteLine($"Error stopping system recognizer: {ex.Message}"); 
            }
            
            // Small delay to ensure recognition has fully stopped
            await Task.Delay(100).ConfigureAwait(false);
            
            // Now safely dispose resources
            try { _micRecognizer?.Dispose(); } catch { }
            try { _systemRecognizer?.Dispose(); } catch { }
            try { _micAudioConfig?.Dispose(); } catch { }
            try { _systemAudioConfig?.Dispose(); } catch { }
            try { _micStream?.Dispose(); } catch { }
            try { _systemStream?.Dispose(); } catch { }
        }

        public void Dispose()
        {
            // For synchronous disposal, we need to block on the async operation
            // This is not ideal but necessary for IDisposable compatibility
            try
            {
                DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in synchronous dispose: {ex.Message}");
                // Fallback: force dispose resources immediately
                try { _micRecognizer?.Dispose(); } catch { }
                try { _systemRecognizer?.Dispose(); } catch { }
                try { _micAudioConfig?.Dispose(); } catch { }
                try { _systemAudioConfig?.Dispose(); } catch { }
                try { _micStream?.Dispose(); } catch { }
                try { _systemStream?.Dispose(); } catch { }
            }
        }

        private SpeechRecognizer CreateRecognizer(AudioConfig audioConfig, bool isMic)
        {
            var rec = new SpeechRecognizer(_config, audioConfig);
            rec.Recognizing += (_, _) => { };
            rec.Recognized += (_, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedSpeech && !string.IsNullOrWhiteSpace(e.Result.Text))
                {
                    TranscriptReceived?.Invoke(this, e.Result.Text);
                    if (isMic) MicTranscriptReceived?.Invoke(this, e.Result.Text);
                    else SystemTranscriptReceived?.Invoke(this, e.Result.Text);
                }
            };
            rec.Canceled += async (_, e) =>
            {
                TranscriptReceived?.Invoke(this, $"[asr canceled] {e.Reason}: {e.ErrorDetails}");
                await TryRestartAsync();
            };
            rec.SessionStopped += async (_, _) =>
            {
                TranscriptReceived?.Invoke(this, "[asr stopped]");
                await TryRestartAsync();
            };
            return rec;
        }

        private async Task TryRestartAsync()
        {
            if (_disposed) return;
            
            try
            {
                // Stop recognition first
                try { await _micRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); } catch { }
                try { await _systemRecognizer.StopContinuousRecognitionAsync().ConfigureAwait(false); } catch { }
                
                // Wait a bit for recognition to fully stop
                await Task.Delay(100).ConfigureAwait(false);
                
                lock (_sync)
                {
                    if (_disposed) return;
                    
                    // Dispose old recognizers
                    try { _micRecognizer?.Dispose(); } catch { }
                    try { _systemRecognizer?.Dispose(); } catch { }
                    
                    // Create new recognizers
                    _micRecognizer = CreateRecognizer(_micAudioConfig, isMic: true);
                    _systemRecognizer = CreateRecognizer(_systemAudioConfig, isMic: false);
                }
                
                // Start recognition on new recognizers
                if (!_disposed)
                {
                    try { await _micRecognizer.StartContinuousRecognitionAsync().ConfigureAwait(false); } catch { }
                    try { await _systemRecognizer.StartContinuousRecognitionAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                TranscriptReceived?.Invoke(this, $"[restart error] {ex.Message}");
            }
        }
    }
}


