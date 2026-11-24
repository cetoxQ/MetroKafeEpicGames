using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MetroKafeEpicGames.Models;
using MetroKafeEpicGames.Services;

namespace MetroKafeEpicGames.Views;

public partial class InstallsView : UserControl, IPage
{
    private static readonly string[] Filters = { "Tümü", "Kurulu", "Silinmiş", "Sorunlu" };

    private Journal? _journal;
    private List<InstallRecord> _records = new();
    private ICollectionView? _view;
    private string _filter = Filters[0];
    private bool _busy;
    private bool _loaded;

    public InstallsView()
    {
        InitializeComponent();
        FilterChips.ItemsSource = Filters;
        FilterChips.SelectedIndex = 0;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F5) { _ = LoadAsync(); e.Handled = true; } };
        UpdateSelectionText();
    }

    public bool HasUnsavedChanges => _journal?.Dirty == true;

    public void OnShown()
    {
        if (!_loaded) { _loaded = true; _ = LoadAsync(AppState.StartupFile); }
    }

    // ---- Yukleme ----

    public async Task LoadAsync(string? path = null)
    {
        if (_journal?.Dirty == true &&
            !await MainWindow.Instance.ConfirmAsync("Yeniden yükle", "Kaydedilmemiş değişiklikler kaybolacak. Devam edilsin mi?", "Yeniden yükle", danger: true))
            return;

        path ??= _journal?.FilePath ?? AppState.FindJournal();
        if (path == null || !File.Exists(path))
        {
            _journal = null;
            ShowRecords(new List<InstallRecord>());
            FileText.Text = "installations.jsonl bulunamadı";
            EmptyText.Text = path == null
                ? $"Bu kullanıcıda Epic Games Launcher'ın kurulum kaydı bulunamadı.\n({AppState.LauncherDataFolder})\n\nDosyayı elle seçebilirsiniz."
                : "Dosya yok: " + path;
            EmptyOpenButton.Visibility = Visibility.Visible;
            return;
        }

        await RunBusyAsync("Okunuyor…", () =>
        {
            var j = Journal.Load(path);
            var recs = j.BuildRecords();
            Dispatcher.Invoke(() =>
            {
                _journal = j;
                ShowRecords(recs);
                AppState.AddLog($"Okundu: {path} ({j.Lines.Count} satır, {recs.Count} kurulum)", LogKind.Success);
                if (j.BrokenLines > 0) AppState.AddLog($"{j.BrokenLines} satır JSON olarak okunamadı; olduğu gibi korunacak", LogKind.Warning);
            });
        });
    }

    private void ShowRecords(List<InstallRecord> records, IEnumerable<string>? keepSelected = null)
    {
        _records = records;
        _view = CollectionViewSource.GetDefaultView(_records);
        _view.Filter = Filter;
        List.ItemsSource = _view;

        if (keepSelected != null)
        {
            var ids = keepSelected.ToHashSet();
            foreach (var r in _records.Where(r => ids.Contains(r.Id))) List.SelectedItems.Add(r);
        }

        FileText.Text = _journal == null ? "" : _journal.FilePath;
        var top = _records.Where(r => !r.IsChild).ToList();
        StatTotal.Text = top.Count.ToString();
        StatInstalled.Text = top.Count(r => r.State == RecordState.Installed).ToString();
        StatDeleted.Text = top.Count(r => r.Deleted).ToString();
        StatProblem.Text = top.Count(r => r.State is RecordState.FolderMissing or RecordState.ExeMissing or RecordState.Duplicate).ToString();

        var dirty = _journal?.Dirty == true;
        SaveButton.IsEnabled = dirty;
        SaveText.Text = dirty ? "Kaydet  •" : "Kaydet";

        UpdateEmpty();
        UpdateSelectionText();
    }

    /// <summary>Degisiklikten sonra kayitlari gunlukten yeniden hesaplar.</summary>
    private void Rebuild()
    {
        if (_journal == null) return;
        var selected = List.SelectedItems.Cast<InstallRecord>().Select(r => r.Id).ToList();
        ShowRecords(_journal.BuildRecords(), selected);
    }

    private void UpdateEmpty()
    {
        var empty = _view == null || _view.IsEmpty;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (_journal != null && empty)
        {
            EmptyText.Text = _records.Count == 0 ? "Dosyada kurulum kaydı yok." : "Bu filtreye uyan kayıt yok.";
            EmptyOpenButton.Visibility = Visibility.Collapsed;
        }
    }

    // ---- Filtre ----

    private bool Filter(object o)
    {
        if (o is not InstallRecord r) return false;
        var ok = _filter switch
        {
            "Kurulu" => r.State == RecordState.Installed,
            "Silinmiş" => r.Deleted,
            "Sorunlu" => r.State is RecordState.FolderMissing or RecordState.ExeMissing or RecordState.Duplicate or RecordState.Unknown or RecordState.Incomplete,
            _ => true,
        };
        if (!ok) return false;
        var q = SearchBox?.Text.Trim() ?? "";
        return q.Length == 0
               || r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || r.Dir.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || r.SubName.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || r.Id.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilterChips.SelectedItem is not string f) return;
        _filter = f;
        _view?.Refresh();
        UpdateEmpty();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateEmpty();
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionText();

    private List<InstallRecord> Selected => List.SelectedItems.Cast<InstallRecord>().ToList();

    private void UpdateSelectionText()
    {
        var n = List.SelectedItems.Count;
        SelectionText.Text = n == 0 ? "Seçim yok" : $"{n} kayıt seçili";
        RestoreText.Text = n == 0 ? "Silinenleri geri yükle" : "Seçilenleri geri yükle";
    }

    // ---- Dosya ----

    private void Reload_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "installations.jsonl seç",
            Filter = "Epic kurulum günlüğü (installations.jsonl)|installations.jsonl;*.jsonl|Tüm dosyalar|*.*",
        };
        var start = _journal != null ? Path.GetDirectoryName(_journal.FilePath) : AppState.LauncherDataFolder;
        if (start != null && Directory.Exists(start)) dlg.InitialDirectory = start;
        if (dlg.ShowDialog() != true) return;
        await LoadAsync(dlg.FileName);
    }

    private void FileText_Click(object sender, MouseButtonEventArgs e)
    {
        if (_journal != null) AppState.OpenInExplorer(_journal.FilePath);
    }

    public void SaveFromShortcut()
    {
        if (_journal?.Dirty == true) Save_Click(this, new RoutedEventArgs());
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null || !_journal.Dirty || _busy) return;

        if (_journal.ChangedOnDisk &&
            !await MainWindow.Instance.ConfirmAsync("Dosya değişmiş",
                "Siz düzenlerken dosya diskte değişti (launcher yazmış olabilir). Kaydederseniz o değişiklikler kaybolur. Yine de kaydedilsin mi?",
                "Üzerine yaz", danger: true))
            return;

        var launchers = AppState.LauncherProcesses();
        if (launchers.Length > 0)
        {
            if (!await MainWindow.Instance.ConfirmAsync("Epic Launcher açık",
                    "Launcher açıkken dosyayı kendisi yeniden yazabilir ve değişiklikleriniz kaybolur. Launcher kapatılıp kaydedilsin mi?",
                    "Kapat ve kaydet"))
                return;
        }

        var journal = _journal;
        await RunBusyAsync("Kaydediliyor…", () =>
        {
            foreach (var p in launchers)
            {
                try { p.Kill(true); p.WaitForExit(10000); }
                catch (Exception ex) { AppState.AddLog("Launcher kapatılamadı: " + ex.Message, LogKind.Error); }
            }
            if (launchers.Length > 0) AppState.AddLog("Epic Launcher kapatıldı", LogKind.Warning);
            var backup = journal.Save();
            AppState.AddLog($"Kaydedildi ({journal.Lines.Count} satır). Yedek: {backup}", LogKind.Success);
        });
        Rebuild();
    }

    // ---- Kayit islemleri ----

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null) return;
        var items = (Selected.Count > 0 ? Selected : _records).Where(r => r.CanRestore).ToList();
        if (items.Count == 0) { await MainWindow.Instance.InfoAsync("Geri yükle", "Geri yüklenecek silinmiş kayıt yok."); return; }

        var noFolder = items.Where(r => !Directory.Exists(r.Dir)).ToList();
        var msg = string.Join("\n", items.Select(r => $"• {r.Name}{(r.IsChild ? "  (" + r.SubName + ")" : "")}"));
        if (noFolder.Count > 0)
            msg += $"\n\nUyarı: {noFolder.Count} kaydın klasörü diskte yok. Launcher bunları açılışta yeniden silebilir; önce klasörü düzeltin.";
        if (!await MainWindow.Instance.ConfirmAsync($"{items.Count} kayıt geri yüklensin mi?", msg, "Geri yükle")) return;

        var removed = items.Sum(r => _journal.Restore(r.Id));
        AppState.AddLog($"{items.Count} kayıt geri yüklendi ({removed} silme satırı kaldırıldı). Kaydetmeyi unutmayın.", LogKind.Success);
        Rebuild();
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null) return;
        var items = Selected;
        if (items.Count == 0) { await MainWindow.Instance.InfoAsync("Kaydı sil", "Önce listeden kayıt seçin."); return; }
        var msg = string.Join("\n", items.Select(r => $"• {r.Name}  ({r.LineCount} satır)")) +
                  "\n\nBu kurulumların bütün satırları dosyadan silinecek. Oyun dosyalarına dokunulmaz.";
        if (!await MainWindow.Instance.ConfirmAsync("Kayıtlar silinsin mi?", msg, "Sil", danger: true)) return;

        var removed = items.Sum(r => _journal.Remove(r.Id));
        AppState.AddLog($"{items.Count} kurulumun {removed} satırı silindi. Kaydetmeyi unutmayın.", LogKind.Warning);
        Rebuild();
    }

    private async void Edit_Click(object sender, RoutedEventArgs e) => await EditAsync(List.SelectedItem as InstallRecord);

    private async void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is InstallRecord r) await EditAsync(r);
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Edit_Click(sender, e);
        else if (e.Key == Key.Delete) Remove_Click(sender, e);
    }

    private async Task EditAsync(InstallRecord? r)
    {
        if (_journal == null) return;
        if (r == null) { await MainWindow.Instance.InfoAsync("Düzenle", "Önce listeden bir kayıt seçin."); return; }
        if (_journal.LatestData(r.Id) == null) { await MainWindow.Instance.InfoAsync("Düzenle", "Bu kaydın düzenlenecek verisi yok."); return; }

        var editor = new RecordEditor(r);
        if (!await MainWindow.Instance.ShowDialogAsync($"{r.Name} düzenle", editor, "Uygula")) return;
        _journal.Update(r.Id, editor.Dir, editor.Exe, editor.Complete);
        if (editor.Restore) _journal.Restore(r.Id);
        AppState.AddLog($"{r.Name}: güncellendi. Kaydetmeyi unutmayın.", LogKind.Success);
        Rebuild();
    }

    private async void Json_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null) return;
        if (List.SelectedItem is not InstallRecord r) { await MainWindow.Instance.InfoAsync("JSON", "Önce listeden bir kayıt seçin."); return; }
        var json = _journal.PrettyLatest(r.Id);
        if (json.Length == 0) { await MainWindow.Instance.InfoAsync("JSON", "Bu kaydın düzenlenecek satırı yok."); return; }

        var editor = new JsonEditor(json, r.Id);
        if (!await MainWindow.Instance.ShowDialogAsync($"{r.Name} · son satır (JSON)", editor, "Uygula")) return;
        _journal.ReplaceLatest(r.Id, editor.Result!);
        AppState.AddLog($"{r.Name}: JSON güncellendi. Kaydetmeyi unutmayın.", LogKind.Success);
        Rebuild();
    }

    // ---- Kod yedegi ----

    private async void SaveCode_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null) return;
        var items = Selected;
        if (items.Count == 0) { await MainWindow.Instance.InfoAsync("Kodu kaydet", "Önce listeden yedeklenecek oyunu seçin."); return; }

        var files = new List<string>();
        foreach (var r in items)
        {
            try
            {
                var file = CodeBackup.Save(_journal, r);
                files.Add(file);
                AppState.AddLog($"{r.Name}: kodu kaydedildi → {file}", LogKind.Success);
            }
            catch (Exception ex) { AppState.AddLog($"{r.Name}: kod kaydedilemedi: {ex.Message}", LogKind.Error); }
        }
        if (files.Count == 0) return;

        var msg = $"{files.Count} oyunun kodu kaydedildi:\n\n" + string.Join("\n", files.Select(Path.GetFileName)) +
                  $"\n\nKlasör: {CodeBackup.Folder}\n\nGerektiğinde 'Koddan yükle' ile geri getirebilirsiniz.";
        if (await MainWindow.Instance.ShowDialogAsync("Kod kaydedildi", new TextBlock
            {
                Text = msg, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Foreground = (Brush)FindResource("SubTextBrush"),
            }, "Klasörü aç", "Kapat"))
            AppState.OpenInExplorer(files[0]);
    }

    private async void ImportCode_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null) { await MainWindow.Instance.InfoAsync("Koddan yükle", "Önce installations.jsonl açılmalı."); return; }

        var dlg = new OpenFileDialog
        {
            Title = "Oyun kod yedeğini seç",
            Filter = "Kod yedeği (*.jsonl)|*.jsonl|Tüm dosyalar|*.*",
            Multiselect = true,
        };
        if (Directory.Exists(CodeBackup.Folder)) dlg.InitialDirectory = CodeBackup.Folder;
        if (dlg.ShowDialog() != true) return;

        var incoming = new List<JournalLine>();
        foreach (var f in dlg.FileNames)
        {
            try { incoming.AddRange(CodeBackup.ReadLatest(f)); }
            catch (Exception ex) { AppState.AddLog($"{Path.GetFileName(f)} okunamadı: {ex.Message}", LogKind.Error); }
        }
        // Ayni kurulum birden fazla dosyada varsa son secilen gecerli
        incoming = incoming.GroupBy(l => l.Id).Select(g => g.Last()).ToList();
        if (incoming.Count == 0) { await MainWindow.Instance.InfoAsync("Koddan yükle", "Seçilen dosyalarda geri yüklenecek kurulum satırı yok."); return; }

        // Ayni oyunun (itemId) baska bir installationId ile etkin kaydi varsa o kayit kaldirilacak
        var replaced = new Dictionary<string, List<InstallRecord>>();
        var body = new StackPanel();
        foreach (var line in incoming)
        {
            var id = line.Id!;
            var dir = line.Str("dir") ?? "";
            var itemId = line.Str("itemId") ?? "";
            var parent = line.Str("parentInstallationId") ?? "";
            var others = _records.Where(r => !r.Deleted && r.Id != id && r.ItemId == itemId && r.ParentId == parent && itemId.Length > 0).ToList();
            if (others.Count > 0) replaced[id] = others;
            var existing = _records.FirstOrDefault(r => r.Id == id);

            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var name = Path.GetFileName(dir.TrimEnd('\\'));
            if (parent.Length > 0) name += "  (alt paket)";
            row.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold });
            row.Children.Add(Hint(dir, Directory.Exists(dir) ? "SubTextBrush" : "DangerBrush"));
            if (!Directory.Exists(dir)) row.Children.Add(Hint("⚠ Klasör bu bilgisayarda yok; launcher kaydı yeniden silebilir", "DangerBrush"));
            if (existing != null && !existing.Deleted) row.Children.Add(Hint("Mevcut kaydın yerine yedekteki konacak", "WarningBrush"));
            else if (existing != null) row.Children.Add(Hint("Silinmiş kayıt yedekten geri gelecek", "SuccessBrush"));
            else row.Children.Add(Hint("Yeni kayıt olarak eklenecek", "SuccessBrush"));
            if (others.Count > 0)
                row.Children.Add(Hint($"Aynı oyunun şu anki kaydı ({string.Join(", ", others.Select(o => o.Id[..8]))}) kaldırılacak", "WarningBrush"));
            body.Children.Add(row);
        }

        if (!await MainWindow.Instance.ShowDialogAsync($"{incoming.Count} kurulum yedekten yüklensin mi?", body, "Yükle")) return;

        foreach (var line in incoming)
        {
            if (replaced.TryGetValue(line.Id!, out var others))
                foreach (var o in others) _journal.Remove(o.Id);
            _journal.Append(line);
        }
        AppState.AddLog($"{incoming.Count} kurulum koddan yüklendi. Kaydetmeyi unutmayın.", LogKind.Success);
        Rebuild();
    }

    private TextBlock Hint(string text, string brush) => new()
    {
        Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        Foreground = (Brush)FindResource(brush),
    };

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (List.SelectedItem is InstallRecord r) AppState.OpenInExplorer(r.Dir);
        else if (_journal != null) AppState.OpenInExplorer(_journal.FilePath);
    }

    // ---- Toplu araclar ----

    private async void ReplacePath_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null) return;
        var editor = new PathReplaceEditor(_journal, CommonParent(_records.Select(r => r.Dir)));
        if (!await MainWindow.Instance.ShowDialogAsync("Yol değiştir", editor, "Uygula")) return;
        var count = _journal.ReplacePrefix(editor.OldPrefix, editor.NewPrefix, apply: true);
        AppState.AddLog($"Yol değiştirildi: {editor.OldPrefix} → {editor.NewPrefix} ({count} kurulum). Kaydetmeyi unutmayın.", LogKind.Success);
        Rebuild();
    }

    private async void FindMissing_Click(object sender, RoutedEventArgs e)
    {
        if (_journal == null || _busy) return;
        var candidates = _records.Where(r => r.Dir.Length > 0 && !Directory.Exists(r.Dir) && _journal.LatestData(r.Id) != null)
                                 .GroupBy(r => r.Dir, StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count == 0) { await MainWindow.Instance.InfoAsync("Kayıp klasörler", "Klasörü bulunamayan kayıt yok."); return; }

        var found = new List<(string OldDir, string NewDir, List<InstallRecord> Records)>();
        var notFound = new List<string>();
        await RunBusyAsync("Klasörler aranıyor…", () =>
        {
            foreach (var g in candidates)
            {
                var hit = Journal.FindMovedFolder(g.Key);
                if (hit != null) found.Add((g.Key, hit, g.ToList()));
                else notFound.Add(g.Key);
            }
        }, rebuild: false);

        if (found.Count == 0)
        {
            await MainWindow.Instance.InfoAsync("Kayıp klasörler", "Yakın klasörlerde eşleşen klasör bulunamadı:\n\n" + string.Join("\n", notFound));
            return;
        }

        var body = new StackPanel();
        foreach (var (oldDir, newDir, recs) in found)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(new TextBlock { Text = string.Join(", ", recs.Select(r => r.IsChild ? r.SubName : r.Name).Distinct()), FontWeight = FontWeights.SemiBold });
            row.Children.Add(new TextBlock { Text = "Eski: " + oldDir, Foreground = (Brush)FindResource("DangerBrush"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = "Yeni: " + newDir, Foreground = (Brush)FindResource("SuccessBrush"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(row);
        }
        if (notFound.Count > 0)
            body.Children.Add(new TextBlock { Text = "Bulunamayan: " + string.Join(", ", notFound.Select(Path.GetFileName)), Style = (Style)FindResource("Hint"), Margin = new Thickness(0, 0, 0, 10) });
        var restoreBox = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Silinmiş olanları da geri yükle", IsChecked = true, Margin = new Thickness(0, 6, 0, 0) };
        body.Children.Add(restoreBox);

        if (!await MainWindow.Instance.ShowDialogAsync($"{found.Count} klasör bulundu", body, "Yolları düzelt")) return;

        foreach (var (_, newDir, recs) in found)
        {
            foreach (var r in recs)
            {
                _journal.Update(r.Id, newDir, r.Exe, r.Complete);
                // Ayni oyun baska kayitla kuruluysa (orn. yeniden kurulan Fortnite) eski kaydi geri getirme
                if (restoreBox.IsChecked == true && r.CanRestore) _journal.Restore(r.Id);
            }
        }
        AppState.AddLog($"{found.Sum(f => f.Records.Count)} kaydın yolu düzeltildi. Kaydetmeyi unutmayın.", LogKind.Success);
        Rebuild();
    }

    private static string CommonParent(IEnumerable<string> dirs)
    {
        var parents = dirs.Where(d => d.Length > 0).Select(d => Path.GetDirectoryName(d.TrimEnd('\\')) ?? "")
                          .Where(p => p.Length > 0).ToList();
        if (parents.Count == 0) return "";
        return parents.GroupBy(p => p, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).First().Key;
    }

    private async Task RunBusyAsync(string text, Action work, bool rebuild = false)
    {
        _busy = true;
        BusyText.Text = text;
        BusyPanel.Visibility = Visibility.Visible;
        try
        {
            await Task.Run(work);
            if (rebuild) Rebuild();
        }
        catch (Exception ex)
        {
            AppState.AddLog(ex.Message, LogKind.Error);
            await MainWindow.Instance.InfoAsync("Hata", ex.Message);
        }
        finally
        {
            BusyPanel.Visibility = Visibility.Collapsed;
            _busy = false;
        }
    }
}
