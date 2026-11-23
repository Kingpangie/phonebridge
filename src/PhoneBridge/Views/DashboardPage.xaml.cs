using System.Windows;
using System.Windows.Controls;
using PhoneBridge.Services;

namespace PhoneBridge.Views;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
    }

    public void UpdateDevices(List<AdbService.Device> android, List<IosService.IosDevice> ios, bool appleServiceRunning)
    {
        AndroidList.ItemsSource = android.Where(d => d.Ready).ToList();
        AndroidEmpty.Visibility = android.Any(d => d.Ready) ? Visibility.Collapsed : Visibility.Visible;

        var iosList = ios.ToList();
        IosList.ItemsSource = iosList;

        if (iosList.Count > 0)
            IosHint.Text = "Full iOS features (backups via Apple's service) require iTunes or the Apple Devices app.";
        else
            IosHint.Text = appleServiceRunning
                ? "Apple service is running — plug in your iPhone and tap 'Trust' on the device."
                : "No iPhone detected. Install iTunes or the Apple Devices app from the Microsoft Store, then connect your phone.";
    }
}
