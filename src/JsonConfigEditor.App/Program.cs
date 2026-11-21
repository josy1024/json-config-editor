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
        CommandLine cmd;
        try { cmd = CommandLine.Parse(args); }
        catch (ConfigEditorException ex) { return Fail(ex.Message + Environment.NewLine + Environment.NewLine + CommandLine.Usage, ex.ExitCode, gui: args.Length > 0); }

        if (cmd.Help) { Out(CommandLine.Usage); return ExitCodes.Ok; }
        if (cmd.Version) { Out(VersionText); return ExitCodes.Ok; }

        if (cmd.Validate)
        {
            var r = ConfigValidation.Run(cmd.Options);
            Out(r.Problems.Count == 0 ? "OK" : string.Join(Environment.NewLine, r.Problems));
            return r.ExitCode;
        }

        // User-Datei für Single-Instance; Parameterfehler gleich hier melden
        ConfigPaths paths;
        try { paths = ConfigPaths.Resolve(cmd.Options).Paths; }
        catch (ConfigEditorException ex) { return Fail(ex.Message, ex.ExitCode, gui: true); }

        Process? owner = null;
        if (cmd.OwnerPid is { } pid)
        {
            try { owner = Process.GetProcessById(pid); }
            catch (ArgumentException) { return ExitCodes.Ok; } // aufrufende App ist schon weg
        }

        using var single = SingleInstance.TryAcquire(paths.UserFile, cmd.Options.InitialSection);
        if (single == null) return ExitCodes.Ok;

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new ConfigEditorWindow(cmd.Options);
        var exitCode = window.View.ViewModel.HasLoadError ? window.View.ViewModel.LoadErrorCode : ExitCodes.Ok;

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

        app.Run(window);
        return exitCode;
    }

    private static string VersionText =>
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

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
