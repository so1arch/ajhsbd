using System.Windows.Forms;

namespace MicStudio;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "MicStudio.SingleInstance.7E1C4A", out bool created);
        if (!created) return;   // уже запущена — значок в трее

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm(args.Contains("--tray")));
    }
}
