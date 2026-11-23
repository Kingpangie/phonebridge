using System.Diagnostics;
using System.IO;
using System.Text;

namespace PhoneBridge.Services;

/// <summary>
/// Wraps the bundled adb.exe. All Android features go through here.
/// </summary>
public class AdbService
{
    public static readonly AdbService Instance = new();

    private string AdbPath
    {
        get
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "adb");
            var p = Path.Combine(dir, "adb.exe");
            return File.Exists(p) ? p : "adb"; // fall back to PATH
        }
    }

    public class Device
    {
        public string Serial { get; set; } = "";
        public string State { get; set; } = "";   // device | offline | unauthorized
        public string Model { get; set; } = "";
        public string Brand { get; set; } = "";
        public string AndroidVersion { get; set; } = "";
        public string ApiLevel { get; set; } = "";
        public string BatteryLevel { get; set; } = "";
        public string StorageUsed { get; set; } = "";
        public string StorageTotal { get; set; } = "";
        public bool Ready => State == "device";
    }

    /// <summary>Run adb with a global serial target; empty = no -s flag.</summary>
    public (string stdout, string stderr, int exit) Run(string args, string? serial = null, int timeoutMs = 15000)
    {
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        var full = string.IsNullOrEmpty(serial) ? args : $"-s {serial} {args}";
        var psi = new ProcessStartInfo
        {
            FileName = AdbPath,
            Arguments = full,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit(timeoutMs);
        if (!p.HasExited)
        {
            try { p.Kill(true); } catch { }
            return (sbOut.ToString(), "adb timed out", -1);
        }
        return (sbOut.ToString(), sbErr.ToString(), p.ExitCode);
    }

    public List<Device> ListDevices()
    {
        var list = new List<Device>();
        var (outp, _, code) = Run("devices -l", timeoutMs: 8000);
        if (code != 0) return list;
        foreach (var line in outp.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("List of") || t.StartsWith("*")) continue;
            var parts = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var d = new Device { Serial = parts[0], State = parts[1] };
            foreach (var kv in parts.Skip(2))
            {
                var idx = kv.IndexOf(':');
                if (idx <= 0) continue;
                var key = kv[..idx];
                var val = kv[(idx + 1)..];
                if (key == "model") d.Model = val;
                else if (key == "device") { if (string.IsNullOrEmpty(d.Model)) d.Model = val; }
            }
            if (d.Ready) Enrich(d);
            list.Add(d);
        }
        return list;
    }

    private void Enrich(Device d)
    {
        try
        {
            var (_, _, _) = (0, "", 0);
            var (_, e1, c1) = Run("shell getprop ro.product.model", d.Serial, 5000);
            if (c1 == 0) d.Model = e1.Trim().Length > 0 ? e1.Trim() : d.Model;
            var (_, e2, _) = Run("shell getprop ro.product.brand", d.Serial, 5000);
            d.Brand = e2.Trim();
            var (_, e3, _) = Run("shell getprop ro.build.version.release", d.Serial, 5000);
            d.AndroidVersion = e3.Trim();
            var (_, e4, _) = Run("shell getprop ro.build.version.sdk", d.Serial, 5000);
            d.ApiLevel = e4.Trim();
            var (_, e5, _) = Run("shell dumpsys battery | grep level", d.Serial, 5000);
            var m = System.Text.RegularExpressions.Regex.Match(e5, @"level:\s*(\d+)");
            if (m.Success) d.BatteryLevel = m.Groups[1].Value + "%";
            var (_, e6, _) = Run("shell df /sdcard", d.Serial, 5000);
            var lines = e6.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 1)
            {
                var cols = lines[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (cols.Length >= 4)
                {
                    d.StorageTotal = cols[1];
                    d.StorageUsed = cols[2];
                }
            }
        }
        catch { /* best effort */ }
    }

    public List<(string Package, string Name)> ListPackages(string serial)
    {
        var result = new List<(string, string)>();
        var (outp, _, code) = Run("shell pm list packages -3", serial, 20000);
        if (code != 0) return result;
        foreach (var line in outp.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("package:"))
                result.Add((t["package:".Length..], t["package:".Length..]));
        }
        return result.OrderBy(x => x.Item1).ToList();
    }

    public bool Uninstall(string serial, string package) =>
        Run($"shell pm uninstall --user 0 {package}", serial, 30000).exit == 0;

    public void InstallApk(string serial, string apkPath)
    {
        var (outp, err, code) = Run($"install -r \"{apkPath}\"", serial, 5 * 60 * 1000);
        if (code != 0 || !outp.Contains("Success"))
            throw new Exception("APK install failed: " + (err.Length > 0 ? err : outp));
    }

    public string Pull(string serial, string remote, string localDir)
    {
        Directory.CreateDirectory(localDir);
        var r = Run($"pull \"{remote}\" \"{localDir}\"", serial, 30 * 60 * 1000);
        return r.exit == 0 ? r.stdout : throw new Exception(r.stderr);
    }

    public string Push(string serial, string local, string remoteDir) =>
        Run($"push \"{local}\" \"{remoteDir}\"", serial, 30 * 60 * 1000).exit == 0
            ? "pushed"
            : throw new Exception(Run($"push \"{local}\" \"{remoteDir}\"", serial, 1000).stderr);

    public byte[] Screenshot(string serial)
    {
        var (outp, err, code) = Run("shell screencap -p", serial, 20000);
        if (code != 0 || outp.Length < 100)
            throw new Exception("screencap failed: " + err);
        // adb in text mode may mangle \r\n -> \n; fix PNG signature damage
        var bytes = Encoding.UTF8.GetBytes(outp);
        var fixedBytes = FixCrlf(bytes);
        return fixedBytes;
    }

    private static byte[] FixCrlf(byte[] data)
    {
        var outList = new List<byte>(data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] == 0x0D && i + 1 < data.Length && data[i + 1] == 0x0A)
                continue; // drop CR that became CRLF
            outList.Add(data[i]);
        }
        return outList.ToArray();
    }

    public void Reboot(string serial, string mode = "")
    {
        var arg = mode switch
        {
            "recovery" => "reboot recovery",
            "bootloader" => "reboot bootloader",
            _ => "reboot"
        };
        Run(arg, serial, 8000);
    }

    public List<string> ListDir(string serial, string path)
    {
        var (outp, _, _) = Run($"shell ls -1 \"{path}\"", serial, 10000);
        return outp.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && l != "ls: " && !l.Contains("Permission denied") && !l.StartsWith("ls:"))
            .ToList();
    }
}
