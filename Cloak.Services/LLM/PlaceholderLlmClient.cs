using System.Threading.Tasks;

namespace Cloak.Services.LLM
{
    public sealed class PlaceholderLlmClient : ILlmClient
    {
        public Task<string> GetSuggestionAsync(string context)
        {
            return Task.FromResult($"[mock LLM] Based on: {context}");
        }
    }
}

