using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;

namespace MetroKafeEpicGames.Services;

public enum LogKind { Info, Success, Warning, Error }

public record LogLine(DateTime Time, string Text, LogKind Kind)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}

public class AppSettings
{
    /// <summary>Bos ise installations.jsonl otomatik bulunur.</summary>
    public string FilePath { get; set; } = "";
    public bool CheckUpdatesOnStart { get; set; } = true;
    public string GitHubRepo { get; set; } = "cetoxQ/MetroKafeEpicGames";
}

/// <summary>Ayarlar ve islem kaydi.</summary>
public static class AppState
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Settings { get; private set; } = new();
    public static ObservableCollection<LogLine> Log { get; } = new();
    public static string? StartupFile { get; set; }

    public static string ExeFolder => AppContext.BaseDirectory.TrimEnd('\\');
    public static string SettingsPath => Path.Combine(ExeFolder, "MetroKafeEpicGames.json");
    public static string LogPath => Path.Combine(ExeFolder, "MetroKafeEpicGames.log");
    public static Version Version => typeof(AppState).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static string VersionText => Version.ToString(3);

    /// <summary>Yeni Epic launcher'in (eglv2) veri klasoru.</summary>
    public static string LauncherDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "com.epicgames.eglv2");

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), Json) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Settings = new AppSettings();
            AddLog("Ayarlar okunamadı, varsayılan kullanılıyor: " + ex.Message, LogKind.Error);
        }
    }

    public static void Save()
    {
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, Json));
        File.Move(tmp, SettingsPath, true);
    }

    public static void AddLog(string text, LogKind kind = LogKind.Info)
    {
        var line = new LogLine(DateTime.Now, text, kind);
        void Add()
        {
            Log.Insert(0, line);
            while (Log.Count > 500) Log.RemoveAt(Log.Count - 1);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) Add();
        else dispatcher.BeginInvoke(Add);

        try { File.AppendAllText(LogPath, $"{line.Time:yyyy-MM-dd HH:mm:ss} [{kind}] {text}{Environment.NewLine}"); }
        catch { /* kayit dosyasi yazilamazsa arayuzdeki kayit yeterli */ }
    }

    /// <summary>Ayarlardaki yol ya da otomatik bulunan installations.jsonl.</summary>
    public static string? FindJournal()
    {
        var configured = Environment.ExpandEnvironmentVariables(Settings.FilePath.Trim());
        if (configured.Length > 0) return configured;

        var root = LauncherDataFolder;
        var direct = Path.Combine(root, "installations.jsonl");
        if (File.Exists(direct)) return direct;
        if (!Directory.Exists(root)) return null;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4 };
        return Directory.EnumerateFiles(root, "installations.jsonl", options)
                        .FirstOrDefault(f => !f.Contains(@"\EBWebView\", StringComparison.OrdinalIgnoreCase));
    }

    public static void OpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (File.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else
        {
            var parent = Path.GetDirectoryName(path.TrimEnd('\\'));
            if (parent != null && Directory.Exists(parent)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{parent}\"") { UseShellExecute = true });
        }
    }

    public static Process[] LauncherProcesses() => Process.GetProcessesByName("EpicGamesLauncher");
}
