using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    /// <summary>Dosya yoluna göre cache'lenen, tekrar diske gitmeyen basit JSON yükleyici.</summary>
    public sealed class DataLoaderService : IDataLoaderService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ConcurrentDictionary<string, Task<IReadOnlyList<LegalDocumentChunk>>> _cache = new();
        private readonly ILogger<DataLoaderService> _logger;

        public DataLoaderService(ILogger<DataLoaderService> logger)
        {
            _logger = logger;
        }

        public Task<IReadOnlyList<LegalDocumentChunk>> LoadChunksAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return _cache.GetOrAdd(filePath, path => LoadFromDiskAsync(path, cancellationToken));
        }

        private async Task<IReadOnlyList<LegalDocumentChunk>> LoadFromDiskAsync(string path, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Hukuki veri seti diskten yükleniyor: {Path}", path);

            await using var stream = File.OpenRead(path);
            var dataset = await JsonSerializer.DeserializeAsync<LegalDataset>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            if (dataset is null)
            {
                throw new InvalidDataException($"Veri seti deserialize edilemedi (boş sonuç): {path}");
            }

            _logger.LogInformation("{Count} belge yüklendi: {Path}", dataset.Documents.Count, path);
            return dataset.Documents;
        }
    }
}
