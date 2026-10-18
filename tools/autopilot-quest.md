# Otopilot görev defteri — turlar arası hafıza

Her tur başında oku, sonunda güncelle. Taze bir oturum ilerlemeyi buradan alır; aynı maddeyi
iki kez yapmaz, park edileni tekrar denemez.

## Şu an
- Madde: **A "`Task.Delay`" / `TraitTraining 32`** — **BİTTİ** (§8.206, Tur 17): alışkanlık eğitimi sayfasını açan iki
  döşemenin ardındaki sabit 500 ms, döşemeyi **basılmadan** sormaya (`press ?…&dry=1`, 0,25 sn aralıkla, en çok 8 sn)
  çevrildi; çıktı TR + EN'de üç durumda öncesiyle birebir aynı, döşeme arası 0,57–0,60 → 0,43–0,53 sn.
- Bir sonraki tur: A/`Task.Delay` sıradaki adaylar: `Responsibilities 283/388` · `Matches 82` · `OppositionCard 55 91` ·
  `Scouting 100` · `TrainingSchedule 345` · `TacticFiles 181` — **`Staff 74` park edildi, tekrar denemeyin**;
  `TraitTraining 32` bitti. Ya da A/`Kısa Until` adayları (`Saves 207/220`, `TraitTraining 89`, `TacticSlots 112`,
  `League 216`, `Prep 124`). Ölçülen bir B işi: TR'de `--trait-suggest` düğmesi bulunamıyor (İngilizce yazı tabloda
  yok, §8.206); `fm settings` hâlâ "Manage settings for this game" / "See all" yazılarıyla menüyü açıyor. Not:
  kaydırmadan sonra okumak isteyen maddelerde (Responsibilities/Matches/Scouting) `Staff 74`'ün tuzağı var — oyun tam
  olmayan pencereyi de "sakin" gösterebiliyor, önce işaret bulunmalı. Köprü 60 çağrılık sık bir döngüde kilitleniyor.
- Durum: bitti, commit atılıyor

## Ölçülen kanıt (tur tur)

### Tur 17 — A "`Task.Delay`" / TraitTraining 32: döşemeleri açan iki sabit 500 ms işarete bağlandı (§8.206, 18 Ekim)
Ne: `cli/fm/TraitTraining.cs`. Alışkanlık eğitimi sayfası iki döşemeyle açılıyor ("Training Rating" oyuncu kartından,
"Traits" onun açtığı sayfadan) ve `foreach` her basıştan sonra sabit 500 ms bekliyordu. Yeni: basılacak döşeme
**basılmadan** soruluyor (`press ?text=…&name=layout-tile-interaction-button&any=1&dry=1`, `Until` ile 0,25 sn
aralıkla, en çok 8 sn), ancak ekranda görününce basılıyor; gelmezse adıyla hata yazılıyor. Döşemeden sonraki liste
beklemesi zaten oyunun işaretiydi (`Ready`: "Start New Trait Training" açılır listesi ya da "Cancel Current Trait
Training" düğmesi), o yüzden ikinci 500 ms de kalktı.
- Ölçüm komutu (okuma yolu; hiçbir şey öğretmiyor): `fm training "Ben House" --trait`.
- Öncesi (eski derleme): TR üç durum (sayfa ilk kez `fm open "Messages"` sonrası / zaten açık / başka sayfa
  `fm open "Squad Dynamics"`) 10 satır, `md5 8a43caeda16d227f818785976e7fa906`, 8,8 / 8,2 / 7,7 sn; EN üç durum 10
  satır, `md5 9e92fe93ca38f07f0d9f2d20a30c8b56`, 10,7 / 7,5 / 7,6 sn. Döşeme→döşeme aralığı (izden) 0,57 / 0,60 /
  0,59 sn.
- Sonrası: TR üç durum 10 satır, **aynı** `md5 8a43caed…`, 9,5 / 8,6 / 9,5 sn; EN üç durum 10 satır, **aynı**
  `md5 9e92fe93…`, 9,5 / 8,8 / 8,5 sn; döşeme→döşeme 0,43 / 0,53 / 0,52 sn. Dönüş: `fm settings language "Türkçe"` →
  `English → Turkish`; `/state` → `Language: turkish`, saat 2 Ağustos 14:00 (ilerletilmedi).
- Denemeler: (1) yalnız sabit bekleme silinip `Arriving`e bırakıldı — çıktı aynı, ama "Traits" basışı 1 sn'lik
  yoklamaya takıldı (izde 6,09 boşa → 7,15 tuttu); (2) döşemeyi `dry=1` ile 0,25 sn aralıkla sormak seçildi
  (kazanç: 0,43–0,53 sn). Negatif (aynı yoldan): TR `fm training "Ben House" --trait-remove "ZZZ yok"` → rc=1,
  seçenekler karttan; EN `… "ZZZ nope"` → rc=1.
- Yan bulgu (B, yeni iş): TR'de `--trait-suggest` düğmeyi bulamıyor ("Oyun Karakteristiği İdmanı Önerisi İste" olarak
  geliyor, İngilizce karşılığı tabloda yok) — eski derlemede de birebir aynı; BAGIMLILIKLAR B'ye örnek olarak yazıldı.
- Paylaşılan koda (Bridge/OpenPage/reply/ortak yardımcılar) dokunulmadı, değişiklik yalnız `TraitTraining.cs` içinde →
  `sweep.sh` gerekmedi. Kayıt test kaydı: 2 Ağustos 2026 14:00; saat ilerletilmedi, maç oynanmadı, hiçbir alışkanlık
  öğretilmedi/bıraktırılmadı.

