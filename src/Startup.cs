using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace PadMouse
{
    /// <summary>
    /// Start-with-Windows handling. Normal mode uses the HKCU Run key; administrator mode uses a
    /// Task Scheduler logon task (the only way to start elevated without a UAC prompt each time).
    /// </summary>
    public static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string TaskName = "PadMouse";

        public static StartupMode GetMode()
        {
            if (TaskExists()) return StartupMode.Admin;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    if (k != null && k.GetValue(AppInfo.Name) != null) return StartupMode.Normal;
            }
            catch { }
            return StartupMode.Off;
        }

        public static bool SetMode(StartupMode mode, out string error)
        {
            error = null;
            try
            {
                switch (mode)
                {
                    case StartupMode.Off:
                        RemoveRunKey();
                        if (TaskExists() && !RunElevated("schtasks.exe", "/Delete /TN " + TaskName + " /F", out error)) return false;
                        return true;
                    case StartupMode.Normal:
                        if (TaskExists() && !RunElevated("schtasks.exe", "/Delete /TN " + TaskName + " /F", out error)) return false;
                        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                            k.SetValue(AppInfo.Name, "\"" + AppInfo.ExePath + "\" --startup");
                        return true;
                    case StartupMode.Admin:
                        string xml = Path.Combine(Path.GetTempPath(), "PadMouse-task.xml");
                        File.WriteAllText(xml, TaskXml(AppInfo.ExePath), Encoding.Unicode);
                        bool ok = RunElevated("schtasks.exe", "/Create /TN " + TaskName + " /XML \"" + xml + "\" /F", out error);
                        try { File.Delete(xml); } catch { }
                        if (!ok) return false;
                        if (!TaskExists()) { error = "the task wasn't created"; return false; }
                        RemoveRunKey();
                        return true;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
            return true;
        }

        /// <summary>After installing/moving the exe, point an existing Run entry at the new path.</summary>
        public static void RetargetRunKey(string exePath)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null && k.GetValue(AppInfo.Name) != null) k.SetValue(AppInfo.Name, "\"" + exePath + "\" --startup");
            }
            catch { }
        }

        public static void RemoveRunKey()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null) k.DeleteValue(AppInfo.Name, false);
            }
            catch { }
        }

        public static bool TaskExists()
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", "/Query /TN " + TaskName)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>Runs a command with a UAC prompt (no prompt if we're already elevated).</summary>
        public static bool RunElevated(string exe, string args, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(20000);
                    if (p.ExitCode != 0) { error = exe + " failed (code " + p.ExitCode + ")"; return false; }
                }
                return true;
            }
            catch (Win32Exception ex)
            {
                error = ex.NativeErrorCode == 1223 ? "the Windows permission prompt was cancelled" : ex.Message;
                return false;
            }
        }

        static string TaskXml(string exe)
        {
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string cmd = SecurityElement.Escape(exe);
            return
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Starts PadMouse (with administrator rights) when you sign in.</Description></RegistrationInfo>\r\n" +
"  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT3S</Delay></LogonTrigger></Triggers>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
"    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
"    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
"    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n" +
"    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
"    <Enabled>true</Enabled>\r\n" +
"    <Hidden>false</Hidden>\r\n" +
"    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
"    <Priority>4</Priority>\r\n" +
"  </Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>" + cmd + "</Command><Arguments>--startup</Arguments></Exec></Actions>\r\n" +
"</Task>\r\n";
        }
    }
}
