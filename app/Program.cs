using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("SwarlexEasyDpi")]
[assembly: System.Reflection.AssemblyProduct("SwarlexEasyDpi")]
[assembly: System.Reflection.AssemblyDescription("GoodbyeDPI tabanli DPI engeli asma araci")]
// Yonetici olarak calisirken DllImport'lar uygulama klasorunden (or. Indirilenler) DLL yuklemesin.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace SwarlexEasyDpi
{
    internal static class Program
    {
        // Oturum basina: baska kullanicinin onceden olusturdugu Global\ nesnesi uygulamayi engelleyemesin.
        const string MutexName = @"Local\SwarlexEasyDpi";
        const string ShowName = @"Local\SwarlexEasyDpi.Show";

        [STAThread]
        static int Main(string[] args)
        {
            // Bundan sonra yuklenen DLL'ler yalniz System32'den.
            try { SetDefaultDllDirectories(0x800); } catch (Exception) { }
            // TLS 1.2/1.3 her ag isteginden once (elle "guncellemeleri denetle" acilistan hemen sonra olabilir)
            Engine.ConfigureNet();

            bool silent = Array.IndexOf(args, "--tray") >= 0;
            // Guncellemeden sonra eski surum kapanana kadar bekle (kilidi o tutuyor).
            bool afterUpdate = Array.IndexOf(args, "--after-update") >= 0;
            if (afterUpdate) silent = true;
            bool created;
            var mutex = new Mutex(false, MutexName);
            try { created = mutex.WaitOne(afterUpdate ? 30000 : 0); }
            catch (AbandonedMutexException) { created = true; } // onceki ornek kilidi birakmadan kapandi
            // "goster" olayi kilitten sonra: guncellemede eskisinin olayi o zamana kadar kapanmis olur.
            bool showCreated;
            using (var show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName, out showCreated))
            {
                if (!created)
                {
                    // Zaten calisiyor: paneli one getir (one gecebilmesi icin izin ver).
                    AllowSetForegroundWindow(-1);
                    show.Set();
                    return 0;
                }
                if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                {
                    MessageBox.Show("SwarlexEasyDpi yönetici yetkisi ister (WinDivert sürücüsü için).", "SwarlexEasyDpi",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 1;
                }

                // Indirilenler gibi yazilabilir bir klasorden calisiyorsa kendini Program Files'a
                // kopyalayip oradan yeniden baslat; yonetici sureci o klasordeki dosyalara guvenmesin.
                if (!RunningInstalled())
                {
                    try
                    {
                        Directory.CreateDirectory(Paths.Dir);
                        byte[] me = ReadSelf();
                        bool same = File.Exists(Paths.InstalledExe) && SameBytes(File.ReadAllBytes(Paths.InstalledExe), me);
                        // Indirilenler'deki eski bir kopya, kendini guncellemis yeni kurulumun ustune yazmasin.
                        bool newer = File.Exists(Paths.InstalledExe) && InstalledVersion() > Updater.Current;
                        if (newer) Log.Write("Program Files'taki surum daha yeni; o calistiriliyor");
                        if (!same && !newer) File.WriteAllBytes(Paths.InstalledExe, me);
                        mutex.ReleaseMutex();
                        mutex.Dispose();
                        mutex = null;
                        show.Dispose(); // yeni kopya "goster" olayini kendisi olusturabilsin
                        AllowSetForegroundWindow(-1); // yeni kopyanin paneli one gecebilsin
                        var psi = new ProcessStartInfo(Paths.InstalledExe, silent ? "--tray" : "")
                        { UseShellExecute = false, WorkingDirectory = Paths.Dir };
                        Process.Start(psi).Dispose();
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Program Files'a kurulamadi, yerinde calisiyor: " + ex.Message);
                        if (mutex == null) return 1;
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.Write("Beklenmeyen hata: " + e.ExceptionObject); };
                // WPF dongusu (panel animasyonlari); WinForms tepsi simgesi ayni is parcaciginda calisir.
                var wpf = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                wpf.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                {
                    Log.Write("Beklenmeyen hata: " + e.Exception);
                    e.Handled = true;
                };
                // Biz ilk ornegiz ama "goster" olayi zaten varsa onu baska bir surec olusturmus:
                // (or. surekli isaretli birakilmis) kullanma.
                if (!showCreated) Log.Write("Uyari: goster olayi baska bir surec tarafindan olusturulmus; yok sayiliyor");
                using (var app = new TrayApp(silent, showCreated ? show : null))
                {
                    app.Exit += delegate { wpf.Shutdown(); };
                    wpf.Run();
                }
                GC.KeepAlive(mutex);
                show.Dispose(); // once olay, sonra kilit: guncellenmis yeni ornek olayi kendisi olusturabilsin
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
                mutex.Dispose();
                return 0;
            }
        }

        static bool RunningInstalled()
        {
            return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Paths.InstalledExe), StringComparison.OrdinalIgnoreCase);
        }

        // Calisan exe'nin kendisini okur. Calisan bir exe yeniden adlandirilip yerine baska dosya
        // konabilir; okunan dosyanin bellekteki goruntuyle ayni dosya oldugu (NT yolu) dogrulanir.
        static byte[] ReadSelf()
        {
            using (var fs = new FileStream(Application.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var opened = new StringBuilder(1024);
                if (GetFinalPathNameByHandle(fs.SafeFileHandle, opened, opened.Capacity, 0x2) == 0) throw new IOException("dosya yolu alinamadi");
                var mapped = new StringBuilder(1024);
                if (K32GetMappedFileName(Process.GetCurrentProcess().Handle, GetModuleHandle(null), mapped, mapped.Capacity) == 0) throw new IOException("goruntu yolu alinamadi");
                if (!string.Equals(opened.ToString(), mapped.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("calisan exe diskteki dosyayla ayni degil");
                var ms = new MemoryStream();
                fs.CopyTo(ms);
                return ms.ToArray();
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle h, StringBuilder path, int len, int flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern int K32GetMappedFileName(IntPtr proc, IntPtr addr, StringBuilder name, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        static Version InstalledVersion()
        {
            try { return Updater.Norm(FileVersionInfo.GetVersionInfo(Paths.InstalledExe).ProductVersion) ?? new Version(0, 0, 0); }
            catch (Exception) { return new Version(0, 0, 0); }
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        [DllImport("kernel32.dll")] static extern bool SetDefaultDllDirectories(uint flags);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    }
}
