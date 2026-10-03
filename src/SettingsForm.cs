using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PadMouse
{
    public enum StartupMode { Off, Normal, Admin }

    /// <summary>What the settings window needs from the rest of the app.</summary>
    public interface ISettingsHost
    {
        PadEngine Engine { get; }
        AppWatcher Watcher { get; }
        void PreviewConfig(Config c);
        void SaveConfig(Config c);
        StartupMode GetStartupMode();
        bool SetStartupMode(StartupMode mode, out string error);
        void ShowWelcome();
        void RestartAsAdmin();
        void CheckForUpdates(bool manual, Action<UpdateInfo, string> done);
        void InstallUpdate(UpdateInfo info);
        void Uninstall();
    }

    /// <summary>The PadMouse settings window: sidebar + pages, dark theme, live preview.</summary>
    public class SettingsForm : Form
    {
        readonly ISettingsHost host;
        readonly Config original;
        Config work;
        bool dirty, saved, loading;
        StartupMode startupAtOpen;

        readonly NavList nav = new NavList();
        readonly Panel content = new Panel();
        readonly List<Panel> pages = new List<Panel>();
        readonly Timer live = new Timer { Interval = 40 };
        readonly Timer previewTimer = new Timer { Interval = 150 };
        Label footerStatus;
        FlatButton saveBtn;
        uint lastMask;

        // Buttons page
        ComboBox profilePicker, actionBox, leftStickBox, rightStickBox;
        ControllerView pad;
        Label selName, selDesc;
        MappingList mappings;
        PadButton selected = PadButton.A;
        int editProfile;

        // Sticks page
        StickView leftView, rightView;
        Slider speed, precision, curve, deadzone, scroll, trigger;
        ToggleSwitch smoothScroll, invertScroll;

        // Profiles page
        ListBox profileList;
        TextBox profName, profApps, nextCombo, prevCombo;
        ComboBox runningApps;
        ToggleSwitch autoSwitch, isDefault;
        int fieldsIndex = -1;    // which profile the name/apps boxes currently show

        // Pausing page
        ToggleSwitch autoPause;
        TextBox neverPause, alwaysPause;
        Label pauseStatus;
        string statusApp = "";

        // General page
        ComboBox startupBox;
        ToggleSwitch startOn, rumble, osd, battery, updates;
        TextBox toggleCombo;
        Slider repeatDelay, repeatRate;

        // About page
        Label updateStatus;
        FlatButton installUpdateBtn;
        UpdateInfo pendingUpdate;

        const int ContentPad = 32;

        public SettingsForm(ISettingsHost host, Config cfg, int startPage)
        {
            this.host = host;
            original = cfg;
            work = cfg.Clone();
            editProfile = Math.Max(0, Math.Min(host.Engine.ActiveProfile, work.Profiles.Count - 1));
            startupAtOpen = host.GetStartupMode();

            Text = "PadMouse settings";
            Icon = TrayIcons.MakeIcon(Theme.Accent);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = Theme.UiFont(9.75f);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1060, 740);
            KeyPreview = true;

            nav.Dock = DockStyle.Left;
            nav.Width = 210;
            nav.Add("\u25C9", "Buttons");
            nav.Add("◎", "Sticks & cursor");
            nav.Add("☰", "Profiles");
            nav.Add("⏸", "Games & pausing");
            nav.Add("◈", "Controllers");
            nav.Add("⚙", "General");
            nav.Add("ⓘ", "About");
            nav.SelectedChanged += delegate { ShowPage(nav.Selected); };

            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Bg;

            loading = true;
            pages.Add(BuildButtonsPage());
            pages.Add(BuildSticksPage());
            pages.Add(BuildProfilesPage());
            pages.Add(BuildPausePage());
            pages.Add(BuildControllersPage());
            pages.Add(BuildGeneralPage());
            pages.Add(BuildAboutPage());
            foreach (var p in pages) { p.Dock = DockStyle.Fill; p.Visible = false; content.Controls.Add(p); }
            content.Controls.Add(BuildFooter());

            Controls.Add(content);
            Controls.Add(nav);
            Theme.Apply(this);
            loading = false;

            nav.Selected = Math.Max(0, Math.Min(startPage, pages.Count - 1));
            ShowPage(nav.Selected);
            LoadButtonsPage();

            live.Tick += delegate { UpdateLive(); };
            previewTimer.Tick += delegate { previewTimer.Stop(); host.PreviewConfig(work.Clone()); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Win32.UseDarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); live.Start(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (dirty && !saved && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show(this, "Save your changes before closing?", "PadMouse", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes && !Save()) { e.Cancel = true; return; }
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            live.Stop(); previewTimer.Stop();
            if (!saved && dirty) host.PreviewConfig(original);   // undo live preview
            base.OnFormClosed(e);
        }

        void ShowPage(int i)
        {
            for (int p = 0; p < pages.Count; p++) pages[p].Visible = p == i;
            if (i == 2) RefreshProfileList();
        }

        void Changed()
        {
            if (loading) return;
            dirty = true;
            previewTimer.Stop(); previewTimer.Start();
        }

        // ================================================================== helpers

        Panel NewPage(string title, string subtitle)
        {
            var p = new Panel { BackColor = Theme.Bg, AutoScroll = false };
            var t = new Label { Text = title, UseMnemonic = false, Font = Theme.Semibold(18f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(ContentPad - 2, 22) };
            var s = new Label { Text = subtitle, ForeColor = Theme.TextMuted, AutoSize = false, Location = new Point(ContentPad, 60), Size = new Size(780, 22) };
            p.Controls.Add(t); p.Controls.Add(s);
            return p;
        }

        static Label Caption(string text, int x, int y, int w = 300)
        {
            return new Label { Text = text, ForeColor = Theme.TextMuted, AutoSize = false, Location = new Point(x, y), Size = new Size(w, 20), Font = Theme.UiFont(9f) };
        }

        static Label Heading(string text, int x, int y)
        {
            return new Label { Text = text, ForeColor = Theme.Text, AutoSize = true, Location = new Point(x, y), Font = Theme.Semibold(11f) };
        }

        static Label Note(string text, int x, int y, int w, int h)
        {
            return new Label { Text = text, ForeColor = Theme.TextMuted, AutoSize = false, Location = new Point(x, y), Size = new Size(w, h), Font = Theme.UiFont(9f) };
        }

        ToggleSwitch Toggle(Panel p, string text, int x, int y, bool value, Action<bool> set, int w = 380)
        {
            var t = new ToggleSwitch { Text = text, Location = new Point(x, y), Size = new Size(w, 30), Checked = value };
            t.CheckedChanged += delegate { set(t.Checked); Changed(); };
            p.Controls.Add(t);
            return t;
        }

        Slider MakeSlider(Panel p, string text, int x, int y, int w, double min, double max, double step, double value, Func<double, string> fmt, Action<double> set, string hint = null)
        {
            var s = new Slider { Text = text, Location = new Point(x, y), Size = new Size(w, 52), Hint = hint };
            s.Format = fmt;
            s.Setup(min, max, step, value);
            s.ValueChanged += delegate { set(s.Value); Changed(); };
            p.Controls.Add(s);
            return s;
        }

        static ComboBox Combo(int x, int y, int w, bool editable)
        {
            return new ComboBox { Location = new Point(x, y), Width = w, DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, MaxDropDownItems = 18, Font = Theme.UiFont(10f) };
        }

        static FlatButton Btn(string text, int x, int y, int w, bool primary = false)
        {
            return new FlatButton { Text = text, Location = new Point(x, y), Size = new Size(w, 34), Primary = primary };
        }

        Profile EditProfile { get { return work.Profiles[Math.Min(editProfile, work.Profiles.Count - 1)]; } }

        // ================================================================== footer

        Control BuildFooter()
        {
            var f = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Sidebar };
            footerStatus = new Label { AutoSize = false, Location = new Point(ContentPad, 0), Size = new Size(560, 64), TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextMuted };
            saveBtn = Btn("Save", 0, 15, 110, true);
            var cancel = Btn("Cancel", 0, 15, 100);
            saveBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            f.Controls.Add(footerStatus); f.Controls.Add(saveBtn); f.Controls.Add(cancel);
            f.Layout += delegate
            {
                saveBtn.Left = f.ClientSize.Width - ContentPad - saveBtn.Width;
                cancel.Left = saveBtn.Left - 10 - cancel.Width;
            };
            saveBtn.Click += delegate { if (Save()) Close(); };
            cancel.Click += delegate { dirty = false; host.PreviewConfig(original); Close(); };
            return f;
        }

        bool Save()
        {
            CommitProfileFields();
            string problems = work.Validate();
            if (problems != null)
            {
                MessageBox.Show(this, problems, "Please fix these first", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var mode = (StartupMode)Math.Max(0, startupBox.SelectedIndex);
            if (mode != startupAtOpen)
            {
                string err;
                if (!host.SetStartupMode(mode, out err))
                {
                    MessageBox.Show(this, "Couldn't change the startup setting: " + err, "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    startupBox.SelectedIndex = (int)host.GetStartupMode();
                }
                startupAtOpen = host.GetStartupMode();
            }
            try { host.SaveConfig(work.Clone()); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't save: " + ex.Message, "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            saved = true; dirty = false;
            return true;
        }

        // ================================================================== live updates

        void UpdateLive()
        {
            var eng = host.Engine;
            uint mask = eng.ConnectedSlot >= 0 ? eng.LiveMask : 0;

            var fam = (PadFamily)eng.ActiveFamily;
            if (eng.ConnectedSlot >= 0 && fam != Names.Family && !wizardOpen)
            {
                Names.Family = fam;
                selName.Text = Names.Of(selected);
                pad.Invalidate(); mappings.Invalidate();
            }
            if (pages[4].Visible) RefreshControllers(false);

            if (pages[0].Visible)
            {
                pad.PressedMask = mask;
                pad.Invalidate();
                // Press a button on the controller to select it.
                uint pressed = mask & ~lastMask;
                if (pressed != 0)
                    for (int i = 0; i < 16; i++)
                        if ((pressed & (1u << i)) != 0) { SelectButton((PadButton)i); break; }
            }
            lastMask = mask;

            if (pages[1].Visible)
            {
                leftView.RawX = eng.LiveLX; leftView.RawY = eng.LiveLY;
                rightView.RawX = eng.LiveRX; rightView.RawY = eng.LiveRY;
                leftView.Deadzone = rightView.Deadzone = work.Deadzone;
                leftView.Curve = rightView.Curve = work.Curve;
                leftView.ModeText = EditProfile.LeftStick.ToString();
                rightView.ModeText = EditProfile.RightStick.ToString();
                leftView.Invalidate(); rightView.Invalidate();
            }

            if (pages[3].Visible) UpdatePauseStatus();

            string s;
            if (eng.ConnectedSlot < 0) s = "⚠  No controller detected: plug in or pair your Xbox controller";
            else
            {
                s = "●  " + (string.IsNullOrEmpty(eng.ActiveName) ? "Controller connected" : eng.ActiveName);
                string bat = Names.Battery(eng.BatteryType, eng.BatteryLevel);
                if (bat.Length > 0) s += "  ·  " + bat;
                s += "  ·  Profile: " + work.Profiles[Math.Min(eng.ActiveProfile, work.Profiles.Count - 1)].Name;
                if (!eng.Enabled) s += "  ·  switched OFF";
                else if (eng.Paused) s += "  ·  paused for game";
            }
            if (AppInfo.IsAdmin) s += "  ·  admin";
            if (footerStatus.Text != s)
            {
                footerStatus.Text = s;
                footerStatus.ForeColor = eng.ConnectedSlot < 0 ? Theme.Warning : Theme.TextMuted;
            }
        }

        // ================================================================== page: buttons

        Panel BuildButtonsPage()
        {
            var p = NewPage("Buttons", "Click a button on the picture, or press it on your controller, then choose what it does.");

            p.Controls.Add(Caption("Profile", ContentPad, 100, 60));
            profilePicker = Combo(ContentPad + 60, 96, 200, false);
            profilePicker.SelectedIndexChanged += delegate
            {
                if (loading || profilePicker.SelectedIndex < 0) return;
                editProfile = profilePicker.SelectedIndex;
                LoadButtonsPage();
            };
            p.Controls.Add(profilePicker);
            var manage = new LinkLabel { Text = "Manage profiles", Location = new Point(ContentPad + 276, 100), AutoSize = true };
            manage.LinkClicked += delegate { nav.Selected = 2; };
            p.Controls.Add(manage);

            pad = new ControllerView { Location = new Point(ContentPad - 8, 138), Size = new Size(470, 262) };
            pad.LabelFor = b => ButtonAction.Describe(EditProfile.Get(b));
            pad.ButtonClicked += b => SelectButton(b);
            p.Controls.Add(pad);

            int rx = 520;
            selName = new Label { Location = new Point(rx, 140), AutoSize = true, Font = Theme.Semibold(15f), ForeColor = Theme.Text };
            p.Controls.Add(selName);
            p.Controls.Add(Caption("Action (pick one, or type a key combo like Key:Ctrl+C)", rx, 178, 310));
            actionBox = Combo(rx, 200, 300, true);
            actionBox.Items.AddRange(ButtonAction.Suggestions);
            actionBox.TextChanged += delegate { OnActionEdited(); };
            p.Controls.Add(actionBox);
            selDesc = new Label { Location = new Point(rx, 234), Size = new Size(300, 40), ForeColor = Theme.TextMuted };
            p.Controls.Add(selDesc);

            p.Controls.Add(Caption("Left stick", rx, 290, 140));
            leftStickBox = Combo(rx, 312, 140, false);
            leftStickBox.Items.AddRange(new object[] { StickMode.Mouse, StickMode.Scroll, StickMode.None });
            leftStickBox.SelectedIndexChanged += delegate { if (!loading) { EditProfile.LeftStick = (StickMode)leftStickBox.SelectedItem; Changed(); } };
            p.Controls.Add(leftStickBox);
            p.Controls.Add(Caption("Right stick", rx + 160, 290, 140));
            rightStickBox = Combo(rx + 160, 312, 140, false);
            rightStickBox.Items.AddRange(new object[] { StickMode.Mouse, StickMode.Scroll, StickMode.None });
            rightStickBox.SelectedIndexChanged += delegate { if (!loading) { EditProfile.RightStick = (StickMode)rightStickBox.SelectedItem; Changed(); } };
            p.Controls.Add(rightStickBox);

            p.Controls.Add(Note("Combos: " + "use the General and Profiles pages to change the on/off and profile-switch combos.", rx, 352, 300, 40));

            mappings = new MappingList { Location = new Point(ContentPad, 410), Size = new Size(796, 222) };
            mappings.LabelFor = b => EditProfile.Get(b);
            mappings.ButtonClicked += b => SelectButton(b);
            p.Controls.Add(mappings);
            return p;
        }

        void LoadButtonsPage()
        {
            bool wasLoading = loading;
            loading = true;
            profilePicker.Items.Clear();
            foreach (var pr in work.Profiles) profilePicker.Items.Add(pr.Name);
            profilePicker.SelectedIndex = Math.Min(editProfile, work.Profiles.Count - 1);
            leftStickBox.SelectedItem = EditProfile.LeftStick;
            rightStickBox.SelectedItem = EditProfile.RightStick;
            loading = wasLoading;
            SelectButton(selected);
        }

        void SelectButton(PadButton b)
        {
            selected = b;
            pad.SelectedButton = b;
            mappings.Selected = b;
            selName.Text = Names.Of(b);
            bool wasLoading = loading;
            loading = true;
            actionBox.Text = EditProfile.Get(b);
            loading = wasLoading;
            UpdateDescription();
            pad.Invalidate(); mappings.Invalidate();
        }

        void OnActionEdited()
        {
            if (loading) return;
            EditProfile.Buttons[selected] = actionBox.Text.Trim();
            UpdateDescription();
            mappings.Invalidate();
            Changed();
        }

        void UpdateDescription()
        {
            string text = actionBox.Text.Trim();
            ButtonAction a; string err;
            bool ok = ButtonAction.TryParse(text, out a, out err);
            selDesc.Text = ok ? ButtonAction.Describe(text) : err;
            selDesc.ForeColor = ok ? Theme.TextMuted : Theme.Danger;
            string extra = "";
            uint bit = PadEngine.Bit(selected);
            uint m;
            string e;
            if (Config.TryParseCombo(work.ToggleCombo, out m, out e) && (m & bit) != 0) extra = "Also part of the on/off combo.";
            else if (Config.TryParseCombo(work.NextProfileCombo, out m, out e) && (m & bit) != 0) extra = "Also part of the next-profile combo.";
            else if (Config.TryParseCombo(work.PrevProfileCombo, out m, out e) && (m & bit) != 0) extra = "Also part of the previous-profile combo.";
            if (extra.Length > 0 && ok) selDesc.Text += "\n" + extra;
        }

        // ================================================================== page: sticks

        Panel BuildSticksPage()
        {
            var p = NewPage("Sticks & cursor", "Changes apply straight away so you can feel them. Press Save to keep them.");

            leftView = new StickView { Caption = "Left stick", Location = new Point(ContentPad, 98), Size = new Size(190, 214) };
            rightView = new StickView { Caption = "Right stick", Location = new Point(ContentPad + 204, 98), Size = new Size(190, 214) };
            p.Controls.Add(leftView); p.Controls.Add(rightView);

            smoothScroll = Toggle(p, "Smooth scrolling", ContentPad, 326, work.SmoothScroll, v => work.SmoothScroll = v, 190);
            invertScroll = Toggle(p, "Invert scrolling", ContentPad + 204, 326, work.InvertScroll, v => work.InvertScroll = v, 190);

            int sx = 450, sw = 380, y = 92;
            speed = MakeSlider(p, "Cursor speed", sx, y, sw, 200, 6000, 50, work.CursorSpeed, v => ((int)v) + " px/s", v => work.CursorSpeed = v); y += 56;
            precision = MakeSlider(p, "Precision speed", sx, y, sw, 0.05, 1, 0.05, work.PrecisionFactor, v => ((int)(v * 100)) + "%", v => work.PrecisionFactor = v, "while LT is held"); y += 56;
            curve = MakeSlider(p, "Response curve", sx, y, sw, 1, 4, 0.1, work.Curve, v => v.ToString("0.0"), v => work.Curve = v, "higher = finer small moves"); y += 56;
            deadzone = MakeSlider(p, "Deadzone", sx, y, sw, 0, 0.5, 0.01, work.Deadzone, v => ((int)Math.Round(v * 100)) + "%", v => work.Deadzone = v, "the red circle"); y += 56;
            scroll = MakeSlider(p, "Scroll speed", sx, y, sw, 60, 4000, 20, work.ScrollSpeed, v => ((int)v) + "", v => work.ScrollSpeed = v); y += 56;
            trigger = MakeSlider(p, "Trigger press point", sx, y, sw, 0.05, 0.95, 0.05, work.TriggerThreshold, v => ((int)(v * 100)) + "%", v => work.TriggerThreshold = v);

            var test = new TestArea { Location = new Point(ContentPad, 432), Size = new Size(796, 200) };
            p.Controls.Add(test);
            return p;
        }

        // ================================================================== page: profiles

        Panel BuildProfilesPage()
        {
            var p = NewPage("Profiles", "Each profile is a full button layout. PadMouse can switch automatically depending on the app in front.");

            profileList = new ListBox { Location = new Point(ContentPad, 100), Size = new Size(230, 260), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36, IntegralHeight = false };
            profileList.DrawItem += DrawProfileItem;
            profileList.SelectedIndexChanged += delegate { if (!loading) { CommitProfileFields(); LoadProfileFields(); } };
            p.Controls.Add(profileList);

            var add = Btn("New", ContentPad, 370, 72);
            var copy = Btn("Copy", ContentPad + 79, 370, 72);
            var del = Btn("Delete", ContentPad + 158, 370, 72);
            var up = Btn("Move up", ContentPad, 410, 111);
            var down = Btn("Move down", ContentPad + 119, 410, 111);
            add.Click += delegate { AddProfile(Profile.Desktop(), "New profile"); };
            copy.Click += delegate { if (profileList.SelectedIndex >= 0) { CommitProfileFields(); var c = work.Profiles[profileList.SelectedIndex].Clone(); c.Apps.Clear(); AddProfile(c, c.Name + " copy"); } };
            del.Click += delegate { DeleteProfile(); };
            up.Click += delegate { MoveProfile(-1); };
            down.Click += delegate { MoveProfile(1); };
            p.Controls.Add(add); p.Controls.Add(copy); p.Controls.Add(del); p.Controls.Add(up); p.Controls.Add(down);

            int x = 300;
            p.Controls.Add(Caption("Name", x, 100));
            profName = new TextBox { Location = new Point(x, 122), Width = 300 };
            profName.TextChanged += delegate { if (!loading) { Changed(); } };
            profName.Leave += delegate { CommitProfileFields(); RefreshProfileList(); };
            p.Controls.Add(profName);

            p.Controls.Add(Caption("Switch to this profile automatically for these apps", x, 160, 500));
            profApps = new TextBox { Location = new Point(x, 182), Width = 500 };
            profApps.TextChanged += delegate { if (!loading) Changed(); };
            p.Controls.Add(profApps);
            p.Controls.Add(Note("App names as shown in Task Manager > Details, without .exe, separated by commas. e.g. chrome, vlc", x, 210, 500, 20));

            runningApps = Combo(x, 238, 220, false);
            runningApps.DropDown += delegate { FillRunningApps(runningApps); };
            p.Controls.Add(runningApps);
            var addApp = Btn("Add running app", x + 230, 236, 150);
            addApp.Click += delegate
            {
                if (runningApps.SelectedItem == null) return;
                var list = Config.ParseAppList(profApps.Text);
                string n = runningApps.SelectedItem.ToString();
                if (!list.Contains(n)) list.Add(n);
                profApps.Text = Config.JoinApps(list);
            };
            p.Controls.Add(addApp);

            isDefault = Toggle(p, "Use this profile when no other profile matches", x, 290, false, v => { }, 500);
            isDefault.CheckedChanged += delegate
            {
                if (loading || profileList.SelectedIndex < 0) return;
                if (isDefault.Checked) work.DefaultProfile = work.Profiles[profileList.SelectedIndex].Name;
                else if (work.Profiles.Count > 0 && work.DefaultProfile == work.Profiles[profileList.SelectedIndex].Name)
                {
                    loading = true; isDefault.Checked = true; loading = false;   // there must always be a default
                }
                profileList.Invalidate();
            };

            var edit = new LinkLabel { Text = "Edit this profile's buttons", Location = new Point(x, 330), AutoSize = true };
            edit.LinkClicked += delegate { CommitProfileFields(); editProfile = Math.Max(0, profileList.SelectedIndex); LoadButtonsPage(); nav.Selected = 0; };
            p.Controls.Add(edit);

            p.Controls.Add(Heading("Switching", x, 380));
            autoSwitch = Toggle(p, "Switch profiles automatically by app", x, 408, work.AutoSwitchProfiles, v => work.AutoSwitchProfiles = v, 500);
            p.Controls.Add(Caption("Next profile combo", x, 448, 200));
            nextCombo = new TextBox { Location = new Point(x, 470), Width = 200, Text = work.NextProfileCombo };
            nextCombo.TextChanged += delegate { work.NextProfileCombo = nextCombo.Text.Trim(); Changed(); };
            p.Controls.Add(nextCombo);
            p.Controls.Add(Caption("Previous profile combo", x + 230, 448, 200));
            prevCombo = new TextBox { Location = new Point(x + 230, 470), Width = 200, Text = work.PrevProfileCombo };
            prevCombo.TextChanged += delegate { work.PrevProfileCombo = prevCombo.Text.Trim(); Changed(); };
            p.Controls.Add(prevCombo);
            p.Controls.Add(Note("Hold View, then press RB or LB to cycle profiles. Button names: A B X Y LB RB LT RT Back Start L3 R3 DPadUp... joined with +. Use None to turn a combo off. Picking a profile by hand keeps it until you switch to another app.", x, 504, 500, 60));
            return p;
        }

        void DrawProfileItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= work.Profiles.Count) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Surface2 : Theme.Surface)) g.FillRectangle(b, e.Bounds);
            if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 8, 3, e.Bounds.Height - 16);
            var prof = work.Profiles[e.Index];
            TextRenderer.DrawText(g, prof.Name, Font, new Rectangle(e.Bounds.X + 14, e.Bounds.Y, e.Bounds.Width - 100, e.Bounds.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            string tag = prof.Name.Equals(work.DefaultProfile, StringComparison.OrdinalIgnoreCase) ? "default" : prof.Apps.Count > 0 ? prof.Apps.Count + " app" + (prof.Apps.Count == 1 ? "" : "s") : "";
            using (var f = Theme.UiFont(8.5f))
                TextRenderer.DrawText(g, tag, f, new Rectangle(e.Bounds.Right - 90, e.Bounds.Y, 80, e.Bounds.Height), Theme.TextMuted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        void RefreshProfileList()
        {
            if (profileList == null) return;
            if (!loading) CommitProfileFields();
            bool wasLoading = loading;
            loading = true;
            int sel = profileList.SelectedIndex;
            profileList.Items.Clear();
            foreach (var pr in work.Profiles) profileList.Items.Add(pr.Name);
            profileList.SelectedIndex = sel >= 0 && sel < work.Profiles.Count ? sel : Math.Min(editProfile, work.Profiles.Count - 1);
            loading = wasLoading;
            LoadProfileFields();
        }

        void LoadProfileFields()
        {
            int i = profileList.SelectedIndex;
            if (i < 0) return;
            bool wasLoading = loading;
            loading = true;
            fieldsIndex = i;
            var pr = work.Profiles[i];
            profName.Text = pr.Name;
            profApps.Text = Config.JoinApps(pr.Apps);
            isDefault.Checked = pr.Name.Equals(work.DefaultProfile, StringComparison.OrdinalIgnoreCase);
            loading = wasLoading;
        }

        /// <summary>Copies the name/apps text boxes into the selected profile.</summary>
        void CommitProfileFields()
        {
            if (profileList == null) return;
            int i = fieldsIndex;
            if (i < 0 || i >= work.Profiles.Count) return;
            var pr = work.Profiles[i];
            string newName = profName.Text.Trim();
            if (newName.Length > 0 && newName != pr.Name)
            {
                if (work.DefaultProfile.Equals(pr.Name, StringComparison.OrdinalIgnoreCase)) work.DefaultProfile = newName;
                pr.Name = newName;
                if (i < profileList.Items.Count) profileList.Items[i] = newName;
            }
            pr.Apps = Config.ParseAppList(profApps.Text);
            profileList.Invalidate();
        }

        void AddProfile(Profile p, string name)
        {
            string n = name; int k = 2;
            while (work.FindProfile(n) != null) n = name + " " + k++;
            p.Name = n;
            work.Profiles.Add(p);
            RefreshProfileList();
            profileList.SelectedIndex = work.Profiles.Count - 1;
            Changed();
        }

        void DeleteProfile()
        {
            int i = profileList.SelectedIndex;
            if (i < 0) return;
            if (work.Profiles.Count <= 1) { MessageBox.Show(this, "You need at least one profile.", "PadMouse"); return; }
            var pr = work.Profiles[i];
            if (MessageBox.Show(this, "Delete the profile '" + pr.Name + "'?", "PadMouse", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            fieldsIndex = -1;
            work.Profiles.RemoveAt(i);
            if (work.FindProfile(work.DefaultProfile) == null) work.DefaultProfile = work.Profiles[0].Name;
            editProfile = Math.Min(editProfile, work.Profiles.Count - 1);
            profileList.SelectedIndex = -1;
            RefreshProfileList();
            profileList.SelectedIndex = Math.Min(i, work.Profiles.Count - 1);
            LoadButtonsPage();
            Changed();
        }

        void MoveProfile(int d)
        {
            int i = profileList.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= work.Profiles.Count) return;
            CommitProfileFields();
            var t = work.Profiles[i]; work.Profiles[i] = work.Profiles[j]; work.Profiles[j] = t;
            fieldsIndex = -1;
            RefreshProfileList();
            profileList.SelectedIndex = j;
            Changed();
        }

        static void FillRunningApps(ComboBox box)
        {
            box.Items.Clear();
            foreach (string n in AppWatcher.RunningWindowedApps()) box.Items.Add(n);
        }

        // ================================================================== page: pausing

        Panel BuildPausePage()
        {
            var p = NewPage("Games & pausing", "PadMouse steps aside while you play, so a game gets the controller to itself.");
            int x = ContentPad;
            autoPause = Toggle(p, "Pause automatically while a fullscreen app or game is in front", x, 100, work.AutoPause, v => work.AutoPause = v, 600);

            p.Controls.Add(Caption("Never pause for these apps (e.g. fullscreen video in a browser)", x, 146, 600));
            neverPause = new TextBox { Location = new Point(x, 168), Width = 796, Text = Config.JoinApps(work.NeverPauseApps) };
            neverPause.TextChanged += delegate { work.NeverPauseApps = Config.ParseAppList(neverPause.Text); Changed(); };
            p.Controls.Add(neverPause);

            p.Controls.Add(Caption("Always pause for these apps, even when they're windowed", x, 210, 600));
            alwaysPause = new TextBox { Location = new Point(x, 232), Width = 796, Text = Config.JoinApps(work.AlwaysPauseApps) };
            alwaysPause.TextChanged += delegate { work.AlwaysPauseApps = Config.ParseAppList(alwaysPause.Text); Changed(); };
            p.Controls.Add(alwaysPause);

            var box = new Panel { Location = new Point(x, 284), Size = new Size(796, 150), BackColor = Theme.Surface };
            box.Controls.Add(new Label { Text = "Right now", Location = new Point(16, 14), AutoSize = true, Font = Theme.Semibold(11f), ForeColor = Theme.Text, BackColor = Theme.Surface });
            pauseStatus = new Label { Location = new Point(16, 44), Size = new Size(760, 44), ForeColor = Theme.TextMuted, BackColor = Theme.Surface };
            box.Controls.Add(pauseStatus);
            var never = Btn("Never pause this app", 16, 96, 190);
            var always = Btn("Always pause this app", 216, 96, 190);
            never.Click += delegate { AddToList(neverPause, alwaysPause); };
            always.Click += delegate { AddToList(alwaysPause, neverPause); };
            box.Controls.Add(never); box.Controls.Add(always);
            p.Controls.Add(box);

            p.Controls.Add(Note("Switch to the game or app you want to check (Alt+Tab), then come back here; \"Right now\" shows the last app that was in front.\n" +
                                "You can still switch PadMouse off and on yourself with " + Names.Combo(work.ToggleCombo) + " while it isn't paused.", x, 450, 796, 60));
            return p;
        }

        void AddToList(TextBox target, TextBox other)
        {
            if (string.IsNullOrEmpty(statusApp)) return;
            var list = Config.ParseAppList(target.Text);
            if (!list.Contains(statusApp)) list.Add(statusApp);
            target.Text = Config.JoinApps(list);
            var o = Config.ParseAppList(other.Text);
            if (o.Remove(statusApp)) other.Text = Config.JoinApps(o);
        }

        void UpdatePauseStatus()
        {
            var w = host.Watcher;
            statusApp = w.ForegroundApp ?? "";
            string s;
            if (statusApp.Length == 0) s = "No other app has been in front yet.";
            else
            {
                bool always = work.AlwaysPauseApps.Contains(statusApp);
                bool never = work.NeverPauseApps.Contains(statusApp);
                bool would = always || (work.AutoPause && w.ForegroundFullscreen && !never);
                s = "Last app in front: " + statusApp + "   ·   fullscreen: " + (w.ForegroundFullscreen ? "yes" : "no") +
                    (always ? "   ·   on always-pause list" : never ? "   ·   on never-pause list" : "") +
                    "\nPadMouse would " + (would ? "PAUSE while it's in front." : "keep running.");
            }
            if (pauseStatus.Text != s) pauseStatus.Text = s;
        }

        // ================================================================== page: controllers

        ListBox padList;
        FlatButton setupBtn;
        Label sdlNote;
        PadInfo[] shownPads;
        bool wizardOpen;

        Panel BuildControllersPage()
        {
            var p = NewPage("Controllers", "Xbox, PlayStation (DualShock 4 / DualSense), Nintendo Switch Pro and most USB or Bluetooth controllers work.");
            int x = ContentPad;
            p.Controls.Add(Caption("Connected now (PadMouse follows whichever one you used last)", x, 100, 600));
            padList = new ListBox { Location = new Point(x, 124), Size = new Size(560, 230), DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 52, IntegralHeight = false };
            padList.DrawItem += DrawPadItem;
            padList.SelectedIndexChanged += delegate { UpdateSetupButton(); };
            padList.DoubleClick += delegate { if (setupBtn.Enabled) RunWizard(); };
            p.Controls.Add(padList);

            setupBtn = Btn("Set up this controller…", x + 576, 124, 220, true);
            setupBtn.Enabled = false;
            setupBtn.Click += delegate { RunWizard(); };
            p.Controls.Add(setupBtn);
            var forget = Btn("Forget saved set-ups", x + 576, 168, 220);
            forget.Click += delegate
            {
                if (MessageBox.Show(this, "Forget the controllers you've set up by hand? You can set them up again any time. (Takes effect after PadMouse restarts.)", "PadMouse", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { System.IO.File.Delete(PadManager.MappingsFile); } catch { }
            };
            p.Controls.Add(forget);

            sdlNote = Note("", x, 364, 796, 40);
            p.Controls.Add(sdlNote);

            p.Controls.Add(Heading("Good to know", x, 414));
            p.Controls.Add(Note(
                "• PlayStation and Switch controllers: connect by USB cable or pair them in Windows Bluetooth settings.\n" +
                "• Button names on the Buttons page change to match your controller (Cross/Circle, L1/R1 and so on).\n" +
                "• If you use DS4Windows, Steam Input or similar, PadMouse sees an Xbox controller. That works too.\n" +
                "• A controller marked \"needs set-up\" isn't known to PadMouse yet. Set it up once and it's remembered.\n" +
                "• Picking up a different controller and pressing a button switches to it (that first press is ignored).",
                x, 442, 796, 110));
            return p;
        }

        void RefreshControllers(bool force)
        {
            var list = host.Engine.Pads.Connected;
            if (!force && ReferenceEquals(list, shownPads)) return;
            shownPads = list;
            int sel = padList.SelectedIndex;
            padList.Items.Clear();
            foreach (var pi in list) padList.Items.Add(pi.Name);
            if (padList.Items.Count == 0) padList.Items.Add("(none)");
            padList.SelectedIndex = Math.Min(Math.Max(sel, 0), padList.Items.Count - 1);
            var pm = host.Engine.Pads;
            sdlNote.Text = pm.SdlProblem != null ? "Only Xbox controllers can be used on this PC right now (" + pm.SdlProblem + ")." : "";
            sdlNote.ForeColor = Theme.Warning;
            UpdateSetupButton();
        }

        void UpdateSetupButton()
        {
            int i = padList.SelectedIndex;
            setupBtn.Enabled = shownPads != null && i >= 0 && i < shownPads.Length && shownPads[i].NeedsSetup;
        }

        void DrawPadItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Surface2 : Theme.Surface)) g.FillRectangle(b, e.Bounds);
            if (shownPads == null || e.Index >= shownPads.Length)
            {
                TextRenderer.DrawText(g, "No controllers connected", Font, new Rectangle(e.Bounds.X + 16, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height), Theme.TextMuted, TextFormatFlags.VerticalCenter);
                return;
            }
            var pi = shownPads[e.Index];
            if (pi.Active) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y + 10, 3, e.Bounds.Height - 20);
            TextRenderer.DrawText(g, pi.Name, Font, new Rectangle(e.Bounds.X + 16, e.Bounds.Y + 6, e.Bounds.Width - 140, 22), Theme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using (var f = Theme.UiFont(8.5f))
                TextRenderer.DrawText(g, Names.FamilyName(pi.Family) + " controller", f, new Rectangle(e.Bounds.X + 16, e.Bounds.Y + 28, 300, 18), Theme.TextMuted, TextFormatFlags.NoPrefix);
            string tag = pi.NeedsSetup ? "needs set-up" : pi.Active ? "in use" : "connected";
            Color tc = pi.NeedsSetup ? Theme.Warning : pi.Active ? Theme.Accent : Theme.TextMuted;
            using (var f = Theme.UiFont(8.5f))
                TextRenderer.DrawText(g, tag, f, new Rectangle(e.Bounds.Right - 120, e.Bounds.Y, 108, e.Bounds.Height), tc, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        void RunWizard()
        {
            int i = padList.SelectedIndex;
            if (shownPads == null || i < 0 || i >= shownPads.Length || !shownPads[i].NeedsSetup) return;
            wizardOpen = true;
            using (var w = new MappingWizard(host.Engine, shownPads[i])) w.ShowDialog(this);
            wizardOpen = false;
            RefreshControllers(true);
        }

        // ================================================================== page: general

        Panel BuildGeneralPage()
        {
            var p = NewPage("General", "Startup, feedback and the on/off combo.");
            int x = ContentPad, x2 = 470;

            p.Controls.Add(Heading("Startup", x, 98));
            startupBox = Combo(x, 128, 380, false);
            startupBox.Items.AddRange(new object[] { "Don't start automatically", "Start when I sign in to Windows", "Start when I sign in, as administrator" });
            startupBox.SelectedIndex = (int)startupAtOpen;
            startupBox.SelectedIndexChanged += delegate { if (!loading) Changed(); };
            p.Controls.Add(startupBox);
            p.Controls.Add(Note("Administrator mode lets PadMouse click in admin windows like Task Manager. It shows a Windows permission prompt when you save.", x, 160, 390, 40));
            startOn = Toggle(p, "Start switched on", x, 206, work.StartEnabled, v => work.StartEnabled = v);

            p.Controls.Add(Heading("Feedback", x, 254));
            rumble = Toggle(p, "Rumble when switching on/off or changing profile", x, 282, work.Rumble, v => work.Rumble = v);
            osd = Toggle(p, "Show pop-ups on screen (on/off, profile changes)", x, 316, work.ShowOsd, v => work.ShowOsd = v);
            battery = Toggle(p, "Warn me when the controller battery is low", x, 350, work.BatteryWarnings, v => work.BatteryWarnings = v);
            updates = Toggle(p, "Check for updates automatically", x, 384, work.CheckForUpdates, v => work.CheckForUpdates = v);

            p.Controls.Add(Heading("On/off combo", x2, 98));
            toggleCombo = new TextBox { Location = new Point(x2, 128), Width = 200, Text = work.ToggleCombo };
            toggleCombo.TextChanged += delegate { work.ToggleCombo = toggleCombo.Text.Trim(); Changed(); };
            p.Controls.Add(toggleCombo);
            p.Controls.Add(Note("Buttons held together, e.g. Back+Start (View + Menu) or L3+R3.", x2, 160, 340, 40));

            p.Controls.Add(Heading("Held keys", x2, 254));
            repeatDelay = MakeSlider(p, "Repeat delay", x2, 280, 340, 100, 1000, 20, work.KeyRepeatDelay, v => ((int)v) + " ms", v => work.KeyRepeatDelay = (int)v);
            repeatRate = MakeSlider(p, "Repeat interval", x2, 336, 340, 15, 200, 5, work.KeyRepeatRate, v => ((int)v) + " ms", v => work.KeyRepeatRate = (int)v);

            int by = 476;
            p.Controls.Add(Heading("Tools", x, by - 30));
            var welcome = Btn("Show welcome guide", x, by, 180);
            welcome.Click += delegate { host.ShowWelcome(); };
            var admin = Btn(AppInfo.IsAdmin ? "Running as administrator" : "Restart as administrator", x + 190, by, 210);
            admin.Enabled = !AppInfo.IsAdmin;
            admin.Click += delegate
            {
                if (dirty && MessageBox.Show(this, "Save your changes first?", "PadMouse", MessageBoxButtons.YesNo) == DialogResult.Yes && !Save()) return;
                dirty = false; host.RestartAsAdmin();
            };
            var cfgFile = Btn("Open config file", x + 410, by, 160);
            cfgFile.Click += delegate { try { Process.Start("notepad.exe", "\"" + Config.FilePath + "\""); } catch { } };
            var defaults = Btn("Restore defaults", x + 580, by, 160);
            defaults.Click += delegate
            {
                if (MessageBox.Show(this, "Reset every setting and profile to the defaults? (Nothing is saved until you press Save.)", "PadMouse", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var fresh = new Config { FirstRunDone = true, LastUpdateCheck = work.LastUpdateCheck };
                work = fresh;
                editProfile = 0;
                ReloadAllPages();
                Changed();
            };
            p.Controls.Add(welcome); p.Controls.Add(admin); p.Controls.Add(cfgFile); p.Controls.Add(defaults);
            return p;
        }

        void ReloadAllPages()
        {
            fieldsIndex = -1;
            loading = true;
            speed.Value = work.CursorSpeed; precision.Value = work.PrecisionFactor; curve.Value = work.Curve;
            deadzone.Value = work.Deadzone; scroll.Value = work.ScrollSpeed; trigger.Value = work.TriggerThreshold;
            smoothScroll.Checked = work.SmoothScroll; invertScroll.Checked = work.InvertScroll;
            autoSwitch.Checked = work.AutoSwitchProfiles; nextCombo.Text = work.NextProfileCombo; prevCombo.Text = work.PrevProfileCombo;
            autoPause.Checked = work.AutoPause; neverPause.Text = Config.JoinApps(work.NeverPauseApps); alwaysPause.Text = Config.JoinApps(work.AlwaysPauseApps);
            startOn.Checked = work.StartEnabled; rumble.Checked = work.Rumble; osd.Checked = work.ShowOsd; battery.Checked = work.BatteryWarnings;
            updates.Checked = work.CheckForUpdates; toggleCombo.Text = work.ToggleCombo;
            repeatDelay.Value = work.KeyRepeatDelay; repeatRate.Value = work.KeyRepeatRate;
            loading = false;
            profileList.SelectedIndex = -1;
            RefreshProfileList();
            LoadButtonsPage();
        }

        // ================================================================== page: about

        Panel BuildAboutPage()
        {
            var p = NewPage("About", "");
            int x = ContentPad;
            var logo = new PictureBox { Location = new Point(x, 96), Size = new Size(72, 72), Image = TrayIcons.Render(72, Theme.Accent), BackColor = Theme.Bg };
            p.Controls.Add(logo);
            p.Controls.Add(new Label { Text = "PadMouse", Font = Theme.Semibold(20f), Location = new Point(x + 88, 98), AutoSize = true, ForeColor = Theme.Text });
            p.Controls.Add(new Label { Text = "Version " + AppInfo.VersionText + (AppInfo.IsAdmin ? "  ·  running as administrator" : ""), Location = new Point(x + 90, 138), AutoSize = true, ForeColor = Theme.TextMuted });
            p.Controls.Add(Note("Use your Xbox controller as a mouse and keyboard on Windows.", x, 186, 700, 22));

            p.Controls.Add(Heading("Updates", x, 226));
            updateStatus = new Label { Location = new Point(x, 254), Size = new Size(560, 22), ForeColor = Theme.TextMuted,
                Text = AppInfo.UpdatesConfigured ? "Not checked yet." : "Update checks aren't set up yet (no GitHub repository configured)." };
            p.Controls.Add(updateStatus);
            var check = Btn("Check for updates", x, 282, 170);
            check.Enabled = AppInfo.UpdatesConfigured;
            check.Click += delegate
            {
                updateStatus.Text = "Checking…";
                host.CheckForUpdates(true, (info, err) => ShowUpdateResult(info, err));
            };
            installUpdateBtn = Btn("Download and install", x + 180, 282, 190, true);
            installUpdateBtn.Visible = false;
            installUpdateBtn.Click += delegate { if (pendingUpdate != null) host.InstallUpdate(pendingUpdate); };
            p.Controls.Add(check); p.Controls.Add(installUpdateBtn);

            p.Controls.Add(Heading("Links", x, 340));
            var repo = new LinkLabel { Text = AppInfo.UpdatesConfigured ? "Project page on GitHub" : "Project page (not published yet)", Location = new Point(x, 370), AutoSize = true, Enabled = AppInfo.UpdatesConfigured };
            repo.LinkClicked += delegate { Open(AppInfo.RepoUrl); };
            var issues = new LinkLabel { Text = "Report a problem", Location = new Point(x + 220, 370), AutoSize = true, Enabled = AppInfo.UpdatesConfigured };
            issues.LinkClicked += delegate { Open(AppInfo.RepoUrl + "/issues"); };
            var folder = new LinkLabel { Text = "Open settings & log folder", Location = new Point(x + 380, 370), AutoSize = true };
            folder.LinkClicked += delegate { Directory.CreateDirectory(Config.Folder); Open(Config.Folder); };
            p.Controls.Add(repo); p.Controls.Add(issues); p.Controls.Add(folder);

            p.Controls.Add(Heading("Installation", x, 416));
            bool installed = Installer.IsInstalledCopy;
            p.Controls.Add(Note(installed ? "Installed in " + Installer.InstallDir : "Running without installing, from " + Path.GetDirectoryName(AppInfo.ExePath), x, 446, 796, 22));
            if (installed)
            {
                var un = new FlatButton { Text = "Uninstall PadMouse…", Location = new Point(x, 476), Size = new Size(200, 34), Danger = true };
                un.Click += delegate { host.Uninstall(); };
                p.Controls.Add(un);
            }

            p.Controls.Add(Note("Free and open source under the MIT licence. Xbox is a trademark of Microsoft; PadMouse isn't affiliated with or endorsed by Microsoft.", x, 560, 796, 40));
            return p;
        }

        void ShowUpdateResult(UpdateInfo info, string err)
        {
            if (IsDisposed) return;
            if (err != null) { updateStatus.Text = "Couldn't check: " + err; updateStatus.ForeColor = Theme.Warning; return; }
            if (info == null) { updateStatus.Text = "You're up to date (version " + AppInfo.VersionText + ")."; updateStatus.ForeColor = Theme.Accent; return; }
            pendingUpdate = info;
            updateStatus.Text = "Version " + info.Version + " is available.";
            updateStatus.ForeColor = Theme.Accent;
            installUpdateBtn.Visible = info.DownloadUrl != null;
        }

        public void ShowAbout() { nav.Selected = 6; }
        public void ShowControllers() { nav.Selected = 4; }

        static void Open(string target) { try { Process.Start(target); } catch { } }
    }

    // ====================================================================== mapping list

    /// <summary>Compact two-column list of every button's action. Click to select.</summary>
    public class MappingList : Control
    {
        public Func<PadButton, string> LabelFor;
        public PadButton? Selected;
        public event Action<PadButton> ButtonClicked;
        int hover = -1;
        const int RowH = 27;

        static readonly PadButton[] Order =
        {
            PadButton.A, PadButton.B, PadButton.X, PadButton.Y, PadButton.LB, PadButton.RB, PadButton.LT, PadButton.RT,
            PadButton.Back, PadButton.Start, PadButton.L3, PadButton.R3, PadButton.DPadUp, PadButton.DPadDown, PadButton.DPadLeft, PadButton.DPadRight
        };

        public MappingList()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Font = Theme.UiFont(9.25f);
            Cursor = Cursors.Hand;
        }

        Rectangle Cell(int i)
        {
            int col = i / 8, row = i % 8;
            int w = (Width - 24) / 2;
            return new Rectangle(8 + col * (w + 8), 6 + row * RowH, w, RowH - 2);
        }

        int IndexAt(Point p) { for (int i = 0; i < Order.Length; i++) if (Cell(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int i = IndexAt(e.Location); if (i != hover) { hover = i; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int i = IndexAt(e.Location);
            if (i >= 0) { Selected = Order[i]; Invalidate(); var h = ButtonClicked; if (h != null) h(Order[i]); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            using (var path = Theme.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), 10)) using (var b = new SolidBrush(BackColor)) g.FillPath(b, path);
            for (int i = 0; i < Order.Length; i++)
            {
                var r = Cell(i);
                var b = Order[i];
                bool sel = Selected.HasValue && Selected.Value == b;
                if (sel || i == hover)
                    using (var path = Theme.RoundRect(r, 6)) using (var br = new SolidBrush(sel ? Color.FromArgb(40, 59, 130, 246) : Theme.Surface2)) g.FillPath(br, path);
                TextRenderer.DrawText(g, Names.Of(b), Font, new Rectangle(r.X + 10, r.Y, 170, r.Height), sel ? Theme.Selection : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                string act = LabelFor != null ? LabelFor(b) : "";
                ButtonAction a; string err;
                bool ok = ButtonAction.TryParse(act, out a, out err);
                TextRenderer.DrawText(g, act.Length == 0 ? "None" : act, Font, new Rectangle(r.X + 180, r.Y, r.Width - 186, r.Height), ok ? Theme.TextMuted : Theme.Danger, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }
}
