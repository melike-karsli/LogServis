🎯 Projenin Amacı
API tabanlı çalışan sistemlerde (Yemeksepeti, Getir, Trendyol vb. entegrasyonları gibi), internet veya sunucu erişim hataları meydana geldiğinde durumu manuel kontrol etmek yerine, kullanıcıyı görsel bir uyarı sistemiyle (System Tray) anında haberdar etmektir.

🛠 Temel Özellikler
Anlık Log Takibi: Belirlenen .txt dosyalarını sürekli tarayarak yeni satırları kontrol eder.

JSON Hata Analizi: Log içerisindeki JSON verilerini C# ile deserialize ederek "Server Not Found", "Connection Error" gibi spesifik hataları ayıklar.

Görsel Durum İndikatörü: Görev çubuğunda (System Tray) çalışan interaktif ikonlar:

🔴 Kırmızı İkon (red.ico): Kritik bir bağlantı hatası var.

🟢 Yeşil İkon (green.ico): Sistem sorunsuz çalışıyor veya hata giderildi.

Düşük Kaynak Tüketimi: Arka planda sistem kaynaklarını yormadan çalışacak şekilde optimize edilmiştir.

🚀 Çalışma Akışı
Uygulama arka planda log dosyasını izlemeye başlar.

Dosyaya yeni bir JSON verisi düştüğünde uygulama bunu otomatik olarak yakalar.

Eğer veri içerisinde "internet erişimi yok" veya "sunucuya ulaşılamıyor" gibi bir hata kodu/metni varsa, bildirim ikonu anında kırmızıya döner.

Hata logları kesildiğinde veya sistem düzeldiğinde ikon tekrar yeşile dönerek güvenli durumu bildirir.

📋 Gereksinimler
IDE: Visual Studio

Dil: C# (.NET)

Dosyalar: * İzlenecek olan .txt log dosyası.

red.ico ve green.ico (İkon dosyaları).

📂 Kurulum
Repoyu klonlayın.

Visual Studio üzerinde projeyi açın.

Log dosyasının okunacağı PATH bilgisini kod içerisinden veya config dosyasından güncelleyin.

Build ederek exe dosyasını çalıştırın.
