// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Threading;
using Microsoft.Win32;

namespace SwarlexEasyDpi
{
    // Tepsi simgesi + panel/menu + motor + guncelleme.
    internal sealed class TrayApp : IDisposable
    {
        const string TaskName = "SwarlexEasyDpi";

        readonly System.Windows.Threading.Dispatcher ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        readonly Settings settings = Settings.Load();
        readonly Engine engine;
        readonly Flyout flyout;
        readonly Tray tray;
        readonly System.Windows.Threading.DispatcherTimer updateTimer, netTimer;
        bool ready, starting, exiting, autostart, langOpen;
        readonly ManualResetEvent quit = new ManualResetEvent(false);
        Thread waiter;
        DateTime lastToggle = DateTime.MinValue;
        State state = State.Connecting;
        Info info = new Info { Error = "prep" };

        // guncelleme durumu
        ReleaseInfo update;               // daha yeni surum varsa
        string updateBusy;                // null | "check" | "install"
        string lastResult;                // null | "uptodate" | "error"
        DateTime? lastCheck;

        public event Action Exit;

        // showSignal null olabilir: baska bir surec "goster" olayini onceden olusturduysa kullanilmaz.
        public TrayApp(bool silent, EventWaitHandle showSignal)
        {
            Strings.Load(settings.Lang ?? Strings.Default());
            engine = new Engine(settings);
            flyout = new Flyout();
            tray = new Tray();
            engine.Changed += delegate(State st, Info i) { Ui(delegate { OnState(st, i); }); };
            flyout.ToggleClicked += Toggle;
            flyout.MenuItems = MenuItems;
            tray.LeftClick += delegate { flyout.TogglePanel(IconPoint(true)); };
            tray.RightClick += delegate { flyout.ToggleMenu(IconPoint(true)); };
            // cift tikta ikinci basis paneli kapatir: acik birak
            tray.DoubleClick += delegate { if (flyout.Open != "panel") flyout.ShowPanel(IconPoint(true)); };

            // Iptal edilebilen SessionEnding degil, kesinlesen kapanis.
            SystemEvents.SessionEnded += delegate { Shutdown(); };
            // Uykudan uyaninca ya da ag degisince baglantiyi dogrula (otomatik baglan aciksa).
            SystemEvents.PowerModeChanged += delegate(object s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Ui(NetChanged); };
            NetworkChange.NetworkAddressChanged += delegate { Ui(NetChanged); };
            netTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            netTimer.Tick += delegate
            {
                netTimer.Stop();
                if (!ready || !settings.AutoConnect) return;
                if (engine.Current == State.Connecting) { netTimer.Start(); return; } // baglanti bitince yeniden dene
                if (engine.Active) ThreadPool.QueueUserWorkItem(delegate { if (!exiting) engine.Recheck(); });
            };

            updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            updateTimer.Tick += delegate { updateTimer.Interval = TimeSpan.FromHours(6); CheckUpdates(false); };
            updateTimer.Start();

            if (showSignal != null)
            {
                // Dispose'ta durdurulur: olay tutamaci acik kalirsa guncellenen yeni surum olayi olusturamaz.
                var handles = new WaitHandle[] { quit, showSignal };
                waiter = new Thread(delegate()
                {
                    while (WaitHandle.WaitAny(handles) == 1)
                    {
                        Ui(delegate { flyout.ShowPanel(IconPoint(false)); });
                        if (quit.WaitOne(500)) break; // surekli isaretli birakilan olay paneli sel gibi acmasin
                    }
                }) { IsBackground = true };
                waiter.Start();
            }

            Log.Write("SwarlexEasyDpi v" + Updater.Current + " basladi" + (silent ? " (tepside)" : ""));
            OnState(State.Connecting, info);
            // simge tepsiye yerlestikten sonra (konumu okunabilsin) paneli ac
            if (!silent) ui.BeginInvoke(new Action(delegate { flyout.ShowPanel(IconPoint(false)); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            StartupAsync(false);
        }

        void Ui(Action a)
        {
            if (exiting) return;
            ui.BeginInvoke(a);
        }

        // Panel simgenin tam ustunde: simgenin gercek yeri. Okunamazsa (simge tasma alaninda): tiklamada
        // imlec, tiklamasiz acilista (baslangic, ikinci baslatma) bildirim alani.
        System.Drawing.Point IconPoint(bool click)
        {
            System.Drawing.Point p;
            if (tray.Center(out p)) return p;
            if (!click && Win.NotifyAreaCenter(out p)) return p;
            return System.Windows.Forms.Cursor.Position;
        }

        void OnState(State st, Info i)
        {
            state = st; info = i ?? new Info();
            tray.Set(st, Flyout.Tip(st, info));
            flyout.SetState(st, info);
            flyout.RefreshMenu();
        }

        void NetChanged() { netTimer.Stop(); netTimer.Start(); }

        // ---------------------------------------------------------------- acilis

        // connect: kullanici "Bagla" dedi (otomatik baglanma kapali olsa da baglan)
        void StartupAsync(bool connect)
        {
            starting = true;
            ThreadPool.QueueUserWorkItem(delegate { Startup(connect); });
        }

        void Startup(bool connect)
        {
            try
            {
                Engine.ConfigureNet();
                Engine.StopForeign();      // eski GoodbyeDPI servisi WinDivert'i tutmasin
                Engine.InstallBinaries();
                bool auto = AutostartEnabled();
                Ui(delegate { autostart = auto; ready = true; starting = false; });
            }
            catch (Exception ex)
            {
                Log.Write("Hazirlik hatasi: " + ex);
                Ui(delegate { starting = false; OnState(State.Error, new Info { Error = Strings.T("errPrepare", ex.Message) }); });
                return;
            }
            if (exiting) return;
            if (settings.AutoConnect || connect) engine.Connect(true);
            else engine.Stop();
        }

        void Toggle()
        {
            if ((DateTime.Now - lastToggle).TotalMilliseconds < 700) return; // cift tiklama kapat-ac yapmasin
            lastToggle = DateTime.Now;
            if (!ready) { if (!starting) StartupAsync(true); return; }   // hazirlik basarisizdi: yeniden dene ve bagla
            if (engine.Active) ThreadPool.QueueUserWorkItem(delegate { engine.Stop(); });
            else ThreadPool.QueueUserWorkItem(delegate { if (!exiting) engine.Connect(true); });
        }

        // ---------------------------------------------------------------- menu

        List<MenuItem> MenuItems()
        {
            var l = new List<MenuItem>();
            if (update != null)
                l.Add(new MenuItem { Glyph = "E896", Marked = true, Disabled = updateBusy == "install",
                    Text = updateBusy == "install" ? Strings.T("downloading", update.Version) : Strings.T("update", update.Version), Action = InstallUpdate });
            bool active = engine.Active;
            l.Add(new MenuItem { Glyph = "E7E8", Text = Strings.T(active ? "disconnect" : "connect"), Disabled = starting, Action = Toggle });
            l.Add(new MenuItem { Glyph = "E945", Text = Strings.T("autoConnect"), Sub = Strings.T("autoConnectSub"), Check = settings.AutoConnect, KeepOpen = true,
                Action = delegate { settings.AutoConnect = !settings.AutoConnect; settings.Save(); } });
            l.Add(new MenuItem { Glyph = "E7B5", Text = Strings.T("startWithWindows"), Check = autostart, Disabled = !ready, KeepOpen = true, Action = ToggleAutostart });
            l.Add(MenuItem.Line());
            l.Add(new MenuItem { Glyph = "E774", Text = Strings.T("language"), Tail = langOpen ? "E70E" : "E70D", KeepOpen = true, Action = delegate { langOpen = !langOpen; } });
            if (langOpen)
                foreach (var lang in Strings.Languages)
                {
                    string code = lang[0];
                    l.Add(new MenuItem { Text = lang[1], Indent = true, Check = Strings.Lang == code, KeepOpen = true, Action = delegate { SetLanguage(code); } });
                }
            string sub = lastCheck.HasValue ? Strings.T("lastChecked", Ago(lastCheck.Value)) : null;
            if (updateBusy == "check") l.Add(new MenuItem { Glyph = "E895", Text = Strings.T("checking"), Disabled = true });
            else if (lastResult == "uptodate") l.Add(new MenuItem { Glyph = "E895", Text = Strings.T("upToDateRow", Updater.Current), Check = true, Sub = sub, KeepOpen = true, Action = delegate { CheckUpdates(true); } });
            else if (lastResult == "error") l.Add(new MenuItem { Glyph = "E7BA", Text = Strings.T("checkFailedRow"), Sub = sub, KeepOpen = true, Action = delegate { CheckUpdates(true); } });
            else l.Add(new MenuItem { Glyph = "E895", Text = Strings.T("checkUpdates", Updater.Current), Sub = sub, KeepOpen = true, Action = delegate { CheckUpdates(true); } });
            l.Add(new MenuItem { Glyph = "E9D9", Text = Strings.T("diagnostics"), Action = OpenLog });
            l.Add(new MenuItem { Glyph = "E8BB", Text = Strings.T("exit"), Action = delegate { Shutdown(); if (Exit != null) Exit(); } });
            return l;
        }

        void SetLanguage(string code)
        {
            settings.Lang = code; settings.Save();
            Strings.Load(code);
            langOpen = false;
            OnState(state, info);
            flyout.Relabel();
        }

        static string Ago(DateTime t)
        {
            int m = (int)(DateTime.Now - t).TotalMinutes;
            return m < 1 ? Strings.T("justNow") : m < 60 ? Strings.T("minutesAgo", m) : Strings.T("hoursAgo", m / 60);
        }

        void OpenLog()
        {
            if (!File.Exists(Paths.LogFile)) return;
            // kabuk uzerinden degil: yonetici surec, kullanicinin HKCU dosya iliskilerini izlemesin
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "notepad.exe"), Engine.Quote(Paths.LogFile)) { UseShellExecute = false };
            Process.Start(psi).Dispose();
        }

        // ---------------------------------------------------------------- guncelleme

        void CheckUpdates(bool manual)
        {
            if (updateBusy != null) return;
            updateBusy = "check";
            var started = DateTime.Now;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r = null; string err = null;
                try { r = Updater.Check(); } catch (Exception e) { err = e.Message; }
                int wait = 800 - (int)(DateTime.Now - started).TotalMilliseconds;
                if (manual && wait > 0) Thread.Sleep(wait); // tiklamanin bir sey yaptigi gorunsun
                Ui(delegate
                {
                    updateBusy = null; lastCheck = DateTime.Now;
                    if (err != null || r == null || r.Version == null) { lastResult = "error"; Log.Write("Guncelleme denetimi: " + err); }
                    else if (r.Version > Updater.Current)
                    {
                        bool isNew = update == null || update.Version != r.Version;
                        update = r; lastResult = null;
                        Log.Write("Yeni surum: v" + r.Version);
                        if (isNew && flyout.Open != "menu") tray.Balloon(Strings.T("updAvailable", r.Version));
                    }
                    else { update = null; lastResult = "uptodate"; }
                    flyout.RefreshMenu();
                });
            });
        }

