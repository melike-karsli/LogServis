using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace RestoPOS.LogMonitor.Service
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main()
        {
            if (Environment.UserInteractive)
            {
                // Standalone / Manual test mode
                LogMonitorService service = new LogMonitorService();
                service.ManualStart();
                Console.WriteLine("RestoPOS Log Monitor TEST modunda çalışıyor.");
                
                // WinExe projelerinde konsol her zaman girdi kabul etmeyebilir.
                // Servisi açık tutmak için bir mesaj kutusu çıkarıyoruz.
                System.Windows.Forms.MessageBox.Show(
                    "Log Monitor Servisi şu an TEST modunda çalışıyor.\n\n" +
                    "Snyal gönderimi devam ediyor.\n\n" +
                    "Durdurmak için 'Tamam' butonuna basın.", 
                    "RestoPOS Servis Kontrol", 
                    System.Windows.Forms.MessageBoxButtons.OK, 
                    System.Windows.Forms.MessageBoxIcon.Information);
                
                service.ManualStop();
            }
            else
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new LogMonitorService()
                };
                ServiceBase.Run(ServicesToRun);
            }
        }
    }
}
