using System;
using System.IO;
using System.Windows;

namespace Roundtime.App
{
    internal static class App
    {
        [STAThread]
        private static void Main(string[] args)
        {
            string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roundtime", "settings.json");
            if (args.Length == 2 && args[0] == "--settings") settingsPath = Path.GetFullPath(args[1]);
            var application = new Application();
            var main = new MainWindow(settingsPath);
            application.Run(main.Window);
        }
    }
}
