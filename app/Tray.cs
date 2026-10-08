// SPDX-License-Identifier: GPL-3.0-or-later
// Tepsi simgesi: bir halka icinde kalkan, Windows ag/ses simgeleri gibi beyaz (acik gorev
// cubugunda siyah). Cizim yaklasimi SwarlexBattery'nin core/Tray.cs dosyasindan (GPL-3.0):
// 4 kat buyuk cizilip kucultulur, sekil dolu ve kenarli cizilir ki 16 px'te net okunsun.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SwarlexEasyDpi
{
    internal static class Art
    {
        public static readonly Color Green = Color.FromArgb(63, 209, 106);
        public static readonly Color GreenLight = Color.FromArgb(30, 150, 70);
        public static readonly Color Amber = Color.FromArgb(245, 165, 36);
        public static readonly Color Red = Color.FromArgb(240, 74, 74);

        public static bool LightTaskbar()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                { var v = k == null ? null : k.GetValue("SystemUsesLightTheme"); return v is int && (int)v == 1; }
            }
            catch (Exception) { return false; }
        }

        // Kalkan silueti, (0,0)-(1,1) birim kutuda.
        public static GraphicsPath Shield(RectangleF r)
        {
            var p = new GraphicsPath();
            float x = r.X, y = r.Y, w = r.Width, h = r.Height;
            p.AddLine(x + w * 0.5f, y, x + w, y + h * 0.17f);
            p.AddBezier(x + w, y + h * 0.17f, x + w, y + h * 0.62f, x + w * 0.82f, y + h * 0.86f, x + w * 0.5f, y + h);
            p.AddBezier(x + w * 0.5f, y + h, x + w * 0.18f, y + h * 0.86f, x, y + h * 0.62f, x, y + h * 0.17f);
            p.CloseFigure();
            return p;
        }

        // spin: -1 = halka tam; 0..1 = donen yay (baglaniyor animasyonu karesi)
        public static Bitmap Ring(int size, Color ring, Color glyph, Color track, double spin, bool tile)
        {
            int k = 4, B = size * k;
            using (var big = new Bitmap(B, B))
            {
                using (var g = Graphics.FromImage(big))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float inset = 0;
                    if (tile)
                    {
                        // uygulama simgesi: koyu yuvarlak kare zemin (SwarlexBattery simgesiyle ayni dil)
                        float pad = B * 0.04f, side = B - 2 * pad, rr = side * 0.23f;
                        using (var t = new GraphicsPath())
                        {
                            t.AddArc(pad, pad, rr * 2, rr * 2, 180, 90); t.AddArc(pad + side - rr * 2, pad, rr * 2, rr * 2, 270, 90);
                            t.AddArc(pad + side - rr * 2, pad + side - rr * 2, rr * 2, rr * 2, 0, 90); t.AddArc(pad, pad + side - rr * 2, rr * 2, rr * 2, 90, 90);
                            t.CloseFigure();
                            using (var bg = new LinearGradientBrush(new PointF(0, pad), new PointF(0, B - pad), Color.FromArgb(54, 56, 62), Color.FromArgb(22, 23, 26)))
                                g.FillPath(bg, t);
                            using (var edge = new Pen(Color.FromArgb(40, 255, 255, 255), B * 0.006f)) g.DrawPath(edge, t);
                        }
                        inset = B * 0.17f;
                    }
                    float w = tile ? B * 0.085f : B * 0.13f, pd = inset + w / 2 + k * 0.4f;
                    var rect = new RectangleF(pd, pd, B - 2 * pd, B - 2 * pd);
                    using (var tr = new Pen(track, w))
                    using (var pen = new Pen(ring, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawEllipse(tr, rect);
                        if (spin < 0) g.DrawEllipse(pen, rect);
                        else g.DrawArc(pen, rect, (float)(spin * 360 - 90), 100);
                    }
                    float gs = (B - 2 * inset) * (tile ? 0.42f : 0.46f);
                    var sr = new RectangleF((B - gs * 0.84f) / 2, (B - gs) / 2 + gs * 0.02f, gs * 0.84f, gs);
                    using (var path = Shield(sr))
                    using (var brush = new SolidBrush(glyph))
                    using (var outline = new Pen(glyph, B * 0.03f) { LineJoin = LineJoin.Round })
                    { g.FillPath(brush, path); g.DrawPath(outline, path); }
                }
                var bmp = new Bitmap(size, size);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.CompositingQuality = CompositingQuality.HighQuality;
                    g.DrawImage(big, new Rectangle(0, 0, size, size), 0, 0, B, B, GraphicsUnit.Pixel);
                }
                return bmp;
            }
        }

        // Uygulama simgesi (.ico, PNG kareli): koyu karo, yesil halka, beyaz kalkan.
        public static void WriteIco(string path)
        {
            int[] sizes = { 256, 64, 48, 32, 24, 16 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Ring(s, Green, Color.FromArgb(245, 245, 245), Color.FromArgb(36, 255, 255, 255), -1, true))
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    byte d = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                    w.Write(d); w.Write(d); w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }

        // Panel basligi icin kucuk simge (WPF'e PNG olarak verilir).
        public static byte[] Png(int size, Color ring)
        {
            using (var bmp = Ring(size, ring, Color.FromArgb(245, 245, 245), Color.FromArgb(36, 255, 255, 255), -1, true))
            using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
        }
    }

    // Tek NotifyIcon; baglanirken halka doner.
    internal sealed class Tray : IDisposable
    {
        readonly NotifyIcon icon = new NotifyIcon();
        readonly Timer spinner = new Timer { Interval = 90 };
        readonly Dictionary<string, Icon> cache = new Dictionary<string, Icon>();
        State state = State.Off;
        int frame;

        public event Action LeftClick, RightClick, DoubleClick;

        public Tray()
        {
            icon.Text = "SwarlexEasyDpi";
            icon.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && LeftClick != null) LeftClick();
                else if (e.Button == MouseButtons.Right && RightClick != null) RightClick();
            };
            icon.MouseDoubleClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left && DoubleClick != null) DoubleClick(); };
            spinner.Tick += delegate { frame = (frame + 1) % 12; icon.Icon = Get(State.Connecting, frame); };
            SystemEvents.UserPreferenceChanged += OnTheme;
            Set(State.Off, null);
            icon.Visible = true;
        }

        void OnTheme(object s, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            var shown = icon.Icon;
            foreach (var ic in cache.Values) if (ic != shown) ic.Dispose();
            cache.Clear();
            Set(state, null);
            // gosterilen eski simge yenisi atandiktan sonra birakilir (onbellekten cikmisti)
            if (shown != null && shown != icon.Icon) shown.Dispose();
        }

        public void Set(State st, string text)
        {
            state = st;
            if (st == State.Connecting) { if (!spinner.Enabled) spinner.Start(); }
            else { spinner.Stop(); icon.Icon = Get(st, 0); }
            if (text != null)
            {
                string tip = "SwarlexEasyDpi — " + text;
                icon.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            }
        }

        // Simgenin ekrandaki ortasi (panel tam ustunde acilsin). NotifyIcon pencere/kimligini disari
        // vermez; Windows'un Shell_NotifyIconGetRect'i icin yansima ile okunur. Simge gizli tasma
        // alanindaysa ya da okunamazsa false.
        public bool Center(out System.Drawing.Point p)
        {
            p = System.Drawing.Point.Empty;
            try
            {
                var f = typeof(NotifyIcon).GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var id = typeof(NotifyIcon).GetField("id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var w = f != null ? f.GetValue(icon) as NativeWindow : null;
                if (w == null || id == null || w.Handle == IntPtr.Zero) return false;
                var nid = new NOTIFYICONIDENTIFIER { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER)), hWnd = w.Handle, uID = (uint)(int)id.GetValue(icon) };
                RECT r;
                if (Shell_NotifyIconGetRect(ref nid, out r) != 0 || r.Right <= r.Left) return false;
                p = new System.Drawing.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
                return true;
            }
            catch (Exception) { return false; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct NOTIFYICONIDENTIFIER { public uint cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
        [System.Runtime.InteropServices.DllImport("shell32.dll")] static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER id, out RECT r);

        public void Balloon(string text) { icon.ShowBalloonTip(4000, "SwarlexEasyDpi", text, ToolTipIcon.Info); }

        Icon Get(State st, int f)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            bool light = Art.LightTaskbar();
            string key = size + "|" + st + "|" + f + "|" + light;
            Icon ic;
            if (cache.TryGetValue(key, out ic)) return ic;
            Color plain = light ? Color.FromArgb(28, 28, 28) : Color.FromArgb(245, 245, 245);
            Color track = Color.FromArgb(light ? 60 : 77, plain);
            Color ring = plain, glyph = plain;
            double spin = -1;
            switch (st)
            {
                case State.On: ring = light ? Art.GreenLight : Art.Green; break;
                case State.Unverified: ring = Art.Amber; break;
                case State.Error: ring = Art.Red; break;
                case State.Connecting: spin = f / 12.0; break;
                default: ring = track; glyph = Color.FromArgb(150, plain); break; // kapali: soluk
            }
            using (var bmp = Art.Ring(size, ring, glyph, track, spin, false))
            {
                IntPtr h = bmp.GetHicon();
                try { ic = (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
            cache[key] = ic;
            return ic;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

        public void Dispose()
        {
            SystemEvents.UserPreferenceChanged -= OnTheme;
            spinner.Dispose();
            icon.Visible = false;
            icon.Dispose();
            foreach (var ic in cache.Values) ic.Dispose();
        }
    }
}
