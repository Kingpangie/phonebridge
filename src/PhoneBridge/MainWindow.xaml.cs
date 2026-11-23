using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PhoneBridge.Services;
using PhoneBridge.Views;

namespace PhoneBridge;

public partial class MainWindow : Window
{
    private DashboardPage? _dashboard;
    private AppsPage? _apps;
    private FilesPage? _files;
    private ToolsPage? _tools;
    private BackupPage? _backup;
    private ExportPage? _export;

    public MainWindow()
    {
        InitializeComponent();
        Navigate("dashboard");
        _ = RefreshDevicesLoop();
    }

    private async Task RefreshDevicesLoop()
    {
        while (true)
        {
            try
            {
                var adb = AdbService.Instance.ListDevices();
                var ios = new IosService().Detect();
                DeviceCountText.Text = $"{adb.Count(d => d.Ready)} android · {ios.Count} iOS connected";
                _dashboard?.UpdateDevices(adb, ios, new IosService().AppleServiceRunning);
                _apps?.SetDevice(adb.FirstOrDefault(d => d.Ready)?.Serial);
                _files?.SetDevice(adb.FirstOrDefault(d => d.Ready)?.Serial);
                _tools?.SetDevice(adb.FirstOrDefault(d => d.Ready)?.Serial);
                _backup?.SetDevice(adb.FirstOrDefault(d => d.Ready)?.Serial);
                _export?.SetDevice(adb.FirstOrDefault(d => d.Ready)?.Serial);
            }
            catch { /* keep UI alive */ }
            await Task.Delay(5000);
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string tag) Navigate(tag);
    }

    private void Navigate(string page)
    {
        // highlight active nav item
        foreach (var child in NavPanel.Children.OfType<Button>())
        {
            var active = (string)child.Tag == page;
            child.Foreground = active
                ? (Brush)FindResource("CardBrush")
                : (Brush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1"));
            child.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }

        ContentHost.Content = page switch
        {
            "dashboard" => _dashboard ??= new DashboardPage(),
            "apps" => _apps ??= new AppsPage(),
            "files" => _files ??= new FilesPage(),
            "tools" => _tools ??= new ToolsPage(),
            "backup" => _backup ??= new BackupPage(),
            "export" => _export ??= new ExportPage(),
            _ => null
        };
        StatusText.Text = $"View: {page}";
    }
}