        // Program Files'taki kopya, indirilip SHA-256'si dogrulanan yeni exe ile degistirilir ve yeniden baslatilir.
        void InstallUpdate()
        {
            var r = update;
            if (r == null || updateBusy != null) return;
            if (!string.Equals(Path.GetFullPath(System.Windows.Forms.Application.ExecutablePath), Path.GetFullPath(Paths.InstalledExe), StringComparison.OrdinalIgnoreCase))
            { Log.Write("Guncelleme yalniz Program Files'taki kopyada calisir"); return; }
            updateBusy = "install";
            ThreadPool.QueueUserWorkItem(delegate
            {
                string fresh = Paths.InstalledExe + ".new", err = null;
                try { Updater.Download(r, fresh); } catch (Exception e) { err = e.Message; }
                Ui(delegate
                {
                    updateBusy = null;
                    if (err != null) { Log.Write("Guncelleme: " + err); tray.Balloon(Strings.T("updFailed", err)); flyout.RefreshMenu(); return; }
                    string exe = Paths.InstalledExe, old = exe + ".old";
                    try
                    {
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(exe, old);          // calisan exe yeniden adlandirilabilir
                        File.Move(fresh, exe);
                        Log.Write("Guncellendi: v" + Updater.Current + " -> v" + r.Version);
                        var psi = new ProcessStartInfo(exe, "--after-update") { UseShellExecute = false, WorkingDirectory = Paths.Dir };
                        Process.Start(psi).Dispose();  // yeni surum bu ornegin kapanmasini bekler
                        Shutdown();
                        if (Exit != null) Exit();
                    }
                    catch (Exception e)
                    {
                        Log.Write("Guncelleme uygulanamadi: " + e);
                        if (!File.Exists(exe) && File.Exists(old)) try { File.Move(old, exe); } catch (Exception) { }
                        tray.Balloon(Strings.T("updFailed", e.Message));
                    }
                });
            });
        }

