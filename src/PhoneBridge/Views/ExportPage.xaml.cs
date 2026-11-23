using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using PhoneBridge.Services;

namespace PhoneBridge.Views;

public partial class ExportPage : UserControl
{
    private string? _serial;

    public ExportPage()
    {
        InitializeComponent();
    }

    public void SetDevice(string? serial)
    {
        _serial = serial;
        DeviceLabel.Text = serial == null ? "No Android device connected" : $"Device: {serial}";
    }

    private void RunExport(Func<int> export, string doneMsg)
    {
        try
        {
            var n = export();
            MessageBox.Show($"{doneMsg}\n{n} record(s) exported.", "PhoneBridge",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Contacts_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        var dlg = new SaveFileDialog { FileName = $"contacts_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV file|*.csv" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var serial = _serial;
        Status("Exporting contacts…");
        Task.Run(() => RunExport(() => ExportService.Instance.ExportContacts(serial, dlg.FileName), "Contacts exported to\n" + dlg.FileName))
            .ContinueWith(_ => Status(""));
    }

    private void Sms_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        var dlg = new SaveFileDialog { FileName = $"sms_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV file|*.csv" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var serial = _serial;
        Status("Exporting SMS…");
        Task.Run(() => RunExport(() => ExportService.Instance.ExportSms(serial, dlg.FileName), "SMS exported to\n" + dlg.FileName))
            .ContinueWith(_ => Status(""));
    }

    private void Calls_Click(object sender, RoutedEventArgs e)
    {
        if (_serial == null) { MessageBox.Show("Connect an Android device first."); return; }
        var dlg = new SaveFileDialog { FileName = $"calllog_{DateTime.Now:yyyyMMdd_HHmmss}.csv", Filter = "CSV file|*.csv" };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
        var serial = _serial;
        Status("Exporting call log…");
        Task.Run(() => RunExport(() => ExportService.Instance.ExportCalls(serial, dlg.FileName), "Call log exported to\n" + dlg.FileName))
            .ContinueWith(_ => Status(""));
    }

    private void Status(string s)
    {
        DeviceLabel.Text = _serial == null ? s : $"Device: {_serial} — {s}";
    }
}
