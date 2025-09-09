using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cloak.Services.LLM
{
    public sealed class AzureOpenAiLlmClient : ILlmClient
    {
        private readonly string _endpoint;
        private readonly string _deployment;
        private readonly string _apiKey;
        private readonly HttpClient _http = new HttpClient();
        private readonly string _apiVersion;

        public AzureOpenAiLlmClient(string endpoint, string deployment, string apiKey, string apiVersion = "2024-06-01")
        {
            _endpoint = endpoint.TrimEnd('/');
            _deployment = deployment;
            _apiKey = apiKey;
            _apiVersion = apiVersion;
        }

        public async Task<string> GetSuggestionAsync(string context)
        {
            var url = $"{_endpoint}/openai/deployments/{_deployment}/chat/completions?api-version={_apiVersion}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("api-key", _apiKey);

            var body = new
            {
                messages = new object[]
                {
                    new { role = "system", content = "You generate concise, actionable meeting suggestions and answers. Keep replies short." },
                    new { role = "user", content = $"Given this live meeting snippet, propose one concise suggestion or answer.\nContext: {context}\nResponse:" }
                },
                temperature = 0.3,
                max_tokens = 128
            };

            req.Content = JsonContent.Create(body);
            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var msg = choices[0].GetProperty("message");
                if (msg.TryGetProperty("content", out var content))
                    return content.GetString() ?? string.Empty;
            }
            return string.Empty;
        }
    }
}


