
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace RestoPOS.LogMonitor.Service
{
    public partial class LogMonitorService : ServiceBase //Windows servisini temsil eden sınıf
    {


        public LogMonitorService()
        {
            InitializeComponent();
        }


        Timer _timer; //log kontrolü için timer
        long _lastPosition = 0; //son okunan log satırının pozisyonu
        DateTime _lastErrorTime = DateTime.MinValue; //son hata tarihi
        DateTime _currentLogDate = DateTime.Today; //bugünün tarihi

        // Son yazılan status'u tekrar yazmamak için tutuyoruz (flicker önlemek için)
        // Dosyaya en son "OK" mi yoksa "ERROR" mu yazdığımızı hatırlar
        string _lastStatus = null;

        // Yeni: hata aktifliğini açıkça tutan bayrak
        bool _isErrorActive = false;

        // Son görülen hata satırını kısa tutmak için
        string _lastErrorLine = null;

        // İlk okuma sırasında çok büyük dosyalarda sadece sondan belli bir kısmı okuruz
        const int InitialTailBytes = 8 * 1024; // 8 KB

        // Servis başlarken bir kere log yazmak için flag
        bool _initialCheckDone = false;

        // Aktif probe zamanlaması: hata durumundayken logtaki hata satırlarının devam edip etmediğini bu aralıkla kontrol et
        readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(20);
        DateTime _lastProbeTime = DateTime.MinValue;


        protected override void OnStart(string[] args) //Servis başlatıldığında çalışan method
        {
            // Başlangıçta bir kere senkron olarak kontrol et ve minimal log yaz (ilk durum kaydı)
            try
            {
                RunCheck(isInitial: true);
            }
            catch
            {
                // Başlangıçta hata olsa da timer ile takip devam etsin
            }
            _initialCheckDone = true;

            // Sonraki kontroller için timer'ı 20s periyodla başlat (sizin isteğe göre)
            _timer = new Timer(CheckLog, null, 20000, 20000);
        }


        protected override void OnStop()
        {
            _timer?.Dispose();
        }




        // Timer callback - sadece normal (sonraki) kontroller için kullanılır
        void CheckLog(object state)
        {
            RunCheck(isInitial: false);
        }

        // Log kontrolünü yapan gerçek method. isInitial==true ise başlangıçta bir kere daha minimal yazabilir.
        void RunCheck(bool isInitial)
        {
            try
            {
                string logPath = GetTodayLogPath();
                if (string.IsNullOrEmpty(logPath))
                {
                    // Log dosyası yok mesajını sadece ilk kontrol sırasında yaz (gereksiz tekrar olmasın)
                    if (isInitial)
                    {
                        File.AppendAllText(
                            @"C:\RestoPOS\debug.txt",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - Log dosyası BULUNAMADI" + Environment.NewLine
                        );
                    }
                    return;
                }

                if (_currentLogDate != DateTime.Today)
                {
                    _currentLogDate = DateTime.Today;
                    _lastPosition = 0;
                }

                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // Eğer daha önce bir pozisyon yoksa (ör. servis yeni başladı) dosyanın tamamını okumak
                    // istemeyiz: sondan sadece küçük bir bölüm oku (tail). Aksi halde eski tüm loglar okunur.
                    if (_lastPosition == 0 && fs.Length > InitialTailBytes)
                    {
                        fs.Seek(fs.Length - InitialTailBytes, SeekOrigin.Begin);
                    }
                    else
                    {
                        // Dosya rotasyonu veya truncation olduysa pozisyonu sıfırla
                        if (_lastPosition > fs.Length)
                            _lastPosition = 0;

                        fs.Seek(_lastPosition, SeekOrigin.Begin);
                    }

                    using (var sr = new StreamReader(fs, Encoding.Default))
                    {
                        string line;
                        DateTime firstErrorSeen = DateTime.MinValue;
                        DateTime lastErrorSeen = DateTime.MinValue;
                        bool anyError = false;
                        string firstErrorLine = null;

                        while ((line = sr.ReadLine()) != null)
                        {
                            if (IsInternetError(line))
                            {
                                if (!anyError)
                                {
                                    firstErrorSeen = DateTime.Now;
                                    firstErrorLine = line;
                                }
                                lastErrorSeen = DateTime.Now;
                                anyError = true;
                                _lastErrorTime = lastErrorSeen;
                                _lastErrorLine = line; // güncel son hata satırı
                            }
                        }

                        // reader'ın base stream pozisyonunu kaydet (internal buffering nedeniyle doğru pozisyon buradan alınır)
                        try
                        {
                            _lastPosition = sr.BaseStream.Position;
                        }
                        catch
                        {
                            _lastPosition = 0;
                        }

                        // Hata bulunduysa: eğer daha önce aktif değilse -> sadece bir kere yaz ve bayrağı aç
                        if (anyError)
                        {
                            if (!_isErrorActive)
                            {
                                _isErrorActive = true;
                                // Hata başlangıcını ve örnek satırı tek sefer yaz
                                File.AppendAllText(
                                    @"C:\RestoPOS\debug.txt",
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - HATA (başlangıç örnek): " + (firstErrorLine ?? _lastErrorLine) + Environment.NewLine
                                );

                                File.AppendAllText(
                                    @"C:\RestoPOS\debug.txt",
                                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - HATA ARALIĞI: " + firstErrorSeen.ToString("HH:mm:ss") + " - " + lastErrorSeen.ToString("HH:mm:ss") + Environment.NewLine
                                );
                            }
                            else
                            {
                                // Zaten aktifse sadece zaman bilgisini güncelle (debug'a yazma)
                                _lastErrorTime = lastErrorSeen;
                            }
                        }
                        else
                        {
                            // Bu turda hata yoksa ve hata daha önce aktif ise probe ile son durum kontrolu yap
                            if (_isErrorActive)
                            {
                                if ((DateTime.Now - _lastProbeTime) >= ProbeInterval)
                                {
                                    _lastProbeTime = DateTime.Now;
                                    bool stillHasErrorInLog = ProbeLogForErrorLines(logPath);
                                    if (!stillHasErrorInLog)
                                    {
                                        // Hata artık yok: temizle, bir kere yaz
                                        _isErrorActive = false;
                                        _lastErrorTime = DateTime.MinValue;
                                        File.AppendAllText(
                                            @"C:\RestoPOS\debug.txt",
                                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - LOG'TA HATA SATIRI BULUNMADI, OK'YA GEÇİLİYOR (probe)" + Environment.NewLine
                                        );
                                    }
                                    // eğer hata halen varsa hiçbirşey yazma (gereksiz tekrarı önlemek için)
                                }
                            }
                        }
                    }

                    UpdateStatus();
                }
            }
            catch (Exception ex)
            {
                // Exception'ları yazmaya devam et (hata bildirimleri için önemli)
                File.AppendAllText(
                    @"C:\RestoPOS\debug.txt",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - EXCEPTION: " + ex.ToString() + Environment.NewLine
                );
            }
        }

        // Log dosyasının son kısmını okuyup belirtilen hata ifadelerinin son kayıtlarda bulunup bulunmadığını kontrol eder.
        // Dosya açılamazsa veya bir hata oluşursa konservatif davranıp 'true' (hata var) döneriz.
        bool ProbeLogForErrorLines(string logPath)
        {
            try
            {
                if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
                    return false;

                // Bu eşiğin dışındaki (yani daha eski) hata satırlarını probe sırasında yok sayacağız.
                var recentThreshold = TimeSpan.FromSeconds(30); // son 30 saniyeyi "yakın" kabul et

                // Tarih parse için regex: "8.01.2026 15:48:54" gibi formatları yakalar
                var dtRegex = new Regex(@"\b\d{1,2}\.\d{1,2}\.\d{4}\s+\d{1,2}:\d{2}:\d{2}\b", RegexOptions.Compiled);

                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = fs.Length > InitialTailBytes ? fs.Length - InitialTailBytes : 0;
                    fs.Seek(start, SeekOrigin.Begin);

                    using (var sr = new StreamReader(fs, Encoding.Default))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (!IsInternetError(line))
                                continue;

                            // Hata satırı bulundu — önce içindeki timestamp'ı parse etmeye çalış
                            var m = dtRegex.Match(line);
                            if (m.Success)
                            {
                                DateTime parsed;
                                // Türkçe biçim (gün.ay.yıl saat:dakika:saniye) ile parse etmeye çalış
                                if (DateTime.TryParseExact(m.Value,
                                        new[] { "d.M.yyyy H:mm:ss", "d.MM.yyyy H:mm:ss", "dd.MM.yyyy HH:mm:ss", "d.M.yyyy HH:mm:ss" },
                                        CultureInfo.GetCultureInfo("tr-TR"),
                                        DateTimeStyles.None,
                                        out parsed))
                                {
                                    // Eğer bulunan tarih 'yakın' tarih aralığında değilse bu satırı yok say
                                    if ((DateTime.Now - parsed) > recentThreshold)
                                        continue; // eski hata satırı, probe için önemsiz
                                    else
                                        return true; // yakın zamanda oluşmuş hata satırı => hala hata
                                }
                                else
                                {
                                    // Tarih parse edilemediyse tedbirli davran: bu satırı hata olarak kabul et
                                    return true;
                                }
                            }
                            else
                            {
                                // Satırda tarih yoksa konservatif davran: hata olarak kabul et
                                return true;
                            }
                        }
                    }
                }

                // Son 8KB içinde yakın zamanda oluşmuş herhangi bir hata satırı bulunmadı
                return false;
            }
            catch
            {
                // Belirsizlik varsa hatanın devam ettiğini varsay (yanlış temizlemeyi engelle)
                return true;
            }
        }









        //bugünkü log dosyasını bulur

        string GetTodayLogPath()
        {
            string[] folders =
            {
                @"C:\RestoPOS\MY\LOG\",
                @"D:\RestoPOS\MY\LOG\"
            };

            string fileName = $"RCGuard_{DateTime.Now:yyyyMMdd}.txt";

            foreach (var folder in folders)
            {
                string fullPath = Path.Combine(folder, fileName);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return null;
        }


        //Logtaki yazım ne olursa olsun, StringComparison.OrdinalIgnoreCase=-Ordinal-Harfleri tek tek ASCII/Unicode değerine göre karşılaştır
        //IgnoreCase=Büyük/küçük harf duyarsız

        bool IsInternetError(string line)
        {
            return line.IndexOf("No such host is known", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("Bilinen böyle bir ana bilgisayar yok", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("A connection attempt failed", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("connected host has failed to respond", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("DataBase Baglantisi Koptu!(Disconnect)", StringComparison.OrdinalIgnoreCase) >= 0;

        }



        //STATUS.TXT YAZAN METHOD



        void UpdateStatus()
        {
            // Artık doğrudan hata bayrağına göre status belirleniyor.
            string status = _isErrorActive ? "ERROR" : "OK";

            Directory.CreateDirectory(@"C:\RestoPOS");

            // Sadece status değiştiyse dosyaya yaz — böylece tray uygulaması gereksiz dosya değişiklikleri
            // nedeniyle yeşil/kırmızı hızlı yanıp sönmelerden kaçınır.
            if (_lastStatus != status)
            {
                File.WriteAllText(
                    @"C:\RestoPOS\status.txt",
                    status,
                    Encoding.ASCII
                );

                _lastStatus = status;
            }
        }







    }
}