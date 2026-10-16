// SwarlexEasyDpi testleri: yonetici gerektirmeyen kisimlar. Arguman: cikti klasoru (onizleme PNG'leri).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace SwarlexEasyDpi
{
    static class Tests
    {
        static int fail;
        static void Check(string n, bool c) { Console.WriteLine((c ? "PASS " : "FAIL ") + n); if (!c) fail++; }

        [STAThread]
        static int Main(string[] a)
        {
            string outDir = a.Length > 0 ? a[0] : ".";

            // ayar listesi: az ve hizli, yalniz bilinen GoodbyeDPI secenekleri, port 1111 hatasi yok
            Check("configs 4", Configs.List.Length == 4);
            Check("configs unique", Enumerable.Range(0, Configs.List.Length).Select(Configs.Describe).Distinct().Count() == Configs.List.Length);
            Check("doh flags match", Configs.Doh.Length == Configs.List.Length && Configs.Doh.Any(d => d) && Configs.Doh.Any(d => !d));
            Check("no plain DNS redirect", !Configs.List.Any(c => c.Contains("--dns")));
            var known = new[] { "-5", "-9", "--set-ttl", "--dns-addr", "--dns-port", "--dnsv6-addr", "--dnsv6-port" };
            for (int i = 0; i < Configs.List.Length; i++)
            {
                var args = Configs.Args(i);
                bool ok = args.Count > 0;
                for (int j = 0; j < args.Count; j++)
                {
                    if (args[j].StartsWith("-")) { ok &= known.Contains(args[j]); if (args[j].StartsWith("--")) { ok &= j + 1 < args.Count && !args[j + 1].StartsWith("-"); j++; } }
                    else ok = false;
                }
                Check("config " + i + " valid: " + Configs.List[i], ok);
            }
            Check("first config has no DNS redirect", Configs.DnsName(0) == null);
            Check("no Yandex 1253", !Configs.List.Any(c => c.Contains("1253")));
            Check("no easydpi port-1111 bug", !Configs.List.Any(c => c.Contains("--dnsv6-port 1111")));
            Check("quote", Engine.Quote(@"C:\Program Files\x.exe") == @"""C:\Program Files\x.exe""" && Engine.Quote("ab") == "ab");

            // diller: her anahtar iki dilde de var
            Strings.Load("en"); string en = Strings.T("stOn"), enLang = Strings.T("language");
            Strings.Load("tr"); string tr = Strings.T("stOn"), trLang = Strings.T("language");
            Check("lang en", en == "On" && enLang.Contains("English"));
            Check("lang tr", tr == "Açık" && trLang.Contains("Türkçe"));
            Check("lang format", Strings.T("subTrying", 2, 4).Contains("2/4"));
            Check("lang fallback key", Strings.T("yok_boyle_anahtar") == "yok_boyle_anahtar");
            // iki dil dosyasinda ayni anahtarlar (eksik ceviri olmasin)
            string langDir = Path.Combine(outDir, @"..\..\lang");
            var js = new System.Web.Script.Serialization.JavaScriptSerializer();
            var kTr = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(langDir, "tr.json"))).Keys.OrderBy(k => k).ToList();
            var kEn = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(langDir, "en.json"))).Keys.OrderBy(k => k).ToList();
            Check("lang same keys (" + kTr.Count + ")", kTr.SequenceEqual(kEn));

            // surum
            Check("version stamped", Updater.Current != null && Updater.Current.Major >= 1);
            Check("norm", Updater.Norm("v1.2.3") == new Version(1, 2, 3) && Updater.Norm("x") == null);

            // simgeler
            foreach (var spin in new[] { -1.0, 0.0, 0.5 })
                using (var b = Art.Ring(32, Art.Green, Color.White, Color.Gray, spin, false)) Check("tray icon spin " + spin, b.Width == 32);
            string ico = Path.Combine(outDir, "test.ico");
            Art.WriteIco(ico);
            Check("ico written", new FileInfo(ico).Length > 1000);
            using (var b = Art.Ring(256, Art.Green, Color.FromArgb(245, 245, 245), Color.FromArgb(36, 255, 255, 255), -1, true)) b.Save(Path.Combine(outDir, "icon-256.png"));

            // panel ve menu onizlemeleri (Turkce)
            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            var f = new Flyout();
            f.SetState(State.On, new Info { Dns = "DoH" }); f.RenderTo(Path.Combine(outDir, "panel-on.png"), "panel");
            f.SetState(State.Off, new Info()); f.RenderTo(Path.Combine(outDir, "panel-off.png"), "panel");
            f.SetState(State.Connecting, new Info { Try = 2, Total = 4 }); f.RenderTo(Path.Combine(outDir, "panel-connecting.png"), "panel");
            f.MenuItems = delegate
            {
                return new List<MenuItem> {
                    new MenuItem { Glyph = "E7E8", Text = Strings.T("disconnect") },
                    MenuItem.Line(),
                    new MenuItem { Glyph = "E774", Text = Strings.T("language"), Side = "lang" },
                    new MenuItem { Glyph = "E713", Text = Strings.T("prefs"), Side = "prefs" },
                    new MenuItem { Glyph = "E895", Text = Strings.T("upToDateRow", "1.1.0"), Check = true, Sub = Strings.T("lastChecked", Strings.T("justNow")) },
                    new MenuItem { Glyph = "E9D9", Text = Strings.T("diagnostics") },
                    new MenuItem { Glyph = "E8BB", Text = Strings.T("exit") },
                };
            };
            f.RenderTo(Path.Combine(outDir, "menu.png"), "menu");
            // Tercihler yan penceresi (onizleme icin menu olarak cizilir)
            f.MenuItems = delegate
            {
                return new List<MenuItem> {
                    new MenuItem { Text = Strings.T("autoConnect"), Sub = Strings.T("autoConnectSub"), Check = true },
                    new MenuItem { Text = Strings.T("startWithWindows"), Sub = Strings.T("startWithWindowsSub"), Check = true },
                    new MenuItem { Text = Strings.T("keepIcon"), Sub = Strings.T("keepIconSub"), Check = true },
                    MenuItem.Line(),
                    new MenuItem { Text = Strings.T("animation"), Sub = Strings.T("animationSub"), Check = true },
                    new MenuItem { Text = Strings.T("theme", Strings.T("themeAuto")), Sub = Strings.T("themeAutoSub") },
                    MenuItem.Line(),
                    new MenuItem { Text = Strings.T("autoUpdate"), Sub = Strings.T("autoUpdateSub"), Check = true },
                };
            };
            f.RenderTo(Path.Combine(outDir, "prefs.png"), "menu");
            Check("previews", File.Exists(Path.Combine(outDir, "panel-on.png")) && File.Exists(Path.Combine(outDir, "menu.png")));

            // uygulama ici motor (gdpi.dll): hatalar sureci kapatmamali, tekrar tekrar calisabilmeli
            Gdpi.DllPath = Path.GetFullPath(Path.Combine(outDir, @"..\gdpi\gdpi.dll"));
            int rcBad = Gdpi.Run(new[] { "goodbyedpi", "--port", "99999" });
            Check("engine: bad option returns code (" + rcBad + "), process alive", rcBad > 0);
            bool admin = new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            if (!admin)
            {
                int rc1 = Gdpi.Run(new[] { "goodbyedpi", "-9", "--set-ttl", "7" });
                int rc2 = Gdpi.Run(new[] { "goodbyedpi", "-5", "--set-ttl", "5" });
                Check("engine: no admin -> filter open fails cleanly (" + rc1 + "," + rc2 + ")", rc1 == 1 && rc2 == 1);
                var run = new GdpiRun(Configs.Args(0));
                var finished = new System.Threading.ManualResetEvent(false);
                run.Finished += delegate { finished.Set(); };
                run.Start();
                Check("engine: thread run finishes with code (" + run.Code + ")", finished.WaitOne(10000) && run.HasExited && run.Code == 1);
                Check("engine: stop after finish is safe", run.StopAndWait(1000));
            }
            else Console.WriteLine("  (yonetici: motor testi WinDivert'i gercekten acar, atlandi)");
            if (!admin)
            {
                int de = Gdpi.DnsStart();
                Check("dns: no admin -> start fails cleanly (" + de + ")", de != 0);
                Gdpi.DnsStop();
                Check("dns: stop when not running is safe", true);
            }
            Console.WriteLine("  dns poisoned here: " + Engine.DnsPoisoned());

            // dogrulama ucu (ag): sonuc yazilir, basarisizlik testi bozmaz (ag/VPN'e bagli)
            Engine.ConfigureNet();
            Console.WriteLine("  probe discord gateway (uygulama ici): " + (Engine.Probe() ? "200" : "basarisiz"));

            Console.WriteLine(fail == 0 ? "ALL OK" : fail + " FAILED");
            return fail;
        }
    }
}
