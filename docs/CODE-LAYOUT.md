# Kod düzeni

## Katmanlar

| Katman | Yer | İş |
|---|---|---|
| CLI girişi | `cli/fm/Program.cs` | `main`, komut yönlendirme, oturum, sürüm kontrolü |
| CLI komutları | `cli/fm/{Core,Match,Squad,Transfer,Club}/` | bir komut ya da okuma; hepsi `Program` partial'ı |
| Oyun köprüsü | `cli/fm/Core/Bridge.cs` | HTTP, port, hata ve zaman aşımı |
| Oyun okuma | `cli/fm/Core/Reads.cs`, `Core/Clock.cs`, `Match/Guided.cs`, `Match/MatchDay.cs` | veri yolu, sayfa, maç akışı |
| Biçim | `cli/fm/Core/Table.cs`, `Ui.cs`, `MoneyFormat.cs`, `JsonOut.cs`, `InboxView.cs` | tablo, terminal, para/sayı |
| Metin | `cli/fm/Core/Labels.cs`, `GameWords.cs`, `EngineTexts.cs` | oyunun dili ve CLI sözlüğü |
| Altyapı | `cli/fm/Core/Launcher.cs`, `Session.cs`, `Screens.cs`, `GameFolders.cs`, `InstallDoctor.cs` | başlatma, klasör, kurulum |
| Plugin | `plugin/FmCliProbe/src/` | IL2CPP tarafı: buton, metin, sayfa, kayıt |
| Araçlar | `tools/` | paket, yayın, test, ekran görüntüsü |
| Doküman | `docs/` | `FINDINGS` teknik arşiv, `KILAVUZ` kullanıcı kılavuzu, `PLAYTEST` kontrol listesi |

## Klasörler

- `Core/` — köprü, okuma, zaman, biçim, etiket, başlatma, oturum, MCP.
- `Match/` — maç günü, anlatım, takvim, fikstür, lig tablosu, rakip raporu.
- `Squad/` — kadro, sağlık, moral, sözleşme, taktik, antrenman, duran top, kayıt.
- `Transfer/` — teklifler, sözleşme görüşmeleri, scout, kısa liste, transfer hedefleri.
- `Club/` — kulüp, yönetim, finans, personel, tesis, kariyer, iş, istifa.
- `Program/` — `Program` partial’ları (giriş dışındaki büyük komut blokları).

## `Program` partial'ları

`Program.cs` yalnız giriş ve yönlendirme taşır. Büyük bloklar sorumluluklarına göre ayrıldı:

- `Program/Program.Squad.cs` — kadro okuma ve görünümleri.
- `Program/Program.Inbox.cs` — gelen kutusu, cevap, mesaj seçimi.
- `Program/Program.Messages.cs` — mesaj gövdesi, ekler, sözleşme/teklif kartı.
- `Program/Program.Ui.cs` — buton, sayfa, dropdown, terminal yardımcıları.

Büyük okuma/akış dosyaları da sorumluluklarına göre bölündü:

- `Match/Guided.cs` — tur ve konuşma; `Guided.SetPieces.cs` — duran top sihirbazı;
  `Guided.Tactic.cs` — taktik planlayıcı.
- `Core/Reads.cs` — arama/oyuncu; `Reads.Squad.cs` — kadro görünümleri; `Reads.League.cs` — lig,
  fikstür ve ilerleme.
- `Match/MatchDay.cs` — maçın ana döngüsü; `MatchDay.Instant.cs` — anında sonuç;
  `MatchDay.Team.cs` — kadro/rakip adları; `MatchDay.Substitute.cs` — değişiklikler;
  `MatchDay.Dialog.cs` — onay pencereleri.

Yeni bir komut eklerken aynı `Program` partial'ına yeni bir dosya aç; `Program.cs`i büyütme.
Komutun okuma kısmı ile yazma kısmını aynı dosyada tut; ortak biçimleyiciyi `Format/` altına koyacaksan
önce mevcut `Table`/`Ui` yardımcılarına bak.

## Plugin

- `FmApi.cs` — HTTP yolları ve yönlendirme.
- `Buttons.cs` — kontrol kataloğu ve olay kuyruğu.
- `Text.cs`, `Translations.cs`, `EngineTexts.cs` — oyun metni ve çeviri.
- `Pages.cs`, `State.cs`, `Tactics.cs`, `Registration.cs`, `Offer.cs` — ekran okuma.
- `HttpBridge.cs` — ana iş parçacığına iş atma ve yanıt.

Yeni bir okuma: veri yolunu `KeyPaths`/`Path` ile al; ekran yazısını veya sıra numarasını ölçüt yapma.
Yeni bir yazma: oyunun kendi olayını gönder; olay kuyruğunun boşalmasını bekle.

## Kurallar

- **Dil ve ekran bağımsız:** veri yolu, olay adı, GUID, kimlik. `press "Confirm"`, `up=N`, sıra numarası yok.
- **Sabit bekleme yok:** oyunun “oldu” işaretini bekle; süre yalnız üst sınır.
- **Tek ekran bağlı:** komut kendi ekranını açar; okuma sonrası başka ekranın verisini varsayma.
- **Hata sessiz kalmasın:** `rc` ve kısa neden; boş tablo ile “veri yok” ayrılsın.
- **Küçük değişiklik:** bir davranışı düzeltirken aynı dosyada ilgisiz refactor yapma.

## Derleme ve kontrol

```bash
dotnet build cli/fm/fm.csproj -c Release
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release
tools/install.sh          # plugin + fm + doctor
fm doctor --install
fm status                 # köprü ve kayıt kontrolü
```

Test kontrol listesi: `docs/PLAYTEST.md`. Teknik bulgular: `docs/FINDINGS.md`.
