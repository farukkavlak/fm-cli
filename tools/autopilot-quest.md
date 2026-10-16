# Otopilot görev defteri — turlar arası hafıza

Her tur başında oku, sonunda güncelle. Taze bir oturum ilerlemeyi buradan alır; aynı maddeyi
iki kez yapmaz, park edileni tekrar denemez.

## Şu an
- Madde: A "reply ara boş an riski" — ÖLÇÜLDÜ, boş ara durum yok (oyun cevap kümesini yerinde değiştiriyor); `[x]`,
  kod değişmedi (churn yok)
- Durum: bitti, commit atıldı

## Ölçülen kanıt (tur tur)

### Tur 5 — A "`reply` ara boş an riski" — ÖLÇÜLDÜ, DEĞİŞİKLİK YOK (§8.193, 16 Ekim)
Ne: Kod değişmedi. Cevap listesi geçişte hiç boşalmıyor; mevcut koşul (başlık = düğmeler + öncekinden farklı, boş
yalnız `CanRespond != "True"` iken) doğru; kanıtlanmış hata olmadığı için eskisi kaldı (kural 4).
- Ölçüm aracı: geçici `python3` (buton basıp `/buttons?json=1` ~60 ms aralıkla, ayrıca `/texts?anchorname=MessageBodyText&up=6&paths=1&most=1`).
- Üç cevaplı mesaj (TR: Aylık Performans / Lincoln son dakikalarda / Street gol orucuna; EN: Monthly Performance /
  Lincoln fall to late defeat / Street ends goal drought) tüm sıralarda: **0/80 örnekte boş cevap**. Geçiş: başlık önce
  B oluyor, düğmeler hâlâ A (reddediliyor: `shown == previous`); sonra düğmeler boşalmadan eskisinden yenisine
  (2→2, 2→1, 1→1). Bir kısmi an (`ans=['View Goal']`, sayfada ikisi) görüldü, `shown == page titles` tutmadığı için
  kod reddediyor.
- Üretim seviyesi (`fm reply A; fm reply B`): TR 10 + EN 6 geçiş, durumlar "başka sayfa açık" / "mesaj zaten açık" /
  "sayfa ilk açılıyor" → her seferinde doğru liste (2 ve 1), hiç "cevap seçeneği yok".
- Aynı mesajı üst üste basma (`previous = null` yolu): TR 6/6 doğru liste.
- Boş yol doğrulaması (negatif): cevaplı mesajdan cevapsız `Fitness tests`'e geçişte "(cevap seçeneği yok)" doğru.
- Dönüş: `fm settings Language "Türkçe"` → "English → Turkish". Paylaşılan koda dokunulmadı → sweep gerekmedi.

### Tur 4 — A "Mesaj eki için 1,5 sn" (`WriteAttachment`) — PARK (§8.192, 16 Ekim)
Ne: Kod değiştirilmedi. Sabit 1,5 sn'nin bu makinede doğru okuduğu ölçüldü (pay ~2,5 kat), ama yerine konacak
oyun işareti bulunamadı; eki olmayan mesaj türü kayıtta olmadığı için negatif durum ölçülemedi → `[?]`.
- Ölçüm aracı: `/press?text=<konu>&name=NewsItemButton&any=1` sonra 30 ms aralıkla
  `/texts?anchor=Attachments&up=8&paths=1` (geçici `python3`, ~/.config/fm-cli/port).
- Zaman çizelgesi (mesaj değiştirilirken, TR): 0,13 sn önceki mesajın eki (Mansfield raporu, 110 metin) ·
  0,20–0,40 sn başlık var içerik yok (boş ara durum) · 0,47–0,55 sn kısmi (9 metin, "Data Hub · No Data") ·
  ≥0,60 sn tam (22 metin). Yani ek ~0,6 sn'de oturuyor; 1,5 sn = ~2,5 kat pay.
- İşaret arayışı: `/elements?text=Attachments&up=2&depth=16..18&members=0|1` ile boş ve dolu durum karşılaştırıldı:
  `#AttachmentsTitleContainer`, `#Attachments`, `FMUnbindDynamicBindingRooot`, `SIVisible`,
  `BindingRoot #MessagesToolBindingRoot`, `BindingExpect #EventTrigger` **ikisinde birebir aynı**; tek fark her
  kartın (`VariableSizedGridElement` › `BindingRemapper #Portal Card Template Element`) içeriğinin bağlanması
  (`TemplateContainer` › `BindingExpect #TileBindingExpect`). Bu, "kartlar bağlandı" işareti olurdu ama eki
  olmayan mesajda kaç kart olacağı bilinmiyor.
