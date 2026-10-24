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
| Plugin | `plugin/FmCliProbe/src/{Bridge,Controls,Reads}/` | IL2CPP tarafı: köprü, buton, metin, sayfa |
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

Ek bölünmeler:

- `Match/Guided.Tactic.cs` — ana taktik/oyuncu/planlayıcı; `Guided.TacticSettings.cs` — ayar, rol ve
  yuva; `Guided.TacticNew.cs` — yeni taktik, maç listesi ve yardımcılar.
- `Program/Messages.cs` — ek gövdeleri; `Messages.Attachments.cs` — ek kartları; `Messages.Cards.cs` —
  kaptanlık, maç raporu, sezon değerlendirmesi, rakip raporu, home-grown, antrenman, veri merkezi ve scout kartları; `Messages.Inbox.cs` — mesaj okuma
  ve numara hatırlama.
- `Transfer/ScoutFocus.cs` — form yardımcıları; `ScoutFocus.Team.cs` — ekip/atama; `ScoutFocus.Form.cs` —
  yeni odak formu ve kalan komutlar.
- `Squad/TrainingSchedule.cs` — değiştirme akışı; `TrainingSchedule.Catalogue.cs` — seans kataloğu ve
  “hakkında”; `TrainingSchedule.Calendar.cs` — takvim, ay ve grid okuma.
- `Core/Saves.cs` — kayıt listesi/tarayıcı; `Saves.SaveAs.cs` — yeni adla kaydetme;
  `Saves.Files.cs` — silme/yükleme.
- `Core/Clock.cs` — `next`/açıklama; `Clock.Status.cs` — kaydetme ve durum;
  `Clock.Fixtures.cs` — fikstür, form, sakatlar ve açık soru.
- `Program/Program.Ui.cs` — buton/sayfa komutları; `Program.Ui.Helpers.cs` — press/select,
  cevap yardımcıları ve alan okuma; `Program.Help.cs` — yardım metinleri.
- `Transfer/OfferScreen.cs` — model/okuma/değiştirme; `OfferScreen.Draw.cs` — kart çizimi;
  `OfferScreen.Labels.cs` — etiket/şart çevirileri.
- `Core/Reads.cs` — arama ve oyuncu açma; `Reads.Player.cs` — oyuncu kartı, geçmiş ve oturma.
- `Squad/TeamInstructions.cs` — takım talimatları; `TeamInstructions.Team.cs` — takım kapsamı/okuma;
  `TeamInstructions.Player.cs` — oyuncu talimatları.
- `Match/Stages.cs` — aşama komutu; `Stages.Lists.cs` — yarışma/aşama listeleri ve içerik;
  `Stages.Ties.cs` — eşleşmeler ve yarışma kataloğu.

Yeni bir komut eklerken aynı `Program` partial'ına yeni bir dosya aç; `Program.cs`i büyütme.
Komutun okuma kısmı ile yazma kısmını aynı dosyada tut; ortak biçimleyiciyi `Format/` altına koyacaksan
önce mevcut `Table`/`Ui` yardımcılarına bak.

## Plugin

- `Bridge/` — `FmApi` (HTTP yüzeyi) + `FmApi.System.cs` (quit, repaint, screenshot, path okuma),
  `HttpBridge`, `Plugin`, `Game`, `State`, `Send`, `Schema`, interop.
- `Controls/` — `Buttons` + `Buttons.Controls.cs` (katalog/click/queue) + `Buttons.Runtime.cs`
  (render, shape, pointer), form/radio, satırlar, alt menüler, dialoglar.
- `Reads/` — sayfalar, paneller, metin/çeviri, oyuncu kartı, teklif, taktik, kayıt.

Yeni bir okuma: veri yolunu `KeyPaths`/`Path` ile al; ekran yazısını veya sıra numarasını ölçüt yapma.
Yeni bir yazma: oyunun kendi olayını gönder; olay kuyruğunun boşalmasını bekle.

## Nullable politikası

- Akış analizi açık; gerçek dereference/value/null-literal uyarıları düzeltildi.
- Kalan beş kategori (`CS8600`, `CS8603`, `CS8604`, `CS8619`, `CS8620`) oyun köprüsünün
  dinamik JSON sınırında oluşuyor; `JsonRead`/`Field` ve çalışma zamanı kontrolleri kullanılıyor.
  Bunlar proje düzeyinde bilinçli olarak susturuldu (bkz. `fm.csproj`).
- Yeni okuma eklerken önce `JsonRead` kullan; tip güvenli DTO’ya geçilince bu liste kaldırılmalı.

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
tools/smoke.sh            # build + plugin + testler + yayınlanmış CLI
```

Test kontrol listesi: `docs/PLAYTEST.md`. Teknik bulgular: `docs/FINDINGS.md`.

Saf yardımcılar için birim testleri (oyun gerekmez):

```bash
dotnet test tests/FmCli.Tests/FmCli.Tests.csproj -c Release
```
