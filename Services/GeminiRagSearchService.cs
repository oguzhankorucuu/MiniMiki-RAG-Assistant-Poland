using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Numerics.Tensors;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    public sealed class GeminiOptions
    {
        public const string SectionName = "Gemini";

        public string ApiKey { get; set; } = string.Empty;
        public string EmbeddingModel { get; set; } = "gemini-embedding-001";
        public string ChatModel { get; set; } = "gemini-3.6-flash";
        public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";
    }

    /// <summary>
    /// Google Gemini (generativelanguage.googleapis.com) batchEmbedContents uç noktasıyla kullanıcı
    /// sorgusunu ve (ülke bazında cache'lenmiş) LegalDocumentChunk içeriklerini vektöre çevirip
    /// System.Numerics.Tensors ile in-memory cosine-similarity tabanlı Top-K benzerlik araması yapar.
    ///
    /// Ömür (lifetime) notu: bu sınıf DI konteynerinde SINGLETON kaydedilmelidir.
    /// _indexCache alanı ülke başına embedding'leri bir kez hesaplayıp bellekte tutar;
    /// her arama isteğinde yeniden hesaplamak hem gecikme hem API maliyeti yaratır.
    /// </summary>
    public sealed class GeminiRagSearchService : IRagSearchService
    {
        // Gemini batchEmbedContents tek istekte en fazla 100 içerik kabul eder; bu proje ölçeğinde
        // (şu an 23 chunk) tek batch yeterli, bu yüzden ayrıca sayfalama eklemedik.
        private readonly HttpClient _httpClient;
        private readonly IDataLoaderService _dataLoader;
        private readonly ICountryContextService _countryContext;
        private readonly GeminiOptions _options;
        private readonly ILogger<GeminiRagSearchService> _logger;

        private readonly ConcurrentDictionary<SupportedCountry, Lazy<Task<EmbeddedChunkIndex>>> _indexCache = new();

        public GeminiRagSearchService(
            HttpClient httpClient,
            IDataLoaderService dataLoader,
            ICountryContextService countryContext,
            IOptions<GeminiOptions> options,
            ILogger<GeminiRagSearchService> logger)
        {
            _httpClient = httpClient;
            _dataLoader = dataLoader;
            _countryContext = countryContext;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<IReadOnlyList<RagSearchResult>> SearchAsync(
            string query,
            int topK = 3,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("Sorgu boş olamaz.", nameof(query));
            }

            if (topK <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(topK), "topK sıfırdan büyük olmalı.");
            }

            var country = _countryContext.CurrentCountry;

            try
            {
                var index = await GetOrBuildIndexAsync(country).ConfigureAwait(false);
                if (index.Vectors.Count == 0)
                {
                    return Array.Empty<RagSearchResult>();
                }

                var queryEmbeddings = await EmbedAsync(new[] { query }, cancellationToken).ConfigureAwait(false);
                var queryVector = queryEmbeddings[0];

                var scored = new List<RagSearchResult>(index.Vectors.Count);
                foreach (var (chunk, vector) in index.Vectors)
                {
                    // Gemini embedding vektörlerinin birim uzunlukta normalize olduğu garanti
                    // edilmediğinden Dot yerine CosineSimilarity kullanıyoruz (norm'a bölerek düzeltir).
                    var score = TensorPrimitives.CosineSimilarity((ReadOnlySpan<float>)queryVector, (ReadOnlySpan<float>)vector);
                    scored.Add(new RagSearchResult(chunk, score));
                }

                return scored
                    .OrderByDescending(r => r.Score)
                    .Take(topK)
                    .ToList();
            }
            catch (RagSearchException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RAG araması başarısız oldu. Ülke: {Country}, Sorgu: {Query}", country, query);
                throw new RagSearchException("Hukuki veri tabanında arama yapılırken beklenmeyen bir hata oluştu.", ex);
            }
        }

        private Task<EmbeddedChunkIndex> GetOrBuildIndexAsync(SupportedCountry country)
        {
            // Not: index inşası tek bir aramaya değil, ülke bazlı paylaşılan cache'e bağlıdır;
            // bu yüzden çağıranın CancellationToken'ını değil CancellationToken.None'ı kullanıyoruz.
            // Aksi halde ilk arama iptal edildiğinde/hata verdiğinde sonraki aramalar da etkilenir.
            var lazy = _indexCache.GetOrAdd(
                country,
                c => new Lazy<Task<EmbeddedChunkIndex>>(
                    () => BuildIndexAsync(c, CancellationToken.None),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return AwaitAndEvictOnFailureAsync(country, lazy);
        }

        private async Task<EmbeddedChunkIndex> AwaitAndEvictOnFailureAsync(
            SupportedCountry country,
            Lazy<Task<EmbeddedChunkIndex>> lazy)
        {
            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                // Başarısız index inşasını cache'te bırakmıyoruz; aksi halde kullanıcı
                // API key'i düzeltse bile sonraki her arama aynı (cache'lenmiş) hatayı
                // sonsuza kadar tekrar eder — bir sonraki arama temiz bir şekilde yeniden dener.
                _indexCache.TryRemove(new KeyValuePair<SupportedCountry, Lazy<Task<EmbeddedChunkIndex>>>(country, lazy));
                throw;
            }
        }

        private async Task<EmbeddedChunkIndex> BuildIndexAsync(SupportedCountry country, CancellationToken cancellationToken)
        {
            var filePath = _countryContext.GetDatasetFilePath(country);
            var chunks = await _dataLoader.LoadChunksAsync(filePath, cancellationToken).ConfigureAwait(false);

            if (chunks.Count == 0)
            {
                _logger.LogWarning("{Country} için yüklenen veri setinde belge bulunamadı.", country);
                return new EmbeddedChunkIndex(Array.Empty<(LegalDocumentChunk, float[])>());
            }

            var inputs = chunks.Select(BuildEmbeddingInput).ToArray();
            var vectors = await EmbedAsync(inputs, cancellationToken).ConfigureAwait(false);

            var pairs = chunks
                .Zip(vectors, (chunk, vector) => (chunk, vector))
                .ToArray();

            _logger.LogInformation("{Country} için {Count} chunk embed edildi (model: {Model}).", country, pairs.Length, _options.EmbeddingModel);
            return new EmbeddedChunkIndex(pairs);
        }

        /// <summary>
        /// Embedding kalitesini artırmak için konu/başlık/anahtar kelimeleri içeriğe önden eklenir
        /// ("contextual header" tekniği) — chunk'ı konu bağlamından koparmadan embed eder.
        /// </summary>
        private static string BuildEmbeddingInput(LegalDocumentChunk chunk)
        {
            var keywords = chunk.Keywords.Count > 0 ? string.Join(", ", chunk.Keywords) : string.Empty;
            return $"[{chunk.Topic} / {chunk.Subtopic}] {chunk.Title}\n{chunk.Content}\nAnahtar terimler: {keywords}";
        }

        private async Task<float[][]> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new RagSearchException(
                    "Gemini API anahtarı tanımlı değil. appsettings.json > Gemini:ApiKey alanını doldurun.");
            }

            var modelPath = $"models/{_options.EmbeddingModel}";

            var requestBody = new BatchEmbedRequest
            {
                Requests = inputs
                    .Select(text => new EmbedRequestItem
                    {
                        Model = modelPath,
                        Content = new EmbedContent
                        {
                            Parts = new List<EmbedPart> { new() { Text = text } }
                        }
                    })
                    .ToList()
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{modelPath}:batchEmbedContents")
            {
                Content = JsonContent.Create(requestBody)
            };
            // Sorgu string'i içine key koymak yerine header kullanıyoruz (URL/loglara sızmasın diye).
            request.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new RagSearchException(
                    $"Gemini embedding isteği başarısız oldu ({(int)response.StatusCode} {response.StatusCode}): {errorBody}");
            }

            var result = await response.Content
                .ReadFromJsonAsync<BatchEmbedResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (result is null || result.Embeddings.Count == 0)
            {
                throw new RagSearchException("Gemini embedding yanıtı çözümlenemedi veya boş döndü.");
            }

            if (result.Embeddings.Count != inputs.Count)
            {
                throw new RagSearchException(
                    $"Gemini embedding yanıtındaki sonuç sayısı ({result.Embeddings.Count}) istek sayısıyla ({inputs.Count}) uyuşmuyor.");
            }

            // batchEmbedContents yanıtı istek sırasını korur (Google dokümantasyonu); OpenAI'nin
            // aksine ayrı bir "index" alanı yok, bu yüzden doğrudan sırayla eşliyoruz.
            return result.Embeddings.Select(e => e.Values).ToArray();
        }

        private sealed record EmbeddedChunkIndex(IReadOnlyList<(LegalDocumentChunk Chunk, float[] Vector)> Vectors);

        private sealed class BatchEmbedRequest
        {
            [JsonPropertyName("requests")]
            public List<EmbedRequestItem> Requests { get; set; } = new();
        }

        private sealed class EmbedRequestItem
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("content")]
            public EmbedContent Content { get; set; } = new();
        }

        private sealed class EmbedContent
        {
            [JsonPropertyName("parts")]
            public List<EmbedPart> Parts { get; set; } = new();
        }

        private sealed class EmbedPart
        {
            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
        }

        private sealed class BatchEmbedResponse
        {
            [JsonPropertyName("embeddings")]
            public List<EmbeddingValues> Embeddings { get; set; } = new();
        }

        private sealed class EmbeddingValues
        {
            [JsonPropertyName("values")]
            public float[] Values { get; set; } = Array.Empty<float>();
        }
    }
}
