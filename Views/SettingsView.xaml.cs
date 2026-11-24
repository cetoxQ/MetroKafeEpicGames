using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using MetroKafeEpicGames.Services;

namespace MetroKafeEpicGames.Views;

public partial class SettingsView : UserControl, IPage
{
    public SettingsView()
    {
        InitializeComponent();
        LoadFromSettings();
    }

    public void OnShown() => LoadFromSettings();

    private void LoadFromSettings()
    {
        var s = AppState.Settings;
        FileBox.Text = s.FilePath;
        FileBox_TextChanged(FileBox, null!);
        RepoBox.Text = s.GitHubRepo;
        CheckUpdatesBox.IsChecked = s.CheckUpdatesOnStart;
    }

    private void FileBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var p = Environment.ExpandEnvironmentVariables(FileBox.Text.Trim());
        if (p.Length == 0)
        {
            var auto = FindAuto();
            FileInfo.Text = auto != null ? "Otomatik bulunan: " + auto : "Otomatik arama: bu kullanıcıda dosya bulunamadı";
            return;
        }
        FileInfo.Text = File.Exists(p) ? "✓ Dosya var" : "⚠ Dosya yok";
    }

    private static string? FindAuto()
    {
        var saved = AppState.Settings.FilePath;
        AppState.Settings.FilePath = "";
        try { return AppState.FindJournal(); }
        finally { AppState.Settings.FilePath = saved; }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "installations.jsonl seç", Filter = "installations.jsonl|*.jsonl|Tüm dosyalar|*.*" };
        if (Directory.Exists(AppState.LauncherDataFolder)) dlg.InitialDirectory = AppState.LauncherDataFolder;
        if (dlg.ShowDialog() == true) FileBox.Text = dlg.FileName;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var p = Environment.ExpandEnvironmentVariables(FileBox.Text.Trim());
        AppState.OpenInExplorer(p.Length > 0 ? p : FindAuto() ?? AppState.LauncherDataFolder);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var repo = RepoBox.Text.Trim().Trim('/');
        if (repo.Length > 0 && repo.Count(c => c == '/') != 1)
        {
            await MainWindow.Instance.InfoAsync("Kaydedilemedi", "Depo 'sahip/ad' biçiminde olmalı (örn. cetoxQ/MetroKafeEpicGames).");
            return;
        }

        var s = AppState.Settings;
        var fileChanged = !string.Equals(s.FilePath, FileBox.Text.Trim(), StringComparison.OrdinalIgnoreCase);
        s.FilePath = FileBox.Text.Trim();
        s.GitHubRepo = repo;
        s.CheckUpdatesOnStart = CheckUpdatesBox.IsChecked == true;
        AppState.Save();
        AppState.AddLog("Ayarlar kaydedildi", LogKind.Success);
        if (fileChanged)
        {
            var p = AppState.FindJournal();
            if (p != null && File.Exists(p)) _ = MainWindow.Instance.InstallsPage.LoadAsync(p);
        }

        var flash = new DoubleAnimationUsingKeyFrames();
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(150))));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1800))));
        flash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
        SavedText.BeginAnimation(OpacityProperty, flash);
    }
}
