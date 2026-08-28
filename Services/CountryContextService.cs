using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    /// <summary>
    /// appsettings.json > CountryDatasets bölümünden ülke -> dosya yolu eşlemesini okur.
    /// Bölüm JSON'da düz bir sözlük olduğu için (ör. {"Polonya": "...", "Turkiye": "..."} —
    /// ayrı bir "Paths" sarmalayıcı anahtarı YOK), bu sınıfın doğrudan Dictionary'den türemesi
    /// gerekiyor; aksi halde konfigürasyon binder'ı hiçbir girdiyi eşleştiremez ve sözlük hep boş kalır.
    /// </summary>
    public sealed class CountryDatasetOptions : Dictionary<string, string>
    {
        public const string SectionName = "CountryDatasets";
    }

    public sealed class CountryContextService : ICountryContextService
    {
        private readonly CountryDatasetOptions _options;
        private readonly ILogger<CountryContextService> _logger;
        private SupportedCountry _currentCountry;

        public CountryContextService(
            IOptions<CountryDatasetOptions> options,
            ILogger<CountryContextService> logger)
        {
            _options = options.Value;
            _logger = logger;

            // Şu an elimizde yalnızca Polonya veri seti gerçek/dolu; varsayılan bu.
            _currentCountry = SupportedCountry.Polonya;
        }

        public SupportedCountry CurrentCountry => _currentCountry;

        public event EventHandler<SupportedCountry>? CountryChanged;

        public void SetCountry(SupportedCountry country)
        {
            if (_currentCountry == country)
            {
                return;
            }

            _currentCountry = country;
            _logger.LogInformation("Ülke bağlamı değişti: {Country}", country);
            CountryChanged?.Invoke(this, country);
        }

        public string GetDatasetFilePath(SupportedCountry? country = null)
        {
            var target = country ?? _currentCountry;

            if (!_options.TryGetValue(target.ToString(), out var configuredPath) || string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new NotSupportedException(
                    $"'{target}' için tanımlı bir veri seti yok. appsettings.json > {CountryDatasetOptions.SectionName} bölümünü kontrol edin.");
            }

            // appsettings.json'daki yol göreli ise, çalışma dizini (Environment.CurrentDirectory)
            // nereden başlatıldığına bağlı olmasın diye uygulamanın kendi bin/output klasörüne
            // (AppContext.BaseDirectory) göre çözümlüyoruz. "dotnet run" ile "MiniMiki.exe" doğrudan
            // çalıştırma arasındaki çalışma dizini farkı bu sayede sorun yaratmaz.
            var path = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(AppContext.BaseDirectory, configuredPath);

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"'{target}' veri seti dosyası diskte bulunamadı: {path}", path);
            }

            return path;
        }
    }
}
