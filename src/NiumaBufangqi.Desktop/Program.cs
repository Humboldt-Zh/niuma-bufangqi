using System;
using Forms = System.Windows.Forms;

namespace NiumaBufangqi.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        Forms.Application.SetCompatibleTextRenderingDefault(false);
        var application = new App();
        application.InitializeComponent();
        application.Run();
    }
}