### Tur 16 — A "`Task.Delay`" / `Staff 74` — PARK, kod geri alındı (§8.205, 18 Ekim)
Ne: `cli/fm/Staff.cs`teki All Staff okuması (`foreach share { scroll; Task.Delay(400); oku }`) sabit duraklamadan
oyunun işaretine çevrildi: `RowsLaidOut(was, moved)` — pencere kaydırmadan öncekinden **farklı** ve art arda iki
okumada **aynı** olunca kabul; `TableScrolled(share)` kaydırmanın oynayıp oynamadığını `scroll` cevabını `?to`suz
çağrıyla karşılaştırarak söylüyor. Derlendi, ölçüldü, **geri alındı** (`git restore cli/fm/Staff.cs`) — kural 4.
- Öncesi (sabit 400 ms), ilk oturum: TR üç durum (başka sayfa açık / sayfa açık / başka sekmeden) 48 satır,
  `md5 13926f52d5ba79fc9c16047fe4f0112d`, süre 7,9 / 4,5 / 4,6 sn; EN üç durum 48 satır, `md5 aa55a48b…`,
  5,6 / 4,7 / 4,5 sn; arka arkaya **12/12 koşu 48 satır**.
- Sonrası (işaret): EN A 48 satır ✓ / **B 34 satır ✗** / C 48 ✓, süreler 3,1 / 2,8 / 3,2 sn; 6 koşuluk seride
  `42, 48, 48, 48, 34, 48` → 18 koşunun 2'sinde pencere kaybı.
- Neden (FM_TRACE + ara okuma saysı): `at 0.75: agree=True differs=True` ile **20 satır** iki okumada aynı dönüp tam
  sanıldı (ara yerleşim sakin görünüyor); bir koşuda kaydırma sorgusu hata döndü (`TableScrolled` null) ve gezinti
  kesildi; 50 ms'lik yoklama köprüyü sıkıştırıyor.
- Satırların oturma ölçümü (geçici `python3`, `/scroll` + `/texts?anchor=All Staff&up=12&paths=1`): beş noktada
  93 / 272 / 125 / 93 / 213 ms → 400 ms'in payı ~1,5–4 kat.
- Yeni oturum doğrulaması (oyun `pkill` + `fm start` sonrası, geri alınmış kod): EN üç durum 48 satır
  `md5 5c9e418e…`, 5,6 / 4,5 / 4,4 sn; TR üç durum 48 satır `md5 2d0ad32b…`, 5,0 / 4,4 / 4,6 sn. Dönüş:
  `fm settings language "Türkçe"` → `English → Turkish` (oyun TR bırakıldı).
- Dokümanlar: BAGIMLILIKLAR (alt madde `[?]`), FINDINGS §8.205, bu defter. Kod değişmedi → `sweep.sh` gerekmedi;
  paylaşılan koda (Bridge/OpenPage/reply) dokunulmadı.

### Tur 15 — B "`fm settings` 'Match Day' sekmesini yazıyla açıyor" (TR/DE): sekme kapsayıcıyla açılıyor (§8.204, 18 Ekim)
Ne: `cli/fm/Settings.cs`. `PreferencesTab` sekmeyi `click ?text=<sekme>&name=DetailedPreferencesTabButton` ile
açıyordu; sekme yazısı oyunun sözü (TR/DE "Matchday", EN "Match Day"), bu yüzden TR/DE'de "Match Day" sekmesi hiç
açılmıyordu. Yeni: `TabButtons` eşlemesi (`Match Day`→`MatchDayTabButton`, `Your World`→`YourWorldTabButton`,
`Processing`→`ProcessingTabButton`; `/elements`ten okunan kapsayıcı adları, şablonun, dilden bağımsız) ile
`click ?within=<kapsayıcı>&name=DetailedPreferencesTabButton`; eşleşme yoksa yazıya düşüyor.
- Öncesi (eski derleme): TR `fm settings --json` (sayfa kapalıyken) → **15** satır, `md5 e5eb7a89…` (dört maç günü
  ayarı yok); DE → **15**; EN → 19 (`md5 cb604c52…`).
- Sonrası: TR dört durum (başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / başka sayfadan `fm open "Squad
  Dynamics"` / sayfa Processing sekmesinde açıkken) → hepsi **19** satır, aynı `md5 fa5b6948ada7f605b9f3ca5aacdd17e9`.
  EN üç durum → 19 satır, `md5 cb604c52…`, **eski derlemeyle birebir**. DE → 19 satır, `language = German`.
- Değerler (TR): `maç gösterimi = Key`, `dinamik mod = Kapalı`, `vurgu hızı = 10x`, `ara hızı = 40x` — EN öncesi
  çıktıyla aynı (üçü de aynı oyun ayarı).
- Yazma yolu (aynı sekme): `fm settings "vurgu hızı" 4x` → `10x → 4x`, sonra `10x` → `4x → 10x`; `--json` → `10x`
  (eski değerine döndü). Negatif: `fm settings "vurgu hızı" 7x` → "bir seçenek değil", rc=1.
- Dönüş: `fm settings language "Türkçe"` → `German → Turkish`, `/state` → `Language: turkish`; final TR koşusu 19
  satır, ilk koşuyla aynı `md5`. Paylaşılan koda (Bridge/OpenPage/reply) dokunulmadı → `sweep.sh` gerekmedi.
- Kayıt test kaydı (2 Ağustos 2026): saat ilerletilmedi, maç oynanmadı; vurgu hızı 4x yapılıp 10x'e geri alındı,
  dil TR'ye döndü.

