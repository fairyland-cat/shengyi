using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Shengyi;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--preview")
        {
            var preview = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            preview.Startup += (_, _) => Preview.Render(Path.GetFullPath(args[1]), preview.Shutdown);
            preview.Run();
            return;
        }
        if (args.Length == 2 && args[0] == "--shots")
        {
            var shots = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            shots.Startup += (_, _) => Shots.Run(Path.GetFullPath(args[1]), shots.Shutdown);
            shots.Run();
            return;
        }

        using var mutex = new Mutex(true, @"Local\shengyi.SingleInstance", out bool first);
        if (!first) return;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new AppController(app);
        app.Startup += (_, _) => controller.Start();
        app.Exit += (_, _) => controller.Dispose();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(e.Exception);
            e.Handled = true;
        };
        app.Run();
    }

    static void Log(Exception e)
    {
        try
        {
            Directory.CreateDirectory(Settings.DataDir);
            File.AppendAllText(Path.Combine(Settings.DataDir, "error.log"), $"[{DateTime.Now:s}] {e}\n\n");
        }
        catch (IOException) { }
    }
}
