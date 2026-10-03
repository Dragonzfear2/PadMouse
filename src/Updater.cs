using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace PadMouse
{
    public class UpdateInfo
    {
        public string Version;      // "1.2.0"
        public string PageUrl;      // release page
        public string DownloadUrl;  // PadMouse.exe asset, may be null
        public string Notes;
    }

    /// <summary>Checks GitHub releases for a newer version and can download and run it.</summary>
    public static class Updater
    {
        static string ApiUrl { get { return "https://api.github.com/repos/" + AppInfo.GitHubRepo + "/releases/latest"; } }

        static WebClient NewClient()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { } // TLS 1.2
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "PadMouse/" + AppInfo.VersionText;
            wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            wc.Encoding = Encoding.UTF8;
            return wc;
        }

        /// <summary>Calls done(info, error) on a worker thread. info is null when already up to date.</summary>
        public static void CheckAsync(Action<UpdateInfo, string> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null; string error = null;
                try
                {
                    if (!AppInfo.UpdatesConfigured) throw new InvalidOperationException("no GitHub repository configured");
                    string json;
                    using (var wc = NewClient()) json = wc.DownloadString(ApiUrl);
                    string tag = JsonString(json, "tag_name");
                    Version latest;
                    if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out latest)) throw new InvalidDataException("unexpected reply from GitHub");
                    var current = AppInfo.Version;
                    if (Normalise(latest) > Normalise(current))
                    {
                        info = new UpdateInfo
                        {
                            Version = tag.TrimStart('v', 'V'),
                            PageUrl = JsonString(json, "html_url"),
                            Notes = JsonString(json, "body"),
                        };
                        var m = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*/PadMouse\\.exe)\"", RegexOptions.IgnoreCase);
                        if (m.Success) info.DownloadUrl = m.Groups[1].Value;
                    }
                }
                catch (WebException ex)
                {
                    var resp = ex.Response as HttpWebResponse;
                    error = resp != null && resp.StatusCode == HttpStatusCode.NotFound ? "no releases published yet" : "couldn't reach GitHub (" + ex.Status + ")";
                }
                catch (Exception ex) { error = ex.Message; }
                done(info, error);
            });
        }

        static Version Normalise(Version v)
        {
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
        }

        /// <summary>Downloads the new PadMouse.exe and starts it in update mode. Returns an error or null.</summary>
        public static string DownloadAndRun(UpdateInfo info)
        {
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "PadMouse-update");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "PadMouse.exe");
                using (var wc = NewClient())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/octet-stream";
                    wc.DownloadFile(info.DownloadUrl, file);
                }
                if (new FileInfo(file).Length < 20000) return "the download looked incomplete";
                Process.Start(new ProcessStartInfo(file, "--update \"" + AppInfo.ExePath + "\"") { UseShellExecute = false });
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>Tiny JSON string extractor for the few fields we need (first match wins).</summary>
        static string JsonString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            string s = m.Groups[1].Value;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length) { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
