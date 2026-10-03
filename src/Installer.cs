using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PadMouse
{
    /// <summary>
    /// Per-user install (no admin needed): copies PadMouse.exe to %LocalAppData%\Programs\PadMouse,
    /// adds Start menu / desktop shortcuts and an entry in Settings > Apps, and handles uninstall/update.
    /// </summary>
    public static class Installer
    {
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PadMouse";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PadMouse"); }
        }

        public static string InstalledExe { get { return Path.Combine(InstallDir, "PadMouse.exe"); } }

        static string StartMenuLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PadMouse.lnk"); }
        }

        static string DesktopLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PadMouse.lnk"); }
        }

        public static bool IsInstalledCopy
        {
            get { return SamePath(AppInfo.ExePath, InstalledExe); }
        }

        public static bool IsInstalled { get { return File.Exists(InstalledExe); } }

        public static Version InstalledVersion
        {
            get
            {
                try { return AssemblyName.GetAssemblyName(InstalledExe).Version; }
                catch { return null; }
            }
        }

        /// <summary>A "portable.txt" next to the exe means: never offer to install.</summary>
        public static bool IsPortable
        {
            get { return File.Exists(Path.Combine(Path.GetDirectoryName(AppInfo.ExePath), "portable.txt")); }
        }

        public static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        // ------------------------------------------------------------------ install / update

        /// <summary>Copies this exe to the install folder and registers it. Returns an error or null.</summary>
        public static string Install(bool startMenu, bool desktop, bool startWithWindows)
        {
            try
            {
                if (!SingleInstance.AskRunningCopyToExit()) return "PadMouse is still running. Exit it from the tray icon and try again.";
                Directory.CreateDirectory(InstallDir);
                CopyWithRetry(AppInfo.ExePath, InstalledExe);
                Register();
                if (startMenu) CreateShortcut(StartMenuLink, InstalledExe, "", "Use your Xbox controller as a mouse and keyboard");
                if (desktop) CreateShortcut(DesktopLink, InstalledExe, "", "Use your Xbox controller as a mouse and keyboard");
                var current = Startup.GetMode();
                if (current == StartupMode.Admin)
                {
                    // Leave the administrator logon task alone (changing it needs a UAC prompt).
                    Startup.RemoveRunKey();
                }
                else if (startWithWindows)
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                        k.SetValue(AppInfo.Name, "\"" + InstalledExe + "\" --startup");
                }
                else Startup.RemoveRunKey();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Update mode: replace the exe at targetPath (installed or portable) with this one.</summary>
        public static string UpdateInPlace(string targetPath)
        {
            try
            {
                if (!SingleInstance.AskRunningCopyToExit()) return "PadMouse didn't close in time. Exit it from the tray icon and run the update again.";
                CopyWithRetry(AppInfo.ExePath, targetPath);
                if (SamePath(targetPath, InstalledExe)) Register();
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        static void CopyWithRetry(string from, string to)
        {
            if (SamePath(from, to)) return;
            for (int i = 0; ; i++)
            {
                try
                {
                    File.Copy(from, to, true);
                    DeleteFile(to + ":Zone.Identifier");   // drop the "downloaded from the internet" mark
                    return;
                }
                catch (IOException) { if (i >= 20) throw; Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { if (i >= 20) throw; Thread.Sleep(250); }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFile(string path);

        static void Register()
        {
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "PadMouse");
                k.SetValue("DisplayVersion", AppInfo.VersionText);
                k.SetValue("Publisher", "Matthew Beddard");
                k.SetValue("DisplayIcon", InstalledExe + ",0");
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", "\"" + InstalledExe + "\" --uninstall");
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                try { k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord); } catch { }
                if (AppInfo.UpdatesConfigured) { k.SetValue("URLInfoAbout", AppInfo.RepoUrl); k.SetValue("HelpLink", AppInfo.RepoUrl + "/issues"); }
            }
        }

        // ------------------------------------------------------------------ uninstall

        /// <summary>Runs the whole uninstall, with its own confirmation. Call from "--uninstall".</summary>
        public static void RunUninstall()
        {
            using (var dlg = new UninstallForm())
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                if (!SingleInstance.AskRunningCopyToExit())
                {
                    MessageBox.Show("PadMouse is still running. Exit it from the tray icon, then uninstall again.", "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string err;
                if (Startup.TaskExists()) Startup.SetMode(StartupMode.Off, out err);
                Startup.RemoveRunKey();
                TryDelete(StartMenuLink);
                TryDelete(DesktopLink);
                try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
                if (dlg.DeleteSettings) { try { Directory.Delete(Config.Folder, true); } catch { } }

                MessageBox.Show("PadMouse has been uninstalled.", "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // We can't delete our own exe while running, so ask cmd to do it once we've exited.
                string dir = InstallDir;
                if (Directory.Exists(dir))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + dir + "\"")
                        { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetTempPath() });
                    }
                    catch { }
                }
            }
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        // ------------------------------------------------------------------ shortcuts (IShellLink)

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        static void CreateShortcut(string lnkPath, string target, string args, string description)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lnkPath));
                var link = (IShellLinkW)new ShellLink();
                link.SetPath(target);
                link.SetArguments(args);
                link.SetDescription(description);
                link.SetWorkingDirectory(Path.GetDirectoryName(target));
                link.SetIconLocation(target, 0);
                ((IPersistFile)link).Save(lnkPath, true);
                Marshal.ReleaseComObject(link);
            }
            catch (Exception ex) { PadEngine.Log(ex); }
        }
    }

    // ====================================================================== install window

    public enum InstallChoice { Cancel, Install, RunPortable }

    /// <summary>Shown when PadMouse.exe is run from Downloads etc. Offers to install or just run.</summary>
    public class InstallForm : Form
    {
        public InstallChoice Choice = InstallChoice.Cancel;
        public ToggleSwitch StartMenu, Desktop, StartWithWindows;

        public InstallForm()
        {
            var installedVer = Installer.IsInstalled ? Installer.InstalledVersion : null;
            bool update = installedVer != null && installedVer < AppInfo.Version;
            bool reinstall = installedVer != null && !update;

            Text = "PadMouse setup";
            Icon = TrayIcons.MakeIcon(Theme.Accent);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = Theme.UiFont(9.75f);
            BackColor = Theme.Bg; ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 400);

            Controls.Add(new PictureBox { Image = TrayIcons.Render(56, Theme.Accent), Location = new Point(32, 30), Size = new Size(56, 56) });
            string title = update ? "Update PadMouse" : reinstall ? "Reinstall PadMouse" : "Install PadMouse";
            Controls.Add(new Label { Text = title, Font = Theme.Semibold(17f), Location = new Point(100, 30), AutoSize = true });
            Controls.Add(new Label
            {
                Text = update ? "Version " + installedVer.ToString(3) + " is installed. This will update it to " + AppInfo.VersionText + ". Your settings are kept."
                     : "Version " + AppInfo.VersionText + ". Installs just for you, no administrator rights needed.",
                Location = new Point(102, 66), Size = new Size(390, 40), ForeColor = Theme.TextMuted
            });

            int y = 126;
            StartMenu = new ToggleSwitch { Text = "Add to the Start menu", Checked = true, Location = new Point(32, y), Size = new Size(440, 30) }; y += 38;
            Desktop = new ToggleSwitch { Text = "Add a desktop shortcut", Checked = false, Location = new Point(32, y), Size = new Size(440, 30) }; y += 38;
            StartWithWindows = new ToggleSwitch { Text = "Start PadMouse when I sign in to Windows", Checked = Startup.GetMode() != StartupMode.Off || !Installer.IsInstalled, Location = new Point(32, y), Size = new Size(440, 30) };
            if (!update) { Controls.Add(StartMenu); Controls.Add(Desktop); Controls.Add(StartWithWindows); }

            Controls.Add(new Label { Text = "Installs to " + Installer.InstallDir, Location = new Point(32, 250), Size = new Size(460, 36), ForeColor = Theme.TextMuted, Font = Theme.UiFont(8.5f) });

            var install = new FlatButton { Text = update ? "Update" : reinstall ? "Reinstall" : "Install", Primary = true, Location = new Point(332, 330), Size = new Size(156, 38) };
            var portable = new FlatButton { Text = "Just run it", Location = new Point(196, 330), Size = new Size(126, 38) };
            install.Click += delegate { Choice = InstallChoice.Install; DialogResult = DialogResult.OK; };
            portable.Click += delegate { Choice = InstallChoice.RunPortable; DialogResult = DialogResult.OK; };
            Controls.Add(install); Controls.Add(portable);
            AcceptButton = install;
            Theme.Apply(this);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Win32.UseDarkTitleBar(Handle); }
    }

    public class UninstallForm : Form
    {
        readonly ToggleSwitch deleteSettings;
        public bool DeleteSettings { get { return deleteSettings.Checked; } }

        public UninstallForm()
        {
            Text = "Uninstall PadMouse";
            Icon = TrayIcons.MakeIcon(Theme.Accent);
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            Font = Theme.UiFont(9.75f);
            BackColor = Theme.Bg; ForeColor = Theme.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 230);
            Controls.Add(new Label { Text = "Uninstall PadMouse?", Font = Theme.Semibold(15f), Location = new Point(28, 26), AutoSize = true });
            Controls.Add(new Label { Text = "This removes PadMouse, its shortcuts and its startup entry.", Location = new Point(30, 66), Size = new Size(400, 22), ForeColor = Theme.TextMuted });
            deleteSettings = new ToggleSwitch { Text = "Also delete my settings and profiles", Location = new Point(28, 102), Size = new Size(400, 30) };
            Controls.Add(deleteSettings);
            var ok = new FlatButton { Text = "Uninstall", Danger = true, Location = new Point(300, 168), Size = new Size(130, 36) };
            var cancel = new FlatButton { Text = "Cancel", Location = new Point(180, 168), Size = new Size(110, 36) };
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(ok); Controls.Add(cancel);
            CancelButton = cancel;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Win32.UseDarkTitleBar(Handle); }
    }
}
