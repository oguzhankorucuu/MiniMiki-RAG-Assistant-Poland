using System.Collections.Generic;
using System.Threading;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    /// <summary>
    /// Retrieval (IRagSearchService) + generation (LLM) adımlarını birleştirip
    /// kullanıcı sorusuna, önceki sohbet geçmişini de dikkate alarak, kaynaklarıyla
    /// birlikte akan (streaming) bir cevap üretir.
    /// </summary>
    public interface IRagChatService
    {
        /// <summary>
        /// İlk olay her zaman <see cref="ChatStreamSources"/>'tır (kaynaklar belliyken, cevap daha üretilmeden önce
        /// gelir); ardından cevabın parçaları <see cref="ChatStreamDelta"/> olarak akar.
        /// </summary>
        /// <param name="history">
        /// Yeni soru eklenmeden önceki sohbet geçmişi (LLM'e multi-turn bağlam olarak gönderilir).
        /// </param>
        /// <exception cref="RagSearchException">Arama veya cevap üretme sırasında bir hata oluşursa.</exception>
        IAsyncEnumerable<ChatStreamEvent> AskStreamingAsync(
            string question,
            IReadOnlyList<ChatMessage> history,
            CancellationToken cancellationToken = default);
    }
}
