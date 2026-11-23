using System.IO;
using System.Windows;
using PhoneBridge.Services;

namespace PhoneBridge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Headless mode for the Windows scheduled task:
        //   PhoneBridge.exe --backup "<destParent>"
        if (e.Args.Length == 2 && e.Args[0] == "--backup")
        {
            base.OnStartup(e);
            RunHeadlessBackup(e.Args[1]);
            Shutdown(0);
            return;
        }
        base.OnStartup(e);
    }

    private void RunHeadlessBackup(string destParent)
    {
        var logPath = Path.Combine(destParent, "phonebridge_backup.log");
        try
        {
            Directory.CreateDirectory(destParent);
            var dest = BackupRunner.Run(null, destParent, s => { /* silent */ });
            File.AppendAllText(logPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  OK  {dest}\r\n");
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  FAIL  {ex.Message}\r\n");
            }
            catch { /* nothing more we can do headless */ }
        }
    }
}
