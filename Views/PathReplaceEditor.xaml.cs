using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MetroKafeEpicGames.Services;

namespace MetroKafeEpicGames.Views;

public partial class PathReplaceEditor : UserControl, IDialogBody
{
    private readonly Journal _journal;

    public string OldPrefix => OldBox.Text.Trim().TrimEnd('\\');
    public string NewPrefix => NewBox.Text.Trim().TrimEnd('\\');

    public PathReplaceEditor(Journal journal, string suggestedOld)
    {
        InitializeComponent();
        _journal = journal;
        OldBox.Text = suggestedOld;
        NewBox.Text = suggestedOld;
        Update();
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e) => Update();

    private void Update()
    {
        if (CountText == null || ExampleText == null) return;
        if (OldPrefix.Length == 0) { CountText.Text = "Eski yol başını yazın"; ExampleText.Text = ""; return; }

        var count = _journal.ReplacePrefix(OldPrefix, NewPrefix, apply: false);
        CountText.Text = count == 0 ? "Bu yolla başlayan kayıt yok" : $"{count} kurulumun yolu değişecek";

        var sample = _journal.Lines.Select(l => l.Str("dir")).FirstOrDefault(d => d != null && Journal.StartsWithPath(d, OldPrefix));
        if (sample == null || NewPrefix.Length == 0) { ExampleText.Text = ""; return; }
        var result = NewPrefix + sample[OldPrefix.Length..];
        ExampleText.Text = $"Örnek: {sample}\n     → {result}   {(Directory.Exists(result) ? "✓ klasör var" : "⚠ klasör yok")}";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Yeni yol başını seç" };
        if (Directory.Exists(NewPrefix)) dlg.InitialDirectory = NewPrefix;
        if (dlg.ShowDialog() == true) NewBox.Text = dlg.FolderName;
    }

    public bool Validate()
    {
        string? error = null;
        if (OldPrefix.Length == 0 || NewPrefix.Length == 0) error = "İki yol da dolu olmalı.";
        else if (!Path.IsPathFullyQualified(NewPrefix)) error = "Yeni yol tam bir yol olmalı (örn. X:\\Epic Games).";
        else if (OldPrefix.Equals(NewPrefix, StringComparison.OrdinalIgnoreCase)) error = "Yeni yol eskisiyle aynı.";
        else if (_journal.ReplacePrefix(OldPrefix, NewPrefix, apply: false) == 0) error = "Bu yolla başlayan kayıt yok.";
        if (error == null) return true;
        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
        return false;
    }
}
