using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using JsonConfigEditor.Core;
using JsonConfigEditor.Wpf;

namespace JsonConfigEditor.App;

public static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Log.Enabled = true;
        Log.Cleanup();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Absturz (unbehandelt)", e.ExceptionObject as Exception);
            ShowCrash(e.ExceptionObject as Exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Task-Fehler", e.Exception);
            e.SetObserved();
        };

        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Log.Error("Absturz", ex);
            ShowCrash(ex);
            return ExitCodes.Crash;
        }
    }

    private static int Run(string[] args)
    {
        Log.Info($"Start {VersionText}" + (args.Length > 0 ? ": " + string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a)) : ""));

        CommandLine cmd;
        try { cmd = CommandLine.Parse(args); }
        catch (ConfigEditorException ex)
        {
            Log.Warn("Parameter: " + ex.Message);
            return Fail(ex.Message + Environment.NewLine + Environment.NewLine + "--help zeigt alle Optionen.", ex.ExitCode, gui: true);
        }

        if (cmd.Help) { Out(CommandLine.Usage); return ExitCodes.Ok; }
        if (cmd.Version) { Out(VersionText); return ExitCodes.Ok; }
        if (cmd.List) { Out(ListText()); return ExitCodes.Ok; }

        if (cmd.Validate)
        {
            if (!cmd.HasTarget) return Fail("--validate braucht einen Pfad (Descriptor oder --base).", ExitCodes.BadArguments, gui: false);
            var r = ConfigValidation.Run(cmd.Options);
            Out(r.Problems.Count == 0 ? "OK" : string.Join(Environment.NewLine, r.Problems));
            return r.ExitCode;
        }

        Application? app = null;
        if (!cmd.HasTarget)
        {
            // Doppelklick ohne Parameter: genau ein Descriptor in der Nähe → direkt öffnen, sonst Startseite
            if (!cmd.Pick)
            {
                var near = AppCatalog.FindNearby(AppContext.BaseDirectory, CatalogLocations.Default.CurrentDir);
                if (near.Count == 1)
                {
                    Log.Info("Gefunden, öffne direkt: " + near[0]);
                    cmd.Options.DescriptorPath = near[0];
                }
            }
            if (!cmd.HasTarget)
            {
                app = CreateApplication();
                var chosen = AppPickerWindow.Choose();
                if (chosen == null) return ExitCodes.Ok;
                cmd.Options.DescriptorPath = chosen.DescriptorPath;
                cmd.Options.BasePath = chosen.BasePath;
                cmd.Options.UserPath ??= chosen.UserPath;
                cmd.Options.MachinePath ??= chosen.MachinePath;
                cmd.Options.AppName ??= chosen.AppName;
            }
        }

        // User-Datei für Single-Instance; Parameterfehler gleich hier melden
        ConfigPaths paths;
        try { paths = ConfigPaths.Resolve(cmd.Options).Paths; }
        catch (ConfigEditorException ex)
        {
            Log.Warn("Pfade: " + ex.Message);
            return Fail(ex.Message, ex.ExitCode, gui: true);
        }

        Process? owner = null;
        if (cmd.OwnerPid is { } pid)
        {
            try { owner = Process.GetProcessById(pid); }
            catch (ArgumentException) { return ExitCodes.Ok; } // aufrufende App ist schon weg
        }

        using var single = SingleInstance.TryAcquire(paths.UserFile, cmd.Options.InitialSection);
        if (single == null)
        {
            Log.Info("Schon offen, vorhandenes Fenster aktiviert: " + paths.UserFile);
            return ExitCodes.Ok;
        }

        app ??= CreateApplication();
        var window = new ConfigEditorWindow(cmd.Options);
        var vm = window.View.ViewModel;
        var exitCode = vm.HasLoadError ? vm.LoadErrorCode : ExitCodes.Ok;
        if (!vm.HasLoadError) AppCatalog.RememberRecent(cmd.Options, vm.Document.Paths.AppName);

        vm.CanOpenOther = Environment.ProcessPath != null;
        vm.OpenOtherRequested += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--pick") { UseShellExecute = false }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Log.Warn("Startseite nicht gestartet", ex);
            }
        };

        single.Listen(section => app.Dispatcher.BeginInvoke(() => window.Activate(section)));

        if (owner != null)
        {
            try
            {
                owner.EnableRaisingEvents = true;
                owner.Exited += (_, _) => app.Dispatcher.BeginInvoke(window.Close);
                if (owner.HasExited) return ExitCodes.Ok;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Prozess darf nicht beobachtet werden: dann bleibt der Editor einfach offen
            }
        }

        app.MainWindow = window;
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        app.Run(window);
        Log.Info("Ende");
        return exitCode;
    }

    /// <summary>Application mit Fehlerfang: Fehler im UI-Thread → Meldung + Log, Fenster bleibt offen (Speichern ist atomar).</summary>
    private static Application CreateApplication()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Fehler im Fenster", e.Exception);
            MessageBox.Show(
                $"Unerwarteter Fehler: {e.Exception.Message}{Environment.NewLine}{Environment.NewLine}Bereits gespeicherte Werte sind sicher. Details im Log:{Environment.NewLine}{Log.CurrentFile}",
                "json-config-editor", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
        return app;
    }

    private static string ListText()
    {
        var c = AppCatalog.Load();
        var lines = new List<string>();
        if (c.Entries.Count == 0) lines.Add("Keine Apps bekannt.");
        foreach (var e in c.Entries)
            lines.Add($"{e.Name}\t[{e.Source}]\t{e.Target}" + (e.Problem != null ? $"\t({e.Problem})" : ""));
        foreach (var w in c.Warnings) lines.Add("Hinweis: " + w);
        return string.Join(Environment.NewLine, lines);
    }

    private static string VersionText =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static void ShowCrash(Exception? ex)
    {
        var message = $"json-config-editor ist abgestürzt: {ex?.Message}{Environment.NewLine}{Environment.NewLine}Details im Log:{Environment.NewLine}{Log.CurrentFile}";
        try
        {
            if (AttachToConsole()) Console.Error.WriteLine(message);
            else MessageBox.Show(message, "json-config-editor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception) { /* nichts mehr zu retten */ }
    }

    private static int Fail(string message, int code, bool gui)
    {
        if (AttachToConsole()) Console.Error.WriteLine(message);
        else if (gui) MessageBox.Show(message, "json-config-editor", MessageBoxButton.OK, MessageBoxImage.Warning);
        return code;
    }

    private static void Out(string text)
    {
        AttachToConsole();
        Console.WriteLine(text);
    }

    private static bool _attached;

    /// <summary>WinExe hat keine Konsole: an die des Aufrufers hängen (cmd, PowerShell). true wenn eine da ist.</summary>
    private static bool AttachToConsole()
    {
        if (_attached) return true;
        if (Console.IsOutputRedirected) return _attached = true;
        return _attached = AttachConsole(-1);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
