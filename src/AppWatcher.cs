using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>
    /// Watches which app is in front. Decides whether PadMouse should pause (fullscreen game)
    /// and which profile should be active (per-app profiles).
    /// </summary>
    public class AppWatcher : IDisposable
    {
        readonly Timer timer = new Timer { Interval = 400 };
        readonly Dictionary<uint, string> nameCache = new Dictionary<uint, string>();
        readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        Config cfg;

        public string ForegroundApp { get; private set; }     // last app in front that isn't PadMouse
        public bool ForegroundFullscreen { get; private set; }
        public bool ShouldPause { get; private set; }
        public string PauseReason { get; private set; }

        string overrideApp;            // manual profile choice holds until this app loses focus
        int lastWantedProfile = -1;

        public event Action<bool> PauseChanged;
        public event Action<int> ProfileWanted;

        /// <summary>Windows overlays that cover the screen but aren't games (Game Bar, Start, search...).</summary>
        static readonly string[] SystemApps =
        {
            "gamebar", "gamebarftserver", "gamebarpresencewriter", "xboxgamebarwidgets", "explorer",
            "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp", "textinputhost",
            "lockapp", "applicationframehost", "screenclippinghost", "snippingtool", "dwm"
        };

        public AppWatcher(Config c)
        {
            cfg = c;
            ForegroundApp = "";
            timer.Tick += delegate { Poll(); };
        }

        public void Start() { timer.Start(); Poll(); }
        public void SetConfig(Config c) { cfg = c; lastWantedProfile = -1; Poll(); }

        /// <summary>The user picked a profile by hand; keep it until they switch to another app.</summary>
        public void ManualOverride() { overrideApp = ForegroundApp; lastWantedProfile = -1; }

        void Poll()
        {
            try
            {
                IntPtr hwnd = Win32.GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return;
                uint pid;
                Win32.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return;
                if (pid == ownPid)
                {
                    // You're using PadMouse's own windows, so it must not be paused.
                    if (ShouldPause) { ShouldPause = false; PauseReason = null; var ph = PauseChanged; if (ph != null) ph(false); }
                    return;
                }

                string app = ProcessName(pid);
                bool full = IsFullscreen(hwnd);
                bool changed = app != ForegroundApp;
                ForegroundApp = app;
                ForegroundFullscreen = full;
                if (changed) overrideApp = null;

                // ---- pausing
                bool pause = false; string reason = null;
                if (cfg.AlwaysPauseApps.Contains(app)) { pause = true; reason = app + " is on your always-pause list"; }
                else if (cfg.AutoPause && full && !cfg.NeverPauseApps.Contains(app) && Array.IndexOf(SystemApps, app) < 0) { pause = true; reason = app + " is fullscreen"; }
                PauseReason = reason;
                if (pause != ShouldPause)
                {
                    ShouldPause = pause;
                    var h = PauseChanged; if (h != null) h(pause);
                }

                // ---- profiles
                if (cfg.AutoSwitchProfiles && overrideApp == null)
                {
                    int want = cfg.IndexOfProfile(cfg.DefaultProfile);
                    for (int i = 0; i < cfg.Profiles.Count; i++)
                        if (cfg.Profiles[i].MatchesApp(app)) { want = i; break; }
                    if (want < 0) want = 0;
                    if (want != lastWantedProfile)
                    {
                        lastWantedProfile = want;
                        var h = ProfileWanted; if (h != null) h(want);
                    }
                }
            }
            catch (Exception ex) { PadEngine.Log(ex); }
        }

        string ProcessName(uint pid)
        {
            string n;
            if (nameCache.TryGetValue(pid, out n)) return n;
            try { n = Config.NormaliseApp(Process.GetProcessById((int)pid).ProcessName); }
            catch { n = "unknown"; }
            if (nameCache.Count > 200) nameCache.Clear();
            nameCache[pid] = n;
            return n;
        }

        static bool IsFullscreen(IntPtr hwnd)
        {
            if (hwnd == Win32.GetShellWindow() || hwnd == Win32.GetDesktopWindow()) return false;
            var cls = new StringBuilder(64);
            Win32.GetClassName(hwnd, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd") return false;

            // Exclusive-mode D3D games report this state directly.
            int state;
            try { if (Win32.SHQueryUserNotificationState(out state) == 0 && state == 3 /*QUNS_RUNNING_D3D_FULL_SCREEN*/) return true; }
            catch { }

            // Normal (even maximised) windows have a title bar; fullscreen games and players don't.
            long style = Win32.GetWindowLongPtr(hwnd, -16 /*GWL_STYLE*/).ToInt64();
            if ((style & 0x00C00000L /*WS_CAPTION*/) == 0x00C00000L) return false;

            Win32.RECT wr;
            if (!Win32.GetWindowRect(hwnd, out wr)) return false;
            IntPtr mon = Win32.MonitorFromWindow(hwnd, 2 /*MONITOR_DEFAULTTONEAREST*/);
            var mi = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32.MONITORINFO)) };
            if (!Win32.GetMonitorInfo(mon, ref mi)) return false;
            return wr.Left <= mi.rcMonitor.Left && wr.Top <= mi.rcMonitor.Top &&
                   wr.Right >= mi.rcMonitor.Right && wr.Bottom >= mi.rcMonitor.Bottom;
        }

        /// <summary>Names of running apps that have a window, for the "add app" pickers.</summary>
        public static List<string> RunningWindowedApps()
        {
            var list = new List<string>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero)
                    {
                        string n = Config.NormaliseApp(p.ProcessName);
                        if (!list.Contains(n) && n != "padmouse") list.Add(n);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public void Dispose() { timer.Dispose(); }
    }
}
