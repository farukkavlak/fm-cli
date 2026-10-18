# Otopilot görev defteri — turlar arası hafıza

Her tur başında oku, sonunda güncelle. Taze bir oturum ilerlemeyi buradan alır; aynı maddeyi
iki kez yapmaz, park edileni tekrar denemez.

## Şu an
- Madde: A "Kısa `Until` (≤ 3 sn) — 16 yer" — **alt maddelerine bölündü**, ilki bitti: **Actions 126
  (`AfterAction`'ın konuşma yolu)**. "Konuşma açıldı mı" artık seçimden önceki düğme kümesinden farklı olmasına
  bakıyor; gazete maddesinin cevap düğmeleri (bütçe kartı ekranda) konuşma sanılmıyor. Üç durum + TR/EN,
  gerçek konuşma yolu ve sonuçsuz iş doğrulandı; sweep 50/50 (§8.199).
- ⚠ Tur 9'un (Tur 4 numaralı koşu) işi ağaçta **commit edilmeden** kalmıştı: koşu 40 dk zaman aşımıyla (rc=124)
  öldürülmüş, defter "commit atıldı" yazmış. Tur 10 aynı ölçümleri yeniden koştu, doğruladı ve commitledi.
- Bir sonraki tur: A/`Kısa Until` — sıradaki adaylar: **Budget 70** (tek TR koşusunda her adımda ilk okuma
  yetiyor: `setcontrol` → `form` arası 0,03–0,08 sn, bir adımda 0,24 sn; 3 sn sınırına ≥12 kat pay → "işaret,
  değişiklik gerekmedi" demek için üç durum + iki dil ölçümü borç) · `Saves 207/220` (kaydırma sonrası 1,5 sn ×5,
  "listede yok" diyebiliyor) · `TraitTraining 89` / `TacticSlots 112` (onay penceresi 3 sn içinde gelmezse
  cevaplamadan devam). Alternatif: A/`Sabit üst sınırlar` (Arriving 8 sn, Bound 20 sn, maç akışındaki 3 sn'ler).
- Durum: bitti, commit atıldı

## Ölçülen kanıt (tur tur)

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
