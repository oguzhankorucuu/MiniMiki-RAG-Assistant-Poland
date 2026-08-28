using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MiniMiki.Models;
using MiniMiki.Services;

namespace MiniMiki.ViewModels
{
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly IRagChatService _chatService;
        private readonly ICountryContextService _countryContext;
        private readonly ILogger<MainViewModel> _logger;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        private string _queryText = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SendCommand))]
        private bool _isBusy;

        [ObservableProperty]
        private SupportedCountry _selectedCountry = SupportedCountry.Polonya;

        public ObservableCollection<ChatMessage> Messages { get; } = new();

        public SupportedCountry[] AvailableCountries { get; } = (SupportedCountry[])Enum.GetValues(typeof(SupportedCountry));

        public MainViewModel(
            IRagChatService chatService,
            ICountryContextService countryContext,
            ILogger<MainViewModel> logger)
        {
            _chatService = chatService;
            _countryContext = countryContext;
            _logger = logger;
        }

        // ComboBox üzerinden SelectedCountry değiştiğinde otomatik tetiklenir
        // (CommunityToolkit.Mvvm source generator'ının ürettiği partial hook).
        partial void OnSelectedCountryChanged(SupportedCountry value)
        {
            _countryContext.SetCountry(value);
        }

        [RelayCommand(CanExecute = nameof(CanSend))]
        private async Task SendAsync(CancellationToken cancellationToken)
        {
            var question = QueryText.Trim();
            QueryText = string.Empty;

            // Yeni soru eklenmeden ÖNCEKİ hâli: LLM'e multi-turn bağlam olarak gönderilecek geçmiş.
            var history = Messages.ToList();
            Messages.Add(new ChatMessage(ChatRole.User, question));

            IsBusy = true;
            ChatMessage? assistantMessage = null;
            IReadOnlyList<SourceReference>? pendingSources = null;
            try
            {
                await foreach (var streamEvent in _chatService.AskStreamingAsync(question, history, cancellationToken))
                {
                    switch (streamEvent)
                    {
                        case ChatStreamSources sourcesEvent:
                            // Henüz balona eklemiyoruz — cevap tamamen bitene kadar "Kaynakça" görünmesin.
                            pendingSources = sourcesEvent.Sources;
                            break;

                        case ChatStreamDelta deltaEvent:
                            if (assistantMessage is null)
                            {
                                assistantMessage = new ChatMessage(ChatRole.Assistant, string.Empty);
                                Messages.Add(assistantMessage);
                            }
                            assistantMessage.AppendText(deltaEvent.Text);
                            break;
                    }
                }

                // Stream sorunsuz tamamlandı: kaynakçayı ancak şimdi göster.
                if (assistantMessage is not null && pendingSources is not null)
                {
                    assistantMessage.Sources = pendingSources;
                }
            }
            catch (RagSearchException ex)
            {
                _logger.LogWarning(ex, "RAG sohbeti kullanıcı tarafında hataya düştü.");
                HandleChatFailure(assistantMessage, ex.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Gerçekten BİZİM iptalimiz: kullanıcı yeni bir mesaj gönderdi veya pencere kapandı; sessizce yut.
            }
            catch (OperationCanceledException ex)
            {
                // Bizim iptal etmediğimiz bir OperationCanceledException — ör. HttpClient'ın kendi
                // isteği zaman aşımına uğrattığı TaskCanceledException. Bunu sessizce yutarsak kullanıcıya
                // "hiçbir şey olmuyor, donmuş" izlenimi verir; bu yüzden görünür bir hata olarak gösteriyoruz.
                _logger.LogWarning(ex, "İstek zaman aşımına uğradı (kullanıcı kaynaklı iptal değil).");
                HandleChatFailure(assistantMessage, "İstek zaman aşımına uğradı. Lütfen tekrar deneyin.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Beklenmeyen hata: RAG sohbeti sırasında.");
                HandleChatFailure(assistantMessage, "Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Streaming sırasında hata olursa: hiç metin gelmediyse balonu kaldırıp yerine kırmızı bir hata
        /// balonu ekler; kısmi bir cevap zaten geldiyse (bağlantı yarıda kesildi gibi) balonu silmeden
        /// sonuna kısa bir uyarı ekler.
        /// </summary>
        private void HandleChatFailure(ChatMessage? partialMessage, string errorText)
        {
            if (partialMessage is not null && !string.IsNullOrEmpty(partialMessage.Text))
            {
                partialMessage.AppendText($"\n\n⚠️ {errorText}");
                return;
            }

            if (partialMessage is not null)
            {
                Messages.Remove(partialMessage);
            }

            Messages.Add(new ChatMessage(ChatRole.Assistant, errorText, isError: true));
        }

        private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(QueryText);
    }
}
