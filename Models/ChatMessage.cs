using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MiniMiki.Models
{
    public enum ChatRole
    {
        User,
        Assistant
    }

    /// <summary>Bir kaynak chunk'a ait, sohbet balonunun altındaki Kaynakça listesinde gösterilen tıklanabilir referans.</summary>
    public sealed record SourceReference(string Id, string Title, string? Url);

    /// <summary>
    /// Sohbet geçmişindeki tek bir mesaj (kullanıcı sorusu veya asistan cevabı).
    /// Text mutable + ObservableObject: streaming cevap geldikçe UI'ı canlı güncelleyebilmek için.
    /// </summary>
    public sealed partial class ChatMessage : ObservableObject
    {
        [ObservableProperty]
        private string _text;

        // Cevap tamamlanana kadar boş kalır (streaming sırasında "Kaynakça" görünmesin diye);
        // ViewModel stream bittiğinde bu property'yi doldurur.
        [ObservableProperty]
        private IReadOnlyList<SourceReference> _sources;

        public ChatRole Role { get; }
        public bool IsError { get; }

        public ChatMessage(ChatRole role, string text, IReadOnlyList<SourceReference>? sources = null, bool isError = false)
        {
            Role = role;
            _text = text;
            _sources = sources ?? Array.Empty<SourceReference>();
            IsError = isError;
        }

        public void AppendText(string delta) => Text += delta;
    }

    /// <summary>AskStreamingAsync'in ürettiği olaylar: önce kaynaklar, sonra metin parçaları gelir.</summary>
    public abstract record ChatStreamEvent;

    public sealed record ChatStreamSources(IReadOnlyList<SourceReference> Sources) : ChatStreamEvent;

    public sealed record ChatStreamDelta(string Text) : ChatStreamEvent;
}
