using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cloak.Services.LLM
{
    public class OpenAiLlmClient : ILlmClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

        public OpenAiLlmClient(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
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

            var requestBody = new
            {
                model = "gpt-4o-mini", // or "gpt-3.5-turbo" for cheaper option
                messages = new[]
                {
                    new { role = "system", content =
                        "You are an Interviewer Copilot assisting a job candidate during a live interview. Your job: (1) detect the interviewer’s question intent from the transcript, (2) provide bullet-point suggestions to the interviewee that are specific, factual, and tailored, (3) ground suggestions in the candidate’s resume and details, and (4) keep guidance concise and actionable.\n\nGuidelines:\n- Focus on the interviewer’s questions and the candidate’s best possible answer.\n- NEVER output yes/no only; provide 2–5 crisp bullet points.\n- Include concrete examples, metrics, or achievements when available.\n- If the question aligns with the job description, highlight relevant skills and experiences.\n- Avoid hallucinations; prefer what’s in the dossier.\n\nReference dossier (for grounding):\n" + dossier },
                    new { role = "user", content = $"Live transcript snippet:\n{context}\n\nTask:\n1) Provide 2–5 crisp, actionable bullet points for how the interviewee should answer.\n2) Then provide a conversational paragraph (3–6 sentences) that answers the question naturally, using first person, grounded in the dossier and transcript.\n\nOutput format:\n- Bullet points\n\nParagraph:\n<your paragraph here>" }
                },
                temperature = 0.3,
                max_tokens = 384
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(ApiUrl, content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseJson);
                var suggestion = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return suggestion?.Trim() ?? "No suggestion available";
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }

        private static string TryRead(string fileName)
        {
            try { return System.IO.File.Exists(fileName) ? System.IO.File.ReadAllText(fileName) : string.Empty; }
            catch { return string.Empty; }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}



