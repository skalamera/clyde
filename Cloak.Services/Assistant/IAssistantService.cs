using System;

namespace Cloak.Services.Assistant
{
    public interface IAssistantService
    {
        event EventHandler<string>? SuggestionReceived;
        void ProcessContext(string text);
        void ForceSuggest();
    }
}

