using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace Featherweight
{
    class UpdateInfo { public string Version, Url, Notes; }

    /// <summary>
    /// The app's only network request: asks GitHub for the latest published release. Sends nothing except
    /// a standard User-Agent ("Featherweight/x.y.z"), which GitHub requires. Never downloads or installs anything.
    /// </summary>
    static class Updates
    {
        public const string Repo = "workingwithbunnies/Featherweight";
        public static string ProjectPage { get { return "https://github.com/" + Repo; } }
        public static string ReleasesPage { get { return ProjectPage + "/releases"; } }
        public static string NewIssuePage { get { return ProjectPage + "/issues/new/choose"; } }

        /// <returns>The latest release, or null with a reason in <paramref name="error"/>.</returns>
        public static UpdateInfo Check(out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                req.UserAgent = "Featherweight/" + MainForm.AppVersion;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 10000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                {
                    var d = Json.Parse(r.ReadToEnd()) as Dictionary<string, object>;
                    return new UpdateInfo { Version = (d.Str("tag_name") ?? "").TrimStart('v', 'V'), Url = d.Str("html_url") ?? ReleasesPage, Notes = d.Str("body") ?? "" };
                }
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                error = hr != null && hr.StatusCode == HttpStatusCode.NotFound ? "No releases have been published yet." : "Couldn't reach GitHub (" + ex.Message + ")";
                return null;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        /// <summary>Compares "1.2.3" style versions; anything after "-" or "+" is ignored.</summary>
        public static bool IsNewer(string candidate, string current)
        {
            Version a, b;
            return Version.TryParse(Normalize(candidate), out a) && Version.TryParse(Normalize(current), out b) && a > b;
        }

        static string Normalize(string v)
        {
            var parts = (v ?? "").Split('-', '+')[0].Split('.').Where(p => p.Length > 0).ToList();
            while (parts.Count < 3) parts.Add("0");
            return string.Join(".", parts.Take(4));
        }

        /// <summary>Automatic check at most once a day, only if the user allowed it.</summary>
        public static bool AutomaticCheckDue
        {
            get
            {
                if (!Settings.CheckUpdates) return false;
                DateTime last;
                return !DateTime.TryParse(Settings.LastUpdateCheck, out last) || DateTime.Now - last > TimeSpan.FromHours(24);
            }
        }
    }
}
