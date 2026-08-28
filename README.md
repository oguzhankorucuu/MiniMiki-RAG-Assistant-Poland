# Mini-MİKİ (Polonya E-Ticaret Mevzuat Asistanı) ⚖️🛒

Bu proje, Polonya e-ticaret yasaları ve tüketici hakları mevzuatında hızlı ve anlamsal (semantic) arama yapabilmek için geliştirilmiş bir **C# WPF** masaüstü asistanıdır. 

Proje, geleneksel kelime eşleştirme yerine **RAG (Retrieval-Augmented Generation)** mimarisini kullanarak soruların bağlamını anlar, diller arası eşleştirme yapar (Türkçe soru -> Polonya yasası) ve önceki sohbet geçmişini (memory) hafızasında tutarak ardışık sorulara tutarlı hukuki referanslar getirir.

## 🚀 Öne Çıkan Özellikler
* **Cross-Lingual Arama:** Türkçe sorulan karmaşık hukuki senaryoları algılayıp, Polonya mevzuatındaki karşılıklarını bulur.
* **Bağlamsal Hafıza:** Peş peşe sorulan sorularda referansları ("bu hak", "o zaman" vb.) anlar.
* **Asenkron ve Temiz Arayüz:** MVVM mimarisine sadık kalınarak tasarlanmış donmayan kullanıcı deneyimi.
* **Hata Yönetimi (Try-Catch):** API veya veritabanı kilitlenmelerinde çökmeden kontrollü geri bildirim.

## 🛠️ Kullanılan Teknolojiler
* C# / .NET 8.0
* WPF (Windows Presentation Foundation)
* LLM Embedding & RAG Mimarisi
* JSON (Hukuki Veritabanı)

## ⚙️ Kurulum ve Çalıştırma
Projeyi kaynak kodundan çalıştırmak için:
1. Repoyu klonlayın.
2. Ana dizindeki `appsettings.example.json` dosyasının adını `appsettings.json` olarak değiştirin.
3. İçerisine kendi API anahtarınızı ekleyin.
4. Visual Studio üzerinden derleyip çalıştırın veya terminalde `dotnet run` komutunu kullanın.
