using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MetroKafeEpicGames.Models;

namespace MetroKafeEpicGames.Services;

/// <summary>
/// Epic Games Launcher v2'nin installations.jsonl gunlugu.
/// Ilk satir baslik ({"version","writtenAt","entries"}), sonraki her satir bir kurulumun o anki hali ya da
/// {"installationId", "deleted":true} silme kaydi. Launcher satirlari sirayla uygular; son satir gecerlidir.
/// </summary>
public class Journal
{
    public static readonly JsonSerializerOptions JsonOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions PrettyOpts = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };

    public string FilePath { get; }
    public JsonObject? Header { get; private set; }
    public string HeaderRaw { get; private set; } = "";
    public List<JournalLine> Lines { get; } = new();
    public int BrokenLines { get; private set; }
    public DateTime LoadedWriteTimeUtc { get; private set; }
    public bool Dirty { get; private set; }

    private Journal(string path) => FilePath = path;

    public static Journal Load(string path)
    {
        var j = new Journal(path);
        var text = File.ReadAllText(path, Encoding.UTF8);
        var raw = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();

        var start = 0;
        if (raw.Count > 0 && TryParse(raw[0]) is { } first && first["installationId"] == null && first["entries"] != null)
        {
            j.Header = first;
            j.HeaderRaw = raw[0];
            start = 1;
        }

        for (var i = start; i < raw.Count; i++)
        {
            var obj = TryParse(raw[i]);
            if (obj == null) j.BrokenLines++;
            j.Lines.Add(new JournalLine { Raw = raw[i], Obj = obj });
        }
        j.LoadedWriteTimeUtc = File.GetLastWriteTimeUtc(path);
        return j;
    }

    private static JsonObject? TryParse(string line)
    {
        try { return JsonNode.Parse(line) as JsonObject; }
        catch { return null; }
    }

    public bool ChangedOnDisk => File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) != LoadedWriteTimeUtc;

    // ---- Okuma ----

    public List<InstallRecord> BuildRecords()
    {
        var order = new List<string>();
        var latest = new Dictionary<string, JournalLine>();
        var deleted = new Dictionary<string, JournalLine>();
        var counts = new Dictionary<string, int>();

        foreach (var line in Lines)
        {
            var id = line.Id;
            if (id == null) continue;
            if (!counts.ContainsKey(id)) { order.Add(id); counts[id] = 0; }
            counts[id]++;
            if (line.IsDeleted) deleted[id] = line;
            else { latest[id] = line; deleted.Remove(id); }
        }

        var names = latest.ToDictionary(kv => kv.Key, kv => FolderName(kv.Value.Str("dir")));
        var result = new List<InstallRecord>();
        foreach (var id in order)
        {
            latest.TryGetValue(id, out var data);
            var isDeleted = deleted.TryGetValue(id, out var del);
            result.Add(Describe(id, data, isDeleted ? del : null, counts[id], names));
        }
        return result;
    }

    private static InstallRecord Describe(string id, JournalLine? data, JournalLine? deletedLine, int count, Dictionary<string, string> names)
    {
        if (data?.Obj == null)
        {
            return new InstallRecord
            {
                Id = id, Name = "(bilinmeyen kurulum)", Deleted = deletedLine != null, LineCount = count,
                LastWritten = FormatTime(deletedLine?.Str("writtenAt")), State = deletedLine != null ? RecordState.Deleted : RecordState.Unknown,
                Detail = "Günlükte bu kurulumun bilgisi yok; geri yüklenemez",
            };
        }

        var o = data.Obj;
        var dir = data.Str("dir") ?? "";
        var exe = data.Str("launchExecutable") ?? "";
        var complete = o["complete"] is JsonValue cv && cv.TryGetValue<bool>(out var c) && c;
        var version = data.Str("version") ?? data.Str("pendingVersion") ?? "";
        var parent = data.Str("parentInstallationId");
        var artifact = data.Str("artifactId") ?? "";
        var tag = (o["tags"] as JsonArray)?.Select(t => t?.GetValue<string>() ?? "").FirstOrDefault(t => t.Length > 0) ?? "";

        var folderExists = dir.Length > 0 && Directory.Exists(dir);
        var exePath = exe.Length > 0 && dir.Length > 0 ? Path.Combine(dir, exe.Replace('/', '\\')) : "";
        var exeExists = exePath.Length == 0 || File.Exists(exePath);

        RecordState state;
        string detail;
        if (deletedLine != null)
        {
            state = RecordState.Deleted;
            detail = folderExists
                ? $"{FormatTime(deletedLine.Str("writtenAt"))} silinmiş · klasör diskte var, geri yüklenebilir"
                : $"{FormatTime(deletedLine.Str("writtenAt"))} silinmiş · klasör diskte yok";
        }
        else if (!folderExists) { state = RecordState.FolderMissing; detail = "Klasör bulunamadı; launcher açılınca kaydı silebilir"; }
        else if (!complete) { state = RecordState.Incomplete; detail = "Kurulum tamamlanmamış (complete: false)"; }
        else if (!exeExists) { state = RecordState.ExeMissing; detail = "Başlatma dosyası yok: " + exe; }
        else { state = RecordState.Installed; detail = exe.Length > 0 ? exe : "Alt paket"; }

        var name = FolderName(dir);
        var sub = "";
        if (parent != null)
        {
            sub = "Alt paket: " + (tag.Length > 0 ? tag : artifact);
            if (names.TryGetValue(parent, out var parentName) && parentName.Length > 0) name = parentName;
        }

        return new InstallRecord
        {
            Id = id, Name = name.Length > 0 ? name : artifact, SubName = sub, Dir = dir, Version = version, Exe = exe,
            Namespace = data.Str("namespace") ?? "", ArtifactId = artifact, Complete = complete,
            Deleted = deletedLine != null, IsChild = parent != null, CanRestore = deletedLine != null,
            LastWritten = FormatTime((deletedLine ?? data).Str("writtenAt")), LineCount = count, State = state, Detail = detail,
        };
    }

    private static string FolderName(string? dir) =>
        string.IsNullOrEmpty(dir) ? "" : Path.GetFileName(dir.TrimEnd('\\', '/'));

    public static string FormatTime(string? iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
            ? t.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "";

    public JournalLine? LatestData(string id) => Lines.LastOrDefault(l => l.Id == id && !l.IsDeleted && l.Obj != null);

    public string PrettyLatest(string id) => LatestData(id)?.Obj?.ToJsonString(PrettyOpts) ?? "";

    // ---- Degisiklikler ----

    /// <summary>Son veri satirindan sonra gelen silme kayitlarini kaldirir.</summary>
    public int Restore(string id)
    {
        var lastData = Lines.FindLastIndex(l => l.Id == id && !l.IsDeleted && l.Obj != null);
        if (lastData < 0) return 0;
        var removed = 0;
        for (var i = Lines.Count - 1; i > lastData; i--)
        {
            if (Lines[i].Id == id && Lines[i].IsDeleted) { Lines.RemoveAt(i); removed++; }
        }
        if (removed > 0) Dirty = true;
        return removed;
    }

    /// <summary>Kurulumun butun satirlarini siler.</summary>
    public int Remove(string id)
    {
        var removed = Lines.RemoveAll(l => l.Id == id);
        if (removed > 0) Dirty = true;
        return removed;
    }

    /// <summary>Klasor ve exe tum satirlarda, tamamlandi bilgisi son satirda guncellenir.</summary>
    public void Update(string id, string dir, string exe, bool complete)
    {
        foreach (var line in Lines.Where(l => l.Id == id && !l.IsDeleted && l.Obj != null))
        {
            var changed = false;
            if (line.Obj!.ContainsKey("dir") && line.Str("dir") != dir) { line.Obj["dir"] = dir; changed = true; }
            if (line.Obj.ContainsKey("launchExecutable") && line.Str("launchExecutable") != exe) { line.Obj["launchExecutable"] = exe; changed = true; }
            if (changed) Rewrite(line);
        }

        var last = LatestData(id);
        if (last?.Obj != null && !(last.Obj["complete"] is JsonValue v && v.TryGetValue<bool>(out var c) && c == complete))
        {
            last.Obj["complete"] = complete;
            Rewrite(last);
        }
    }

    /// <summary>Son satiri elle duzenlenmis JSON ile degistirir.</summary>
    public void ReplaceLatest(string id, JsonObject obj)
    {
        var last = LatestData(id) ?? throw new InvalidOperationException("Bu kurulumun düzenlenecek satırı yok.");
        last.Obj = obj;
        Rewrite(last);
    }

    /// <summary>Yol basini degistirir (orn. X:\Epic Games -> X:\Epic Games\Epic Games). Etkilenen kurulum sayisini dondurur.</summary>
    public int ReplacePrefix(string oldPrefix, string newPrefix, bool apply)
    {
        oldPrefix = oldPrefix.TrimEnd('\\');
        newPrefix = newPrefix.TrimEnd('\\');
        var ids = new HashSet<string>();
        foreach (var line in Lines.Where(l => l.Obj != null && !l.IsDeleted))
        {
            var dir = line.Str("dir");
            if (dir == null || !StartsWithPath(dir, oldPrefix)) continue;
            ids.Add(line.Id ?? "");
            if (!apply) continue;
            line.Obj!["dir"] = newPrefix + dir[oldPrefix.Length..];
            Rewrite(line);
        }
        return ids.Count;
    }

    public static bool StartsWithPath(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase);

    private void Rewrite(JournalLine line)
    {
        line.Raw = line.Obj!.ToJsonString(JsonOpts);
        Dirty = true;
    }

    // ---- Kaydetme ----

    /// <summary>Once yedek alir, sonra basliktaki satir sayisini guncelleyip yazar. Yedek dosyanin yolunu dondurur.</summary>
    public string Save()
    {
        var backup = $"{FilePath}.yedek_{DateTime.Now:yyyyMMdd_HHmmss}";
        if (File.Exists(FilePath)) File.Copy(FilePath, backup, true);

        var sb = new StringBuilder();
        if (Header != null)
        {
            Header["entries"] = Lines.Count;
            Header["writtenAt"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            HeaderRaw = Header.ToJsonString(JsonOpts);
            sb.Append(HeaderRaw).Append('\n');
        }
        foreach (var line in Lines) sb.Append(line.Raw).Append('\n');

        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, FilePath, true);

        LoadedWriteTimeUtc = File.GetLastWriteTimeUtc(FilePath);
        Dirty = false;
        return backup;
    }

    // ---- Kayip klasor bulma ----

    /// <summary>
    /// Klasoru bulunamayan kurulumlar icin ayni adli klasoru yakinda arar
    /// (orn. tasima sirasinda olusan "Epic Games\Epic Games\Fortnite" ic ice klasoru).
    /// </summary>
    public static string? FindMovedFolder(string dir)
    {
        var leaf = Path.GetFileName(dir.TrimEnd('\\'));
        var parent = Path.GetDirectoryName(dir.TrimEnd('\\'));
        if (string.IsNullOrEmpty(leaf) || string.IsNullOrEmpty(parent)) return null;

        // Yukari dogru en fazla 2 seviye cik, her seviyede 3 seviye derine bak
        var roots = new List<string>();
        for (var p = parent; p != null && roots.Count < 3; p = Path.GetDirectoryName(p))
            if (Directory.Exists(p)) roots.Add(p);

        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var root in roots)
        {
            var hits = Directory.EnumerateDirectories(root, leaf, options)
                                .Where(d => !d.Equals(dir, StringComparison.OrdinalIgnoreCase))
                                .Take(2).ToList();
            if (hits.Count == 1) return hits[0];
            if (hits.Count > 1) return null; // birden fazla aday: tahmin etme
        }
        return null;
    }
}
