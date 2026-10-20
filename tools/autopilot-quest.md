# Otopilot görev defteri — turlar arası hafıza

Her tur başında oku, sonunda güncelle. Taze bir oturum ilerlemeyi buradan alır; aynı maddeyi
iki kez yapmaz, park edileni tekrar denemez.

## Şu an (Tur 66)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `fm players` arama panelinin Apply/Cancel yedeği** — **BİTTİ** (§8.254).
  Tur 50'de birincil yol (`PanelSent`: `EventFmxFilterRestoreFilter` + `SendsProps True/False`) yazmıştı; ama
  `ApplySearch`in `PanelButton`ı kimlik yoksa `form ?anchorpath=…&buttons=1` içindeki `Text == "Apply"/"Cancel"`e
  düşüyordu. Yedek silindi: Apply `PanelSent(true)`, Cancel `PanelSent(false)`; kimlik yoksa yazıya düşülmüyor,
  Apply dürüst hata veriyor. Böylece `fm players` panelinde basış yolu tamamen sözsüz.
- Kanıt: TR + EN, üç durum (`fm open "Messages"` sonrası / hemen ikinci koşu / `Squad Dynamics` [`Dynamics`] sonrası)
  × iki yol (`fm players "Aaron" --age 18-23 --json` = Apply, `fm players --age 120-130` = Cancel). Apply: TR s3 70
  satır `md5 f85f8ff2…` iki derlemede de aynı; TR s1/s2 22 satır (bilinen kısa tablo, §8.239) isim/sıra/alanlar
  (durum sütunu dışında) **birebir** — yalnız `StatusTitleShort`/`StatusType` koşular arasında oynuyor, iki derlemede
  de. EN s1/s2 **bayt bayt aynı** (`b92e2c6b…`, `253b0c5c…`), s3 70 satır canon aynı (tek fark satır 8'de
  `StatusTitleShort Wnt→Loa`). Cancel: iki dilde üç durumda rc=64, stdout boş `d41d8cd9…`, stderr aynı
  ("En küçük yaş: '120' diye bir seçenek yok…" / "Youngest age: …"), sonraki koşu panelin kapandığını gösteriyor.
  İz (`FM_TRACE=1`): Apply `click ?ptr=0x407e63800`, Cancel `click ?ptr=0x4a9081000`; `text=` yalnız sayfanın kendi
  `press ?text=Clear&event=EventToPlugin`i (dokunulmayan, ayrı iş). Dosyalar `…/opencode/t66/{before-tr,after-tr,
  after-tr2,before-en,after-en}`, betik `t66/run.sh`, iz `t66/trace-{apply,cancel}.err`.
- Dönüş/yan etki: kayıt yalnız **okundu** (2 Ağustos 2026 09:15, saat ilerletilmedi, maç oynanmadı), oyuncu araması
  tur sonunda `--clear` ile süzgeçsiz; dil `fm settings language "Türkçe"` ile geri alındı (`fm settings --json` →
  `Turkish`). `ApplySearch` yalnız `fm players` kullanıyor (`Reads.cs:804`); paylaşılan koda dokunulmadı → `sweep.sh`
  gerekmedi.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 65)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `fm staff find`in `Apply`/`Cancel` yedeği** — **BİTTİ** (§8.253).
  Tur 50'de birincil yol (`PanelSent`: `EventFmxFilterRestoreFilter` + `SendsProps True/False`) yazıdan önceye geçmişti,
  ama kimlik bulunamazsa `?text=Apply` ve `form ?anchor=Apply`deki `Text == "Cancel"` hâlâ yedekti. Yedek silindi:
  Apply event yoksa dürüst hata veriyor, Cancel yalnız `SendsProps=False` düğmesini basıyor. Böylece `StaffFind`te
  yazıyla basma kalmadı (okuma yolu zaten Tur 51'de veri yoluna geçmişti).
- Kanıt: TR + EN, üç durum (`fm open "Messages"` sonrası / hemen ikinci koşu / `Squad Dynamics` [`Dynamics`] sonrası)
  × iki yol (`fm staff find --role Coach --json`, `fm staff find --role "zzz yok"`); her koşu öncesi `--clear`.
  Eski derleme (değişiklik öncesi kurulu Release) ile **birebir**: apply TR `7f4956af…`/`7f4956af…`/`447a8d36…`,
  EN `5b380c79…`/`5b380c79…`/`44f56f55…`; iptal TR `32337789…`, EN `52c49a4e…`, rc 0 / 64. İz (`FM_TRACE=1`):
  yalnız `buttons ?json=1&gen=1` → `click ?ptr=…`; `text=` sayısı 0. Dosyalar `/tmp/t65/{before,after}-{tr,en}/`,
  betik `/tmp/t65/run.sh`. Değişiklik yalnız `cli/fm/StaffSearch.cs` (yerel `Cancel` + Apply), paylaşılan `PanelSent`
  aynı → `sweep.sh` gerekmedi.
- Dönüş/yan etki: kayıt yalnız **okundu** (2 Ağustos 2026 09:15, saat ilerletilmedi, maç oynanmadı); personel arama
  süzgeci tur sonunda temizlendi (`--clear`); dil TR'ye döndü (`fm settings dil` → `Türkçe`).
- Not (ölçüm tuzağı): dil değişimi İngilizce'den dönerken `fm settings dil` değil `fm settings language "Türkçe"`;
  ilk `after-tr` koşusu bu yüzden EN çıktısı verdi, tekrarlandı.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 64)
- Madde: **A "`Task.Delay`" / Propose 91** (`fm propose <oyuncu> <ücret>`: ücret kutusuna yazdıktan sonraki sabit 600 ms)
  — **BİTTİ** (§8.252). `setnumber`dan sonra `FMCombobox.Label` oyunun para yazısına dönüşüp öncekinden farklı olana
  kadar okunuyor (`Until`, 4 sn üst sınır, 0,2 sn); ekranda aynı ücret duruyorsa hiç beklenmiyor.
- Kanıt: etiket `setnumber`dan 188–216 ms sonra para oluyor; 33 ms'teki ilk okuma hâlâ "Unspecified". TR + EN × üç
  durum (başka sayfa / hemen ikinci koşu / başka sayfaya geçtikten sonra) + `0`, `1`, `250000`, `30000000`: çıktı ve
  rc eski derlemeyle **birebir** (`£100B`/`£100K`, `£5M`, `£250B`/`£250K`). Dosyalar:
  `/private/var/folders/22/ltdvnwm95m52zlkknwhg4lzw0000gn/T/opencode/t60/{old,new}-{tr,en}/`, iz `t60/trace-{tr,en}.err`.
- Dönüş/yan etki: kayıt test kaydı; hiçbir teklif gönderilmedi (`--yes` yok), saat ilerletilmedi, maç oynanmadı; dil
  TR'ye döndü (`fm settings language "Türkçe"` → `dil: Türkçe`). Yalnız `cli/fm/Propose.cs` değişti → paylaşılan kod
  yok, `sweep.sh` gerekmedi.
- Kalan (aynı fonksiyon): `Propose 77` (kiralık düğmesi sonrası 1200 ms) — basış dışa vurulan buttons/combobox kümesini
  2,6 sn boyunca hiç değiştirmiyor; işaret için başka bir alan/eklenti bilgisi gerekir.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 63)
- Madde: **A "Kısa `Until`" / TacticSlots 112** (`tactic clear --yes`: silme onayı için sabit 3 sn) — **BİTTİ** (§8.251).
  Eski kod 3 sn boyunca yalnız pencereyi yokluyor, süre dolunca "soru yok" sayıyordu. Artık bekleme oyunun kendi
  cevabı: pencere doldu **veya** yuvanın `TacticCount`'u düştü (`Until`, 8 sn üst sınır, 0,25 sn); pencere geldiyse
  birincil düğmesi basılıyor. Ölçüm: pencere basıştan 0,74–2,39 sn'de geliyordu, yani 3 sn'lik sınır bir koşuda
  ~0,6 sn pay bırakıyordu.
- Kanıt: TR üç durum (yuva zaten açık / `Messages` açık / `Messages`+`Squad` sonrası) + EN üç durum, hepsi rc=0;
  çıktı eski derlemeyle **birebir** (TR `3851a52a…`, `9b2bb072…`; EN `cc748d6e…`, `b92f07e1…`), negatif yuva 9
  rc=1. Dosyalar: `/tmp/t53/{base,after}/{tr,en}-s{1,2,3}-clear.{out,err}`. EN'in bir durumunda "already active"
  satırı koşular arasında oynuyor (3 tekrarda 2/3 yok) — dokunulmayan ilk okumadan, değişikliğe bağlı değil.
- Dönüş/yan etki: kayıt test kaydı; taktik yuvası 3 kopyalanıp boşaltıldı (yuvalar: 1 boş, 2 `fmcli en`, 3 boş),
  saat ilerletilmedi, maç oynanmadı; dil `fm settings language "Türkçe"` ile geri alındı. Paylaşılan koda
  dokunulmadı (yalnız `TacticClear`), `sweep.sh` gerekmedi.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 62)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `fm settings` yazma yolunun onayı** (`Settings.cs`,
  `click ?text=Confirm&name=ConfirmButton`) — **BİTTİ** (§8.251). Preferences'in kendi `ConfirmButton`ı
  `PreferencesConfirmChanges` (+ `PrivacyPolicyConfirmSelections`, `ClosePanel`) gönderiyor; TR + EN'de etiket
  "Confirm" olsa da bu oyunun kendi sözü, başka dilde çevrilebilir. Artık olayı taşıyan açık düğme işaretçisiyle
  basılıyor, bulunamazsa yazıya düşülüyor.
- Kanıt: `fm settings tarih` (TR) / `fm settings "date format"` (EN) ile `mm/dd/yyyy` ↔ `dd/mm/yyyy`; üç durum
  (başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / preferences elle açık bırakılmış) × iki dil.
  Eski derleme (`git stash push -- cli/fm/Settings.cs` + `dotnet build -c Release cli/fm`): altı koşu iki dilde de
  **birebir**, hepsi rc=0 (`dd/mm/yyyy → mm/dd/yyyy` ve tersi). İz: eski `click ?text=Confirm&name=ConfirmButton`
  → yeni `buttons ?json=1&gen=…` + `click ?ptr=0x425b90800`. Dosyalar: `/tmp/t62/{before,after}-{tr,en}/`,
  eski iz `/tmp/t62/before-tr/trace-old.txt`, yeni iz `/tmp/t62/after-tr/s1a.txt`.
- Dönüş/yan etki: kayıt yalnız **okundu** (2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı); tarih biçimi
  `dd/mm/yyyy`de bırakıldı; dil `fm settings dil "Türkçe"` ile geri alındı (`fm settings --json` → `Turkish`), EN
  ölçümü için `English`e geçişte bir kez `fm settings language "English"` denendi ve TR'de ad "dil" olduğu için
  rc=1 aldı (beklenen: yerelleşmiş ad), sonra `dil` ile geçildi. Paylaşılan koda dokunulmadı → `sweep.sh` gerekmedi.
- Not: `fm settings` okuma yolu (`PreferencesTab`) ve diğer ayarlar dokunulmadı; yazma yolunun kalan yazı
  bağımlılıkları (FM menüsü "Manage settings for this game"/"See all", sekme yedeği) ayrı iş.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 61)
- Madde: **B "`contract <oyuncu> --agent`"** (CHECKLIST B, §8.250) — **PARK** (`[?]`): eski not "TR'de kimi koşuda
  seçenek yok, EN'de hep var" diyordu; ölçüm bunu **çürüttü**. Hata dil değil, **açık kalan konuşma / yenileme kartı**
  durumu: aynı oyuncuya arka arkaya ikinci soru ve açık kart varken soru **iki dilde de rc=1**; temiz sayfada ilk
  soru **iki dilde de rc=0**. Kart açıkken oyuncu sayfasının üç `SpeakToAction` satırı `Action=""` (bağlanmamış)
  geliyor ve `SpeakToAction` onları eliyor; `contract --cancel` sonrası da soru bulunmuyor (oyunun yakın-zaman
  kuralı olabilir, karttan ayrıştırılamadı). Yan bulgu: sayfa örtülüyken bir EN koşusu "not our player" verdi
  (`ContractDetails` düğmesi listede yok; `OpenPlayer` önbellekteki kartı isimle kabul ediyor).
- Kanıt (komut + rc, dosyalar `/tmp/t61-*.txt`): TR temiz `"Adam Reach"`, `"Dom Jefferies"`, `"Erik Ring"`,
  `"Freddie Draper"` → rc=0; `"Conor McGrandles"` 1. rc=0 / hemen 2. **rc=1**; `fm contract "George Wickens"`
  rc=0 → `--agent` **rc=1**. EN `"Jack Moylan"` 1. **rc=0**, hemen 2. ve `fm open "Dynamics"` sonrası 3. **rc=1**;
  George kart açıkken rc=1, `contract --cancel` sonrası rc=1. Üç durum: (1) temiz sayfa, (2) hemen ikinci koşu,
  (3) başka sayfa / kart açık. Dil: TR + EN. Dil dönüşü `fm settings language "Türkçe"` (`/state` → turkish).
- Kod değişmedi (kural 4: yeni hâl kanıtlanmış biçimde iyi değil; iki davranış kararı Faruk'un): (a) CLI açık
  konuşmayı kendiliğinden kapatsın mı (menajer/oyuncu üzülebilir), (b) `OpenPlayer` örtülü oyuncu sayfasını öne
  getirsin mi? Kayıt test kaydı; saat 2 Ağustos 2026 14:00, maç yok, kayıt silinmedi.
- Durum: park, commit atılıyor

## Önceki durum (Tur 60)
- Madde: **A "`Task.Delay`" / StaffSearch 407** (`ApplySearch`: `fm players --max-value` figürü listeden seçildikten
  sonraki sabit **1200 ms**) — **BİTTİ** (§8.249). Bekleme oyunun kendi işaretine bağlandı: figürü taşıyan liste
  (choices'ında 7,5M olan, dil/biçim bağımsız bulunan liste) kendi **ham değerini** (`Value`) seçilen figür gösterene
  kadar `Until` (üst sınır 4 sn / 0,25 sn aralık); oturmazsa "… değeri panele oturmadı" + panel kapanır. Seçimin
  kendisi "no events to send" diyor (liste olaysız) — eskisi gibi yok sayılıyor.
- Ölçüm (canlı): `select` dönüşü 37–132 ms ve liste `Value`'yu hedefe **61–131 ms**'de getiriyor → 1200 ms kör
  duraklama. İz (FM_TRACE=1): eski `select ?ptr=…&choice=500B £` 7,42 sn → sıradaki çağrı 8,63 sn (1,21 sn boşluk);
  yeni `select` 7,36 sn → `dropdowns ?json=1` hemen → `buttons ?json=1&gen=1` 7,61 sn.
- Kanıt: `fm players --max-value 500K --json`, üç durum (başka sayfa açık `fm open "Messages"` sonrası / hemen
  ikinci koşu / `fm open "Squad Dynamics"` [EN `Dynamics`] sonrası) × iki dil, eski derleme
  (`git stash push -- cli/fm/StaffSearch.cs` + `dotnet build -c Release`): **oturmuş TR üç durumda da birebir**
  (`md5 b8ffd0a5…`, 70 satır; EN oturmuş `83aed8ac…`). Dil geçişinden hemen sonraki ilk koşular geçici kısa tablo
  veriyor (22 satır) — **eski derlemede de aynı** (§8.239/§8.194); bu yüzden karşılaştırma hem oturmuş md5'lerle hem
  **veri** karşılaştırmasıyla (satır kümesi + bütün alanlar) yapıldı: altı durumda özdeş. `--max-value 20K` yeni
  derlemede 22 satır, hepsi 20B £ sınırının altında. Betik: `…/t60/run.sh`, `checks.py`, klasörler `before-tr`,
  `before2-tr`, `after-tr`, `after2-tr`, `before-en`, `after-en`; izler `/tmp/tr_old.txt`, `/tmp/tr_new.txt`.
- Dönüş/yan etki: kayıt yalnız **okundu** (2 Ağustos 2026 09:15, saat ilerletilmedi, maç oynanmadı); oyuncu arama
  sayfası `fm players --clear` ile süzgeçsiz bırakıldı; dil EN ölçümünden sonra `fm settings language "Türkçe"` ile
  geri alındı (`fm settings dil` → `dil`). `ApplySearch`i yalnız `fm players` çağırıyor (`Reads.cs:804`); yine de
  `sweep.sh` koşuldu.
- Kalan (aynı panelde): `PanelButton("Apply"/"Cancel")`in yazı yedeği (asıl yol `PanelSent`, §8.239) — ayrı alt madde.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 59)
- Madde: **A "`Task.Delay`" / StaffSearch 91 100 110** (`fm staff find --role/--free/--reputation` panelinin listeleri)
  — **BİTTİ** (§8.248). Panelin üç kuralı **seçenek sözüyle** bulunuyordu (`Has(d, "Medical ›")`, `"By Nation"`,
  `"Exceptional"`) ve her seçimden sonra sabit **500 ms** bekleniyordu. Türkçe oyunda çalışma durumu listesinin
  sözleri eklentinin çevirisinden geldiği için "By Nation" diye bir seçenek yok (TR: All/No/At the club/Milli Takım'da)
  → `--free` ve üç kural birlikte **yakalanmamış çökmeyle** düşüyordu (rc=134, `StaffSearch.cs:98`; EN'de çalışıyordu).
- Ne yapıldı: her kural artık oyunun **bağladığı alandan** bulunuyor (`filter.filterrulestaffrole.filtervalue`,
  `filter.filterruleemployed.filtervalue`, `Filter.FilterRuleWorldReputation.FilterValue`), `--employed` dalına
  listenin yokluğu denetimi eklendi (çökme yerine dürüst hata) ve seçimden sonra beklenen şey oyunun kendi cevabı:
  **kuralın listesi seçilen sözde duruyor** (`Until`, üst sınır 6 sn; olmazsa "… listesine oturmadı" + panel kapanır).
- Kanıt (üç durum = `fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` [EN `Dynamics`]
  sonrası; TR + EN; eski derleme `git stash push -- cli/fm/StaffSearch.cs` + `dotnet build -c Release`):
  **EN'de her yol birebir** — `--role "Coach"` 3/3 `44f56f55d7197eeafb0ed19e1caa2098` = eski s3 (eski s1/s2
  `5b380c79…`, tablonun bilinen geçici hâli), `--free` `ebb9ec51337cd439270314e9307432d2` (üç durumda da),
  `--reputation "Nominal"` `816e0cb7a5742c6eb0ba468f58be665e`, **üç kural birlikte** `dc9c9659d32e80f1cd8852dfa4b51aa6`
  (sabit beklemelerin kalktığı yer: eski derlemede de rc=0 ve aynı md5). **TR'de** `--role` 3/3
  `278f3c93afe416f0d6c62353a517cd67` = eski **s3** (eski s1/s2 geçici 21 satır `7f4956af…`), `--reputation` `8830aae3…`
  iki derlemede de; `--free` ve üç kural birlikte **eskiden rc=134 → şimdi rc=0** (70 satır; `--free` üç durumda da
  aynı, TR'nin 70 kişisi EN'ninkiyle aynı küme, üç kural birlikte aynı sırada). Sayfanın kendi sayacı:
  `Aramayı Düzenle (1)` / `Edit Search (1)`. Negatifler iki dilde rc=64; `--views`, `--filters`, `--clear`, `--view`,
  ad süzmesi ve `fm players "Aaron" --attr "Pace>=15"` (aynı dosyadaki dokunulmayan `ApplySearch`) çalışıyor.
- İşaretin ölçümü (canlı, `/dropdowns?json=1`): `select ?ptr=…&fire=1` dönerken liste **zaten** seçileni gösteriyor
  (okuma 186/284 ms, yazı 125/154 ms) ve **0 ms arayla** yapılan iki seçim de oturuyor (4 tur: 178/348/280/693 ms),
  ikinci listenin işaretçisi değişmiyor → 500 ms kör duraklamaydı. Betikler `…/opencode/t59/`
  (`run.sh`, `run2.sh`, `checks.sh`, `probe5/6/8/10.py`); klasörler `before-tr`, `after-tr`, `after-tr-free`,
  `after-tr-free-m`, `before-en`, `after-en`, `after-en-free`, `checks-tr`.
- Dönüş/yan etki: kayıt yalnız **okundu** (2 Ağustos 2026 09:15, saat ilerletilmedi, maç oynanmadı); sayfa süzgeci
  tur sonunda **temizlendi** (`Clear all`); dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı
  (`fm settings dil` → `Türkçe`). Paylaşılan koda (Bridge/`Until`/OpenPage/`ApplySearch`) dokunulmadı → `sweep.sh`
  gerekmedi; aynı dosyadaki `fm players` yolu ayrıca denendi.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 58)
- Madde: **A "`Task.Delay`" / ScoutFocus `Show` (gözlemci seçme penceresinin okuması)** — **BİTTİ** (§8.247):
  pencerenin satırları okunurken her kaydırmadan sonra sabit **400 ms** bekleniyordu ve satır bölgesi oyunun başlık
  sözü **"World Knowledge"** ile bulunuyordu (`SkipWhile`). Ölçüm (canlı, pencere açık): satır kümesi kaydırmadan
  hemen sonra oturmuş (üç konumda ilk okumada aynı, 78/73/87 ms) → bekleme kör duraklamaydı; pencerenin hücreleri
  hiçbir veri yolu taşımıyor (yalnız "Assign Any Scouts" onay kutusu `focusitem.anyscout`a bağlı; başlık hücresi
  `#TIFO-TableHeaderTemplate`/`#data_display-table-heading-default-default`, gövde `StreamedTable #scouts-table` ›
  `DropTarget #View` › `TableRowNavigatable`), ama satırın kendisi 3. hücresindeki "Active Focuses" **sayısıyla**
  işaretli → `Rows()` artık yalnız `texts[i+2]` tamsayı olan yerleri satır sayıyor, hiçbir söz aramıyor.
- Kanıt (üç durum = başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / başka sayfaya geçtikten sonra;
  TR + EN; `fm scout edit 1 --by "ZZZ yok"` iptal yolu okunanları listeliyor): rc hep 64, stdout boş
  (`d41d8cd9…`), stderr **eski derlemeyle birebir** — TR `fc708400b832c3d94035af82890c387f`, EN
  `b11015ec53078666c1abe1499cce0271` (6 isim: Joe Hutchinson · Kenny Morrison · James Sanderson · Finlay McKenna ·
  George Herbert · Simon Rowley). `fm scout --json` koşu öncesi/sonrası aynı (TR `a62e4f7f…`, EN `86fe1bcc…`) →
  odak kurulmadı. Eski derleme `git stash push -- cli/fm/ScoutFocus.cs` + `dotnet build -c Release`; klasörler
  `$TMPDIR/opencode/t58/{new-tr,new-tr2,old-tr,new-en,new-en2,old-en2}`.
- Dönüş/yan etki: kayıt yalnız okundu; 2 Ağustos 2026 **09:15**, saat ilerletilmedi, maç oynanmadı; dil TR'ye döndü
  (`fm settings dil` → `Türkçe`). Tur ortasında FM menüsü açılmaz oldu (§8.236) → `pkill -9` + `fm start` +
  `fm continue` ile kayıt aynı yerden yüklendi, ölçümler tekrarlandı. Paylaşılan koda (Bridge/`Until`/OpenPage)
  dokunulmadı; değişiklik yalnız `cli/fm/ScoutFocus.cs` (yerel `Rows`/`Show`).
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 57)
- Madde: **A "Alt sayfa açmada sabit 1,5 sn"** (`OpenSubpage`, `cli/fm/Program.cs`) — **BİTTİ** (§8.246):
  ana sayfa açıldıktan sonraki `Task.Delay(1.5s)` yerine oyunun kendi işaretleri. Ölçüm (canlı, eklenti, 4 durum):
  ana sayfa istendikten sonra çubuk **0,16–0,46 sn**'de cevap veriyor (`open?guid=<sayfa>&check=1` hata değil
  "not shown" diyor); ondan **önce** basılan satır kaybediliyor (cevap "no page with guid … (a subtab's page: open
  the page whose bar lists it first)") ve kendiliğinden gelmiyor → yeni akış: çubuk cevap verene kadar bekle
  (0,25 sn aralık, üst sınır 6 sn), satırı seç, sayfayı ancak **çubuk o satırda dururken** (`check=1`: "shown")
  **ve** içerik görünürken üst üste iki okumada kabul et; olmazsa 4 kez yinele.
- Kanıt (üç durum = başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / başka sayfaya geçtikten sonra
  `fm open "Squad Dynamics"`; her biri TR + EN): `fm coaches` çıktısı eski derlemeyle **birebir**
  (`md5` TR `241ea50f14b238505fc9df267b725966`, EN `f7af385e323701f2c118b57bb1a62ebc`), süre TR 3,83/2,3/2,3 →
  **2,56/1,28/1,82 sn**, EN 3,83/2,12/2,15 → **3,08/1,27/2,17 sn**; `fm intensity --json` TR + EN üç durumda
  25 satır, `md5` birebir (`1a826717ef5f972ee3b4829327ae7423` / `013460b4dfdb0a41d754d0e332aac2a4`).
  Eski derleme `git stash push -- cli/fm/Program.cs` + `dotnet build -c Release` ile; iz (`FM_TRACE=1`):
  eski `open ?guid=<alt sayfa>` 2,87 sn'de (1,5 sn beklemeden sonra) ve içerik 3,88 sn'de → yeni basış 1,52 sn,
  içerik 2,96 sn. Klasörler: `$TMPDIR/opencode/t57/{new,old}` (`/var/folders/.../T/opencode/t57`).
- Dönüş/yan etki: dil TR'ye döndü (`fm settings language "Türkçe"` → `English → Turkish`, `/state` →
  `Language: turkish`, 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı). Paylaşılan koda
  (OpenSubpage) dokunulduğu için `sweep.sh`: **50 komut, 0 hata** (`…/t57/sweep`).
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 56)
- Madde: **B "Cümle parçası arayanlar" / home-grown (altyapı) kartı** (Program.cs `WriteAttachment`) — **BİTTİ**
  (§8.245): özet dalı kendini `rest[0] == "Homegrown" && rest.Contains("Home-Grown Status")` ile tanıyordu; ama
  kartın ilk sözü **oyunun kendi cümlesi** ("Upcoming Status Changes") ve sütun başlığı canlıda "Homegrown Status"
  (tiresiz) → dal **TR'de de EN'de de** hiç çalışmıyordu, mesaj ham döküm veriyordu; satırlar ayrıca
  `rest[i+1].EndsWith("years old")` ile bulunuyordu (TR "19 yaşında" → hiç eşleşmez).
- Ne yapıldı: kart artık oyunun **kendi bölüm başlığıyla** (`Upcoming Status Changes`) tanınıyor; satırlar oyunun
  bağladığı **veri yolundan** okunuyor — satır `binding.Name` ile başlar, ardından yaş, ülke, kadro, statü, tarih
  gelir; satır **3. `binding` hücresinde kesilir** (kartın yanındaki panel de yazı taşıyor: son satırda o panel
  satıra sızıyordu). Sütunlar sırayla bağlandığı için okuma oyunun kendi işaretiyle yineleniyor: bütün satırlar beş
  hücreli olana kadar (`OpenMessage` ile yeniden okuma, 12 × 250 ms üst sınır) — sabit süre yok.
- Kanıt (canlı, `fm open "Squad Dynamics"` sonrası `fm reply "<konu>"`): **yeni derleme** TR 4 koşunun 3'ünde
  birebir `md5 aa45275675be3b9694c33e268e53631a`, 12/12 satır (`Zane Okoro (19, USA, As Takım) — 30/6/2027 (kulüp 15-21)` …
  `John Blair-Henry (16, ENG, U-18) — …`); EN 3 koşunun 2'sinde birebir `md5 424f3646599cf300e81ad7fedabdce5f`, 12/12
  (`Zane Okoro (19, USA, Senior) — 30/6/2027 (club 15-21)`). **Eski derleme** (`git stash push cli/fm/Program.cs` +
  `dotnet build -c Release`) aynı durumda **hiç satır vermiyor**, başlığı ve yan paneli tek satıra yayıyordu
  (`md5 be2c6235235c02eb0c367f67a27ac1cf` / `00d691986fbc38e117a0ae9ff4ad7d39`).
- Üç durum / dil notu: ölçüm "başka sayfa açık → `reply`" durumunda yapıldı (üç dil-durum eşdeğeri: `fm open` ile
  sayfa değiştirilip mesaj yeniden açılıyor). Kalan oynaklık **paylaşılan** okumadan: `WriteAttachment`ın 1,5 sn'si
  bazen eki hiç yakalamıyor (`Konusunda 'Yetiştirilmiş' geçen mesaj yok` ya da yalnız başlık) — §8.192'nin Faruk
  kararıyla park ettiği iş; bu değişiklik onu iyileştirmiyor, yalnızca **yakalanan** okumada doğru satırları veriyor
  (eski kod yakalanan okumada bile ham döküm veriyordu, yani her hâlde daha iyi).
- Negatif: `fm reply "Kondisyon"` ve veri merkezi mesajları eski derlemeyle **birebir**; `fm settings dil English`
  ile EN ölçüldü, sonunda `fm settings language "Türkçe"` ile dil geri alındı (`fm settings dil` → `Türkçe`).
  Oyunda saat ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı, mesajlar yalnız okundu; `sweep.sh` 50/50 (§8.245).
- Not: değişiklik yalnız `cli/fm/Program.cs` içinde; `reply`in ortak ek okuyucusuna dokunulduğu için `sweep.sh`
  koşuldu (oyun maç arasında).
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 55)
- Madde: **B "İngilizce yazıyla basma/tıklama" / gözlemci seçme penceresinin `Cancel`/`Confirm`i**
  (`AssignFocusScouts` → `Close`) — **BİTTİ** (§8.244): pencere TR oyununda İngilizce gelen "Cancel"/"Confirm"
  yazısıyla bulunuyordu (`FormButton(await Window(), "Cancel"/"Confirm")`), satır düğmelerini seçen `Show` de
  aynı iki yazıyı dışlıyordu. Ölçüm (canlı, TR + EN, `/buttons?json=1&gen=1`, pencere açık): pencerenin iki
  düğmesi kendi olayını gönderiyor — `EventToPlugin:EventFmxRecruitmentFocusScoutAnalystEditCancel, ClosePanel`
  ve `…EditConfirm, ClosePanel` (`Enabled=True`; şablon adı `button-secondary-label` / `button-primary-label`);
  **formun** Cancel/Confirm'i başka olay (`RecruitmentFocusEditConfirmationDialog`) taşıdığı için karışmıyor.
  Artık `EventButton(sent)` olayı taşıyan açık düğmeyi `?ptr=` ile basıyor (kimlik yoksa yazıya düşülüyor) ve
  `Show` satır düğmesini bu iki düğmenin **işaretçisinden** dışlıyor.
