using System;
using MiniMiki.Models;

namespace MiniMiki.Services
{
    /// <summary>
    /// Kullanıcının arayüzden seçtiği ülke bağlamını tutar ve RAG servislerinin
    /// hangi veri seti dosyasını kullanacağını belirlemesini sağlar.
    /// </summary>
    public interface ICountryContextService
    {
        SupportedCountry CurrentCountry { get; }

        event EventHandler<SupportedCountry>? CountryChanged;

        void SetCountry(SupportedCountry country);

        /// <summary>Belirtilen (veya mevcut) ülkeye ait veri seti dosya yolunu döndürür.</summary>
        /// <exception cref="NotSupportedException">Ülke için tanımlı bir veri seti yoksa.</exception>
        /// <exception cref="System.IO.FileNotFoundException">Tanımlı dosya diskte bulunamazsa.</exception>
        string GetDatasetFilePath(SupportedCountry? country = null);
    }
}
