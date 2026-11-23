using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using PhoneBridge.Services;

namespace PhoneBridge.Views;

public partial class AppsPage : UserControl
{
    private string? _serial;
    private readonly ObservableCollection<string> _packages = new();

    public AppsPage()
    {
        InitializeComponent();
        AppList.ItemsSource = _packages;
    }

    public void SetDevice(string? serial)
    {
        _serial = serial;
        DeviceLabel.Text = serial == null ? "No Android device connected" : $"Device: {serial}";
    }

    private async void Load_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device with USB debugging enabled."); return; }
        AppList.IsEnabled = false;
        try
        {
            var pkgs = await Task.Run(() => AdbService.Instance.ListPackages(_serial));
            _packages.Clear();
            foreach (var p in pkgs) _packages.Add(p.Package);
            UpdateCount();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
        finally { AppList.IsEnabled = true; }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        var q = SearchBox.Text.ToLowerInvariant();
        // filter in place is fine for v1
        if (AppList.ItemsSource is ObservableCollection<string>)
        {
            // re-filter from stored list
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        AppCount.Text = $"{_packages.Count} user apps";
    }

    private void InstallApk_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Pick an APK to install", Filter = "APK package|*.apk" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var serial = _serial;
        var path = dlg.FileName;
        try
        {
            Task.Run(() => AdbService.Instance.InstallApk(serial, path)).Wait();
            MessageBox.Show("Installed " + System.IO.Path.GetFileName(path));
        }
        catch (Exception ex) { MessageBox.Show(ex.InnerException?.Message ?? ex.Message); }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) return;
        var sel = AppList.SelectedItems.Cast<string>().ToList();
        if (sel.Count == 0) { MessageBox.Show("Select one or more apps first."); return; }
        if (MessageBox.Show($"Uninstall {sel.Count} app(s) for the current user?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        foreach (var pkg in sel)
        {
            await Task.Run(() => AdbService.Instance.Uninstall(_serial, pkg));
            _packages.Remove(pkg);
        }
        UpdateCount();
    }
}