- Kanıt: `scout edit 1 --by "ZZZ yok"` (iptal) ve `--by "Kenny Morrison"` (onay), üç durum (`fm open
  "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` [`Dynamics`] sonrası) × iki dil. **EN**:
  eski derleme (`git stash push -- cli/fm/ScoutFocus.cs` + `dotnet build`) ile **birebir** — aynı başlangıç
  durumu (`before-scout` `86fe1bcc76180061e6b35cee1def2ac4`), iptal üç durumda rc=64 + boş stdout
  `d41d8cd98f00b204e9800998ecf8427e`, onay rc=0 ve `190cf72a…` / `00e361e1…` / `82814c43…` (eski derlemenin
  aynısı; yeni derlemenin bir koşusu köprü sustuğu için rc=1 düştü, §8.99). **TR**: iptal üç durumda rc=64 +
  boş stdout (iki derlemede de), onay rc=0 `537180b3b4689595e3dab1cf05232288` (yeni derlemede üç durumda da
  aynı; eski derlemede de rc=0 ama koşular arası hedef odak kaydığı için md5'ler karşılaştırılamaz). İz: eski
  `form ?anchor=Assign Any Scouts&up=8&buttons=1` (yazı) → yeni `buttons ?json=1&gen=1` (olay); basış iki
  derlemede de `click ?ptr=`. Betik: `…/t55/run2.sh` + `…/t55/watch.py` (canlı olay ölçümü); klasörler
  `new-tr`, `new-en3`, `old-en2`, `old-tr`.
- Ölçüm notu (karşılaştırma): iptal yolu durumdan bağımsız (rc=64 + boş stdout), onay yolu ise `scout edit <#>`
  her onayda oyunun eski kaydı listede 'Complete' bırakması yüzünden **listeyi kaydırıyor** — bu yüzden koşular
  arasında aynı numara başka odağı gösteriyor; birebir karşılaştırma EN'de oyun yeniden başlatılıp **aynı
  başlangıç durumundan** koşulduğu için yapıldı. Yan bulgu: sayfa yeni açılınca `ScoutFocuses()` ara sıra listeyi
  boş okuyor (TR'de `scout edit 6` "Odak: '6' diye bir seçenek yok" — §8.242'nin yan bulgusu, iki derlemede de).
- **Kayıt uyarısı:** ölçüm gerçekten odak kurdu/değiştirdi (`England`, `Sky Bet League One`, … Kenny Morrison'a
  atandı; onayların doğal sonucu birkaç 'Complete' odak kaydı daha eklendi; CLI Complete odakları silemiyor).
  Saat ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı; dil EN ölçümünden sonra `fm settings language
  "Türkçe"` ile geri alındı (`/state` → turkish). Oyun bir kez köprü yüzünden sustu (§8.99) → `pkill -9` +
  `fm start </dev/null`, kayıt aynı yerden yüklendi.
- Kalanlar (aynı pencerede): okuma çapası `form ?anchor=Assign Any Scouts` (ölçüldü:
  `anchorpath=focusitem.anyscout&up=8&buttons=1` **0 öğe** döndürüyor, yol çapası tutmuyor) ve `Rows()`in
  başlık sözü ("World Knowledge", bugün TR'de de İngilizce geliyor) — ikisi ayrı iş.
- Not: değişiklik yalnız `cli/fm/ScoutFocus.cs` içinde (yerel `EventButton` + iki çağrı + `Show`); paylaşılan
  koda (Bridge/`Until`/OpenPage) dokunulmadı → `sweep.sh` gerekmedi.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 54)
- Madde: **B "İngilizce yazıyla basma/tıklama" / recruitment focus formunun "form kapandı" işareti** (§8.243) —
  **BİTTİ**: Tur 53 kalan işi şöyle bırakmıştı: `ScoutNew`in `catch` dalındaki "form açık mı" ve Confirm akışındaki
  "form kapandı mı" işaretleri hâlâ `FormButton(FocusForm("Next"/"Back"), "Next"/"Confirm")`, yani **hem** yazı
  çapası (`form ?anchor=Back`) **hem** düğme etiketi. İkisi de artık `FormStep()`: `!= null` (form açık) /
  `== null` (kapandı) — düğmelerin gönderdiği olay (`EventFmxRecruitmentFocusMoveToStep`) ve yük (`One`/`MinusOne`).
  Değişen 3 yer: `ScoutNew` catch, `edit` sonrası "Form kapanmadı" kontrolü, `made` kontrolü.
- Ölçüm (canlı, eklenti + `python3`, **eski derleme**): Confirm'e basmadan önce adım düğmeleri `[Back, Confirm]`,
  `/form?anchor=Back` **47** öğe, odak listesi **5**; tık 0,04 sn'de döndü; ilk yoklamada (0,271 sn) adım düğmeleri
  **boş**, `/form?anchor=Back` **0** öğe ve odak listesi **6** — üçü **aynı anda**; yani iki işaret eşdeğer (eski
  işaret yazıya bakıyordu, yenisi düğmelerin olayına). İkisi de bir yoklama aralığında dönüyor.
- Kanıt: `fm scout edit "<odak>" --name "<yeni>"` (Confirm'e basan yol) **TR 3 durum × 2 koşu + EN 3 durum** → 9/9
  rc=0, çıktı aynı (`Odak güncellendi: …` / `Focus updated: …`); İZDE: Confirm'den sonraki iki yoklama eski
  derlemede `form ?anchor=Back&up=12&buttons=1` (eski derleme izi: 2 kez) → yeni derlemede `buttons ?json=1&gen=1`
  (2 kez); aynı sayıda tur, aynı sonuç. Hata yolu (`fm scout edit 1 --area "ZZZ yok"`) **TR + EN üç durumda**
  rc=64, stdout `d41d8cd9…`, stderr md5 TR `3e18cfaa…` / EN `f66c35f8…` — §8.242'nin kaydıyla **birebir** (o yol
  değişen satırlara girmiyor; dile/çapa regresyon yok). TR s1 bir kez "Odak: '1' diye bir seçenek yok" verdi
  (§8.242'nin yan bulgusu: sayfa yeni açılınca liste boş okunuyor; eski derlemede de var).
- Kalanlar (sıradaki tur): aynı formda hâlâ yazıyla — okuma çapası `form ?anchor=Next`/`Back` (ayrı iş, ölçüldü:
  adım 1/2 ile birebir), `click ?text=Confirm` basışı ve `AbandonFocus`in `click ?text=Cancel`i (ikisi de
  `m_cancelEdit`; `/buttons?json=1` özetinde yok → eklenti değişikliği + oyun restartı ister, park), gözlemci seçme
  penceresinin `Cancel`/`Confirm`i (`Close`, ölçülmedi), `click ?text=Edit Recruitment Focus` (oyun bu düğmeye
  kimlik vermiyor, §8.234).
- **Kayıt uyarısı:** bu turun ölçümü gerçekten odak kurdu/değiştirdi; tarama sayfasında **12 adet** `t54 …`
  artığı kaldı (11 `t54 e*`/`t54 old tr` **Complete**, 1 `t54 e11` **durdurulmuş**). Oyun Complete odakları
  CLI'den silmiyor (`scout remove` → "zaten durmuş"); istenirse oyunun kendi sayfasından silinmeli. Saat
  ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı, dil Türkçe bırakıldı (`/state` → turkish).
- Not: değişiklik yalnız `cli/fm/ScoutFocus.cs` içinde; paylaşılan koda (Bridge/`Until`/OpenPage) dokunulmadı →
  `sweep.sh` gerekmedi (Tur 53'te aynı dosya için 50/50 koşulmuştu).
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 53)
- Madde: **B "İngilizce yazıyla basma/tıklama" / recruitment focus formunun adım geçişi ve adım işaretleri**
  (`scout new`/`scout edit`) — **BİTTİ** (§8.242, Tur 53): Tur 52 `scout areas`in yolunu kimliğe çevirmişti
  (`FocusSteps`); aynı formun `scout new`/`edit` yolu hâlâ yazıyla yürüyordu — "ikinci adım açıldı mı" işareti
  `FormButton(FocusForm("Back"), "Confirm")`, "birinci adım açık mı / mevki oturdu mu" işareti
  `FormButton(FocusForm("Next"), "Next")` + `Enabled`, ileri basışı `click ?text=Next`. Ölçüm: adım düğmeleri iki
  dilde de İngilizce yazıyor ("Next"/"Back") ama okuma çapası da yazı — `/form?anchor=Next` adım 1'de **52 öğe**,
  adım 2'de **0**; `/form?anchor=Back` tersi (adım 2'de 47, adım 1'de 0). Yeni `FormStep()` bunu adım düğmelerinin
  olayından (`FocusSteps`: `EventFmxRecruitmentFocusMoveToStep` + `One`/`MinusOne`, yalnız **açık** olan) okuyor:
  "next" (adım 1) / "back" (adım 2) / null (form yok); ileri basışı da `?ptr=`.
- Kanıt: TR + EN, üç durum (s1 `fm open "Messages"` sonrası / s2 hemen ikinci koşu / s3 `fm open "Squad Dynamics"`
  [EN `Dynamics`] sonrası) × iki yol (`fm scout edit 1 --area "ZZZ yok"`, `fm scout new --position GK --priority top
  --area "ZZZ yok"`) → **eski derleme** (`git stash push -- cli/fm/ScoutFocus.cs` + `dotnet build -c Release`) ile
  **birebir**: hepsi rc=64, stdout boş (`d41d8cd9…`), stderr md5 TR `3e18cfaa…` / EN `f66c35f8…` (s1-new, s2-edit,
  s2-new, s3-edit, s3-new = 5/6 koşu; TR iki kez: `after-tr`, `after-tr2`). İz: eski `click ?text=Next` → yeni
  `click ?ptr=0x…`; adım işaretleri artık `buttons ?json=1&gen=1`den (iki dilde de). Betik:
  `/private/var/…/opencode/t53/run.sh`; klasörler `before-tr`, `after-tr`, `after-tr2`, `before-en`, `after-en`.
- Tek fark (bu turun değişikliğiyle ilgisiz, yan bulgu): s1-edit (`fm open "Messages"` **hemen ardından**
  `scout edit 1`) odak listesini ara sıra **boş** okuyor ("Odak: '1' diye bir seçenek yok") — eski derlemede
  8 koşunun 4'ü, yeni derlemede 4'ün 1'i; `ScoutFocuses()`/`FocusList()` sayfa oturmadan dönüyor (ölçüm bu yüzden
  s2/s3 ile karşılaştırıldı; `probe-before`/`probe-before2`/`probe-after` klasörleri).
- Kalanlar (sıradaki tur önerisi): aynı formda `FormStep()`e taşınmayan işaretler — `ScoutNew`in `catch` dalındaki
  "form açık mı" ve Confirm akışındaki "form kapandı mı" (L~475/690/701; ikisi de Confirm/Cancel'ın park edilmiş
  maddesine bağlı, ölçülmesi Confirm basışı ister); **okuma çapası** `form ?anchor=Next`/`Back` (yazı; ölçüldü:
  adım 1/2 ile birebir — ayrı iş); `AbandonFocus`in `click ?text=Cancel`i ve `click ?text=Confirm` (park:
  `m_cancelEdit`, eklenti + oyun restartı); gözlemci seçme penceresinin `Cancel`/`Confirm`i (`Close`, ölçülmedi).
  A'da kısa `Until`ler (League 216 · MatchControls 131 266 · Reads 892 · TraitTraining 89 · TacticFiles 29 ·
  TacticSlots 112 · Saves 207 220 · StaffSearch 354 · Prep 124 · ScoutFocus 55 · Program 986 1918 2118).
- Sweep: **50 komut, 0 hata** (Türkçe oyunda, `scout` satırı rc=0). Değişiklik yalnız `cli/fm/ScoutFocus.cs`
  içinde; paylaşılan koda (Bridge/`Until`/OpenPage) dokunulmadı.
- Kayıt durumu: kayıt yalnız **okundu**; koşuların hiçbiri Confirm'e basmadı → **odak kurulmadı/değişmedi**
  (`fm scout --json` md5 koşudan önce/sonra aynı: TR `1779d9ad4e9c34f47166fd91e4db0008`, EN
  `9142427a9d97a87532c05ebd8367bf92`); saat ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı; dil EN ölçümünden
  sonra `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish; ilk denemede "The settings page did
  not open." → tekrarlandı, §8.236).
- Not: ağaçta yalnız bu turun dosyası var (`cli/fm/ScoutFocus.cs`) + dokümanlar; `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 52)
- Madde: **B "İngilizce yazıyla basma/tıklama" / recruitment focus formunun ADIM düğmeleri (`Next`/`Back`)** —
  **BİTTİ** (§8.241, Tur 52): formun "açıldı / yarım kalmış" işareti (`OpenFocusForm`) ve `fm scout areas`in bölge
  adımına geçişi düğmenin **yazısıyla** konuyordu (`FormButton(FocusForm("Next"), "Next")`, `click ?text=Next`).
  Ölçüm (`/buttons?json=1&gen=1`, TR): iki adım düğmesi **aynı olayı** gönderiyor
  (`EventToPlugin:EventFmxRecruitmentFocusMoveToStep`, enum 1227976013); ayırt eden **yük** — ileri `SendsProps One`,
  geri `MinusOne` — ve göstermeyen adımın kendi düğmesi `Enabled=False` (adım 2'de ileri düğmesi ekranda duruyor
  ama kapalı). Artık `FocusSteps()` bu olayı + yükü taşıyan **açık** düğmeyi arıyor; bulunamazsa eskisi gibi yazıya
  düşülüyor.
- Kanıt: TR + EN, dört durum (s1 `fm open "Messages"` sonrası / s2 hemen ikinci koşu / s3 `fm open "Squad Dynamics"`
  [EN `Dynamics`] sonrası / s4 **form ikinci adımda bırakılmış** → kalıntı dalı) × `fm scout areas --json`:
  **dört durumda da md5 `fed56895c45431dd9820725651421c2b`**, rc=0, çıktı
  `["Competition ›","Nation ›","Recently Selected ›"]` — **eski derleme (git stash + `dotnet build`) ile birebir**
  (aynı md5, aynı dört durum; `before-tr`/`before-en`). İz: eski `form ?anchor=Next&up=12` + `form ?anchor=Back…` +
  `click ?text=Next` → yeni `buttons ?json=1&gen=1` + `click ?ptr=0x…`; s4'te iki derleme de kalıntıyı görüp
  `click ?text=Cancel` + pencerenin cevabıyla formu atıyor, sonra temizini açıyor. Ek koşu (odağı kurmadan bölge
  adımında düşen yol): `fm scout new --priority top --area "ZZZ yok"` iki derlemede de **rc=64**, aynı mesaj,
  `fm scout --json` md5'i koşudan önce/sonra **aynı** (`9142427a9d97a87532c05ebd8367bf92`, 5 odak) → yeni odak yok.
  Betik: `/private/var/…/opencode/t52/run.sh`; klasörler `before-tr`, `after-tr`, `before-en`, `after-en`,
  `before-en-new`, `after-en-new`.
- Sweep: **50 komut, 0 hata** (Türkçe oyunda, `scout` satırı rc=0); son TR koşusu `fm scout areas --json` aynı md5.
- Kalan (sıradaki tur önerisi): aynı formda **Cancel** ve **Confirm** hâlâ yazıyla — ölçüldü, ikisi de
  `RecruitmentFocusEditConfirmationDialog` olayını taşıyor ve ayırt eden alan (`m_cancelEdit` True/False,
  `/buttons?text=` bloğunda) `/buttons?json=1` özetinde **yok**: JSON'a eklemek **eklenti değişikliği + oyun
  restartı** ister → "Sonraya bırakılanlar"a yazıldı. Ayrıca aynı dosyada kalanlar: `AbandonFocus`in
  `click ?text=Cancel`i, gözlemci seçme penceresinin `Cancel`/`Confirm`i (`Close`), okuma çapası (`form ?anchor=Next`),
  `scout new/edit`in kalan `Next`/`Confirm` basışları (L550/571/575/666/669/680) ve `FormButton(form, "Next"/"Confirm")`
  işaretleri. B'de kalanlar: `row()`in satır adı (oyunun kendi sözü), `contract --agent` kartın işareti, haber
  "Portugal". A'da kısa `Until`ler (League 216 · MatchControls 131 266 · Reads 892 · TraitTraining 89 · TacticFiles 29 ·
  TacticSlots 112 · Saves 207 220 · StaffSearch 354 · Prep 124 · ScoutFocus 55 · Program 986 1918 2118).
- Kayıt durumu: kayıt yalnız **okundu**; ölçüm sırasında Confirm basışının doğal sonucu olarak **1 odak kuruldu**
  (bölge seçilmeden, adı bölge adı "All"/"Hepsi" → listede 5. sırada; düğmenin işaretini ölçerken basıldı, pencere
  açılmadı, form kapandı) — silme yolu olmadığı için (silme Faruk'un kararı) temizlenmedi. Tur 45'in 3 yinelenen
  "Sky Bet League Two" kaydı artık listede **yok** (liste 7 → 5 + bu odak; oyun kendisi temizlemiş görünüyor, bu
  turda ölçülmedi). Saat ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı; dil EN ölçümünden sonra
  `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).
- Not: ağaçta yalnız bu turun dosyası var (`cli/fm/ScoutFocus.cs`) + dokümanlar; `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 51)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `fm players` arama panelinin OKUMA yolu** (`Under`/`Row`,
  StaffSearch.cs 270/349) — **BİTTİ** (§8.240, Tur 51): panel `form ?anchor=Apply&up=8` ile, yani Apply düğmesinin
  **sözüyle** bulunuyordu; kural başlıkları da ekrandaki İngilizce sözle eşleşiyordu. Ölçüm (TR oyun, panel açık):
  `/form?anchorpath=filter.FilterRulePlayerPosition.Name&up=8&buttons=1` ile `/form?anchor=Apply&up=8&buttons=1`
  **birebir aynı 55 öğe**; her kuralın denetimi kendi veri yolunu taşıyor (`Binds`): rol
  `filter.FilterRuleIpRole.filtervalue`, yaş `filter.filterruleage.minvalue`/`.maxvalue`, scout notu
  `filter.FilterRuleScoutingGrade.MinValue`, sözleşme `filter.filterrulecontract.filtervalue`, transfer
  `filter.filterruletransferstatus.filtervalue`, kiralık `filter.filterruleloanstatus.filtervalue`; satırın iki
  denetimi `rule.selectedcompareflag` / `rule.minvalue`. Artık `Under(rule)` **yolu** (`Binds`) eşliyor, çapa
  `anchorpath`, satırın karşılaştırma/kademe denetimi de `Binds` ile; `Reads.cs` `search` listesi yolu taşıyor
  (yazı yalnız kullanıcıya görünen ad olarak kaldı). "Is At Least"/"20" arayışları kalktı.
- Kanıt: TR + EN, üç durum (s1 `fm open "Messages"` sonrası / s2 hemen ikinci koşu / s3 `fm open "Squad Dynamics"`
  [EN `Dynamics`] sonrası), önce = eski derleme (`git stash` + `dotnet build -c Release`), sonra = yeni:
  **`fm players --age 18-23` eskiden rc=1** `error: arama panelinde 'Aged Between (Years)' yok` (üç durumda da boş
  çıktı `d41d8cd9…`) — TR'de başlık "Aged Between **(Yıl Olarak)**" geliyor; **yeni derlemede rc=0**, 73 satır
  (`e810bfd7dfeff0cdfcd63524209a9d8e` s1/s3, `931742e61551b6f85a4992d17f11e265` s2). Birebir kalanlar (md5 iki
  derlemede aynı): TR `--attr "Pace>=15"` `a5e61246cdd29aee99333f1c1325980c` (s1/s3) /
  `93f620f143f5d22f669d09e9cb020c76` (s2), `--grade "B+"` `718b654db8e050583aeb72e67baf7c79` (3/3),
  `--role "Goalkeeper"` `5c72cb45c5355e257ac12ef7da904e4a` (s1/s3), `--max-value 500K`
  `de8b428cd1bb36169092b230fab9882d` (3/3); EN oturmuş `--attr` `6f67b7f5cccc12705268ca33e263b797`, `--age`
  `51c9d225443a12f233df25844fb60c5c`, `--grade` `db9d5183fdfebeef45ba5c98732fba68` — yeni derlemenin s1/s3'ü eski
  derlemenin s3'üyle birebir (zincir: after-en s1 = before-en s3, after-en s3 = before-en s3). rc hep 0.
  İz: eski `form ?anchor=Apply&up=8` → yeni `form ?anchorpath=filter.FilterRulePlayerPosition.Name&up=8`;
  betikler `/private/var/…/opencode/t51/run.sh` (attr/age/grade) ve `run2.sh` (role/max/attr), klasörler
  `before-tr`, `after-tr`, `before-tr2`, `after-tr2`, `before-en`, `after-en`.
- Ölçüm notu: **dil geçişinden hemen sonraki** ilk koşularda tablo yine kısa geliyor (22/25 satır; iki derlemede de
  aynı — Tur 50'nin yan bulgusu, §8.239/§8.194), o yüzden karşılaştırma **oturmuş** durumlarla yapıldı
  (`after-en s1/s3 = before-en s3`). Ayrıca s2'deki farklar tablonun kendi durum sütunu (İzl/Lst) ve hangi süzgecin
  kaldığına bağlı; iki derlemede aynı yerde aynı.
- Kalan (sıradaki tur önerisi): aynı panelde `PanelButton`in **yedeği** hâlâ `Text == "Apply"/"Cancel"` (asıl yol
  `PanelSent`, §8.239) ve `Row()`in satır adı eşleşmesi niteliğin oyunun menüsünden gelen **kendi sözüyle** (sabit
  İngilizce değil) — ikisi de "yazı" sayılırsa ayrı alt madde. B'de kalanlar: `ScoutFocus` formunun
  `Next`/`Confirm`/`Cancel`i (`click ?text=…`), `MatchDay`in üç penceresi (maç günü ister), `contract --agent`
  kartın işareti, haber "Portugal"/"Defence (Core)", `Edit`/`Create a New Recruitment Focus` (kimlik yok),
  `FocusAt`in yarışı (§8.234 sonu). A'da kısa `Until`ler (League 216 · MatchControls 131 266 · Reads 892 ·
  TraitTraining 89 · TacticFiles 29 · StaffSearch 354). Park: Stages 337, TrainingSchedule 345, Clock 179.
- Not (kayıt durumu): kayıt yalnız **okundu**, hiçbir şey yazılmadı/değiştirilmedi; sayfa oyuncu arama süzgeciyle
  bırakıldı (komutların doğal sonucu: son süzgeç duruyor, `--clear` ile temizlenebilir); 2 Ağustos 2026 14:00,
  saat ilerletilmedi, maç oynanmadı; dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı
  (`/state` → turkish). Sweep koşulmadı: değişiklik yalnız `StaffSearch.cs` + `Reads.cs` içinde (`ApplySearch`i
  `fm staff find`in panel yolu ve paylaşılan Bridge/OpenPage **kullanmıyor**); `fm players` sweep'te yok.
- Not: ağaçta yalnız bu turun dosyaları var (`cli/fm/StaffSearch.cs`, `cli/fm/Reads.cs`, BAGIMLILIKLAR, FINDINGS,
  bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 50)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `staff find` arama panelinin `Apply`/`Cancel`i** — **BİTTİ** (§8.239,
  Tur 50): `StaffFind` (112) gönderi `click ?text=Apply` ile, `ApplySearch`in `PanelButton`ı ve `StaffFind`in kendi
  `Cancel`i panelin form okumasından (`Text == "Apply"/"Cancel"`) basıyordu. Ölçüm (TR oyun, panel açık,
  `/buttons?json=1&gen=1`): panelin iki düğmesi **aynı olayı** gönderiyor (`PanelHistoryClose,
  EventToPlugin:EventFmxFilterRestoreFilter`); ayırt eden **gönderilen yük** — Apply `SendsProps True`, Cancel
  `False` (dile/skine bağlı değil; `Name` de farklı, `button-primary-leading-icon-label` / `button-base-label`, ama o
  şablon sınıfı → kural 3 gereği yüke öncelik). Artık `PanelSent(apply)` (StaffSearch.cs) bu olayı ve yükü taşıyan
  düğmeyi `?ptr=` ile basıyor; kimlik yoksa eskisi gibi form okuması + yazı.
- Kanıt: TR + EN, üç durum (başka sayfa açık `fm open "Messages"` sonrası / hemen ikinci koşu / başka sayfaya
  geçtikten sonra) × iki yol (`fm staff find --role "Coach" --json` = Apply, `fm staff find --role "zzz yok"` =
  Cancel), her koşu öncesi `--clear` (aynı süzgeç durumu) → **eski derlemeyle (git stash + `dotnet build`) birebir**:
  stdout `md5` TR `278f3c93afe416f0d6c62353a517cd67` (3/3), EN oturmuş `2ba97887d7c948522efd6e8436749986` (3/3);
  rc 0 / 64, stderr `md5` TR `323377898b24fb0011935d1f72ddaf29` / EN `52c49a4e6158ce3d2d73b48d1f300ccf`. İz:
  eski `click ?text=Apply` → yeni `buttons ?json=1&gen=1` + `click ?ptr=0x…` (altı koşunun altısında; iptal yolu da
  form okumasından yüke geçti — `form ?anchor=Apply&up=8&buttons=1` artık yalnız yedek, bu turda hiç çağrılmadı).
  Betik: `/private/var/…/opencode/t50/run.sh`; ölçüm klasörleri `t50/before-tr`, `before-en`, `before-en2`,
  `after-en`, `after-tr`, `after-tr2`, `after-tr3`, `after-en3`.
- Paylaşılan yol (regresyon): aynı `ApplySearch`i `fm players` de kullanıyor (Reads.cs 802) — `fm players "Aaron"
  --attr "Pace>=15"` iki derlemede **birebir** (`md5 d8cd16daa30d08417f6a34d9007d3e6e`, rc=0); izde eski
  `form ?anchor=Apply&up=8&buttons=1` → yeni `buttons ?json=1&gen=1` + `click ?ptr=…`.
- Ölçüm notu: TR'de dil geçişinden hemen sonraki geçici durum (yukarıdaki yan bulgu) yalnız **yeni** derlemede
  ölçüldü (`7f4956af…`); aynı durumun iki derlemedeki eşliği EN'de kanıtlandı (`5b380c79…` iki derlemede de,
  before-en s1/s2 = after-en3 s1/s2).
- Yan bulgu (yeni, düzeltilmedi — §8.239 sonu): **dil değişiminden hemen sonraki** ilk koşularda personel tablosu
  yalnız süzülmüş satırları veriyor (21), sayfa oturduktan sonra 69 (48 satırın `JobString`i boş) — **eski derlemede
  de aynı** (before-en s1/s2 = `5b380c79…`, after-tr s1/s2 = `7f4956af…`, sonra oturuyor) → bu turun değişikliğiyle
  ilgisi yok. Ayrı iş: süzgeçli tablonun neden 69 satır verdiği ve 48 boş `JobString` (okuma mı, görünüm mü?).
- Kalan (sıradaki tur önerisi): aynı panelin **okuma** yolu (`Under`/`Row`, StaffSearch.cs 262/332) hâlâ
  `form ?anchor=Apply` ile **kelimeyle** çapalı — panel artık `Page`i/yüküyle bulunabildiğine göre çapa da kimlikten
  türetilebilir mi bakılacak. B'de aynı sınıfın kalanları: `ScoutFocus` formunun `Next`/`Confirm`/`Cancel`i
  (`click ?text=…`), `Edit`/`Create a New Recruitment Focus` (kimlik yok, ölçüldü), `FocusAt`in yarışı (§8.234 sonu),
  `contract <oyuncu> --agent` TR aksaklığı (ekranda kalan kart), haber "Portugal"/"Defence (Core)" (örnek yok).
  A'da kısa `Until`ler (League 216 · MatchControls 131 266 · Reads 892 · TraitTraining 89 · TacticFiles 29).
  Park: Stages 337, TrainingSchedule 345, Clock 179.
- Not (kayıt durumu): kayıt yalnız okundu, hiçbir şey yazılmadı; personel sayfasının arama süzgeci (tur başında 1
  kural duruyordu) `--clear` ile boşaltıldı → sayfa artık süzgeçsiz ("Aramayı Düzenle", Clear kapalı); 2 Ağustos 2026
  14:00, saat ilerletilmedi, maç oynanmadı; dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı
  (`/state` → turkish).
- Not: ağaçta yalnız bu turun dosyaları var (`cli/fm/StaffSearch.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 49)
- Madde: **B "İngilizce yazıyla basma/tıklama" / kayıt tarayıcısının `Delete`i** (`saves delete`) — **BİTTİ** (§8.238,
  Tur 49): `DeleteSave` düğmeyi `click ?text=Delete` ile basıyor ve öncesinde `Label == "Delete"` + `Type == "SIButton"`
  + `Enabled` ile bekliyordu. Tarayıcının dört düğmesi aynı şablon ailesinden, etiketler o dilin sözü (§8.236); ayırt
  eden **gönderdikleri olay** — Delete `DeleteSelectedGame` (Tur 47 ölçümünde tarayıcı yeni açıldığında
  `Enabled=False`). Artık yerel `DeleteButton()` bu olayı gönderen **açık** düğmeyi bulup `?ptr=` ile basıyor
  (bekleme + basış tek işaret); kimlik yoksa yazıya düşülüyor.
- Kanıt: kayıt klasörüne **yalnız kendi kopyamız** kondu (`cp "…(v03).fm" "t49 s1.fm"` … `s3` / `e1`…`e3`) → TR + EN,
  üç durum (başka sayfa açık `fm open "Messages"` sonrası / hemen ikinci koşu / başka sayfaya geçtikten sonra) ×
  `fm saves delete "<kopya>" --yes` → altı koşu **eski derlemeyle (git stash + `dotnet build`) birebir** (`md5` TR
  `efb3dc45d615438934915147628fdd84` / `fd3c4108754bada68053788e02c50cb2` / `feb30e749daff812bc17878e36758c0c`,
  EN `ff8c9d93e91ac7dbc5e5fdd30a2358d2` / `da1d75f073736527df7e9a8eddabb831` / `6d5b52d66fc3af5de4e7199afdd62052`),
  rc hepsi 0, dosya gerçekten silindi; izde eski `click ?text=Delete` → yeni `buttons ?json=1` + `click ?ptr=0x…`.
  Negatif: `saves delete "zzz yok" --yes` rc=1 (tarayıcı hiç açılmıyor), yüklü kaydı silme girişimi rc=1 ("şu an
  yüklü olan kayıt; silinmez", tarayıcı Cancel'la kapanıyor). Faruk'un dört kaydı (ana, overwrite backup, v02, v03)
  mtime/boyut olarak değişmeden yerinde; klasörde artık dosya kalmadı.
- Not (tuzak): bir EN koşusu tarayıcıyı açamadı (bilinen `OpenFmMenu`/`OpenSaveBrowser` aksaklığı, §8.236) → tekrar
  edildi, aynı çıktı. İngilizce oyunda `fm open "Squad Dynamics"` **"no page named"** diyor; doğrusu
  `fm open "Dynamics"` (sayfa adı o dilin sözü — ayrı iş, bu turda ölçüldü, koda dokunulmadı).
- Sweep: **50 komut, 0 hata** (Türkçe oyunda). Değişiklik yalnız `cli/fm/Saves.cs` içinde (yerel yardımcı + tek çağrı);
  paylaşılan koda dokunulmadı.
- Not (dönüş): kayıt yalnız kendi kopyalarımız yüzünden kurcalandı (silindi), kayıt okunmadı/yazılmadı; 2 Ağustos
  2026 09:15, saat ilerletilmedi, maç oynanmadı; dil `fm settings language "Türkçe"` ile geri alındı.
- Sıradaki tur (öneri): B'de aynı sınıfın kalan yerleri — yükleme ekranının `Load`u + `LoadTile`/`Back to last saves`
  (oyun restartı ister), `ScoutFocus` formunun `Next`/`Confirm`/`Cancel`i, `StaffSearch` panelinin `Apply`/`Cancel`i,
  `MatchDay`in üç penceresi (`Instant Result`, `Team Selection`, `Injured Players`; maç gününde ölçüm ister),
  `FocusAt`in yarışı (§8.234 sonu). A'da kısa `Until`ler (League 216 · MatchControls 131 266 · Reads 892 ·
  TraitTraining 89 · TacticFiles 29). Sonraya bırakılanlar (ağır) aşağıda.
- Not: ağaçta yalnız bu turun dosyaları var (`cli/fm/Saves.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 48)
- Madde: **B "İngilizce yazıyla basma/tıklama" / onay pencereleri** (`save --as`in "Replace Existing File"i,
  `saves delete`in "Delete File"i) — **BİTTİ** (§8.237, Tur 48): `Saves.cs` `DialogButton` pencereyi **başlık yazısıyla**
  arıyordu. Eklenti (`/dialog`) her pencerenin oyunun kendi adını (`Type`) veriyor; ölçüldü: `ConfirmSaveOverrideDialog`
  ve `confirmDeleteSelectedSave` (DİKKAT: ikincisi küçük harfli) — ikisinin başlığı TR oyunda bile İngilizce, yani yazı
  yolu bugün tesadüfen çalışıyor. Artık `DialogButton(type, title)` önce `Type`e bakıyor, bulamazsa başlığa düşüyor.
- Kanıt: TR + EN, üç durum (başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / `fm open "Squad Dynamics"`
  sonrası) × `save --as "<yüklü kaydın adı>" --yes` → altı koşu **eski derlemeyle birebir** (`md5` TR
  `d4d552c7844b8005649f054ac42d8413`, EN `46212f7f30265749e8e115da9b1f1683`; Tur 47'nin kaydıyla da aynı); izde
  `dialog` → `click ?ptr=0x…`. Silme yolu uçtan uca: kayıt klasörüne **yalnız kendi kopyamız** kondu
  (`cp "…(v03).fm" "t48 test.fm"`) → `fm saves delete "t48 test" --yes` rc=0 "Silindi: t48 test" (iz: `rowselect` →
  `click ?text=Delete` → `dialog` → `click ?ptr=0x…`); Faruk'un dört dosyası değişmeden yerinde. Negatif:
  `save --as` onaysız rc=1, `saves delete "zzz yok" --yes` rc=1 — ikisi de tarayıcıyı açmıyor.
- Sweep: **50 komut, 0 hata** (Türkçe oyunda). Değişiklik yalnız `Saves.cs` içinde (yerel yardımcı, iki çağrı);
  paylaşılan koda dokunulmadı.
- Not (yeni ölçüm yolu): "silme Faruk'un kararı" diye kapanmayan yollar için **kendi kopyanı koy, onu sil** yolu
  işe yarıyor (oyun tarayıcısı klasördeki `.fm` dosyalarını listeliyor; ad satırın yazısı). Tarayıcının `Delete`
  düğmesi hâlâ `click ?text=Delete` → sıradaki alt madde bu yolla ölçülebilir.
- Not (dönüş): kayıtlar okundu + aynı adla üzerine yazıldı + kendi kopyamız silindi; 2 Ağustos 2026 **09:15**,
  saat ilerletilmedi, maç oynanmadı; dil `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).
- Sıradaki tur (öneri): (a) tarayıcının `Delete`i — artık kendi kopyamızla uçtan uca ölçülebilir
  (`DeleteSelectedGame`; §8.236'da kimliği ölçülmüştü); (b) `MatchDay`in üç penceresi (`Instant Result`,
  `Team Selection`, `Injured Players`) `Type` ile — maç gününde ölçüm ister; (c) yükleme ekranının `Load`u +
  `LoadTile`/`Back to last saves` (oyun restartı ister); (d) `ScoutFocus` formunun `Next`/`Confirm`/`Cancel`i.
  A'da kısa `Until`ler (League 216 · MatchControls 131 266 · Reads 892 · TraitTraining 89 · TacticFiles 29).
  Park: Stages 337 (ağır), TrainingSchedule 345, OppositionCard 55, Staff 74.
- Not: ağaçta yalnız bu turun dosyası var (`cli/fm/Saves.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 47)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `save --as` · `saves delete` (kayıt tarayıcısının `Cancel`/`Save`i)** —
  **BİTTİ** (§8.236, Tur 47): `SaveAs` düğmeyi `click ?text=Save`, `CloseSaveBrowser` `?text=Cancel` ile basıyordu.
  Ölçülen kimlikler (TR, tarayıcı açıkken `/buttons?json=1`, panel `008eea55…`): tarayıcının dört düğmesi **aynı
  şablonu** taşıyor (`Name` = `button-base-leading-icon-label`: Browse/Delete/Cancel) → `Name` ayırt etmiyor; ayırt
  eden **gönderdikleri olay**: Cancel `ClosePanel`, Save `SaveGame` (Delete `DeleteSelectedGame`, Browse
  `SetDatastoreValue`). Artık `BrowserButton(save)` önce `Events == SaveGame`i buluyor, Cancel için **aynı `Page`teki**
  `ClosePanel` düğmesi alınıyor; kimlik bulunamazsa eskisi gibi yazıya düşülüyor.
