using System.IO;

namespace PhoneBridge.Services;

/// <summary>
/// Shared media-backup routine — used by the Backup page and by the
/// headless scheduled run (--backup CLI).
/// </summary>
public static class BackupRunner
{
    public static readonly string[] SourceFolders =
    {
        "/sdcard/DCIM", "/sdcard/Pictures", "/sdcard/Download", "/sdcard/Movies",
        "/sdcard/Music", "/sdcard/Documents", "/sdcard/WhatsApp/Media"
    };

    /// <summary>Creates a timestamped backup folder under destParent and copies user media. Returns the backup folder.</summary>
    public static string Run(string? serial, string destParent, Action<string> progress)
    {
        var adb = AdbService.Instance;

        serial ??= adb.ListDevices().FirstOrDefault(d => d.Ready)?.Serial
            ?? throw new Exception("No Android device connected (USB debugging enabled?).");

        var dest = Path.Combine(destParent, $"phonebridge_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dest);

        int folderIdx = 0;
        foreach (var src in SourceFolders)
        {
            folderIdx++;
            var name = src.TrimEnd('/').Split('/').Last();
            progress($"[{folderIdx}/{SourceFolders.Length}] {src} …");
            var check = adb.Run($"shell test -d {src} && echo yes", serial, 5000);
            if (!check.stdout.Contains("yes")) continue;
            try { adb.Pull(serial, src, Path.Combine(dest, name)); }
            catch (Exception ex) { progress($"[warn] {src}: {ex.Message}"); }
        }
        progress("Backup complete.");
        return dest;
    }
}
