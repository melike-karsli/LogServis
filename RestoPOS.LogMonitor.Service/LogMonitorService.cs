using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Configuration;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace RestoPOS.LogMonitor.Service
{
    public partial class LogMonitorService : ServiceBase
    {
        // --- AYARLAR VE DEĞİŞKENLER ---
        private System.Threading.Timer _timer;
        private bool _isErrorActive = false;
        private string _lastStatus = null;
        private string _lastErrorLine = null;
        private DateTime _currentLogDate = DateTime.Today;
        private DateTime _lastLogCheck = DateTime.MinValue;
        private readonly TimeSpan LogCheckInterval = TimeSpan.FromSeconds(30);

        private const string DebugPath = @"C:\RestoPOS\debug.txt";
        private const string StatusPath = @"C:\RestoPOS\status.txt";

        public LogMonitorService() => InitializeComponent();

        protected override void OnStart(string[] args) => ManualStart();

        public void ManualStart()
        {
            LogToDebug("Servis başlatılıyor...");
            RunCheck(isInitial: true); // İlk açılış kontrolü
            _timer = new System.Threading.Timer(obj => RunCheck(false), null, 15000, 15000); // 15 sn'de bir sinyal
        }

        protected override void OnStop() => ManualStop();

        public void ManualStop()
        {
            LogToDebug("Servis durduruluyor.");
            _timer?.Dispose();
        }

        // --- ANA MANTIK AKIŞI ---


       
        private void RunCheck(bool isInitial)
        {
            Directory.CreateDirectory(@"C:\RestoPOS");
            try
            {
                // 1. Periyodik Log Kontrolü (Sadece 2 dakikada bir)
                if (isInitial || (DateTime.Now - _lastLogCheck) >= LogCheckInterval)
                {
                    string logPath = GetTodayLogPath();
                    if (string.IsNullOrEmpty(logPath))
                    {
                        if (isInitial) LogToDebug("Log dosyası henüz oluşmamış. Durum: OK");
                        _isErrorActive = false;
                    }
                    else
                    {
                        _isErrorActive = IsRecentErrorActive(logPath);
                    }
                    _lastLogCheck = DateTime.Now;
                    UpdateStatusFile();
                }

                // 2. Her zaman sinyal (Heartbeat) gönder (15 saniyede bir)
                SendHeartbeat();
            }
            catch (Exception ex) { LogToDebug($"KRİTİK HATA (RunCheck): {ex.Message}"); }
        }

        private bool IsRecentErrorActive(string logPath)
        {
            try
            {
                if (!File.Exists(logPath)) return false;

                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // Sadece son 2KB'ı okuyoruz (Hızlı tepki için kullanıcının isteği)
                    long start = fs.Length > 2 * 1024 ? fs.Length - 2 * 1024 : 0;
                    fs.Seek(start, SeekOrigin.Begin);

                    using (var sr = new StreamReader(fs, Encoding.Default))
                    {
                        string line;
                        bool foundErrorInLast2K = false;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (IsInternetError(line))
                            {
                                // Son 2KB içinde herhangi bir hata satırı varsa ERROR ver
                                _lastErrorLine = line; 
                                foundErrorInLast2K = true;
                            }
                        }

                        if (foundErrorInLast2K)
                        {
                            LogToDebug($"AKTİF HATA (Son 2KB içinde): {_lastErrorLine}");
                            return true;
                        }
                    }
                }
                if (_isErrorActive) LogToDebug("HATA GİDERİLDİ: Son 2KB'lık loglar temiz.");
                return false; 
            }
            catch (Exception ex)
            {
                LogToDebug($"Log okuma hatası: {ex.Message}");
                return _isErrorActive; 
            }
        }

        private bool IsInternetError(string line)
        {
            string[] keywords = { 
                "No such host", "ana bilgisayar yok", "connection attempt failed", 
                "failed to respond", "DataBase Baglantisi Koptu",
                "Access violation", "Adisyon Kayıt Hata"
            };
            return keywords.Any(k => line.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void UpdateStatusFile()
        {
            string currentStatus = _isErrorActive ? "ERROR" : "OK";
            if (_lastStatus == currentStatus) return;

            Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
            File.WriteAllText(StatusPath, currentStatus, Encoding.ASCII);
            _lastStatus = currentStatus;
            
            LogToDebug($"DURUM DEĞİŞTİ -> {currentStatus}");
        }

        private void HandleDayChange()
        {
            // Artık pencere bazlı tasıma yaptığımız için gün değişiminde pos sıfırlamaya gerek yok
            // Ama tarih bilgisini başka yerde kullanıyorsak kalabilir.
            if (_currentLogDate != DateTime.Today)
            {
                _currentLogDate = DateTime.Today;
            }
        }

        private void LogToDebug(string message)
        {
            string log = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}{Environment.NewLine}";
            File.AppendAllText(DebugPath, log);
        }

        //private string GetTodayLogPath()
        //{
        //    string fileName = $"RCGuard_{DateTime.Now:yyyyMMdd}.txt";

        //    // Client'ın ağ adresi (Örn: 192.168.1.50 veya KASA01)
        //    string clientIP = "192.168.1.155";

        //    // Server artık kendi C sürücüsüne değil, ağdaki Client'a bakıyor
        //    string remotePath = $@"\\{clientIP}\RestoPOS\MY\LOG\{fileName}";

        //    return File.Exists(remotePath) ? remotePath : null;
        //}


        private string GetTodayLogPath()

        {

            string fileName = $"RCGuard_{DateTime.Now:yyyyMMdd}.txt";

            string[] paths = { $@"C:\RestoPOS\MY\LOG\{fileName}", $@"D:\RestoPOS\MY\LOG\{fileName}" };

            return paths.FirstOrDefault(File.Exists);
        }

        private void SendHeartbeat()
        {
            try
            {
                string serverIP = ConfigurationManager.AppSettings["ServerIP"] ?? "127.0.0.1";
                int serverPort = int.Parse(ConfigurationManager.AppSettings["ServerPort"] ?? "5000");
                string message = _isErrorActive ? "ERROR" : "OK";

                using (UdpClient client = new UdpClient())
                {
                    byte[] data = Encoding.ASCII.GetBytes(message);
                    client.Send(data, data.Length, serverIP, serverPort);
                    // Başarılı gönderimi logla (sadece hata ayıklama için geçici olabilir)
                    LogToDebug($"Sinyal Gönderildi: {message} -> {serverIP}:{serverPort}");
                }
            }
            catch (Exception ex)
            {
                LogToDebug($"UDP Sinyal Hatası: {ex.Message}");
            }
        }
    }
}
