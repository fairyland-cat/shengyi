using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        // The two offline renderers return above and never reach Init, so --shots / --preview
        // create neither a log file nor a background writer. Three exception channels feed the
        // same log; the old unbounded error.log is gone (Diag.Clear also removes a leftover).
        var boot = Settings.Load();
        string version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        Diag.Init(Settings.DataDir, Diag.ParseMode(boot.DiagnosticsEnabled, boot.DiagnosticsLevel), version);
        Diag.Info("boot", "process starting",
            ("version", version), ("os", Environment.OSVersion.VersionString),
            ("runtime", Environment.Version.ToString()), ("pid", Environment.ProcessId),
            ("mode", Diag.ModeId(Diag.Mode)));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Diag.Crash("AppDomain.UnhandledException", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Diag.Crash("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var controller = new AppController(app);
        app.Startup += (_, _) => controller.Start();
        app.Exit += (_, _) =>
        {
            controller.Dispose();
            Diag.Info("boot", "process exiting");
            Diag.Shutdown();
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Diag.Crash("DispatcherUnhandledException", e.Exception);
            e.Handled = true;
        };
        app.Run();
    }
}
