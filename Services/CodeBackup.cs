using System.Text;
using System.Text.Json.Nodes;
using MetroKafeEpicGames.Models;

namespace MetroKafeEpicGames.Services;

/// <summary>
/// Oyun bazinda kod yedegi. Exe'nin yanindaki "Kod Yedekleri" klasorune oyun_tarih.jsonl olarak yazilir:
/// ilk satir aciklama ({"metroKafeYedek":1, ...}), sonraki satirlar installations.jsonl'daki satirlarin birebir kopyasi.
/// </summary>
public static class CodeBackup
{
    public static string Folder => Path.Combine(AppState.ExeFolder, "Kod Yedekleri");

    public static string Save(Journal journal, InstallRecord r)
    {
        var lines = journal.LinesWithChildren(r.Id);
        if (lines.Count == 0) throw new InvalidOperationException("Bu kaydın satırı yok.");

        Directory.CreateDirectory(Folder);
        var name = r.IsChild ? $"{r.Name}_{r.SubName.Replace("Alt paket: ", "")}" : r.Name;
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));
        var file = Path.Combine(Folder, $"{safe}_{DateTime.Now:yyyy-MM-dd_HHmmss}.jsonl");

        var meta = new JsonObject
        {
            ["metroKafeYedek"] = 1,
            ["oyun"] = r.Name,
            ["altPaket"] = r.SubName,
            ["id"] = r.Id,
            ["dir"] = r.Dir,
            ["surum"] = r.Version,
            ["durum"] = r.StateText,
            ["satir"] = lines.Count,
            ["kaydedildi"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["kaynak"] = journal.FilePath,
            ["program"] = "MetroKafeEpicGames " + AppState.VersionText,
        };

        var sb = new StringBuilder();
        sb.Append(meta.ToJsonString(Journal.JsonOpts)).Append('\n');
        foreach (var line in lines) sb.Append(line.Raw).Append('\n');
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        return file;
    }

    /// <summary>Yedek dosyasindaki her kurulumun son veri satiri (geri yuklenecek olan).</summary>
    public static List<JournalLine> ReadLatest(string file)
    {
        var latest = new Dictionary<string, JournalLine>();
        var order = new List<string>();
        foreach (var raw in File.ReadAllLines(file, Encoding.UTF8))
        {
            if (raw.Trim().Length == 0) continue;
            JsonObject? obj;
            try { obj = JsonNode.Parse(raw) as JsonObject; }
            catch { continue; }
            if (obj == null || obj.ContainsKey("metroKafeYedek")) continue;

            var line = new JournalLine { Raw = raw.TrimEnd('\r'), Obj = obj };
            var id = line.Id;
            if (id == null || line.IsDeleted) continue;
            if (!latest.ContainsKey(id)) order.Add(id);
            latest[id] = line;
        }
        return order.Select(id => latest[id]).ToList();
    }
}
