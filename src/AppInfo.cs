using System;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

[assembly: AssemblyTitle("PadMouse")]
[assembly: AssemblyProduct("PadMouse")]
[assembly: AssemblyDescription("Use an Xbox controller as a mouse and keyboard")]
[assembly: AssemblyCompany("Matthew Beddard")]
[assembly: AssemblyCopyright("Copyright © 2026 Matthew Beddard")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace PadMouse
{
    public static class AppInfo
    {
        public const string Name = "PadMouse";

        /// <summary>GitHub "owner/repo" used for update checks. Leave as OWNER/... to disable.</summary>
        public const string GitHubRepo = "Dragonzfear2/PadMouse";

        public static bool UpdatesConfigured { get { return !GitHubRepo.StartsWith("OWNER/"); } }
        public static string RepoUrl { get { return "https://github.com/" + GitHubRepo; } }

        public static Version Version
        {
            get { return Assembly.GetExecutingAssembly().GetName().Version; }
        }

        public static string VersionText
        {
            get { var v = Version; return v.Major + "." + v.Minor + "." + v.Build; }
        }

        public static string ExePath { get { return Application.ExecutablePath; } }

        public static bool IsAdmin
        {
            get
            {
                try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }
    }
}
