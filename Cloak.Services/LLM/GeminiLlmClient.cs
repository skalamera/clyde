using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cloak.Services.LLM
{
    public sealed class GeminiLlmClient : ILlmClient
    {
        private readonly string _apiKey;
        private readonly string _modelName;
        private readonly HttpClient _http = new HttpClient();

        public GeminiLlmClient(string apiKey, string modelName = "gemini-1.5-flash")
        {
            _apiKey = apiKey;
            _modelName = modelName;
        }

        public async Task<string> GetSuggestionAsync(string context)
        {
            // Load interviewer dossier files if present (copied to app output)
            string dossier = string.Empty;
            try
            {
                var parts = new[]
                {
                    TryRead("additional_details.txt"),
                    TryRead("resume.txt"),
                    TryRead("job_description.txt")
                };
                dossier = string.Join("\n\n", parts);
            }
            catch { }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
            var payload = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = $"You are an Interviewer Copilot assisting a job candidate during a live interview. Your job: (1) detect the interviewer’s question intent from the transcript, (2) provide bullet-point suggestions to the interviewee that are specific, factual, and tailored, (3) ground suggestions in the candidate’s resume and details, and (4) keep guidance concise and actionable.\n\nGuidelines:\n- Focus on the interviewer’s questions and the candidate’s best possible answer.\n- NEVER output yes/no only; provide 2–5 crisp bullet points.\n- Include concrete examples, metrics, or achievements when available.\n- If the question aligns with the job description, highlight relevant skills and experiences.\n- Avoid hallucinations; prefer what’s in the dossier.\n\nReference dossier (for grounding):\n" + dossier + $"\n\nLive transcript snippet:\n{context}\n\nTask:\n1) Provide 2–5 crisp, actionable bullet points for how the interviewee should answer.\n2) Then provide a conversational paragraph (3–6 sentences) that answers the question naturally, using first person, grounded in the dossier and transcript.\n\nOutput format:\n- Bullet points\n\nParagraph:\n<your paragraph here>" } } }
                }
            };
            using var resp = await _http.PostAsJsonAsync(url, payload);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var content = candidates[0].GetProperty("content");
                var parts = content.GetProperty("parts");
                if (parts.GetArrayLength() > 0 && parts[0].TryGetProperty("text", out var text))
                    return text.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        private static string TryRead(string fileName)
        {
            try { return System.IO.File.Exists(fileName) ? System.IO.File.ReadAllText(fileName) : string.Empty; }
            catch { return string.Empty; }
        }
    }
}