- Negatif durum bulunamadı: `fm reply` ile 14 mesaj denendi, hepsinde "Ek:" ya da "Haber:" satırı var; `/inbox`
  verisi ek ile ilgili alan taşımıyor. Bir `fm next` (09:15→10:00) yeni mesaj getirmedi; gün atlatmak gerekiyor.
- Karar (kural 4): kanıtlanmış iyi hâl yok → eskisi kaldı, madde `[?]`, soru BAGIMLILIKLAR'da. Commit: yalnız
  dokümanlar (BAGIMLILIKLAR, FINDINGS §8.192, bu defter). Paylaşılan koda dokunulmadı → sweep gerekmedi.

### Tur 3 — B "Acknowledge and Move On yazısıyla basan yerler" (§8.191, 16 Ekim gece)
Ne: 10 basış (`press text="Acknowledge and Move On"`) artık düğmenin gönderdiği olayla bulunuyor. Yeni
`cli/fm/Acknowledge.cs` (`AcknowledgeButton()`/`Acknowledge()`) `SpeakToAction` düğmelerini `/buttons?json=1&gen=` ile
alıp `Action` düğümünün `Event`'ini `PullEach` ile okuyor, `Event == 31` (`EventFmxClearChanges`, `ActionID` 0) olanı
işaretçisiyle basıyor; yoksa hiç basmıyor. Değişen dosyalar: `cli/fm/Acknowledge.cs` (yeni), `BoardRequests.cs`,
`Captains.cs`, `Propose.cs`, `Release.cs` (3), `TraitTraining.cs` (2), `TrainingReport.cs` (2). Paylaşılan koda
dokunulmadı (Bridge/OpenPage/reply yok) → yine de `sweep.sh` çalıştırıldı.
- Sorun (önce): düğme etiketle bulunuyordu; etiket oyunun kendi cümlesi (başka dilde çevrilebilir). TRACE ile eski kod
  bir istekte 3 kez `press ?text=Acknowledge and Move On&any=1` (boşa denemeler), yeni kod tek `press ?ptr=0x…`.
- Kimlik ölçümü (canlı, `/buttons?json=1&gen=…` + `/pull?props=Event,ActionID`):
  - TR ve EN "Acknowledge and Move On" → `{"Event": "31", "ActionID": "0"}`.
  - Aynı karttaki menü düğmeleri → `Event` 27 / 0 / 0 / 44 (hiçbiri 31 değil) → negatif; yardımcı bu durumda basmıyor.
- Üç durum + iki dil (`fm training "Freddie Draper" --report --coach "<antrenör>"`, `FM_TRACE=1`, rc=0 her seferinde):
  - TR "kart zaten açık": `39: press ?ptr=0x4b50ed000`; "başka sayfa açık" (Messages açık): `press ?ptr=0x42da9d800`;
    "sayfa ilk açılır": `press ?ptr=0x49dde7000`; hepsi "Freddie Draper — antrenman raporu (…)".
  - EN aynı ikisi: `press ?ptr=0x4b4168800` / `press ?ptr=0x497bdc000`; çıktı "training report (…)".
  - Dönüş: `fm settings language "Türkçe"` → "English → Turkish"; final TR koşusu `press ?ptr=0x4d0e10000`, rc=0.
  - Negatif CLI: `fm propose "Ben Waine"` → 1 `buttons?gen=` çağrısı, 0 `press ?ptr=` (yardımcı kartı yok).
- Öncesi/sonrası doğrulaması: değişiklik `git stash` ile alınıp eski kod derlendi → eski TRACE'te `press ?text=…` (3 kez);
  sonra geri alınıp derlendi. `propose` "Önerme ekranı açılmadı" hatası eski koddada birebir aynı → bu değişiklikten değil.
- `tools/sweep.sh`: 50 komut, 0 hata. Kayıt test kaydı; TR'ye dönüldü.

### Tur 2 — D "`~/.config/fm-cli/` önbellekleri hangi kayda/dile ait" (§8.190, 15 Ekim gece)
Ne: `diff`in baktığı hâl (`Diff.cs`) üst klasörde yalnız takım adıyla tutuluyordu; oyuna ait kelimeler/dil
taşıdığı için `CacheFile`e (dil + yönetici–kulüp) taşındı. Dosya: `cli/fm/Diff.cs` (+ `docs/BAGIMLILIKLAR.md`,
`docs/FINDINGS.md`). Paylaşılan koda (Bridge/CacheFile tanımı) dokunulmadı → sweep gerekmedi.
- Sorun (önce): TR'de `fm diff` ile bakış kaydedilip `fm settings dil English` sonrası `fm diff` → her oyuncunun
  sözleşme tarihi değişmiş sayıldı; ölçüm çıktısı: `"Contracts":[{"Name":"Jamie Pardington","Was":"N/A","Is":"Yok"}]`.
  Dosya: `~/.config/fm-cli/diff-Lincoln.json` (üst klasör, takım adıyla).
