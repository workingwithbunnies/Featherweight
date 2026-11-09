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

        /// <returns>The newest release, or null with a reason in <paramref name="error"/>.</returns>
        /// <remarks>
        /// GitHub's "latest release" skips pre-releases (alpha, beta). Someone running a pre-release is shown the newest
        /// release of any kind, so alpha testers hear about the next alpha as well as the final version; everyone else
        /// only sees final releases.
        /// </remarks>
        public static UpdateInfo Check(out string error)
        {
            error = null;
            try
            {
                bool pre = IsPrerelease(MainForm.AppVersion);
                var json = Get("https://api.github.com/repos/" + Repo + (pre ? "/releases?per_page=20" : "/releases/latest"));
                // The JSON reader returns a list of releases as an array (object[]), a single release as a dictionary.
                var list = json as System.Collections.IList;
                var releases = list != null ? list.OfType<Dictionary<string, object>>().ToList()
                                            : new List<Dictionary<string, object>> { json as Dictionary<string, object> }.Where(d => d != null).ToList();
                UpdateInfo best = null;
                foreach (var d in releases.Where(d => !d.Bool("draft")))
                {
                    var info = new UpdateInfo { Version = (d.Str("tag_name") ?? "").TrimStart('v', 'V'), Url = d.Str("html_url") ?? ReleasesPage, Notes = d.Str("body") ?? "" };
                    if (best == null || IsNewer(info.Version, best.Version)) best = info;
                }
                if (best == null) error = "No releases have been published yet.";
                return best;
            }
            catch (WebException ex)
            {
                var hr = ex.Response as HttpWebResponse;
                error = hr != null && hr.StatusCode == HttpStatusCode.NotFound ? "No releases have been published yet." : "Couldn't reach GitHub (" + ex.Message + ")";
                return null;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        static object Get(string url)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Featherweight/" + MainForm.AppVersion;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 10000;
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream()))
                return Json.Parse(r.ReadToEnd());
        }

        public static bool IsPrerelease(string v) { return ((v ?? "").Split('+')[0]).Contains("-"); }

        /// <summary>
        /// Compares versions like "1.2.3", "0.1.0-alpha" and "0.1.0-alpha.2" the usual way (semantic versioning):
        /// numbers first; for equal numbers a final release is newer than its alphas, and "alpha.2" is newer than "alpha".
        /// Anything after "+" (the build's commit) is ignored.
        /// </summary>
        public static bool IsNewer(string candidate, string current) { return Compare(candidate, current) > 0; }

        public static int Compare(string x, string y)
        {
            Version a, b;
            if (!Version.TryParse(Normalize(x), out a) || !Version.TryParse(Normalize(y), out b)) return 0;
            int c = a.CompareTo(b);
            if (c != 0) return c;
            string pa = Pre(x), pb = Pre(y);
            if (pa == pb) return 0;
            if (pa == "") return 1;   // final > pre-release
            if (pb == "") return -1;
            var ia = pa.Split('.'); var ib = pb.Split('.');
            for (int i = 0; i < Math.Max(ia.Length, ib.Length); i++)
            {
                if (i >= ia.Length) return -1;
                if (i >= ib.Length) return 1;
                int na, nb; bool da = int.TryParse(ia[i], out na), db = int.TryParse(ib[i], out nb);
                int r = da && db ? na.CompareTo(nb) : da ? -1 : db ? 1 : string.CompareOrdinal(ia[i], ib[i]);
                if (r != 0) return r;
            }
            return 0;
        }

        static string Pre(string v) { var core = (v ?? "").Split('+')[0]; int i = core.IndexOf('-'); return i < 0 ? "" : core.Substring(i + 1).ToLowerInvariant(); }

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
