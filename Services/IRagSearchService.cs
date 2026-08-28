using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    public sealed record RagSearchResult(LegalDocumentChunk Chunk, float Score);

    /// <summary>OpenAI/OpenAiRagSearchService gibi somut implementasyonlardan bağımsız arama sözleşmesi.</summary>
    public interface IRagSearchService
    {
        /// <summary>
        /// Mevcut ülke bağlamına (ICountryContextService.CurrentCountry) göre en alakalı Top-N chunk'ı döndürür.
        /// </summary>
        /// <exception cref="RagSearchException">Embedding çağrısı, veri seti okuma veya ağ hatası durumunda.</exception>
        Task<IReadOnlyList<RagSearchResult>> SearchAsync(string query, int topK = 3, CancellationToken cancellationToken = default);
    }
}
