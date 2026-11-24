using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MetroKafeEpicGames.Services;

public record ReleaseInfo(string Tag, Version Version, string Title, string Notes, DateTime? Published,
                          string? AssetUrl, string? AssetName, long AssetSize)
{
    public bool IsNewer => Version > AppState.Version;
}

/// <summary>GitHub Releases uzerinden surum kontrolu ve kendini guncelleme (acik depo, token gerekmez).</summary>
public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MetroKafeEpicGames/" + AppState.VersionText);
        return http;
    }

    public static async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct = default)
    {
        var repo = AppState.Settings.GitHubRepo.Trim().Trim('/');
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var res = await Http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Sürüm bulunamadı. Ayarlar'daki depo adını kontrol edin; depoda henüz yayın olmayabilir.");
        if (res.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("GitHub istek sınırına takıldı. Biraz sonra tekrar deneyin.");
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"GitHub yanıtı: {(int)res.StatusCode} {res.ReasonPhrase}");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) version = new Version(0, 0, 0);

        string? assetUrl = null, assetName = null;
        long assetSize = 0;
        if (r.TryGetProperty("assets", out var assets))
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                assetUrl = a.GetProperty("browser_download_url").GetString();
                assetName = name;
                assetSize = a.GetProperty("size").GetInt64();
                break;
            }
        }

        DateTime? published = r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetDateTime().ToLocalTime() : null;
        var title = r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? tag : tag;
        var notes = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";

        return new ReleaseInfo(tag, version, title, notes, published, assetUrl, assetName, assetSize);
    }

    public static async Task<string> DownloadAsync(ReleaseInfo info, IProgress<double> progress, CancellationToken ct = default)
    {
        if (info.AssetUrl == null) throw new InvalidOperationException("Bu sürümde indirilecek .exe dosyası yok.");

        using var res = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();

        var path = Path.Combine(Path.GetTempPath(), $"MetroKafeEpicGames_{info.Tag}.exe");
        var total = res.Content.Headers.ContentLength ?? info.AssetSize;
        await using (var src = await res.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(path))
        {
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, count), ct);
                read += count;
                if (total > 0) progress.Report((double)read / total);
            }
        }
        return path;
    }

    /// <summary>Uygulama kapaninca yeni exe'yi eskisinin yerine koyup yeniden baslatir.</summary>
    public static void InstallAndRestart(string newExe)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
        var pid = Environment.ProcessId;
        var script = Path.Combine(Path.GetTempPath(), "MetroKafeEpicGames_guncelle.cmd");

        var lines = new[]
        {
            "@echo off",
            ":bekle",
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto bekle)",
            $"move /y \"{newExe}\" \"{exe}\" >nul",
            $"start \"\" \"{exe}\"",
            "del \"%~f0\"",
        };
        Encoding oem;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { oem = Encoding.ASCII; }
        File.WriteAllText(script, string.Join("\r\n", lines) + "\r\n", oem);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
        MainWindow.Instance.ForceClose = true;
        Application.Current.Shutdown();
    }
}
