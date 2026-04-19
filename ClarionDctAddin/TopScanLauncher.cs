using System;
using System.Diagnostics;
using System.IO;

namespace ClarionDctAddin
{
    // Locates Clarion's TopScan.exe (the built-in TPS data viewer) and launches
    // it against a given .tps path. TopScan uses the same native ClaTPS.dll
    // that Clarion-generated apps use, so it reads every TPS flavour correctly —
    // encryption, compression, memos, long records, everything.
    //
    // Not in-dialog, but 100% reliable; serves as our "get TPS working now"
    // exit when the experimental inline reader can't produce a valid record.
    internal static class TopScanLauncher
    {
        public static string FindTopScan()
        {
            // Try the standard locations first; the add-in is installed inside
            // the Clarion bin folder so AppDomain.BaseDirectory often IS that
            // bin (or close to it).
            var cands = new[]
            {
                @"C:\clarion12\bin\TopScan.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TopScan.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\TopScan.exe"),
            };
            foreach (var c in cands)
            {
                try { if (File.Exists(c)) return Path.GetFullPath(c); } catch { }
            }
            return null;
        }

        public static bool Launch(string tpsPath, out string error)
        {
            error = null;
            var topScan = FindTopScan();
            if (topScan == null)
            {
                error = "TopScan.exe not found in C:\\clarion12\\bin or next to the add-in.";
                return false;
            }
            if (string.IsNullOrEmpty(tpsPath) || !File.Exists(tpsPath))
            {
                error = "TPS file not found: " + tpsPath;
                return false;
            }
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = topScan,
                    Arguments = "\"" + tpsPath + "\"",
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(tpsPath)
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to launch TopScan: " + ex.Message;
                return false;
            }
        }
    }
}