- Durumlar + diller (sonra):
  - EN ilk: `fm diff --json` → `{"First":true,...}`; dosya `cache/english/Faruk Kavlak - Lincoln/diff-Lincoln.json`.
  - EN ikinci: `fm diff --json` → `"Contracts":[]` (yanlış değişiklik yok).
  - TR ilk: `fm settings Language "Türkçe"` → `fm diff --json` → yeniden `{"First":true,...}` (EN dosyası ve
    eski üst dosya okunmadı); TR ikinci → `"Contracts":[]`; dosya `cache/turkish/.../diff-Lincoln.json`.
  - Üstteki eski `diff-Lincoln.json` dokunulmadı (mtime 23:53 sabit kaldı).
- Kayıt test kaydı; TR'ye dönüldü.

### Tur 1 — B "Mutluluk seviyesi" (§8.189, 15 Ekim gece)
Ne: `happiness` sıra+rengi seviyenin yazısına değil oyunun puanına bağlandı; moral/konum/seviye yazıları
oyunun diline çevrildi. Dosya: `cli/fm/Happiness.cs` (+ `docs/BAGIMLIKLAR.md`, `docs/FINDINGS.md`,
`docs/manual/KILAVUZ.md`). Paylaşılan koda dokunulmadı → sweep gerekmedi.
- Sorun (önce): `Array.IndexOf(HappinessLevels, p.Level)` "Biraz Mutlu"/"Biraz Mutsuz" için -1 →
  bu seviyeler listenin başına düşüyor, rengi kırmızı; `--why` moral/konum İngilizce ("Poor").
  Kanıt: `/tmp/happ.json` (önce) Ben House "Very Unhappy", listenin başı "Biraz Mutlu".
- Veri yolu (ölçüldü, `/pull`): `Human.Team.Players` › `HierarchyOverallHappinessLevel` — 27/27 oyuncu.
  Değerler: 80 Delighted, 60 Very Happy, 40 Happy, 20 Slightly Happy, -14/-20 Content, -40 Slightly Unhappy,
  -65 Very Unhappy. Bant sınırları boşluklarda: `<-30` kırmızı, `<30` sarı, üstü yeşil.
- Komutlar/kanıt (sonra):
  - TR: `fm open Messages` → `fm happiness --json`; `fm happiness --json` (açık); `fm open Messages` →
    `fm happiness --json` → üçü de `md5 2d06d6c224a52fe0286684100b10c3d9`, sıra artan (-65…80 doğrulandı).
  - TR renk (pty): `script -q /dev/null fm happiness` → `38;5;9` Çok Mutsuz/Biraz Mutsuz (kırmızı),
    `38;5;11` Memnun/Biraz Mutlu (sarı), `38;5;2` Mutlu+ (yeşil).
  - TR `--why`: `fm happiness "Ben House"` → "Çok Mutsuz · moral Kötü · Yüksek Nüfuzlu Oyuncu";
    `fm happiness --why` 27/27, 11 sn, rc=0.
  - EN: `fm settings dil English` → `fm open Messages` → `fm happiness --json`; `fm happiness --json`
    (açık) → ikisi aynı; renk Very Unhappy/Slightly Unhappy kırmızı, Content/Slightly Happy sarı.
  - Dönüş: `fm settings Language "Türkçe"` → "English → Turkish"; `fm happiness --json` önceki TR
    çıktısıyla birebir aynı.
- Önce/sonra (aynı oyuncu, TR): `fm happiness "Ben House"` önce "moral Poor", sonra "moral Kötü".

## Park edilenler ([?], kararı verilmiş)
- A "Mesaj eki için 1,5 sn" (`WriteAttachment`, Program ~993) — **Karar (Faruk, 16 Ekim): (a), sabit 1,5 sn kaldı**
  (pay ~2,5 kat). İşarete çevirmek için iki ölçüm borcu var: (1) oyunun "ek hazır" işareti (aday
  `#TileBindingExpect`), (2) eki olmayan bir mesajda ağacın/işaretin davranışı (bu kayıtta örnek yok). İkisi
  bulununca sabit bekleme işarete çevrilecek (§8.192).

## Sabitler
- Kapsam: **A** (zamanlama) + **B** (dil/kimlik) + **C** (ayarlar), mümkünse **D**.
- Girmeyeceğiz: §14 UX/görsel, **G** (platform), **H** (yayın).
- Model: opencode-go/deepseek-v4.1-flash · sürücü: tools/autopilot.sh
- Commit: main'e, push yok. Conventional Commits, son satır `Co-Authored-By: OpenCode`.