- Kanıt: TR + EN, üç durum (başka sayfa açık `fm open "Messages"` / FM menüsü zaten açık / başka sayfaya geçtikten
  sonra `fm open "Squad Dynamics"`) × iki yol (`saves delete "<yüklü kaydın adı>" --yes` rc=1 "şu an yüklü olan kayıt;
  silinmez" + tarayıcı Cancel ile kapanıyor; `save --as "<aynı ad>" --yes` rc=0 "Kaydedildi: …" + "Replace Existing
  File" onayı) — koşular **eski derlemeyle birebir** (stdout `md5`: TR delete boş `d41d8cd9…`, TR save `d4d552c7…`,
  EN save `46212f7f…`; rc dağılımı aynı); izde eski `click ?text=Cancel` / `?text=Save` → yeni `click ?ptr=0x…`.
  Negatif: `saves delete "zzz yok" --yes` rc=1, `save --as` onaysız rc=1 — ikisi de tarayıcı açmıyor.
  Sweep: **50 komut, 0 hata** (`saves` rc=0, 7 satır). Kayıt yalnız okundu + **aynı adla** üzerine yazıldı (yeni dosya
  açılmadı, yüklü kaydın adı değişmedi); 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı; dil
  `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).
- Not (ölçüm tuzağı): TR oyunda ayarın **adı** da oyunun sözü — geçiş `fm settings dil "English"`, dönüş
  `fm settings language "Türkçe"`. Ayrıca `OpenFmMenu`/`OpenSaveBrowser` ara sıra ilk koşuda tarayıcıyı açmıyor (FM
  menüsü tıklaması hedefi dinlemeden gidiyor; `PreferencesTab` bu yüzden yolu iki kez yürüyor) — ölçüm 4 denemeye
  kadar tekrarladı, ilk TR koşusu 2. denemede geçti; **kod değişmedi** (ayrı iş).
- Sıradaki tur (öneri): aynı sınıfın kalan yerleri — `saves`in `Delete`i (`DeleteSelectedGame`; silme Faruk'un kararı
  olduğu için ölçülemedi) ve yükleme ekranının `Load`u; `ScoutFocus` formunun `Cancel`/`Next`/`Confirm`i (çok adımlı
  form, §8.234 sonundaki `FocusAt` yarışıyla birlikte); `StaffSearch` arama panelinin `Apply`/`Cancel`i. A'da kısa
  `Until`ler (League 216 · MatchControls 131 266 · Reads 892 · Saves 207 220 · TacticFiles 29 · TraitTraining 89).
  Park: Stages 337, TrainingSchedule 345.
- Not: ağaçta yalnız bu turun dosyası var (`cli/fm/Saves.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 46)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `budget` düğmeleri** — **BİTTİ** (§8.235, Tur 46): `fm budget ayarla`
  üç basışı da oyunun İngilizce sözüyle yapıyordu (kartı açan döşeme `Adjust Transfer and Wage Budget`, kartın
  `Got It` ve `Cancel`i). Ölçülen kimlikler: döşeme bir `SpeakToAction`, eylemi **1885623898**
  (`ClubBudgetAdjustmentTransfersAndWages`; yanındaki "Adjust Scouting Budget" 1332374646 → `/buttons?json=1&gen=` +
  `/pull?props=ActionID`); kartın "Got It"i `Events` içinde **EventFmxBudgetAdjustmentConfirm** olayını gönderiyor
  (enum 146), "Cancel" aynı panelin (`Page` = kartın paneli, kapatıp açınca da aynı: `c16a10db…`) yalnız kapatın
  (`ClosePanel`) düğmesi. Artık üçü `click ?ptr=` ile basılıyor; kimlik bulunamazsa eskisi gibi yazıya düşülüyor.
- Kanıt: TR + EN, üç durum (başka sayfa açık / sayfa zaten açık / başka sayfaya geçtikten sonra) × üç yol
  (`ayarla 99` rc=1 hata+Cancel, `ayarla 15` "Bütçe zaten böyle."+Cancel, `ayarla 14` gerçek değişim+Got It, sonra
  `ayarla 15` ile geri) — iki dilde on beşer koşu, çıktılar **eski derlemeyle birebir** (`md5`: TR 99
  `bd1073cf…`, same `29db1cb4…`, 14 `93cbe81d…`, json-after `d0b08a70…`, json-back `2b1645ca…`; EN 99 `c26273fb…`,
  same `f03d9d91…`, 14 `cd196064…`, json-after `f8f717b2…`, json-back `5ffe45d1…`), izde eski `click ?text=…` →
  yeni `buttons` + `click ?ptr=…`, rc dağılımı aynı (15×0, 3×1). Bütçe değişti ve geri alındı: 626B £ / 2.320 £
  aşılmış → 584B £ / 1.517 £ → geri 626B £ / 2.320 £ (EN 626K/584K); saat 2 Ağustos 2026 14:00, maç oynanmadı,
  dil `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish). Sweep: 50 komut, 13 hata — hepsi
  sayfa açılmayan ailede ve tur sonunda oyunun ana iş parçacığı sustu (§8.99); `pkill -9` + `fm start </dev/null`
  sonrası her biri tek başına rc=0, `budget` satırı zaten rc=0.
- Not (ölçüm tuzağı/engel): kartın **okuma** yolu (Wage/Transfer Budget, Scouting, "Budget Adjustment" çapaları)
  yazıyla kalıyor — ölçüldü, o sayıların sayfada veri yolu yok (`/texts?paths=1` yalnız
  `club.RemainingWageBudgetWithText` / `club.CurrentWageSpendingWithText` veriyor); ayrı madde olarak not düşüldü.
- Sıradaki tur (öneri): B'de aynı sınıfın kalan yerleri — `scout` odak formunun `Cancel`/`Next`/`Confirm`i ve
  `saves`in `Cancel`/`Save`i (kimlikleri ölçülüp basılabilir mi), `Edit`/`Create a New Recruitment Focus`
  (kimlik yok, ölçüldü → etiketle kalıyor); **`FocusAt`in yarışı** (etiket + "art arda iki okuma aynı" işareti eski
  odağın düğmelerinde oturuyor: §8.234 sonu, §8.235 sonu değil — ayrı tur). A'da kısa `Until`ler (League 216 ·
  MatchControls 131 266 · Reads 892 · Saves 207 220 · TacticFiles 29 · TraitTraining 89). Park: Stages 337,
  TrainingSchedule 345.
- Not: ağaçta yalnız bu turun dosyaları var (`cli/fm/Budget.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 45)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `scout remove` · `scout restart`** — **BİTTİ** (§8.234, Tur 45):
  odak sayfasının iki düğmesi oyunun çeviri tablosundan gelen İngilizce etiketle bulunuyordu. Ölçüm: ikisi de
  `SpeakToAction` ve eylemleri olay gönderiyor — **Remove = `EventFmxRecruitmentFocusRemove` (53)**,
  **Restart = `EventFmxRecruitmentFocusRestart` (54)** ("Create Recommended Focus" = `1245866784`). Artık düğme
  `SpeakToActionEvent` ile olayından bulunup işaretçisiyle basılıyor (bulunamazsa `remove` eskisi gibi görevler
  panelindeki kaldır simgesine düşüyor). **"Edit Recruitment Focus" ve "Create a New Recruitment Focus" ise
  `ActionID 0` + `Event 0`**: oyun bu ikisine kimlik vermiyor → etiketle kalıyorlar (dil değişse de aynı İngilizce).
- Kanıt: TR üç durum (başka sayfa açık → `fm scout restart 5` rc=0 "Odak yeniden başlatıldı"; sayfa zaten açık →
  `fm scout remove 4 --yes` rc=0 "Odak durduruldu"; başka sayfaya geçtikten sonra → durmuş odağa `remove` rc=1
  "zaten durmuş" + süren odağa `restart` rc=1 "zaten sürüyor"), EN aynı koşular (`Focus restarted` / `Focus stopped`
  rc=0, negatifler rc=1); sonuç `fm scout --json`ten doğrulandı (durdurulan `Stopped/False`, yeni `In Progress/True`).
  `sweep.sh` → **50 komut, 0 hata** (Türkçe oyunda; `scout` satırı rc=0).
- Not (kayıt durumu): ölçüm **kaydı değiştirdi** — odak listesi 4 → 7 oldu: ölçümün doğal sonucu, 3 yinelenen
  "Sky Bet League Two" kaydı eklendi (biri sürüyor, ikisi durmuş; `restart` eski kaydı "durmuş" olarak listede
  bırakıyor). Aynı bölgeyi süren bir odak (#4) duruyor, yani tarama kapsamı bozulmadı; silme yolu olmadığı için
  (silme Faruk'un kararı) temizlenmedi. Tarih 2 Ağustos 2026 14:00 (`/state` → Clock 14), maç oynanmadı, saat
  ilerletilmedi; dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).
- Yan bulgu (yeni, düzeltilmedi — §8.234 sonu): `FocusAt` "süren odak" kararını `Label != "Restart Recruitment
  Focus"` ile veriyor ve bir `restart`tan sonraki ilk `fm scout` iki kez yanlış okudu (1 numaralı "England", gerçekte
  sürerken, "Durduruldu" göründü; sonraki koşuda düzeldi). İşaret "düğme kümesi önceki seçiminkinden **farklı** ve
  sonra iki okumada aynı" olmalı (ya da 53/54 varlığı) — ayrı madde.
- Sıradaki tur (öneri): aynı sınıfın kalan yerleri — `ScoutFocus` `Edit`/`Create a New` (kimlik yok, ölçüldü),
  `Saves` `Save Game As`/`Cancel` (FM menü satırı `FMMenuOptionFromID`; satırın kendi numarası — `SaveGameAs`=2 —
  eklentide `Events` özetine eklenmeden basılamıyor: **eklenti değişikliği + oyun restartı ister**, ya da kabı
  `SaveGameFrame` ama o kap iki düğme taşıyor), `Budget` `Cancel`/`Got It`, `Saves` kayıt tarayıcısı `Cancel`/`Save`.
  A'da kısa `Until`ler (League 216 · MatchControls 131 266 · Reads 892 · Saves 207 220 · TacticFiles 29 ·
  TraitTraining 89) ve `FocusAt`in yarışı. Park: Stages 337 (çapa), TrainingSchedule 345 (ay karışıklığı).
- Not: ağaçta yalnız bu turun dosyaları var (`cli/fm/ScoutFocus.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 44)
- Madde: **A "Kısa `Until`" / Calendar 157** (`fm calendar` / `CalendarMonths`: "Today" düğmesinden sonraki bekleme) —
  **BİTTİ** (§8.233, Tur 44): bekleme "art arda iki okuma aynı ve boş değil" idi ve bu işaret basıştan **önceki** ayı da
  kabul eder. Ölçüm (kimlik): düğme basıldıktan hemen sonraki ilk okuma hâlâ eski ayı veriyor (126 / 95 / 128 ms, üç
  ayrı aydan), yeni ay 198 / 252 / 304 ms'de oturuyor → pay bir okuma aralığından (0,3 sn) az; yavaş makinede iki okuma
  da eski ayı verir ve **bütün ay yanlış aydan sayılır**. Artık işaret oyunun cevabı: değer basıştan öncekinden
  **farklı** ve sonra iki okumada aynı (üst sınır 8 sn); "Today" zaten bugünün ayındayken `Enabled=False` ve basış
  "error: … is disabled" dönüyor → o hâlde hiç beklenmiyor (bugün+bugün yolu 1,0 sn, eskisi gibi).
- Kanıt: TR + EN, üç durum (başka sayfa açık / zaten açık / başka sayfaya geçtikten sonra) + ay elle uzağa
  kaydırıldığında `fm calendar --json` **önce/sonra birebir** (TR `0dd9e2647a65b730035f4f24925b8beb`, EN
  `025897203ed1add7dc0f55748250abc5`; insan çıktısı TR `bcd02554…`, EN `2b1d76e3…`); ay 13/4 iken de aynı md5 ve
  liste 2'ye dönüyor. Aynı yardımcıyı kullanan `fm schedule` ve `fm fixtures` çalışıyor. `sweep.sh`: 50 komut, 0 hata.
- Not (kayıt durumu): kayıt yalnız **okundu**; 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı, hiçbir şey
  yazılmadı; dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish). Tuzak:
  ayar adı da dile bağlı — TR'de ad "dil", EN'de "language"; değer de oyunun kendi sözlüğünden ("English"/"Türkçe").
  Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`calendar`, `fixtures`, `schedule`, `league`, `stages` dahil).
- Sıradaki tur (öneri): A'da kalan kısa `Until`ler — `League 216` (`pathset` bağı, kısa sınır ve sonucu
  denetlenmiyor), `TacticSlots 112`, `TraitTraining 89` (dialog gelmezse "yok say"), `Saves 207 220`,
  `MatchControls 266` (1,5 sn), `Calendar`… hepsi ölçülüp ya işarete bağlanacak ya "pay yeter" diye kapanacak;
  A'da `Task.Delay` kalanları ve `TacticFiles 181` (kapanıştan sonra 800 ms). B'de "yazıyla basma" kalanları
  (ScoutFocus `Cancel`/`Next`/`Confirm`/`Edit-Restart-Remove Recruitment Focus`, Budget, Saves) — her biri oyunda
  ölçülüp öğe adı/olay kimliğiyle basılabilir mi bakılacak; `contract --agent` kartın işareti; haber "Portugal".
- Not: ağaçta yalnız bu turun dosyaları var (`Calendar.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 43)
- Madde: **B "İngilizce yazıyla basma/tıklama" komşusu / `fm prep`** — **BİTTİ** (§8.232, Tur 43): oyunun kendi
  sözüyle gelen odak adları gösteriliyor ama geri kabul edilmiyordu; `--json` da ham İngilizceydi.
- Ne: `cli/fm/Prep.cs`. `SetFocus` seçeneği yalnız ham `Name` ("Progression") üzerinden arıyordu; TR'de insan çıktısı
  "Kaydedilen İlerleme" gösterdiği için yazılan ad "diye bir seçenek yok" (rc=64) alıyordu. Artık `Matching` önce
  gösterilen sözde (`GameWords.Of(c.Name)`), sonra ham İngilizcede arıyor (sayıyla seçme duruyor); hata listesi,
  interaktif liste, "zaten/→" satırı ve `--json` (`Name`/`Means`/`Options`) oyunun sözüyle yazılıyor. EN'de değişen
  satır (`→`) `L()`ya alındı.
- Kanıt (TR, üç durum: `fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` sonrası):
  `fm prep --json` **önce** `md5 50611761b5f34661f55fc4722c919043` (adlar İngilizce: "Progression"), **sonra**
  `e9a22f165f7aaf41c3150d1e3861dc61` (Türkçe: "Kaydedilen İlerleme", "Topa sahipken topu ileri taşımaya odaklanılır."),
  üç koşuda da aynı. `fm prep topla "Kaydedilen İlerleme"` → önce rc=64 ("… diye bir seçenek yok"), sonra rc=0
  "Topla: zaten Kaydedilen İlerleme."; `fm prep topla 2` aynı sonuç (sayı yolu korundu); negatif `fm prep topla zzz`
  → Türkçe seçenek listesi, rc=64. İnsan listesi önce/sonra birebir ("başka: Oyun Kurulumu (öneri) · Üçüncü Bölge").
- İki dil: EN'de `git stash` + eski derleme ile yeni derleme **birebir** (`md5 e9a22f16…`, üç durum). ⚠ Bu kayıtta
  dil değiştikten sonra oyunun bağlı sözleri eski dilde kalıyor (`fm training --position` EN'de "İkinci Forvet"),
  yani EN çıktısı zaten Türkçe geliyordu; yeni kod onu değiştirmiyor (eski derleme de aynı). Temiz EN ölçümü için
  oyunun yeniden açılması gerekir — bu turda yapılmadı, §8.232 sonuna not düşüldü.
- Dönüş: `fm settings language "Türkçe"` → `English → Turkish`, `/state` → `Language: turkish`, final TR koşusu
  aynı `md5`; saat 2 Ağustos 2026 14:00 (ilerletilmedi), maç oynanmadı, hiçbir odak değiştirilmedi (yalnız "zaten"
  yolu ve okuma).
- Regresyon: değişiklik yalnız `Prep.cs` içinde (paylaşılan Bridge/OpenPage/reply yok) → `sweep.sh` gerekmedi;
  yine de tur içinde sweep koşuldu (`/tmp/sweep_t43`): **50 komut, 2 hata** — `health` ("Kadro tablosunda
  'GeneralInfo' görünümü bulunamadı") ve `youth --training` ("Altyapı antrenman tablosu açılmadı"). İkisi de tek
  başına tekrarlandığında **rc=0** veriyor (`fm health`, `fm offers` sonrası da) → arka arkaya koşuda sayfa/görünüm
  durumuna bağlı, bu turun değişikliğiyle ilgisiz; yeni bulgu olarak not edildi (ayrı madde açılmadı: tekrar
  üretilemedi).
- Sıradaki tur (öneri): B'de "haber Portugal/Defence (Core)" (bu turda TR `fm news` ve 15 haber gövdesi tarandı,
  İngilizce sızıntı **görülmedi** — örnek mesaj gerekiyor); `fm prep` gibi "gösterilen ad geri yazılamıyor" başka
  komutlar (aday: `fm training --position` alt menü adları); A'da `TacticFiles 181` (kapanıştan sonra 800 ms),
  diğer kısa `Until`ler; `health`/`youth --training`'in arka arkaya koşudaki görünüm kaybı (ölçüm: sweep'i
  tekrarla, arada sayfa durumunu kaydet).
- Not: ağaçta yalnız bu turun dosyaları var (`Prep.cs`, BAGIMLILIKLAR, FINDINGS, bu defter), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 42)
- Madde: **A "Kısa `Until`" / Stages 337** (`fm stages` kupa turu: her kaydırmadan sonra "iki okuma aynı") — **PARK**
  (§8.231, Tur 42): turun bağları pencerede tam durmuyor (Carabao Cup 1. Tur'da **35 bağ**; pencere 18–25 satır) ve
  liste `to=0..1` ile geziliyor. Gezintinin menzili `"0 of 1401,8"` yazısından okunuyor; o sayı oyunun **kendi dilinin**
  ondalık işaretiyle geliyor (TR virgül) ve `InvariantCulture` onu çözemiyor → TR'de `extent = 0`, yani gezinti **hiç
  çalışmıyor** (yalnız ilk pencere okunuyor). Menzil `GameNumbers.Read` ile çözülüp denendi: gezinti çalışıyor
  (12 `to=` adımı) ama sonuç **kararsız** (TR 35/19/19, EN 35/35/19) — çapa kaydırma ilerleyince kayboluyor
  (elle: `to=0.4` çalıştı, `to=0.5` cevapsız kaldı ve pencere bir daha değişmedi) ve "iki okuma aynı" işareti
  kaydırmadan önceki pencereyi de kabul edebiliyor (Scouting 100 / Matches 82 tuzağı). İkisi birlikte düzeltilmeli.
  Kural 4 gereği kod `git restore cli/fm/Stages.cs` ile geri alındı.
- Kanıt: TR `fm stages "Carabao Cup" "1. Tur" --json` → eski derleme 18 (`--all`) / 25 (aynı durumda 3/3), 0 kaydırma
  adımı; EN `fm stages "Carabao Cup" "First Round" --json` → 24 (3/3); her koşu iki dilde üç durumda (başka sayfa açık
  / hemen ikinci koşu / başka sayfaya geçtikten sonra) aynı md5. Ayrıntı: `### Tur 42` (aşağıda).
- Not (kayıt durumu): kayıt yalnız **okundu**, hiçbir şey yazılmadı/değiştirilmedi; maç oynanmadı, saat ilerletilmedi
  (2 Ağustos 2026 14:00); dil TR (`/state` → turkish). Tuzak: köprü bir kez "the main thread did not run this request
  within 30s" ile sustu → `pkill -9` + `fm start </dev/null` (§8.99), kayıt aynı yerden yüklendi.
- Sıradaki tur (öneri): gezintiyi sağlamlaştır — sabit çapa (satırın işaretçisi ya da listenin kabı) **ve**
  "pencereden farklı **ve** iki okumada aynı" işareti; menzil de `GameNumbers.Read` ile okunsun (üçü birlikte
  kanıtlanınca bu madde kapanır). Alternatif küçük işler: B'de "yazıyla basma"nın kalan yerleri (Budget/Saves/
  ScoutFocus), A'da `TacticFiles 181` (kapanıştan sonra 800 ms) ve diğer kısa `Until`ler; `Reads 114 131` (300 ms'ler
  döngüde yoklama aralığı), `Responsibilities 109` (ayrı dosyada aynı menzil hatası, fallback 6000 olduğu için
  sonucu bozmuyor — §8.231 sonu).
- Not: ağaç temiz (bu turda kod değişikliği bırakılmadı, yalnız doküman), `stash` yok.
- Durum: park edildi, commit atılıyor

## Önceki durum (Tur 41)
- Madde: **A "`Task.Delay`" / Staff 74** (`fm staff`'ın kaydırmalı okuması) — **BİTTİ** (§8.230, Tur 41): her
  kaydırma konumundan sonra sabit 400 ms bekleniyordu. Ölçüm: personel tablosu kaydırıldıktan sonra satırlarını **iki
  adımda** diziyor (`to=1` 6 → 10 → 27 satır, `to=0.75` 13 → 17 → 34, `to=0.5` 21 → 25 → 36, `to=0.25` 27 → 31 → 34);
  yarım hâller 0,1–0,2 sn duruyor ve `/scroll`un bildirdiği konum ilk okumada bile hedefte (`455,9 of 911,8`) → geciken
  şey konum değil diziliş, yani 400 ms'in payı yalnız ~2 kat ve yavaş makinede 6/13/17 satır okunup personel sessizce
  eksiliyordu. Artık her konum, satırlar **0,25 sn arayla üç okumada aynı** olana kadar okunuyor (oyunun kendi
  "diziliş bitti" işareti; `Until`, üst sınır 4 sn). Ölçüm komutu: `/texts?anchor=All Staff&up=12&paths=1` +
  `binding.Name` satırları, `/scroll` sonrası 40–70 ms aralıklı okumalarla.
- Kanıt: TR üç durum (`fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` sonrası) insan
  `md5 4df8a2aa85db8c96b94348fd3a41cd1c`, `--json` `0d24d6ceeaba9cf15553bb4421613e42`; EN üç durum
  `8b2e842f7efbd5ac7dbce534a5ce5ef2` / `50d543a83c2259ce25b069d6fd1a7c8f` — hepsi **eski derlemeyle birebir**,
  48 personel eksiksiz. İzde konum başına dört okuma (4,62 / 4,91 / 5,20 / 5,53 sn). Bedel: komut ~1,5–2 sn yavaş
  (TR 5–8 → 6–8 sn, EN 4–5 → 6–7 sn). `sweep.sh` **50 komut, 0 hata** (Türkçe oyunda; `staff` sweep'te var).
- Geri alma: hiçbir şey değiştirilmedi (komut yalnız okur); dil TR (`/state` → turkish), saat 2 Ağustos 2026 14:00,
  maç oynanmadı, saat ilerletilmedi.
- Not (tuzak): TR ölçümünden önce `fm settings dil "Türkçe"` yanlış komut ("There is no setting called 'dil
  Türkçe'") → ilk "TR" koşuları aslında İngilizceydi; doğrusu `fm settings language "Türkçe"`.
- Regresyon: paylaşılan koda (Bridge/`Until`/OpenPage) dokunulmadı, değişiklik yalnız `cli/fm/Staff.cs` içinde
  (`Staff()`'a yerel `Settled()`); yine de `sweep.sh` koşuldu → 50/50.
- Sıradaki tur (öneri): A'da kalan küçük işler — kısa `Until` listesi (Calendar 157 · League 216 · Prep 124 ·
  Stages 337 · StaffSearch 354 · TacticFiles 29 · TacticSlots 112 · TraitTraining 89 · Program · Reads · Saves) ve
  diğer `Task.Delay`'ler (Clock 179 işsiz kayıt, Coaches 53 88, MatchDay 181 314 368, Transfers 96 128 131 139 …);
  B'de `contract <oyuncu> --agent` kartın işareti, "yazıyla basma"nın kalan yerleri (Budget 4 · Saves 3 · ScoutFocus
  `click text="Create a New Recruitment Focus"`). Ağır listeler: yok.
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 40)
- Madde: **B "`scout new/edit --by` / `--analysts`"** — **BİTTİ** (§8.229): seçim penceresini açan `Edit` düğmesi
  `FormAfter(form, "Assign Scouts", …)` başlığıyla aranıyordu (başlık oyunun cümlesi, TR "Gözlemci Görevlendir (1)")
  → TR'de "Formda scout seçme butonu yok." Artık satır, yanındaki insanın rozetindeki rol sözünden bulunuyor
  (`/translate?words=Scout|Analyst` → "Gözlemci"/"Analist"; bulunamazsa form sırası: gözlemci önce); pencere kutusunun
  bağıyla (`focusitem.anyscout` / `focusitem.anyanalyst`), "kaç seçildi" sayısı oyunun kabından
  (`/texts?within=SIVisible-selected-scouts-number`) doğrulanıyor.
- Kanıt: TR üç durum (18,7 / 19,8 / 25,0 sn) `--by "Kenny Morrison, Simon Rowley"` → "Odak güncellendi: İngiltere · …",
  odak "2 gözlemci"; `--analysts "Jack Coles"` rc=0. EN üç durum (26,4 / 26,0 / 27,9 sn) aynı; negatif TR `--by "ZZZ
  yok"` → rc=64 (EN negatifi köprü sustuğu için rc=1 kaldı). Odak eski hâline döndürüldü, dil TR, saat 2 Ağustos
  14:00, maç yok. `sweep.sh` gerekmedi (`scout` sweep'te yok) — paylaşılan koda dokunulmadı.
- Not (tuzak): uzun oturumda köprü iki kez "the main thread did not run this request within 30s" ile sustu;
  `pkill -9` + `fm start` sonrası aynı komut çalıştı (§8.99).
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılmıştı

## Önceki durum (Tur 39)
- Madde: **B "Yazıyla basma" / `scout new/edit --area`** — **BİTTİ** (§8.228, Tur 39): yazma yolundaki bölge
  döngüsü listeyi hâlâ `FormAfter(second, "Scouting Areas", "dropdown")` ile ve `before` yazısını
  `StartsWith("Scouting Areas")` ile arıyordu; başlık oyunun cümlesi (TR "Gözlemleme Yerleri (0)") → TR'de
  `.Value` yakalanmamış `InvalidOperationException` atıyordu (`scout edit 1 --area "Türkiye"` →
  `Nullable object must have a value`, form açık kalıyor). Artık liste `Binds` ile
  (`possiblescoutingareasdropdownselectedoption`, okuma yoluyla aynı denetim) ve "eklendi" işareti listenin
  **hemen önündeki** başlığın değişmesi (`FormHeadingBefore`) — hiçbir sabit söz aranmıyor. TR üç durum
  (`Squad Dynamics` / hemen ikinci / `Messages`) + EN üç durum: `select` sonrası ilk okumada/0,16 sn'de değişim
  → komut bölge adımını geçti (bölge ekleme `Confirm` edilmedi, 4 odak aynı kaldı); EN eski derlemede birebir.
- Yan bulgu (yeni alt madde açıldı): `scout new/edit --by` / `--analysts` seçim penceresini açan `Edit` düğmesi
  hâlâ `FormAfter(form, "Assign Scouts", …)` başlığıyla bulunuyor; TR'de "Gözlemci Görevlendir (1)" →
  "Formda scout seçme butonu yok." İki `Edit` düğmesi birebir aynı (ad/yazı/olay), ayırt eden formdaki sırası.
- Not (kayıt durumu): kayıt yalnız okundu; hiçbir odak `Confirm` edilmedi, saat ilerletilmedi (2 Ağustos 2026
  09:15), maç oynanmadı, ayar değişmedi; dil TR (`/state` → turkish).
- Sıradaki tur (öneri): yeni alt madde `scout --by/--analysts` (küçük: `Edit` düğmesini formdaki sırasıyla ve
  pencereyi kapsayıcısıyla bulmak — ölçüm gerek); B'de kalanlar `contract --agent` kartın işareti, haber
  "Portugal", "yazıyla basma"nın kalan yerleri; A'da kısa `Until` listesi + sabit üst sınırlar.
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 38)
- Madde: **B "Cümle parçası arayanlar" / `fm scout areas`** — **BİTTİ** (§8.227, Tur 38): Recruitment Focus formunun
  bölge listesini `FormAfter(form, "Scouting Areas", "dropdown")` başlık yazısıyla arıyordu; başlık sayı taşıyan
  oyun cümlesi (TR "Gözlemleme Yerleri (0)") ve tabloda İngilizcesi yok → TR'de `fm scout areas` her durumda rc=1
  "Bölge listesi bulunamadı." Artık denetim formun kendi bağıyla bulunuyor (`Binds` →
  `focusitem.possiblescoutingareasdropdownselectedoption`; `FormBound`). TR üç durum + EN üç durum: aynı üç seçenek,
  aynı `md5 fed56895…` (EN eski derlemeyle birebir); izde `Next` → ilk form okuması 0,07 sn'de bağlı denetimi
  taşıyor; `scout areas "Nation"` → `Europe ›`; negatif aynı. Her koşu formu `Cancel` + onayla kapatıyor, `fm scout
  --json` yine 4 odak (yeni odak yok). İlk TR koşusu dil değişiminden hemen sonra bir kez "Odak formu açılmadı"
  dedi (sayfa geçişi); tekrar ve izli koşuda üç durum da geçti — kanıt o koşular.
- Not (kayıt durumu): kayıt yalnız okundu (2 Ağustos 2026 09:15), saat ilerletilmedi, maç oynanmadı, hiçbir
  ayar/oyuncu değişmedi; dil TR (`/state` → turkish).
- Sıradaki tur (öneri): B'de kalanlar — `scout new/edit --area` aynı başlığı hâlâ yazıyla arıyor (yazma yolu;
  `FormBound` + başlığı denetimin **hemen öncesindeki** yazıdan almak yeter; o fonksiyondaki 400 ms'ler ayrı iş),
  "yazıyla basma"nın kalan yerleri, Program cümle parçaları. **Haber "Portugal"** bu kayıtta yok: ülke maçı haberi
  gerekiyor, yeniden üretilemedi (o yüzden seçilmedi). A'da kalan küçük iş: TacticFiles 181, kısa `Until` listesi
  (Calendar/League/Stages…). Ağır/park: TrainingSchedule 345, Clock 179 (işsiz kayıt).
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 37)
- Madde: **B "Veri merkezi bulguları İngilizce çıktı"** — **BİTTİ** (§8.226, Tur 37): `reply` mesaj ekinin
  veri merkezi dalında `row.AnalysisPointString` cümleleri olduğu gibi yazılıyordu; TR oyunda dört bulgu Türkçe
  (içlerinde sayı var, eklenti çeviremiyor) ama **tabloda birebir duran** cümle İngilizce kalıyordu
  ("We've regained possession well in central areas of our own third of the pitch."). Oysa oyunun tablosunda eş var:
  `/translate?words=…` → "Yarı sahamızın orta kısmında çok top kazandık." (X-Shown başlığı). Artık bulgular
  yazılmadan önce `GameWords.Ask(Fm, points)` ile bir kez soruluyor ve satır `GameWords.Of(point)` ile yazılıyor
  (`GameWordsLines`'ın satır bölmesi "· " yüzünden cümleyi çevirmiyordu). Kanıt: TR üç durumda tam dosya farkı
  yalnız o satır (Türkçe), EN üç durumda bulgular eskisiyle birebir; Türkçe cümleler için tablo boş yanıt veriyor.
- Not (kayıt durumu): kayıt yalnız okundu (2 Ağustos 2026 14:00), saat ilerletilmedi, maç oynanmadı, hiçbir
  ayar/oyuncu değişmedi; dil Türkçeye döndü (`/state` → `turkish`).
- Yan bulgu (§8.226 sonu): mesaj eki okunuşu duruma göre eksik gelebiliyor (ilk koşuda "Goller/Maçın oyuncusu/
  Taktikler" var, ikinci koşuda yok; bir EN koşusunda takım adları boş) — `WriteAttachment` 1,5 sn sabiti yarım
  bağlanmış sayfayı okuyabiliyor; o madde Faruk'un kararıyla sabit kalıyor (§8.192).
- Sıradaki tur (öneri): B'de kalanlar — haber "Portugal" / "Defence (Core)" (haber başlığındaki ülke adı; ölçüm:
  haber yolu eşi öğrenmiyor, `/translate` ile bakılacak), Program cümle parçaları (kaptan eki "to be selected",
  "Transfers In/Out", sezon özeti — örnek mesaj lazım), "yazıyla basma"nın kalan yerleri (Budget/ScoutFocus/
  Saves/Settings `click text=…`), `contract <oyuncu> --agent` (kartın kendi işareti). A'da kalan küçük iş:
  kısa `Until` listesi (League 216 · Prep 124 · Calendar 157 · Stages 349 · StaffSearch 354 · TacticSlots 112 …),
  TacticFiles 181 (panel kapanışından sonra 800 ms), sabit üst sınırlar. Ağır: TrainingSchedule 345 (takvim ayı).
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 36)
- Madde: **B "Cümle parçası arayanlar" / `Release "Is there anything else"`** — **BİTTİ** (§8.225, Tur 36):
  `fm release <oyuncu> --mutual --yes` sonrası konuşmanın asistan cevabından sonra oyun kendi kapanış sorusunu
  ekliyor ve o soru **oyunun dilinde kuruluyor** (TR "Sizin için yapabileceğim başka bir şey var mı hocam?"),
  bu yüzden eski `StartsWith("Is there anything else")` süzgeci TR'de tutmuyor ve soru asistanın cevabı gibi
  yazılıyordu. Artık satır, konuşmanın genel kapatma düğmesinin (`EventFmxClearChanges`, 31) bitirdiği satır
  olarak düşülüyor; metin araması kalktı. Kanıt: TR üç durumda yalnız asistan cevabı (önce iki satır, sonra
  tek), EN üç durumda eskisiyle birebir; negatif listede olmayan oyuncu (Cody Tomlinson, Gael Mande, Adam Reach)
  iki dilde de "seçenek yok" değişmedi (§8.225).