### Tur 14 — B "`fm settings` ayarı başlık yazısıyla buluyor" (Almanca dil ayarı): blok adıyla bulunuyor (§8.203, 18 Ekim)
Ne: `cli/fm/Settings.cs`. `Locate` ayarın denetimini `/form`daki başlık yazısıyla (`Heading: "Game Language"`)
arıyordu; Almanca oyunda başlık "Language" geldiği için dil satırı bulunamıyordu. Yeni: `Setting`e `Block` alanı
eklendi (11 açılır liste), `Locate` önce `Within` zincirinde blok adını arıyor, bulamazsa başlığa düşüyor.
- Öncesi (DE, eski derleme): `fm settings --json` → **14** satır (`language` yok); `fm settings dil Deutsch` →
  "was set to Deutsch, but when the page was opened again could not be read", rc=1; `/state` → `Language: german`.
- Sonrası (DE): `fm settings --json` → **15** satır, `language` = `German` (20 seçenek); `fm settings language
  "Türkçe"` → `language: German → Turkish`, rc=0, `/state` → `Language: turkish`.
- TR, üç durum (başka sayfa açık `fm open "Messages"` / sayfalar zaten açık / başka sayfadan `fm open "Squad
  Dynamics"`): üçünde de 15 satır, `md5 e5eb7a89c1678a1181292bc240263388` — **değişiklik öncesi çıktıyla birebir
  aynı** (`python3` karşılaştırması `True`). Yazma yolu: `fm settings maaş Haftalık` → `Aylık → Haftalık`, sonra
  `Aylık` → `Haftalık → Aylık`, `fm settings maaş --json` → `Monthly` (eski değer).
- TR negatif: `fm settings dil Klingon` ve `fm settings "para birimi" Klingon` → seçenek listesiyle red, rc=1.
- EN, üç durum: 19 satır, üçü de aynı md5 `cb604c52…`; `git stash` + eski derleme ile yeni derleme çıktısı
  **birebir aynı**. Dönüş: `fm settings language "Türkçe"` → `English → Turkish`; final TR koşusu 15 satır.
- Yeni bulgu (§8.203 sonu): sekme yazısı — TR/DE oyunda "Matchday", EN'de "Match Day" → TR/DE'de dört maç günü
  ayarı hiç okunmuyor; madde açıldı.
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`/tmp/sweep_t14`). Kayıt test kaydı (2 Ağustos 2026):
  saat ilerletilmedi, maç oynanmadı; maaş ayarı Haftalık yapılıp Aylık'a geri alındı, dil TR'ye döndü.

### Tur 13 — A "`Task.Delay`" / Takers 206: tür seçildikten sonraki 500 ms gereksizdi (§8.202, 18 Ekim)
Ne: `cli/fm/Takers.cs` `ShowSetPieceKind`. Set Piece Type menüsünden türü seçtikten sonra sabit 500 ms bekleniyor,
sonra `Still(...)` ile "menü bu türü gösteriyor **ve** atıcı düğmesi iki okumada aynı" işareti aranıyordu. Bekleme
bu işaretin kapsadığı bir şey → kaldırıldı (`Task.Delay(500)` silindi, yerine açıklama).
- Öncesi/sonrası çıktı (beş tür): `fm takers --json` TR'de `md5 8eb553398145f8d263e7f19a7d428c04` (önce **ve**
  sonra), EN'de `f42fb27412440ca4961b07d58885e920`; türler ve atıcı sıraları birebir aynı (Korner sağ: Bayliss,
  Varfolomeev, Moylan … Taç: Hamer, Collins, Bradley).
- Üç durum (her biri TR + EN, `rc=0`): başka sayfa açık (`fm open "Squad Dynamics"`) / sayfa zaten açık (hemen
  ikinci koşu) / başka sayfadan (`fm open "Messages"`) → üçünde de aynı md5.
- Zamanlama (işaretin tuttuğunu kanıtlar, hedef değil): `FM_TRACE=1 fm takers --json`; `select ?value=<tür>` ile
  doğru düğmeye basma arası önce ~1,47–1,60 sn, sonra ~1,50–1,60 sn (seçimden sonraki ilk yoklama artık beklemesiz:
  0,03–0,06 sn). Toplam süre 3 koşu ortalaması: önce ~19,2 sn (19,55/18,99/19,10), sonra ~19,3 sn
  (19,36/19,23/19,39) → **nötr**; `Still` kimi türde üçüncü okuma isteyebiliyor, kazanç yok. Karar: kaldırma kalır
  (kural 2), çünkü kalan sabit bekleme makine hızına dair bir varsayımdı; işaret zaten vardı.
- Mutasyon yolu (aynı fonksiyon): `fm takers korner sag move "Ivan Varfolomeev" 1` → sağ liste
  `1. Ivan Varfolomeev, 2. Tom Bayliss, 3. Jack Moylan` (veriden doğrulandı), sonra `… 2` ile eski sıraya alındı
  (`1. Tom Bayliss, 2. Ivan Varfolomeev`); rc=0.
- Dönüş: `fm settings language "Türkçe"` → "English → Turkish", `/state` → `Language: turkish`.
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`/tmp/sweep_t13`). Paylaşılan koda (Bridge/OpenPage/reply)
  dokunulmadı, değişiklik yalnız `Takers.cs` içinde. Kayıt test kaydı: 2 Ağustos 2026 14:00; saat ilerletilmedi,
  maç oynanmadı, atıcı sırası denendi ve geri alındı.

