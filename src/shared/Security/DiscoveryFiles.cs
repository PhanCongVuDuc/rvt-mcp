using System.IO;
using System.Text.RegularExpressions;

namespace RvtMcp.Plugin
{
    /// <summary>
    /// Names and cleanup of the discovery files one plugin writes. Revit-API-free so the test project can compile it.
    ///
    /// Each plugin writes two files: revit-YYYY.json, which the last Revit of that year to start overwrites (the name
    /// older servers read), and revit-YYYY-PID.json, which no other Revit touches - the only way a server can reach
    /// every one of several Revits of the same year.
    /// </summary>
    public static class DiscoveryFiles
    {
        public static string YearFileName(string year)
        {
            return "revit-" + year + ".json";
        }

        public static string PidFileName(string year, int pid)
        {
            return "revit-" + year + "-" + pid + ".json";
        }

        /// <summary>
        /// Deletes this Revit's own files on a clean shutdown. The year file is deleted only while it still names this
        /// pid: once a later Revit of the same year has overwritten it, it is that Revit's, and deleting it would cut
        /// a live Revit off from every server reading the year file.
        /// </summary>
        public static void DeleteOwn(string dir, string year, int pid)
        {
            var pidFile = Path.Combine(dir, PidFileName(year, pid));
            if (File.Exists(pidFile)) File.Delete(pidFile);

            var yearFile = Path.Combine(dir, YearFileName(year));
            if (File.Exists(yearFile) && NamesPid(File.ReadAllText(yearFile), pid)) File.Delete(yearFile);
        }

        public static bool NamesPid(string discoveryJson, int pid)
        {
            var match = Regex.Match(discoveryJson ?? string.Empty, "\"pid\"\\s*:\\s*(\\d+)");
            return match.Success && match.Groups[1].Value == pid.ToString();
        }
    }
}
