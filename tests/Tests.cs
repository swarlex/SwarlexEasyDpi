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
            Check("configs unique", Configs.List.Distinct().Count() == Configs.List.Length);
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
            f.SetState(State.On, new Info { Dns = "Quad9" }); f.RenderTo(Path.Combine(outDir, "panel-on.png"), "panel");
            f.SetState(State.Off, new Info()); f.RenderTo(Path.Combine(outDir, "panel-off.png"), "panel");
            f.SetState(State.Connecting, new Info { Try = 2, Total = 4 }); f.RenderTo(Path.Combine(outDir, "panel-connecting.png"), "panel");
            f.MenuItems = delegate
            {
                return new List<MenuItem> {
                    new MenuItem { Glyph = "E896", Marked = true, Text = Strings.T("update", "1.0.1") },
                    new MenuItem { Glyph = "E7E8", Text = Strings.T("disconnect") },
                    new MenuItem { Glyph = "E945", Text = Strings.T("autoConnect"), Sub = Strings.T("autoConnectSub"), Check = true },
                    new MenuItem { Glyph = "E7B5", Text = Strings.T("startWithWindows"), Check = true },
                    MenuItem.Line(),
                    new MenuItem { Glyph = "E774", Text = Strings.T("language"), Tail = "E70D" },
                    new MenuItem { Glyph = "E895", Text = Strings.T("upToDateRow", "1.0.0"), Check = true, Sub = Strings.T("lastChecked", Strings.T("minutesAgo", 2)) },
                    new MenuItem { Glyph = "E9D9", Text = Strings.T("diagnostics") },
                    new MenuItem { Glyph = "E8BB", Text = Strings.T("exit") },
                };
            };
            f.RenderTo(Path.Combine(outDir, "menu.png"), "menu");
            Check("previews", File.Exists(Path.Combine(outDir, "panel-on.png")) && File.Exists(Path.Combine(outDir, "menu.png")));

            // dogrulama ucu (ag): sonuc yazilir, basarisizlik testi bozmaz (ag/VPN'e bagli)
            Engine.ConfigureNet();
            Console.WriteLine("  probe discord gateway (uygulama ici): " + (Engine.Probe() ? "200" : "basarisiz"));

            Console.WriteLine(fail == 0 ? "ALL OK" : fail + " FAILED");
            return fail;
        }
    }
}
