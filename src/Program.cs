using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace PadMouse
{
    static class Program
    {
        /// <summary>
        /// Command line:
        ///   (none)            normal start; offers to install when run from outside the install folder
        ///   --startup         started by Windows at sign-in (no balloon)
        ///   --restart         wait for the previous copy to exit (restart as admin)
        ///   --update "path"   replace the exe at path with this one, then start it
        ///   --uninstall       remove PadMouse
        ///   --portable        never offer to install
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
#if NET
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#else
            Win32.MakeDpiAware();
#endif
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => PadEngine.Log(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { var ex = e.ExceptionObject as Exception; if (ex != null) PadEngine.Log(ex); };

            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            string notice = null;

            if (mode == "--uninstall") { Installer.RunUninstall(); return; }

            if (mode == "--update" && args.Length > 1)
            {
                string target = args[1];
                string err = Installer.UpdateInPlace(target);
                if (err != null) { MessageBox.Show("PadMouse couldn't update: " + err, "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                StartAndExit(target, "--updated");
                return;
            }

            bool quiet = mode == "--startup";
            if (mode == "--updated") notice = "Updated to version " + AppInfo.VersionText + ".";

            // Offer to install when run from Downloads etc.
            if (!Installer.IsInstalledCopy && !Installer.IsPortable && mode != "--portable" && mode != "--restart" && mode != "--startup" && mode != "--updated")
            {
                InstallChoice choice;
                using (var f = new InstallForm())
                {
                    f.ShowDialog();
                    choice = f.Choice;
                    if (choice == InstallChoice.Install)
                    {
                        string err = Installer.Install(f.StartMenu.Checked, f.Desktop.Checked, f.StartWithWindows.Checked);
                        if (err != null) { MessageBox.Show("Install failed: " + err, "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
                        StartAndExit(Installer.InstalledExe, "--installed");
                        return;
                    }
                }
                if (choice == InstallChoice.Cancel) return;
            }
            if (mode == "--installed") notice = "Installed. Find PadMouse in the Start menu any time.";

            // Only one copy at a time, otherwise two engines would fight over the cursor.
            if (!SingleInstance.TryAcquire(mode == "--restart" || mode == "--installed" || mode == "--updated" ? 10000 : 0))
            {
                if (!SingleInstance.SignalShowSettings())
                    MessageBox.Show("PadMouse is already running (with administrator rights). Use its icon in the system tray.",
                        "PadMouse", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try { Application.Run(new TrayApp(quiet, notice)); }
            finally { SingleInstance.Release(); }
        }

        static void StartAndExit(string exe, string arg)
        {
            try { Process.Start(new ProcessStartInfo(exe, arg) { UseShellExecute = false, WorkingDirectory = System.IO.Path.GetDirectoryName(exe) }); }
            catch (Exception ex) { MessageBox.Show("Couldn't start PadMouse: " + ex.Message, "PadMouse"); }
        }
    }
}
