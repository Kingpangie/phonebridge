using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PhoneBridge.Services;

/// <summary>
/// Exports user data (contacts, SMS, call log) from an Android device to CSV
/// using the content provider via adb shell. Shell-level access only —
/// exports data the user can already see on their own device.
/// </summary>
public class ExportService
{
    public static readonly ExportService Instance = new();

    private static readonly Regex PairRegex =
        new(@"(\w+)=((?:[^=]|=(?![\w@.]+=))*?)(?=, \w+=|$)", RegexOptions.Compiled);

    private static Dictionary<string, string> ParseRow(string line)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = line.Trim();
        var m = Regex.Match(body, @"^Row:\s*\d+\s+(.*)$");
        if (m.Success) body = m.Groups[1].Value;
        foreach (Match pm in PairRegex.Matches(body))
            dict[pm.Groups[1].Value] = pm.Groups[2].Value.Trim();
        return dict;
    }

    private string Query(string serial, string args)
    {
        var (outp, err, code) = AdbService.Instance.Run($"shell content query {args}", serial, 60000);
        if (code != 0 && outp.Length == 0)
            throw new Exception("content query failed: " + err);
        return outp;
    }

    // ---------- Contacts ----------

    public int ExportContacts(string serial, string csvPath)
    {
        var raw = Query(serial,
            "--uri content://com.android.contacts/data --projection contact_id:display_name:data1:mimetype");

        var names = new Dictionary<string, string>();
        var phones = new List<(string id, string number)>();
        var emails = new List<(string id, string email)>();

        foreach (var line in raw.Split('\n'))
        {
            if (!line.Contains("Row:")) continue;
            var row = ParseRow(line);
            var id = row.GetValueOrDefault("contact_id", "");
            var data1 = row.GetValueOrDefault("data1", "");
            var mime = row.GetValueOrDefault("mimetype", "");
            if (mime.Contains("/name") && data1.Length > 0) names[id] = data1;
            else if (mime.Contains("phone") && data1.Length > 0) phones.Add((id, data1));
            else if (mime.Contains("email") && data1.Length > 0) emails.Add((id, data1));
        }

        var sb = new StringBuilder("Name,Phone,Email\r\n");
        var allIds = names.Keys.Union(phones.Select(p => p.id)).Union(emails.Select(e => e.id)).Distinct();
        foreach (var id in allIds)
        {
            var name = names.GetValueOrDefault(id, "");
            var nums = string.Join("; ", phones.Where(p => p.id == id).Select(p => p.number));
            var mails = string.Join("; ", emails.Where(e => e.id == id).Select(e => e.email));
            sb.Append(Csv(name)).Append(',').Append(Csv(nums)).Append(',').AppendLine(Csv(mails));
        }
        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
        return allIds.Count();
    }

    // ---------- SMS ----------

    public int ExportSms(string serial, string csvPath)
    {
        var raw = Query(serial, "--uri content://sms --projection _id:address:body:date:type");
        return WriteSmsCsv(raw, csvPath);
    }

    private static int WriteSmsCsv(string raw, string csvPath)
    {
        // bodies can contain newlines: split on Row: anchors across whole output
        var rows = Regex.Split(raw, @"(?=Row:\s*\d+)");
        var sb = new StringBuilder("Date,Direction,Address,Body\r\n");
        int count = 0;
        foreach (var block in rows)
        {
            if (!block.Contains("Row:")) continue;
            var flat = block.Replace("\r", " ").Replace("\n", " ").Trim();
            var row = ParseRow(flat);
            var addr = row.GetValueOrDefault("address", "");
            var body = row.GetValueOrDefault("body", "");
            var type = row.GetValueOrDefault("type", "") switch
            {
                "1" => "inbox", "2" => "sent", "3" => "draft", "4" => "outbox",
                "5" => "failed", "6" => "queued", var t => t
            };
            var date = row.GetValueOrDefault("date", "");
            if (long.TryParse(date, out var ms))
                date = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            sb.Append(Csv(date)).Append(',').Append(Csv(type)).Append(',').Append(Csv(addr)).Append(',').AppendLine(Csv(body));
            count++;
        }
        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
        return count;
    }

    // ---------- Call log ----------

    public int ExportCalls(string serial, string csvPath)
    {
        var raw = Query(serial,
            "--uri content://call_log/calls --projection number:cached_name:date:duration:type");
        var rows = Regex.Split(raw, @"(?=Row:\s*\d+)");
        var sb = new StringBuilder("Date,Direction,Number,Name,Duration(s)\r\n");
        int count = 0;
        foreach (var block in rows)
        {
            if (!block.Contains("Row:")) continue;
            var row = ParseRow(block);
            var number = row.GetValueOrDefault("number", "");
            var name = row.GetValueOrDefault("cached_name", "");
            var duration = row.GetValueOrDefault("duration", "");
            var type = row.GetValueOrDefault("type", "") switch
            {
                "1" => "incoming", "2" => "outgoing", "3" => "missed",
                "4" => "voicemail", "5" => "rejected", "6" => "blocked", var t => t
            };
            var date = row.GetValueOrDefault("date", "");
            if (long.TryParse(date, out var ms))
                date = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
            sb.Append(Csv(date)).Append(',').Append(Csv(type)).Append(',')
              .Append(Csv(number)).Append(',').Append(Csv(name)).AppendLine(Csv(duration));
            count++;
        }
        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
        return count;
    }

    private static string Csv(string? field)
    {
        field ??= "";
        if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }
}
