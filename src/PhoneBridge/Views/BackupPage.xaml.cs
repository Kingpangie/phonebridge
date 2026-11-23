using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PhoneBridge.Services;

namespace PhoneBridge.Views;

public partial class BackupPage : UserControl
{
    private string? _serial;
    private const string TaskName = "PhoneBridge Auto Backup";

    public BackupPage()
    {
        InitializeComponent();
        RefreshScheduleStatus();
    }

    public void SetDevice(string? serial)
    {
        _serial = serial;
        DeviceLabel.Text = serial == null ? "No Android device connected" : $"Device: {serial}";
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose parent folder for backups" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        BackupButton.IsEnabled = false;
        var progress = new Progress<string>(s => BackupStatus.Text = s);
        try
        {
            var dest = await Task.Run(() => BackupRunner.Run(_serial, dlg.FolderName, s => ((IProgress<string>)progress).Report(s)));
            MessageBox.Show($"Backup finished.\n{dest}", "PhoneBridge", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Backup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { BackupButton.IsEnabled = true; }
    }

    private void Schedule_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose folder for scheduled backups" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        var hour = HourBox.SelectedIndex;
        var exe = AppDomain.CurrentDomain.BaseDirectory + "PhoneBridge.exe";
        var tr = $"\"{exe}\" --backup \"{dlg.FolderName}\"";
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = $"/Create /TN \"{TaskName}\" /SC DAILY /ST {hour:00}:00 /TR \"{tr.Replace("\"", "\\\"")}\" /F",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        ScheduleStatus.Text = p.ExitCode == 0
            ? $"Scheduled daily at {hour:00}:00 → {dlg.FolderName}"
            : "Schedule failed: " + output.Trim();
    }

    private void Unschedule_Click(object sender, RoutedEventArgs e)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = $"/Delete /TN \"{TaskName}\" /F",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        var p = Process.Start(psi)!;
        p.WaitForExit(15000);
        RefreshScheduleStatus();
    }

    private void RefreshScheduleStatus()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks",
            Arguments = $"/Query /TN \"{TaskName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        try
        {
            var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            var m = System.Text.RegularExpressions.Regex.Match(output, @"(\d{1,2}:\d{2}:\d{2})");
            ScheduleStatus.Text = p.ExitCode == 0
                ? $"Scheduled: daily at {(m.Success ? m.Groups[1].Value[..5] : "?")}"
                : "No automatic backup scheduled.";
        }
        catch { ScheduleStatus.Text = ""; }
    }
}
