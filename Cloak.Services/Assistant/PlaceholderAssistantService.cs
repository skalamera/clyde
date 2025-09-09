using System;

namespace Cloak.Services.Assistant
{
    public sealed class PlaceholderAssistantService : IAssistantService
    {
        public event EventHandler<string>? SuggestionReceived;

        public void ProcessContext(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var suggestion = $"Suggestion: consider asking a clarifying question about '{text[..Math.Min(32, text.Length)]}'";
            SuggestionReceived?.Invoke(this, suggestion);
        }

        public void ForceSuggest()
        {
            SuggestionReceived?.Invoke(this, "[mock] Suggest now");
        }
    }
}