### Tur 12 — A "Kısa `Until`" / Budget 70 (kartın slider adımları): ölçüldü, değişiklik gerekmedi (§8.200, 18 Ekim)
Ne: `cli/fm/Budget.cs` **değişmedi**. Karar (kural 4): mevcut işaret oyunun kendi işareti — slider bir adıma
konunca kartın `budget.BudgetAdjustmentEditedTransferValue` / `...EditedOtherValue` yazıları değişiyor **ve**
kartın yürürlükteki adımı ayıran yıldızı adıma uyuyor. 3 sn / 0,2 sn aralıkla bu işaret bekleniyor; sabit bekleme
yok, süre yalnız üst sınır.
- Ölçüm aracı: `FM_TRACE=1 fm budget ayarla` (adımsız → komut 16 adımı gezip listeyi basar ve `Cancel` eder,
  yani bütçe değişmez). İz `[ zaman +süre] route ?query` satırları veriyor; `setcontrol` satırından sonraki
  `form ?anchor=Budget Adjustment` okumaları sayılıp `setcontrol` bitişiyle arasındaki süre ölçüldü.
- Üç durum (her biri TR + EN, `rc=0`, 16 adım, doğru rakamlar):
  - sayfa ilk kez açılıyor (`fm open "Messages"` sonrası): TR `37–270 ms` (2 adımda ikinci okuma) / `20–275 ms`
    (1) · EN `30–106 ms` (0).
  - sayfa zaten açık (hemen ikinci koşu): TR `32–278 ms` (1) · EN `23–268 ms` (1).
  - kart zaten açık (curl ile `Adjust Transfer and Wage Budget` basılıp koşuldu): TR `27–331 ms` (2) ·
    EN `27–371 ms` (4).
  - Toplam 7 koşu = 112 adım, 11'inde ikinci okuma, en kötü 371 ms → 3 sn sınırına ≥8 kat pay.
- İki dil: TR ve EN'de 16 adımın rakamları birebir aynı (yalnız "Transfers/Wages" etiketi çevrili; `diff` ile
  doğrulandı). Dönüş: `fm settings language "Türkçe"` → "English → Turkish", `/state` → `Language: turkish`;
  ayarlar bütçe rakamları turn başındakiyle aynı (`fm budget --json`: Transfer 626B £, maaş 2.320 £ aşılmış).
- Ölçüm tuzağı (kayda değer): erken koşulardan ikisi "no button shows 'Adjust Transfer and Wage Budget'" ile
  düştü — sebep zamanlama değil, **ekranda açık kalmış bir "Budget Adjustment" kartı** (Almanca/EN denemelerinden
  bana kalmıştı); kartın kendisi modal olduğu için sayfanın düğmeleri listelenmiyor. `Cancel` ile kapatılınca aynı
  koşu 2/2 hatasız. Yani bu kayıtta tazeyken düşen bir vaka yok; kart açıkken de çalışmak ayrı bir iyileştirme
  olarak duruyor (§8.200).
- Regresyon: **kod değişmedi** (yalnız ölçüm) → `sweep.sh` gerekmedi. Kayıt test kaydı: 2 Ağustos 2026 09:15;
  zaman ilerletilmedi, maç oynanmadı, bütçe dağılımı değiştirilmedi (her koşu `Cancel` ile kapandı).
- Yeni bulgu: B "`fm settings` ayarı başlık yazısıyla buluyor; Almanca oyunda dil ayarı bulunamıyor" (§8.201) —
  madde açıldı, kalıcı yol için üç seçenek yazıldı. Ölçüm: Almanca oyunda `fm settings` listesinde dil satırı yok,
  `fm settings language "Türkçe"` → "'language' ayarı Your World sekmesinde bulunamadı."; oysa açılır liste
  yerinde (`Binds app.nativesettings`, 20 dil) ve `/form` başlığı "Language" diye veriyor (CLI "Game Language"
  arıyor). Dil eklenti ucuyla (`/select?ptr=…&index=13`) Türkçeye döndürüldü.

### Tur 11 — A "Kısa `Until`" / Actions 126 (`AfterAction`): ekrandaki konuşan düğmeler konuşma sanılıyordu (§8.199, 18 Ekim)
Ne: `cli/fm/Actions.cs`. `AfterAction` "oyun bir konuşma açtı mı" diye `TalkOptions()`a bakıyordu — o da ekranda
`SpeakToAction` taşıyan her `StandardButton`ı sayıyor, dolayısıyla **gazete maddesinin isteğe bağlı cevapları**
(ör. "Adjust Scouting Budget"/"Adjust Transfer and Wage Budget") konuşma sanılıyordu. Yeni: `TalkButtons()`
(düğmenin işaretçisi + yazısı) seçimden **önce** alınıyor, `AfterAction(name, path, talkBefore)` konuşmayı yalnız
bu küme **değiştiyse** kabul ediyor (`ChooseAction` ve `staff fire --mutual` yolu güncellendi).
- Öncesi (TR, bütçe kartı ekranda, eski derleme): `FM_TRACE=1 fm actions "Ben House" "Contract>Enter Contract
  Negotiations"` → `actions ?choose=` (6,05 sn), sonra `dialog`/`contract`/`buttons`/`dropdowns`/`talk` → çıktı
  **bütçe seçenekleri "konuşma" olarak** ("1 Gözlemcilik Bütçesini Düzenle … Söylemek için: talk <numara>");
  oysa oyunun onay penceresi seçimden 0,24 sn sonra geliyordu (`/dialog`: "Enter contract negotiations?") ve
  **cevapsız kaldı** (sonraki komut "Oyun bir soru bekliyor" der).
- İşaret ölçümü (aynı ekran, `python3` 30 ms aralıkla `/buttons?json=1`): seçimden önce **ve** sonra birebir aynı
  iki düğme (aynı işaretçi/yazı, sayfa `9bef185e…`); `dialog` 0,24 sn'de. Gerçek konuşmada
  (`Interaction>Praise>Praise Training/Development`) seçimden **0,29 sn** sonra düğme kümesi değişiyor
  (6 yeni düğme, sayfa `9c5015031b…`) → "değiştiyse konuşma" işareti ayırıyor.
