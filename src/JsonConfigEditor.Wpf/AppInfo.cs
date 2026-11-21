using System.Diagnostics;
using System.Reflection;

namespace JsonConfigEditor.Wpf;

/// <summary>Version und Projektseite, für Fußzeile und F3-Debug.</summary>
public static class AppInfo
{
    public const string RepoUrl = "https://github.com/josy1024/json-config-editor";

    /// <summary>Version ohne Git-Hash, z. B. "0.2.0".</summary>
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "";

    public static string VersionText => $"json-config-editor {Version}";

    private static readonly Lazy<System.Windows.Media.ImageSource?> LazyIcon = new(() =>
    {
        try
        {
            var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/JsonConfigEditor.Wpf;component/Assets/app.ico"));
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null; // ohne Icon geht's auch
        }
    });

    /// <summary>Fenster-Icon (Titelleiste, Alt+Tab). null, wenn's nicht ladbar ist.</summary>
    public static System.Windows.Media.ImageSource? Icon => LazyIcon.Value;

    /// <summary>Im Standardbrowser öffnen. false = ging nicht.</summary>
    public static bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
