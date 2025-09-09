using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cloak.Services.Rag
{
    public sealed class PlaceholderRagIndex : IRagIndex
    {
        private readonly ConcurrentDictionary<string, string> _docs = new();

        public Task IndexAsync(string documentId, string content)
        {
            _docs[documentId] = content;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> QueryAsync(string query, int topK = 3)
        {
            var results = _docs.Values
                .OrderByDescending(v => Score(v, query))
                .Take(topK)
                .ToList();
            return Task.FromResult((IReadOnlyList<string>)results);
        }

        private static int Score(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query)) return 0;
            return text.Contains(query, System.StringComparison.OrdinalIgnoreCase) ? query.Length : 0;
        }
    }
}

