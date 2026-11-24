# MetroKafe Epic Games

Yeni Epic Games Launcher'ın (eglv2) kurulu oyun kayıtlarını tuttuğu `installations.jsonl` dosyasını
görüntüleyen ve düzelten Windows uygulaması.

## Neden gerekli?

Yeni launcher kurulu oyunları `%LOCALAPPDATA%\com.epicgames.eglv2\installations.jsonl` dosyasında tutar.
Her değişiklik dosyanın sonuna yeni bir satır olarak eklenir; bir oyunun son satırı geçerlidir.
Oyun klasörü taşınır ve launcher açılışta oyunu kayıttaki yolda bulamazsa o oyun için
`{"installationId": "...", "deleted": true}` satırı yazar ve oyun listeden kaybolur.
Bu program kayıtları gösterir, yolları düzeltir ve silinen kayıtları geri getirir.

## Özellikler

- **Kurulumlar**: Her oyunun son durumu (Kurulu, Yarım, Silinmiş, Klasör yok, Exe yok), klasörü, sürümü,
  son kayıt zamanı. Fortnite gibi oyunların alt paketleri ayrıca gösterilir.
- **Kayıp klasörleri bul**: Klasörü bulunamayan oyunları yakın klasörlerde arar (örn. taşıma sırasında oluşan
  `Epic Games\Epic Games\Fortnite`), yolları düzeltir ve istenirse silinmiş kayıtları geri yükler.
- **Yol değiştir**: Bütün kayıtlarda yolun başını topluca değiştirir.
- **Geri yükle**: Silinmiş kurulumun silme satırlarını kaldırır.
- **Düzenle / JSON**: Klasör, başlatma dosyası ve "tamamlandı" bilgisini ya da son satırın JSON'unu düzenler.
- **Kaydı sil**: Kurulumun bütün satırlarını dosyadan çıkarır (oyun dosyalarına dokunmaz).
- **Güvenli kaydetme**: Her kayıtta `installations.jsonl.yedek_tarih` yedeği alınır, açık olan launcher
  kapatılır, dosya diskte değişmişse uyarılır. Değişmeyen satırlar dosyaya birebir geri yazılır.
- **Güncelleme**: GitHub Releases'tan yeni sürümü indirip kendini günceller.

## Kullanım

1. `MetroKafeEpicGames.exe`'yi oyunları kullanan Windows kullanıcısıyla çalıştırın (yönetici gerekmez).
2. Dosya otomatik bulunur; bulunamazsa **Dosya aç** ile seçin ya da Ayarlar'a yol yazın.
3. Değişiklikleri yapın, **Kaydet**'e basın (Ctrl+S).
4. Launcher'ı açın.

Klasör yolu yanlışken silinen kaydı geri yüklerseniz launcher açılışta yeniden siler; önce yolu düzeltin.

## Yeni sürüm yayınlamak

```
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
gh release create v1.0.1 publish/MetroKafeEpicGames.exe --title "v1.0.1" --notes "..."
```

`MetroKafeEpicGames.csproj` içindeki `<Version>` etiketle aynı olmalıdır.
