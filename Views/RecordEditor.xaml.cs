using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MetroKafeEpicGames.Models;

namespace MetroKafeEpicGames.Views;

public partial class RecordEditor : UserControl, IDialogBody
{
    public string Dir => DirBox.Text.Trim().TrimEnd('\\');
    public string Exe => ExeBox.Text.Trim();
    public bool Complete => CompleteBox.IsChecked == true;
    public bool Restore => RestoreBox.IsChecked == true;

    public RecordEditor(InstallRecord r)
    {
        InitializeComponent();
        IdText.Text = r.Id;
        ArtifactText.Text = r.ArtifactId;
        VersionText.Text = r.Version;
        StateText.Text = $"{r.StateText} · {r.Detail}";
        DirBox.Text = r.Dir;
        ExeBox.Text = r.Exe;
        CompleteBox.IsChecked = r.Complete;
        if (r.CanRestore)
        {
            RestoreBox.Visibility = Visibility.Visible;
            RestoreBox.IsChecked = true;
        }
        UpdateStates();
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e) => UpdateStates();

    private void UpdateStates()
    {
        if (DirState == null || ExeState == null) return;
        var dirOk = Dir.Length > 0 && Directory.Exists(Dir);
        DirState.Text = Dir.Length == 0 ? "" : dirOk ? "✓ Klasör var" : "⚠ Klasör yok";
        DirState.Foreground = (System.Windows.Media.Brush)FindResource(dirOk ? "SuccessBrush" : "WarningBrush");

        if (Exe.Length == 0) { ExeState.Text = "Boş (alt paketlerde normal)"; ExeState.Foreground = (System.Windows.Media.Brush)FindResource("FaintTextBrush"); return; }
        var exeOk = dirOk && File.Exists(Path.Combine(Dir, Exe.Replace('/', '\\')));
        ExeState.Text = exeOk ? "✓ Dosya var" : "⚠ Dosya bulunamadı";
        ExeState.Foreground = (System.Windows.Media.Brush)FindResource(exeOk ? "SuccessBrush" : "WarningBrush");
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Oyun klasörünü seç" };
        var start = Directory.Exists(Dir) ? Dir : Path.GetDirectoryName(Dir);
        if (start != null && Directory.Exists(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog() == true) DirBox.Text = dlg.FolderName;
    }

    public bool Validate()
    {
        if (Dir.Length == 0 || !Path.IsPathFullyQualified(Dir))
        {
            ErrorText.Text = "Klasör tam bir yol olmalı (örn. X:\\Epic Games\\Fortnite).";
            ErrorText.Visibility = Visibility.Visible;
            return false;
        }
        return true;
    }
}
