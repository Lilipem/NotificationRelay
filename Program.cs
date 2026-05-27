using System.Runtime.InteropServices;
using System.Text;

namespace NotificationRelay;

// Program starts here. Checks for package identity and shows setup instructions if not found.
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        if (!HasPackageIdentity())
        {
            MessageBox.Show(
                "Notification Relay needs a one-time setup to capture transient notifications.\n\n" +
                "1. Run this in PowerShell:\n\n" +
                "    .\\Register-Package.ps1\n\n" +
                "2. Then launch via PowerShell:\n\n" +
                "    Start-Process \"shell:AppsFolder\\com.NotificationRelay.Desktop_fmsbxbq82ser0!App\"\n\n" +
                "Or search 'Notification Relay' in the Start menu.",
                "Setup Required — Run Register-Package.ps1",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        Application.Run(new TrayApplicationContext());
    }

    // Win32 API: returns the package full name of the current process, or APPMODEL_ERROR_NO_PACKAGE (15700) if the process has no package identity.
    // This is important because only processes with package identity can register for and receive transient notifications.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(
        ref int packageFullNameLength, StringBuilder? packageFullName);

    private static bool HasPackageIdentity()
    {
        int len = 0;
        return GetCurrentPackageFullName(ref len, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
    }
}