- Sonrası (TR, aynı ekran): aynı komut oyunun sorusunu basıyor ("Sözleşme görüşmeleri başlasın mı? … Cevaplamak
  için: dialog <No | Yes>"); izde `choose` (6,16) → `dialog`(boş) → `contract` → `buttons`/`dropdowns` (küme aynı,
  konuşma değil) → 6,83'te `dialog` bulundu. Üç durum: sayfa ilk kez açılıyor (`fm open "Messages"` sonrası) /
  zaten açık (ikinci koşu) / başka sayfa açık (`fm open "Squad Dynamics"`) → üçünde de soru basıldı, hepsi
  `dialog "No"` ile kapatıldı.
- İki dil: `fm settings dil English` → bütçe kartı ekranda aynı komut İngilizce soruyu bastı ("You are about to
  enter contract negotiations with the player… To answer: dialog <No | Yes>"); gerçek konuşma İngilizcede de
  açıldı (`Interaction>Praise>Praise Conduct` → "I heard you wanted to talk…", 3 cevap). Dönüş:
  `fm settings language "Türkçe"` → "English → Turkish" (`/state` → `Language: turkish`).
- Negatif (sonuçsuz iş, aynı ekran): `fm actions "Ben House" "Training>Rest Player>1 day"` → konuşma yoluna
  girmeden "yapıldı" (3 sn sınırı doldu), veriden doğrulandı: `fm intensity --json` → Ben House
  `Intensity: "1 Gün"`, `Value: 10`, `RestDays: 1`.
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`/tmp/sweep_t11`). Kayıt test kaydı (2 Ağustos 2026): saat
  ilerletilmedi, maç oynanmadı; övgü konuşması ve sözleşme sorusu kapatıldı, oyuncu 1 gün dinlendirildi.
- Yan ölçüm (aynı maddenin başka yeri, kod değişmedi): `FM_TRACE=1 fm budget ayarla` → her adımda `setcontrol`
  sonrası **ilk** `form` okuması yetiyor (0,03–0,08 sn; 15. adımda bir kez 0,24 sn) → 3 sn sınırına ≥12 kat pay;
  Budget 70 için "işaret, değişiklik gerekmedi" demek üzere üç durum + iki dil ölçümü borç.

### Tur 10 — B "Jobs listesi yalnız görünen pencereyi veriyor": Tur 9'un işi doğrulandı ve commitlendi (18 Ekim)
Tur 9'un koşusu (autopilot log: "tur 4 bitti — rc=124 (commit yok)") 40 dk'da öldürülmüş; `cli/fm/Jobs.cs`, `Mcp.cs`,
`docs/BAGIMLILIKLAR.md`, `docs/manual/KILAVUZ.md` ve §8.197 ağaçta commit edilmeden duruyordu. Bu tur kodu yeniden
derleyip ölçümleri baştan koştu, sonra commitledi (kod değiştirilmedi).
- Derleme: `dotnet build -c Release` → 0 uyarı, 0 hata.
- TR (`fm jobs --json`, 25–28 sn): **321** ilan, ilk `Celesti`, son `New Zealand`.
- Üç durum (TR, `md5` kulüp listesi): başka sayfa açıkken (`fm open "Squad Dynamics"`) / sayfa zaten açık /
  `fm open Messages` sonrası → üçünde de 321, aynı md5 `e4e18ab5…` (Celesti → New Zealand).
- İki dil: `fm settings dil English` → `fm jobs --json` 321, ilk Celesti, son New Zealand, BAŞVURU değerleri
  birebir aynı (`CanApply` listesi eşit). Kulüp adlarına göre karşılaştırma **yanıltıcı**: 15 ulus takımının adı
  oyunun dilinde (U.S.A. ↔ United States, Ivory Coast ↔ Fildişi Sahilleri) — §8.198. Dönüş: `fm settings language
  "Türkçe"` → "English → Turkish" (Türkçede setting adı `dil`, İngilizcede `language`; `fm settings language
  "Turkish"` olmuyor, seçenek adı `Türkçe`).
- Negatif (TR): `fm jobs 400` → "'400' ilanlarda yok"; `fm jobs "ZZZ yok"` → ilanlarda yok; `fm jobs "Barcelona"`
  (61. sıra, pencerede değil) → doğru panel (`1. Lig`, ESP, `4.`, Türkçe kurallar).
- Regresyon: `tools/sweep.sh` → 50 komut, 0 hata (ikinci koşu; ilkinde oyun kaydı boşaltmıştı, §8.198).
- Kayıt test kaydı: 2 Ağustos 2026 09:15; zaman ilerletilmedi, maç oynanmadı, kayıt değiştirilmedi.

