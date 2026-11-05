using System;
using System.Collections.Generic;
using System.IO;
using System.Management;

namespace Featherweight
{
    /// <summary>User preferences, stored in settings.json in the data folder.</summary>
    static class Settings
    {
        static string FilePath { get { return Path.Combine(Paths.Root, "settings.json"); } }

        public static bool FirstRunDone;
        public static bool CheckUpdates = true;     // asked on the welcome screen
        public static bool RestorePoints = true;    // System Restore point before changes
        public static string LastUpdateCheck;       // ISO time of the last automatic check
        public static string SkippedVersion;        // "Not now" on an update banner hides that version

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = Json.ReadFile(FilePath);
                FirstRunDone = d.Bool("firstRunDone");
                CheckUpdates = !d.ContainsKey("checkUpdates") || d.Bool("checkUpdates");
                RestorePoints = !d.ContainsKey("restorePoints") || d.Bool("restorePoints");
                LastUpdateCheck = d.Str("lastUpdateCheck");
                SkippedVersion = d.Str("skippedVersion");
            }
            catch { } // a damaged settings file just means defaults
        }

        public static void Save()
        {
            try
            {
                Json.WriteFile(FilePath, new Dictionary<string, object> {
                    { "firstRunDone", FirstRunDone }, { "checkUpdates", CheckUpdates }, { "restorePoints", RestorePoints },
                    { "lastUpdateCheck", LastUpdateCheck }, { "skippedVersion", SkippedVersion } });
            }
            catch { }
        }
    }

    /// <summary>
    /// Windows System Restore as a second safety net on top of Featherweight's own backups.
    /// Windows keeps at most one automatic point per 24 hours, so a recent point is reused.
    /// </summary>
    static class RestorePoint
    {
        public static void Ensure(Action<string> log)
        {
            if (!Settings.RestorePoints) { log("NOTE   System Restore point skipped (turned off in Settings)."); return; }
            const string fallback = " Featherweight's own backup still protects every change.";
            try
            {
                DateTime? latest = Latest();
                if (latest.HasValue && DateTime.Now - latest.Value < TimeSpan.FromHours(24))
                {
                    log("OK     Using the System Restore point from " + latest.Value.ToString("d MMM HH:mm") + ".");
                    return;
                }
                log("…      Creating a System Restore point (this can take a minute)…");
                using (var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null))
                {
                    var p = cls.GetMethodParameters("CreateRestorePoint");
                    p["Description"] = "Featherweight: before optimizing";
                    p["RestorePointType"] = 12; // MODIFY_SETTINGS
                    p["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE
                    var result = cls.InvokeMethod("CreateRestorePoint", p, null);
                    uint rv = Convert.ToUInt32(result["ReturnValue"]);
                    if (rv == 0) log("OK     System Restore point created.");
                    else if (rv == 1058) log("NOTE   System Restore is turned off on this PC, so no restore point was made." + fallback);
                    else log("NOTE   Windows didn't create a restore point (code " + rv + ")." + fallback);
                }
            }
            catch (Exception ex) { log("NOTE   Couldn't create a System Restore point (" + ex.Message.Trim() + ")." + fallback); }
        }

        static DateTime? Latest()
        {
            DateTime? best = null;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\default", "SELECT CreationTime FROM SystemRestore"))
                    foreach (ManagementObject o in s.Get())
                    {
                        var t = ManagementDateTimeConverter.ToDateTime((string)o["CreationTime"]);
                        if (best == null || t > best) best = t;
                    }
            }
            catch { }
            return best;
        }
    }
}
