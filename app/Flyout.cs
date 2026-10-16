// SPDX-License-Identifier: GPL-3.0-or-later
// Tepsi simgesinin tam ustunde acilan pencere: sol tikta panel, sag tikta menu. Pencere kurulumu,
// konumlandirma, menu satirlari ve acilis animasyonu SwarlexBattery'nin core/Flyout.cs dosyasindan
// uyarlandi (GPL-3.0).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace SwarlexEasyDpi
{
    // Menu satiri (Segoe Fluent Icons kod noktasi + metin).
    internal sealed class MenuItem
    {
        public string Glyph, Text, Sub, Tail;
        public bool Check, Marked, Indent, KeepOpen, Disabled;
        public Action Action;
        public bool Separator;
        public string Side;      // dolu: tiklayinca menunun yaninda bu yan pencere acilir ("lang" / "prefs")
        public static MenuItem Line() { return new MenuItem { Separator = true }; }
    }

    internal sealed class Flyout
    {
        public event Action ToggleClicked;
        public Func<List<MenuItem>> MenuItems;   // menu her acilista yeniden kurulur
        public Func<string, List<MenuItem>> SideItems;   // yan pencere (Dil / Tercihler) satirlari
        public bool Animate = true;              // tercih: acilis animasyonu
        public string ThemeMode = "auto";        // tercih: auto (gorev cubugu gibi) | dark | light

        // Menunun yaninda acilan yan pencere (SwarlexBattery'deki Tercihler penceresi gibi).
        Window side;
        readonly ContentControl sideBody = new ContentControl { Focusable = false };
        IntPtr sideHwnd;
        string sideKind;

        const double Btn = 136, Stroke = 6;

        readonly Window win;
        readonly ContentControl body = new ContentControl { Focusable = false };
        IntPtr hwnd;
        System.Drawing.Point anchor;
        bool above = true;
        string open;                              // null | "panel" | "menu"
        string lastClosed; DateTime lastClosedAt = DateTime.MinValue;
        bool light;

        Color panel, surface, surfaceHover, fg, muted, track;
        readonly SolidColorBrush ringBrush = new SolidColorBrush(), glyphBrush = new SolidColorBrush(), glowBrush = new SolidColorBrush(),
                                 faceBrush = new SolidColorBrush(), fgBrush = new SolidColorBrush(), mutedBrush = new SolidColorBrush(),
                                 trackBrush = new SolidColorBrush(), lineBrush = new SolidColorBrush(), accentBrush = new SolidColorBrush();
        Style rowStyle, rowStyleMarked;

        readonly FrameworkElement panelView;
        Ellipse ring, glow;
        RotateTransform spin;
        ScaleTransform press;
        TextBlock status, detail;
        Image logo;
        State state = State.Off;
        Info info = new Info();

        public Flyout()
        {
            win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = false, ShowInTaskbar = false, Topmost = true,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 300,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"), Title = "SwarlexEasyDpi",
            };
            win.Content = new Border { Padding = new Thickness(14), Child = body, Focusable = false };
            win.FocusVisualStyle = null;
            win.SourceInitialized += delegate { hwnd = new WindowInteropHelper(win).Handle; Win.ToolWindow(hwnd); Win.FlyoutFrame(hwnd, !light); };
            win.Deactivated += delegate { CloseUnlessFocused(); };
            win.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            side = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = false, ShowInTaskbar = false, Topmost = true, ShowActivated = false,
                ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height, Width = 320,
                FontFamily = win.FontFamily, Title = "SwarlexEasyDpi",
            };
            side.Content = new Border { Padding = new Thickness(8), Child = sideBody, Focusable = false };
            side.SourceInitialized += delegate { sideHwnd = new WindowInteropHelper(side).Handle; Win.ToolWindow(sideHwnd); Win.FlyoutFrame(sideHwnd, !light); };
            side.Deactivated += delegate { CloseUnlessFocused(); };
            side.KeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Escape) Close(); };
            new WindowInteropHelper(side).EnsureHandle();
            ApplyTheme();
            panelView = BuildPanel();
            Apply(false);
            new WindowInteropHelper(win).EnsureHandle(); // ilk tiklamada WPF soguk baslangic gecikmesi olmasin
        }

        public string Open { get { return open; } }

        bool JustClosed(string what) { return lastClosed == what && (DateTime.Now - lastClosedAt).TotalMilliseconds < 400; }

        static Duration Ms(int ms) { return new Duration(TimeSpan.FromMilliseconds(ms)); }

        // ---------------------------------------------------------------- tema

        void ApplyTheme()
        {
            light = ThemeMode == "light" || (ThemeMode == "auto" && Art.LightTaskbar());
            panel = light ? Color.FromRgb(243, 243, 243) : Color.FromRgb(32, 32, 32);
            surface = light ? Color.FromRgb(255, 255, 255) : Color.FromRgb(44, 44, 44);
            surfaceHover = light ? Color.FromRgb(236, 236, 236) : Color.FromRgb(56, 56, 56);
            fg = light ? Color.FromRgb(26, 26, 26) : Color.FromRgb(255, 255, 255);
            muted = light ? Color.FromRgb(96, 96, 96) : Color.FromRgb(160, 160, 160);
            track = light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(46, 255, 255, 255);
            win.Background = new SolidColorBrush(panel);
            if (side != null) side.Background = win.Background;
            fgBrush.Color = fg; mutedBrush.Color = muted; trackBrush.Color = track;
            faceBrush.BeginAnimation(SolidColorBrush.ColorProperty, null); faceBrush.Color = surface; // eski hover animasyonu yeni temayi ezmesin
            accentBrush.Color = C(light ? Art.GreenLight : Art.Green);
            lineBrush.Color = light ? Color.FromArgb(24, 0, 0, 0) : Color.FromArgb(30, 255, 255, 255);
            // satir vurgusu IsMouseOver tetikleyicisiyle (MouseEnter/Leave yeniden cizimde kacirilabiliyor)
            const string xaml = "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Border\"><Setter Property=\"Background\" Value=\"{0}\"/><Style.Triggers><Trigger Property=\"IsMouseOver\" Value=\"True\"><Setter Property=\"Background\" Value=\"{1}\"/></Trigger></Style.Triggers></Style>";
            string hover = light ? "#0F000000" : "#1FFFFFFF";
            rowStyle = (Style)XamlReader.Parse(string.Format(xaml, "Transparent", hover));
            rowStyleMarked = (Style)XamlReader.Parse(string.Format(xaml, hover, hover));
            if (hwnd != IntPtr.Zero) Win.FlyoutFrame(hwnd, !light);
            if (sideHwnd != IntPtr.Zero) Win.FlyoutFrame(sideHwnd, !light);
        }

        static Color C(System.Drawing.Color c) { return Color.FromArgb(c.A, c.R, c.G, c.B); }

        Color Accent(State st)
        {
            switch (st)
            {
                case State.On: case State.Connecting: return C(light ? Art.GreenLight : Art.Green);
                case State.Unverified: return C(Art.Amber);
                case State.Error: return C(Art.Red);
                default: return track;
            }
        }

        // ---------------------------------------------------------------- panel

        FrameworkElement BuildPanel()
        {
            var root = new StackPanel { Margin = new Thickness(4, 2, 4, 6) };

            var head = new DockPanel { LastChildFill = true };
            logo = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            DockPanel.SetDock(logo, Dock.Left);
            head.Children.Add(logo);
            head.Children.Add(new TextBlock { Text = "SwarlexEasyDpi", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = fgBrush, VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(head);

            var btn = new Grid { Width = Btn, Height = Btn, Margin = new Thickness(0, 22, 0, 16), HorizontalAlignment = HorizontalAlignment.Center, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            press = new ScaleTransform(1, 1, Btn / 2, Btn / 2);
            btn.RenderTransform = press;
            glow = new Ellipse { Fill = glowBrush, Margin = new Thickness(10), Opacity = 0, Effect = new BlurEffect { Radius = 34 } };
            var trackEl = new Ellipse { Stroke = trackBrush, StrokeThickness = Stroke };
            ring = new Ellipse { Stroke = ringBrush, StrokeThickness = Stroke, StrokeDashCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            spin = new RotateTransform(0, Btn / 2, Btn / 2);
            ring.RenderTransform = spin;
            var face = new Ellipse { Fill = faceBrush, Margin = new Thickness(Stroke + 9) };
            var glyph = new TextBlock
            {
                Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 40,
                Foreground = glyphBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            foreach (UIElement el in new UIElement[] { glow, trackEl, ring, face, glyph }) btn.Children.Add(el);
            btn.MouseEnter += delegate { Scale(1.04); faceBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(surfaceHover, Ms(140))); };
            btn.MouseLeave += delegate { Scale(1.0); faceBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(surface, Ms(180))); };
            btn.MouseLeftButtonDown += delegate { Scale(0.96); };
            btn.MouseLeftButtonUp += delegate { Scale(1.04); if (ToggleClicked != null) ToggleClicked(); };
            root.Children.Add(btn);

            status = new TextBlock { FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = fgBrush, HorizontalAlignment = HorizontalAlignment.Center };
            detail = new TextBlock { FontSize = 12, Foreground = mutedBrush, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            root.Children.Add(status);
            root.Children.Add(detail);
            return root;
        }

        void Scale(double to)
        {
            var a = new DoubleAnimation(to, Ms(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            press.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            press.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }

        public void SetState(State st, Info i)
        {
            bool changed = st != state;
            state = st; info = i ?? new Info();
            Apply(changed && open == "panel");
        }

        // Gorunurken yumusak gecis; gizliyken dogrudan ata (animasyon saati pencere cizilene kadar ilerlemez).
        static void Paint(SolidColorBrush b, Color c, bool animate)
        {
            if (animate)
                b.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(c, Ms(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            else { b.BeginAnimation(SolidColorBrush.ColorProperty, null); b.Color = c; }
        }

        void Apply(bool animate)
        {
            State st = state;
            Color accent = Accent(st);
            Paint(ringBrush, accent, animate);
            Paint(glyphBrush, st == State.Off ? muted : st == State.Connecting ? fg : accent, animate);
            glowBrush.Color = accent;
            bool visible = open == "panel";

            if (st == State.Connecting && visible)
            {
                double units = Math.PI * (Btn - Stroke) / Stroke;
                ring.StrokeDashArray = new DoubleCollection { units * 0.26, units };
                spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, Ms(1000)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                if (st == State.Connecting) { double units = Math.PI * (Btn - Stroke) / Stroke; ring.StrokeDashArray = new DoubleCollection { units * 0.26, units }; }
                else ring.StrokeDashArray = null;
                spin.BeginAnimation(RotateTransform.AngleProperty, null);
                spin.Angle = 0;
            }

            if ((st == State.On || st == State.Unverified) && visible)
                glow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.22, 0.42, Ms(1600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            else { glow.BeginAnimation(UIElement.OpacityProperty, null); glow.Opacity = st == State.On || st == State.Unverified ? 0.3 : 0; }

            string main, sub;
            switch (st)
            {
                case State.On:
                    main = Strings.T("stOn");
                    sub = Strings.T("subOn") + (info.Dns != null ? "\n" + Strings.T("dnsVia", info.Dns) : "");
                    break;
                case State.Unverified:
                    main = Strings.T("stOn");
                    sub = Strings.T("subUnverified") + (info.Dns != null ? "\n" + Strings.T("dnsVia", info.Dns) : "");
                    break;
                case State.Connecting:
                    main = Strings.T("stConnecting");
                    sub = info.Error == "prep" ? Strings.T("subPreparing") : info.Try > 0 ? Strings.T("subTrying", info.Try, info.Total) : Strings.T("subConnecting");
                    break;
                case State.Error:
                    main = Strings.T("stError");
                    sub = info.Error == "errStart" || info.Error == "errStopped" || info.Error == "errStuck" ? Strings.T(info.Error) : (info.Error ?? "");
                    break;
                default:
                    main = Strings.T("stOff"); sub = Strings.T("subOff"); break;
            }
            if (status.Text != main || detail.Text != sub)
            {
                status.Text = main; detail.Text = sub;
                if (animate)
                    foreach (var t in new[] { status, detail })
                        t.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.2, 1, Ms(220)));
            }
            logo.Source = Logo(st);
        }

        // Tepsi ipucu icin kisa metin.
        public static string Tip(State st, Info i)
        {
            string s;
            switch (st)
            {
                case State.On: s = Strings.T("stOn"); break;
                case State.Unverified: s = Strings.T("subUnverified"); break;
                case State.Connecting: s = Strings.T("stConnecting"); break;
                case State.Error: s = Strings.T("stError"); break;
                default: s = Strings.T("stOff"); break;
            }
            if (i != null && i.Dns != null && (st == State.On || st == State.Unverified)) s += " · " + Strings.T("dnsVia", i.Dns);
            return s;
        }

        readonly Dictionary<State, BitmapSource> logos = new Dictionary<State, BitmapSource>();

        BitmapSource Logo(State st)
        {
            BitmapSource b;
            if (logos.TryGetValue(st, out b)) return b;
            var c = st == State.On ? Art.Green : st == State.Unverified ? Art.Amber : st == State.Error ? Art.Red : System.Drawing.Color.FromArgb(120, 120, 120);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new System.IO.MemoryStream(Art.Png(64, c));
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            logos[st] = bmp;
            return bmp;
        }

        // ---------------------------------------------------------------- menu

        TextBlock Text(string t, Brush brush, double size, bool semi)
        {
            return new TextBlock { Text = t, Foreground = brush, FontSize = size, FontWeight = semi ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        }

        static TextBlock Glyph(string code, Brush brush, double size)
        {
            return new TextBlock { Text = ((char)Convert.ToInt32(code, 16)).ToString(), FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = size, Foreground = brush };
        }

        Border MenuRow(MenuItem it)
        {
            if (it.Side != null) { it.Marked = sideKind == it.Side; it.Tail = "E76C"; it.KeepOpen = true; }
            var b = new Border { Padding = new Thickness(10, 7, 10, 7), CornerRadius = new CornerRadius(4), Cursor = it.Disabled ? Cursors.Arrow : Cursors.Hand,
                                 Style = it.Marked ? rowStyleMarked : rowStyle, Opacity = it.Disabled ? 0.5 : 1 };
            var g = new Grid();
            foreach (var w in new[] { new GridLength(it.Glyph != null || it.Indent ? 26 : 0), new GridLength(1, GridUnitType.Star), GridLength.Auto }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            if (it.Glyph != null) { var ic = Glyph(it.Glyph, fgBrush, 13); ic.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(ic); }
            if (it.Tail != null) { var t = Glyph(it.Tail, mutedBrush, 10); t.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(t, 2); g.Children.Add(t); }
            UIElement label = Text(it.Text, fgBrush, 13, false);
            if (!string.IsNullOrEmpty(it.Sub)) { var two = new StackPanel(); two.Children.Add(label); two.Children.Add(Text(it.Sub, mutedBrush, 11, false)); label = two; }
            Grid.SetColumn(label, 1); g.Children.Add(label);
            if (it.Check) { var ck = Glyph("E73E", accentBrush, 12); ck.VerticalAlignment = VerticalAlignment.Center; ck.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(ck, 2); g.Children.Add(ck); }
            b.Child = g;
            if (!it.Disabled)
                b.MouseLeftButtonUp += delegate
                {
                    try
                    {
                        if (it.Side != null) { ToggleSide(it.Side); return; }
                        if (!it.KeepOpen) Close();
                        if (it.Action != null) it.Action();
                        if (it.KeepOpen) RefreshMenu();
                    }
                    catch (Exception ex) { Log.Write("menu: " + ex); }
                };
            return b;
        }

        FrameworkElement BuildMenu()
        {
            var root = new StackPanel();
            var h = Text("SwarlexEasyDpi", mutedBrush, 11, true); h.Margin = new Thickness(10, 0, 0, 4); root.Children.Add(h);
            if (MenuItems != null)
                foreach (var it in MenuItems())
                    root.Children.Add(it.Separator ? (UIElement)new Border { Height = 1, Background = lineBrush, Margin = new Thickness(4, 5, 4, 5) } : MenuRow(it));
            return root;
        }

        FrameworkElement BuildSide(string kind)
        {
            var root = new StackPanel();
            if (SideItems != null)
                foreach (var it in SideItems(kind))
                    root.Children.Add(it.Separator ? (UIElement)new Border { Height = 1, Background = lineBrush, Margin = new Thickness(4, 5, 4, 5) } : MenuRow(it));
            return root;
        }

        void ToggleSide(string kind)
        {
            if (sideKind == kind) { CloseSide(); RefreshMenu(); return; }
            sideKind = kind;
            side.Width = kind == "lang" ? 200 : 330;
            sideBody.Content = BuildSide(kind);
            if (!side.IsVisible) { side.Left = -20000; side.Top = -20000; side.Show(); }
            side.UpdateLayout();
            PlaceSide();
            if (Animate) Slide(side, false);
            RefreshMenu();
        }

        void CloseSide() { sideKind = null; if (side.IsVisible) side.Hide(); }

        // Odak ne menude ne yan pencerede ise kapat (ikisi arasinda gecis kapatmasin).
        void CloseUnlessFocused()
        {
            win.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (open != null && !win.IsActive && !side.IsActive) Close();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // Menunun saginda (yer yoksa solunda); gorev cubugu alttaysa alt kenarlari hizali.
        void PlaceSide()
        {
            if (sideHwnd == IntPtr.Zero || hwnd == IntPtr.Zero) return;
            var scr = Forms.Screen.FromPoint(anchor);
            var wa = Win.AreaOutsideTaskbar(scr.Bounds, scr.WorkingArea);
            int mx, my, mw, mh, w, h;
            if (!Win.WindowRect(hwnd, out mx, out my, out mw, out mh) || !Win.WindowSize(sideHwnd, out w, out h)) return;
            double scale = Win.DpiScale(anchor);
            int gap = (int)Math.Round(4 * scale), m = (int)Math.Round(8 * scale);
            int x = mx + mw + gap;
            if (x + w > wa.Right - m) x = Math.Max(wa.Left + m, mx - w - gap);
            int y = above ? my + mh - h : my;
            y = Math.Max(wa.Top + m, Math.Min(wa.Bottom - h - m, y));
            Win.MoveTo(sideHwnd, x, y);
        }

        public void RefreshMenu()
        {
            if (open != "menu") return;
            if (sideKind != null) { sideBody.Content = BuildSide(sideKind); side.UpdateLayout(); }
            body.Content = BuildMenu();
            // satir eklenip cikinca alt kenar gorev cubugunun ustunde kalsin
            win.UpdateLayout();
            Place();
            if (sideKind != null) PlaceSide();
        }

        // ---------------------------------------------------------------- acma / kapama

        // at: simgenin ortasi (yoksa imlec)
        public void TogglePanel(System.Drawing.Point at)
        {
            if (open == "panel") { Close(); return; }
            if (JustClosed("panel")) return;
            ShowPanel(at);
        }

        public void ToggleMenu(System.Drawing.Point at)
        {
            if (open == "menu") { Close(); return; }
            if (JustClosed("menu")) return;
            CloseSide();
            open = "menu";
            ApplyTheme();
            win.Width = 270;
            body.Content = BuildMenu();
            ShowAt(at);
        }

        public void ShowPanel(System.Drawing.Point at)
        {
            CloseSide();
            open = "panel";
            ApplyTheme();
            win.Width = 300;
            body.Content = panelView;
            Apply(false);
            ShowAt(at);
        }

        void ShowAt(System.Drawing.Point at)
        {
            anchor = at;
            if (!win.IsVisible) { win.Left = -20000; win.Top = -20000; win.Show(); }
            win.UpdateLayout();
            Place();
            OpenAnimation();
            Win.ForceForeground(hwnd);
            win.Activate();
        }

        public void Close()
        {
            if (open == null) return;
            lastClosed = open; lastClosedAt = DateTime.Now;
            open = null;
            win.Hide();
            CloseSide();
            // gizliyken sonsuz animasyonlar islemci harcamasin; panel acilinca Apply yeniden baslatir
            glow.BeginAnimation(UIElement.OpacityProperty, null);
            spin.BeginAnimation(RotateTransform.AngleProperty, null);
        }

        // Simgenin ortasina gore ortalanir; gorev cubugunun hemen ustunde (ust/yan gorev cubugunda yaninda).
        void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            var scr = Forms.Screen.FromPoint(anchor);
            var wa = Win.AreaOutsideTaskbar(scr.Bounds, scr.WorkingArea);
            int w, h; if (!Win.WindowSize(hwnd, out w, out h)) return;
            int m = (int)Math.Round(8 * Win.DpiScale(anchor));
            above = anchor.Y > (wa.Top + wa.Bottom) / 2;
            int x = Math.Max(wa.Left + m, Math.Min(wa.Right - w - m, anchor.X - w / 2));
            int y = above ? wa.Bottom - h - m : wa.Top + m;
            Win.MoveTo(hwnd, x, y);
        }

        void OpenAnimation()
        {
            var el = win.Content as FrameworkElement;
            if (el == null) return;
            el.BeginAnimation(UIElement.OpacityProperty, null); el.Opacity = 1; el.RenderTransform = null;
            if (!Animate) return;
            Slide(win, true);
        }

        // Icerik gorev cubugu yonunden (yan pencere: yandan) kayarak ve belirerek gelir.
        void Slide(Window w, bool vertical)
        {
            var el = w.Content as FrameworkElement;
            if (el == null) return;
            var tt = vertical ? new TranslateTransform(0, above ? 14 : -14) : new TranslateTransform(-10, 0);
            el.RenderTransform = tt;
            var dur = Ms(200); var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            if (vertical) tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(tt.Y, 0, dur) { EasingFunction = ease });
            else tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tt.X, 0, dur) { EasingFunction = ease });
        }

        // Tema tercihi degisince: renkler, satir stilleri ve acik pencereler yeniden.
        public void Retheme()
        {
            ApplyTheme();
            logos.Clear();
            Apply(false);
            if (open == "panel") body.Content = panelView;
            RefreshMenu();
        }

        // Dil degisince acik gorunumu yeniden yaz.
        public void Relabel() { Apply(false); RefreshMenu(); }

        // Onizleme/test icin: paneli ya da menuyu PNG olarak ciz.
        public void RenderTo(string path, string what)
        {
            open = what;
            win.Width = what == "menu" ? 270 : 300;
            body.Content = what == "menu" ? BuildMenu() : panelView;
            Apply(false);
            win.Left = -20000; win.Top = -20000; win.ShowActivated = false; win.Show(); win.UpdateLayout();
            var el = (FrameworkElement)win.Content;
            el.Measure(new Size(win.Width, double.PositiveInfinity));
            double hgt = el.DesiredSize.Height;
            el.Arrange(new Rect(0, 0, win.Width, hgt)); // pencere genisliginde (gercek pencere gibi)
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(win.Width), (int)Math.Ceiling(hgt), 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(panel), null, new Rect(0, 0, win.Width, hgt));
            rtb.Render(dv);
            rtb.Render(el);
            var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var f = System.IO.File.Create(path)) enc.Save(f);
            win.Hide(); open = null;
        }
    }
}
