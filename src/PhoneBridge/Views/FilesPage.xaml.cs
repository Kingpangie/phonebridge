using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhoneBridge.Services;

namespace PhoneBridge.Views;

public partial class FilesPage : UserControl
{
    private string? _serial;
    private string _currentPath = "/sdcard";
    private readonly List<string> _entries = new();

    public FilesPage()
    {
        InitializeComponent();
    }

    public void SetDevice(string? serial)
    {
        _serial = serial;
        DeviceLabel.Text = serial == null ? "No Android device connected" : $"Device: {serial}";
    }

    private async void Browse_Click(object sender, RoutedEventArgs e) => await Refresh();

    private async Task Refresh()
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        var path = PathBox.Text.Trim();
        if (path.Length == 0) return;
        try
        {
            var entries = await Task.Run(() => AdbService.Instance.ListDir(_serial, path));
            _currentPath = path;
            _entries.Clear();
            FileList.Items.Clear();
            foreach (var e2 in entries.OrderBy(x => x))
            {
                _entries.Add(e2);
                FileList.Items.Add(e2 + "/");
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private async void FileList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not string item) return;
        var name = item.TrimEnd('/');
        var sub = System.Net.WebUtility.UrlEncode(name)?.Replace("+", "%20");
        var target = _currentPath.EndsWith("/") ? _currentPath + name : _currentPath + "/" + name;
        PathBox.Text = target;
        await Refresh();
    }

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) return;
        if (FileList.SelectedItems.Count == 0) { MessageBox.Show("Select files or folders first."); return; }
        var dlg = new OpenFolderDialog { Title = "Save to folder" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            Status("Copying…");
            foreach (var item in FileList.SelectedItems.Cast<string>())
            {
                var name = item.TrimEnd('/');
                var remote = _currentPath.EndsWith("/") ? _currentPath + name : _currentPath + "/" + name;
                await Task.Run(() => AdbService.Instance.Pull(_serial, remote, dlg.FolderName));
            }
            Status("Done");
            MessageBox.Show($"Saved to {dlg.FolderName}");
        }
        catch (Exception ex) { Status("Failed"); MessageBox.Show(ex.Message); }
    }

    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) return;
        var dlg = new OpenFileDialog { Title = "Pick a file to send", Multiselect = false };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            Status("Sending…");
            await Task.Run(() => AdbService.Instance.Push(_serial, dlg.FileName, _currentPath));
            Status("Done");
            MessageBox.Show("File sent to " + _currentPath);
        }
        catch (Exception ex) { Status("Failed"); MessageBox.Show(ex.Message); }
    }

    private void Status(string s)
    {
        DeviceLabel.Text = _serial == null ? s : $"Device: {_serial} — {s}";
    }
}
