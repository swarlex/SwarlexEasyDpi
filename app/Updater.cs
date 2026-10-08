// SPDX-License-Identifier: GPL-3.0-or-later
// GitHub Releases'tan guncelleme. SwarlexBattery'nin core/Updater.cs dosyasindan uyarlandi (GPL-3.0):
// github.com/<repo>/releases/latest yonlendirmesinden surum etiketi okunur (API hiz siniri yok),
// SwarlexEasyDpi.exe ayni surumdeki SwarlexEasyDpi.exe.sha256 ile dogrulanir ve calisan exe'nin
// yerine konur (calisan exe yeniden adlandirilabilir, uzerine yazilamaz).
using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SwarlexEasyDpi
{
    internal sealed class ReleaseInfo { public string Tag, Url, Sha; public Version Version; }

    internal static class Updater
    {
        const int MaxExe = 20 * 1024 * 1024;

        // GitHub surum dosyasini indirir: yonlendirmeleri tek tek izler (her adim HTTPS ve GitHub'in
        // dosya sunuculari), boyutu sinirlar.
        static byte[] Fetch(string url, int max)
        {
            var u = new Uri(url);
            for (int hop = 0; hop < 6; hop++)
            {
                if (u.Scheme != "https" || !(u.Host == "github.com" || u.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("beklenmeyen indirme adresi: " + u.Host);
                var q = (HttpWebRequest)WebRequest.Create(u);
                q.AllowAutoRedirect = false; q.Proxy = null; q.UserAgent = Agent; q.Timeout = 30000; q.ReadWriteTimeout = 30000;
                HttpWebResponse r;
                try { r = (HttpWebResponse)q.GetResponse(); }
                catch (WebException e) { r = e.Response as HttpWebResponse; if (r == null) throw; }
                using (r)
                {
                    int code = (int)r.StatusCode;
                    if (code >= 300 && code < 400) { u = new Uri(u, r.Headers["Location"]); continue; }
                    if (code != 200) throw new Exception("indirme yaniti " + code);
                    if (r.ContentLength > max) throw new Exception("dosya beklenenden buyuk");
                    using (var s = r.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[81920]; int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                        {
                            ms.Write(buf, 0, n);
                            if (ms.Length > max) throw new Exception("dosya beklenenden buyuk");
                        }
                        return ms.ToArray();
                    }
                }
            }
            throw new Exception("cok fazla yonlendirme");
        }

        public const string Repo = "swarlex/SwarlexEasyDpi";
        const string Agent = "SwarlexEasyDpi-updater";
        const string Asset = "SwarlexEasyDpi.exe";

        public static Version Current
        {
            get
            {
                var a = Assembly.GetExecutingAssembly();
                var info = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(a, typeof(AssemblyInformationalVersionAttribute));
                return Norm(info != null ? info.InformationalVersion : null) ?? Norm(a.GetName().Version.ToString());
            }
        }

        public static Version Norm(string v)
        {
            Version x;
            if (v == null || !Version.TryParse(v.Trim().TrimStart('v', 'V'), out x)) return null;
            return new Version(x.Major, x.Minor, Math.Max(0, x.Build));
        }

        static int Head(string url, out string location)
        {
            var q = (HttpWebRequest)WebRequest.Create(url);
            q.Method = "HEAD"; q.AllowAutoRedirect = false; q.Timeout = 20000; q.UserAgent = Agent; q.Proxy = null;
            q.Headers["Cache-Control"] = "no-cache";
            HttpWebResponse s;
            try { s = (HttpWebResponse)q.GetResponse(); }
            catch (WebException e) { s = e.Response as HttpWebResponse; if (s == null) throw; }
            using (s) { location = s.Headers["Location"]; return (int)s.StatusCode; }
        }

        public static ReleaseInfo Check()
        {
            string u = "https://github.com/" + Repo + "/releases/latest?t=" + DateTime.UtcNow.Ticks, tag = null;
            for (int i = 0; i < 4 && tag == null; i++)
            {
                string loc; int code = Head(u, out loc);
                if (code < 300 || code >= 400 || string.IsNullOrEmpty(loc)) throw new Exception("beklenmeyen yanit " + code);
                var next = new Uri(new Uri(u), loc);
                // her adim HTTPS ve github.com olmali
                if (next.Scheme != "https" || next.Host != "github.com") throw new Exception("beklenmeyen yonlendirme: " + next.Host);
                u = next.AbsoluteUri;
                var m = Regex.Match(u, "/releases/tag/([^/?#]+)$");
                if (m.Success) tag = Uri.UnescapeDataString(m.Groups[1].Value);
            }
            if (tag == null) throw new Exception("surum etiketi bulunamadi");
            string b = "https://github.com/" + Repo + "/releases/download/" + Uri.EscapeDataString(tag);
            return new ReleaseInfo { Tag = tag, Version = Norm(tag), Url = b + "/" + Asset, Sha = b + "/" + Asset + ".sha256" };
        }

        // Yalniz bu deponun surum dosyalari; SHA-256 tutmazsa dosya silinir.
        public static void Download(ReleaseInfo info, string dest)
        {
            foreach (var u in new[] { info.Url, info.Sha })
            {
                var x = new Uri(u);
                if (x.Scheme != "https" || x.Host != "github.com" || !x.AbsolutePath.StartsWith("/" + Repo + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                    throw new Exception("beklenmeyen indirme adresi: " + u);
            }
            string first = Encoding.ASCII.GetString(Fetch(info.Sha, 4096)).Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)[0];
            string expected = first.ToLowerInvariant();
            if (!Regex.IsMatch(expected, "^[0-9a-f]{64}$")) throw new Exception("gecersiz SHA-256 dosyasi");
            var bytes = Fetch(info.Url, MaxExe);
            File.WriteAllBytes(dest, bytes);
            if (bytes.Length < 50 * 1024 || bytes[0] != 0x4D || bytes[1] != 0x5A) { File.Delete(dest); throw new Exception("indirilen dosya exe degil"); }
            var sb = new StringBuilder();
            using (var sha = SHA256.Create()) foreach (var x in sha.ComputeHash(bytes)) sb.Append(x.ToString("x2"));
            if (sb.ToString() != expected) { File.Delete(dest); throw new Exception("SHA-256 tutmuyor: dosya bozuk ya da degistirilmis"); }
        }
    }
}