        // ---------------------------------------------------------------- Windows ile baslat

        static bool AutostartEnabled() { return RunSchtasks("/Query", "/TN", TaskName) == 0; }

        void ToggleAutostart()
        {
            bool want = !autostart;   // kullanicinin istedigi durum (gorev disaridan degistiyse de)
            autostart = want;         // menu hemen tepki versin; sonuc asagida dogrulanir
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    lock (quit)   // iki hizli tiklama gorev.xml uzerinde yarismasin
                    {
                    bool has = AutostartEnabled();
                    if (!want && has) { RunSchtasks("/Delete", "/TN", TaskName, "/F"); Log.Write("Windows ile baslatma kapatildi"); }
                    else if (want && !has)
                    {
                        // XML ile: "yalniz prizdeyken baslat / pile geciste durdur / 72 saatte kapat" varsayilanlari kapali.
                        string xml = Path.Combine(Paths.Dir, "gorev.xml");
                        File.WriteAllText(xml, TaskXml(), System.Text.Encoding.Unicode);
                        int rc = RunSchtasks("/Create", "/TN", TaskName, "/XML", xml, "/F");
                        File.Delete(xml);
                        Log.Write("Windows ile baslatma acildi (schtasks " + rc + ")");
                        if (rc != 0) Ui(delegate { tray.Balloon(Strings.T("taskFailed")); });
                    }
                    }
                }
                catch (Exception ex) { Log.Write("Otomatik baslatma hatasi: " + ex.Message); }
                bool now = AutostartEnabled();
                Ui(delegate { autostart = now; flyout.RefreshMenu(); });
            });
        }

        static string TaskXml()
        {
            string user = System.Security.SecurityElement.Escape(System.Security.Principal.WindowsIdentity.GetCurrent().Name);
            string exe = System.Security.SecurityElement.Escape(Paths.InstalledExe);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<RegistrationInfo><Description>SwarlexEasyDpi oturum acilisinda tepside baslar.</Description></RegistrationInfo>" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" +
                "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority><Enabled>true</Enabled></Settings>" +
                "<Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><Arguments>--tray</Arguments></Exec></Actions></Task>";
        }

        static int RunSchtasks(params string[] args)
        {
            var parts = new List<string>();
            foreach (string a in args) parts.Add(Engine.Quote(a));
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), string.Join(" ", parts))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // ---------------------------------------------------------------- cikis

        void Shutdown()
        {
            if (exiting) return;
            exiting = true;
            updateTimer.Stop(); netTimer.Stop();
            try { engine.Stop(); } catch (Exception) { }
            Log.Write("SwarlexEasyDpi kapandi");
        }

        public void Dispose()
        {
            Shutdown();
            quit.Set();
            if (waiter != null) waiter.Join(2000);
            flyout.Close();
            tray.Dispose();
        }
    }
}
