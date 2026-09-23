using System;
using System.Windows.Forms;

namespace OfflineScan
{
    internal static class Program
    {
        [STAThread] // WIA yra COM ir reikalauja STA gijos
        private static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
