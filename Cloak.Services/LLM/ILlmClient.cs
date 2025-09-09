using System.Threading.Tasks;

namespace Cloak.Services.LLM
{
    public interface ILlmClient
    {
        Task<string> GetSuggestionAsync(string context);
    }
}

