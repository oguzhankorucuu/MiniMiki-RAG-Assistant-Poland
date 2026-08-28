using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    /// <summary>
    /// IRagSearchService ile alakalı chunk'ları getirir, ardından Gemini'nin streamGenerateContent
    /// uç noktasına bu chunk'ları + son birkaç tur sohbet geçmişini bağlam olarak vererek doğal dilde,
    /// Türkçe bir cevabı SSE üzerinden parça parça (streaming) ürettirir.
    /// Kaynakça listesi LLM'e yazdırılmaz; UI, RagSearchResult'lardan doğrudan kendisi oluşturur
    /// (linkler her zaman veri setindeki gerçek source_urls'e gider, halüsinasyon riski olmaz).
    /// </summary>
    public sealed class GeminiRagChatService : IRagChatService
    {
        private const int TopKForContext = 5;

        // Modele gönderilen geçmiş tur sayısı (kullanıcı+asistan mesajı birlikte); token/maliyet
        // büyümesini sınırlamak için son birkaç turla sınırlıyoruz.
        private const int MaxHistoryMessages = 6;

        private const string SystemInstruction =
            "Sen Mini-MİKİ adlı, Polonya e-ticaret mevzuatı konusunda bilgi veren Türkçe konuşan bir asistansın. " +
            "SADECE sana verilen BAĞLAM içindeki bilgilere dayanarak cevap ver; bağlamda olmayan bir konuda kesin bilgi uydurma, " +
            "böyle bir durumda bunu açıkça belirt. Önceki sohbet geçmişini dikkate alarak takip sorularını (ör. 'peki ya masraflar?') " +
            "doğru şekilde anla ve bağlamını koru. Cevabının sonuna ayrı bir kaynak/link listesi EKLEME, uygulama bunu zaten " +
            "otomatik olarak gösterecek. Kısa, net ve sohbet diliyle cevap ver. Cevabını DÜZ METİN olarak yaz; markdown " +
            "biçimlendirmesi kullanma (yıldız/kalın yazı, madde işareti, başlık gibi işaretler koyma), sadece normal cümleler " +
            "ve gerekirse satır sonu kullan. Bu içeriğin hukuki tavsiye yerine geçmediğini gerektiğinde kısaca hatırlat.";

        private readonly HttpClient _httpClient;
        private readonly IRagSearchService _searchService;
        private readonly GeminiOptions _options;
        private readonly ILogger<GeminiRagChatService> _logger;

        public GeminiRagChatService(
            HttpClient httpClient,
            IRagSearchService searchService,
            IOptions<GeminiOptions> options,
            ILogger<GeminiRagChatService> logger)
        {
            _httpClient = httpClient;
            _searchService = searchService;
            _options = options.Value;
            _logger = logger;
        }

        public async IAsyncEnumerable<ChatStreamEvent> AskStreamingAsync(
            string question,
            IReadOnlyList<ChatMessage> history,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var retrievalQuery = BuildRetrievalQuery(question, history);
            var results = await _searchService.SearchAsync(retrievalQuery, TopKForContext, cancellationToken).ConfigureAwait(false);

            if (results.Count == 0)
            {
                yield return new ChatStreamSources(Array.Empty<SourceReference>());
                yield return new ChatStreamDelta("Bu soruyla ilgili veri setinde bir bilgi bulamadım.");
                yield break;
            }

            var sources = results
                .Select(r => new SourceReference(r.Chunk.Id, r.Chunk.Title, r.Chunk.SourceUrls.FirstOrDefault()))
                .ToList();
            yield return new ChatStreamSources(sources);

            var userContent = BuildUserContent(question, results);
            var historyContents = BuildHistoryContents(history);

            await foreach (var delta in StreamAnswerAsync(historyContents, userContent, cancellationToken).ConfigureAwait(false))
            {
                yield return new ChatStreamDelta(delta);
            }
        }

        /// <summary>
        /// Takip sorularında ("peki ya masrafları?") arama kalitesini artırmak için, önceki kullanıcı
        /// mesajını da embedding sorgusuna dahil ediyoruz (ayrı bir LLM çağrısı gerektirmeyen basit ama etkili bir yöntem).
        /// </summary>
        private static string BuildRetrievalQuery(string question, IReadOnlyList<ChatMessage> history)
        {
            var lastUserMessage = history.LastOrDefault(m => m.Role == ChatRole.User && !m.IsError);
            return lastUserMessage is null ? question : $"{lastUserMessage.Text} {question}";
        }

        private static List<ContentItem> BuildHistoryContents(IReadOnlyList<ChatMessage> history)
        {
            return history
                .Where(m => !m.IsError && !string.IsNullOrWhiteSpace(m.Text))
                .TakeLast(MaxHistoryMessages)
                .Select(m => new ContentItem
                {
                    Role = m.Role == ChatRole.User ? "user" : "model",
                    Parts = new List<TextPart> { new() { Text = m.Text } }
                })
                .ToList();
        }

        private static string BuildUserContent(string question, IReadOnlyList<RagSearchResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Kullanıcının sorusu: {question}");
            sb.AppendLine();
            sb.AppendLine("Aşağıda bu soruyla ilgili olabilecek hukuki metin parçaları var. Cevabını sadece bunlara dayandır:");

            foreach (var r in results)
            {
                sb.AppendLine();
                sb.AppendLine($"[{r.Chunk.Id}] {r.Chunk.Title}");
                sb.AppendLine(r.Chunk.Content);
            }

            return sb.ToString();
        }

        private async IAsyncEnumerable<string> StreamAnswerAsync(
            List<ContentItem> historyContents,
            string currentUserContent,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new RagSearchException(
                    "Gemini API anahtarı tanımlı değil. appsettings.json > Gemini:ApiKey alanını doldurun.");
            }

            var contents = new List<ContentItem>(historyContents)
            {
                new() { Role = "user", Parts = new List<TextPart> { new() { Text = currentUserContent } } }
            };

            var requestBody = new GenerateContentRequest
            {
                SystemInstruction = new ContentPart { Parts = new List<TextPart> { new() { Text = SystemInstruction } } },
                Contents = contents
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{_options.ChatModel}:streamGenerateContent?alt=sse")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new RagSearchException(
                    $"Gemini yanıt üretme isteği başarısız oldu ({(int)response.StatusCode} {response.StatusCode}): {errorBody}");
            }

            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                    if (line is null || !line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var delta = TryExtractDelta(line["data: ".Length..]);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        yield return delta;
                    }
                }
            }
        }

        /// <summary>SSE satırındaki JSON'dan metin parçasını çıkarır; bozuk/eksik bir event gelirse akışı bozmadan atlar.</summary>
        private static string? TryExtractDelta(string eventJson)
        {
            try
            {
                using var eventDoc = JsonDocument.Parse(eventJson);
                return eventDoc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private sealed class GenerateContentRequest
        {
            [JsonPropertyName("systemInstruction")]
            public ContentPart? SystemInstruction { get; set; }

            [JsonPropertyName("contents")]
            public List<ContentItem> Contents { get; set; } = new();
        }

        private sealed class ContentItem
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = "user";

            [JsonPropertyName("parts")]
            public List<TextPart> Parts { get; set; } = new();
        }

        private sealed class ContentPart
        {
            [JsonPropertyName("parts")]
            public List<TextPart> Parts { get; set; } = new();
        }

        private sealed class TextPart
        {
            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
        }
    }
}