### Tur 9 — B "Jobs listesi yalnız görünen pencereyi veriyor" — ölçüldü ve düzeltildi (§8.197, 18 Ekim)
Ne: `cli/fm/Jobs.cs`. Tablo yalnız görünen pencereyi (12–20 satır) döşüyor; `fm jobs` bu yüzden listenin başını
okuyordu (15 satır). Yeni: `AllJobs()` listeyi `/scroll?anchor=Current Manager&to=<0..1>` ile pencere pencere
geziyor, pencereleri sırayla birleştiriyor. Kaydırmadan sonraki **ilk okuma boş** geliyor (satırlar bir-iki kare
sonra döşeniyor) → pencere ancak "öncekinden farklı **ve** iki okumada aynı" olunca kabul (sabit bekleme yok).
Adım sabit değil: pencere, önceki pencerenin satır sayısına yakın sayıda *yeni* satır getirdiyse (iyi kesişmediyse)
yarıya iniyor, iyi kesiştiyse büyüyor → ekranın kaç satır sığdırdığı adımı belirliyor.
`SelectJob` artık satırı önce görünüme kaydırıyor: `rowselect text=<kulüp>&and=<güvenlik>&dry=1` ile sorup
(yan etkisiz) `Job.Share` konumuna, gerekirse ±0,05 kaydırıp öyle seçiyor. Panel gelmezse stderr'e uyarı yazıyor
(eskiden başka kulübün ayrıntısı basılabiliyordu). `JobDetail`/`JobAnswer` değişmedi.
- Öncesi (eski derleme, TR): `fm jobs --json` → **15** ilan (32 sn), `fm jobs "Celesti"`-benzeri pencerede olmayan
  kulüpler "ilanlarda yok"; liste kaydırıldığında başka kulüpler dönüyordu.
- Sonrası (TR): `fm jobs --json` → **321** ilan, sıra oyunun kendi sırası (Celesti → … → New Zealand), 22–29 sn.
  Ayrıntı: `fm jobs "Barcelona"` (61. sıra, pencerede değil) → doğru panel (`1. Lig/ESP/4.`, World Ranking, kurallar,
  CanApply false, 26 sn); `fm jobs "York"` (1. pencere) → 1,3 sn, `CanApply:true`. `fm jobs 321 --json` → New Zealand.
- Negatif (TR): `fm jobs "ZZZ yok"` → "ilanlarda yok"; `fm jobs 400` → "ilanlarda yok"; `fm jobs "Kentucky"` yok.
  `fm jobs "Danimarka"/"New Zealand"` → panel gelmedi uyarısı stderr'e, **başka kulübün ayrıntısı basılmıyor**
  (ulus işlerinde panelde "Training Facilities" satırı yok — eski davranış).
- Üç durum (TR): başka sayfa açık (`fm open "Messages"`) → 321, ilk Celesti, son New Zealand; zaten açık → 321,
  kulüp sırası birebir aynı; başka sekmeden (`fm open "Squad Dynamics"`) → 321. Dört koşuda kulüp listesi aynı
  (`md5` yerine liste karşılaştırması).
- İki dil (EN): `fm settings dil English` → üç durumda 321 ilan, aynı sıra, aynı başvuru değerleri (Celesti false,
  Udinese false, Villarreal false, York true, ABD null); `fm jobs "Barcelona"` → `First Division/ESP/4th`, kurallar
  İngilizce, 5,8 sn. Dönüş: `fm settings language "Türkçe"` → "English → Turkish"; son TR koşusu 321.
- Regresyon: `tools/sweep.sh` → ilk koşuda oyun ana menüye dönüp kayıt boşaldı (10 `rc=69`); `fm start` ile kayıt
  yüklenip sweep tekrarlandı → **50 komut, 0 hata** (`/tmp/sweep_t5b`). Paylaşılan koda (Bridge/OpenPage/reply)
  dokunulmadı, değişiklik yalnız `Jobs.cs` içinde. Bu tuzak §8.198'e yazıldı.