- Not (kayıt durumu): verilen üç karşılıklı fesih teklifi `fm load … --no-save` ile geri alındı; saat
  ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı, dil Türkçeye döndü (`/state` → `turkish`).
- Sıradaki tur (öneri): B'de kalanlar — haber "Portugal" / "Defence (Core)", veri merkezi İngilizce çıktıları,
  "yazıyla basma"nın kalan yerleri, Program cümle parçaları; A'da kalan küçük iş `TacticFiles 181` (panel
  kapanışından sonra sabit 800 ms). **Park (ölçüldü, kod değişmedi):** `contract <oyuncu> --agent` "aynı oyuncu
  bir koşuda rc=0, başka koşuda seçenek yok" şikâyeti koda bağlı değil — sorma hakkı o oturumda tükeniyor
  (aynı oyuncuya ikinci soru rc=1), farklı oyuncu açık konuşma/sözleşme kartı varken de çalışıyor
  (Reeco konuşması açıkken Ben House rc=0; Freddie'nin vaat kartı açıkken Reeco rc=0). Yeni işaret yok,
  eski mesaj ("yakın zamanda konuşulmuş olabilir") duruyor.
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 35)
- Madde: **B "Cümle parçası arayanlar" / `Matches "Match Ratings"+"Inf"`** — **BİTTİ** (§8.224, Tur 35):
  `player <oyuncu> --matches` sayfayı üç İngilizce sözle tanıyordu (bekleme `"Match Ratings"`+`"Inf"`, okuma
  `anchor=All Competitions&up=10`, satır başı `IndexOf("Inf")`). Artık oyunun kendi kabı
  (`/texts?within=PlayerFormCard`; kapalıyken `[]`), satırlar `paths=1` ile veri yoluyla okunuyor ve "yüklendi"
  işareti özetin kendi bağı (`formstats.GamesPlayed`) + satırların oturması; `Rows()` `Inf` aramıyor (numara +
  tarih çifti). Aynı fonksiyonda puan başlığı `"PUAN"` sabitti → `L("PUAN")` (EN'de "RATING"). Kanıt: TR üç
  durum × (Draper 20 maç / kaleci Jeacock / 0 maçlık Pardington) eski derlemeyle **birebir** (json md5
  `6dd18789…`, `a11de1ed…`, `f5fba54c…`), EN `--json` birebir, EN insan çıktısında yalnız başlık düzeldi;
  negatif `player "ZZZ yok" --matches` rc=1; `sweep.sh` **50 komut, 0 hata**.
- Not (kayıt durumu): kayıt yalnız **okundu** (2 Ağustos 2026), saat ilerletilmedi, maç oynanmadı, hiçbir
  ayar/oyuncu değişmedi; dil Türkçeye döndü (`/state` → `turkish`).
- Sıradaki tur (öneri): B'de kalanlar — `Release "Is there anything else"` (karşılıklı fesih konuşması),
  haber "Portugal" / "Defence (Core)", veri merkezi bulguları, "yazıyla basma"nın kalan yerleri. A'da kalan
  küçük iş: `TacticFiles 181` (panel kapanışından sonra sabit 800 ms). Ağır: TrainingSchedule 345.
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 34)
- Madde: **B "Cümle parçası arayanlar" / `Stages "This round will be drawn"`** — **BİTTİ** (§8.223, Tur 34):
  kurası çekilmemiş turun tarihi `StartsWith("This round will be drawn")` ile aranıyor ve tarih öneğin arkasından
  kesiliyordu; cümleyi oyun kurduğu için TR'de "Bu tur 13/8/2026 tarihinde oynanacak" geliyor, önek tutmuyor ve
  not `null` kalıyordu → `fm stages "Carabao Cup" "2. Tur"` TR'de "Bu aşamada gösterilecek bir şey yok." diyordu
  (`--json` → `Note:null`); EN'de doğruydu. Artık turun metinlerinde **dört haneli yıl + ayırıcı** taşıyan sözün
  tarihi alınıyor (`Matches.Rows`un tarih tanıması gibi, sıra oyunun tarih ayarından — §C); İngilizce söz araması
  kalktı. Kanıt: TR üç durumda `Note:"13/8/2026"` + "Kura henüz çekilmedi; çekiliş 13/8/2026.", EN üç durumda
  eskisiyle birebir; TR `--all` 8 turun 6'sına tarih veriyor (3. Tur 27/8 · 4. Tur 3/9 · Çeyrek 30/10 · Yarı
  18/12 · Final 3/2/2027); lig tablosu ve oynanmış turlar değişmedi; negatif `stages … "ZZZ"` aynı liste.
- Not (kayıt durumu): kayıt yalnız **okundu** (2 Ağustos 2026 14:00), saat ilerletilmedi, maç oynanmadı, hiçbir
  ayar/oyuncu değişmedi; dil Türkçeye döndü (`/state` → `turkish`).
- Sıradaki tur (öneri): B'de kalanlar — `Release "Is there anything else"` (karşılıklı fesih konuşması),
  `Matches "Match Ratings"+"Inf"` (§8.208 okuması bu iki söze dayanıyor; TR'de eklenti ikisini de İngilizce
  veriyor), `contract <oyuncu> --agent` TR'de kimi koşuda satır yok (kartın kendi işareti), haber "Portugal" /
  "Defence (Core)", veri merkezi bulguları. A'da kalan küçük iş: `TacticFiles 181` (panel kapanışından sonra
  sabit 800 ms). Ağır: TrainingSchedule 345.
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 33)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `fm release` listesi** — **BİTTİ** (§8.222, Tur 33): liste
  `Label == "Release"` ile aranıyordu, oyunun etiketi **"Set For Release"** (TR'de de) → liste hiç bulunmuyordu ve
  `fm release <oyuncu>` her oyuncuda "serbest bırakma seçeneği yok." diyordu; `--free`/`--mutual` yolu hiç
  açılmıyordu. Artık liste **kabıyla** bulunuyor (`Binds == selectedaction` **ve** `Within` zincirinde
  `PlayerContractCard`; şablonun adı, dilden bağımsız), etiket eşleşmesi yedek kaldı. Aynı fonksiyondaki ikinci
  borç da temizlendi (kural 3): satırlar `select choice=` ile İngilizce sözle seçiliyordu, oysa TR'de oyunun
  satırı "Bedelsiz Serbest Bırak" geliyor (`--free` → "no choice 'Release On A Free'"); satır artık oyunun
  `/translate` ile öğrenilen sözüyle (`Release On A Free` → "Serbest Bırak", satır onunla bitiyor) ya da
  İngilizcesiyle bulunuyor. Kanıt: TR + EN, üç durum (başka sayfa açık / kart zaten açık / başka sayfaya
  geçtikten sonra) → `--free` onaysız her koşuda tazminat penceresi + "Vazgeçildi" (TR "£350B"=350 bin, EN
  "£350K"); izde `select ?choice=Bedelsiz Serbest Bırak&fire=1`; negatif `release "ZZZ yok"` ve listesi olmayan
  oyuncu (Oscar Thorn) değişmedi. `Release.cs` dışına dokunulmadı, `sweep` `release` koşmuyor.
- Not (kayıt durumu): **hiçbir oyuncu serbest bırakılmadı/teklif edilmedi** (`--free`ler onaysız → "Vazgeçildi",
  `--mutual` onaysız → yalnız onay istemi); saat ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı; dil
  Türkçeye döndü (`/state` → `turkish`).
- Sıradaki tur (öneri): B'de kalan cümle parçaları — `Stages "This round will be drawn"` (kupa kurası; sayfada
  yolu yok, draw tarihi veriden gelebilir mi bakılacak), `Release "Is there anything else"` (karşılıklı fesih
  konuşması; artık `release ... --mutual --yes` ile ölçülebilir — dikkat: gerçekten teklif eder, sonra kaydı
  yeniden yükle), `Matches "Match Ratings"+"Inf"` (ölçüldü: TR oyunda eklenti bu iki etiketi İngilizce veriyor).
  A'da kalan küçük iş: `TacticFiles 181` (panel kapanışından sonra sabit 800 ms). Ağır: TrainingSchedule 345.
- Not: ağaç temiz (yalnız bu turun dosyası `cli/fm/Release.cs`), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 32)
- Madde: **B "Cümle parçası arayanlar" / `Saves "Saves automatically" / "Autosave"` · `Saves "Estimated game speed:"`**
  (`fm world`) — **BİTTİ** (§8.221, Tur 32): FM menüsü artık `anchorpath=game.GameTime` ile bulunuyor ve bütün
  değerler veri yoluyla okunuyor (`game.RealTimeSinceLastSave`, `game.GameTimeSinceLastSave`,
  `game.AutosaveModeAndIntervalString`, `game.GameTime`, `game.ActiveNationsCount`/`ActiveLeaguesCount`/
  `EstimatedPlayerCount`, `game.DetailLevel*`, `Person.Name`/`ManagedTeamsString`,
  `app.LastSaveSummary.GameSummaryGameName`); "Saves automatically…", "Estimated game speed:", "Selected Leagues",
  "Save Game" arayışları kalktı. Bekleme "iki okuma aynı **ve** iki değer geldi" (okuma 0,90 → işaret 1,47 sn).
  Tek istisna: menajerin durum rozeti hiçbir yola bağlı değil → etiketle değil, kulüp değerinin **yanındaki** yazı
  olarak alınıyor. TR + EN üç durumda alan alan ve insan çıktısı eski derlemeyle **birebir** (insan `md5` TR
  `899c9313…`, EN `b4399afa…`); `sweep.sh` 50 komut, 0 hata (`world` 2 sn, 6 satır).
- Not (kayıt durumu): kayıt yalnız **okundu**; 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı, hiçbir
  ayar değiştirilmedi; dil Türkçeye döndü (`/state` → `turkish`).
- Sıradaki tur (öneri): B'de kalan cümle parçaları — `Stages "This round will be drawn"` (kura tarihi, sayfada
  yolu var mı bakılacak), `Release "Is there anything else"` (örnek mesaj gerek), `Matches "Match Ratings"+"Inf"`
  (§8.208 okuması bu sözlere dayanıyor). A'da kalan küçük iş: `TacticFiles 181` (panel kapanışından sonra sabit
  800 ms), kısa `Until` üst sınırları. Ağır: TrainingSchedule 345 (takvim ayı karışıklığı).
- Not: ağaç temiz (yalnız bu turun dosyası `cli/fm/Saves.cs`), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 31)
- Madde: **B "İş paneli ekran yazısıyla bulunuyordu"** (Tur 30'un yan gözlemi: ulus ilanlarında `CanApply`
  bilinmiyordu) — **BİTTİ** (§8.220, Tur 31): panel başlıkta kulüp adı (`Contains(job.Club)`) **ve** "Training
  Facilities" satırıyla tanınıyordu; ikisi de dile/kayda bağlı (ulus başlığı oyunun sözüyle: TR "Danimarka
  Menajerlik Görevi", satırın hücresi `team.name` = "Denmark"; ulus panelinde "Training Facilities" yok) →
  `CanApply` `null` kalıyor, `fm jobs "Denmark"` "Başvurmak için: … apply" diyordu ama `apply` haklı olarak
  reddediyordu (düğme `Enabled=False`). Artık panel oyunun kendi kabından: `/texts?within=job-advert-container&paths=1`
  (şablonun adı; kapalıyken `[]`) ve bekleme oyunun kendi işareti — kap **veri yolu taşıyan** metin taşıyor **ve**
  art arda iki okuma aynı (satır seçilince bir an yalnız başlık+iki cevap: 0,259 sn'de 3 metin/0 yol, 0,466 sn'de
  hazır). TR + EN üç durumda `Denmark`/`Fildişi Sahilleri` `CanApply:false` + "Başvuru kapalı…", `United States`
  `CanApply:true`; kulüp ilanı `York` eski derlemeyle birebir (`md5 34c583c8…` TR / `1ee49ee3…` EN); 8–9 → 3–5 sn;
  negatif `jobs "ZZZ yok"` değişmedi.
- Not (kayıt durumu): **hiçbir başvuru/ilgi gönderilmedi** (yalnız okuma), saat ilerletilmedi, maç oynanmadı;
  2 Ağustos 2026, dil Türkçeye döndü (`/state` → `turkish`).
