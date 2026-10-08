// SPDX-License-Identifier: GPL-3.0-or-later
// Win32 yardimcilari. Panel konumlandirma (gorev cubugu, DPI, kose) SwarlexBattery'nin
// core/Native.cs dosyasindan uyarlandi (GPL-3.0).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SwarlexEasyDpi
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    // Uygulama cokse bile goodbyedpi arkada kalmasin: KILL_ON_JOB_CLOSE ile bir Job nesnesine baglanir.
    internal static class Job
    {
        static IntPtr handle = IntPtr.Zero;

        public static void Add(Process p)
        {
            if (handle == IntPtr.Zero)
            {
                handle = CreateJobObject(IntPtr.Zero, null);
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int len = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr ptr = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(handle, 9, ptr, (uint)len)) throw new Win32Exception();
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            AssignProcessToJobObject(handle, p.Handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong a, b, c, d, e, f; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr a, string name);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint len);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr proc);
    }

    internal static class Win
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int index, int value);
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT p, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public static bool IsWindows11 { get { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000; } }

        // Tepsi tiklamasi bu surece one gecme hakki verir; panel onu kullanir.
        public static void ForceForeground(IntPtr h) { if (h != IntPtr.Zero) SetForegroundWindow(h); }

        // Alt+Tab ve gorev cubugunda gorunmesin (WS_EX_TOOLWINDOW).
        public static void ToolWindow(IntPtr h) { SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x00000080); }

        // Windows 11 yuvarlak kose + koyu/acik pencere cercevesi.
        public static void FlyoutFrame(IntPtr h, bool dark)
        {
            if (h == IntPtr.Zero) return;
            try
            {
                int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4);
                int d = dark ? 1 : 0;
                if (DwmSetWindowAttribute(h, 20, ref d, 4) != 0) DwmSetWindowAttribute(h, 19, ref d, 4);
            }
            catch (Exception) { }
        }

        // Bir monitorun calisma alani, o monitordeki gorev cubugu disarida birakilarak
        // (otomatik gizlenen gorev cubugu dahil).
        public static System.Drawing.Rectangle AreaOutsideTaskbar(System.Drawing.Rectangle bounds, System.Drawing.Rectangle work)
        {
            var bars = new List<IntPtr>();
            IntPtr h = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null); if (h != IntPtr.Zero) bars.Add(h);
            for (h = IntPtr.Zero; (h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero; ) bars.Add(h);
            int left = work.Left, top = work.Top, right = work.Right, bottom = work.Bottom;
            foreach (var bar in bars)
            {
                RECT r; if (!GetWindowRect(bar, out r)) continue;
                int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
                if (cx < bounds.Left - 50 || cx > bounds.Right + 50 || cy < bounds.Top - 50 || cy > bounds.Bottom + 50) continue;
                int wdt = r.Right - r.Left, hgt = r.Bottom - r.Top;
                if (wdt >= hgt)
                {
                    if (cy < (bounds.Top + bounds.Bottom) / 2) top = Math.Max(top, bounds.Top + hgt);
                    else bottom = Math.Min(bottom, bounds.Bottom - hgt);
                }
                else
                {
                    if (cx < (bounds.Left + bounds.Right) / 2) left = Math.Max(left, bounds.Left + wdt);
                    else right = Math.Min(right, bounds.Right - wdt);
                }
            }
            if (right - left < 100 || bottom - top < 100) return work;
            return System.Drawing.Rectangle.FromLTRB(left, top, right, bottom);
        }

        // Ana gorev cubugundaki bildirim alaninin (saatin yani) ortasi.
        public static bool NotifyAreaCenter(out System.Drawing.Point p)
        {
            p = System.Drawing.Point.Empty;
            IntPtr bar = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
            if (bar == IntPtr.Zero) return false;
            IntPtr area = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
            RECT r;
            if (!GetWindowRect(area != IntPtr.Zero ? area : bar, out r)) return false;
            if (area == IntPtr.Zero) r.Left = Math.Max(r.Left, r.Right - 300); // bulunamazsa sag uc
            p = new System.Drawing.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            return true;
        }

        // Noktanin oldugu monitorun olcegi (1.0 = 96 dpi).
        public static double DpiScale(System.Drawing.Point p)
        {
            try
            {
                uint x, y; var m = MonitorFromPoint(new POINT { X = p.X, Y = p.Y }, 2);
                if (GetDpiForMonitor(m, 0, out x, out y) == 0 && x > 0) return x / 96.0;
            }
            catch (Exception) { }
            return 1.0;
        }

        public static bool WindowSize(IntPtr h, out int w, out int hgt)
        {
            RECT r; bool ok = GetWindowRect(h, out r);
            w = r.Right - r.Left; hgt = r.Bottom - r.Top; return ok && w > 0 && hgt > 0;
        }

        // Boyutu, sirayi ve odagi degistirmeden tasi.
        public static void MoveTo(IntPtr h, int x, int y) { SetWindowPos(h, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010); }
    }
}