- Yeni bulgu (§8.197): panel yazıları bağlı kaldıkça eskisini gösterebiliyor (§8.194'ün devamı) — bir kez York'un
  ayrıntısında Celesti'nin "Wage Bill"/"Training Facilities" satırları okundu; düğme durumu (CanApply) doğruydu.
  Bu, `SelectJob`'un panel işaretinin ayrı bir madde olarak elden geçmesi gerektiğini gösteriyor.
- Kayıt test kaydı: 2 Ağustos 2026; zaman ilerletilmedi, maç oynanmadı, kayıt değiştirilmedi (başvuru gönderilmedi:
  `fm jobs "<kulüp>" apply` yalnız gönderme satırını yazdı, `--yes` verilmedi).

### Tur 8 — B "Jobs 'Managerial Position'" — ölçüldü ve düzeltildi (§8.196, 18 Ekim)
Ne: `cli/fm/Jobs.cs`. Satırlar 8'li adımlarla sayılıp lig sırası `^\d+(st|nd|rd|th)$` ile doğrulanıyordu; Türkçede
sıra "7."/"20." yazıldığı için ilk satırda kontrol düşüyor, liste boş kalıyordu. Seçilen işin paneli de
`<kulüp> Managerial Position` cümlesinin son sözcüğüyle bulunuyordu (Türkçede "Celesti Menajerlik Görevi").
Yeni: `/texts?…&paths=1`; satırın kulüp hücresi `team.name`, yönetici hücresi "" ya da `Manager.Name`; panel
başlığı yolu olmayan yazılar arasında kulüp adını içeren son yazı, panel sonu "Declare Interest In Job". Dosyalar:
`cli/fm/Jobs.cs`, `docs/BAGIMLILIKLAR.md`, `docs/FINDINGS.md`.
- Öncesi (eski derleme, TR): `fm jobs --json` → `İş ilanları okunamadı.` (10,8 sn).
- Sonrası (TR): `fm jobs --json` → 15 ilan: Celesti, Udinese, Villarreal, York, United States, FC Augsburg, Luton,
  Oxford United, Denmark, Fildişi Sahilleri, Albania, Austria, Switzerland, Colombia, Norway — alanlar doğru
  (League/Nation/Rating/Position oyunun kendi yazısı, Security, CanApply). Detay `fm jobs "York" --json`:
  `Rules:["Managers cannot work for two teams in the same division in the same season"]`, `Facts:{Reputation:Regional,
  Finances:Fair, Wage Bill:"1,5M £", Training Facilities:"Muhteşem antrenman tesisleri"}`, CanApply true.
- Üç durum (TR): başka sayfa açık (`fm open "Messages"` → 12 ilan) · zaten açık (15) · başka sekmeden
  (`fm open "Squad Dynamics"` → 17: Brazil, Curaçao, Czechia, …) — üçünde de gerçek ilanlar, hata yok.
- İki dil (EN): `fm settings dil English` → 14-15 jobs (N.Ireland, Ivory Coast, Dominican Republic …), detay
  `fm jobs "York"` → aynı kural + `Reputation/Finances Okay/Wage Bill 1,5M £/Superb training facilities`;
  dönüş `fm settings language "Türkçe"` → "English → Turkish" (`fm settings dil` → Türkçe doğrulandı).
- Yeni bulgu (madde açıldı): liste yalnız görünen pencereyi veriyor — aynı durumda 12 ya da 15 satır
  (sayfa kaydırıldığında başka kulüpler), pencerede olmayan ilan `fm jobs "Celesti"` → "ilanlarda yok"; ayrıca
  satırı görünmeyen işte panel boş kalabiliyor (Denmark). Ayrı madde olarak BAGIMLILIKLAR'a yazıldı.
- Paylaşılan koda (Bridge/OpenPage/reply) dokunulmadı → sweep gerekmedi.
- Kayıt test kaydı: 2 Ağustos 2026, saat ilerletilmedi; yalnız sayfa açıldı/seçim yapıldı, kayıt değişmedi.

### Tur 7 — C "Tarih biçimi (gün/ay sırası)" — ölçüldü ve düzeltildi (§8.195, 18 Ekim)
Ne: `cli/fm/Matches.cs` maç puanları tablosunu kurarken tarihi `^\d{2}/\d{2}/\d{4}$` ile arıyor, zengin tablodaki
kısa biçimi `TryParseExact("dd/MM/yyyy")` ile çözüyordu. Oyunun kendi "date format" ayarı (Preferences › Your World
› Format) dört sıra sunuyor; aynı maç (20 numaralı, Bristol Rovers, 1 Ağustos 2026) sayfada sırayla `01/08/2026`,
`08/01/2026`, `2026/08/01`, `2026/01/08`. Yeni tanıma sıradan bağımsız (dört haneli yıl bir uçta, ayırıcı `/ . -`),
tablo kısa biçimi bıraktı ve oyunun yazdığı tarihi gösteriyor. Dosyalar: `cli/fm/Matches.cs`, `docs/BAGIMLILIKLAR.md`,
`docs/FINDINGS.md`.
- Ölçüm — sayfa yazıları (plugin `/texts?anchor=All Competitions&up=10`, `Inf`'ten sonrası), aynı satır:
  `dd/mm/yyyy` → `01/08/2026` · `mm/dd/yyyy` → `08/01/2026` · `yyyy/mm/dd` → `2026/08/01` · `yyyy/dd/mm` → `2026/01/08`.
- Öncesi (`fm player "Ben House" --matches --json`, ayar sırayla değiştirilerek): 19 / 19 / **0** / **0** satır.
  `mm/dd/yyyy` öncesi zengin tablo (`git stash` + eski derleme, `script -q /dev/null` çıktısı): 20 numaralı satır
  `8 Jan | Bristol Rovers | 1 - 2` — maç 1 Ağustos'tu, tarih 8 Ocak yazıldı.
- Sonrası: dördünde de 19 satır (`01/08/2026`, `08/01/2026`, `2026/08/01`, `2026/01/08`); varsayılan `dd/mm/yyyy`'de
  eski çıktıyla birebir aynı (19 satır, ilk `01/08/2026`, son `17/02/2026`).
- Üç durum (TR): `fm open "Messages"` sonrası `fm player "Ben House" --matches --json` (kart ilk kez açılıyor) →
  19 satır; hemen ikinci kez (kart zaten açık) → 19; `fm open "Squad Dynamics"` sonrası (başka sayfa açık) → 19.
  EN'de aynı üç durum, `mm/dd/yyyy` ile (ilk `08/01/2026`) ve `yyyy/mm/dd` ile (ilk `2026/08/01`) → 19 satır.
- İki dil: TR (yukarıdaki üç durum) + EN (üç durum, iki ayrı ayarla); dönüş `fm settings language "Türkçe"` →
  "English → Turkish", tarih ayarı `dd/mm/yyyy` bırakıldı (`fm settings tarih --json` doğrulandı).
- Yan ölçüm (aynı maddenin boy/kilo yarısı): `fm settings boy Fit` sonrası `fm player "Ben House" --json` →
  `Height: 178` (ham cm, ayardan bağımsız); CLI'nin " cm" yazısı veriyle tutuyor, oyunun birim ayarını uygulamak
  Preferences sayfasını açmayı gerektiriyor → kod değişmedi, Faruk'a soru (BAGIMLILIKLAR §C).
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata**. Paylaşılan koda (Bridge/OpenPage/reply) dokunulmadı.
- Kayıt test kaydı: 2 Ağustos 2026; maç oynanmadı, saat ilerletilmedi. Bir ara `fm open "Mesajlar"` /
  `fm open "As Takım"` "no page named" dedi: `fm pages` adları oyunun dilinde gösteriyor, `fm open` oyunun ham
  adını istiyor (`fm open "Messages"`) — not edildi (bu maddenin dışı).

### Tur 6 — C "Maaş dönemi" — ölçüldü, yalnız CLI'nin sözü düzeltildi (§8.194, 18 Ekim)
Ne: Kod davranışı değişmedi. `--wage` / `--term "Wage=…"` tutarı `/setnumber`'a olduğu gibi gidiyor; kutunun dönemi
oyunun kendi "Wages" ayarı (Preferences › Your World) ve kartın gösterdiği dönem. Yardım/MCP metinleri "haftalık"
diyordu, artık "oyunun maaş döneminde" diyor. Dosyalar: `cli/fm/Program.cs` (help TR), `cli/fm/HelpEnglish.cs`
(help EN), `cli/fm/Mcp.cs` (`fm_contract`, `fm_staff_hire`, `fm_staff`), `docs/BAGIMLILIKLAR.md`, `docs/FINDINGS.md`.
- Ölçüm (aynı oyuncu, aynı komut, yalnız ayar değişiyor; `fm contract --term "Wage=5000" --json`):
  - TR + Aylık: `Wage 5.000 £ aylık`, `Cost 6,5B £ aylık` (≈21,7B £/hafta).
  - TR + Haftalık (`fm settings maaş Haftalık`): `Wage 5.000 £ hft`, `Cost 5,25B £ hft`.
  - EN + Aylık (`fm settings dil English`): `Wage 5.000 £ p/m`, `Cost 7,5K £ p/m`.
  - Yani aynı sayı ayara göre haftada ~4,3 kat farklı ücret → gerçek bir "ayar" bağımlılığı (C bölümü).
- Üç durum: sözleşme görüşmesi kartı "sayfa ilk kez açılıyor" (Oisin Gallagher, `contract "<ad>"` → `--next`),
  "zaten açık" (`--term` ile ikinci okuma) ve "başka sayfa açık" (Zane Okoro / Sonny Bradley kartı, arka planda
  başka panel) — üçünde de kutu kartın dönemini gösterdi; negatif yok (kutu dönemsiz gösterilemiyor).
- İki dil: TR (`5.000 £ aylık`, `5.000 £ hft`) + EN (`5.000 £ p/m`). Sonunda Türkçe'ye dönüldü
  (`fm settings language "Türkçe"` → "English → Turkish") ve maaş ayarı `Aylık` bırakıldı.
- Doğrulama: `fm help` (TR + EN) yeni satırı gösteriyor; `fm mcp` `tools/list` 98 araç, JSON geçerli, üç açıklama yeni.
  Paylaşılan davranış koduna dokunulmadı (yalnız yazı) → `sweep.sh` gerekmedi.
- Not (yan bulgular, §8.194): `fm finances` statü satırları aylık ama toplamı haftalık; görüşme kartının "şimdiki
  ücret"i terimlerden başka dönemde olabiliyor; oyunun `DisplayValue` yazıları bağlanınca donuyor (ayar değişince
  eski okumalar eski dönemde kalıyor, gün ilerletmek tazeliyor — 2 Ağu 09:15 → 10:00 denendi).
- Kayıt test kaydı: 2 Ağustos 2026, saat 09:15 → 10:00 ilerletildi; açılan görüşmeler `contract --cancel` ile kapatıldı.

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
- A "`Task.Delay`" / `Staff 74` (18 Ekim, Tur 16): kaydırmadan sonraki sabit 400 ms işarete çevrilemedi — oyun ara
  yerleşimi sakin gösterebiliyor (aynı 20 satır iki okumada) ve kaydırma çapası bir an bulunamıyor; işarete bağlı sürüm
  18 koşunun 2'sinde pencere kaybetti, 400 ms 12/12 tam. **Karar bekleniyor:** (a) 400 ms kalsın (bu makinede pay
  ~1,5–4 kat), (b) kaydırma "görünen son satırın kendi yazısına" çapalanıp liste baştan sona gezilsin (Mutluluk'taki
  gibi; ayrı ve daha büyük bir iş). Ölçüm §8.205. Kod `git restore` edildi, tekrar denemeyin.
