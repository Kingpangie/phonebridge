using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using PhoneBridge.Services;

namespace PhoneBridge.Views;

public partial class ToolsPage : UserControl
{
    private string? _serial;

    public ToolsPage()
    {
        InitializeComponent();
    }

    public void SetDevice(string? serial)
    {
        _serial = serial;
        DeviceLabel.Text = serial == null ? "No Android device connected" : $"Device: {serial}";
    }

    private async void Screenshot_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        var dlg = new SaveFileDialog { FileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png", Filter = "PNG image|*.png" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var bytes = await Task.Run(() => AdbService.Instance.Screenshot(_serial));
            await File.WriteAllBytesAsync(dlg.FileName, bytes);
            MessageBox.Show("Saved " + dlg.FileName);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        var mode = (string)((Button)sender).Tag!;
        if (mode != "normal" && MessageBox.Show($"Reboot into {mode} mode?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Task.Run(() => AdbService.Instance.Reboot(_serial, mode));
        MessageBox.Show("Reboot command sent.");
    }

    private async void RefreshInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        try
        {
            var d = await Task.Run(() => AdbService.Instance.ListDevices().FirstOrDefault(x => x.Serial == _serial));
            DiskInfo.Text = d == null ? "Device went offline." :
                $"Android {d.AndroidVersion} (API {d.ApiLevel})\nBattery: {d.BatteryLevel}\nStorage: {d.StorageUsed} used of {d.StorageTotal}";
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
}
