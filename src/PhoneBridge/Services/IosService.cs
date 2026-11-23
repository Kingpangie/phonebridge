using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PhoneBridge.Services;

/// <summary>
/// Basic iPhone detection on Windows. Requires iTunes (or the standalone
/// "Apple Devices" app) so that Apple's Mobile Device Service (usbmuxd) is running.
/// Deep iOS features can be layered in later on top of usbmuxd/lockdown.
/// </summary>
public class IosService
{
    public class IosDevice
    {
        public string Name { get; set; } = "iPhone";
        public string PnpId { get; set; } = "";
        public bool AppleServiceRunning { get; set; }
    }

    public bool AppleServiceRunning
    {
        get
        {
            try
            {
                var procs = Process.GetProcessesByName("AppleMobileDeviceService");
                return procs.Length > 0;
            }
            catch { return false; }
        }
    }

    public List<IosDevice> Detect()
    {
        var list = new List<IosDevice>();
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command " +
                    "Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'Apple (Mobile Device|iPhone|iPad)' -or $_.Name -match '^iPhone|^iPad' } | " +
                    "Select-Object -Property Name, DeviceID | ConvertTo-Json -Compress",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi)!;
            var json = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);

            if (string.IsNullOrWhiteSpace(json)) return list;
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                AddFromElement(doc.RootElement, list);
            }
            else if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                    AddFromElement(el, list);
            }
        }
        catch { /* no device or service */ }
        return list;
    }

    private static void AddFromElement(System.Text.Json.JsonElement el, List<IosDevice> list)
    {
        var name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
        var id = el.TryGetProperty("DeviceID", out var d) ? d.GetString() ?? "" : "";
        if (name.Length == 0) return;
        // one entry per physical device: group by USB VID/PID segment
        var m = Regex.Match(id, @"USB\\VID_[0-9A-F]{4}&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
        var key = m.Success ? m.Groups[1].Value : id;
        if (!list.Any(x => x.PnpId.EndsWith(key)))
            list.Add(new IosDevice { Name = name, PnpId = id + "|" + key, AppleServiceRunning = true });
    }
}
