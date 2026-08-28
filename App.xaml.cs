using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniMiki.Services;
using MiniMiki.ViewModels;

namespace MiniMiki
{
    public partial class App : Application
    {
        private IHost? _host;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.SetBasePath(AppContext.BaseDirectory);
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<GeminiOptions>(context.Configuration.GetSection(GeminiOptions.SectionName));
                    services.Configure<CountryDatasetOptions>(context.Configuration.GetSection(CountryDatasetOptions.SectionName));

                    services.AddHttpClient("Gemini", (sp, client) =>
                    {
                        var opts = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
                        client.BaseAddress = new Uri(opts.BaseUrl);
                        // gemini-3.6-flash bazı sorularda ilk token'a kadar 12-17 sn sürebiliyor;
                        // 30 sn bazen bu payı zorluyordu, 60 sn'ye çıkarıp daha fazla nefes payı bırakıyoruz.
                        client.Timeout = TimeSpan.FromSeconds(60);
                    });

                    services.AddSingleton<IDataLoaderService, DataLoaderService>();
                    services.AddSingleton<ICountryContextService, CountryContextService>();

                    // Singleton: GeminiRagSearchService'in içindeki embedding cache'inin
                    // uygulama ömrü boyunca korunması için typed-client yerine bilinçli
                    // olarak IHttpClientFactory ile manuel oluşturuyoruz.
                    services.AddSingleton<IRagSearchService>(sp => new GeminiRagSearchService(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Gemini"),
                        sp.GetRequiredService<IDataLoaderService>(),
                        sp.GetRequiredService<ICountryContextService>(),
                        sp.GetRequiredService<IOptions<GeminiOptions>>(),
                        sp.GetRequiredService<ILogger<GeminiRagSearchService>>()));

                    services.AddSingleton<IRagChatService>(sp => new GeminiRagChatService(
                        sp.GetRequiredService<IHttpClientFactory>().CreateClient("Gemini"),
                        sp.GetRequiredService<IRagSearchService>(),
                        sp.GetRequiredService<IOptions<GeminiOptions>>(),
                        sp.GetRequiredService<ILogger<GeminiRagChatService>>()));

                    services.AddTransient<MainViewModel>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Embedding cache'ini arka planda ısıt: kullanıcı ilk sorusunu yazana kadar
            // 23 chunk'ın embed edilmesi genelde zaten bitmiş olur, ilk gerçek soru da hızlı yanıtlanır.
            _ = WarmUpAsync(_host.Services);
        }

        private static async Task WarmUpAsync(IServiceProvider services)
        {
            try
            {
                var ragSearchService = services.GetRequiredService<IRagSearchService>();
                await ragSearchService.SearchAsync("başlangıç ısınması", topK: 1);
            }
            catch
            {
                // Isınma başarısız olursa sorun değil; ilk gerçek soru normal şekilde embed eder.
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