- Sıradaki tur (öneri): **A "`Task.Delay`" / TrainingSchedule 345** — hâlâ takvim ayı karışıklığına takılı (Tur 27
  notu; `General`/`Training` sekmelerinin ayrı `MonthDropdown`'ı). Alternatif küçük işler: A'da `TacticFiles 181`
  (ölçüldü: panel 0,31 sn'de yok, kaydırmalı yolda tetiklemek zor), Guided 161 174 (bedel isteyen söz), Alt
  sayfa/sabit üst sınırlar. B'de kalanlar: `Stages/Saves/Release/Matches` cümle parçaları, `contract --agent` TR
  (kartın kendi işareti), haber "Portugal", "veri merkezi İngilizce çıktı", "yazıyla basma"nın kalan 37 yeri.
- Not: ağaç temiz (yalnız bu turun dosyaları), `stash` yok.
- Durum: bitti, commit atılıyor

## Önceki durum (Tur 28)
- Madde: **B "İngilizce yazıyla basma/tıklama" / `propose` düğmeleri** — **BİTTİ** (§8.217, Tur 28): `fm propose`
  TR oyunda hiç çalışmıyordu (gönder düğmesinin sözü oyunun tablosunda başka metne bağlı); iki düğme artık
  eylem kimliğiyle (`PersonOfferToClubs` 1164992875) ve eylemin gönderdiği olayla (`EventFmxTransferOfferResponse`
  32) bulunuyor. TR üç durum + iki oyuncu aynı çıktı (öncesi rc=1), EN eski derlemeyle birebir; sweep 50/50.
- Not (kayıt durumu): Ben House artık **önerilmiş** (gönderme yolu denendi, §8.94'ün beklenen hâli) — `propose`
  ölçümü bir sonraki turda **başka bir oyuncuyla** yapılmalı (Freddie Draper çalışıyor).
- Bir sonraki tur (öneri): **A "`Task.Delay`" / `TrainingSchedule 345`** (park) — önce takvim ayı karışıklığı ayrı
  madde olarak düzeltilmeli (aşağıda, Tur 27 notu). A'da kalanlar: `TacticFiles 181` · `Jobs 408` · Clock 179
  (işsiz kayıt) · Guided 161 174 (bedel isteyen söz) · Alt sayfa/sabit üst sınırlar.
- B'de kalan (bu turda açılan yan iş): `fm actions` oyuncunun transfer aksiyon listesini göremiyor
  ("No 'Remove From Loan List' in the menu" — oysa düğme ekranda ve `SpeakToAction` listesinde duruyor; §8.217).
- Not: ağaç temiz, `stash` yok; commit atılıyor.
- Durum: bitti

## Önceki durum (Tur 27)
- Madde: **A "`Task.Delay`" / `TrainingSchedule 345`** — **PARK** (§8.216, Tur 27): `fm schedule set`'te seçenek
  basıldıktan sonra açıklamayı okumadan önceki **sabit 500 ms** ölçüldü (yeni açıklama 107–371 ms'de oturuyor, arada
  6 metinlik boşaltılmış bir an 312 ms'de) ve yerine oyunun kendi cevabı ("basıştan önceki hâlden farklı **ve** `About`
  bir şey söylüyor") yazılıp derlendi; TR'de iki durumda eski derlemeyle **birebir** çıktı verdi
  (`Physical → General` `md5 0aaf9592…`, `General → Physical` `md5 414fade6…`). Ama üçüncü durum + İngilizce
  tamamlanamadı: `fm schedule` ara sıra `Takvimin antrenman sekmesi okunamadı.` ile düşüyor — takvim sayfası
  `General` ve `Training` sekmeleri için **ayrı** `MonthDropdown` tutuyor, ikisi ayrı aya bakabiliyor ve
  `CalendarMonths` etiket boş gelince yanlış aya (`value=0` = Haziran) geçiyor (eski derlemede de aynı). Kural 4
  gereği **kod geri alındı** (`git restore cli/fm/TrainingSchedule.cs`), madde `[?]` yapıldı.
- Bir sonraki tur (öneri): **önce takvim ayı karışıklığını düzelt** (ayrı bir madde): `General`/`Training` sekmelerinin
  ayrı `MonthDropdown`'ları var, doğrusu sayfanın **kendi** (Training) listesinden okumak ve etiket boşken seçimin
  tuttuğunu göstergeden doğrulamak; sonra bu madde (TrainingSchedule 345) üç durum + iki dilde kapanır.
  A'da kalanlar: `TacticFiles 181` (panel kapanışından sonra 800 ms) · `Jobs 408` (başvurudan sonra 1 sn) ·
  Clock 179 (işsiz kayıt) · Guided 161 174 (bedel isteyen söz) · Alt sayfa/sabit üst sınırlar.
- Yeni bulgu (§8.216): `select?ptr=&value=` iki ay listesinde de aynı; seans adları (`General`) dil değişiminden sonra
  sayfa yenilenmeyince oyunun o günkü sözüyle geliyor (§8.194'ün devamı) — İngilizce denemede `'General' diye bir
  seans yok` çıktı.
- Not: ağaç temiz, `stash` yok; bu turda kod değişikliği bırakılmadı (yalnız doküman).
- Durum: park edildi, commit atılıyor

## Sonraya bırakılanlar (ağır / ölçümü pahalı — bu koşularda yeniden seçilmez)
Bir listeyi baştan sona kaydırmayı ya da ölçmeyi gerektiren, oyunu yeniden başlatmayı isteyen veya elde
örneği olmayan maddeler. Seçilirse önce buraya bakılır; kanıt toplanabilirse çıkarılır.
- A Stages 337 (`fm stages` kupa turu listesi): gezinti + menzil birlikte düzeltilmeli, kararsız (§8.231).
- A TrainingSchedule 345: önce takvim ayı karışıklığı (General/Training ayrı `MonthDropdown`) düzeltilmeli (§8.216).
- A Clock 179 (`Unemployed`, 2×400 ms): işsiz bir kayıt gerekiyor.
- A `Task.Delay` listesinin taraması (89 yer): tek turda baştan sona sınıflamak ağır; madde madde gidiliyor.
- B yükleme ekranının `Load`u / `LoadTile` / `Back to last saves`: oyun restartı istiyor.
- B `MatchDay`in üç penceresi (`Instant Result`, `Team Selection`, `Injured Players`): maç gününde ölçüm ister.
- B `contract <oyuncu> --agent` TR'de kimi koşuda satır yok: ekranda kalan sözleşme kartının kendi işareti (§8.210 sonu).
- B recruitment focus formunun `Cancel`/`Confirm`i: ikisi de `RecruitmentFocusEditConfirmationDialog` gönderiyor,
  ayırt eden alan `m_cancelEdit` (True/False) yalnız `/buttons?text=` bloğunda, `/buttons?json=1` özetinde yok →
  JSON'a eklemek eklenti değişikliği + oyun restartı ister (§8.241).
- B haber "Portugal" / "Defence (Core)": elde örnek mesaj yok, yeniden üretilemedi (§8.226 öncesi).
- D `up=N` (76 yer) ve sayfa GUID'leri (36 sabit): skin/güncelleme bağımlılığı, baştan sona tarama işi.
- D `Penalties` "MAÇA" sütunu: eklenti alanı ya da maske çevirisi kararı Faruk'un (§8.218).
- B mesaj eki okumasının 1,5 sn'si (`WriteAttachment`): home-grown kartı dahil eki kimi koşuda hiç yakalamıyor
  (`fm reply` boş/eksik) — Faruk'un 16 Ekim kararıyla park; çözülürse §8.245'in kalan oynaklığı da kapanır (§8.192).

## Ölçülen kanıt (tur tur)

### Tur 64 — A "`Task.Delay`" / Propose 91: ücret kutusu sabit 600 ms yerine kendi para yazısını bekliyor (§8.252, 19 Ekim)
Ne: `cli/fm/Propose.cs`. Ücret `setnumber` ile yazıldıktan sonra 600 ms beklenip `FMCombobox.Label` okunuyordu. Artık
okuma oyunun kendi cevabına bağlı: etiket **para yazısına dönüşüp öncekinden farklı** olana kadar (`Until`, 4 sn üst
sınır, 0,2 sn aralık); ekranda önceki ücret duruyorsa ve aynı sayı yazıldıysa hiç beklenmiyor.
- Ölçüm (canlı, yeni derleme): `setnumber` +45 ms; ilk `buttons` 33–50 ms sonra hâlâ "Unspecified"; etiket 188–216 ms'de
  `£100B` / `£100K` oluyor, sonra değişmiyor. İz: `t60/trace-tr.err` `10.81s setnumber` → `10.86s buttons` (Unspecified)
  → `11.09s buttons` (para); `t60/trace-en.err` aynı desen.
- Sonuç: TR + EN × üç durum (s1 `fm open Messages` sonrası / s2 hemen ikinci koşu / s3 `Squad Dynamics`/`Dynamics`
  sonrası) ve `0`, `1`, `250000`, `30000000`: hepsi rc=0, çıktı eski derlemeyle **birebir** (`£100B`/`£100K`, `£100B`,
  `£250B`/`£250K`, `£5M`). Negatif oyuncu iki dilde rc=1; `--loan` iki derlemede aynı.
- Eski derleme: `git stash push -- cli/fm/Propose.cs` + `dotnet build -c Release`, çıktı `/tmp` yerine
  `.../opencode/t60/fm-old-dir` (apphost `fm.dll` yanında olmalı; tek dosya kopyası `fm.dll` arıyor).
- Kayıt/tuzak: kayıt yalnız okundu, hiçbir teklif gönderilmedi (`--yes` yok), saat ilerletilmedi, maç oynanmadı;
  dil TR'ye döndü. Yalnız `Propose.cs` değişti → `sweep.sh` gerekmedi. Kalan: `Propose 77` kiralık düğmesi sonrası
  1200 ms; basış buttons/combobox kümesini 2,6 sn boyunca değiştirmiyor, işaret yok.

### Tur 59 — A "`Task.Delay`" / StaffSearch 91 100 110: personel arama panelinin kuralları veri yolundan bulunuyor, seçimler sabit 500 ms yerine oyunun kendi hâliyle (§8.248, 19 Ekim)
Ne: `cli/fm/StaffSearch.cs` (`StaffFind`). Kural listeleri seçenek sözüyle bulunuyordu (`"Medical ›"`, `"By Nation"`,
`"Exceptional"`) ve her `select`ten sonra `await Task.Delay(500)` vardı. TR oyununda çalışma durumu listesinin
sözleri çeviriden geldiği için ("All/No/At the club/Milli Takım'da") `"By Nation"` hiç yoktu → `--free` (ve üç kural
birlikte) yakalanmamış `InvalidOperationException` ile düşüyordu: rc=134, `StaffSearch.cs:98`; EN'de çalışıyordu.
Yeni: kural `Binds` yolundan bulunuyor, seçim sonrası `Landed(binds, choice)` kuralın listesi o sözde durana kadar
bekliyor (`Until`, üst sınır 6 sn), `--employed` dalına listenin yokluğu denetimi eklendi.
- Ölçüm yolu: `…/opencode/t59/run.sh <etiket> tr|en` (s1 `fm open "Messages"` sonrası, s2 hemen ikinci koşu,
  s3 `fm open "Squad Dynamics"`/EN `Dynamics` sonrası × `--role "Coach"` / `--free` / `--reputation "Nominal"` /
  üçü birlikte / negatifler), `run2.sh` (`--free` üç durumda + sayfanın kendi `Edit Search (n)` sayacı), `checks.sh`.
  Canlı işaret ölçümü: `probe5/6/8.py` (`/dropdowns?json=1`, `/select`), `probe10.py` (EN panelinin yolları/sözleri).
- Sonuç: **EN'de her yol eski derlemeyle birebir** — `--role` yeni 3/3 `44f56f55d7197eeafb0ed19e1caa2098` = eski s3
  (eski s1/s2 `5b380c79…` = bilinen geçici tablo), `--free` `ebb9ec51…` (3 durumda da), `--reputation` `816e0cb7…`,
  üçü birlikte `dc9c9659…`. **TR'de** `--role` 3/3 `278f3c93…` = eski s3 (eski s1/s2 geçici 21 satır `7f4956af…`),
  `--reputation` `8830aae3…` iki derlemede de, `--free` + üçü birlikte **rc=134 → rc=0** (70 satır; TR kümesi = EN
  kümesi, üç kural birlikte aynı sırada). Negatifler rc=64.
- Ölçüm notu: md5 ham JSON metni üzerinden (anahtar sırası oynayabiliyor): `after-tr/free` ile `after-tr-free/f1`
  (aynı süzgeç) md5'leri farklı ama **bütün alanlar birebir** (python ile karşılaştırıldı). Bir TR s3 koşusu
  `6f1bef2e…` verdi: 69 satır aynı, yalnız Nick Adkins'in `StatusTitleShort`/`StatusType` alanları bağlanmamıştı
  (sonraki 3 koşu `278f3c93…`).
- Kayıt/tuzak: kayıt yalnız okundu, sayfa süzgeci sonunda `Clear all` ile temizlendi; saat 2 Ağustos 2026 09:15,
  ilerletilmedi, maç oynanmadı; dil TR'ye döndü (`fm settings dil` → `Türkçe`). Paylaşılan koda dokunulmadı.
- Kalan (aynı ailede, sıradaki tur): `ApplySearch` 438 (`fm players --max-value` seçiminden sonra sabit 1200 ms) ve
  `PanelButton("Apply"/"Cancel")`in yazı yedeği; `StaffSearch.cs` yalnız bu dosyada değişti.

### Tur 58 — A "Task.Delay" / ScoutFocus `Show`: gözlemci penceresinin okuması sabit 400 ms yerine satırların oturması, başlık sözü yerine satırın kendi sayısı (§8.247, 19 Ekim)
Ne: `cli/fm/ScoutFocus.cs`. `Show()` her kaydırmadan sonra `await Task.Delay(400)` yapıyordu; `Rows()` satır bölgesini
`view...SkipWhile(t => t != "World Knowledge").Skip(1)` ile buluyordu. Ölçüm (canlı, pencere açık): 58 öğe, hücrelerin
hiçbiri veri yolu taşımıyor (yalnız `focusitem.anyscout` onay kutusu bağlı); başlık hücresi `Button` ›
`TemplateContainer #TIFO-TableHeaderTemplate` › `#data_display-table-heading-default-default`, gövde satırları
`StreamedTable #scouts-table` › `DropTarget #View` › `TableRowNavigatable`. Satırın 3. hücresi "Active Focuses"
sayısı olduğundan söz araması gereksizdi; kaydırma sonrası satır kümesi ilk okumada oturuyor (78/73/87 ms) → bekleme
kör duraklamaydı. Yeni: satırlar art arda iki okumada aynı olana kadar okunuyor (`Until`, üst sınır 2 sn), `Rows()`
tüm sözleri tarayıp `texts[i+2]` tamsayı olan yerleri satır sayıyor.
- Ölçüm yolu: `/private/var/…/opencode/t58/run.sh <etiket> TR|EN` → s1 `fm open "Messages"` sonrası, s2 hemen ikinci
  koşu, s3 `fm open "Squad Dynamics"` (EN `Dynamics`) sonrası × `fm scout edit 1 --by "ZZZ yok"` (iptal; okunan
  gözlemcileri stderr'e yazıyor), her koşunun öncesi/sonrası `fm scout --json`. Ek betikler: `probe*.py` (pencere
  DOM'u ve kaydırma ölçümü), `cleanup.py` (pencereyi kapatma).
- Sonuç: **eski derleme** (`git stash push -- cli/fm/ScoutFocus.cs` + `dotnet build -c Release`) ile **birebir** —
  rc hep 64, stdout boş `d41d8cd98f00b204e9800998ecf8427e`, stderr md5 TR `fc708400b832c3d94035af82890c387f`
  (new-tr 3/3, old-tr 3/3, new-tr2 3/3), EN `b11015ec53078666c1abe1499cce0271` (new-en 3/3, new-en2 3/3,
  old-en2 3/3); liste 6 isim. `scout --json` koşu öncesi/sonrası aynı (TR `a62e4f7f…`, EN `86fe1bcc…`).
- Kayıt/tuzak: kayıt yalnız okundu, 2 Ağustos 2026 09:15, saat ilerletilmedi, maç oynanmadı; FM menüsü tur ortasında
  açılmaz oldu (§8.236) → `pkill -9` + `fm start </dev/null` + `fm continue` (kayıt aynı yerden yüklendi), ölçümler
  o durumda tekrarlandı; dil TR (`fm settings dil` → `Türkçe`). `sweep.sh` gerekmedi: değişiklik yalnız
  `ScoutFocus.cs` içinde (yerel `Rows`/`Show`), paylaşılan koda dokunulmadı.

### Tur 54 — B "yazıyla basma" / recruitment focus formunun "form kapandı" işareti: artık Confirm/Cancel yazısı ve `?anchor=Back` çapası değil, adım düğmelerinin olayı (§8.243, 19 Ekim)
Ne: `cli/fm/ScoutFocus.cs` — üç yer. `ScoutNew`in `catch` dalı: `FormButton(FocusForm("Next"), "Next") != null ||
FormButton(FocusForm("Back"), "Confirm") != null` → `await FormStep() != null`. `edit` akışı ve `made` kontrolü:
`FormButton(await FocusForm("Back"), "Confirm") == null` → `await FormStep() == null`. Böylece "form açık mı"
işareti **hem** yazı çapasından (`form ?anchor=Next`/`Back`) **hem** düğme etiketinden ("Next"/"Back"/"Confirm")
kurtuldu; düğmelerin gönderdiği olay + yük söylüyor (`EventFmxRecruitmentFocusMoveToStep`, `One`/`MinusOne`).
- Ölçüm (canlı, `python3` + eklenti, eski derleme): Confirm'e basmadan önce `[Back, Confirm]`, `/form?anchor=Back`
  **47** öğe, odak listesi **5**; tık 0,04 sn'de döndü; **0,271 sn**'deki ilk yoklamada üçü de boş/sıfır/artmış
  (düğmeler `[]`, form **0**, liste **6**) → iki işaret aynı anda dönüyor, eşdeğer.
- Doğrulama (sonra, `fm scout edit "<odak>" --name "<yeni>"`, Confirm'e basan yol): TR 3 durum × 2 koşu + EN 3
  durum = 9 koşu, hepsi rc=0 ve çıktı aynı (`Odak güncellendi: …` / `Focus updated: …`); izde Confirm'den sonraki
  iki yoklama eski derlemede `form ?anchor=Back&up=12&buttons=1`, yenide `buttons ?json=1&gen=1` (ikisi de 2 tur).
- Hata yolu (dokunulmayan satırlar, regresyon kontrolü): `fm scout edit 1 --area "ZZZ yok"` TR + EN üç durumda
  rc=64, stdout `d41d8cd9…`, stderr md5 TR `3e18cfaa…` / EN `f66c35f8…` = §8.242 ile birebir. TR s1 bir kez
  "Odak: '1' diye bir seçenek yok" (§8.242 yan bulgusu, sayfa yeni açılınca liste boş okunuyor).
- Kayıt: 12 `t54 …` artığı (Complete/durdurulmuş) oyunun odak listesinde kaldı; CLI Complete odağı silmiyor.
  Saat ilerletilmedi, maç oynanmadı, dil Türkçe (`fm settings language "Türkçe"`; not: EN'e geçerken ayar adı
  Türkçe oyunda `dil`: `fm settings dil English`).
- Betikler/izler: `/tmp/t54/` (`rename.sh`, `neg.sh`, `*.out`, `*.err`).

### Tur 53 — B "yazıyla basma" / recruitment focus formunun adım geçişi + adım işaretleri (`scout new/edit`) (§8.242, 19 Ekim)
Ne: `cli/fm/ScoutFocus.cs`. Tur 52 `scout areas`in ileri basışını `FocusSteps`e (olay + yük) taşımıştı; aynı formun
`scout new`/`scout edit` yolu yazıyla yürüyordu — `FormButton(FocusForm("Back"), "Confirm")` (adım 2 açıldı),
`FormButton(FocusForm("Next"), "Next")` + `Enabled` (adım 1 açık / mevki oturdu), `click ?text=Next` (ileri).
Ölçüm (TR): `/form?anchor=Next` **adım 1'de 52 öğe**, adım 2'de **0**; `/form?anchor=Back` tersi (47 / 0) → çapa da
yazı. Yeni `FormStep()` adım düğmelerinin olayından (`EventFmxRecruitmentFocusMoveToStep` + `One`/`MinusOne`, yalnız
açık olan) "next"/"back"/null okuyor; dört çağrı yeri + ileri basışı `?ptr=` (kimlik yoksa yazıya düşülüyor).
- Ölçüm yolu: `/private/var/…/opencode/t53/run.sh <etiket> TR|EN` → s1 `fm open "Messages"` sonrası, s2 hemen ikinci
  koşu, s3 `fm open "Squad Dynamics"` (EN `Dynamics`) sonrası × iki yol (`scout edit 1 --area "ZZZ yok"`,
  `scout new --position GK --priority top --area "ZZZ yok"`), artı iz için `FM_TRACE=1` iki koşu.
- Sonuç: **eski derleme** (`git stash push -- cli/fm/ScoutFocus.cs` + `dotnet build -c Release`) ile **birebir** —
  hepsi rc=64, stdout boş (`d41d8cd98f00b204e9800998ecf8427e`), stderr md5 TR `3e18cfaa975961217a55fda58612fb7e`,
  EN `f66c35f888ad4351f12861c34e6f6950` (5/6 koşu: s1-new, s2-edit, s2-new, s3-edit, s3-new; TR iki tam koşu
  `after-tr` + `after-tr2`). Fark yalnız `s1-edit`te: `fm open "Messages"` hemen ardından `scout edit 1` odak
  listesini ara sıra boş okuyor ("Odak: '1' diye bir seçenek yok") — ayrı ölçüm: eski derleme 8 koşunun 4'ü
  (`probe-before2`), yeni derleme 4'ün 1'i (`probe-after`) → sayfa oturma yarışı, değişiklikle ilgisiz.
- İz (TR, `trace-new`): eski `click ?text=Next` → yeni `buttons ?json=1&gen=1` + `click ?ptr=0x51f02d800`; EN aynı
  (eski `click ?text=Next` → yeni `click ?ptr=0x521894800`). Odak listesi koşudan önce/sonra aynı md5
  (TR `1779d9ad4e9c34f47166fd91e4db0008`, EN `9142427a9d97a87532c05ebd8367bf92`) → **yeni odak kurulmadı**.
- `sweep.sh`: 50 komut, 0 hata (Türkçe oyunda). Kayıt yalnız okundu; saat 2 Ağustos 2026 14:00, maç oynanmadı;
  dil geri Türkçeye alındı (`/state` → turkish). Kalan: Confirm basışı + Confirm akışındaki "form kapandı" işareti
  (park: `m_cancelEdit`), okuma çapası `form ?anchor=Next`/`Back`.

### Tur 52 — B "yazıyla basma" / recruitment focus formunun adım düğmeleri: `Next`/`Back` artık gönderdikleri yükle (`One`/`MinusOne`) bulunuyor (§8.241, 19 Ekim)
Ne: `cli/fm/ScoutFocus.cs`. `OpenFocusForm`in "form açıldı / yarım kalmış" işareti
`FormButton(await FocusForm("Next"), "Next") != null || FormButton(await FocusForm("Back"), "Confirm") != null` idi ve
`ScoutAreas` adımı `click ?text=Next` ile geçiyordu. Ölçüm (form açıkken `/buttons?json=1&gen=1`): adım 1'in `Next`i ve
adım 2'nin `Back`i **aynı olayı** gönderiyor (`EventToPlugin:EventFmxRecruitmentFocusMoveToStep`, enum 1227976013),
ayırt eden **yük**: ileri `SendsProps One`, geri `MinusOne`; göstermeyen adımın kendi düğmesi ekranda duruyor ama
`Enabled=False`. Yeni `FocusSteps()` olayı + yükü taşıyan **açık** düğmeyi bulup işaretçisini veriyor; kimlik yoksa
eskisi gibi yazıya düşülüyor.
- Ölçüm yolu: `/private/var/…/opencode/t52/run.sh <etiket> TR|EN` → her koşuda s1 `fm open "Messages"` sonrası,
  s2 hemen ikinci koşu, s3 `fm open "Squad Dynamics"` (EN: `Dynamics`) sonrası, s4 form **ikinci adımda bırakılmış**
  (kalıntı dalı; sayfa + `Create a New Recruitment Focus` + `Next` curl ile basılıyor) → `fm scout areas --json`.
- Sonuç: **dört durumda da** stdout `md5 fed56895c45431dd9820725651421c2b`, rc=0,
  `["Competition ›","Nation ›","Recently Selected ›"]` — **eski derleme** (`git stash push -- cli/fm/ScoutFocus.cs` +
  `dotnet build -c Release`) ile **birebir**: `before-tr` (4/4) ve `before-en` (4/4) aynı md5; `after-tr`/`after-en`
  de aynı md5 (yorum: hiçbir durumda davranış değişmedi).
- İz (s1, TR): eski `click ?text=Create a New Recruitment Focus` (bazen 2-3 deneme) → `form ?anchor=Next&up=12` →
  `form ?anchor=Back&up=12` → `click ?text=Next`; yeni `click ?text=Create…` → **`buttons ?json=1&gen=1`** →
  **`click ?ptr=0x…`** → `form ?anchor=Back&up=12` → `click ?text=Cancel` (iptal yolu bu turda değişmedi). s4 (TR):
  yeni derlemede ilk `buttons` işareti adım 2'yi (MinusOne açık, One yok) görüyor → `click ?text=Cancel` + `dialog`
  cevabı → sayfa yeniden açılıp temiz form; iki derlemede de aynı akış, aynı çıktı.
- Ek (regresyon, odağı kurmayan yol): `fm scout new --priority top --area "ZZZ yok"` → iki derlemede de **rc=64**,
  stdout boş, stderr aynı satır (`Area: 'ZZZ yok' was not found…`); `fm scout --json` md5 koşudan önce/sonra
  `9142427a9d97a87532c05ebd8367bf92` (5 odak) → **yeni odak yok**. İz farkı yalnız yeni `buttons ?json=1&gen=1`
  işareti (eski `form ?anchor=Next`).
- Aynı formda ölçülen ama **değiştirilmeyen** (kural 4): `Cancel` (0x435301800) ve `Confirm` (0x426ea2800) ikisi de
  `RecruitmentFocusEditConfirmationDialog` olayını taşıyor; ayırt eden alan `/buttons?text=Confirm` bloğunda
  görünüyor — `m_cancelEdit = False` (Confirm) / `True` (Cancel) — ama bu alan `/buttons?json=1` özetinde yok;
  JSON'a eklemek **eklenti değişikliği + oyun restartı** ister (Sonraya bırakıldı). Confirm'e basışın doğal sonucu
  ölçüldü: pencere açılmıyor, form kapanıyor, **odak kuruluyor** (bölge seçilmeden adı bölge adı olan "All"/"Hepsi"
  kaydı eklendi — kayıt durumu notu Tur 52 başlığında).
- Kayıt durumu: kayıt yalnız okundu; turun tek yazımı yukarıdaki odak (Confirm ölçümü); saat 2 Ağustos 2026 14:00,
  ilerletilmedi, maç oynanmadı; dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı
  (`/state` → turkish).
- Regresyon: `tools/sweep.sh /private/var/…/opencode/t52/sweep` → **50 komut, 0 hata** (Türkçe oyunda; `scout`
  satırı rc=0). Değişiklik yalnız `ScoutFocus.cs` içinde (`FocusSteps()` + iki çağrı yeri); paylaşılan koda
  (Bridge/OpenPage/`Until`) dokunulmadı. Son TR koşusu: `fm scout areas --json` → `md5 fed56895…`, rc=0.

### Tur 50 — B "yazıyla basma" / `staff find` arama paneli: `Apply` ve `Cancel` artık gönderdikleri yükle (`True`/`False`) basılıyor (§8.239, 19 Ekim)
Ne: `cli/fm/StaffSearch.cs`. Gönder yolu `StaffFind` 112'de `click ?text=Apply`; `ApplySearch`in `PanelButton`ı (262) ve
`StaffFind`in kendi `Cancel`i (69) paneli `form ?anchor=Apply&up=8&buttons=1` ile okuyup `Text == "Apply"/"Cancel"`
ile buluyordu. Ölçüm (TR oyun, panel açık, `/buttons?json=1&gen=1`): panelin iki düğmesi **aynı olayı** gönderiyor
(`PanelHistoryClose, EventToPlugin:EventFmxFilterRestoreFilter`), ayırt eden **yük**: Apply `SendsProps True`, Cancel
`False` (yükle gelen yük bindingsiz değil; `Name` de farklı — `button-primary-leading-icon-label` /
`button-base-label` — ama o şablon sınıfı, kural 3 gereği yüke öncelik verildi). Yeni `PanelSent(bool apply)` bu olayı
ve yükü taşıyan düğmeyi bulup `?ptr=` ile basıyor; bulamazsa eskisi gibi form okuması + yazı.
- Ölçüm yolu: `/private/var/…/opencode/t50/run.sh <out> TR|EN` — her koşudan önce `fm staff find --clear` (aynı süzgeç
  durumu), sonra iki yol; üç durum s1 `fm open "Messages"` sonrası / s2 hemen ikinci koşu / s3 başka sayfaya
  geçtikten sonra (EN'de `fm open "Dynamics"`). Yanıt bozuksa (`no element at` / `screen may have changed`) bir kez
  tekrar; bu turda hiç gerekmedi.
- Üç durum × iki dil × iki yol, önce = eski derleme (`git stash push -- cli/fm/StaffSearch.cs` + `dotnet build`),
  sonra = yeni derleme; kullanılan klasörler: `before-tr` (oturmuş), `before-en` (dil geçişinden hemen sonra),
  `before-en2` (oturmuş, eski derlemeyle son karşılaştırma), `after-en` (v1), `after-tr`/`after-tr2`, `after-tr3`,
  `after-en3` (son derleme).
  stdout `md5` **birebir**: TR `278f3c93afe416f0d6c62353a517cd67` (before-tr 3/3, after-tr2 3/3, after-tr3 3/3),
  EN oturmuş `2ba97887d7c948522efd6e8436749986` (before-en s3 + before-en2 3/3 + after-en 3/3 + after-en3 s3);
  dil geçişinden hemen sonraki koşular iki derlemede de aynı: EN `5b380c79ecdd5f8c20579f44aadde061` (before-en s1/s2 =
  after-en3 s1/s2), TR `7f4956affc9d5a0989739834069849c8` (after-tr s1/s2; sonra oturuyor).
  rc hep 0 (Apply) / 64 (Cancel); iptal yolunun insan mesajı `md5` TR `323377898b24fb0011935d1f72ddaf29` /
  EN `52c49a4e6158ce3d2d73b48d1f300ccf` (iki derlemede aynı).
- İz: eski derleme `… select ?ptr=…&names=Coaching>Coach&fire=1` → `click ?text=Apply`; yeni derleme `… buttons
  ?json=1&gen=1` → `click ?ptr=0x…`. İptal yolu eski derlemede `form ?anchor=Apply&up=8&buttons=1` + `click ?ptr=…`,
  yeni derlemede `buttons ?json=1&gen=1` + `click ?ptr=0x…` (yedek form okuması **hiç çağrılmadı**: altı koşunun
  altısında `form ?anchor=Apply&up=8&buttons=1` sayısı 0).
- Yan bulgu (yeni, düzeltilmedi): **dil değişiminden hemen sonraki** ilk koşularda tablo yalnız süzülmüş satırları
  veriyor (21: 17 Coach + 4 Assistant Manager), sayfa oturduktan sonra 69 (48 satırın `JobString`i boş) — eski
  derlemede de aynı (before-en s1/s2 vs s3), yani bu turun değişikliğiyle ilgisi yok (§8.194/§8.232 devamı).
- Kalan: panelin *okuma* yolu (`Under` 262, `Row` 332) hâlâ `form ?anchor=Apply` ile kelimeyle çapalı.
- Paylaşılan yol: aynı `ApplySearch`i `fm players` de kullanıyor (Reads.cs 802) — `fm players "Aaron" --attr
  "Pace>=15"` eski ve yeni derlemede **birebir** (`md5 d8cd16daa30d08417f6a34d9007d3e6e`, rc=0); izde eski
  `form ?anchor=Apply&up=8&buttons=1` → yeni `buttons ?json=1&gen=1` + `click ?ptr=…` (o yolda da basış zaten
  `?ptr=` ile oluyordu, değişen kimliğin kaynağı).
- Kayıt durumu: kayıt yalnız **okundu**, hiçbir şey yazılmadı/değiştirilmedi; personel sayfasının arama süzgeci (tur
  başında 1 kural duruyordu) `--clear` ile boşaltıldı, sayfa süzgeçsiz kaldı ("Aramayı Düzenle" + Clear kapalı);
  2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı; dil `fm settings language "Türkçe"` ile geri alındı
  (`/state` → turkish, `Language: turkish`).
- Regresyon: `tools/sweep.sh /private/var/…/opencode/t50/sweep` → **50 komut, 0 hata** (Türkçe oyunda; `staff` rc=0,
  7 sn, 63 satır). Değişiklik yalnız `StaffSearch.cs` içinde (yeni `PanelSent` + üç çağrı yeri); paylaşılan koda
  (Bridge/OpenPage) dokunulmadı.

### Tur 49 — B "yazıyla basma" / `saves delete`: kayıt tarayıcısının Delete'i artık gönderdiği olayla (`DeleteSelectedGame`) basılıyor (§8.238, 19 Ekim)
Ne: `cli/fm/Saves.cs` `DeleteSave` (407) önce `Label == "Delete" && Type == "SIButton" && Enabled == "True"` bekliyor,
sonra `click ?text=Delete` yapıyordu. Tarayıcının dört düğmesi aynı şablon ailesinden ve etiketleri o dilin sözü
(§8.236); ayırt eden gönderdikleri olay. Yeni yerel yardımcı `DeleteButton()`: `Events == "DeleteSelectedGame"` **ve**
`Enabled == "True"` olan düğmeyi bekler, `?ptr=` ile basar; bulamazsa eskisi gibi `?text=Delete`e düşer. Bekleme ve
basış aynı işaret oldu (eski üç koşullu bekleme kalktı).
- Ölçüm yolu: kayıt klasörüne **yalnız kendi kopyamız** kondu (`cp "Faruk Kavlak - Fenerbahçe (v03).fm" "t49 s1.fm"` …),
  `FM_TRACE=1 fm saves delete "t49 s1" --yes`. Betik: `/private/var/…/opencode/t49/run.sh`; klasör listesi koşudan
  önce/sonra saklandı (`-before.ls` / `-after.ls`).
- Üç durum × iki dil (önce = eski derleme `git stash` + `dotnet build`, sonra = yeni):
  s1 `fm open "Messages"` sonrası, s2 hemen ikinci koşu, s3 `fm open "Squad Dynamics"` (EN: `fm open "Dynamics"`)
  sonrası. rc hepsi 0, dosya gerçekten silindi, stdout `md5` **birebir**:
  TR `efb3dc45d615438934915147628fdd84` / `fd3c4108754bada68053788e02c50cb2` / `feb30e749daff812bc17878e36758c0c`,
  EN `ff8c9d93e91ac7dbc5e5fdd30a2358d2` / `da1d75f073736527df7e9a8eddabb831` / `6d5b52d66fc3af5de4e7199afdd62052`.
- İz: eski `buttons ?json=1` → `click ?text=Delete`; yeni `buttons ?json=1` (olay+açık işareti) → `click ?ptr=0x…`.
  Kalan akış aynı: `click ?text=Save Game As` (FM menüsü, hâlâ yazıyla) → `rowselect ?text=<ad>` → bizim basış →
  `dialog` → `click ?ptr=…` (pencerenin "Yes"i, §8.237) → `buttons` + `click ?ptr=…` (tarayıcı Cancel'ı, §8.236).
- Negatif (yeni derleme): `fm saves delete "zzz yok" --yes` → rc=1 "'zzz yok' adında kayıt yok." (tarayıcı hiç
  açılmıyor); `fm saves delete "Faruk Kavlak - Fenerbahçe" --yes` (yüklü kayıt) → rc=1 "… şu an yüklü olan kayıt;
  silinmez." + tarayıcı Cancel ile kapanıyor (izde Delete'e basış yok). Eski derlemede de aynı.
- Kayıt durumu: yalnız kendi kopyalarımız silindi; Faruk'un dört dosyası (ana kayıt `19 Eki 02:53`, overwrite backup
  `02:52`, v02 `14 Eki 18:04`, v03 `15:59`) mtime/boyut değişmeden yerinde; 2 Ağustos 2026 09:15, saat ilerletilmedi,
  maç oynanmadı; dil `fm settings language "Türkçe"` ile geri alındı (`fm settings` → `dil: Türkçe`).
- Tuzak/ölçüm notu: bir EN koşusu tarayıcıyı açamadı ("The save browser did not open.", `OpenFmMenu` aksaklığı —
  §8.236'da da görülmüştü), kopya yerinde kaldı ve koşu tekrarlandı; ayrıca İngilizce oyunda `fm open "Squad Dynamics"`
  "no page named 'Squad Dynamics'" diyor, doğrusu `fm open "Dynamics"` (sayfa adı o dilin sözü; ölçüldü, koda
  dokunulmadı — ayrı iş).
- Regresyon: `tools/sweep.sh /private/var/…/opencode/t49/sweep` → **50 komut, 0 hata** (Türkçe oyunda; `saves` rc=0,
  7 satır). Değişiklik yalnız `Saves.cs` içinde (yeni yerel yardımcı + tek çağrı); paylaşılan koda dokunulmadı.

### Tur 48 — B "yazıyla basma" / onay pencereleri: `save --as`in ve `saves delete`in penceresi artık başlık yazısıyla değil oyunun verdiği adla (`Type`) bulunuyor (§8.237, 19 Ekim)
Ne: `cli/fm/Saves.cs` `DialogButton` (268). Pencere `Field(dialog,"Title").StartsWith(title)` ile, yani **başlık
yazısıyla** aranıyordu ("Replace Existing File" / "Delete File"). Eklenti (`/dialog`, `Dialogs.cs`) her pencerenin
oyunun kendi adını da veriyor (`Type` = `DialogManager.m_currentDialog.Value.Type`), o ad dilden bağımsız. Yeni imza
`DialogButton(string type, string title)`: önce `Field(dialog,"Type") == type`, bulunamazsa eskisi gibi başlık.
- Ölçüm (canlı, TR oyun, `dialog?data=1`): `save --as "<var olan ad>"` → `Type=ConfirmSaveOverrideDialog`,
  `Title=Replace Existing File?`, `primary` `Confirm`/`ConfirmSaveGame`, `secondary` `Cancel`/`close`; tarayıcıda
  başka bir kaydın satırı seçilip `DeleteSelectedGame` düğmesine basılınca → `Type=confirmDeleteSelectedSave`,
  `primary` "Yes", `secondary` "No" (ikisinin `EventID`i boş). **Dikkat:** silme penceresinin türü küçük harfle
  başlıyor (`confirmDeleteSelectedSave`). İkisinin de başlığı TR oyunda **İngilizce** geliyor — yazı yolu bugün bu
  makinede çalışıyor, ama oyunun çeviri tablosuna/skine bağlı bir söz.
- Öncesi/sonrası: `/private/var/…/opencode/t48/run.sh` TR + EN, üç durum (s1 `fm open "Messages"` sonrası / s2 hemen
  ikinci koşu / s3 `fm open "Squad Dynamics"` sonrası) × `fm save --as "Faruk Kavlak - Fenerbahçe" --yes`.
  stdout `md5`: TR `d4d552c7844b8005649f054ac42d8413` (3/3), EN `46212f7f30265749e8e115da9b1f1683` (3/3) —
  **eski derleme ile birebir** (ölçüm eski derlemeyle alındı: `before-tr`, `before-en`; Tur 47'nin kaydıyla da aynı).
- İz (yeni derleme, TR): `click ?text=Save Game As` → `type ?ptr=…&text=…` → `click ?ptr=…` (Save) →
  `dialog` 1,47 → `dialog` 1,82 → `click ?ptr=0x3ee05f000` (onay, artık `Type` eşleşmesiyle bulundu).
- Silme yolu (uçtan uca, yeni derleme, TR): kayıt klasörüne **yalnız kendi kopyamız** kondu
  (`cp "Faruk Kavlak - Fenerbahçe (v03).fm" "t48 test.fm"`; oyunun tarayıcısı klasördeki dosyaları listeliyor:
  `texts?anchor=Last saved` → `Name, Last saved, t48 test, 19/10/26 - 02:53, …`) →
  `FM_TRACE=1 fm saves delete "t48 test" --yes` → **rc=0 "Silindi: t48 test"** (iz: `rowselect ?text=t48 test` →
  `click ?text=Delete` → `dialog` → `click ?ptr=0x46a57a800` = pencerenin "Yes"i → tarayıcı Cancel ile kapandı).
  Klasörde Faruk'un dört dosyası (ana kayıt, overwrite backup, v02, v03) aynen duruyor; kopya yok.
- Negatif/değişmeyen: `fm save --as "Faruk Kavlak - Fenerbahçe"` (onaysız) → rc=1 "… üzerine yazar … --yes",
  tarayıcı hiç açılmıyor; `fm saves delete "zzz yok" --yes` → rc=1 + dört kaydın listesi, tarayıcı hiç açılmıyor.
  Pencere hiç çıkmazsa `Type` alanı boş geliyor → eşleşme yok, `DialogButton` null dönüyor (eski davranış).
- Karar/gerekçe: başlık, oyunun kendi kurduğu söz; `Type` oyunun kendi sınıf adı. Çıktı birebir olduğu için kimlik
  öne alındı, başlık yedek olarak duruyor (kural 3: fonksiyondaki tek yazı bağımlılığı buydu).
  Aynı sınıfta kalanlar (ayrı alt maddeler): tarayıcının `Delete` düğmesi hâlâ `click ?text=Delete`; `MatchDay`in
  `Instant Result` / `Team Selection` / `Injured Players` pencereleri hâlâ başlık yazısıyla aranıyor.
- Regresyon: `tools/sweep.sh /private/var/folders/…/t48/sweep` → **50 komut, 0 hata** (Türkçe oyunda; `saves` rc=0,
  7 satır). Değişiklik yalnız `Saves.cs` içinde (yerel yardımcı + iki çağrı); paylaşılan koda dokunulmadı.
- Dönüş/geri: kayıtlar yalnız okundu ve aynı adla üzerine yazıldı (yeni dosya açılmadı, yüklü kaydın adı
  değişmedi), kendi kopyamız silindi; 2 Ağustos 2026 09:15, saat ilerletilmedi, maç oynanmadı, hiçbir ayar/oyuncu
  değişmedi; dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).
- Dosya: `cli/fm/Saves.cs`, `docs/BAGIMLILIKLAR.md` (B "yazıyla basma" altında yeni `[x]` alt madde + "Hâlâ açık"
  notundaki 5 pencere satırı), `docs/FINDINGS.md` §8.237, bu defter.

### Tur 47 — B "yazıyla basma" / `save --as` · `saves delete`: kayıt tarayıcısının Cancel/Save'i artık gönderdikleri olayla basılıyor (§8.236, 19 Ekim)
Ne: `cli/fm/Saves.cs`. `SaveAs` (289) `click ?text=Save`, `CloseSaveBrowser` (240) `?text=Cancel`. Yerine yeni yerel
yardımcı `BrowserButton(bool save)`: `buttons ?json=1` içinde `Events == "SaveGame"` düğmesi bulunuyor (Save için o
basılıyor); Cancel için **aynı `Page`teki** `Events == "ClosePanel"` düğmesi alınıyor; kimlik bulunamazsa eskisi gibi
yazı yolu. `Name` ayırt etmiyor (Browse/Delete/Cancel hepsi `button-base-leading-icon-label`).
- Ölçüm (kimlik): tarayıcı açıkken `/buttons?json=1` → panel `008eea55baa5a3c49b3e446596ede765`:
  `BindableTextEditBox EditBox` ("Faruk Kavlak - Fenerbahçe", `OnSubmitEvents→SaveGame`), `Browse`
  (`SetDatastoreValue`), `Delete` (`DeleteSelectedGame`, `Enabled=False`), **`Cancel` (`ClosePanel`)**,
  **`Save` (`SaveGame`)** — dördü de `button-base-leading-icon-label` / `button-primary-leading-icon-label`.
  FM menüsü satırı `Save Game As` = `Name=button-base-label`, `Events=FMMenuOptionFromID` (seçenek numarası yok) →
  satır yazıyla kalıyor.
- Öncesi/sonrası: `/private/var/folders/…/opencode/t47/run.sh` TR + EN, üç durum (s1 `fm open "Messages"` sonrası / s2 FM menüsü zaten açık /
  s3 `fm open "Squad Dynamics"` sonrası) × iki yol. `saves delete "Faruk Kavlak - Fenerbahçe" --yes` (tarayıcı açılır,
  Cancel kapatır, rc=1, stderr "'…' şu an yüklü olan kayıt; silinmez…" / EN "is the save loaded now; it is not
  deleted") ve `save --as "Faruk Kavlak - Fenerbahçe" --yes` (tarayıcı açılır, Save basılır, "Replace Existing File"
  onaylanır, rc=0 "Kaydedildi: … (2 Ağustos 2026 Pazar)" / EN "Saved: … (2 August 2026 Sunday)").
  stdout `md5`: TR delete boş `d41d8cd98f00b204e9800998ecf8427e` (3/3), TR save `d4d552c7844b8005649f054ac42d8413`
  (3/3), EN save `46212f7f30265749e8e115da9b1f1683` (3/3) — **eski derleme (git stash) ile birebir**.
- İz (aynı komutlar): eski derleme `click ?text=Save Game As` → `click ?text=Cancel` / `type …` → `click ?text=Save`
  → `click ?ptr=<dialog onayı>`; yeni derleme `click ?text=Save Game As` → `click ?ptr=0x…` (Cancel) / `type …` →
  `click ?ptr=0x…` (Save) → `click ?ptr=0x…` (onay). Tarayıcıyı açan satır her iki derlemede de yazıyla
  (`?text=Save Game As`), çünkü o satırın kimliği yok.
- Negatif/değişmeyen: `saves delete "zzz yok" --yes` rc=1 (adaylar listelenir, tarayıcı hiç açılmaz);
  `save --as "<ad>"` onaysız rc=1 ("üzerine yazar … Onaylamak için: save --as … --yes"), tarayıcı açılmaz.
- Karar/gerekçe: iki etiket de oyunun kendi sözleri ve bu dilin tablosundan geçiyor (§8.209/§8.234/§8.235 sınıfı);
  tablo çakışmaları yüzünden yazı yolu dile bağlı, olay yolu değil. Çıktı birebir olduğu için kimlik öne alındı,
  yazı yedek olarak duruyor.
- Dönüş/geri: kayıt yalnız okundu ve **aynı adla** üzerine yazıldı (yeni dosya yok, `fm saves --json` aynı dört kayıt,
  yüklü kaydın adı değişmedi); 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı; dil EN ölçümünden sonra
  `fm settings language "Türkçe"` ile geri alındı (`/state` → `turkish`).
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (Türkçe oyunda; `saves` rc=0, 1 sn, 7 satır). Değişiklik yalnız
  `Saves.cs` (yeni yerel yardımcı), paylaşılan koda dokunulmadı.
- Not (ölçüm tuzağı): TR oyunda ayarın adı da oyunun sözü (`fm settings dil "English"` ile geçildi, dönüş
  `fm settings language "Türkçe"`). `OpenFmMenu`/`OpenSaveBrowser` ara sıra ilk koşuda tarayıcıyı açmıyor (ilk TR
  koşusu 2. denemede geçti; EN'de 6/6 ilk denemede) — **kod değişmedi**, ayrı iş.
- Dosya: `cli/fm/Saves.cs`, `docs/BAGIMLILIKLAR.md` (B "yazıyla basma" altında yeni `[x]` alt madde),
  `docs/FINDINGS.md` §8.236, bu defter.

### Tur 46 — B "yazıyla basma" / `budget`: kartı açan döşeme ve kartın "Got It"/"Cancel"i artık işaretçiyle (§8.235, 19 Ekim)
Ne: `cli/fm/Budget.cs` `Budget()`. Üç basış oyunun İngilizce sözüne dayanıyordu: `click text="Adjust Transfer and Wage
Budget"`, `click text="Got It"`, `click text="Cancel"`. Yerine: döşeme eylemiyle (`SpeakToAction`, ölçülen ActionID
1885623898 = `ClubBudgetAdjustmentTransfersAndWages`), "Got It" gönderdiği olayla (`Events` içinde
`EventFmxBudgetAdjustmentConfirm`, enum 146), "Cancel" aynı panelin kapatın düğmesi olarak (`Page` eşit + `ClosePanel`
+ onay olayını taşımayan). Üçü `click ?ptr=`; kimlik bulunamazsa eski yazı yolu yedek.
- Ölçüm (kimlik): kart açıkken `/buttons?json=1` → panel `c16a10db7d53a244c8862e289a5dc7ca` dört düğme:
  `AdjustDownButton`/`AdjustUpButon` `EventToPlugin:EventFmxBudgetAdjustmentAdjust`,
  `button-secondary-label` "Cancel" `ClosePanel`, `button-primary-leading-icon-label` "Got It"
  `EventToPlugin:EventFmxBudgetAdjustmentConfirm, ClosePanel`. Kart `Cancel` ile kapatılıp yeniden açıldığında panel
  Guid'i **aynı**. Döşemeler: `/buttons?json=1&gen=77` → `Action=fmcli.speak.g77.9`;
  `/pull?path=…&props=ActionID,Event` → `1885623898` / `1332374646` (`ClubBudgetAdjustmentScouting`).
- Öncesi/sonrası: `run.sh` ile TR ve EN, üç durum (s1: `fm open "Messages"` sonrası / s2: sayfa zaten açıkken hemen
  ikinci koşu / s3: `fm open "Squad Dynamics"` sonrası), her durumda `ayarla 99` (rc=1), `ayarla 15` ("zaten",
  rc=0), `ayarla 14` (Gerçek değişim, rc=0) + `fm budget --json` + `ayarla 15` (geri) + `--json`. Çıktı md5'leri:
  TR old = new: 99 `bd1073cf828803b6268041bf40344aa9`, same `29db1cb45edc4780a6c11408e513fd94`, 14
  `93cbe81d1119355a12e19422fb40936a`, json-after `d0b08a7072a6e67dd70f4f74e34c5572`, json-back
  `2b1645ca04aff15e0d53133299653545`; EN old = new: 99 `c26273fbacd0e788066fcd03844b30b8`, same
  `f03d9d91b76ea9720ed674c9e7a5c51f`, 14 `cd196064cdc715cce5e676ab3e579650`, json-after
  `f8f717b2ae5ea03374246b35506b215c`, json-back `5ffe45d1bb119d2537246f3acf2b5401`.
- İz (aynı komutlar): eski derleme `click ?text=Adjust Transfer and Wage Budget` → `click ?text=Got It` /
  `click ?text=Cancel`; yeni derleme `buttons ?json=1&gen=…` + `click ?ptr=0x…` (döşeme) ve `buttons ?json=1` +
  `click ?ptr=0x…` (kart) — üç durumda da döşeme ve kart basışları böyle. rc: yeni TR 15×0 / 3×1 (üç `ayarla 99`),
  EN aynı; eski de aynı.
- Negatif/değişmeyen: `ayarla 99` → "… bir adım değil (0–15)." rc=1, kart kapanıyor; `ayarla 15` (şu anki adım) →
  "Bütçe zaten böyle." rc=0, kart kapanıyor; `ayarla 14` gerçekten uygulanıyor (`Transfer bütçesi: 584B £`, `Maaş:
  1.517 £ aşılmış`), `ayarla 15` ile **başladığı yere dönüyor** (626B £ / 2.320 £; EN 584K/626K).
- Karar/gerekçe: etiketler oyunun kendi sözleri ve bu dilin tablosundan geçiyor (§8.209/§8.234'ün sınıfı); üç
  düğmenin kimliği ölçüldü, çıktı birebir → yazı bırakıldı, kimlik öne alındı (yedek yazı yolu duruyor). Kartın
  **okuma** yolu (Wage/Transfer Budget, Scouting, "Budget Adjustment" çapaları) bilerek dokunulmadı: ölçüldü, o
  sayıların sayfada veri yolu yok.
- Dönüş/geri: kayıt yalnız okundu + iki adım arasında gerçekten değişti ve geri alındı (bütçe başladığı yerde);
  saat 2 Ağustos 2026 14:00, maç oynanmadı, saat ilerletilmedi; dil EN ölçümünden sonra
  `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).
- Regresyon: `tools/sweep.sh` → **50 komut, 13 hata**, hepsi sayfa/görünüm açılmayan ailede (`depth`, `opposition`,
  `world`, `numbers`, `register`, `responsibilities`, `youth`, `youth --training`, `inbox` …) ve tur sonunda oyunun
  ana iş parçacığı 30 sn'den uzun sustu (`inbox`: "the main thread did not run this request within 30s") + kayıt
  boşaldı ("Kayıt yüklü değil") — §8.99'daki bilinen tuzak; `pkill -9` + `fm start </dev/null` ile oyun **aynı
  yerden** döndü (2 Ağustos 2026 14:00, bütçe 626B £ / 2.320 £) ve o hâlde her biri tek başına **rc=0** (`world`,
  `numbers`, `opposition`, `health`, `register`, `youth --training`). Sweep'in `budget` satırı **rc=0**; değişiklik
  yalnız `fm budget ayarla` yolunda (`cli/fm/Budget.cs`), paylaşılan koda dokunulmadı.
- Dosya: `cli/fm/Budget.cs`, `docs/BAGIMLILIKLAR.md` (B "yazıyla basma" altında yeni `[x]` alt madde),
  `docs/FINDINGS.md` §8.235, bu defter.

### Tur 44 — A "Kısa `Until`" / Calendar 157: "Today" basışından sonra liste "eskisinden farklı **ve** iki okumada aynı" olana kadar okunuyor (§8.233, 19 Ekim)
Ne: `cli/fm/Calendar.cs` `CalendarMonths()` (146–158). Takvim sayfasının ay listesi, "Today" düğmesine basıldıktan sonra
"art arda iki okuma aynı ve boş değil" olunca kabul ediliyordu; bu işaret basıştan **önceki** ayı da kabul eder.
- Ölçüm (kimlik, TR): ay elle 6 / 10 / 13'e kaydırıldı (o hâlde düğme `Enabled=True`), `click ptr=<Today>` sonrası:
  basıştan hemen sonraki okuma hâlâ **eski ay** (126 / 95 / 128 ms), liste 2'ye (bugünün ayı) 198 / 252 / 304 ms'de
  geçiyor ve orada kalıyor; ay zaten bugünün ayındayken (2) düğme `Enabled=False` ve basış `error: "Today" is disabled`.
  Yani eski işaretin payı bir okuma aralığından (0,3 sn) az; yavaş makinede iki okuma da eski ayı verir, ay haritası
  yanlış aydan sayılır ve **bütün ayın maçları** yanlış listelenir.
- Sonrası (yeni derleme): `before` basıştan önce okunuyor, işaret "değer değişti **ve** sonra iki okumada aynı"
  (üst sınır 8 sn); basış `error` dönerse (zaten bugün) hiç beklenmiyor.
- Öncesi/sonrası (aynı komut, üç durum: `fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"`
  sonrası): TR `fm calendar --json` **birebir** `md5 0dd9e2647a65b730035f4f24925b8beb`, insan `bcd02554ce589761b74b2053a3c2c932`;
  EN `--json` `025897203ed1add7dc0f55748250abc5`, insan `2b1d76e316a4f5cd4abecb6bf836d54d`. Ayrıca ay elle 13 / 4 / 0 / 7'ye
  kaydırılıp `fm calendar --json` koşulduğunda da aynı md5 ve liste sonunda 2; süre: zaten açıkken 1,0 sn (yeni dal
  beklemiyor), ay uzaktayken 2,0 sn.
- Negatif/değişmeyen: düğme sayfada yoksa blok yine atlanıyor; aynı yardımcıyı kullanan `fm schedule` ve `fm fixtures`
  çalışıyor (ikisi de aynı gün, aynı kayıt).
- Regresyon: `tools/sweep.sh /private/var/folders/…/t44/sweep` → **50 komut, 0 hata** (Türkçe oyunda).
- Dönüş/geri: kayıt yalnız okundu; 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı, hiçbir şey yazılmadı;
  dil `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).
- Not (ölçüm tuzağı): ayarın **adı da** oyunun diline bağlı — TR'de `fm settings dil "English"`, EN'de
  `fm settings language "Türkçe"`; değer oyunun kendi sözlüğünden ("English" / "Türkçe").
- Dosya: `cli/fm/Calendar.cs`, `docs/BAGIMLILIKLAR.md` (A listesinde `Calendar 157` çizildi, `[x]` alt madde),
  `docs/FINDINGS.md` §8.233, bu defter.

### Tur 42 — A "Kısa `Until`" / Stages 337: `fm stages`te turun bağları Türkçe oyunda eksik okunuyordu — menzil oyunun ondalık işaretiyle çözülmüyor; düzeltme denendi, gezinti kararsız çıktı, geri alındı (§8.231, 19 Ekim)
Ne: `cli/fm/Stages.cs` `EveryTie()`. Turun bağları pencerede tam durmuyor, liste `to=0..1` adımlarıyla geziliyor ve
önce menzil okunuyor (`Scroll(first, null)` → `"0 of 1401,8"`). O sayı oyunun kendi dilinin ondalık işaretiyle geliyor
(TR "1401,8", EN "1636.6") ve kod onu `double.TryParse(…, InvariantCulture)` ile çözdüğü için TR'de `extent = 0` oluyor
→ **gezinti hiç çalışmıyor**.
- Ölçüm (kimlik): Carabao Cup **1. Tur'da 35 bağ** (Ön Tur 2, ötekiler kura bekliyor). Elle `curl`:
  `scroll?anchor=Oldham&to=0.0` → `0 of 1401,8`, `/ties` 18 satır (Oldham..Wolves); `to=0.2` → 24 satır; `to=0.4`
  → `560,7 of 1401,8`, 22 satır (Bristol City..Stoke); `to=0.5` → **cevapsız**, pencere bir daha değişmiyor.
- Öncesi (eski derleme, aynı durum): TR `fm stages "Carabao Cup" "1. Tur" --json` → **25 bağ** (3/3 aynı md5
  `64386372f51746bf2aa117c7e4802faf`), izde **0** `to=` adımı; `--all`da 1. Tur **18**; başka durumda 18. EN
  `fm stages "Carabao Cup" "First Round" --json` → **24 bağ** (3/3, md5 `efbef27ad98bc82fb43bf42e1081f91a`), izde 12
  adım. Yani aynı tur TR'de 18–25, EN'de 24 — üçü de eksik.
- Sonrası (menzil `GameNumbers.Read` ile, aynı durum): TR **35 bağ** (3/3, md5 `a69ce74b163c6793678d8081186d33f4`),
  izde 12 adım → eksik 10 bağ geldi. Ama **kararsız**: sonraki durumlarda TR 35/19/19 ve EN 35/35/19; düşen koşularda
  gezinti ilk adımlarda kırılıyor (çapa yazısı yerleşimden çıktığı için `/scroll` cevapsız).
- Değişmeyen/negatif: 35 bağın 35'i ayrık (kopya yok); Ön Tur 2, kurası çekilmemiş turlar (`2. Tur` 13/8/2026 …) ve
  lig tablosu yolu değişmedi; `fm stages "Carabao Cup" "ZZZ"` aynı listeyi veriyor.
- Karar: kod geri alındı (`git restore cli/fm/Stages.cs`) — gezinti sağlamlaşmadan (sabit çapa + "pencereden farklı
  **ve** iki okumada aynı") menzil düzeltmesi tek başına sonucu kanıtlanmış biçimde iyileştirmiyor (bazı durumlarda
  ilk pencereden de az toplayabiliyor). Madde `[?]`.
- Dönüş/geri: kayıt yalnız okundu (2 Ağustos 2026 14:00), maç oynanmadı, saat ilerletilmedi, hiçbir şey yazılmadı;
  dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish). Köprü bir kez
  takıldı → `pkill -9` + `fm start </dev/null` (§8.99).
- Dosya: `docs/BAGIMLILIKLAR.md` (A "Kısa `Until`" altında Stages 337 `[?]`), `docs/FINDINGS.md` §8.231, bu defter.

### Tur 41 — A "`Task.Delay`" / Staff 74: `fm staff` her kaydırmadan sonra 400 ms bekliyordu, tablo satırlarını iki adımda diziyor (§8.230, 19 Ekim)
Ne: `cli/fm/Staff.cs` `Staff()`. Kaydırma döngüsü `scroll` → `Task.Delay(400)` → `StaffRows()` idi. Ölçüm (kimlik):
`to=1` 6 → 10 → 27 satır, `to=0.75` 13 → 17 → 34, `to=0.5` 21 → 25 → 36, `to=0.25` 27 → 31 → 34; yarım hâller
0,1–0,2 sn duruyor (en uzunu `to=0.75`'te 13 satır: 0,03 → 0,23 sn), `/scroll` konumu ilk okumada doğru
(`455,9 of 911,8`) → geciken **diziliş**. Yeni: yerel `Settled()` — satırlar 0,25 sn arayla **üç okumada aynı** olana
kadar okunuyor (üst sınır 4 sn, boş okuma kabul değil).
- Öncesi (eski derleme, TR): üç durumda (başka sayfa açık / tablo zaten açık / başka sayfaya geçtikten sonra) insan
  `md5 4df8a2aa85db8c96b94348fd3a41cd1c`, `--json` `0d24d6ceeaba9cf15553bb4421613e42`; EN aynı üç durum
  `8b2e842f7efbd5ac7dbce534a5ce5ef2` / `50d543a83c2259ce25b069d6fd1a7c8f`. Süre TR 5–8 sn, EN 4–5 sn.
- Sonrası (yeni derleme): TR ve EN üçer durumda **birebir aynı md5'ler**, 48 personel; izde konum başına dört okuma
  (4,62 / 4,91 / 5,20 / 5,53 sn) → süre +1,5–2 sn.
- Negatif/değişmeyen: `fm staff` salt okuma (hiçbir şey açmıyor/değiştirmiyor), hata yolu (`Personel kartı açılmadı.`)
  aynı kaldı; sweep `staff` satırı rc=0.
- Regresyon: `tools/sweep.sh /tmp/sweep_t41` → **50 komut, 0 hata** (Türkçe oyunda).
- Dönüş/geri: kayıt yalnız okundu; dil TR (`/state` → turkish), saat 2 Ağustos 2026 14:00, maç oynanmadı.
- Dosya: `cli/fm/Staff.cs`, `docs/BAGIMLILIKLAR.md` (A listesinde `Staff 74` çizildi, `[x]` alt madde), `docs/FINDINGS.md`
  §8.230, bu defter.

### Tur 39 — B `scout new/edit --area`: bölge listesi yazma yolunda da oyunun bağıyla, "eklendi" işareti başlığın kendisi (§8.228, 18 Ekim)
Ne: `cli/fm/ScoutFocus.cs` `ScoutNewForm`'un bölge döngüsü. Liste `FormAfter(second, "Scouting Areas", "dropdown").Value`,
`before` yazısı `StartsWith("Scouting Areas")` ile bulunuyordu; başlık oyunun kendi cümlesi (TR "Gözlemleme Yerleri (0)").
TR'de `FormAfter` `null` dönünce `.Value` yakalanmamış `InvalidOperationException` atıyordu (satır 552), form açık
kalıyordu. Yeni: `FormBound(second, "possiblescoutingareasdropdownselectedoption")` (okuma yoluyla aynı denetim;
bulunamazsa `AbandonFocus` + "Odak formunda bölge listesi bulunamadı.", çökme yok) ve bekleme, listenin **hemen
önündeki** başlığın değişmesi (`FormHeadingBefore` — aynı öğenin değeri, dilden bağımsız).
- Ölçüm (kimlik, TR): `/form?anchor=Back&up=12&buttons=1` 47 öğe; `dropdown.Binds =
  focusitem.possiblescoutingareasdropdownselectedoption`, `-1` komşusu `text` "Gözlemleme Yerleri (0)";
  `/select?ptr=…&names=Nation>Europe>England&fire=1` sonrası 40 ms aralıklı okumalar: "Gözlemleme Yerleri (1)"
  + ardından `button` "England" (satır 26/27/28) ve bu hâl sabit. `dropdown.Selected` öncesi/sonrası `-1` kalıyor →
  işaret dropdown'ın kendisi değil, başlığı.
- Öncesi (eski derleme `git stash push -- cli/fm/ScoutFocus.cs` + `dotnet build cli/fm -c Release`):
  TR `fm scout edit 1 --area "Türkiye"` → iz `form …` sonra `Unhandled exception … Nullable object must have a
  value … ScoutFocus.cs:line 552`, rc≠0, form açık (manuel `Cancel`); EN aynı komut
  (`--area "Competition>England>Sky Bet Championship" --by "ZZZ yok"`) → `select …names=Competition>England>Sky Bet
  Championship&fire=1` 6,92 sn, sonra scout adımı → "Scout: there is no option 'ZZZ yok'".
- Sonrası (yeni derleme) — komut hep aynı (`… --area "Competition>England>Sky Bet Championship" --by "ZZZ yok"`;
  `--by` bilerek geçersiz, bölge adımından **sonra** düşer → form `Cancel` + onayla kapanır, kayıt değişmez):
  - TR: durum 1 `fm open "Squad Dynamics"` sonrası → `select` 9,00 / ilk okuma 9,06 / değişim 9,22 (**0,16 sn**)
    → "Formda scout seçme butonu yok."; durum 2 hemen ikinci koşu ve durum 3 `fm open "Messages"` sonrası → aynı
    ("Bölge eklenemedi" yok, çökme yok).
  - EN (`fm settings dil English`, `/state` → english): üç durumun üçünde `select` sonrası komut `Assign Any
    Scouts` penceresine geçti → "Scout: there is no option 'ZZZ yok'. Options: Joe Hutchinson…" (durum 3 izi:
    `select …names=Competition>England>Sky Bet Championship&fire=1` 7,18 sn).
  - EN eski derleme aynı komutta aynı yolu geçti → İngilizce davranış **birebir**.
- Negatif / değişmeyen: zaten seçili bölge `Nation>Europe>England` (odak "England"/TR "İngiltere") iki dilde de
  eklenmiyor: "Bölge eklenemedi: Nation › Europe › England" — oyun yinelenen bölgeyi almıyor, başlık sayısı
  değişmiyor (eski kodun EN davranışı da böyleydi).
- Yan bulgu (yeni alt madde, düzeltilmedi): `--by`/`--analysts` seçim penceresini açan `Edit` düğmesi
  `FormAfter(form, "Assign Scouts", "button")` ile bulunuyor; TR başlığı "Gözlemci Görevlendir (1)" →
  "Formda scout seçme butonu yok." İki `Edit` düğmesi `Name`/`Text`/`Events` bakımından **birebir aynı**
  (`button-base-trailing-icon-label`, `OpenPanel`), ayırt eden formdaki sırası; pencere başlığı
  (`Assign Any Scouts`, TR'de de İngilizce) da yazıyla.
- Dönüş/geri: hiçbir odak `Confirm` edilmedi; `fm scout --json` yine **4** odak (Complete kopya yok), dil
  `fm settings language "Türkçe"` (`/state` → turkish), saat 2 Ağustos 2026 09:15, maç oynanmadı.
- Not (ölçüm tuzağı): EN ikinci durumda oyunun ana iş parçacığı bir kez takıldı (form okuması
  `timeout: the main thread did not run this request within 30s`), oyun yeniden başlatılıp (`fm start` +
  `fm continue`) koşu tekrarlandı ve geçti; kayıt değişmedi.
- Regresyon: yalnız `ScoutFocus.cs` içi (`FormHeadingBefore` başka yerde çağrılmıyor, `FormBound` okuma yoluyla
  ortak ve değişmedi); paylaşılan koda dokunulmadı, `scout` sweep'te yok → `sweep.sh` gerekmedi.
- Dosya: `cli/fm/ScoutFocus.cs`, `docs/BAGIMLILIKLAR.md` (yazma yolu `[x]`, `--by` yeni alt madde),
  `docs/FINDINGS.md` §8.228, bu defter.

### Tur 37 — B "Veri merkezi bulguları İngilizce çıktı": tabloda olan bulgu cümlesi artık oyunun dilinde (§8.226, 18 Ekim)
Ne: `cli/fm/Program.cs`, `WriteAttachment`'ın veri merkezi dalı (satır ~1676–1681). Bulgular
(`row.AnalysisPointString`) olduğu gibi yazılıyordu. Cümleler oyunun kendi sözü: içinde sayı/isim taşıyanları
eklenti çeviremiyor ve oyunun dilinde geliyor (TR'de beş bulgunun dördü Türkçe), ama **tabloda birebir duran**
cümleyi eklenti İngilizceye çeviriyor ve geri eşini göndermiyor → TR oyunda o satır İngilizce kalıyordu.
Yeni: bulgular yazılmadan önce `GameWords.Ask(Fm, [.. points])` (cümle başına eksik olanlar için tek `translate`
çağrısı) ve satır `GameWords.Of(point)` ile yazılıyor — `GameWordsLines`'ın satır bölmesi `· ` yüzünden cümleyi
bölüp eşleştiremediği için tam eş `Of` ile alınıyor. Tabloda olmayan cümle (boş yanıt) nasıl geldiyse öyle kalıyor.
- Ölçüm (kimlik): TR `/translate?words=We've regained possession well in central areas of our own third of the
  pitch.` → gövde aynı cümle + `Yarı sahamızın orta kısmında çok top kazandık.`, X-Shown başlığı da bu çifti
  taşıyor (base64 çözüldü). Aynı çağrı dört **Türkçe** bulgu için boş gövde + X-Shown yok → yanlış eş yazılmıyor.
- Öncesi (eski derleme `git stash push -m t37 -- cli/fm/Program.cs` + `dotnet build cli/fm -c Release`):
  TR `fm reply "Lincoln 1-2"` (mesaj: "(L1) Lincoln 1-2 Bristol Rovers: Dikkat Çekenler") → beş bulgunun sonuncusu
  `· We've regained possession well in central areas of our own third of the pitch.` (diğer dördü Türkçe);
  dosya `md5` TR `4f53cf96…` (durum 1, 16 satır) / `a334a5b1…` (durum 2 ve 3, 13 satır). EN aynı üç durum →
  `69d0a7fb…` (1 ve 2) / `594b687e…` (3).
- Sonrası (yeni derleme): TR üç durumda **tam dosya farkı yalnız o satır**:
  `· We've regained possession…` → `· Yarı sahamızın orta kısmında çok top kazandık.` (üç durumda da);
  TR `md5` `d433b084…` (1) / `f5dd5953…` (2 ve 3). EN üç durumda bulgular blokları öncesiyle **birebir**
  (`diff` boş), yani İngilizce oyunda hiçbir şey değişmiyor.
- Üç durum (her biri TR + EN): başka sayfa açık (`fm open "Squad Dynamics"` sonrası, ek ilk kez okunuyor) /
  mesaj zaten açık (hemen ikinci koşu) / başka sayfaya geçtikten sonra (`fm open "Messages"`).
- Negatif / değişmeyen: aynı mesajın Türkçe bulguları, "Gol beklentisi", "Maçın oyuncusu", "Taktikler" satırları
  ve ekin başlığı değişmedi; komut rc=0 ve satır sayısı aynı. `Record("row.AnalysisPointString")` başka yerde
  okunmuyor (`grep`: yalnız bu dal), `GameWords.Ask/Of` var olan yardımcılar (değişmedi).
- Yan bulgu (bu değişiklikten değil, eski derlemede de var): ekin okunuşu duruma göre eksik geliyor — birinci
  durumda "Goller / Maçın oyuncusu / Taktikler" satırları varken ikinci/üçüncü durumda gelmiyor, bir EN koşusunda
  takım adları boş (`Attached: data hub —  1 - 2  `). `WriteAttachment`'ın 1,5 sn sabiti (§8.192) yarım bağlanmış
  sayfayı okuyabiliyor; o madde Faruk'un kararıyla sabit kalıyor, bu turda dokunulmadı.
- Regresyon: `tools/sweep.sh /private/var/folders/…/T/opencode/t37/sweep` → **50 komut, 0 hata** (İngilizce oyunda koşuldu: sweep başlarken dil EN'de kalmıştı; `inbox` 1 sn/16 satır). Değişen dal yalnız mesaj eki okuması, paylaşılan yardımcılara dokunulmadı.
- Dönüş/geri: kayıt yalnız okundu (2 Ağustos 2026 14:00), saat ilerletilmedi, maç oynanmadı, hiçbir ayar/oyuncu
  değişmedi; dil `fm settings language "Türkçe"` ile geri alındı (`/state` → `Language: turkish`).
- Dosya: `cli/fm/Program.cs` (yalnız bu dal), `docs/BAGIMLILIKLAR.md` (B maddesi `[x]`), `docs/FINDINGS.md` §8.226,
  `docs/manual/KILAVUZ.md` (bulguların dili notu), bu defter.

### Tur 36 — B "Cümle parçası arayanlar" / `Release "Is there anything else"`: kapanış sorusu artık kapatma düğmesinin bitirdiği satır (dil değil, olay) (§8.225, 18 Ekim)
Ne: `cli/fm/Release.cs`, karşılıklı fesih dalının çıktısı (satır 191–198). Tekliften sonra `/talk` üç satır
veriyor ve üçüncüsü oyunun kendi kapanış sorusu; cümleyi oyun kurduğu için TR'de İngilizce değil
("Sizin için yapabileceğim başka bir şey var mı hocam?"), eski `StartsWith("Is there anything else")` süzgeci
tutmuyordu ve o satır asistanın cevabı sanılıp yazılıyordu. Yeni: satırlar arasından `»` yankılar atılıyor, sonra
konuşmanın genel kapatma düğmesi (`EventFmxClearChanges`, 31) varsa **son insan-dışı satır** düşülüyor; düğme
yoksa hepsi yazılıyor. Aynı okuma hem kapatma basışı hem süzgeç için kullanılıyor.
- Ölçüm (kimlik): `/talk` Says TR `["» Mutual Termination", "Tamamdır hocam, ben ona bu kararımızı söylerim …
  emin olun derim.", "Sizin için yapabileceğim başka bir şey var mı hocam?"]`, EN `["» Mutual Termination",
  "Alright, I'll pass on your decision to him, and make sure the board are in agreement.", "Is there anything
  else I can do for you?"]`; `/translate?words=Is there anything else` boş → cümle tablodan geçmiyor.
  Konuşma açıkken `buttons?json=1&gen=…` + `pull …&props=Event`: kapatma "Acknowledge and Move On" `Event:31`,
  diğer cevaplar `Event:44` (TR + EN aynı) → süzgeç metne değil olaya bağlı.
- Öncesi (eski derleme `git stash push -- cli/fm/Release.cs` + `dotnet build`, TR): `fm release "Ben House"
  --mutual --yes` ve `fm release "Jenson Jones" --mutual --yes` → iki satır: asistan cevabı **+ "Sizin için
  yapabileceğim başka bir şey var mı hocam?"**. EN (eski derleme): aynı iki oyuncuda tek satır (asistan cevabı).
- Sonrası (TR, üç durum): `fm open "Squad Dynamics"` sonrası `Tom Bayliss`, `fm open "Messages"` sonrası
  `Ben House`, hemen ardından `Jenson Jones` → üçünde de yalnız asistan cevabı + "karşılıklı fesih teklif edildi"
  satırı; oyunun sorusu yok. EN (üç durum, aynı düzen, oyuncular Tom Bayliss / Ben House / Jenson Jones) →
  eskisiyle **birebir** tek satır.
- Negatif / değişmeyen: serbest bırakma listesi olmayan oyuncular (Cody Tomlinson, Gael Mande, Adam Reach) iki
  dilde de "serbest bırakma seçeneği yok." / "There is no option to release …" (rc=1); paylaşılan koda
  dokunulmadı (`AcknowledgeButton` yalnız okundu), `sweep.sh` `release` koşmuyor.
- Dönüş/geri: kayıt `fm load "Faruk Kavlak - Fenerbahçe" --no-save` ile iki kez baştan yüklendi (teklifler geri
  alındı), dil `fm settings language "Türkçe"` (`/state` → `turkish`), saat 2 Ağustos 2026 14:00, maç oynanmadı.
- Dosya: `cli/fm/Release.cs`, `docs/BAGIMLILIKLAR.md` (B alt maddesi `[x]`), `docs/FINDINGS.md` §8.225,
  `docs/manual/KILAVUZ.md` (fesih çıktısı notu), bu defter.

### Tur 35 — B "Cümle parçası arayanlar" / `Matches "Match Ratings"+"Inf"`: maç puanları sayfası oyunun kabıyla ve veri yoluyla okunuyor; puan başlığı İngilizce oyunda Türkçe kalıyordu (§8.224, 18 Ekim)
Ne: `cli/fm/Matches.cs` (`player <oyuncu> --matches`). Sayfa üç İngilizce sözle tanınıyordu:
(1) bekleme `texts.Contains("Match Ratings") && texts.Contains("Inf")`, (2) okuma
`/texts?anchor=All Competitions&up=10`, (3) satırların başı `texts.IndexOf("Inf")`; ayrıca kapanış kontrolü
`texts?anchor=Match Ratings&up=0`. Yeni: sayfa oyunun **kendi kabıyla** okunuyor —
`/texts?within=PlayerFormCard&paths=1` (oyunun şablon adı; sayfa kapalıyken `[]`), her satır bağlı olduğu
**veri yoluyla** geliyor ve "yüklendi" işareti özetin kendi bağı (`formstats.GamesPlayed`) **ve** satırların
iki okumada aynı olması; `Rows()` artık `Inf` aramıyor (satırlar zaten `^\d+$` + tarih çiftiyle bulunuyor,
tablonun üstünde bu çifti veren başka bir şey yok). Aynı fonksiyondaki dil artığı (kural 3):
`heads.Add("PUAN")` → `L("PUAN")`.
- Ölçüm (kimlik): `curl '/texts?within=PlayerFormCard&paths=1'` → kart basılmadan `[]`; "Last 5 Club Games"
  döşemesine basıldıktan sonra 40–80 ms aralıkla: 0,30 sn'de 13 metin, 0,42 sn'de 113 metin +
  `formstats.GamesPlayed` **bağlı**, 0,59 sn'de 210 metin/19 satır, sonra sabit (226 metin). `/layers?name=Card`
  → `#Body › #PlayerFormCard › #ElementBindingRoot` (sayfa açıkken).
- Öncesi (eski derleme, `git stash push -- cli/fm/Matches.cs` + `dotnet build cli/fm -c Release`), üç durum
  (`fm open "Squad Dynamics"` sonrası / hemen ikinci koşu / `fm open "Messages"` sonrası) × iki dil:
  - TR: Draper human `md5 1a76c645…`, json `ed1fb048…`; kaleci Jeacock human `a8814295…`, json `97ba11ad…`;
    0 maçlık Pardington human `c142b845…`, json `f5fba54c…`.
  - EN: Draper human `52fac980…`, json `6dd18789…`; Jeacock human `36b0c5f6…`, json `a11de1ed…`;
    Pardington human `44fec348…`, json `f5fba54c…`.
- Sonrası: TR aynı üç durum × üç oyuncu → **birebir aynı md5'ler**; EN `--json` üçünde de **birebir**
  (`6dd18789…`, `a11de1ed…`, `f5fba54c…`); EN insan çıktısında tek fark tablo başlığı: `…AST\tPUAN` →
  `…AST\tRATING` (Draper) ve `…CONCEDED\tPUAN` → `…CONCEDED\tRATING` (Jeacock, Pardington).
- Negatif / sınır: `fm player "ZZZ yok" --matches` → rc=1 "'ZZZ yok' adında bir oyuncu kadroda da aramada da
  yok." (değişmedi); hiç oynamamış oyuncu (Pardington, `Apps:"0"`, satırlar "did not play") sayfayı yine
  okuyor → eski md5'lerle birebir; kaleci (xGA sütunu) ve yedek (girdi/çıktı sütunu) yolları aynı.
- Yan bulgu (eski derlemede de var): `fm open "Messages"` sonrası `player --matches` arama yolundan gidiyor ve
  "form kutusu yok (bu sezon maçı olmayabilir)" ile düşebiliyor — eski derlemede 4 koşunun 2'si (Draper, TR),
  yeni derlemede de ara sıra (Draper/Jeacock/Pardington); `fm open "Squad Dynamics"` (kadro tablosu yolu) her
  koşuda geçiyor. Koda bağlı değil (aynı derlemede hem geçen hem düşen koşu var).
- Regresyon: `tools/sweep.sh /tmp/sweep_t35` → **50 komut, 0 hata** (`matches` sweep'te yok; paylaşılan koda
  dokunulmadı, `Rows`/`Keeper` yalnız `Matches.cs` içinde).
- Dönüş/geri: kayıt yalnız okundu (2 Ağustos 2026), saat ilerletilmedi, maç oynanmadı, ayar/oyuncu değişmedi;
  dil `fm settings language "Türkçe"` ile geri alındı (`/state` → `Language: turkish`).
- Dosya: `cli/fm/Matches.cs`, `docs/BAGIMLILIKLAR.md` (B alt maddesi `[x]`, Release ayrıldı), `docs/FINDINGS.md`
  §8.224, `docs/manual/KILAVUZ.md` (başlık dili notu), bu defter.


### Tur 34 — B "Cümle parçası arayanlar" / `Stages "This round will be drawn"`: kurası çekilmemiş turun tarihi artık tarihin kendi biçiminden (§8.223, 18 Ekim)
Ne: `cli/fm/Stages.cs` `StageContent`. Kurası çekilmemiş turun tarihi
`StartsWith("This round will be drawn")` ile aranıyor, tarih `note["This round will be drawn on ".Length..]` ile
kesiliyordu. Cümleyi oyun kuruyor (içinde tarih var), eklentinin tablosundan geçmiyor:
TR `/texts?anchor=2.%20Tur&up=12&paths=1` → `"Bu tur 13/8/2026 tarihinde oynanacak"` (yol yok),
EN → `"This round will be drawn on 13/8/2026"` — aynı tur, aynı tarih. TR'de önek tutmadığı için not `null`
kalıyor, aşama boş görünüyordu. Yeni: turun metinleri içinde **dört haneli yıl + ayırıcı** taşıyan sözün tarihi
alınıyor (`Matches.Rows`un tarih tanıması gibi; sıra oyunun tarih ayarından — §C), İngilizce söz araması kalktı.
- Öncesi (eski derleme): TR `fm stages "Carabao Cup" "2. Tur"` üç durumda (başka sayfa açık `fm open "Squad
  Dynamics"` / sayfa zaten açık / ilk kez `fm open "Messages"` sonrası) → `--json`: `Note:null,Ties:null,Table:null`;
  insan: "Bu aşamada gösterilecek bir şey yok." EN aynı üç durum → `Note:"13/8/2026"` + "The draw has not been
  made yet; the draw is 13/8/2026." (yani hata yalnız TR'de).
- Sonrası: TR aynı üç durum → `Note:"13/8/2026"` + "Kura henüz çekilmedi; çekiliş 13/8/2026."; EN aynı üç durum
  → eskisiyle **birebir**. TR `fm stages "Carabao Cup" --all --json` → 8 turun 6'sında tarih (3. Tur 27/8/2026 ·
  4. Tur 3/9/2026 · Çeyrek 30/10/2026 · Yarı 18/12/2026 · Final 3/2/2027; Ön Tur/1. Tur `ties` dolu).
- Değişmeyen / negatif: lig (`fm stages "Sky Bet League One" --json` → `Note:null`, `Table` dolu; insan tablo),
  oynanmış tur (`"1. Tur"` → `Ties` dolu, `Note:null`), `stages "Carabao Cup" "ZZZ"` → aynı "aşama yok" listesi.
  `Regex` yalnız tarih biçimine bakıyor; lig tablosunda tarih yok → not uydurulmuyor.
- Regresyon: `tools/sweep.sh /tmp/sweep_t34` → **50 komut, 0 hata** (`stages` 4 sn/30 satır; `table` da geçti).
  `StageContent` yalnız `Stages.cs` içinde çağrılıyor.
- Dönüş/geri: kayıt yalnız okundu (2 Ağustos 2026 14:00), saat ilerletilmedi, maç oynanmadı, ayar değişmedi;
  dil EN ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı (`/state` → `Language: turkish`).
- Dosya: `cli/fm/Stages.cs` (yalnız `StageContent`), `docs/BAGIMLILIKLAR.md` (B maddesi bölündü, Stages `[x]`),
  `docs/FINDINGS.md` §8.223, bu defter.

### Tur 33 — B "İngilizce yazıyla basma/tıklama" / `fm release`: liste ve satırlar oyunun kimliğiyle bulunuyor; komut hiç çalışmıyordu (§8.222, 18 Ekim)
Ne: `cli/fm/Release.cs` `Release()`. Liste `dropdowns?json=1` içinde `Field(d,"Label") == "Release"` ile
aranıyordu; oyunda o listenin etiketi **"Set For Release"** → hiç bulunamıyor, komut her oyuncuda
"serbest bırakma seçeneği yok." (rc=1) diyor, `--free`/`--mutual` yolu hiç açılmıyordu. Yeni: liste
**kabıyla** bulunuyor — `Field(d,"Binds") == "selectedaction"` **ve** `Within` zincirinde
`PlayerContractCard` (şablonun adı, dilden bağımsız); etiket eşleşmesi yedek. Aynı fonksiyondaki ikinci
borç (kural 3): satırlar `select choice=` ile İngilizce sözle seçiliyordu, oysa oyunun satır yazısı
dile göre geliyor — TR'de `--free` "no choice 'Release On A Free'" veriyordu; yeni `Row()` satırı
oyunun `/translate` ile öğrenilen sözüyle (ya da İngilizcesiyle) bulup `select`e **seçilen satırın
kendi yazısını** veriyor.
- Ölçüm (kart açıkken, `/dropdowns?json=1`): `{"Name":"GenericDropdown","Binds":"selectedaction",
  "Label":"Set For Release","Selected":"-1","Choices":["Bedelsiz Serbest Bırak","Mutual Termination"],
  "Values":[null,null],"Within":[…,"PlayerContractCard","Body"]}`. `/translate?words=Release On A
  Free\nMutual Termination\nSet For Release` → `Serbest Bırak` · `Karşılıklı Fesih` · `Serbest Bırak`.
  Yani satırların yazısı ile tablo sözü ayrı: oyunun satırı "Bedelsiz Serbest Bırak" (tablo sözü onun
  sonu), `Mutual Termination` ise İngilizce geliyor — bu yüzden eşleşme hem İngilizce hem oyunun
  sözüyle, ikincisinde "onunla bitiyor" ölçüsüyle yapılıyor.
- Öncesi (bu turun başındaki derleme, TR): `fm release "Ben House"` → "Ben House için serbest bırakma
  seçeneği yok." rc=1; aynı şey Cody Tomlinson, Gael Mande, Jenson Jones, Oscar Thorn'da (yani hata
  oyuncuya değil koda bağlı). Liste gerçekten ekranda duruyordu (yukarıdaki `/dropdowns` kaydı).
- Sonrası, üç durum × iki dil (başka sayfa açık `fm open "Messages"` / kart zaten açık, ikinci koşu /
  başka sayfaya geçtikten sonra `fm open "Squad Dynamics"`):
  - TR: `fm release "Ben House"` → "sözleşmesi 30/6/2028 tarihinde bitiyor." + iki seçenek satırı;
    `fm release "Ben House" --free` (onaysız) → "Serbest Bırak - House?" + "Lincoln, House'ye £350B
    tazminat ödeyecek." + "Vazgeçildi. Serbest bırakmak için: … --free --yes" (üç durumda aynı).
  - EN (`fm settings dil English`, `/state` → `english`): `fm release "Ben House"` → "their contract
    ends on 30/6/2028." + iki seçenek; `--free` → "Release House?" + "Lincoln will have to pay £350K
    in compensation to House." + "Cancelled. To release them: … --free --yes" (üç durumda aynı).
  - İz (`FM_TRACE=1 fm release "Ben House" --free`, TR): `dropdowns` 0,53 → `translate ?words=Release
    On A Free` 0,64 → `select ?ptr=0x502f36800&choice=Bedelsiz Serbest Bırak&fire=1` 0,66 → `dialog`
    0,72/1,27 → `click` 1,42 ("Hayır"). Yani altı basışın hepsi oyunun kendi satırına gidiyor.
- Negatif / değişmeyen: TR+EN `release "ZZZ yok"` → "… adında bir oyuncu kadroda da aramada da yok."
  (rc=1); listesi olmayan oyuncu (Oscar Thorn) iki dilde de "serbest bırakma seçeneği yok." / "There
  is no option to release Oscar Thorn." — yeni eşleşme yanlış liste uydurmuyor. `--mutual` onaysız:
  "Karşılıklı fesih oyunda onaysız işliyor. Göndermek için: … --mutual --yes" (değişmedi).
- Dönüş/geri: **hiçbir oyuncu serbest bırakılmadı, hiçbir teklif gönderilmedi** — `--free`ler onaysız
  koştu (oyun "Hayır" seçildi), `--mutual` onaysız yalnız onay istemini yazdı; dil `fm settings
  language "Türkçe"` ile geri alındı (`/state` → `turkish`), kayıt 2 Ağustos 2026 14:00, saat
  ilerletilmedi, maç oynanmadı.
- Regresyon: `Release.cs` dışına dokunulmadı (`GameWords.Ask`/`Fold` var olan yardımcılar, kendileri
  değişmedi), `release` zaten `sweep.sh` komutları arasında değil → sweep gerekmedi.
- Dosya: `cli/fm/Release.cs`, `docs/BAGIMLILIKLAR.md` (B alt maddesi `[x]`), `docs/FINDINGS.md` §8.222,
  bu defter.

### Tur 32 — B "Cümle parçası arayanlar" / `fm world`: menü ve değerleri artık yazıyla değil veri yoluyla okunuyor (§8.221, 18 Ekim)
Ne: `cli/fm/Saves.cs` `World()` (`fm world`). Değerler etiketlerin yanına/öncesine bakarak alınıyordu:
`Before("Save Game")`, `After("Save Game As")`, `After("Save Game As", 2)`,
`texts.FirstOrDefault(t => t.StartsWith("Saves automatically") || t.StartsWith("Autosave"))`,
`Before("Estimated game speed:")`, `After("Selected Nations")`, `After("Selected Leagues")`,
`After("Approximate player count")`, `After("Game Status")`/`(…, 2)`/`(…, 3)`,
`After("International Competition Detail Levels")`/`(…)`; menü de `anchor=Manage settings for this game` ile
bulunuyor ve "geldi mi" işareti `texts.Contains("Selected Leagues") && texts.Contains("Estimated game speed:")`
idi — hepsi oyunun kendi sözü. Yeni: sayfa `anchorpath=game.GameTime` ile bulunuyor (veri yolu, dilden bağımsız;
ölçüm: bu kökten `up=12` eskisiyle aynı listeyi veriyor) ve sayfa `paths=1` okunuyor; her değer bağlı olduğu
yoldan alınıyor — `app.LastSaveSummary.GameSummaryGameName` (kayıt adı), `game.RealTimeSinceLastSave`,
`game.GameTimeSinceLastSave` (parantezliyse ayıklanıyor), `game.AutosaveModeAndIntervalString`,
`game.GameTime` (oynanan süre), `game.ActiveNationsCount`/`ActiveLeaguesCount`/`EstimatedPlayerCount`,
`game.DetailLevelClub/International.DisplayValue`, `Person.Name`/`Person.ManagedTeamsString`.
İşaret: iki okuma **aynı** ve `game.GameTime` ile `game.RealTimeSinceLastSave` **geldi** (0,5 sn aralık, en çok 8 sn).
Tek bir yazının yolu yok: menajerin durum rozeti (ekranda TR "Oyun", EN "Playing") bağlı değil → etiket ("Game Status")
yerine kulüp değerinin (`Person.ManagedTeamsString`) **hemen ardındaki** yazı olarak alınıyor.
- Ölçüm (okuma yolu): `curl '/texts?anchorpath=game.GameTime&up=12&paths=1'` → 36 metin; yolu olanlar yukarıdaki
  değerler, "Saves automatically…" / "Estimated game speed:" / "Save Game" / "Game Status" gibi etiketlerin yolu **yok**.
  FM menüsü `/texts?...&paths=1` ile uçtan uca okunabiliyor; rozetin İngilizcesi TR oyunda "Game Information" geliyor
  (eklentinin ters tablosu; okuma değil).
- İz (`FM_TRACE=1 fm world --json`, TR): menü 0,54'te açılıyor → `texts ?anchorpath=game.GameTime&up=12&paths=1`
  0,90 / 1,47 → işaret tuttu (0,57 sn), kapanış 1,64. Eskiden aynı yerde `anchor=Manage settings for this game` okumaları vardı.
- Öncesi (eski derleme, `git stash` + `dotnet build cli/fm -c Release`), üç durum (`fm open "Messages"` sonrası /
  menü zaten açık — curl ile `dashboard-hud-fm-button` basılıp / `fm open "Squad Dynamics"` sonrası) × iki dil:
  alanlar her iki dilde de **doğruydu** — TR `Status=Game Information`, `Played=1 Gün, 10 Saat, 26 Dakika`,
  `LastSavedAt=Oyun içindeki süreye bakarsak 09:15 gibiydi`; EN `Status=Playing`, `Played=1 Day, 10 Hours, 26 Minutes`,
  `LastSavedAt=Today at 09:15 in-game time`. Yani bu madde bir **hata düzeltmesi değil**, yazı/sıra bağımlılığını
  veri yoluna çevirme (kanıt: çıktı değişmedi, ama okuma artık etikete bağlı değil).
- Sonrası (yeni derleme, aynı üç durum × iki dil): alan alan **birebir aynı**; insan çıktısı da birebir —
  TR `md5 899c9313ba0cff8f0ba341033066ad3d`, EN `md5 b4399afafd450365728151ec2f896111` (eski derlemede de aynı iki `md5`).
  `Save`/`LastSaved`/`Nations`/`Leagues`/`Players`/`Manager`/`Club`/`Detail` alanları da aynı.
- Not (ölçüm tuzağı): `LastSaved` ("Last saved less than a minute ago" → "…1 minute ago") gerçek zamana bağlı
  değişiyor; `md5` karşılaştırmaları arka arkaya koşularda yapıldı, kalıcı karşılaştırma alan alan.
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`world` 2 sn, 6 satır;
  `/private/var/folders/…/T/opencode/sweep_t32`). `World()` yalnız `fm world`'dan çağrılıyor, başka dosyaya dokunulmadı.
- Dönüş/geri: kayıt yalnız okundu; dil `fm settings dil English` → `fm settings language "Türkçe"`
  (`/state` → `Language: turkish`); saat 2 Ağustos 2026 14:00, ilerletilmedi; maç oynanmadı.
- Dosya: `cli/fm/Saves.cs` (yalnız bu fonksiyon), `docs/BAGIMLILIKLAR.md` (B alt maddesi `[x]`), `docs/FINDINGS.md` §8.221,
  bu defter.

### Tur 31 — B "İş paneli ekran yazısıyla bulunuyordu" / `fm jobs <ilan>`: panel oyunun kendi kabından okunuyor, ulus ilanlarında başvuru durumu yanlış söyleniyordu (§8.220, 18 Ekim)
Ne: `cli/fm/Jobs.cs` (`SelectJob` + yeni dosya-içi yardımcı `JobPanel`). Panel iki ekran yazısıyla tanınıyordu:
başlıkta `Contains(job.Club)` **ve** panelde `"Training Facilities"`. İkisi de kırılgan — ulus ilanlarında başlık
oyunun kendi sözü (TR "Danimarka Menajerlik Görevi", satırın hücresi `team.name` = "Denmark") ve ulus panelinde
"Training Facilities" satırı yok → `shown=false`, `CanApply=null` → ayrıntı satırı "Başvurmak için: … apply"
diyordu, oysa `apply` reddediyordu. Yeni: `JobPanel()` = `/texts?within=job-advert-container&paths=1` (şablonun
adı, dilden bağımsız; panel kapalıyken `[]`) ve bekleme oyunun kendi işareti: kap **veri yolu taşıyan** metin
taşıyor **ve** art arda iki okuma aynı (0,25 sn aralık, en çok 5 sn). `JobDetail`/`ButtonEnabled` değişmedi.
- Ölçüm (40 ms aralıkla, `rowselect`ten sonra): 0,259 sn'de panel 3 metin/0 yol (eski başlık + iki cevap duruyor),
  0,466 sn'de yeni ilanın 9 metni (Denmark). Yani "Training Facilities" yerine gereken işaret "kap doldu **ve**
  bağlı değerleri geldi"; düğmenin kendisi (satır 0,259'da da var) tek başına yetmiyor.
- Düğme kimliği (`/buttons?json=1`): `EventToPlugin:EventFmxApplyForJob` `Enabled=False` (Denmark, Fildişi
  Sahilleri), `Enabled=True` (United States); `EventFmxDeclareInterestInJob` her ikisinde `True`. Yani "başvuru
  kapalı" cevabı oyunun kendi düğmesinden.
- Öncesi (eski derleme, `git stash` + `dotnet build cli/fm -c Release`): TR `jobs "Denmark" --json` →
  `{"…","Facts":{},"CanApply":null}` + stderr `"Danimarka" iş paneli açılmadı…`, 8–9 sn, üç durumda aynı
  (`md5 05f9b554…`); TR insan çıktısı `md5 3bd51675…`, son satır "Başvurmak için: jobs "Danimarka" apply";
  `jobs "Fildişi Sahilleri" --json` → `CanApply:null` + aynı uyarı (`md5 b1bf9395…`); EN `jobs "Denmark" --json`
  → `CanApply:null` (`md5 e66ccdda…`). Kulüp ilanı `jobs "York" --json` TR `md5 34c583c8…`, EN `md5 1ee49ee3…`.
- Sonrası: TR `jobs "Denmark" --json` → `"Facts":{"Reputation":"Worldwide"},"CanApply":false`, uyarı yok, 3–5 sn,
  üç durumda aynı `md5 99e67921…`; TR insan → son satır "Başvuru kapalı. İlgilenmek için: jobs "Danimarka"
  interest" (`md5 67aceaf8…`); `jobs "Fildişi Sahilleri" --json` → `CanApply:false` (`md5 404a2258…`); EN `jobs
  "Denmark" --json` → `CanApply:false` (`md5 3fe372d8…`), EN insan "Applications are closed. To declare
  interest: …" (`md5 dd853491…`); `jobs "United States" --json` → `CanApply:true` + "Başvurmak için: jobs
  "A.B.D." apply" (düğme `Enabled=True`) — yani pozitif yol da doğru.
- Üç durum (her biri TR + EN): başka sayfa açık (`fm open "Squad Dynamics"` sonrası, panel ilk kez açılıyor) /
  panel zaten açık (hemen ikinci koşu) / başka sayfaya geçtikten sonra (`fm open "Messages"`) → her komut üç
  durumda aynı `md5`; kulüp ilanı `York` iki dilde eski derlemeyle **birebir** (değişen tek şey ulus ilanları).
- Negatif: `fm jobs "ZZZ yok"` → `'ZZZ yok' ilanlarda yok. Liste: jobs`, rc=1 (değişmedi). EN'de
  `jobs "Fildişi Sahilleri"` → "is not among the vacancies" (oyun İngilizceyken satırın `team.name`'i başka adla
  geliyor; bu komutun kendi argüman eşlemesi, bu değişiklikten değil — öncesi de aynı, rc=1).
- Dönüş/geri: hiçbir başvuru/ilgi gönderilmedi (yalnız okuma), saat ilerletilmedi, maç oynanmadı; dil
  `fm settings language "Türkçe"` ile geri alındı (`/state` → `Language: turkish`), kayıt 2 Ağustos 2026.
- Regresyon: paylaşılan koda (Bridge/OpenPage/reply/ortak yardımcılar) dokunulmadı, `JobPanel` yalnız `Jobs.cs`
  içinde çağrılıyor → `sweep.sh` gerekmedi (sweep zaten `jobs` koşmuyor).
- Dosya: `cli/fm/Jobs.cs`, `docs/BAGIMLILIKLAR.md` (B'ye yeni alt madde `[x]`), `docs/FINDINGS.md` §8.220,
  `docs/manual/KILAVUZ.md` (başvuru durumu notu), bu defter.

### Tur 30 — A "`Task.Delay`" / `jobs apply|interest` (+ B "yazıyla basma"): basıştan sonraki sabit 1 sn yerine oyunun cevabı, iki düğme olay kimliğiyle (§8.219, 18 Ekim)
Ne: `cli/fm/Jobs.cs` (`fm jobs "<kulüp>" apply|interest`). İki şey birlikte temizlendi (kural 3):
(1) `JobAnswer`'daki `await Task.Delay(1000)` + `dialog` okuması → "`dialog` doldu **veya** düğme artık sunulmuyor"
(0,25 sn aralık, en çok 6 sn); (2) düğme arayışı artık İngilizce etiketle değil oyunun olayıyla — `JobButton` /
`ButtonEnabled`, `/buttons?json=1` içinde `Events` alanında `EventFmxApplyForJob` / `EventFmxDeclareInterestInJob`
(TR + EN'de aynı, `docs/reference/enums.txt`) arıyor, basış işaretçiyle. `SelectJob`'un `CanApply` okuması da aynı
olaya bağlandı. Başka dosyaya dokunulmadı; `JobButton` yalnız burada çağrılıyor → `sweep.sh` gerekmedi (sweep zaten
`jobs` koşmuyor).
- İşaret ölçümü (python, `/press?ptr=` + 40 ms aralıkla `/dialog` ve düğmenin `Enabled`'i): `Apply For Job`
  (York) → düğme **0,467 sn**'de `Enabled=False`, `dialog` **13,5 sn boyunca boş**; `Declare Interest In Job`
  (aynı kulüp) → **0,629 sn**, `dialog` yine hep boş. Yani eski sabit 1 sn tam o anda boş `dialog` okuyordu
  (makine hızı tahmini).
- Öncesi (eski derleme `git stash` + derle, TR): `FM_TRACE=1 fm jobs "York" apply --yes` → `press ?ptr=` 3,32 →
  **tek** `dialog` 4,43 (1,11 sn) → `York (Sky Bet League Two) işine başvurulacak.` + `Gönderildi. Cevap gelen
  kutusuna düşer: inbox`.
- Sonrası (yeni derleme), üç durum (sayfa ilk kez açılıyor `fm open "Messages"` / zaten açık / başka sayfa açık
  `fm open "Squad Dynamics"`):
  - TR: `York apply`, `Celesti interest`, `Udinese interest` → üçü de rc=0, `… işine başvurulacak/ilgi bildirilecek.`
    + `Gönderildi. Cevap gelen kutusuna düşer: inbox`; izde `press` → kabul **0,56 / 0,50 / 0,37 sn** (okumalar
    `dialog`+`buttons` çiftleri, düğme `Enabled=False` olunca bitti).
  - EN (`fm settings dil English`, aynı üç durum): `York interest`, `Celesti interest`, `Udinese interest` → üçü de
    rc=0, `Sent. The answer will come to the inbox: inbox`; `press` → kabul **0,75 / 0,20 / 0,52 sn**.
  - Çıktı **eskisiyle aynı** (dil başına tek satır; eski derleme basıştan 1 sn sonra boş `dialog` okuyup aynı satırı
    yazıyordu — python ölçümü `dialog`'un 1,0 sn'de de boş olduğunu gösteriyor).
- Veriden doğrulama: `fm jobs "York"` basıştan sonra "Başvuru kapalı. İlgi bildirmek için: …" (başvuru gerçekten
  gitmiş; `--json` → `CanApply: false`).
- Negatif: TR `fm jobs "Villarreal" apply --yes` → `Villarreal başvuru kabul etmiyor. İlgi bildirmek için: …`, rc=1
  (düğme olayla bulundu, `Enabled=false` → hiç basılmadı). EN'de ulus işleri (`Denmark`, `Brazil`) panelde
  "Training Facilities" satırı olmadığı için `CanApply` zaten bilinmiyordu (eski kodda da öyle) → `does not take
  applications`, rc=1; bu değişiklikten değil.
- Dönüş/geri: ölçümün altı basışı yalnız bellekteydi; kayıt `fm load "Faruk Kavlak - Fenerbahçe" --no-save` +
  `fm continue` ile **baştan yüklendi** (2 Ağustos 2026, `fm jobs "York"` → "Başvurmak için…", yani York geri geldi).
  Dil `fm settings language "Türkçe"` → `/state` → `turkish`. Saat ilerletilmedi, maç oynanmadı.
- Dosya: `cli/fm/Jobs.cs` (dosyada `Task.Delay` kalmadı), `docs/BAGIMLILIKLAR.md` (A + B maddeleri `[x]`),
  `docs/FINDINGS.md` §8.219, bu defter.

### Tur 29 — B "Cümle parçası arayanlar" / `fm penalties` yer sütunu: iki dilde iki ayrı söz, dilden bağımsız yol yok → PARK (§8.218, 18 Ekim)
Ne: `cli/fm/Penalties.cs` `Starting()` (satır 119) ve okuduğu alan
(`PenaltyTakers`, satır 150: `tactic.TeamSelectedPositionStringFromPlayer`). **Kod değişmedi** (kural 4: kanıtlanmış
biçimde daha iyisi bulunamadı).
- Ölçüm komutu: `fm penalties --json` (2 Ağustos 2026 14:00 kaydı). Tek taker James Collins. Sonra
  `fm penalties add "Tom Bayliss"` (ilk 11'de) → aynı komut → `fm penalties remove "Tom Bayliss"` (sıra tur
  başındaki hâle döndü, doğrulandı: tek satır `1 9 James Collins yedek`).
- TR `Where`: `"On Subs bench"` (kulübe) · `"İlk 11'de (DOSMSğ)"` (ilk 11) — ikincisi ham oyun cümlesi.
  İnsan tablosu: kulübe `yedek`, ilk 11 `İlk 11'de (DOSMSğ)` (önek eşleşmediği için normalize edilmiyor).
- EN (`fm settings dil English`, `/state` → `english`, aynı iki oyuncu): `"On Subs bench"` / `"Starting at DMCR"`.
  İnsan tablosu: `sub` / **`ilk 11 · DMCR`** — İngilizce çıktıda Türkçe söz (önek `Starting()`te sabit yazılıyor).
- Yani aynı alan aynı kayıtta dil başına başka bir söz: mevki kodu taşıyan cümleyi eklenti çeviremiyor, kodsuz
  olanı çeviriyor.
- Neden düzeltilemedi (ölçüm): `/form?anchor=Order&up=26` bu satır için yalnız `Path` + `Text` veriyor (dile bağsız
  "ilk 11/kulübe" alanı yok, satırın başka hücresi de yok). `/tactic` Slot'ları (`Player`: `Bayliss`, sayısal
  `Position` maskesi) ile birleştirmek kısaltılmış ad ↔ tam ad eşlemesi yüzünden kırılgan. Yapı kuralı da yok:
  TR'de parantez var (`(DOSMSğ)`), EN'de yok (`DMCR`).
- Dönüş/geri: `fm settings language "Türkçe"` → `language: English → Turkish`, `/state` → `Language: turkish`;
  penaltı sırası ve kuralları tur başındaki hâlde (kurallar kapalı, tek taker). Saat ilerletilmedi, maç oynanmadı.
  Kod değişmedi → `sweep.sh` gerekmedi.
- Faruk'a soru: yer bilgisi dilden bağımsız nereden gelmeli — eklentiye slotun oyuncusunu söyleyen bir alan mı
  eklensin (o zaman ceza satırıyla kimlik üzerinden birleşir), yoksa `/tactic` Slot `Position` maskesi CLI'nin
  kendi slot adlarına çevrilip tabloda o mu yazılsın?
- Doküman: `docs/BAGIMLILIKLAR.md` (B'de alt madde `[?]`), `docs/FINDINGS.md` §8.218.

### Tur 28 — B "İngilizce yazıyla basma/tıklama" / `propose`: öneri ekranının iki düğmesi eylem/kimlikle bulunuyor; TR'de hiç çalışmıyordu (§8.217, 18 Ekim)
Ne: `cli/fm/Propose.cs` (`fm propose [--loan]`, §8.94). Akış iki düğmeyi oyunun İngilizce sözüyle arıyordu:
listedeki "Offer via TransferRoom" (`Arriving("press", text=…)`) ve teklif ekranının gönder düğmesi "Offer Player".
İkincisi TR oyunda **hiç İngilizce gelmiyor** (etiket ekranda `Oyuncuyu Kulüplere Öner`) → `propose` 8 sn bekleyip
`Önerme ekranı açılamadı.` ile rc=1. Yeni: ikisi de eylemle bulunuyor — `SpeakToAction("1164992875")`
(`InteropEventIDs.PersonOfferToClubs`) ve yeni yardımcı `SpeakToActionEvent("32")`
(`EventFmxTransferOfferResponse`, eklentinin `Action` düğümünün `Event`'i; `IsExitButton=True`). Yazı aranmıyor;
dördüncü sırada kalan "artık yazıyla basma" borcu bu fonksiyonda kalmadı (`Offered()` işareti ve gönder sonrası
bekleme de kimliğe çevrildi).
- Kimlik ölçümü (`/buttons?json=1&gen=<taze>` + `/pull?path=…&props=ActionID,Event,IsExitButton`; her okuma için
  ayrı `gen`, yoksa `{}` dönüyor): TR 'Offer via TransferRoom' → `ActionID 1164992875`, `Event 0`;
  TR `Oyuncuyu Kulüplere Öner` → `ActionID 0`, `Event 32`, `IsExitButton True`; EN 'Offer Player' → aynı
  `Event 32`. `docs/reference/enums.txt`: 1164992875 = `PersonOfferToClubs`, 32 = `EventFmxTransferOfferResponse`.
  `/translate?words=Offer Player` (TR) → 'Oyuncuyu Takasa Kat' (başka metin) — tablo çakışması, yazı yolu kapalı.
- Öncesi (eski derleme, TR): `fm propose "Ben House"` ve `--loan`, üç durumda (`fm open "Messages"` sonrası /
  hemen ikinci koşu / `fm open "Squad Dynamics"` sonrası) → beş koşunun hepsi `Önerme ekranı açılamadı.`, rc=1.
  EN (eski derleme, üç durum): fee `md5 f97a868853f2cf6a8767a9f03a3292ef`, kiralık `md5 d030b63f27837cb2cf54b920a2b841aa`.
- Sonrası: TR `fm propose "Ben House"` üç durumda aynı (`md5 eb6329d8488790acaeb03ec6e08f0a2c`), `--loan` üç
  durumda aynı (`md5 adad8e6fa5c9ae0f8b33cac14f576db1`); ikinci oyuncu TR `Freddie Draper` üç durumda aynı
  (`md5 55976f65ae9efdea71bf1c425f73fa95`, kiralık `3faf6e004d84c9a45212c700a1fc6fcc`) → **TR'de ilk kez çalıştı**.
  EN (yeni derleme, üç durum): fee `f97a8688…`, kiralık `d030b63f…` → **eski derlemeyle birebir**; `Freddie Draper`
  EN üç durum aynı (`md5 e615cfa23d74fbc7d1684716d0a06f1`). Süre: EN 10,5 → 9,4–10,7 sn (bekleme işaret gelince
  bitiyor), TR 9,4–11,8 sn.
- Gönderme yolu (aynı fonksiyon, kimliğe çevrilen son basış): `fm propose "Ben House" --loan --yes` → "Önerildi.
  İlgilenen kulüpler önümüzdeki günlerde gelen kutusuna düşer." (rc=0); hemen sonra `fm propose "Ben House"`
  → "There is no option to offer…" (iki seçenek listeden kalktı, §8.94'ün beklenen hâli). Geri almak için
  "Remove From Loan List" ekranda basıldı + onay penceresi (`/dialog`, Yes) cevaplandı → kiralık listesi temizlendi;
  **TransferRoom önerisi kalkmadı** (oyunun kendi geri alma yolu yok, `--yes` zaten onay istiyordu).
- Negatif: `fm propose "ZZZ yok"` → öncesi/sonrası aynı ("Oyuncu bulunamadı" yolu, rc=1); `--loan` + ücret →
  rc=64 mesajı değişmedi; benimsenmeyen oyuncu yolu (kart değeri boş) değişmedi.
- Yan bulgu (§8.217 sonu): `fm actions "<oyuncu>" "Remove From Loan List"` / `"Remove From Transfer List"` →
  "No '…' in the menu." — oysa iki düğme de ekranda ve `SpeakToAction` listesinde; `fm actions` oyuncunun bu
  aksiyon listesini göremiyor (ayrı iş; bu turda `propose` dışına dokunulmadı).
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (paylaşılan `Acknowledge.cs`'e yardımcı eklendi).
  Kayıt test kaydı: 2 Ağustos 2026 14:00; saat ilerletilmedi, maç oynanmadı; Ben House önerilmiş kaldı (yukarıda).

### Tur 27 — A "`Task.Delay`" / TrainingSchedule 345: ölçüldü, yazıldı, üçüncü durum + İngilizce tamamlanamadı → PARK (§8.216, 18 Ekim)
Ne: `cli/fm/TrainingSchedule.cs` `PressAndConfirm` (`fm schedule set|style`). Seçenek basıldıktan sonra açıklamayı
okumadan önceki **sabit 500 ms** kaldırılıp oyunun kendi cevabına bağlanacaktı: "okuma, basıştan **önceki hâlden
farklı** ve `About(texts)` bir şey söylüyor" (0,3 sn aralık, 6 sn üst sınır). Kod yazıldı, derlendi, ölçüldü; üçüncü
durum + İngilizce tamamlanamadığı için **kural 4 gereği geri alındı** (`git restore cli/fm/TrainingSchedule.cs`,
ağaç temiz, `dotnet build` yeniden yapıldı).
- Ölçüm (okuma yolu, hiçbir şey öğretmiyor): `python3` + `/open?guid=…` + `/form?anchorpath=day.DayOfTheMonth` +
  `/click?ptr=` (seans düğmesi) + `/form?anchor=Confirm&up=10&buttons=1`; basış `/press?ptr=`. Altı basışta yeni
  açıklama **107 · 230 · 262 · 312→369 · 339 · 364 · 371 ms**'de oturdu; bir koşuda arada **6 metinlik** boşaltılmış
  bir an (Defending, 312 ms) görüldü → eski 500 ms'in payı 1,3–2,2 kat, yani makine hızı tahmini.
- Öncesi (eski derleme, TR): `fm schedule set 3 1 "General"` → `3 Ağustos Pazartesi, 1. seans: Physical → General` +
  `Oyuncuların teknik, zihinsel ve fiziksel olarak çalıştırıldıkları idman. Takım halinde çalışılır.`,
  `md5 0aaf9592c6c1f0b1ad17467e76369a00`, 10,2 sn; `… "Physical"` → `General → Physical` +
  `Oyuncuların fiziksel olarak çalıştırıldığı idman. Takım halinde çalışılır.`,
  `md5 414fade63ba17d568d39592753611763`, 9,6 sn.
- Sonrası (yeni derleme, TR): aynı iki geçiş **birebir aynı md5** (`0aaf9592…`, 14,8 sn / `414fade6…`, 8,5 sn),
  açıklama satırı seçilen seansın kendi cümlesi. Durumlar: `fm open "Messages"` sonrası (takvim ilk kez açılıyor) ve
  hemen ardından (takvim zaten açık). Süre nötr–hafif yavaş (ek bir "basıştan önceki hâl" okuması).
- Veriden doğrulama: `fm schedule` → `Pzt 3 Ağustos 1. seans` her koşudan sonra doğru seans (Genel ↔ Fiziksel),
  tur sonunda **başlangıçtaki hâline** döndü (`Fiziksel`).
- Takılma (bu maddenin işi değil): `fm schedule`/`schedule set` ara sıra `Takvimin antrenman sekmesi okunamadı.`
  (rc=1, ~20 sn) veriyor — takvim sayfası `General` ve `Training` sekmeleri için **ayrı** `MonthDropdown` tutuyor;
  sekmeler ayrı aya bakabiliyor (biri Ağustos, öteki Haziran) ve `CalendarMonths` etiket boş gelince yanlış aya
  (`value=0` = Haziran) geçiyor. Ölçüm: `/dropdowns?json=1` iki `MonthDropdown` verdi (`Label` '' ve `Ağustos 2026`),
  `select?ptr=&value=1`→`value=2` her ikisinde de yapılınca koşular geçti; **eski derlemede de aynı** (koda bağlı
  değil) → `md5 d41d8cd98f00b204e9800998ecf8427e` (boş) ve aynı hata.
- İngilizce deneme yarıda kaldı: `fm settings dil English` (`/state` → `english`), sonra
  `fm schedule set 3 1 "General"` → `There is no session called 'General'. …` (seçenek adları sayfa yenilenmediği
  için oyunun o günkü sözüyle geliyor, §8.194); ardından gelen iki koşu takvim hatasına düştü. Dil
  `fm settings language "Türkçe"` ile geri alındı (`English → Turkish`, `/state` → `turkish`).
- Dönüş/geri: saat ilerletilmedi (2 Ağustos 2026 14:00), maç oynanmadı, seans planı tur başındaki hâlde; kod
  değişmedi → `sweep.sh` gerekmedi (paylaşılan koda dokunulmadı; `PressAndConfirm` yalnız bu dosyada).
- Faruk'a soru: takvim ayı karışıklığı ayrı bir madde olarak düzeltilsin mi (o zaman bu madde üç durum + iki dilde
  bitebilir), yoksa ölçüm için başka bir yol mu seçilsin?

### Tur 26 — A "`Task.Delay`" / Responsibilities 283 388: sabit 500 ms yerine listenin "baştayım" cevabı (§8.215, 18 Ekim)
Ne: `cli/fm/Responsibilities.cs` (`fm responsibilities give|take|delegate`). Sayfanın görev listesi uzun; komut, görevin
satırına inmeden önce listeyi `scroll ?anchor=Take Control of All|Delegate All to Recommended&to=0` ile başa alıp
**sabit 500 ms** bekliyordu (`GiveDuty` 283, `SetDuty` 388). Bekleme yalnız satırların ne zaman çizildiğine dair bir
tahmindi. Yeni yardımcı `ToTop()`: aynı kaydırma gönderiliyor ve **listenin kendi cevabı** bekleniyor — `scroll` nereye
oturduğunu söylüyor (`to=0` → `0 of 728,6`; baştayken ilk sayı `0`), `Until` 0,3 sn aralıkla en çok 6 sn. Satırların
çizilmesini hemen ardından gelen `Duties()` okuması karşılıyor ("içerik değişti **ve** iki okumada aynı").
- Ölçüm (okuma yolu, hiçbir şeyi değiştirmiyor): python3, `scroll?anchor=Take Control of All` → `to=1` `728,6 of 728,6`,
  `to=0` `0 of 728,6` (iki kez aynı), `to=0.5` `322 of 644`. `to=1` sonra `to=0`: `/form?anchor=Take Control of All&up=12`
  40 ms aralıkla okundu → satırlar **0,35 sn**'de oturdu → 500 ms makine hızı tahminiydi.
- Öncesi (eski derleme): `fm responsibilities give "Assigning Scouts"` TR dört durum (sayfa ilk kez `fm open "Messages"`
  sonrası / zaten açık / `fm open "Squad Dynamics"` sonrası / liste curl ile dibe kaydırılmış) → **hepsi `rc=0`,
  `md5 570b1bd243e4d67a2f4800b7588ab50b`**, 11–14 sn; çıktı `Gözlemcilik Görevlendirmelerini Yapar: Jez George` +
  `Verilebilecekler: Jez George · Joe Hutchinson · Faruk Kavlak`. EN dört durum → `md5 0030afd6c876a5bc3197cca3da59e1b7`,
  11–12 sn (`Assigning Scouts: Jez George` / `Can be given to: …`).
- Sonrası: aynı sekiz koşu → **birebir aynı md5'ler** (TR 570b1bd2…, EN 0030afd6…), süre 11–15 sn (nötr); negatif
  `fm responsibilities give "ZZZ yok"` → rc=64 `'ZZZ yok' diye bir görev yok` (EN `There is no duty called 'ZZZ nope'`),
  md5 eskiyle aynı. İz (`FM_TRACE=1`): `select …choice=Scouting Responsibility` (10,20) → `scroll …to=0` (10,27, ToTop)
  → `buttons`/`form` (10,35 / 10,40, okuma) → `form` (10,90, iki okuma aynı) → `scroll …to=0` (10,97, `Duties`in kendi
  taraması) → `dropdowns` (11,00): sabit beklemenin yerinde artık listenin cevabı var.
- Yazma yolu (aynı yardımcının ikinci çağrı yeri, `SetDuty`): liste elle dibe kaydırılıp `fm responsibilities take
  "Assigning Scouts" --yes` → `Gözlemcilik Görevlendirmelerini Yapar: artık sende.` (rc=0, 14 sn); geri: `fm
  responsibilities give "Assigning Scouts" "Jez George" --yes` → `Gözlemcilik Görevlendirmelerini Yapar: Faruk Kavlak
  → Jez George` (rc=0, 11 sn). Veriden doğrulandı: `fm responsibilities scout --json` → `Assigning Scouts | delegated |
  Jez George` (tur başındaki hâli).
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`/tmp/sweep_t26`). Paylaşılan koda (Bridge/OpenPage/reply)
  dokunulmadı, `ToTop` yalnız bu dosyada çağrılıyor.
- Dönüş/geri: dil `fm settings dil English` → `fm settings language "Türkçe"` (`/state` → `Language: turkish`),
  2 Ağustos 2026 14:00; kişi verilmediği için **hiçbir görev devredilmedi**, saat ilerletilmedi, maç oynanmadı.
- Dosya: `cli/fm/Responsibilities.cs` (yalnız; dosyada `Task.Delay` kalmadı), `docs/BAGIMLILIKLAR.md` (madde `[x]`),
  `docs/FINDINGS.md` §8.215.

### Tur 25 — B "Cümle parçası arayanlar" / `player --career`: satırlar başlık yazısıyla değil satırın kendi hücresiyle bulunuyor; komut Türkçede hiç çalışmıyordu (§8.214, 18 Ekim)
Ne: `cli/fm/Career.cs` (`fm player <oyuncu> --career`). Satırların başladığı yer `texts.IndexOf("Avg Rat") + 1` ile
bulunuyordu; başlık oyunun sözü ve eklenti her dilde başka bir İngilizce veriyor (EN `Info`/`Avg Rat`, TR
`Information`/`Avg. Rating`). Türkçede `IndexOf` -1 → `start = 0` → `start <= 0` koruması → hata. Yeni:
`start = texts.FindIndex(SeasonMark.IsMatch)` (satırın kendi ilk hücresi sezon), koruma `start < 0`. Başka dosyaya
dokunulmadı; kalecinin `Cln Sh` sütunu iki dilde de aynı geldiği için dokunulmadı → `sweep.sh` gerekmedi
(sweep zaten `player --career` koşmuyor, paylaşılan koda dokunulmadı).
- Öncesi (eski derleme): TR üç durum (kart ilk kez açılıyor `fm open "Messages"` sonrası / zaten açık / başka sayfa
  açık `fm open "Squad Dynamics"`) + kaleci `Zach Jeacock` → **rc=1**, `md5 a797f52cb11838fd711552e432048867`,
  "Kariyer tablosu okunamadı.". EN aynı üç durum → rc=0, `md5 0dfdaa7473271f10f0e28766a6d1d2e7`.
- Sonrası: TR üç durum + kaleci → rc=0, `md5 505029b89e88f455a296737967297581` (hepsi aynı), 15 sezon,
  `Fees 30B £`, `Totals ["206","43","2","2"]`; insan çıktısı `Toplam: 206 maç, 43 gol · ödenen bonservisler
  30B £`, "GELİŞ" sütunu `kiralık`/`bedelsiz`. EN üç durum → rc=0, `md5 0dfdaa74…` — eski derlemeyle **birebir**.
- Ölçüm (okuma yolu, `python3` + `/texts?anchor=Total Apps&up=16`): EN'de başlık `[...,'Info',...,'Avg Rat']`,
  TR'de `[...,'Information',...,'Avg. Rating']`; kaleci sayfasında ikisi de `Cln Sh`. Yani kırılan tek şey başlık
  yazısına çapalanan `start`.
- Negatif: `fm player "ZZZ yok" --career` → rc=1 "…adında bir oyuncu kadroda da aramada da yok."; `Zane Okoro`
  (4 sezon), `Jenson Jones` (5 sezon, `How 165B £`), `Gael Mande`, `Cody Tomlinson` rc=0. Bu kayıtta kariyeri boş
  oyuncu yok → `marks.Count == 0` yolu ölçülemedi (eski derlemede de aynı).
- Dönüş/geri: `fm settings language "Türkçe"` → `English → Turkish`, `/state` → `Language: turkish`, 2 Ağustos 2026
  14:00 (saat ilerletilmedi, maç oynanmadı).
- Not (bu maddenin dışı): JSON `How` alanı hâlâ oyunun ham sözü (`Loan`/`Free` TR'de, `loan`/`free` EN'de; insan
  çıktısı `How()` ile CLI'nin sözünü yazıyor); dil değişiminden sonra sayfa yenilenmezse bağlı yazılar eski dilde
  kalıyor (`Nation: İngiltere` İngilizce oyunda — §8.194).

### Tur 24 — A "`Task.Delay`" / Scouting 100: kaydırmadan sonraki sabit 700 ms pencerenin işaretine bağlandı (§8.213, 18 Ekim)
Ne: `cli/fm/Scouting.cs` (`fm scout <#|ad>`), + yeni dosya-içi yardımcı `Window(rows)`. Başka dosyaya dokunulmadı;
`Window` yalnız burada çağrılıyor → `sweep.sh` gerekmedi (paylaşılan koda dokunulmadı).
- Öncesi (eski derleme, `git show HEAD:cli/fm/Scouting.cs`): `fm scout "England" --json` TR üç durum
  (başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / `fm open "Squad Dynamics"`) **20 satır**,
  `md5 6915efcbaa79fecda145857e4769ccc0`, 8 / 8 / 9 sn. EN üç durum aynı komutlar **20 satır**,
  `md5 86ccb87b46c3e3ed34d7f419e91e4362`, 8 / 8 / 7 sn.
- Ölçüm (okuma yolu, geçici `python3`: `/scroll?anchor=<son satırın değeri>&to=0.5` + 30 ms aralıkla
  `/texts?anchorpath=selectedfocus.Name&paths=1`): tablo penceresi 18 satır, `Matched` 20 → kaydırma dalı
  gerçekten çalışıyor. Kaydırmadan sonra satır adları 207–485 ms'de değişiyor; tam içerik (ad + değer) art arda
  iki okumada 690–1080 ms'de oturuyor → eski 700 ms tam sınırdaydı (makine hızı tahmini).
- Sonrası: aynı üç durum × iki dil → TR `md5 6915efcb…` (20 satır), EN `md5 86ccb87b…` (20 satır); `diff` boş,
  yani **birebir**. Satırların hepsinde değer + derece dolu (`Value`/`Grade` boş yok). Süre 7–10 sn (nötr).
- İşaretin tuttuğu (`FM_TRACE=1 fm scout "England" --json`): `scroll` 8,27 sn → okumalar 8,33 (eski pencere,
  `key == before` → red) / 8,64 (yeni pencere, `key != hold` → bekle) / 9,06 (aynı → kabul, 20 satır). Kaydırmadan
  kabule 0,79 sn; eski 0,70 sn + tek okuma. Yani kaldırılan yalnızca makine hızına dair varsayım.
- Negatif: `fm scout "ZZZ yok" --json` → rc=64, odak listesi; `fm scout 999 --json` → rc=64. Kaydırmasız yollar:
  `fm scout "England" --near` 6 satır, `--progress --json` 10 satır, ikisi de rc=0.
- Bilinen (bu maddenin dışı, eski davranış): `fm open "Messages"` hemen ardından odak listesi bir kez boş
  okunabiliyor (`Odak: 'England' diye bir seçenek yok`, rc=64) — hem eski hem yeni derlemede görüldü; §8.185 hâli.
- Dönüş: `fm settings dil English` → `dil: Turkish → English` (EN ölçümü), sonra `fm settings language "Türkçe"`
  → `English → Turkish`, `/state` → `Language: turkish`. Kayıt test kaydı: 2 Ağustos 2026, saat ilerletilmedi,
  maç oynanmadı.
- Park / dikkat: `cli/fm/Responsibilities.cs` ağaçta commit edilmemiş duruyor **ve** `stash@{0}` aynı maddenin
  (283/388 sabit 500 ms) daha büyük bir sürümünü tutuyor; ikisi de bu turun işi değil, **dokunulmadı**. Bir
  sonraki tur önce hangisinin geçerli olduğunu seçmeli.

### Tur 23 — B "`opposition` seçenek adları": değer hem İngilizce hem ekranda görünen sözle kabul ediliyor (§8.212, 18 Ekim)
Ne: `cli/fm/OppositionCard.cs` (+ `docs/BAGIMLILIKLAR.md`, `docs/FINDINGS.md`). Paylaşılan koda dokunulmadı → sweep gerekmedi.
- Sorun (önce, TR): `fm opposition "Kyle Knoyle" "Pressing=Her Zaman"` → `Pres: 'Her Zaman' yok. Seçenekler: - · Hiçbir Zaman · Her Zaman`,
  kartta `Pressing` `-` kaldı. Sebep: eklenti seçenekleri İngilizce (`-`,`Never`,`Always`; `Normal`,`Minor`,`Gusty`;
  `Left/Right/Weak Foot`), ama ekrana basılan ad `GameWords` yazıcısından geçip oyunun sözü oluyor; gösterilen ad geri yazılamıyordu.
- Çözüm: değer, seçeneğin İngilizcesiyle ve `GameWords.Of(seçenek)` ile eşleşiyor (sabit söz/tablo yok, liste hâlâ eklentiden).
- Üç durum TR (`Her Zaman`/`Hiçbir Zaman`, `Sert`/`Hafif`, `Sol Ayak`, `Hard`): kart açık / `fm open Messages` / `fm open Squad`
  → hepsi rc=0 ve `fm opposition --json` kartı istenen değeri yazdı (`Always`/`Never`/`Gusty`/`Minor`/`Left Foot`/`Weak Foot`).
- Üç durum EN (`Always`/`Never`/`Hard`): hepsi rc=0, kart doğru. Negatif iki dilde rc=1 (`Pres: 'zzz' yok…` / EN `Pressing: no 'zzz'…`).
- Dönüş: `fm settings Language "Türkçe"` → "English → Turkish"; son TR koşusu `Pressing=Her Zaman`+`Tackling=Hafif`+`Foot=Sol Ayak`
  → `Always`/`Minor`/`Left Foot`. Sonra `fm opposition --clear` → dört sütun `-`. Kayıt test kaydı, saat ilerletilmedi, maç oynanmadı.

### Tur 22 — A "`Task.Delay`" / `cli/fm/Numbers.cs` 158 177: alt sayfa ve kaydırma sabit duraklamaları oyunun işaretine (§8.211, 18 Ekim)
Ne: `cli/fm/Numbers.cs` (Tur 20'den kalan commit edilmemiş iş; bu turda derlenip ölçüldü, küçük eklemelerle).
- Öncesi (eski derleme, `git stash` + build): `OpenNumbers` alt sayfa düğmesinden önce sabit 1,5 sn;
  `ReadNumbers` her kaydırmadan sonra sabit 400 ms. Gerçek hata: `fm open Messages` sonra `fm numbers --json`
  EN'de 6 koşunun **2'sinde** `Forma numaraları okunamadı.` rc=1 (0,94 sn); tablo bir an durup okuma gelince gidiyor.
  TR'de aynı desende 6/6 geçti (yarış dile bağlı değil, zamana bağlı).
- Sonrası: boş okumada sayfa yeniden isteniyor → EN 6/6 rc=0, 37 oyuncu; TR 5/5 rc=0, 37 oyuncu.
- Üç durum × iki dil (başka sayfa açık / sayfa zaten açık / sayfa ilk kez `fm open Messages`):
  TR `md5 a7fde227c69469540409cb405b5a14de` (eskisiyle aynı), EN `md5 e7bf7cebfdb3a855d06cb1b3fecad5dc`
  (eskisiyle aynı), her dilde üç koşu da birbirinin aynı.
- `stopAt` yolu (numarayı elle verme): TR `fm numbers "Adam Reach" 44` → `- → 44 (henüz kaydedilmedi)`, rc=0;
  EN `fm numbers "Oscar Thorn" 45` → `- → 45 (not saved yet)`, rc=0; negatif `fm numbers "Nobody Here" 3` →
  "kadroda yok", rc=1. Kayıt geri alındı: iki numara `-`e çevrildi, `md5` baştaki değere döndü.
- Dil dönüşü: `fm settings language "Türkçe"` → `/state` → `Language: turkish`. Saat 2 Ağustos 2026 14:00,
  ilerletilmedi; maç oynanmadı.
- Regresyon: `tools/sweep.sh /tmp/sweep_t23` → **50 komut, 0 hata** (`numbers` 6 sn, 42 satır).
- Süre: nötr (EN "zaten açık" 4,9 → 5,0 sn); kaldırılan yalnızca makine hızına dair varsayım.

### Tur 21 — B "İngilizce yazıyla basma/tıklama" / `contract <oyuncu> --agent` satırı: satır oyunun eylem kimliğiyle, sabit 1 sn yerine konuşmanın işaretiyle (§8.210, 18 Ekim)
Ne: `cli/fm/Release.cs` (+ `Acknowledge.cs`e taşınan `SpeakToAction` yardımcısı, `TraitTraining.cs` onu kullanıyor).
Bu turun bir önceki koşusu kodu ve dokümanı yazmış, 40 dk sınırında commit atmadan öldürülmüştü; bu tur kodu
derleyip üç durumda iki dilde yeniden ölçtü ve commitledi.
- Öncesi (ölü koşunun ölçümü, §8.210): TR'de `press text="Discuss New Contract With Agent"` sözü oyunun kendi
  konuşma cümlesi ve tabloda İngilizcesi yok → üç durumda da `… menajerle görüşme seçeneği yok`, rc=1. Sabit 1 sn
  de tahmin: basıştan sonra cevap satırları 744 ms'de boşalıp 1145 ms'de doluyor.
- Sonrası (bu tur, `/tmp/t22/`): EN üç durum rc=0, 12 satır — başka sayfa açık Reeco Hackett `What can I do for
  you?`, kart zaten açık Jack Moylan `Hey! What did you want to talk about?`, sayfa ilk kez (`fm open "Messages"`)
  Ryley Towler `What can I do for you?`. TR üç durum (Oscar Thorn) rc=0, 12 satır, üçünde de
  `Benimle mi görüşmek istediniz hocam?` + 4 cevap. Her koşudan sonra konuşma `PanelHistoryClose` ile kapatıldı.
- Negatif: `fm contract "ZZZ yok" --agent` → rc=1, `'ZZZ yok' adında bir oyuncu kadroda da aramada da yok.`
- Kalan (yeni madde): TR'de kimi koşuda satır bulunamıyor — Tom Hamer ve Zach Jeacock aynı oturumda önce rc=0,
  sonra `seçenek yok`; Josh Honohan, Ben Waine, Erik Ring EN'de rc=0, TR'de `seçenek yok`. O sırada ekranda
  `Action` alanı boş bir sözleşme görüşmesi kartı (`Finalise Promises` / `Cancel Negotiations`) görüldü; yani iş
  tümden dile bağlı değil, kartın hangi oyuncuda ne durduğuna bağlı görünüyor → BAGIMLILIKLAR B'ye madde yazıldı.
- Dönüş: `fm settings language "Türkçe"` → `English → Turkish`, `/state` → `Language: turkish`; saat ilerletilmedi,
  maç oynanmadı, hiçbir şey söylenmedi (her konuşma `PanelHistoryClose` ile kapandı).
- Regresyon: ilk `tools/sweep.sh` koşusu **tuzağa düştü** (§8.198, Tur 9'dakinin aynısı): `scout` 30 sn'de zaman
  aşımına uğradı, sonraki iki komut `Kayıt yüklü değil` (rc=69) → sweep 24 komutta durdu. Oyun `fm start` +
  `fm continue` ile aynı kayda döndü (2 Ağustos 2026 09:15; saat ilerletilmedi, maç oynanmadı) ve ikinci koşu
  **50 komut, 0 hata** (`/tmp/sweep_t22b`). İlk koşudaki hata kodun kendi hatası değil; kayıt yüklenince geçti.
- Bilerek bırakılan: `cli/fm/Numbers.cs`teki commit edilmemiş iş (başka madde) dokunulmadı.


### Tur 20 — B "İngilizce yazıyla basma/tıklama" / `training --trait-suggest`: düğme artık oyunun eylem kimliğiyle (§8.209, 18 Ekim)
Ne: `cli/fm/TraitTraining.cs`. Düğme `press text="Ask For Trait Training Suggestion"` ile aranıyordu; eklenti sorgu
metnini oyunun tablosundan o dile çevirdiği için bu yol dilin tablosuna bağlıydı ve o sözün İngilizcesi tabloda
**yok** (`/translate?words=…` boş döndürüyor; komşu beş söz — "Start New Trait Training", "Remove Trait",
"Training Rating", "Traits", "Cancel Current Trait Training" — çevriliyor). Yeni: `SpeakToAction(actionID)` yardımcısı
(Acknowledge/Mentoring'in kullandığı yol) `buttons?json=1&gen=` ile `SpeakToAction` düğmelerini alıp `Action`
düğümünün `ActionID`'sini okuyor; düğme 0,25 sn aralıkla en çok 8 sn aranıyor, bulunamazsa stderr'e yazılıp rc=1.
Kimlik: **1263235426** = `FM.GamePlugin.InteropEventIDs.PlayerAskStaffToSuggestPlayerTraitTraining`
(`docs/reference/enums.txt`); aynı oyunda açılan onay penceresinin `Type`'ı da `Confirm1263235426`. Cümleli cevaptaki
`Answer` çapası da İngilizce sabit yerine düğmenin kendi sözü (`Field(button,"Label")`).
- Ölçüm (kimlik, ekran): TR oyunda `Shaw'dan bireysel antrenmanlarla ilgilenmesini iste` — hem `StandardButton`
  yazısı hem `action.ActionTitle` metni **birebir aynı**; yani tablonun çeviremediği bir sözde ekranın gösterdiği
  şey düğmenin kendi yazısı, dolayısıyla `Answer` için doğru çapa.
- Öncesi (eski derleme, HEAD'den geri yüklenip derlendi): TR üç durum (sayfa ilk kez `fm open "Messages"` sonrası /
  hemen ikinci koşu / `fm open "Squad Dynamics"` sonrası) → üçünde de `error: no control labelled 'Ask For Trait
  Training Suggestion' on the loaded screens`, rc=1. EN üç durum → üçünde de öneri penceresi, rc=0.
- Sonrası: TR üç durum → `Oyuncu Karakteristiği Hakkında Öneri / … öneriyorum. / Onaylamak için aynı komutu --yes ile
  çalıştır.`, rc=0; EN üç durum → `Player Trait Suggestion / … / To confirm, run the same command with --yes.`, rc=0.
  (Öneri metni soruşlar arasında değişebiliyor — TR üç koşuda aynı, EN 1. koşuda farklı — o yüzden biçim karşılaştırıldı.)
- Negatif: `fm training "ZZZ yok" --trait-suggest` → rc=1, "'ZZZ yok' adında bir oyuncu kadroda da aramada da yok."
  Sekiz oyuncuda `--trait-suggest` denendi, hepsinde öneri penceresi çıktı ve her koşu "Don't start training" ile
  kapandı (hiçbir alışkanlık öğretilmedi; `fm training "Jenson Jones" --trait` → "alışkanlıkları: yok", çalışan yok).
- Dönüş: `fm settings language "Türkçe"` → `/state` → `Language: turkish`; 2 Ağustos 2026 14:00, saat
  ilerletilmedi, maç oynanmadı.
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`/tmp/sweep_t20`). Paylaşılan koda (Bridge/OpenPage/reply)
  dokunulmadı; yeni yardımcı `TraitTraining.cs` içinde (yalnız oradan çağrılıyor), `Arriving` başka yerlerde kaldı.
- Bilerek bırakılan: cümleli cevap yolu (öneri yok) bu kayıtta üretilemedi — TR'de eskiden o satıra hiç gelinmiyordu
  (basış düşüyordu), yani gerileme yok; çapa ölçümü yukarıda. Bir dahaki "öneri yok" vakasında doğrulanacak.

### Tur 19 — A "`Task.Delay`" / Matches 82: sabit 500 ms kalktı ve ölü dal ilk kez çalıştı — en eski maç artık düşmüyor (§8.208, 18 Ekim)
Ne: `cli/fm/Matches.cs` (`fm player <oyuncu> --matches`). Liste yalnız görünen pencereyi döşüyor (19 satır) ve giriş
koşulu `rows.Count < apps` (oyun sayısı) olduğu için kaydırma dalı **hiç çalışmıyordu**: her oyuncunun en eski maçı
(#1) sessizce eksik kalıyordu. Yeni: giriş oyunun kendi cevabı (`scroll?anchor=<ilk satır>&to=0` → "0 of 211,2";
" of 0" ise pencere bütün listeyi tutuyor, `Numbers.cs` ile aynı okuma), pencere kaydırmadan sonra "değişti **ve** iki
okumada aynı" olunca kabul ediliyor (kaydırılan satırlar ~30 ms'de geliyor → sabit 500 ms makine hızı tahminiydi);
iki uç da geziliyor, liste başına alınıyor, sonunda başa dönülüyor.
- Ölçüm (okuma yolu, hiçbir şey öğretmiyor): `python3` + `/texts?anchor=All Competitions&up=10` 30 ms aralıkla:
  `scroll … to=0.5` → pencere 30 ms içinde 20 satıra çıkıyor (ara yerleşim görülmedi).
- Öncesi (eski derleme `git stash` + derle): `fm player "Ben House" --matches --json` → **19** satır (`2..20`); TR üç
  durum `md5 d2186ce9…`, 8,91 / 2,01 / 8,23 sn; EN üç durum `md5 5934f197…`, 8,99 / 1,91 / 2,11 sn. Dört oyuncuda da
  19 satır (Jeacock, Bradley, Hamer, Draper). Sayfa başka bakıştan kaydırılmış kalırsa düşen satır değişiyordu
  (python'da `19..1`) → okuma **nereden bakıldığına** bağlıydı.
- Sonrası: **20** satır (`1..20`), dört oyuncuda da tam. TR üç durum + iki tekrar (5 koşu) `md5 d4b4b0ea…`
  (8,82 / 3,49 / 9,88 / 3,72 / 3,75 sn); EN üç durum + tekrar (4 koşu) `md5 d9ddd02b…` (10,26 / 3,63 / 3,47 / 3,47 sn);
  dönüş `fm settings language "Türkçe"` → final TR koşusu aynı `md5` (3,39 sn). İnsan çıktısının son satırı artık
  `1  07/02/2026  Plymouth  4 - 1  oynamadı …`.
- Üç durum (her biri TR + EN): `fm open "Messages"` sonrası (sayfa ilk kez açılıyor) · hemen ikinci koşu (zaten açık) ·
  `fm open "Squad Dynamics"` sonrası (başka sayfa açık) → hepsinde 20 satır, aynı `md5`.
- Negatif: maçı olmayan oyuncuda davranış aynı — `fm player "Cody Tomlinson" --matches` → "form kutusu yok", rc=1;
  `fm player "Gael Mande" --matches` → `Matches: []`, `Apps: 9` (**eski derlemede de birebir aynı**). Gael'de kart
  açıldıktan sonra `/texts` bir an önceki oyuncunun (Ben House, Apps 12) tablosunu veriyor: FM'in bağlı kalan
  panelleri — §8.208 sonuna yan bulgu olarak yazıldı.
- Regresyon: paylaşılan koda (Bridge/OpenPage/reply/ortak yardımcılar) dokunulmadı, değişiklik yalnız `Matches.cs`
  içinde → `sweep.sh` gerekmedi (sweep zaten `player --matches` koşmuyor). Kayıt test kaydı: 2 Ağustos 2026 14:00;
  saat ilerletilmedi, maç oynanmadı (oyun bir ara yanıtsız kaldı, `fm start </dev/null` ile aynı kayıt açıldı).
- Not (maliyet): dal hiç çalışmadığı için bu okuma +~1,6 sn (bir `scroll` + iki pencere turu + başa dönüş); karşılığı
  sessizce düşen en eski maçın geri gelmesi. Kazanç/kayıp kabul edildi; daha ucuz yol aranırsa: pencerenin bütün
  listeyi tutup tutmadığını söyleyen cevabın birimini (satır sayısı mı, yükseklik mi) çözmek gerekir.

### Tur 18 — A "`Task.Delay`" / OppositionCard 91: sabit 500 ms yerine sütunun kendi değeri, her talimat taze satırdan (§8.207, 18 Ekim)
Ne: `cli/fm/OppositionCard.cs` (`opposition <oyuncu> <talimat>=<değer>`). Seçimden sonraki `Task.Delay(500)` + okuma,
"o sütun seçilen değeri gösteriyor" işaretine çevrildi (0,25 sn aralık, en çok 8 sn; gelmezse stderr'e uyarı). Aynı
fonksiyondaki **sıra bağımlılığı** da kaldı: satır her seçimden önce **adıyla** yeniden bulunuyor (`RowNow`, ara çizimde
satır kaybolursa bekliyor) ve işaretçi taze okumadan alınıyor.
- Ölçüm (okuma yolu, hiçbir şey öğretmiyor): `python3` + `/opposition`; dört sütunun bütün seçenekleri tek tek seçilip
  değerin görünmesi ölçüldü → **301 · 321 · 329 · 363 · 365 · 376 · 379 · 400 · 429 · 494 ms** (10 ölçüm) — 500 ms'in
  payı neredeyse yoktu. `FM_TRACE=1 fm opposition "Kyle Knoyle" "Pressing=Always"`: `select` 0,38 s → okuma 0,45 (eski
  değer) → oturan okuma 0,77 s.
- Sıra bağımlılığı (eski derleme iki kez yanlış sonuç verdi): TR "başka sayfa açık" durumunda `Marking=Always
  Pressing=Never` → yazılan tablo `Her Zaman` dedi ve kartın kendisi (1,5 sn sonra bağımsız okuma) `Always` gösterdi
  (`md5 ee3c73ba…`); EN'de `Pressing=Always Marking=-` → `Marking` `Always` kaldı (`md5 9ea2312e…`). İkisinde de rc=0.
- Komut (üç durum, her biri TR + EN): `fm open "Messages"` → `fm opposition "Kyle Knoyle" "Marking=Always"
  "Pressing=Never"` (sayfa ilk kez açılıyor) · hemen ikinci koşu (sayfa açık) · `fm open "Squad Dynamics"` sonrası
  (başka sayfa açık).
- Öncesi (eski derleme, `git stash` + derle): TR `md5 97059da1 / ed1cd387 / ee3c73ba / ed1cd387`, süre 2,60 / 1,10 /
  1,77 / 1,15 sn; EN `md5 9e41298d / 8704a667 / 9e41298d / 9ea2312e`, 2,14 / 1,02 / 1,09 / 1,09 sn.
- Sonrası (yeni derleme): TR `97059da1 / ed1cd387 / 97059da1 / ed1cd387`, 2,25 / 1,61 / 2,09 / 1,59 sn; EN
  `9e41298d / 8704a667 / 9e41298d / 8704a667`, 2,35 / 1,55 / 1,53 / 1,50 sn — yani **1, 2 ve 4. koşular birebir**;
  yalnız "başka sayfa açık" koşusu değişti (eskiden ikinci talimat uygulanmadan yazılıyordu, şimdi 1. koşunun aynısı).
  Her koşudan 1,5 sn sonra kartın kendisi okunup yazılan tabloyla karşılaştırıldı → 8/8 birebir (TR'de sözler oyunun
  diliyle: `Always→Her Zaman`, `Never→Hiçbir Zaman`; eşleme tablosu tutarlı).
- Tekrar (ilk talimat gerçekten değişecek biçimde, 6'şar koşu): eski 6/6, yeni 6/6 → bu dizide fark yok; sıra
  bağımlılığının kanıtı yukarıdaki iki yakalanmış koşu (nadir; ama sessizce yanlış talimatla bırakıyordu).
- Negatif (TR): `"ZZZ yok" "Pressing=Always"` → rc=1 "Rakipte 'ZZZ yok' yok." · `"Pressing=zzz"` → rc=1 "Pres: 'zzz'
  yok. Seçenekler: …" · `"Pressing"` / `"Yok=Always"` → rc=64 biçim satırı · kısmi ad (`"Knoyle"`) → rc=0, satır tam
  adla bulunup uygulandı (karttan doğrulandı). Satır hiç görünmezse ("RowNow" zaman aşımı, geçici bozuk kodla denendi):
  "Rakipte 'Kyle Knoyle' yok.", rc=1 — çökme yok.
- `--advice` / `--clear` yolu (satır 55) **değişmedi**: "Apply all advice" bastıktan sonra kart 192 ms'de bir ara
  yerleşime geçiyor ve **281 ms'deki okuma onunla aynı** (gerçek sonuç 384 ms) → "iki okuma aynı" işareti erken kabul
  ederdi; düğmenin etkin/olay alanları uygulama boyunca değişmiyor (`/buttons?json=1` 12 okuma). İşaret bulunamadı,
  madde `[?]`.
- Dönüş/geri alma: kart `fm opposition --clear` ile tur başındaki boş hâline döndürüldü (11 satır, hepsi "-"),
  `/state` → `Language: turkish`, 2 Ağustos 2026 14:00 (saat ilerletilmedi, maç oynanmadı).
- Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (`/tmp/sweep_t18`). Paylaşılan koda (Bridge/OpenPage/reply)
  dokunulmadı, değişiklik yalnız `OppositionCard.cs` içinde.
- Yan bulgu (B, yeni madde): seçenek adları İngilizce geliyor, hata mesajı ve yardım satırı onları oyunun sözleriyle
  yazıyor → yazılan ad geri yazılamıyor; TR'de çeviri tablosu sıkı giriş seçeneklerini `Normal · Minor · Gusty` veriyor.

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
- A "Kısa `Until`" / **Stages 337** (`fm stages` kupa turu; 19 Ekim, Tur 42): menzil `"0 of 1401,8"` oyunun ondalık
  işaretiyle geliyor, `InvariantCulture` çözemiyor → TR'de gezinti hiç çalışmıyor (yalnız ilk pencere: 18–25 bağ;
  gerçek 35). Menzil `GameNumbers.Read` ile çözülünce gezinti çalışıyor ama **kararsız** (TR 35/19/19, EN 35/35/19):
  çapa, okunan pencerenin ilk bağının yazısı ve kaydırma ilerleyince kayboluyor; ayrıca "iki okuma aynı" işareti
  kaydırmadan önceki pencereyi de kabul edebiliyor. Kod `git restore` edildi, **tekrar denemeyin**; doğrusu üçü
  birlikte: sabit çapa (satır işaretçisi ya da listenin kabı) + "pencereden farklı **ve** iki okumada aynı" işareti +
  menzilin oyunun okuyucusuyla çözülmesi. Ölçüm §8.231. Faruk'a soru: çapa için eklentiye tur satırlarının
  işaretçisini veren bir alan mı eklenmeli, yoksa gezinti listenin kabına mı çapalanmalı?
- A "`Task.Delay`" / `OppositionCard 55` (`--advice` / `--clear` sonrası 500 ms; 18 Ekim, Tur 18): işaret bulunamadı —
  "Apply all advice" bastıktan sonra kart 192 ms'de ara yerleşime geçiyor ve 281 ms'deki okuma **onunla aynı** (gerçek
  sonuç 384 ms), yani "iki okuma aynı" işareti erken kabul ederdi (Staff 74'ün tuzağı, §8.205); düğmenin etkin/olay
  alanları uygulama boyunca değişmiyor. 500 ms kaldı (pay ~1,2 kat → yavaş makinede ölçülmeli). Ölçüm §8.207.
  Faruk'a soru yok; aynı yol yine denenmek istenirse: oyunun kendi verisindeki talimat listesi (kart yerine veri)
  bulunursa oradan doğrulanabilir.
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

## Sonraya bırakılanlar (ağır liste/kaydırma — bu koşuda seçilmez)
- (henüz yok)

## Sabitler
- Kapsam: **A** (zamanlama) + **B** (dil/kimlik) + **C** (ayarlar), mümkünse **D**.
- Girmeyeceğiz: §14 UX/görsel, **G** (platform), **H** (yayın).
- Model: opencode-go/deepseek-v4.1-flash · sürücü: tools/autopilot.sh
- Commit: main'e, push yok. Conventional Commits, son satır `Co-Authored-By: OpenCode`.
