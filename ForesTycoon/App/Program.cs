using System.Runtime.Versioning;
using System;
using System.Windows.Forms;

[assembly: SupportedOSPlatform("windows6.1")]

namespace ForesTycoon
{
    static class Program
    {
        [System.STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) =>
                MessageBox.Show(args.Exception.ToString(), "Unhandled UI exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                MessageBox.Show(args.ExceptionObject?.ToString() ?? "Unknown fatal error", "Unhandled fatal exception", MessageBoxButtons.OK, MessageBoxIcon.Error);

            Application.Run(new MainForm());
        }
    }
}
