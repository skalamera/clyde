using System.Collections.Generic;
using System.Threading.Tasks;

namespace Cloak.Services.Rag
{
    public interface IRagIndex
    {
        Task IndexAsync(string documentId, string content);
        Task<IReadOnlyList<string>> QueryAsync(string query, int topK = 3);
    }
}

