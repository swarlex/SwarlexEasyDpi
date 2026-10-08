// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace SwarlexEasyDpi
{
    internal static class Paths
    {
        // Program Files yalniz yoneticinin yazabildigi bir yer; yonetici olarak calisan
        // dosyalari normal kullanici surecleri degistiremez. Ortam degiskeni degil bilinen klasor kaydi.
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "SwarlexEasyDpi");
        public static readonly string Bin = Path.Combine(Dir, "bin");
        public static readonly string Settings = Path.Combine(Dir, "ayarlar.ini");
        public static readonly string LogFile = Path.Combine(Dir, "kayit.txt");
        public static readonly string InstalledExe = Path.Combine(Dir, "SwarlexEasyDpi.exe");
        public static readonly string Gdpi = Path.Combine(Bin, "goodbyedpi.exe");
    }

    internal static class Log
    {
        static readonly object gate = new object();

        public static void Write(string msg)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg;
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Paths.Dir);
                    var fi = new FileInfo(Paths.LogFile);
                    if (fi.Exists && fi.Length > 512 * 1024) File.Delete(Paths.LogFile);
                    File.AppendAllText(Paths.LogFile, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (Exception) { }
            }
        }
    }

    // GoodbyeDPI ayarlari: Turkiye'de calistigi bilinen en iyi 4 tanesi (tm4to/easydpi'nin listesi +
    // cagritaskn). Once DNS'e dokunmayan; sonra ayni ayarin DNS yonlendirmeli hali (ISS DNS'i
    // zehirliyse gerekir, duz DNS trafigini o sunucuya yollar). Sifresiz/standart disi portlu Yandex
    // cikarildi (bazi aglarda 1253. port kapali, tum DNS'i kiriyordu). easydpi'deki
    // "--dnsv6-port 1111" hatasi (Cloudflare o portta DNS sunmaz) duzeltildi.
    internal static class Configs
    {
        public static readonly string[] List = {
            "-9 --set-ttl 7",
            "-9 --set-ttl 7 --dns-addr 9.9.9.9",                                  // easydpi varsayilan
            "-5 --set-ttl 5",                                                     // cagritaskn
            "-5 --set-ttl 5 --dns-addr 1.1.1.1 --dnsv6-addr 2606:4700:4700::1111",
        };

        public static List<string> Args(int i) { return new List<string>(List[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)); }

        // DNS yonlendiriyorsa hangi sunucuya (ipucu metninde gosterilir), yoksa null.
        public static string DnsName(int i)
        {
            string c = List[i];
            if (c.Contains("9.9.9.9")) return "Quad9";
            if (c.Contains("1.1.1.1")) return "Cloudflare";
            return null;
        }
    }

    internal sealed class Settings
    {
        public int Config = 0;            // en son calisan ayar (Configs.List sirasi)
        public bool AutoConnect = true;   // acilista ve ag degisince baglan
        public string Lang;               // null = Windows'un dili
        const int Version = 3;            // ayar listesi degisince eski indeks gecersiz

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(Paths.Settings)) return s;
                var kv = new Dictionary<string, string>();
                foreach (string raw in File.ReadAllLines(Paths.Settings, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq > 0) kv[raw.Substring(0, eq).Trim()] = raw.Substring(eq + 1).Trim();
                }
                int n; string v;
                if (kv.TryGetValue("version", out v) && v == Version.ToString())
                {
                    if (kv.TryGetValue("config", out v) && int.TryParse(v, out n) && n >= 0 && n < Configs.List.Length) s.Config = n;
                }
                else
                {
                    // eski bicim: bir kez tasi ve yeni bicimde yaz (proxy geri alma her acilista tekrarlanmasin)
                    MigrateV1(kv);
                    s.Save();
                }
                if (kv.TryGetValue("auto_connect", out v)) s.AutoConnect = v != "0";
                if (kv.TryGetValue("lang", out v) && Strings.Has(v)) s.Lang = v;
            }
            catch (Exception ex) { Log.Write("Ayarlar okunamadi: " + ex.Message); }
            return s;
        }

        // Ilk surum ByeDPI modunda sistem proxy'sini degistirebiliyordu: yarim kaldiysa geri al.
        static void MigrateV1(Dictionary<string, string> kv)
        {
            string set;
            if (!kv.TryGetValue("proxy_set", out set) || set != "1") return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    string en, srv, ovr; int e;
                    k.SetValue("ProxyEnable", kv.TryGetValue("proxy_old_enable", out en) && int.TryParse(en, out e) ? e : 0, RegistryValueKind.DWord);
                    if (kv.TryGetValue("proxy_old_server", out srv) && srv.Length > 0) k.SetValue("ProxyServer", srv); else k.DeleteValue("ProxyServer", false);
                    if (kv.TryGetValue("proxy_old_override", out ovr) && ovr.Length > 0) k.SetValue("ProxyOverride", ovr); else k.DeleteValue("ProxyOverride", false);
                }
                Log.Write("Eski surumden kalan proxy ayari geri alindi");
            }
            catch (Exception ex) { Log.Write("Eski proxy ayari geri alinamadi: " + ex.Message); }
        }

        static readonly object saveGate = new object();

        public void Save()
        {
            try
            {
                lock (saveGate)
                {
                    Directory.CreateDirectory(Paths.Dir);
                    File.WriteAllText(Paths.Settings,
                        "version=" + Version + Environment.NewLine +
                        "config=" + Config + Environment.NewLine +
                        "auto_connect=" + (AutoConnect ? "1" : "0") + Environment.NewLine +
                        (Lang != null ? "lang=" + Lang + Environment.NewLine : ""), Encoding.UTF8);
                }
            }
            catch (Exception ex) { Log.Write("Ayarlar kaydedilemedi: " + ex.Message); }
        }
    }

    internal enum State { Off, Connecting, On, Unverified, Error }

    // Arayuzun dile gore metin uretmesi icin durum ayrintisi.
    internal sealed class Info
    {
        public int Try, Total;     // baglanirken: kacinci ayar
        public string Dns;         // DNS yonlendiriliyorsa sunucu adi
        public string Error;       // dil anahtari (errStart / errStopped) ya da ham hata metni
    }

    // Gomulu GoodbyeDPI'yi kurar, calistirir, dogrular ve izler.
    internal sealed class Engine
    {
        // Dogrulama: tm4to/easydpi gibi Discord'un acik API'si 200 donmeli (DNS + TLS + DPI ucu birden).
        const string ProbeUrl = "https://discord.com/api/v10/gateway";

        static readonly string[] Files = { "goodbyedpi.exe", "WinDivert.dll", "WinDivert64.sys" };

        readonly object gate = new object();
        readonly Settings settings;
        Process proc;
        int gen;            // her Connect/Stop'ta artar; eski arka plan isleri kendini iptal eder
        int crashes;
        DateTime startedAt;
        State current = State.Off;

        public event Action<State, Info> Changed;   // kilit altinda cagrilir, bloklamamali

        public Engine(Settings s) { settings = s; }

        public State Current { get { lock (gate) return current; } }

        public bool Active { get { var c = Current; return c == State.On || c == State.Unverified || c == State.Connecting; } }

        // ---- kurulum ----

        public static void InstallBinaries()
        {
            Directory.CreateDirectory(Paths.Bin);
            using (var sha = SHA256.Create())
            {
                foreach (var f in Files)
                {
                    byte[] data = Resource(f);
                    string dst = Path.Combine(Paths.Bin, f);
                    if (File.Exists(dst) && Same(sha, File.ReadAllBytes(dst), data)) continue;
                    File.WriteAllBytes(dst, data);
                    if (!Same(sha, File.ReadAllBytes(dst), data)) throw new IOException(f + " yazilamadi");
                    Log.Write("Kuruldu: " + dst);
                }
            }
            // ilk surumden kalanlar (artik kullanilmiyor)
            foreach (var old in new[] { "ciadpi.exe", "doh.ps1" })
            {
                string p = Path.Combine(Paths.Bin, old);
                try { if (File.Exists(p)) { File.Delete(p); Log.Write("Eski dosya silindi: " + p); } } catch (Exception) { }
            }
        }

        static byte[] Resource(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) throw new FileNotFoundException("Gomulu kaynak yok: " + name);
                var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }

        static bool Same(HashAlgorithm h, byte[] a, byte[] b)
        {
            return a.Length == b.Length && Convert.ToBase64String(h.ComputeHash(a)) == Convert.ToBase64String(h.ComputeHash(b));
        }

        // Eski GoodbyeDPI servisi/surecleri WinDivert'i tutuyorsa cakisir.
        public static void StopForeign()
        {
            try
            {
                using (var sc = new System.ServiceProcess.ServiceController("GoodbyeDPI"))
                    if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                    {
                        Log.Write("Cakisan GoodbyeDPI servisi durduruluyor");
                        sc.Stop();
                        sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
            }
            catch (InvalidOperationException) { }
            catch (Exception ex) { Log.Write("GoodbyeDPI servisi durdurulamadi: " + ex.Message); }
            foreach (var p in Process.GetProcessesByName("goodbyedpi"))
            {
                try { Log.Write("Cakisan goodbyedpi kapatiliyor (" + p.Id + ")"); p.Kill(); p.WaitForExit(3000); }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
        }

        // ---- surec ----

        public static string Quote(string s)
        {
            if (s.Length > 0 && s.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return s;
            var sb = new StringBuilder("\"");
            int bs = 0;
            foreach (char c in s)
            {
                if (c == '\\') { bs++; continue; }
                if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); }
                else { sb.Append('\\', bs); sb.Append(c); }
                bs = 0;
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        // Konsol penceresi acmadan baslatir (easydpi "start" ile gorunur pencere aciyordu).
        static Process Spawn(string exe, IEnumerable<string> args)
        {
            var parts = new List<string>();
            foreach (string a in args) parts.Add(Quote(a));
            var psi = new ProcessStartInfo(exe, string.Join(" ", parts))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe),
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Start();
            Job.Add(p);
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Log.Write("  goodbyedpi: " + e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        // Uygulama icinde HTTP istegi: harici curl yok (yonetici olarak calisan curl, kullanicinin
        // yazabildigi %APPDATA%\_curlrc dosyasini okurdu). Proxy kullanilmaz; DNS onbellegi tutulmaz
        // ki ayar degisince eski (zehirli) cevap kullanilmasin.
        public static bool Probe()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(ProbeUrl);
                req.Proxy = null;
                req.KeepAlive = false;
                req.Timeout = 4000; req.ReadWriteTimeout = 4000; // hizli secim: engelli ayar 4 sn'de elenir
                req.AllowAutoRedirect = false;
                req.UserAgent = "SwarlexEasyDpi";
                var t = System.Threading.Tasks.Task.Run(() => { using (var resp = (HttpWebResponse)req.GetResponse()) return (int)resp.StatusCode == 200; });
                // DNS cozumleme req.Timeout'a dahil degil (15 sn'ye kadar surebilir): toplam sureyi sinirla
                if (!t.Wait(5000)) { req.Abort(); Log.Write("  dogrulama: zaman asimi"); return false; }
                return t.Result;
            }
            catch (AggregateException ae)
            {
                var ex = ae.InnerException as WebException;
                var r = ex != null ? ex.Response as HttpWebResponse : null;
                Log.Write("  dogrulama: " + (r != null ? ((int)r.StatusCode).ToString() : ex != null ? ex.Status.ToString() : ae.InnerException.Message));
                if (r != null) r.Close();
                return false;
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                Log.Write("  dogrulama: " + (r != null ? ((int)r.StatusCode).ToString() : ex.Status.ToString()));
                if (r != null) r.Close();
                return false;
            }
            catch (Exception ex) { Log.Write("  dogrulama hatasi: " + ex.Message); return false; }
        }

        public static void ConfigureNet()
        {
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; } // + TLS 1.3
            catch (NotSupportedException) { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
            ServicePointManager.DnsRefreshTimeout = 0;
        }

        [DllImport("dnsapi.dll")] static extern bool DnsFlushResolverCache();

        // ---- baglan / kes ----

        // Kayitli ayardan baslayarak her ayari dener; Discord yanit verince orada kalir.
        // user: kullanici istedi (cokme sayaci sifirlanir) / otomatik yeniden baglanma.
        public void Connect(bool user) { Connect(user, -1); }

        // expected >= 0: yalniz nesil hala buysa (otomatik yeniden baglanma; arada Kapat/Bagla olduysa vazgec)
        void Connect(bool user, int expected)
        {
            int my;
            lock (gate)
            {
                if (expected >= 0 && expected != gen) return;
                my = ++gen; if (user) crashes = 0; KillLocked();
            }
            try { ConnectInner(my); }
            catch (Exception ex)
            {
                Log.Write("Baglanti hatasi: " + ex);
                lock (gate) { if (my == gen) KillLocked(); }
                Raise(my, State.Error, new Info { Error = ex.Message });
            }
        }

        void ConnectInner(int my)
        {
            Raise(my, State.Connecting, new Info());
            var order = new List<int> { settings.Config };
            for (int i = 0; i < Configs.List.Length; i++) if (i != settings.Config) order.Add(i);

            for (int n = 0; n < order.Count; n++)
            {
                int c = order[n];
                if (!Alive(my)) return;
                Raise(my, State.Connecting, n == 0 ? new Info() : new Info { Try = n + 1, Total = order.Count });
                Log.Write("Deneniyor: " + Configs.List[c]);
                if (!StartConfig(my, c)) continue;
                Thread.Sleep(1200); // WinDivert filtresi ~1 sn icinde devrede
                if (!Running(my)) { Log.Write("goodbyedpi kapandi"); continue; }
                try { DnsFlushResolverCache(); } catch (Exception) { }
                if (!Probe()) { Log.Write("Dogrulanamadi: " + Configs.List[c]); continue; }
                if (settings.Config != c) { settings.Config = c; settings.Save(); }
                string dns = Configs.DnsName(c);
                // dogrulama surerken surec olmus olabilir: kontrol ve durum ayni kilit altinda
                // (arada olurse HandleExit "baglaniyor" gorup sessiz kalirdi)
                lock (gate)
                {
                    if (my != gen) return;
                    if (proc == null || proc.HasExited) { Log.Write("goodbyedpi dogrulamadan sonra kapandi"); continue; }
                    Raise(my, State.On, new Info { Dns = dns });
                }
                Log.Write("Baglandi: " + Configs.List[c]);
                return;
            }

            // Hicbiri dogrulanamadi (internet yok ya da Discord kapali): kayitli ayarla acik kal.
            if (!Alive(my)) return;
            if (StartConfig(my, settings.Config))
            {
                Thread.Sleep(1000);
                string dns = Configs.DnsName(settings.Config);
                lock (gate)
                {
                    if (my != gen) return;
                    if (proc != null && !proc.HasExited)
                    {
                        Raise(my, State.Unverified, new Info { Dns = dns });
                        Log.Write("Hicbir ayar dogrulanamadi; kayitli ayarla acik");
                        return;
                    }
                }
            }
            Raise(my, State.Error, new Info { Error = "errStart" });
        }

        bool StartConfig(int my, int c)
        {
            lock (gate)
            {
                if (my != gen) return false;
                KillLocked();
                try
                {
                    proc = Spawn(Paths.Gdpi, Configs.Args(c));
                    startedAt = DateTime.Now;
                    proc.Exited += OnExited;
                    return true;
                }
                catch (Exception ex) { Log.Write("goodbyedpi baslatilamadi: " + ex.Message); proc = null; return false; }
            }
        }

        bool Alive(int my) { lock (gate) return my == gen; }

        bool Running(int my)
        {
            Process p;
            lock (gate) { if (my != gen) return false; p = proc; }
            // HasExited kilit disinda: .NET Framework Exited olayini cagiran is parcaciginda tetikleyebilir.
            return p != null && !p.HasExited;
        }

        // Process'in kendi kilidi tutulurken gate'i beklememek icin is kuyruga alinir (kilitlenme onlemi).
        void OnExited(object sender, EventArgs e)
        {
            ThreadPool.QueueUserWorkItem(delegate { HandleExit((Process)sender); });
        }

        void HandleExit(Process sender)
        {
            int my, n;
            lock (gate)
            {
                if (sender != proc) return; // bilerek kapatildi ya da degistirildi (KillLocked birakir)
                proc = null;
                my = gen;
                if (current != State.On && current != State.Unverified) { sender.Dispose(); return; } // deneme sirasinda: Connect ilerler
                int code = -1; try { code = sender.ExitCode; } catch (Exception) { }
                Log.Write("goodbyedpi beklenmedik sekilde kapandi (kod " + code + ")");
                sender.Dispose();
                if ((DateTime.Now - startedAt).TotalMinutes > 5) crashes = 0;
                n = ++crashes;
            }
            if (n <= 3)
                ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(1500); Connect(false, my); });
            else Raise(my, State.Error, new Info { Error = "errStopped" });
        }

        public void Stop()
        {
            int my;
            lock (gate) { my = ++gen; crashes = 0; KillLocked(); }
            Raise(my, State.Off, new Info());
        }

        void KillLocked()
        {
            var p = proc;
            proc = null;
            if (p == null) return;
            // Exited olayi kuyruga alindigi icin burada beklemek kilitlenme yaratmaz.
            try { if (!p.HasExited) { p.Kill(); p.WaitForExit(5000); } } catch (Exception) { }
            p.Dispose();
        }

        // Durum ve bildirim ayni kilit altinda: gec kalan eski bir "Baglaniyor" yeni "Kapali"nin ustune yazamaz.
        // Ag degisti / uykudan uyanildi: aciksa baglantiyi yeniden dogrula, gerekirse yeniden bagla.
        public void Recheck()
        {
            int my; State was;
            lock (gate) { if (current != State.On && current != State.Unverified) return; my = gen; was = current; }
            try { DnsFlushResolverCache(); } catch (Exception) { }
            if (Probe())
            {
                Log.Write("Ag degisikliginden sonra baglanti saglam");
                if (was == State.Unverified)
                    lock (gate)
                        if (my == gen && current == State.Unverified && proc != null && !proc.HasExited)
                            Raise(my, State.On, new Info { Dns = Configs.DnsName(settings.Config) });
                return;
            }
            Log.Write("Ag degisikliginden sonra baglanti yok; yeniden baglaniliyor");
            Connect(false, my);
        }

        void Raise(int my, State st, Info info)
        {
            lock (gate)
            {
                if (my != gen) return;
                current = st;
                var h = Changed;
                if (h != null) h(st, info);
            }
        }
    }
}
