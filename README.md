# fm-cli

Gerçek **Football Manager 26**'yı terminalden oynat.

```
$ fm inbox
#  SAAT   GÖNDEREN          KONU
1  08:05  Sadettin Saran    Welcome to Fenerbahçe!                      okunmadı
4  08:13  Sadettin Saran    Club vision and expectations meeting        okunmadı, cevap bekliyor
...
4 mesaj cevap bekliyor — bunlar `fm next`'i blokluyor. Cevaplamak için: reply <#>

$ fm reply 4
  1  Negotiate
  2  Accept Current Vision
  3  View Club Vision

Seçim (1-3, x: vazgeç): 2
Cevaplandı: Club vision and expectations meeting → Accept Current Vision

$ fm squad
OYUNCU          POZİSYON         YAŞ  STATÜ         MAAŞ         DEĞER          DURUM
Anthony Musaba  AM (RL), ST (C)  24   Squad Player  £14.25K p/w  £2M - £4.9M
Ederson         GK               31   Star Player   £230K p/w    £56M - £62M
...

$ fm squad --fitness
OYUNCU             POZİSYON         KONDİSYON  MAÇ FORMU  DURUM
Talisca            AM (RC), ST (C)  -          35%        Inj
Dominik Livaković  GK               98%        82%
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
player <isim>   oyuncu sayfası: kimlik, sözleşme, alışkanlıklar, özellikler
table           lig tablosu
fixtures        sıradaki maçlar
inbox           gelen kutusu
reply <#>       mesajın cevaplarını göster ve seçimi sor (x: vazgeç)
reply <#> <no>  mesajı cevapla (numara ya da cevabın bir parçası)
talk            açık konuşma (toplantı, basın toplantısı, takım konuşması): ne dendi, seçenekler
talk <no>       söyle;  talk leave: çık
tour            açık tanıtım turunu yürüt;  --skip: atla
next [n]        zamanı ilerlet, oyunun durmasını bekle, ne olduğunu söyle
match           maç gününü oyna: kadroyu gönder, maçı izle, maçtan çık;  --talk: konuşmalarda konuş
  --instant     maçı oynatma, yardımcılara bırak (birkaç saniye)
sub             maç sırasında sahadakiler ve kulübe;  sub <çıkan> <giren>: oyuncu değiştir
dialog [cevap]  açık onay penceresini göster ya da cevapla
status          tarih, saat ve oyunun neyi beklediği
save --yes      oyunu kaydet (dosyanın üzerine yazar)

tactic                      taktik: stil, mentalite, on bir mevki, roller, oyuncular
tactic new "<stil>" "<topla diziliş>" "<topsuz diziliş>"   taktik oluştur (eksik adımı listeler)
tactic mentality [<x>]      mentaliteyi göster / değiştir;  tactic style [<x>] aynısı stil için
tactic pick                 ilk 11'i oyuna seçtir
setpieces new [<no> …]      duran top rutinlerini kur (altı soru)

pages           oyunun bütün sayfaları (77)
open "<sayfa>"  sayfayı aç
  --tab <sekme> aynı adda birden çok sayfa varsa
buttons         ekrandaki butonlar ve ne gönderdikleri
press "<etiket>"            butona bas
dropdowns       ekrandaki açılır listeler ve seçenekleri
select <liste> "<seçenek>"  listeden seç
  --dry         değiştiren komutlarda: yapma, ne yapacağını söyle
--json          ham veri (script'lenebilir)
fm mcp          komutları MCP araçları olarak sun (stdio); repo kökündeki .mcp.json bunu tanımlıyor
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
