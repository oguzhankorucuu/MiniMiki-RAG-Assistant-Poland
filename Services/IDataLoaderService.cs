using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    public interface IDataLoaderService
    {
        Task<IReadOnlyList<LegalDocumentChunk>> LoadChunksAsync(string filePath, CancellationToken cancellationToken = default);
    }
}
