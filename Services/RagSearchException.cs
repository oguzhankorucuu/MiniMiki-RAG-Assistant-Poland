using System;

namespace MiniMiki.Services
{
    /// <summary>
    /// RAG arama sürecindeki (embedding API hatası, veri seti bulunamadı, ağ hatası vb.)
    /// tüm alt-seviye istisnaları tek bir tip altında ViewModel'e taşımak için kullanılır.
    /// </summary>
    public sealed class RagSearchException : Exception
    {
        public RagSearchException(string message) : base(message)
        {
        }

        public RagSearchException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
