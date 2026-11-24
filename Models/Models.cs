using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace MetroKafeEpicGames.Models;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

/// <summary>installations.jsonl icindeki tek satir. Degismeyen satir dosyaya oldugu gibi geri yazilir.</summary>
public class JournalLine
{
    public string Raw { get; set; } = "";
    public JsonObject? Obj { get; set; }

    public string? Id => Str("installationId");
    public bool IsDeleted => Obj?["deleted"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public string? Str(string key) => Obj?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

public enum RecordState { Installed, Incomplete, Deleted, FolderMissing, ExeMissing, Duplicate, Unknown }

/// <summary>Bir kurulumun gunlugun sonundaki hali (satirlar sirayla uygulanarak bulunur).</summary>
public class InstallRecord : Observable
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string SubName { get; init; } = "";
    public string Dir { get; init; } = "";
    public string Version { get; init; } = "";
    public string Exe { get; init; } = "";
    public string Namespace { get; init; } = "";
    public string ArtifactId { get; init; } = "";
    /// <summary>Oyunun katalog kimligi; ayni oyun yeniden kurulunca installationId degisir ama bu ayni kalir.</summary>
    public string ItemId { get; init; } = "";
    public string ParentId { get; init; } = "";
    public bool Complete { get; init; }
    public bool Deleted { get; init; }
    public bool IsChild { get; init; }
    public bool CanRestore { get; set; }
    public string LastWritten { get; init; } = "";
    public int LineCount { get; init; }
    public RecordState State { get; set; }
    public string Detail { get; set; } = "";

    public string StateText => State switch
    {
        RecordState.Installed => "Kurulu",
        RecordState.Incomplete => "Yarım",
        RecordState.Deleted => "Silinmiş",
        RecordState.FolderMissing => "Klasör yok",
        RecordState.ExeMissing => "Exe yok",
        RecordState.Duplicate => "Çift kayıt",
        _ => "Bilinmiyor",
    };

    public bool IsProblem => State is RecordState.FolderMissing or RecordState.ExeMissing or RecordState.Duplicate or RecordState.Unknown
                             || (Deleted && CanRestore);
}
