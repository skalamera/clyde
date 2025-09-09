using System;
using System.Threading;
using System.Threading.Tasks;
using Cloak.Services.LLM;

namespace Cloak.Services.Assistant
{
    public sealed class LlmAssistantService : IAssistantService
    {
        public event EventHandler<string>? SuggestionReceived;
        private readonly ILlmClient _llmClient;

        private string _buffer = string.Empty;
        private int _charsSinceLast = 0;
        private DateTime _lastSuggestionAt = DateTime.MinValue;
        private string _lastSuggestion = string.Empty;
        private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(7);

        public LlmAssistantService(ILlmClient llmClient)
        {
            _llmClient = llmClient;
        }

        public async void ProcessContext(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _buffer += text + "\n";
            _charsSinceLast += text.Length;
            if (_charsSinceLast < 120) return;
            if (DateTime.UtcNow - _lastSuggestionAt < _minInterval) return;
            _charsSinceLast = 0;
            var snapshot = _buffer.Length > 2000 ? _buffer[^2000..] : _buffer;
            var suggestion = (await _llmClient.GetSuggestionAsync(snapshot)).Trim();
            if (string.IsNullOrWhiteSpace(suggestion)) return;
            if (string.Equals(suggestion, _lastSuggestion, StringComparison.OrdinalIgnoreCase)) return;
            _lastSuggestion = suggestion;
            _lastSuggestionAt = DateTime.UtcNow;
            SuggestionReceived?.Invoke(this, suggestion);
        }

        public async void ForceSuggest()
        {
            var snapshot = _buffer.Length > 2000 ? _buffer[^2000..] : _buffer;
            var suggestion = (await _llmClient.GetSuggestionAsync(snapshot)).Trim();
            if (string.IsNullOrWhiteSpace(suggestion)) return;
            if (string.Equals(suggestion, _lastSuggestion, StringComparison.OrdinalIgnoreCase)) return;
            _lastSuggestion = suggestion;
            _lastSuggestionAt = DateTime.UtcNow;
            SuggestionReceived?.Invoke(this, suggestion);
        }
    }
}


