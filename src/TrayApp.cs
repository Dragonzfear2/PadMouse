using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace PadMouse
{
    /// <summary>Lives in the system tray and wires the engine, watcher, pop-ups and windows together.</summary>
    public class TrayApp : ApplicationContext, ISettingsHost
    {
        readonly Control ui;                 // marshals engine events onto the UI thread
        readonly NotifyIcon tray;
        readonly PadEngine engine;
        readonly AppWatcher watcher;
        readonly OnScreenKeyboard osk;
        readonly Osd osd = new Osd();
        readonly Icon iconOn, iconOff, iconPaused;
        readonly ToolStripMenuItem enabledItem, profilesMenu, updateItem, statusItem;
        readonly DateTime startedAt = DateTime.Now;
        Config cfg;
        SettingsForm settings;
        WelcomeForm welcome;
        UpdateInfo availableUpdate;
        bool lowBatteryWarned;
        bool exiting;

        public PadEngine Engine { get { return engine; } }
        public AppWatcher Watcher { get { return watcher; } }

        public TrayApp(bool quietStart, string notice)
        {
            ui = new Control();
            ui.CreateControl();
            var h = ui.Handle; // force handle creation on this thread

            string error;
            cfg = Config.Load(out error);

            iconOn = TrayIcons.MakeIcon(Theme.Accent);
            iconOff = TrayIcons.MakeIcon(Color.FromArgb(128, 132, 140));
            iconPaused = TrayIcons.MakeIcon(Theme.Warning);

            engine = new PadEngine(cfg);
            watcher = new AppWatcher(cfg);
            osk = new OnScreenKeyboard();
            osk.VisibleChanged += delegate { engine.OskVisible = osk.Visible; };

            engine.EnabledChanged += on => Post(() => OnEnabledChanged(on));
            engine.ConnectionChanged += connected => Post(() => OnConnectionChanged(connected));
            engine.ProfileChanged += (idx, manual) => Post(() => OnProfileChanged(idx, manual));
            engine.BatteryChanged += () => Post(OnBatteryChanged);
            engine.ToggleKeyboard += () => Post(() => osk.ToggleShow());
            engine.KeyboardCommand += c => Post(() => osk.Command(c));
            watcher.PauseChanged += p => { engine.SetPaused(p); if (p && osk.Visible) osk.Hide(); UpdateTray(); };
            watcher.ProfileWanted += idx => engine.RequestProfile(idx);

            // ---- tray menu
            var menu = new ContextMenuStrip { ShowImageMargin = false };
            statusItem = new ToolStripMenuItem("PadMouse") { Enabled = false };
            enabledItem = new ToolStripMenuItem("Enabled", null, delegate { engine.RequestEnabled(!engine.Enabled); });
            profilesMenu = new ToolStripMenuItem("Profile");
            updateItem = new ToolStripMenuItem("Update available", null, delegate { InstallOrOpenUpdate(); }) { Visible = false };
            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(enabledItem);
            menu.Items.Add(profilesMenu);
            menu.Items.Add("On-screen keyboard", null, delegate { osk.ToggleShow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Settings…", null, delegate { ShowSettings(0); });
            menu.Items.Add("Welcome guide", null, delegate { ShowWelcome(); });
            menu.Items.Add(updateItem);
            menu.Items.Add("About PadMouse", null, delegate { ShowSettings(5); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitApp(); });
            menu.Opening += delegate { RebuildProfileMenu(); UpdateTray(); };

            // Set the icon before making it visible, otherwise Windows may not add it to the tray.
            tray = new NotifyIcon { ContextMenuStrip = menu, Icon = engine.Enabled ? iconOn : iconOff, Text = "PadMouse" };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowSettings(0); };
            tray.BalloonTipClicked += delegate { if (availableUpdate != null) ShowSettings(5); };
            tray.Visible = true;
            UpdateTray();

            // ---- signals from other PadMouse processes
            if (SingleInstance.ShowSignal != null)
                ThreadPool.RegisterWaitForSingleObject(SingleInstance.ShowSignal, (st, to) => Post(() => ShowSettings(0)), null, -1, false);
            if (SingleInstance.ExitSignal != null)
                ThreadPool.RegisterWaitForSingleObject(SingleInstance.ExitSignal, (st, to) => Post(ExitApp), null, -1, true);

            XInputState probe;
            XInput.TryGetState(0, out probe);   // makes XInput.Available meaningful below
            engine.Start();
            watcher.Start();

            // ---- startup messages
            if (error != null)
                Balloon("Problem in config.ini", Truncate(error, 220), ToolTipIcon.Warning);
            else if (!XInput.Available)
                Balloon("XInput not found", "PadMouse couldn't load XInput, so it can't read the controller.", ToolTipIcon.Error);
            else if (notice != null)
                Balloon("PadMouse", notice);
            else if (!quietStart && cfg.FirstRunDone)
                Balloon("PadMouse is running", "Press " + Names.Combo(cfg.ToggleCombo) + " to switch on/off. Click this icon for settings.");

            if (!cfg.FirstRunDone)
            {
                cfg.FirstRunDone = true;
                TrySave(cfg);
                Post(ShowWelcome);
            }

            MaybeCheckForUpdates();
        }

        void Post(Action a)
        {
            if (exiting || !ui.IsHandleCreated || ui.IsDisposed) return;
            try { ui.BeginInvoke(a); } catch (InvalidOperationException) { }
        }

        // ------------------------------------------------------------------ engine events

        void OnEnabledChanged(bool on)
        {
            if (!on && osk.Visible) osk.Hide();
            UpdateTray();
            if (cfg.ShowOsd) osd.ShowMessage(on ? "PadMouse on" : "PadMouse off", on ? CurrentProfileName : "Press " + Names.Combo(cfg.ToggleCombo) + " to switch back on", on ? Theme.Accent : Theme.TextMuted);
        }

        void OnConnectionChanged(bool connected)
        {
            lowBatteryWarned = false;
            UpdateTray();
            if ((DateTime.Now - startedAt).TotalSeconds < 4) return; // startup message already shown
            if (cfg.ShowOsd)
                osd.ShowMessage(connected ? "Controller connected" : "Controller disconnected", connected ? (engine.Enabled ? CurrentProfileName : "PadMouse is off") : "", connected ? Theme.Accent : Theme.Warning);
        }

        void OnProfileChanged(int idx, bool manual)
        {
            if (manual) watcher.ManualOverride();
            UpdateTray();
            if (manual && cfg.ShowOsd) osd.ShowMessage("Profile: " + CurrentProfileName, ProfileHint(), Theme.Selection);
        }

        string ProfileHint()
        {
            return "Hold View + RB / LB to switch";
        }

        void OnBatteryChanged()
        {
            UpdateTray();
            bool low = engine.BatteryType != XInput.BATTERY_TYPE_WIRED && engine.BatteryType != XInput.BATTERY_TYPE_DISCONNECTED &&
                       engine.BatteryLevel >= 0 && engine.BatteryLevel <= XInput.BATTERY_LEVEL_LOW;
            if (low && !lowBatteryWarned && cfg.BatteryWarnings)
            {
                lowBatteryWarned = true;
                Balloon("Controller battery low", "Charge or swap the batteries soon.", ToolTipIcon.Warning);
                if (cfg.ShowOsd) osd.ShowMessage("Controller battery low", "Charge it soon", Theme.Warning);
            }
            if (!low) lowBatteryWarned = false;
        }

        string CurrentProfileName
        {
            get { var p = cfg.Profiles; return p.Count == 0 ? "" : p[Math.Min(engine.ActiveProfile, p.Count - 1)].Name; }
        }

        // ------------------------------------------------------------------ tray

        void UpdateTray()
        {
            bool on = engine.Enabled, paused = engine.Paused;
            tray.Icon = !on ? iconOff : paused ? iconPaused : iconOn;

            string state = !on ? "Off" : paused ? "Paused (game)" : "On";
            string text = "PadMouse: " + state;
            if (engine.ConnectedSlot < 0) text += " · no controller";
            else
            {
                text += " · " + CurrentProfileName;
                string bat = Names.Battery(engine.BatteryType, engine.BatteryLevel);
                if (bat.Length > 0) text += " · " + bat;
            }
            tray.Text = Truncate(text, 63);

            statusItem.Text = text;
            enabledItem.Checked = on;
        }

        void RebuildProfileMenu()
        {
            profilesMenu.DropDownItems.Clear();
            for (int i = 0; i < cfg.Profiles.Count; i++)
            {
                int idx = i;
                var item = new ToolStripMenuItem(cfg.Profiles[i].Name, null, delegate { engine.RequestProfile(idx); watcher.ManualOverride(); })
                { Checked = i == engine.ActiveProfile };
                profilesMenu.DropDownItems.Add(item);
            }
        }

        void Balloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            tray.ShowBalloonTip(4000, title, text, icon);
        }

        // ------------------------------------------------------------------ windows

        void ShowSettings(int page)
        {
            if (settings != null && !settings.IsDisposed)
            {
                if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
                settings.Activate();
                if (page == 5) settings.ShowAbout();
                return;
            }
            settings = new SettingsForm(this, cfg.Clone(), page);
            settings.Show();
            settings.Activate();
        }

        public void ShowWelcome()
        {
            if (welcome != null && !welcome.IsDisposed) { welcome.Activate(); return; }
            welcome = new WelcomeForm(cfg, engine);
            welcome.FormClosed += delegate { if (welcome.OpenSettingsRequested) ShowSettings(0); welcome = null; };
            welcome.Show();
            welcome.Activate();
        }

        // ------------------------------------------------------------------ ISettingsHost

        public void PreviewConfig(Config c) { engine.RequestConfig(c); }

        public void SaveConfig(Config c)
        {
            c.Save();
            cfg = c;
            engine.RequestConfig(c);
            watcher.SetConfig(c);
            UpdateTray();
        }

        void TrySave(Config c) { try { c.Save(); } catch (Exception ex) { PadEngine.Log(ex); } }

        public StartupMode GetStartupMode() { return Startup.GetMode(); }
        public bool SetStartupMode(StartupMode mode, out string error) { return Startup.SetMode(mode, out error); }

        public void RestartAsAdmin()
        {
            try
            {
                Process.Start(new ProcessStartInfo(AppInfo.ExePath, "--restart") { UseShellExecute = true, Verb = "runas" });
                ExitApp();
            }
            catch (System.ComponentModel.Win32Exception) { /* prompt cancelled: keep running */ }
        }

        public void CheckForUpdates(bool manual, Action<UpdateInfo, string> done)
        {
            Updater.CheckAsync((info, err) =>
            {
                Post(() =>
                {
                    if (err == null)
                    {
                        cfg.LastUpdateCheck = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                        TrySave(cfg);
                    }
                    SetAvailableUpdate(info, manual);
                    if (done != null) done(info, err);
                });
            });
        }

        void MaybeCheckForUpdates()
        {
            if (!cfg.CheckForUpdates || !AppInfo.UpdatesConfigured) return;
            DateTime last;
            if (DateTime.TryParse(cfg.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last) &&
                (DateTime.UtcNow - last).TotalHours < 24) return;
            CheckForUpdates(false, null);
        }

        void SetAvailableUpdate(UpdateInfo info, bool manual)
        {
            availableUpdate = info;
            updateItem.Visible = info != null;
            if (info == null) return;
            updateItem.Text = "Update to version " + info.Version + "…";
            if (!manual && info.Version != cfg.SkippedVersion)
                Balloon("PadMouse " + info.Version + " is available", "Click here, or use the tray menu, to update.");
        }

        void InstallOrOpenUpdate()
        {
            if (availableUpdate == null) return;
            if (availableUpdate.DownloadUrl != null) InstallUpdate(availableUpdate);
            else if (availableUpdate.PageUrl != null) { try { Process.Start(availableUpdate.PageUrl); } catch { } }
        }

        public void InstallUpdate(UpdateInfo info)
        {
            if (info == null || info.DownloadUrl == null) return;
            if (MessageBox.Show("Download and install PadMouse " + info.Version + " now? PadMouse will restart.", "PadMouse update",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            Cursor.Current = Cursors.WaitCursor;
            string err = Updater.DownloadAndRun(info);
            Cursor.Current = Cursors.Default;
            if (err != null) { MessageBox.Show("The update couldn't be downloaded: " + err, "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            // The new copy asks us to exit through the exit signal once it's ready.
        }

        public void Uninstall()
        {
            try { Process.Start(new ProcessStartInfo(Installer.InstalledExe, "--uninstall") { UseShellExecute = false }); }
            catch (Exception ex) { MessageBox.Show("Couldn't start the uninstaller: " + ex.Message, "PadMouse"); }
        }

        // ------------------------------------------------------------------ exit

        void ExitApp()
        {
            if (exiting) return;
            exiting = true;
            engine.Stop();
            watcher.Dispose();
            tray.Visible = false;
            tray.Dispose();
            if (settings != null && !settings.IsDisposed) { settings.Dispose(); }
            if (welcome != null && !welcome.IsDisposed) welcome.Dispose();
            osk.Close();
            osd.Close();
            SingleInstance.Release();
            ExitThread();
        }

        static string Truncate(string s, int n) { return s.Length <= n ? s : s.Substring(0, n - 1) + "…"; }
    }
}
