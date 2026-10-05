# fm-cli

Gerçek **Football Manager 26**'yı terminalden oynat.

```
$ fm inbox
SAAT   GÖNDEREN          KONU
08:05  Sadettin Saran    Welcome to Fenerbahçe!                      okunmadı
08:13  Zeki Murat Göle   Fenerbahçe Tactics Introduction             okunmadı, cevap bekliyor
...
5 mesaj cevap bekliyor — bunlar `fm next`'i blokluyor.

$ fm squad
OYUNCU              POZİSYON          YAŞ  TAKIM
Marco Asensio       M (C), AM (RLC)   29   Fenerbahçe
N'Golo Kanté        DM, M (C)         34   Fenerbahçe
...
```

Oyunu sıfırdan yazmıyoruz — gerçek FM26'nın içine bir BepInEx plugin'i enjekte edip
oyunun kendi veri katmanını (`SI.Bindable.Bindings`) ve olay sistemini CLI'a açıyoruz.
Oyun arka planda açık kalır; ekran, fare, ekran görüntüsü yok.

**Durum:** kadro (kondisyon ve moral dahil), gelen kutusu, mesaj cevaplama ve zaman ilerletme
çalışıyor. Ekrandaki her buton ve açılır liste plugin üzerinden listelenip kullanılabiliyor.

📄 Bütün teknik bulgular, API'ler ve kısıtlar: **[docs/FINDINGS.md](docs/FINDINGS.md)**
📋 Yol haritası: **[PLAN.md](PLAN.md)**

## Yapı

```
plugin/FmCliProbe/        FM26 içinde çalışan plugin (C#, BepInEx 6 IL2CPP)
cli/fm/                   `fm` komutu (.NET)
tools/sigdump/            interop'un gerçekten neyi açtığını raporlayan araç
docs/reference/           oyundan çıkarılmış veri modeli ve event kataloğu
```

## Komutlar

```
fm start        oyunu başlat, kaydı yükle, oturuma gir
  --debug       pencereyi gizleme
fm              oturuma gir (oyun açıkken)

continue        açık oyunda son kaydı yükle
squad           kadroyu bas
  --fitness     kondisyon ve maç formu, en yorgun önce
  --morale      moral, hiyerarşi ve sosyal grup
inbox           gelen kutusu
next            zamanı ilerlet (bloke eden mesajları bildirir)
buttons         ekrandaki butonlar ve ne gönderdikleri
press "<etiket>"            butona bas
dropdowns       ekrandaki açılır listeler ve seçenekleri
select <liste> "<seçenek>"  listeden seç
  --dry         press ve select için: yapma, ne yapacağını söyle
status          bağlantı durumu
--json          ham veri (script'lenebilir)
```

Oturumda komutlar `fm` öneki olmadan verilir:

```
$ fm start
FM başlatılıyor…
Son kayıt yükleniyor…
Hazır.
fm› next
Bloke: 5 mesaj cevap bekliyor.
  08:13  Club vision and expectations meeting
  ...
```

`fm start` oyun zaten açıksa ona bağlanır; kayıt yüklü değilse yükler.

Oyun **Dock'ta ve ekranda görünmez**: bundle accessory olarak işaretlenir, pencereli
başlatılır ve küçültülür. Veri bu haldeyken de akar. `--debug` üçünü de atlar.
Gerçekten headless (`-nographics`) çalışmıyor — UI olmadan binding ağacı kurulmuyor
(`docs/FINDINGS.md` §8.19). Küçültme Erişilebilirlik izni ister; verilmezse `fm` bunu söyler.

## Gereksinimler

- macOS, Apple Silicon
- Football Manager 26 (Steam)
- BepInEx 6 IL2CPP arm64 — kurulum `docs/FINDINGS.md` §3'te
- .NET SDK

## Derle ve kur

```bash
# plugin
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll \
   "$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26/BepInEx/plugins/FmCliProbe/"

# cli
dotnet build cli/fm -c Release
ln -sf "$PWD/cli/fm/bin/Release/net10.0/fm" /usr/local/bin/fm
```

## Referans dosyaları

`docs/reference/` oyundan çıkarılmış, elle yazılmamış iki katalog içeriyor:

| Dosya | İçerik |
|---|---|
| `COOKBOOK.md` | **buradan başla** — taktik, transfer, basın, konuşma, antrenman reçeteleri |
| `data-model.txt` | 931 referans tipi, 14504 isimli property, referans kenarlarıyla |
| `events.txt` | 275 UI event tipi, alanlarıyla |
| `actions.txt` | 225 `ActionID` + 680 `InteropEventIDs` — simülasyonun aksiyon yüzeyi |
| `enums.txt` | 2160 enum, 19119 üye — roller, direktifler, mentalite, formasyonlar |
| `panels.txt` | yüklü ekranlar, çevrilmiş isimleriyle (kısmi) |

Yenilemek için: oyun açıkken `curl localhost:7777/schema` ve `/panels`; oyun gerekmeden
`dotnet run --project tools/sigdump -- --catalog events` / `--enums` / `--method <ad>`.
