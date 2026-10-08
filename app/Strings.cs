// SPDX-License-Identifier: GPL-3.0-or-later
// Dil destegi (Turkce / Ingilizce). Yapi SwarlexBattery'nin core/App.cs Strings sinifindan.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace SwarlexEasyDpi
{
    internal static class Strings
    {
        public static string Lang = "en";
        public static readonly string[][] Languages = { new[] { "tr", "Türkçe" }, new[] { "en", "English" } };
        static Dictionary<string, object> en, cur;

        public static bool Has(string code)
        {
            foreach (var l in Languages) if (l[0] == code) return true;
            return false;
        }

        // Kayitli dil yoksa Windows'un dili (Turkce ise Turkce, degilse Ingilizce).
        public static string Default() { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en"; }

        public static void Load(string lang)
        {
            var js = new JavaScriptSerializer();
            en = js.Deserialize<Dictionary<string, object>>(Text("lang.en.json"));
            cur = en; Lang = "en";
            if (lang != "en" && Has(lang))
            {
                var t = Text("lang." + lang + ".json");
                if (t != null) { cur = js.Deserialize<Dictionary<string, object>>(t); Lang = lang; }
            }
        }

        public static string T(string key, params object[] args)
        {
            object v;
            string f = cur != null && (cur.TryGetValue(key, out v) || en.TryGetValue(key, out v)) ? v as string : null;
            if (f == null) f = key;
            return args.Length > 0 ? string.Format(f, args) : f;
        }

        static string Text(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
    }
}