- C "Boy birimleri" (Faruk'a soru, 18 Ekim): oyuncunun `Height` alanı ayardan bağımsız, her zaman ham santimetre
  (`fm settings boy Fit` iken de `fm player --json` → `Height: 178`); CLI'nin " cm" yazısı veriyle tutuyor, yani
  bugün yanlış okumuyor. Oyunun birim ayarına uydurmak (Fit seçiliyken fit/inç göstermek) Preferences sayfasını
  açmayı gerektiriyor (~7 sn ve sayfa bağımlılığı; kural 3'e aykırı). Karar bekleniyor: (a) kalsın (veri hep cm,
  yazı da cm), (b) oyunun ayarına uysun (ağır yol göze alınsın), (c) başka bir ucuz yol (oyunun birim
  biçimlendiricisi `/money` gibi bir uç) bulunursa ona bağlansın. Ölçüm §8.195.
- A "Mesaj eki için 1,5 sn" (`WriteAttachment`, Program ~993) — **Karar (Faruk, 16 Ekim): (a), sabit 1,5 sn kaldı**
  (pay ~2,5 kat). İşarete çevirmek için iki ölçüm borcu var: (1) oyunun "ek hazır" işareti (aday
  `#TileBindingExpect`), (2) eki olmayan bir mesajda ağacın/işaretin davranışı (bu kayıtta örnek yok). İkisi
  bulununca sabit bekleme işarete çevrilecek (§8.192).

## Sabitler
- Kapsam: **A** (zamanlama) + **B** (dil/kimlik) + **C** (ayarlar), mümkünse **D**.
- Girmeyeceğiz: §14 UX/görsel, **G** (platform), **H** (yayın).
- Model: opencode-go/deepseek-v4.1-flash · sürücü: tools/autopilot.sh
- Commit: main'e, push yok. Conventional Commits, son satır `Co-Authored-By: OpenCode`.
