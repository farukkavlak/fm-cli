# Bağımlılık dökümü: oyun dili, ekran / tema, kariyer, gömülü sabitler

Amaç: fm-cli başka dilde, temada, kulüpte / ligde, makinede, sürümde, platformda açılınca nerede kırılır. İş takibi
aşağıdaki CHECKLIST'te; altta 13 Ekim kod taramasının kısa arka planı (çözülenler işaretli, ayrıntı FINDINGS'te).
14 Ekim'de sadeleştirildi; uzun hali `git show 515eef0:docs/BAGIMLILIKLAR.md`.

İşaretler: **[kesin]** kodda açıkça görülüyor · **[olası]** koddan çıkarım, oyunda ölçülmedi · **[ölçülecek]** ancak
oyunu değiştirip deneyince bilinir.

---

## CHECKLIST — temizlik sırası (14 Ekim 2026 akşam, kod taraması + Türkçe oyunda denenenler)

Faruk: "dile / sisteme / bu lige / bu takıma / bu kayda / bu donanıma göre tasarlanmış şeylerin hepsini checklist
yapalım, adım adım temizleyip bitirelim." Aşağıdaki bölümler (0–8) 13 Ekim'in arka planı; iş takibi **burada**.

`[x]` bitti ve denendi · `[ ]` yapılacak · `[~]` bilerek bırakıldı (neden yanında). Her maddede: nerede, neden kırılır,
nasıl düzelir. Kural: yeni işaret en az üç durumda (ilk açılış / zaten açık / başka sayfa açık) ve iki dilde ölçülmeden
koda girmez; kanıtlanmazsa eskisi kalır.

### A. Donanım / makine hızı (zamanlama)

Sabit "N ms bekle sonra yap" yavaş makinede yanlış okur, hızlıda boşa bekler. Doğrusu oyunun kendi işaretini beklemek;
süre yalnızca cömert bir üst sınır. Döngü içindeki "tekrar bak" aralığı sorun değil.

- [ ] **`Task.Delay` — 89 yer.** Her biri sınıflanacak: döngüde yoklama aralığı (sorun yok) mı, "bekle sonra yap" mı.
  Dosya: satırlar —
  Bridge 103 109 127 147 · Clock 148 157 179 · Coaches 53 88 · Contracts 57 · ~~Finances 247~~ · Guided 161 174 ~~1035~~ ·
  ~~Happiness 148 195~~ · ~~IndividualTraining 40 50~~ · ~~Intensity 43 54 106~~ · ~~Jobs 231~~ · Launcher 213 269 · League 243 ·
  MatchDay 181 314 368 · MatchReport 35 · ~~Matches 82~~ · ~~Mentoring 45 188 205 217 246 285 297 331~~ · ~~Numbers 158 177~~ ·
  OfferScreen 259 · Offers 99 · OppositionCard 55 91 · Program 589 693 2112 2153 2232 · Propose 66 80 · Reads 114 131 ·
  Registration 32 115 301 305 · Release ~~57~~ 123 · ~~Responsibilities 283 388~~ · ScoutFocus 107 237 349 537 576 724 ·
  ~~Scouting 100~~ · Settings 226 · ~~Staff 74~~ · StaffSearch 91 100 110 407 · Stages 62 97 116 132 209 · StatusFlags 54 ·
  TacticFiles 181 · ~~Takers 206~~ · TrainingReport 82 · ~~TrainingSchedule 345~~ · ~~TraitTraining 32~~ · Transfers 96 128 131 139
  - [x] Bridge 103 109 127 147: dördü döngüde yoklama aralığı (250 ms), değişiklik gerekmedi; değer 2. okumada, ~5 kat
    pay. "Yol boş" ile "henüz gelmedi" aynı cevap (`{}`) — FINDINGS §8.179.
  - [x] `table`: olmayan `CurrentComp5..8` artık beklenmiyor (`ClubCompetitions`, tek okuma); 8,7 → 2,5–2,9 sn, üç
    durumda ve dört turnuva adıyla aynı sonuç (§8.179).
  - [x] Clock 148 157 (`next`'in `Settled`): 16 ilerlemede ölçüldü, değişiklik gerekmedi. Basıştan sonra ilk değişiklik
    0,34–1,24 sn (sınır 4 sn, ≥3 kat pay); iş bitince durum 0,1–0,53 sn titriyor (CanContinue→NewsAggregator/NeedAction/
    Fixtures→…), "1,4 sn sakin" bunu örtüyor (2,6 kat). Ayrı bir "bitti" işareti yok: `GameManagersNotContinuedCount`
    `Processing`'i birebir izliyor; bir adımda `Processing` hiç görünmedi, yalnız saat değişti (§8.181).
  - [x] Staff 74 (`fm staff`): kaydırmadan sonra sabit 400 ms yerine oyunun kendi "diziliş bitti" işareti — tablo
    kaydırılınca satırlarını iki adımda diziyor (ölçüm: `to=1` 6 → 10 → 27, `to=0.75` 13 → 17 → 34 satır; yarım hâller
    0,1–0,2 sn duruyor, `/scroll` konumu ilk okumada doğru → geciken **diziliş**), yani 400 ms'in payı yalnız ~2 kat ve
    yavaş makinede yarım tablo okunup personel eksiliyordu. Artık 0,25 sn arayla üç okuma aynı olana kadar okunuyor.
    TR + EN üç durumda çıktı eski derlemeyle **birebir** (48 personel); bedel ~1,5–2 sn (§8.230).
  - [x] ScoutFocus (`Show`, gözlemci seçme penceresinin okuması): tablo kaydırıldıktan sonra sabit **400 ms**
    bekleniyordu; oysa satırlar kaydırmadan hemen sonra yerleşmiş oluyor (ölçüm: üç kaydırma konumunda da satır kümesi
    ilk okumada — 78/73/87 ms — aynı), yani bekleme kör bir duraklamaydı. Artık satırlar **art arda iki okumada aynı**
    olana kadar okunuyor (oyunun kendi "yerleşti" işareti, üst sınır 2 sn). Aynı okumadaki **başlık sözü** de kalktı:
    `Rows()` satırları `SkipWhile(t != "World Knowledge")` ile buluyordu — satırın kendisi zaten 3. hücresindeki
    "Active Focuses" **sayısıyla** işaretli (başlıklarda o hücre sayı değil); TR 3 + EN 3 durumda okuma eski
    derlemeyle birebir (`md5` TR `fc708400…`, EN `b11015ec…`), `fm scout --json` koşu öncesi/sonrası aynı (§8.247).
  - [ ] Clock 179 (`Unemployed`, 2×400 ms): işsiz bir kayıt lazım.
  - [x] Contracts 57: sabit 300 ms yerine `{}` (bağlanmamış) dönen alanlar gelene kadar okunuyor; boş şart "-" geliyor,
    `{}` kalmıyor. 27 oyuncu iki kez, aynı ve tam (§8.182). İngilizcede denenmedi.
  - [x] Finances 247 (+ kısa `Until` Finances 238): `finances` ve dökümü artık hiç sayfa açmıyor, hepsi kulübün
    verisinden (`/pull`); bekleme, kaydırma, yazı çapası kalmadı. Türkçe döküm daha önce hiç çalışmıyordu ("Madde" →
    "Clause"); 8 bileşim doğru, kalem toplamları toplamı tutuyor, art arda 10/10, kart açıkken / başka sayfada doğru
    (§8.183). İngilizcede denenmedi (veri yolu dilden bağımsız; kalem adları `GameWords` ile gösteriliyor).
  - [x] Guided 1035 (`tactic pick`): her okumadan önce 1 sn yerine 0,25 sn'de bir; değiştiyse bir aynı okuma, değişmediyse
    1,5 sn sessizlik (seçim tek adımda 0,33–0,54 sn, başka işaret yok). 3,7 → 1,2 / 2,2 sn; başka sayfa açıkken düğme
    bulunamıyordu, artık sayfa açılıyor (§8.184). İngilizcede denenmedi.
  - [x] Happiness 148 195 (kaydırmadan sonra 500/400 ms): kaydırınca satırlar iki adımda diziliyor (15 → 21); artık okuma
    "öncekinden farklı ve art arda iki kez aynı" olunca kabul, liste takımın bütün oyuncuları (kulüp verisi) okunana kadar
    0 / 0,5 / 1 (gerekirse ara onda birler) geziliyor. Üç durumda aynı çıktı, tek oyuncu ve `--why` doğru (§8.185).
    İngilizcede denenmedi.
  - [x] IndividualTraining 40 50 (kaydırmadan sonra 400 ms, sabit 0–1 taraması): liste artık hiç sayfa açmıyor, oyuncuların
    verisinden (`PlayerTraining`, `Judge = Human`, sayfadakiler `IsVisible`); değiştirme oyunun kendi listesiyle ama liste
    "List" yazısı / öğe adı / ad yazısıyla değil, gönderdiği olay + satırın oyuncusu (`UniqueId`, eklenti `/rowcontrols`) ile
    bulunuyor, görünüm `selectedView` = 1, sonuç veriden doğrulanıyor. 7,1 → 2,3 sn; 10/10 + çoklu + alt menüler (§8.186).
    Rehabdaki 2 oyuncu listede yok, oyunun tablosunda da yoklar (aynı 25). İngilizcede ve küçük ekranda denenmedi.
  - [x] Intensity 43 54 106 (kaydırmadan sonra 400 ms, `--auto`'dan sonra 1,5 sn): liste oyuncuların verisinden
    (`TrainingIntensityOrRest`, `team` değişkeni), değiştirme Bireysel Antrenman tablosundaki yoğunluk listesiyle (olay +
    satırın `UniqueId`'si, seçenek numarasıyla `/select?value=`), sonuç veriden; `--auto` düğmesi olay + sayfa Guid'i
    (eklentide yeni `Page` alanı), oyunun onay penceresi yuvayla (`--yes` yoksa "Hayır"). Stil sınıfı
    (`dropdown-base-regular-trailing-icon`), "Ask … training intensities" ve "3 Days" yazıları kalktı (son ikisi Türkçede hiç
    çalışmıyordu). 5,9 → 2,3 sn liste, 3,8 sn değiştirme; 22/22 + `--auto` 7/7 (§8.187). İngilizcede denenmedi.
  - [x] Mentoring 45 188 205 217 246 285 297 331 (onaylardan, kaydırmalardan, seçimden sonra 400–800 ms): gruplar takımın
    verisinden (`Human.Team.MentoringGroups` + `MentoringGroupPlayerOne/Two/Three`), sayfa açılmıyor, `/tree` taraması yok.
    `--auto`: yardımcı düğmeleri eylem kimliğiyle (eklenti `/buttons?gen=` → `Action`: sor = `ActionID`
    `TeamSetRecommendedMentoringGroups`, kapat = `Event` 31 `EventFmxClearChanges`), gruplar varken oyunun onay penceresi
    (`Confirm1882280514`, `--yes` yoksa "Hayır"). `new`: adaylar düzenleyici tablosunun öğelerinden (eklenti `/checkrows`,
    görünüm sırası), işaret `/rowselect?table=&index=` (kaydırma yok), her işaretten önce sıra yeniden okunuyor ve seçim
    grubun `SelectedPlayers`'ından doğrulanıyor; ad kutusu `Binds == newgroupname`, onay `IsValid` ile. `remove`: grup listesi
    `Binds == selectedmentoringgroup`, silme düğmesine yalnızca gönderdiği grup (eklenti `Sends`) hedefin üyeleriyse basılıyor.
    Bir grubun kimliği üyeleri (UniqueId ve oyunun verdiği ad silmeden sonra yeniden numaralanıyor). "Mentoring Groups",
    "Auto Select"/"Hierarchy"/"Confirm", "Mentoring Group Name", "assign mentoring groups", "Acknowledge and Move On" ve
    `EditBox` adı kalktı. Liste 8,4 → 0,7 sn; Türkçede `--auto` ilk kez çalıştı. Oyunun tuzağı: aynı gün silip yeni grup
    açınca eski liste geri geliyor ve yeni grup var olan bir grubun kimliğini alıyor → `new` aynı oyun gününde bir
    `remove`'dan sonra reddediyor (§8.188). Gün ilerletilerek doğrulandı: tek/çift silme, iki kurma, kurup silme. İngilizcede
    denenmedi.
  - [x] "Acknowledge and Move On" yazısıyla basan yerler (BoardRequests 99 101, Captains 61, Propose 44, Release 47 80 156,
    TraitTraining 45 110, TrainingReport 40 86): düğme artık listedeki yazıyla değil gönderdiği olayla bulunuyor —
    eklenti `/buttons?json=1&gen=` her `SpeakToAction` düğmesine `Action` veriyor, `Event` 31 (`EventFmxClearChanges`,
    `ActionID` 0) olan yalnızca söyleneni kapatan düğmedir. Yardımcı `Acknowledge()` (Program) düğmeyi işaretçisiyle
    basıyor, yoksa hiç basmıyor. Ölçüm: TR + EN'de Event 31 doğrulandı; eski kod 3 boşa basış, yeni kod tek basış;
    üç durumda rc=0, negatifte 0 basış; `sweep.sh` 50/50 (§8.191). BoardRequests'teki karşılaştırma da kimliğe bağlandı.
  - [x] Takers 206: duran top türü seçildikten sonraki sabit 500 ms kaldırıldı; doğru atıcı düğmesini bekleyen işaret
    zaten vardı (`Still`: menü bu türü gösteriyor **ve** düğme iki okumada aynı duruyor). `fm takers --json` beş türle
    üç durumda (başka sayfa açık / sayfa zaten açık / başka sayfadan) TR + EN'de aynı çıktı (`md5`), süre nötr
    (3+3 koşuda ~19,2 ↔ ~19,3 sn): kaldırılan yalnızca makine hızına dair bir varsayım (§8.202).
  - [x] TraitTraining 32: alışkanlık eğitimi sayfasını açan iki döşemenin ("Training Rating" → "Traits") ardındaki
    sabit 500 ms kaldırıldı; döşeme basılmadan önce soruluyor (`press ?…&dry=1`, 0,25 sn aralıkla, en çok 8 sn) ve
    ancak ekranda olunca basılıyor. `fm training "Ben House" --trait` üç durumda TR (`md5 8a43caed…`) + EN
    (`md5 9e92fe93…`) öncesiyle birebir aynı 10 satır; döşeme→döşeme aralığı 0,57–0,60 → 0,43–0,53 sn (§8.206).
  - [x] Jobs (başvurudan sonra sabit 1 sn; satır 408): basıştan sonraki bekleme oyunun kendi cevabına bağlandı —
    "`dialog` doldu **veya** düğme artık sunulmuyor" (0,25 sn aralık, en çok 6 sn). Ölçüm: düğmenin `Enabled`'i
    basıştan **0,467 sn** (apply) / **0,629 sn** (interest) sonra düşüyor ve `dialog` 13,5 sn boş kalıyor → eski 1 sn
    tam o anda boş `dialog` okuyordu (eski derleme izi: `press` 3,32 → tek `dialog` 4,43; yeni: `press` 4,35 →
    `Enabled=False` 4,79–4,91). Üç durum × TR + EN: altı basış da rc=0, çıktı eskisiyle aynı ("Gönderildi…" /
    "Sent…"), basış→kabul 0,20–0,75 sn; `fm jobs "York"` artık "Başvuru kapalı…" (§8.219). Dosyada `Task.Delay`
    kalmadı.
  - [x] `jobs apply|interest` düğmeleri: panelin iki cevabı artık etiketle değil **olayla** bulunuyor (`Events` içinde
    `EventFmxApplyForJob` / `EventFmxDeclareInterestInJob`; TR + EN'de aynı, `docs/reference/enums.txt`), basış
    işaretçiyle. Negatif: TR `Villarreal apply` → "başvuru kabul etmiyor", rc=1 (§8.219, B "yazıyla basma" maddesi).
  - [ ] Guided 161 174 (bedel sözü penceresi: onaydan sonra 1 sn, açılmasını 800 ms bekleme): bedel isteyen bir söz
    konuşması lazım; o an oyunda yok, ölçülmeden değiştirilmedi. Not: etkileşimli `talk` her cevaptan sonra 800 ms bekliyor.
  - [x] Coaches 53 88: atamada "kutu istenen halde ve yıldız yalnız atandığı yerde" (≤5 sn), `--auto`'da sayfa değişimi
    (değişmezse 3 sn; eskiden sabit 1,5); 20 atama + 2 `--auto` üç durumda doğru (§8.180). İngilizcede denenmedi.
  - [x] `coaches` 3/10: okuma yalnız tam ızgarayı ("ad, görev, dokuz kutu+yıldız", iş yüküne kadar) kabul ediyor; 12
    açılışta 17 ara hal reddedildi, 12/12 tam (§8.180). Kural yapıdan, dilden bağımsız.
  - [?] Staff 74 (kaydırmadan sonra sabit 400 ms): sabit duraklamayı oyunun işaretiyle değiştirmek **denendi ve geri
    alındı** — satırlar kaydırmadan 93–272 ms sonra oturuyor, ama oyun tam olmayan bir pencereyi de "sakin" gösterebiliyor
    (20 satır iki okumada aynı) ve kaydırma çapası bir an bulunamıyor; işarete bağlı sürüm 18 koşunun 2'sinde pencere
    kaybetti (34/42 satır), 400 ms'lik duraklama 12/12 tam (48). Sabit duraklama şimdilik kalıyor (§8.205). Faruk'a
    soru: 400 ms bu listeye yetiyor mu, yoksa kaydırma "görünen son satırın kendi yazısına" çapalanıp (Mutluluk'taki
    gibi) liste baştan sona gezilsin mi — ikincisi ayrı ve daha büyük bir değişiklik.
  - [x] OppositionCard 91 (`opposition <oyuncu> <talimat>=<değer>`): seçimden sonraki sabit 500 ms kaldırıldı. Her talimat
    artık **satırın kendi adıyla yeniden bulunduğu** güncel karttan seçiliyor (kart bir talimat oturunca yeniden çiziliyor
    ve denetimlere yeni işaretçi veriyor; eskiden ikinci talimat bayat işaretçiye gidiyor, oyun "seçildi" deyip hiçbir şey
    yapmıyordu — iki kez yakalandı: TR 3. durumda istenen `Never` yerine `Always`, EN'de istenen `-` yerine `Always`, rc=0)
    ve okuma "sütun seçileni gösterene kadar" (0,25 sn aralık, en çok 8 sn; gelmezse stderr'e uyarı). Değerin oturma
    zamanı seçimden sonra 301–494 ms (10 ölçüm, dört sütunun bütün seçenekleri) → 500 ms'in payı neredeyse yoktu.
    Çıktı TR + EN'de üç durumda (sayfa ilk kez açılıyor / zaten açık / başka sayfa açıkken) eskisiyle birebir (`md5`
    97059da1…, ed1cd387…); yalnız "başka sayfa açık" durumunda yazılan tablo artık doğru (§8.207). `md5`ler dört koşuda
    kartın kendisiyle de karşılaştırıldı (bağımsız okuma) ve birebir.
  - [x] Matches 82 (`fm player <oyuncu> --matches`'te kaydırmadan sonraki sabit 500 ms): liste artık görünen pencereye
    göre değil, sayfanın kendi kaydırmasıyla **baştan sona** okunuyor. Girişin işareti oyunun kendisi: `scroll`
    cevabı "0 of 211,2" (" of 0" ise pencere bütün listeyi tutuyor — sayı tablosundaki okuma); kaydırmadan sonra
    pencere "değişti **ve** iki okumada aynı" olunca kabul (kaydırılan satırlar ~30 ms'de geliyor, sabit 500 ms
    yalnız makine hızını tahmin ediyordu). Eski koşul (`rows.Count < apps`) yüzünden dal **hiç çalışmıyordu**: her
    oyuncunun en eski maçı (#1) sessizce düşüyordu ve sayfa başka bir bakıştan kaydırılmış kalmışsa düşen satır
    değişiyordu (okuma nereden bakıldığına bağlıydı). Ölçüm: **19 → 20 satır**, dört oyuncuda da tam; üç durumda
    TR `md5 d4b4b0ea…` (5 koşu) / EN `md5 d9ddd02b…` (4 koşu); süre soğuk 8,8–9,9 / sıcak 3,5–3,8 sn (§8.208).
  - [x] Scouting 100 (odak tablosunu kaydırdıktan sonraki sabit 700 ms): kaydırmadan sonra pencere "öncekinden
    **farklı** ve iki okumada **aynı**" olunca kabul ediliyor (fark şart: kaydırma sonrası ilk okuma bir an eski
    pencereyi veriyor). Ölçüm: satır adları 207–485 ms'de değişiyor, tam içerik 690–1080 ms'de oturuyor → eski
    700 ms tam sınırdaydı. `fm scout "England" --json` TR üç durum `md5 6915efcb…`, EN üç durum `md5 86ccb87b…`,
    ikisi de eski derlemeyle birebir (`diff` boş), 20/20 satır tam; `--near`/`--progress` (kaydırma yok) 6/10
    satır; negatif rc=64 (§8.213).
  - [x] Release 57 (`fm contract <oyuncu> --agent`'te menajeri sorma satırından sonraki sabit 1 sn): satır artık
    oyunun **eylem kimliğiyle** (bkz. B bölümü, §8.210) bulunup basılıyor; bekleme yerine oyunun kendi işareti
    bekleniyor — cevap satırları menajerin sözleşme konuşması olmaktan çıkıp menajerin kendi sorularına dönüyor
    (arada panel bir an **boş** kalıyor: 744 ms'de boşalıp 1145 ms'de dolduğu ölçüldü → sabit 1 sn tam o boşluğa
    düşüyordu; bir EN koşusunda menajerin ilk cümlesi boş geldi). TR'de üç durumda öncesi `menajerle görüşme
    seçeneği yok`, rc=1; sonrası üç durumda menajerin konuşması + 4 cevap, rc=0 (§8.210).
  - [x] Numbers 158 177 (alt sayfa düğmesinden sonra sabit 1,5 sn + her kaydırma adımından sonra sabit 400 ms): ikisi de
    oyunun kendi işaretine bağlandı. Düğme (`SecondaryTabDropdownButton`) seçilmeden önce menünün o seçeneği sunduğu
    soruluyor (`select … choice="Squad Numbers" dry=1`, 0,3 sn aralık, en çok 10 sn); kaydırmada `scroll` cevabı istenen
    yer zaten oradaysa tek okuma, değilse pencere "değişti ve iki okumada aynı" olunca kabul. Ayrıca okuma anında sayfa
    boş gelirse komut sayfayı yeniden isteyip okuyor — eskiden `fm open Messages` sonra `fm numbers`, EN'de 6 koşunun
    2'sinde `Forma numaraları okunamadı.` rc=1 idi (tablo bir an duruyor, okuma gelince gitmiş); yeni kod 6/6 rc=0,
    37 oyuncu. Üç durumda TR `md5 a7fde227…` + EN `md5 e7bf7ceb…` eskisiyle birebir; numarayı elle verme yolu
    (`fm numbers "Adam Reach" 44` TR, `"Oscar Thorn" 45` EN) rc=0; `sweep.sh` 50/50 (§8.211).
  - [?] OppositionCard 55 (`--advice` / `--clear` sonrası 500 ms): **kaldı**. Kartın ara yerleşimi iki okumada aynı
    görünebiliyor (192–281 ms arası aynı satırlar, gerçek sonuç 384 ms'de) — "iki okuma aynı" işareti erken kabul ederdi
    (Staff 74'ün tuzağı); "Apply all advice" düğmesinin durumu (etkin/olay) uygulama boyunca değişmiyor. Ölçüldü, işaret
    bulunamadan değiştirilmedi. Faruk'a soru gerekmiyor: madde ölçüldü ve park edildi.
  - [x] Responsibilities 283 388 (listeyi başa alma + sabit 500 ms): uzun görev listesi `scroll …&to=0` ile başa
    alınıyor ve ardından okumadan önce sabit 500 ms bekleniyordu — bu süre yalnız satırların ne zaman çizildiğine dair
    bir tahmindi (ölçüm: kaydırmadan ~0,35 sn sonra iki ardışık okuma aynı). Yeni yardımcı `ToTop()`: sayfa başa
    alınıyor **ve** bekleme oyunun kendi cevabına bağlı — `scroll` listenin nerede durduğunu söylüyor (`0 of 728,6`;
    baştayken ilk sayı `0`). Satırların çizilmesini de hemen ardından gelen "iki okumada aynı" işareti (`Duties`'in
    kendi okuması) karşılıyor. `fm responsibilities give "Assigning Scouts"` TR + EN dört durumda (`fm open "Messages"`
    sonrası / zaten açık / `fm open "Squad Dynamics"` sonrası / liste elle dibe kaydırılmış) **eskisiyle birebir**:
    TR `md5 570b1bd243e4d67a2f4800b7588ab50b`, EN `md5 0030afd6c876a5bc3197cca3da59e1b7`, süre 11–15 sn (nötr);
    negatif iki dilde `rc=64` (§8.215).
  - [?] TrainingSchedule 345 (`fm schedule set`: seçenek basıldıktan sonra açıklamayı okumadan önceki sabit 500 ms):
    ölçüldü — açıklama basıştan sonra birkaç karede değişiyor (yeni metin 107–371 ms'de oturuyor, arada boşaltılmış
    bir an var: 6 metin, 312 ms) → 500 ms'in payı 1,3–2,2 kat. Yerine "açıklama basıştan önceki hâlinden farklı ve
    `About` bir şey söylüyor" işareti yazıldı, derlendi; TR'de iki durumda eski derlemeyle **birebir** çıktı verdi
    (`Physical → General` `md5 0aaf9592…`, `General → Physical` `md5 414fade6…`). Üçüncü durum ve İngilizce
    tamamlanamadı: `fm schedule` ara sıra `Takvimin antrenman sekmesi okunamadı.` ile düşüyor — takvim sayfası
    `General` ve `Training` sekmeleri için **ayrı** `MonthDropdown` tutuyor, sekmeler ayrı aya bakabiliyor ve
    `CalendarMonths` etiket boş gelince yanlış aya geçiyor (eski derlemede de aynı, koda bağlı değil; §8.216).
    Kural 4 gereği kod **geri alındı**. Faruk'a soru: bu takvim/ay karışıklığı ayrı bir madde olarak düzeltilsin mi
    (o zaman bu madde ölçülüp bitirilebilir), yoksa ölçüm için ayrı bir kayıt/sekme durumu mu kullanılmalı?
- [ ] **Kısa `Until` (≤ 3 sn) — 16 yer;** "gelmezse yok say" anlamına gelenler tehlikeli: Actions 126 · Budget 70 ·
  ~~Calendar 157~~ · ContractTalks 364 · ~~Finances 238~~ · League 216 · MatchControls 131 266 · Prep 124 · Program 986 1918 2118 ·
  Reads 892 · Saves 207 220 · ScoutFocus 55 · StaffSearch 354 · Stages 337 · TacticFiles 29 · TacticSlots 112 ·
  TrainingSchedule 491 506 · TraitTraining 89
  - [x] Actions 126 (`AfterAction`'ın konuşma yolu): "oyun bir konuşma açtı mı" artık ekrandaki konuşan düğmelere değil,
    seçimden **önceki** düğme kümesinden farklı olmasına bakıyor (`TalkButtons()`: düğmenin işaretçisi + yazısı).
    Gazete maddesinin isteğe bağlı cevapları (ör. "Adjust Scouting Budget", bütçe kartı ekranda) da `SpeakToAction`
    taşıdığı için konuşma sanılıyordu: o ekranda `fm actions "Ben House" "Contract>Enter Contract Negotiations"`
    oyunun sorusu yerine bütçe seçeneklerini basıyor, oyunun sorusu (seçimden 0,24 sn sonra gelen) cevapsız kalıyordu.
    Sonra: TR + EN'de soru basılıyor, üç durumda aynı; gerçek konuşma (`Interaction>Praise>…`) hâlâ açılıyor; sonuçsuz
    iş (`Training>Rest Player>1 day`) "yapıldı" diyor ve veriden doğrulandı (`fm intensity` → RestDays 1); sweep 50/50 (§8.199).
  - [x] Budget 70 (kartın slider adımları): ölçüldü, **değişiklik gerekmedi**. Okuma, oyunun kendi işaretiyle kabul
    ediliyor: kartın `budget.BudgetAdjustmentEditedTransferValue` / `...EditedOtherValue` yazıları değişti **ve**
    yürürlükteki adımı ayıran yıldız adıma uydu. 7 koşu × 16 adım (TR + EN; sayfa ilk kez açılıyor / zaten açık /
    kart zaten açık): `setcontrol` bittikten sonra kabul eden okuma 20–371 ms, 112 adımın 11'inde ikinci okuma
    gerekti; 3 sn sınırına ≥8 kat pay. TR ile EN'de rakamlar aynı (§8.200).
  - [?] Stages 337 (`fm stages` kupa turu: her kaydırmadan sonra "iki okuma aynı"): ölçüldü, **kod değişmedi**. Turun
    bağları pencerede tam durmuyor (Carabao Cup 1. Tur: 35 bağ, pencere 18–25 satır) ve liste `to=0..1` ile geziliyor;
    menzil `"0 of 1401,8"` yazısından okunuyor ama o sayı oyunun **kendi dilinin** ondalık işaretiyle geliyor ve
    `InvariantCulture` onu çözemiyor → TR'de `extent = 0`, yani gezinti **hiç çalışmıyor** (TR 18–25 bağ, EN 24;
    `--all`da TR 18 / EN 24). Menzil oyunun okuyucusuyla (`GameNumbers.Read`) çözülüp denendiğinde gezinti çalışıyor
    (12 adım) ama sonuç kararsız (TR 35/19/19, EN 35/35/19): çapa okunan pencerenin ilk bağının **yazısı** ve kaydırma
    ilerleyince o satır yerleşimden çıkıp `/scroll` cevapsız kalıyor (döngü kırılıyor), ayrıca işaret kaydırmadan
    önceki pencereyi de kabul edebiliyor. İkisi birlikte düzeltilmeli (sabit çapa — satır işaretçisi ya da listenin
    kabı — **ve** "pencereden farklı **ve** iki okumada aynı"); kanıtlanmadığı için geri alındı (§8.231). Faruk'a
    soru: çapa için eklentiye tur satırlarının işaretçisini veren bir alan mı eklensin (yazıyla çapa kaydırma
    ilerledikçe kayboluyor), yoksa gezinti listenin kabına mı çapalanmalı?
  - [x] Calendar 157 (`fm calendar` / `CalendarMonths`: "Today" düğmesinden sonraki bekleme): işaret "art arda iki
    okuma aynı" idi ve bu, basıştan **önceki** ayı da kabul eder — ölçüm: basıştan hemen sonraki ilk okuma hâlâ eski
    ay (126 / 95 / 128 ms), yeni ay 198 / 252 / 304 ms'de geliyor, yani pay bir okuma aralığından (0,3 sn) az; yavaş
    makinede iki okuma da eski ayı verir ve bütün ay yanlış aydan sayılır. Artık işaret oyunun cevabı: değer basıştan
    öncekinden **farklı** ve sonra iki okumada aynı (üst sınır 8 sn); "Today" zaten bugünün ayındayken kapalı ve basış
    reddediliyor (`error: … is disabled`) → o hâlde hiç beklenmiyor. TR + EN üç durumda (başka sayfa açık / zaten açık
    / başka sayfaya geçtikten sonra) ve ay elle uzağa kaydırılıp `fm calendar` denendiğinde çıktı eski derlemeyle
    **birebir** (`md5` TR `0dd9e264…`, EN `02589720…`); zaten açıkken 1,0 sn, ay uzaktayken 2,0 sn. Aynı yardımcıyı
    kullanan `fm schedule` ve `fm fixtures` çalışıyor; `sweep.sh` 50 komut, 0 hata (§8.233).
- [x] **`reply` cevapları:** başlıklar = düğmeler ve önceki mesajınkinden farklı (cevapsızda `CanRespond`); Türkçe 24/24,
  İngilizce 14/14, başka sayfa açıkken 4/4 (§8.178).
- [x] **`reply` ara boş an riski:** ölçüldü, boş ara durum **yok** — oyun cevap kümesini silip yeniden kurmuyor,
  başlık önce değişiyor, düğmeler boşalmadan eskisinden yenisine geçiyor (2→2, 2→1, 1→1 TR+EN, 20 geçişte 0 boş okuma,
  gerçekten cevapsız mesajda boş yol doğru çalışıyor). Bu yüzden koşul değişmedi (churn yok); gerekçe §8.193.
- [?] **Mesaj eki için 1,5 sn** (`WriteAttachment`, Program ~986): ek gelmezse "ek yok". Ölçüldü (16 Ekim, §8.192):
  sabit 1,5 sn'nin ~2,5 kat payı var (içerik ~0,6 sn'de oturuyor), bu makinede yanlış okumuyor. Boş ara durum
  (~0,2–0,4 sn: başlık var, içerik yok) oyunun "henüz bağlanmadı" hâli; ağaç (başlık, `#Attachments`,
  `FMUnbindDynamicBindingRooot`, `BindingRoot`, `BindingExpect`) içerikli ve boş durumda birebir aynı, tek fark
  kartın içeriğinin bağlanması (`#TileBindingExpect`). Eki olmayan mesaj türü bu kayıtta yok (14 mesaj denendi,
  hepsinde "Ek:"/"Haber:" var), gelen kutusu verisi de ek alanı taşımıyor. **Karar (Faruk, 16 Ekim): (a) — sabit
  1,5 sn şimdilik kalıyor** (pay ~2,5 kat, bu makinede doğru okunuyor). İki iş not edildi, ikisi de ekran/negatif
  durum ölçümü ister: **(1)** oyunun "ek hazır" işareti bulunacak — en güçlü aday kartın içeriğinin bağlanması
  (`#TileBindingExpect`); **(2)** eki olmayan bir mesaj yakalanıp o durumda ağacın/işaretin nasıl davrandığı
  ölçülecek (bu kayıtta örnek yok). İkisi de bulununca sabit bekleme işarete çevrilir (§8.192).
- [x] **Alt sayfa açmada sabit 1,5 sn** (`OpenSubpage`, Program ~2138): sabit bekleme kalktı, işaret oyunun kendi
  cevabı oldu. Ölçüm (canlı, eklenti): ana sayfa istendikten sonra çubuk **0,16–0,46 sn**'de cevap veriyor
  (`open?guid=<sayfa>&check=1` artık hata değil "not shown" diyor); ondan **önce** basılan satır kayboluyor
  ("no page with guid … (a subtab's page: …)") ve kendiliğinden gelmiyor — bu yüzden çubuk cevap verene kadar
  bekleniyor (0,25 sn aralık), sonra satır seçiliyor ve sayfa ancak **çubuk o satırda dururken** (`check=1`:
  "shown") **ve** içerik görünürken, üst üste iki okumada alınıyor; olmazsa dört kez yineleniyor (`fm coaches`
  TR + EN üç durumda eski derlemeyle birebir, süre 3,83/2,3/2,3 → 2,56/1,28/1,82 sn; `fm intensity --json`
  TR + EN üç durumda 25 satır ve aynı `md5`; `sweep.sh` 50/50; §8.246). Kalan: `fm open X` (OpenPage) hâlâ
  açılış bitmeden dönüyor — hemen ardından açılan sayfanın üstüne gelebiliyor (Mesajlar › Mutluluk'ta 1/10,
  §8.185); çağıranın kendi işaretiyle beklemesi ayrı iş.
- [ ] **Sabit üst sınırlar** (`Arriving` 8 sn, `Bound` 20 sn, maç akışında 3 sn'ler): yavaş makinede yetmeyebilir; ölçülecek.

### B. Oyun dili

Eklenti ekran yazılarını İngilizceye çevirip veriyor, CLI tek dil üstünden çalışıyor; gösterirken oyunun diline geri
çevriliyor. Kırılan yerler: oyunun **kurduğu cümleleri** (isim/sayı içeren) İngilizce parçayla arayan kod, çünkü onlar
tablodan çevrilemiyor.

- [x] Etiket ardından okuma (`After("Supporters")`) — dilden bağımsız (tarama 93 yer; club/budget/finances/takers/world denendi).
- [x] Yeni ek okuyucuları veri yoluyla (9 tür, FINDINGS §8.177); mesaj gövdesi/eki kimlikle (§8.176).
- [x] `budget ayarla` yönetim cümlesi (yol), `training-report` (oyuncu adı), stderr + tırnak içi yazılar oyunun dilinde,
  `reply "<konu>"` oyunun dilindeki konuyla, yüzde işaretinin yeri ("%70") oyundan.
- [x] Maç içi `sub` / `tactic role`: kulübeyi açan satırın yazısı Türkçede hiç gelmiyor → menüler ve kulübe yapıyla.
- [ ] **Cümle parçası arayanlar** (her biri örnek mesaj gelince yola taşınacak):
  - [ ] Program: kaptan eki "to be selected" · yönetim beklentisi "End of" · "Transfers In/Out" · sezon özeti
    "injuries this season" · Coach Report / Player Report " years old" Replace
  - [x] **Gözlemci/analist seçme penceresinin satırları** (`Rows`, ScoutFocus): satır bölgesi oyunun başlık sözü
    "World Knowledge" ile bulunuyordu (`SkipWhile(t => t != "World Knowledge").Skip(1)`); pencerenin hücreleri hiçbir
    veri yolu taşımıyor (ölçüldü: yalnız "Assign Any Scouts" onay kutusu `focusitem.anyscout`a bağlı; gövde satırları
    `StreamedTable #scouts-table` › `DropTarget #View` altındaki `TableRowNavigatable`lar). Satırın kendisi zaten
    3. hücresindeki "Active Focuses" sayısıyla belli olduğundan başlık sözü araması kalktı; okuma TR 3 + EN 3 durumda
    eski derlemeyle birebir (§8.247).
  - [x] **Home-grown (altyapı) kartı:** özet dalı kendini `rest[0] == "Homegrown"` + "Home-Grown Status" ile
    tanıyordu, ama kartın ilk sözü oyunun cümlesi ("Upcoming Status Changes") ve sütun başlığı "Homegrown Status"
    (tiresiz) → dal **iki dilde de** hiç çalışmıyordu, Türkçe ve İngilizce ham döküm veriyordu; satırlar da
    `EndsWith("years old")` ile bulunuyordu (TR "19 yaşında" → eşleşmez). Artık kart oyunun kendi bölüm başlığıyla
    tanınıyor, satırlar oyunun bağladığı **veri yolundan** (`binding.Name`; ardından yaş, ülke, kadro, statü, tarih —
    satır 3. `binding` hücresinde kesiliyor, yan panel dışarıda) okunuyor ve sütunlar bağlandıkça okuma oyunun
    kendi işaretiyle yineleniyor. TR 3/4 koşuda birebir `md5 aa452756…` (12/12 satır), EN 2/3 koşuda
    `md5 424f3646…` (12/12); eski derleme hiç satır vermiyordu (§8.245). Kalan oynaklık §8.192'nin park ettiği
    1,5 sn'lik ek okumasından; negatif komşu mesajlar birebir, `sweep.sh` 50/50.
  - [x] `player --career` tablosunun satırları başlık yazısıyla değil satırın kendi ilk hücresiyle (sezon,
    `2026/27`) bulunuyor: `start` `IndexOf("Avg Rat")` ile aranıyordu, ama başlık iki dilde iki ayrı İngilizce
    geliyor (`Info`/`Avg Rat` ↔ `Information`/`Avg. Rating`), Türkçede `start` 0'a düşüp komut
    `Kariyer tablosu okunamadı.` ile rc=1 veriyordu. TR üç durum + kaleci rc=0 (tek `md5`), EN çıktısı eski
    derlemeyle birebir; negatif oyuncu rc=1 (§8.214).
  - [x] sakatlık eki "Received in" / "Out for" — Türkçede çalıştı (tablodan çevriliyor)
  - [x] ~~Intensity "Ask … training intensities"~~ (sayfa Guid'iyle, §8.187)
  - [x] ~~Mentoring "assign mentoring groups"~~ (eylem kimliğiyle, §8.188)
  - [x] Jobs: satır ve panel artık yazıyla değil **veri yoluyla** bulunuyor — satırın kulüp hücresi `team.name`
    (yönetici hücresi "" / `Manager.Name`), panel başlığı yolu olmayan yazılar arasında kulüp adını içeren son yazı;
    "Managerial Position" arayışı ve lig sırasını doğrulayan `^\d+(st|nd|rd|th)$` kalktı (Türkçede sıra "7." idi,
    bu yüzden tüm liste düşüyordu). Ölçüm: TR'de önce `fm jobs --json` → "İş ilanları okunamadı.", sonra 12–15 ilan
    doğru alanlarla (üç durum + EN); detay (`fm jobs "York"`) iki dilde aynı (§8.196).
  - [x] **Jobs listesi yalnız görünen pencereyi veriyor:** liste artık kendi kaydırması boyunca baştan sona geziliyor
    (pencere pencere; adım, okunan pencerenin kaç satır getirdiğinden hesaplanıyor) → 15 satır yerine **321 ilan**,
    oyunun kendi sırasıyla; `fm jobs "<kulüp>"` satırı önce görünüme kaydırıyor (ilk penceredeki iş 1,3 sn). BAŞVURU
    sütunu artık yalnız menajersiz (boş) ilanlar için soruluyor — düğme yalnız seçili satırda belli oluyor ve 321
    satırın hepsini seçmek dakikalar sürerdi; dolu ilanlar "?". Üç durum (başka sayfa / zaten açık / başka sekme) ve
    iki dil aynı 321 ilan ve aynı sıra (§8.197). Panel gelmezse artık başka kulübün ayrıntısı basılmıyor: satır
    verisinden okunan alanlar + stderr uyarısı.
  - [x] **İş paneli ekran yazısıyla bulunuyordu** (`fm jobs "<ilan>"` ayrıntısı): panel başlıkta kulüp adıyla
    (`Contains(job.Club)`) **ve** "Training Facilities" satırıyla tanınıyordu; ikisi de dile/kayda bağlı — ulus
    ilanlarının başlığı oyunun kendi sözüyle geliyor (TR "Danimarka Menajerlik Görevi", satırın hücresi
    `team.name` = "Denmark") ve ulus panelinde "Training Facilities" satırı yok, bu yüzden `CanApply` `null`
    kalıyordu: `fm jobs "Denmark"` "Başvurmak için: … apply" diyordu ama `apply` "başvuru kabul etmiyor" diyordu.
    Artık panel oyunun kendi kabından okunuyor (`/texts?within=job-advert-container&paths=1`; şablonun adı, dilden
    bağımsız) ve bekleme oyunun kendi işareti: kap **veri yolu taşıyan** bir metin taşıyor **ve** art arda iki okuma
    aynı (satır seçilince panel bir an yalnız başlık + iki cevabı tutuyor: 0,259 sn'de 3 metin/0 yol, 0,466 sn'de
    hazır). TR + EN üç durumda: `Denmark`/`Fildişi Sahilleri` `CanApply:false` + "Başvuru kapalı…", `United States`
    `CanApply:true` + "Başvurmak için…"; kulüp ilanı `York` eski derlemeyle **birebir**; 8–9 → 3–5 sn; negatif
    `jobs "ZZZ yok"` değişmedi (§8.220).
  - [x] Saves "Saves automatically" / "Autosave" · Saves "Estimated game speed:" (`fm world`): menü artık
    yazıların yanındaki değerleri sayarak değil **veri yoluyla** okuyor — sayfa `anchorpath=game.GameTime` ile
    bulunuyor, değerler `game.RealTimeSinceLastSave`, `game.GameTimeSinceLastSave`, `game.AutosaveModeAndIntervalString`,
    `game.GameTime`, `game.ActiveNationsCount`/`ActiveLeaguesCount`/`EstimatedPlayerCount`,
    `game.DetailLevel*`, `Person.Name`/`ManagedTeamsString`, `app.LastSaveSummary.GameSummaryGameName` ile alınıyor;
    "Saves automatically" / "Estimated game speed:" / "Selected Leagues" / "Save Game" arayışları kalktı (§8.221).
    Menajerin durumu (kartın kendi rozeti) yolu olmayan bir yazı olduğu için kulüp değerinin **yanındaki** yazı olarak
    alınıyor ("Game Status" etiketi de kalktı); TR + EN üç durumda çıktı eskisiyle birebir.
  - [x] Stages "This round will be drawn": kurası çekilmemiş turun tarihi artık oyunun İngilizce cümlesine değil,
    tarihin **kendi biçimine** bağlı — cümleyi oyun kuruyor, TR'de "Bu tur 13/8/2026 tarihinde oynanacak" oluyor;
    İngilizce önek tutmayınca tarih hiç yazılmıyordu ("Bu aşamada gösterilecek bir şey yok."). Artık turun
    metinleri içinde dört haneli yıl + ayırıcı taşıyan sözün tarihi alınıyor (sıra oyunun tarih ayarı, §C).
    TR üç durumda `Kura henüz çekilmedi; çekiliş 13/8/2026.`, EN üç durumda eskisiyle birebir; TR'de `--all`
    8 turun 6'sının tarihini veriyor (eskiden yalnız İngilizce sözü tutanlar) (§8.223).
  - [x] Matches "Match Ratings" + "Inf" (`player <oyuncu> --matches`): sayfa artık başlık yazısıyla değil
    oyunun kendi kabıyla okunuyor — oyun maç puanları sayfasını `PlayerFormCard` diye adlandırıyor
    (`/texts?within=PlayerFormCard`; sayfa kapalıyken boş), her satır da bağlı olduğu **veri yoluyla**
    (`paths=1`), ve "sayfa yüklendi" işareti özetin kendi bağı (`formstats.GamesPlayed`) + satırların
    oturması; satırlar zaten numara + tarih çiftiyle bulunuyordu, `Inf` başlığı hiç gerekmiyor. "Match
    Ratings", "All Competitions" ve "Inf" arayışları kalktı. Aynı fonksiyondaki puan sütunu başlığı
    `"PUAN"` sabit yazılıydı → `L("PUAN")` (İngilizce oyunda "RATING"; eskiden tablo başlığı Türkçe
    kalıyordu). TR üç durum + kaleci + hiç oynamamış oyuncu (0 maç) eski derlemeyle **birebir**,
    EN `--json` birebir, EN insan çıktısında yalnız başlık düzeldi; negatif oyuncu rc=1; `sweep.sh`
    50/50 (§8.224).
  - [x] Release "Is there anything else" (karşılıklı fesih konuşması): tekliften sonra oyun asistanın cevabının
    ardına kendi kapanış sorusunu ekliyor ve bu soru **oyunun dilinde kuruluyor** (TR "Sizin için yapabileceğim
    başka bir şey var mı hocam?", EN "Is there anything else I can do for you?"). Eski süzgeç yalnız İngilizce
    öneği tanıdığı için TR'de o soru asistanın cevabı gibi yazılıyordu. Artık satır, konuşmanın genel kapatma
    düğmesinin (`EventFmxClearChanges`, 31) bitirdiği satır olarak düşülüyor; metin araması kalktı. TR üç
    durumda yalnız asistanın cevabı, EN üç durumda eskisiyle birebir (§8.225).
  - [?] Penalties "Starting at " / "On Subs bench" (`fm penalties` tablosunun "MAÇA" sütunu): ölçüldü, **kod
    değişmedi**. Alan (`tactic.TeamSelectedPositionStringFromPlayer`) oyunun kendi cümlesi ve iki dilde iki ayrı
    söz veriyor: TR'de kulübedeki `On Subs bench` (eklenti çeviriyor), ilk 11'de başlayan `İlk 11'de (DOSMSğ)`
    (içinde mevki kodu olduğu için çevrilmiyor); EN'de sırayla `On Subs bench` / `Starting at DMCR`. CLI'nin
    `Starting()`i yalnız İngilizce `Starting at ` önekini tanıyor ve öneki sabit Türkçe yazıyor → TR'de tablo ham
    oyun cümlesini, EN'de `ilk 11 · DMCR` (Türkçe söz) yazıyor; `--json` `Where` alanı da dile göre değişiyor.
    `/form` bu satır için dile bağsız bir "ilk 11 / kulübe" alanı vermiyor ve `/tactic` Slot adları kısaltılmış
    (`Bayliss`), ceza tablosundaki tam adlarla birleştirmek kırılgan. Faruk'a soru: yer bilgisi nereden gelmeli —
    eklentiye slotun oyuncusunu söyleyen bir alan mı eklensin, yoksa `Position` maskesi CLI'nin kendi slot
    adlarına mı çevrilsin? (§8.218)
  - [x] Recruitment Focus formu "Scouting Areas (0)" başlığını yazıyla arıyordu: başlık sayı taşıyan oyun cümlesi
    (TR "Gözlemleme Yerleri (0)"), İngilizce sözü tabloda yok → `fm scout areas` TR'de her durumda "Bölge listesi
    bulunamadı." (rc=1). Artık açılır liste formun kendi bağıyla bulunuyor (`Binds` →
    `focusitem.possiblescoutingareasdropdownselectedoption`; dilden bağımsız). TR üç durum + EN üç durum aynı üç
    seçenek (`Competition ›`, `Nation ›`, `Recently Selected ›`), EN çıktısı eski derlemeyle birebir; `scout areas
    "Nation"` → `Europe ›`; negatif yol aynı hatayı veriyor (§8.227). Yazma yolu (`scout new/edit --area`) da
    artık aynı bağı kullanıyor: liste `FormBound` ile, "eklendi" işareti listenin **hemen önündeki** başlığın
    değişmesi (başlık oyunun cümlesi: TR "Gözlemleme Yerleri (0)" → "(1)") — İngilizce söz aranmıyor. TR'de
    eskiden `Nullable object must have a value` ile çöküyordu; TR üç durum + EN üç durum aynı yolu geçti,
    hiçbir odak değişmedi (§8.228).
  - [x] `scout new/edit --by` / `--analysts`: seçim penceresini açan `Edit` düğmesi artık başlık yazısıyla değil,
    satırın kendi rolüyle bulunuyor — iki satırın düğmesi aynı şablon (`button-base-trailing-icon-label`), başlık
    da oyunun cümlesi ("Gözlemci Görevlendir (1)"), o yüzden satır, yanındaki insanın rozetindeki rol sözünden
    ("(Gözlemci)", "(Oyuncu Analisti)"; söz oyunun kendi tablosundan) seçiliyor, bulunamazsa form sırası (gözlemci
    önce). Pencerenin hangi liste olduğu yazıyla değil kutusunun bağlı olduğu alanla (`focusitem.anyscout` /
    `focusitem.anyanalyst`) doğrulanıyor; kaç kişinin seçildiği de oyunun cümlesinden değil, kendi kabından
    (`SIVisible-selected-scouts-number` / `…-analysts-number`) okunan sayıdan. TR üç durum + `--analysts`, EN üç
    durum + `--analysts` rc=0; negatif TR rc=64 (§8.229).
  - [~] ContractTalks "final decision" / "be in touch": İngilizce dışında tahmin etmiyor, dürüstçe "ya imzalandı ya da
    birkaç gün içinde" diyor.
- [ ] **İngilizce yazıyla basma/tıklama — 44 yer** (`press/click text="Acknowledge and Move On"`, "Cancel", "Confirm",
  "Save Game As"…): eklentinin çeviri tablosundan geçiyor; tabloda 2.376 çakışma var ("Confirm" → Onayla/Kabul). Her biri
  öğe adı / olay adıyla basılabilir mi bakılacak. Ölçülmüş bir örnek (§8.206): TR oyunda `fm training "<oyuncu>"
  --trait-suggest` düğmeyi bulamıyor — düğme "Oyun Karakteristiği İdmanı Önerisi İste" diye geliyor, İngilizce
  "Ask For Trait Training Suggestion" ile eşleşmiyor (eski derlemede de aynı).
  - [x] `training --trait-suggest` düğmesi: artık eylem kimliğiyle bulunuyor — eklentinin verdiği `Action` düğümünün
    `ActionID`'si **1263235426** (`InteropEventIDs.PlayerAskStaffToSuggestPlayerTraitTraining`; `docs/reference/enums.txt`),
    işaretçisiyle basılıyor. Bu sözün İngilizcesi oyunun çeviri tablosunda hiç yok (`/translate?words=…` boş; komşu beş
    söz çevriliyor), o yüzden yazıyla basmak dilin tablosuna bağlıydı. Cevapsız (cümleli) durumdaki cevap da düğmenin
    kendi sözüyle aranıyor. TR + EN üç durumda öneri penceresi (rc=0), negatif oyuncu rc=1 (§8.209).
  - [x] `contract <oyuncu> --agent` satırı: artık eylem kimliğiyle bulunuyor — eklentinin `Action` düğümünün
    `ActionID`'si **1882353528** (`InteropEventIDs.PersonAskAgentAboutClientContractRenewal`), işaretçisiyle
    basılıyor. Bu satır oyunun kendi konuşma cümlesi; TR'de "Temsilci İle Yeni Sözleşme Hakkında Görüş" olarak
    geliyor ve tabloda İngilizcesi yok, bu yüzden `press text="Discuss New Contract With Agent"` TR'de hiç
    bulunamıyordu (üç durumda `menajerle görüşme seçeneği yok`, rc=1 — oysa satır ekranda duruyordu). Şimdi TR
    üç durumda menajerin konuşması + 4 cevap (rc=0), EN birebir aynı (§8.210).
  - [x] `propose <oyuncu>` öneri ekranının iki düğmesi ("Offer via TransferRoom" ve gönder düğmesi): artık
    eylem/kimlikle bulunuyor — birincisi eylemin `ActionID`'si **1164992875** (`PersonOfferToClubs`), ikincisi
    eylemin gönderdiği olay **32** (`EventFmxTransferOfferResponse`). Gönder düğmesinin sözü oyunun tablosunda
    başka bir metne bağlı ("Offer Player" → "Oyuncuyu Takasa Kat"), bu yüzden TR'de etiket İngilizce hiç gelmiyordu
    (`Oyuncuyu Kulüplere Öner`) ve `fm propose` TR'de her durumda `Önerme ekranı açılamadı.` ile rc=1 veriyordu.
    Şimdi TR'de iki oyuncuda üç durum aynı çıktı (rc=0), EN eski derlemeyle birebir (§8.217).
  - [x] `jobs "<kulüp>" apply` / `interest` düğmeleri: artık eylem/olay kimliğiyle bulunuyor — eklentinin verdiği
    `Events` alanı bu iki düğmede `EventFmxApplyForJob` / `EventFmxDeclareInterestInJob` (`/buttons?json=1`; TR + EN'de
    aynı, etiketler ise tablonun sözü). Basış işaretçiyle; negatifte (düğme kapalıyken) hiç basılmıyor, uyarı
    yazılıyor. TR + EN üç durumda aynı çıktı, rc=0 (§8.219).
  - [x] `release <oyuncu>` listesi: artık ekran yazısıyla değil **kabıyla** bulunuyor — liste, oyuncunun sözleşme
    kartının kendi listesi (`dropdowns` → `Binds == selectedaction`, `Within` zincirinde `PlayerContractCard`;
    şablonun adı, dilden bağımsız), etiket eşleşmesi yedek. Eskiden `Label == "Release"` aranıyordu; oyunun etiketi
    **"Set For Release"** (TR'de de) olduğu için liste hiç bulunmuyordu ve komut her oyuncuda "serbest bırakma
    seçeneği yok." diyordu (`--free`/`--mutual` yolu hiç açılmıyordu). Satırlar da artık İngilizce sözle değil
    oyunun sözüyle seçiliyor: `Release On A Free` → `/translate` "Serbest Bırak", oyunun satırı "Bedelsiz Serbest
    Bırak" onunla bitiyor; `Mutual Termination` iki dilde de İngilizce geliyor. TR + EN üç durumda `--free`
    tazminat penceresini gösteriyor ("Vazgeçildi", hiçbir şey değişmiyor), negatif ve listesi olmayan oyuncu
    (Oscar Thorn) aynı (§8.222).
  - [x] `scout remove` / `scout restart` düğmeleri: artık oyunun **olayıyla** bulunuyor — odak sayfasının
    "Remove Recruitment Focus" düğmesinin eylemi `EventFmxRecruitmentFocusRemove` (53) gönderiyor, "Restart
    Recruitment Focus" `…Restart` (54); ikisi de dilden bağımsız, oyunun kendi numarası (aynı yardım
    `Acknowledge`/§8.209'ta kullanılan `SpeakToActionEvent`). Ölçüm (TR): `/buttons?gen=…` → odak sayfasındaki
    dört düğmenin hepsi `SpeakToAction`;
    `pull?props=ActionID,Event` → Remove `53`, Restart `54`, "Create Recommended Focus" `1245866784`
    (`…CreateRecommendedFocus`), ama **"Edit Recruitment Focus" ve "Create a New Recruitment Focus"** ikisi de
    `ActionID 0` + `Event 0` — yani oyun bu iki düğmeye hiçbir kimlik vermiyor (etiket, oyunun çeviri
    tablosundan gelen İngilizce söz — §8.209'daki tür) → o ikisi etiketle kalıyor. Buton bulunamazsa
    eskisi gibi görevler panelindeki kaldır simgesine düşülüyor. TR üç durum (başka sayfa açık / sayfa zaten
    açık / başka sayfaya geçtikten sonra) + negatifler ve EN üç durum: `restart` rc=0 "Odak yeniden başlatıldı",
    `remove --yes` rc=0 "Odak durduruldu", durmuş odağa `remove`/süren odağa `restart` rc=1 ("zaten durmuş" /
    "zaten sürüyor"); sonuç `fm scout --json`ten doğrulandı (§8.234).
  - [x] `budget` düğmeleri (kartı açan döşeme + "Got It"/"Cancel"): üçü de oyunun İngilizce sözüyle basılıyordu.
    Kimlikleri ölçüldü: döşeme eylemi **1885623898** (`ClubBudgetAdjustmentTransfersAndWages`; yanındaki
    "Adjust Scouting Budget" 1332374646), "Got It" gönderdiği olay **EventFmxBudgetAdjustmentConfirm** (enum 146),
    "Cancel" aynı panelin yalnız kapatın (`ClosePanel`) düğmesi. Artık üçü işaretçisiyle basılıyor, bulunamazsa
    yazıya düşülüyor. TR + EN üç durumda üç yol (`ayarla 99` / `ayarla 15` / `ayarla 14` + geri) eski derlemeyle
    **birebir**, izde `click ?ptr=…` (§8.235). Kartın **okuma** yolu (Wage/Transfer Budget, Scouting, "Budget
    Adjustment" çapaları) yazıyla kalıyor: o sayıların sayfada veri yolu yok (ölçüldü, ayrı iş).
  - [x] `save --as` / `saves delete`: kayıt tarayıcısının "Cancel" ve "Save"i artık **gönderdikleri olayla** basılıyor —
    Cancel `ClosePanel`, Save `SaveGame` (`SaveAs`'te `Save`, `CloseSaveBrowser`da Cancel; `Name` ayırt etmiyor:
    Browse/Delete/Cancel aynı `button-base-leading-icon-label` şablonunda). Kimlik bulunamazsa yazıya düşülüyor.
    TR + EN, üç durum × iki yol (`saves delete "<yüklü kaydın adı>" --yes` → tarayıcı açılıp Cancel ile kapanıyor,
    rc=1; `save --as "<aynı ad>" --yes` → Save + "Replace Existing File" onayı, rc=0) eski derlemeyle **birebir**
    (`md5`); izde `click ?text=Cancel`/`?text=Save` → `click ?ptr=…` (§8.236). Yükleme ekranının `Load`u yazıyla
    kalıyor — sıradaki alt madde. FM menüsünün "Save Game As" satırı da yazıyla kalıyor (satırın numarası eklentinin
    `Events` özetinde yok; eklenti değişikliği ister).
  - [x] **Onay pencereleri başlık yazısıyla değil oyunun kendi adıyla** (`save --as`in "Replace Existing File"i,
    `saves delete`in "Delete File"i): eklenti (`/dialog`) her pencerenin `Type`ını veriyor — ölçüldü:
    `ConfirmSaveOverrideDialog` ve `confirmDeleteSelectedSave` (ikisinin başlığı TR oyunda bile İngilizce:
    "Replace Existing File?", "Delete File"). `DialogButton` artık önce `Type`e bakıyor, bulamazsa başlığa
    düşüyor. `save --as "<yüklü kaydın adı>" --yes` TR + EN, üç durumda eski derlemeyle **birebir** (`md5` TR
    `d4d552c7…`, EN `46212f7f…`); silme yolu kendi kopyamızla uçtan uca ölçüldü (klasöre `t48 test.fm` kondu →
    `fm saves delete "t48 test" --yes` rc=0, izde pencerenin "Yes"i `?ptr=` ile basılıyor, Faruk'un dört dosyası
    yerinde); negatifler değişmedi (§8.237). `MatchDay`in üç penceresi de (`Instant Result`, `Team Selection`,
    `Injured Players`) hâlâ başlık yazısıyla aranıyor — ayrı alt maddeler.
  - [x] Kayıt tarayıcısının `Delete`i artık **gönderdiği olayla** bulunuyor: düğme `DeleteSelectedGame` gönderiyor ve
    satır seçilene kadar kapalı (`Enabled=False`), o yüzden işaret "bu olayı gönderen **ve** açık düğme" (eskisi
    `Label == "Delete"` + `Type == "SIButton"` + `Enabled`); kimlik yoksa yazıya düşülüyor. Kendi kopyamızla uçtan
    uca: TR + EN, üç durum (başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra) `md5` eski
    derlemeyle **birebir** (isim dışında tek satır "Silindi/Deleted"), izde `click ?text=Delete` → `click ?ptr=…`;
    negatifler (`zzz yok` rc=1, yüklü kayıt rc=1) değişmedi; Faruk'un dört dosyası yerinde (§8.238).
  - [x] `staff find` arama panelinin `Apply`i ve `Cancel`i artık **gönderdikleri yükle** basılıyor: ikisi de aynı olayı
    (`EventFmxFilterRestoreFilter`) gönderiyor; ayırt eden yük — Apply `SendsProps True`, Cancel `False` — dile ve
    skine bağlı değil (`Name` de farklı ama o şablon sınıfı; kural 3). `click ?text=Apply` ve panel form okumasındaki
    `Text == "Cancel"` yerine `PanelSent(apply)` geçti (iki çağrı yeri), kimlik yoksa eskisi gibi yazıya düşülüyor.
    TR + EN, üç durum × iki yol (`--role "Coach"` / `--role "zzz yok"`) eski derlemeyle **birebir** (`md5`), izde
    `click ?ptr=…` (§8.239). Panelin **okuma** yolu (`Under`/`Row`) hâlâ `form ?anchor=Apply` ile kelimeyle çapalı —
    sıradaki alt madde. **→ Tur 51'de yapıldı (aşağıda).**
  - [x] `fm players` arama panelinin **okuma** yolu artık yazıyla çapalı değil: panel `form ?anchorpath=` ile oyunun
    bağladığı başlıktan (`filter.FilterRulePlayerPosition.Name`, eski `?anchor=Apply` ile **birebir** aynı 55 öğe),
    her kural kendi denetiminin yazdığı veri yolundan (`filter.filterruleage.minvalue`,
    `filter.FilterRuleScoutingGrade.MinValue`…) ve satırın iki denetimi (`rule.selectedcompareflag`, `rule.minvalue`)
    kendi yollarından bulunuyor; "Is At Least"/"20" gibi İngilizce seçenek sözleri kalktı. `Under` artık kuralın
    **yolunu** alıyor (`Reads.cs`), yazı yalnız kullanıcının gördüğü ad olarak kaldı. Kanıt (TR, üç durum, eski
    derleme `git stash` + `dotnet build -c Release`): `fm players "Aaron" --age 18-23` **eskiden** rc=1
    `error: arama panelinde 'Aged Between (Years)' yok` (TR'de başlık "Aged Between **(Yıl Olarak)**" geliyor, üç
    durumda da boş çıktı `md5 d41d8cd9…`) → **şimdi** rc=0, 70 satır (`e810bfd7…` / `931742e6…`); `--attr "Pace>=15"`
    (`a5e61246…`/`93f620f1…`), `--grade "B+"` (`718b654d…`, üç durumda da), `--role "Goalkeeper"` (`5c72cb45…`),
    `--max-value 500K` (`de8b428c…`) iki derlemede **birebir** (s1/s3), hepsi rc=0. EN: oturmuş durumda `--attr`
    `6f67b7f5…`, `--age` `51c9d225…`, `--grade` `db9d5183…` — yeni derlemenin s1/s3'ü eski derlemenin s3'üyle
    birebir (§8.240). Dil geçişinden hemen sonraki koşular yine kısa tablo veriyor (22/25 satır; eski derlemede de
    aynı, §8.239 yan bulgusu) — ölçüm zinciri bu yüzden oturmuş durumlar karşılaştırıldı.
    Aynı panelde kalan: `PanelButton("Apply"/"Cancel")`in **yedeği** hâlâ yazıyla (asıl yol `PanelSent`, §8.239) ve
    `Row()`in satır adı eşleşmesi niteliğin oyunun menüsünden gelen **kendi sözüyle** (sabit İngilizce değil).
  - [x] Recruitment focus formunun **adım düğmeleri** (`Next`/`Back`): formun "açıldı / yarım kalmış" işareti
    (`OpenFocusForm`) ve `fm scout areas`in bölge adımına geçişi yazıyla konuyordu (`FormButton(FocusForm("Next"),
    "Next")`, `click ?text=Next`). Ölçüldü: ikisi de **aynı olayı** gönderiyor
    (`EventToPlugin:EventFmxRecruitmentFocusMoveToStep`, enum 1227976013), ayırt eden **yük** — ileri `SendsProps One`,
    geri `MinusOne` — ve göstermeyen adımın kendi düğmesi `Enabled=False` (adım 2'de ileri düğmesi ekranda duruyor
    ama kapalı). Artık `FocusSteps()` bu olayı + yükü taşıyan açık düğmeyi arıyor, kimlik yoksa eskisi gibi yazıya
    düşülüyor. Kanıt: TR + EN, dört durum (başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra /
    form ikinci adımda bırakılmış → kalıntı dalı) `fm scout areas --json` eski derlemeyle **birebir**
    (`md5 fed56895…`, rc=0); izde eski `form ?anchor=Next` + `click ?text=Next` → yeni `buttons ?json=1&gen=1` +
    `click ?ptr=…`; odağı kurmayan `scout new` yolunda (`--area "ZZZ yok"`) iki derleme de rc=64, odak sayısı
    değişmiyor (§8.241). Aynı formda kalan: **`Cancel`/`Confirm`** hâlâ yazıyla — ölçüldü, ikisi de
    `RecruitmentFocusEditConfirmationDialog` taşıyor ve ayırt eden alan (`m_cancelEdit`) `/buttons?json=1` özetinde
    **yok** (eklenti değişikliği + oyun restartı ister, Sonraya bırakıldı); ayrıca `AbandonFocus`in
    `click ?text=Cancel`i, gözlemci seçme penceresinin `Cancel`/`Confirm`i, okuma çapası (`form ?anchor=Next`) ve
    `scout new/edit`in kalan `Confirm` basışı. **→ Tur 53'te `scout new`/`edit`in adım geçişi ve adım işaretleri de
    `FormStep()`e taşındı (§8.242): "ikinci adım açıldı" işareti artık `FormStep() == "back"`, "birinci adım açık /
    mevki oturdu" işareti `FormStep() == "next"`, ileri basışı `click ?ptr=…`; üç durum × iki dilde eski derlemeyle
    birebir (rc=64, aynı stderr). Kalan: Confirm basışı, Confirm akışındaki "form kapandı" işareti ve okuma çapası
    `form ?anchor=Next`/`Back` (ölçüldü: adım 1'de `/form?anchor=Next` 52 öğe / `anchor=Back` 0, adım 2'de tersi).**
  - [x] `scout new/edit`in **"form kapandı" işareti** artık yazıyla değil, adım düğmelerinin olayıyla: `ScoutNew`in
    `catch` dalındaki "form açık mı" ve Confirm akışındaki "form kapandı mı" `FormStep() != null` / `== null`
    (`EventFmxRecruitmentFocusMoveToStep` + `One`/`MinusOne`, yalnız açık düğme). Eskiden ikisi de **hem** yazı
    çapasıydı (`form ?anchor=Next`/`Back`) **hem** düğme etiketi ("Next"/"Confirm") — etiket bugün iki dilde de
    İngilizce geldiği için tesadüfen çalışıyordu. Ölçüm (canlı, Confirm'e basınca): adım düğmeleri, form okuması ve
    odak listesi **aynı anda** dönüyor (tık 0,04 sn'de; ilk yoklamada 0,271 sn'de düğmeler boş, form 0 öğe, liste
    5→6) → iki işaret eşdeğer. Kanıt: `fm scout edit "<odak>" --name "<yeni>"` TR üç durum + EN üç durum rc=0 ve
    çıktı aynı; izde Confirm'den sonraki yoklamalar `form ?anchor=Back` → `buttons ?json=1&gen=1`; hata yolu
    (`scout edit 1 --area "ZZZ yok"`) TR + EN üç durumda rc=64 ve stderr md5 §8.242 ile birebir (§8.243). Kalan:
    `click ?text=Confirm` / `AbandonFocus`in `click ?text=Cancel`i (ikisi de `m_cancelEdit`; `/buttons?json=1`
    özetinde yok → eklenti + oyun restartı ister), gözlemci seçme penceresinin `Cancel`/`Confirm`i ve okuma çapası
    `form ?anchor=Next`/`Back`. **→ Tur 55'te gözlemci seçme penceresinin `Cancel`/`Confirm`i olaya bağlandı
    (aşağıda, §8.244).**
  - [x] **Gözlemci seçme penceresinin `Cancel`/`Confirm`i** (`AssignFocusScouts` → `Close`) artık yazıyla değil
    **gönderdikleri olayla** bulunuyor: ikisi de `EventFmxRecruitmentFocusScoutAnalystEditCancel` /
    `…EditConfirm` (+ `ClosePanel`) gönderiyor ve açık; **formun** kendi Cancel/Confirm'i başka bir olay
    (`RecruitmentFocusEditConfirmationDialog`) taşıdığı için karışmıyor. Satır düğmesini seçen `Show` de artık
    "Cancel"/"Confirm" yazısına değil bu iki düğmenin **işaretçisine** bakıyor — yani aynı fonksiyonun pencereyle
    ilgili bütün yazı bağımlılığı kalktı. Olay adları TR + EN'de birebir (canlı ölçüm, 13 yoklama). Kanıt
    (`scout edit 1 --by "ZZZ yok"` iptal / `--by "Kenny Morrison"` onay; üç durum: `fm open "Messages"` sonrası /
    hemen ikinci koşu / başka sayfaya geçtikten sonra): EN'de **eski derlemeyle birebir** (aynı başlangıç durumu;
    iptal üç durumda rc=64, stdout boş `d41d8cd9…`; onay `190cf72a…` / `00e361e1…` / `82814c43…` = eski
    derlemenin aynısı; yalnız bir koşu köprü sustuğu için düştü), TR'de iptal üç durumda rc=64 + boş stdout, onay
    rc=0 (`537180b3…`). İz: eski `form ?anchor=Assign Any Scouts&up=8&buttons=1` (yazı) → yeni
    `buttons ?json=1&gen=1` (olay); basış iki derlemede de `click ?ptr=` (§8.244). Aynı pencerede kalan: okuma
    çapası `form ?anchor=Assign Any Scouts` (ölçüldü: `anchorpath=focusitem.anyscout` **0 öğe** döndürüyor, yol
    çapası tutmuyor) ve `Rows()`in başlık sözü ("World Knowledge") — ikisi ayrı iş.
- [x] Veri merkezi bulgularından İngilizce çıkan satırlar: ölçüldü — **tabloda olan** cümle İngilizce geliyordu, oyunun
  kendi sözü öğrenilmiyordu (TR oyunda "We've regained possession well in central areas of our own third of the
  pitch."; `/translate?words=…` → "Yarı sahamızın orta kısmında çok top kazandık."). Artık `reply` bir ekte veri
  merkezi bulgularını yazmadan önce o cümleler için oyunun kendi tablosuna bir kez soruyor ve satırı oyunun sözüyle
  yazıyor; tabloda olmayan (sayı/isim taşıyan) cümleler zaten oyunun dilinde geliyor. TR üç durumda satır Türkçe,
  EN üç durumda eskisiyle **birebir** (§8.226).
- [ ] `contract <oyuncu> --agent`: TR'de kimi koşuda satır bulunamıyor (aynı oyuncu bir koşuda rc=0, başka koşuda
  `seçenek yok`; aynı oyuncular EN'de hep bulunuyor). Ekranda kalan bir sözleşme görüşmesi kartı (`Finalise
  Promises` / `Cancel Negotiations`, `Action` alanı boş) görüldü — kartın kendi işareti ölçülecek (§8.210 sonu).
- [x] **`opposition` seçenek adları:** seçenekler eklentiden İngilizce geliyor; hata/yardım satırı ise onları oyunun
  sözleriyle yazdığı için gösterilen ad geri yazılamıyordu (`Pres=Her Zaman` → "yok"). Artık değer hem İngilizcesiyle
  hem ekranda görünen sözüyle kabul ediliyor (kimlik listesi hâlâ eklentiden; oyunun verdiği `GameWords` eşlemesiyle
  çevrilir, sabit söz yok). TR üç durum + EN üç durum: `Her Zaman`/`Hafif`/`Sol Ayak` ve `Always`/`Hard` aynı satıra
  oturuyor; negatif hâlâ hata veriyor (§8.211).
- [x] **Mutluluk seviyesi:** sıralama ve renk artık seviyenin yazısına değil oyunun kendi puanına (`Human.Team.Players`
  › `HierarchyOverallHappinessLevel`; 80 Delighted … -65 Very Unhappy) bakıyor, bant sınırları ölçülen boşluklarda (-30/30);
  seviye/moral/konum yazıları oyunun diline çevriliyor. Türkçe (3 durum) + İngilizce (2 durum) aynı sıra/renk (§8.189).
- [ ] Haberde "Portugal" çevrilmedi (news yolu eşi öğrenmiyor); "Defence (Core)" birim adı İngilizce kaldı.
- [ ] İngilizce oyunda bugünkü değişiklikler (yeni ekler, yüzde, sub, reply) denenmedi.
- [x] **`fm settings` ayarı başlık yazısıyla buluyor; Almanca oyunda dil ayarı bulunamıyor** — başlık `Heading:
  "Game Language"` diyordu, Almanca oyunda `/form` aynı başlığı **"Language"** diye veriyor (oyunun "Sprache" yazısı
  iki İngilizceyle eşleşiyor, eklenti ilkini döndürüyor; §8.167 çakışması): dil satırı listede yok, `fm settings dil
  Deutsch` ayarı yapıyor ama okuma dönmüyordu. Artık ayar, başlıkla değil öğenin bulunduğu **blok adıyla**
  bulunuyor (eklentinin `/form`da verdiği üst öğe adları: `GameLanguagePreferencesBlock`, `WagesPreferencesBlock`,
  `AutoSaveIntervalSetting`…; şablonun kendi adları, dilden bağımsız), bulamazsa eskisi gibi başlığa düşüyor.
  Almanca oyunda 15 satır + dil satırı `German`, `fm settings language "Türkçe"` → `German → Turkish`; TR çıktısı
  değişiklik öncesiyle birebir, EN'de eski derlemeyle birebir; maaş ayarı yazılıp geri alındı (§8.203).
  Kalan: İşlem sekmesinin üç listesi aynı bloğu paylaştığı, slider/onay kutusu/radio için oyun blok adı vermediği
  için onlar hâlâ başlıkla bulunuyor.
- [x] **`fm settings` sekmesi artık yazıyla değil, düğmenin bulunduğu kapsayıcıyla açılıyor** — sekme yazısı oyunun
  sözü, TR/DE'de "Matchday", EN'de "Match Day"; artık üç sekme (`MatchDayTabButton`, `YourWorldTabButton`,
  `ProcessingTabButton`; şablonun kendi adları) düğmesiyle açılıyor, bulunamazsa eskisi gibi yazıya düşüyor. TR/DE
  `fm settings --json` 15 → **19** satır (maç gösterimi/dinamik mod/vurgu hızı/ara hızı artık okunuyor), EN çıktısı
  eski derlemeyle birebir; yazma yolu da çalışıyor (§8.204).

- [x] **`fm prep` gösterdiği odağı geri kabul etmiyordu; `--json` oyunun sözünü İngilizce bırakıyordu** — odaklar
  oyunun kendi sözleri (TR "Kaydedilen İlerleme"), veri ise İngilizcesini taşıyor ("Progression"). İnsan çıktısı
  `GameWords` ile çevriliyordu, ama `prep <yuva> <odak>` yalnız İngilizceyi kabul ediyordu: TR'de ekranda görünen ad
  yazılınca "diye bir seçenek yok" (rc=64), `--json` ise `Name`/`Means`/seçenek adlarını ham İngilizce veriyordu.
  Artık seçenek hem gösterilen sözle hem verinin İngilizcesiyle bulunuyor (§8.211'in kuralı), mesajlar/seçim
  listesi/`--json` oyunun sözüyle yazılıyor; sayıyla seçme duruyor. Kanıt: TR üç durumda `md5 e9a22f16…` (önce
  `50611761…`, İngilizce), `fm prep topla "Kaydedilen İlerleme"` → "zaten Kaydedilen İlerleme" (önce rc=64),
  negatif `zzz` → Türkçe liste rc=64; EN üç durum eski derlemeyle **birebir** (§8.232).

### C. Oyunun ayarları (dil dışında)

- [x] Para birimi ve kısaltmalar oyunun biçimlendiricisinden (`/money`), ondalık işareti oyundan.
- [x] **Maaş dönemi:** ölçüldü — kutuya girilen tutar oyunun "Wages" ayarının döneminde sayılıyor: TR + Aylık'ta
  `fm contract --term "Wage=5000"` kartı `5.000 £ aylık` (≈21,7B/hafta), TR + Haftalık'ta `5.000 £ hft`,
  EN + Aylık'ta `5.000 £ p/m` — yani aynı sayı ayara göre ~4,3 kat farklı ücret. Çevrim yapılmadı (dönemi okuyacak
  sağlam yol yok, 52/12 tahmin olurdu); CLI'nin kendi sözü help (TR + EN) ve MCP'de "oyunun maaş döneminde" olarak
  düzeltildi, davranışa dokunulmadı (§8.194). Yan bulgular: `finances` statü satırları aylık, toplamı haftalık;
  görüşme kartının "şimdiki ücret"i terimlerden başka dönemde olabiliyor; oyunun yazıları bağlanınca donuyor.
- [x] **Tarih biçimi (gün/ay sırası):** ölçüldü ve düzeltildi — sıra oyunun kendi ayarından geliyor
  (Preferences › Your World › Format; `dd/mm/yyyy` · `mm/dd/yyyy` · `yyyy/mm/dd` · `yyyy/dd/mm`), CLI ise
  `Matches.cs`'te `^\d{2}/\d{2}/\d{4}$` arıyor ve kısa gösterimi `dd/MM/yyyy` diye çözüyordu. Aynı maç
  (20 numaralı, Bristol Rovers, 1 Ağustos 2026) sayfada sırayla `01/08/2026` · `08/01/2026` · `2026/08/01` ·
  `2026/01/08` yazılıyor; `fm player "Ben House" --matches --json` **önce** 19/19/**0**/**0** satır veriyordu ve
  `mm/dd/yyyy`'de zengin tablo tarihi `8 Oca` (1 Ağustos yerine 8 Ocak) yazıyordu. Artık tarih sıraya bakmadan
  tanınıyor (dört haneli yıl bir uçta, ayırıcı `/ . -`) ve tablo oyunun kendi tarih yazısını gösteriyor; dört
  ayarda da 19 satır, üç durumda (kart ilk kez / zaten açık / başka sayfa açık) TR + EN aynı (§8.195).
  Not: `fm settings` seçeneği insan çıktısında `gg/aa/yyyy` görünür ama komuta oyunun ham adı verilmeli
  (`fm settings tarih dd/mm/yyyy`).
- [?] **Boy/kilo birimleri:** ölçüldü, kod değişmedi — oyuncunun `Height` alanı ayardan bağımsız ham santimetre
  (`fm settings boy Fit` iken de `Height: 178`), yani CLI'nin " cm" yazısı veriyle tutuyor; oyunun birim ayarına
  uydurmak Preferences sayfasını açmayı gerektiriyor (~7 sn, daha ağır bağımlılık). Faruk'a soru (§8.195).

### D. Lig / takım / kayıt

- [x] Kodda lig, takım, kulüp ya da kayıt adı yok (tarama: yalnızca para biçiminin öğrenilene kadarki varsayılanı "£").
- [ ] Kariyer başka ülkede (Almanya, kupa adları, kayıt kuralları, Avrupa kupası) ve sıfırdan başlayan bir kayıt hiç
  denenmedi — yayın hedefi için şart (H).
- [x] `~/.config/fm-cli/` önbellekleri hangi kayda/dile ait olduğunu biliyor mu: `CacheFile` hepsini `cache/<dil>/<yönetici - kulüp>/` altına koyuyor (rol adları, görev alanları, talimatlar, sezon/istatistik, gelen kutusu numaraları…). Tek istisna `diff`in baktığı hâli (`Diff.cs`) üst klasörde yalnız takım adıyla tutuyordu; o da `CacheFile`e taşındı. Ölçüldü: TR'de bakış kaydedilip İngilizceye geçilince eski kod her oyuncunun sözleşme tarihini değişmiş sayıyordu ("N/A" → "Yok"); yeni kodda dil başına ayrı dosya, geçişte değişiklik yok (§8.190). Üstte kalan eski `diff-*.json` / `snapshot.json` dosyaları önceki sürümlerin artığı, okunmuyor.

### E. Ekran / tema / çözünürlük

- [ ] Öğe adları (`MessageBodyText`, `BindingRoot_SquadOverviewPlayerBlock`, `NavTabDropdown`, `#low-button`…) ve
  biçim sınıfı (prim kartında seçili düğme "-active-"): skin değişince kırılır; skin ile denenmedi.
- [ ] `up=N` (yazıdan N basamak yukarı) — 76 yer; skin/güncelleme ile kırılabilir.
- [~] Match Day › Overview açılınca ekran görüntüsü siyah (oyunun kendi davranışı; okuma etkilenmiyor).

### F. Oyun sürümü

- [ ] Sayfa GUID'leri — 36 sabit (`Screen.cs`); güncellemede değişir mi ölçülecek.
- [ ] Eklentide oyunun özel alanları (`m_choices`, `m_key`, `m_selectionIndex`… ~25 ad): güncellemede değişebilir.
- [ ] Olay adları (`OpenSubtabEvent`, `SpeakToAction`…), `languages.fmf` yolu ve biçimi.

### G. Platform / mağaza (en sonda)

- [ ] Kurulum yeri: `Launcher.cs` 31–37 (`~/fm26_bep/fm.app`, Steam yolu; `FM_HOME` ile geçilebiliyor). Epic / Game Pass / Windows yok.
- [ ] macOS araçları: `osascript` (190), `/usr/bin/pgrep` (230), `/usr/bin/pkill` (238), `PlistBuddy`, `.app`.
- [x] Kayıt/taktik klasörü oyuna soruluyor (`/folders`, `GameFolders.cs`); platform varsayılanı macOS'a göre (`GameFolders` 31).
- [ ] BepInEx arm64 yamalı paket; Windows x64 resmi paketle denenmedi.
- [~] `~/.config/fm-cli/` (UserProfile altında) her platformda geçerli bir yer.

### H. Kurulum / yayın (hedef: macOS + Steam, başkaları kolayca kurup oynasın)

15 Ekim'de eklendi. Faruk'un "live" tanımı: macOS + Steam'de başkasının kolayca kurup oynayabildiği hal. Bugün yalnızca
geliştiricinin makinesinde çalışıyor. Bu hedef için D (başka lig / sıfırdan kayıt) şart; E (skin) ve G'nin Windows / Epic
kısmı sonraya kalabilir. Tahmin (15 Ekim): A+B+C 5–7 gün, D 1–2, H 4–6, polish 3–5 → ~3–4 hafta.

- [ ] **BepInEx lisansı:** DadMych/fm26-player-export-macos paketini yeniden dağıtmaya izin var mı; yoksa kullanıcı ayrıca
  indirir, kurulum ona göre.
- [ ] **Oyun kopyası:** `~/fm26_bep/fm.app` nasıl kuruluyor (Steam'deki kopyadan, Info.plist yaması), adım adım yazılmalı ve
  betiğe girmeli; Steam oyunu güncelleyince kopya ne oluyor.
- [ ] **Tek komutla kurulum:** betik ya da `brew` paketi: oyun yolunu bul, BepInEx'i kur, oyun kopyasını hazırla, eklentiyi
  koy, `fm`'i PATH'e koy, MCP ayarını yaz. Tekrar çalıştırılabilir olsun (güncelleme = aynı komut).
- [ ] **SDK'sız `fm`:** hazır derlenmiş, kendi kendine yeten `dotnet publish` (osx-arm64); eklenti DLL'i de hazır gelir.
- [ ] **`fm doctor` kurulumu denetlesin:** her adımı (oyun, BepInEx, eklenti, köprü, kayıt) tek tek söylesin, ne yapılacağını
  yazsın.
- [ ] **Sürüm koruması (F'nin bir kısmı):** eklenti oyun sürümünü bilsin; desteklenmeyen sürümde sessizce yanlış yapmak
  yerine "bu FM sürümü desteklenmiyor" desin.
- [ ] **Temiz makinede deneme:** yeni bir macOS kullanıcı hesabında README'yi izleyerek sıfırdan kurulum; çıkan her
  takılma düzeltilir.
- [ ] **Intel Mac:** ilk sürümde "yalnızca Apple Silicon" mu denecek, karar.
- [ ] **README / kılavuz:** kurulum, ilk açılış, sık sorunlar; geliştirici bilgileri ayrı yere.

---

## Arka plan (13 Ekim 2026 kod taraması; durumu 14 Ekim)

**Neyle tutunuyoruz, sağlamdan kırılgana:** veri yolu + sayı/enum alanı · olay adı / enum · oyunun kimlik numarası ·
panel Guid (dilde ve skin'de sağlam) → veri yolu + `…String` alanı (değer çevrilir) → kontrol / öğe adı, `up=N`, sıra
numarasıyla okuma (skin / güncellemede kırılabilir) → ekran yazısı, cümle ayrıştırma (dilde kırılır).

**Karar (Faruk, 13 Ekim):** dil → oyunun çeviri tablosu (seçenek B; A = her yazıyı kimliğe taşımak, C = oyunu İngilizce
zorunlu tutmak reddedildi); sıra numarasıyla okuma kötü uygulama, yerine yazının veri yolu (§8.169) ya da ekranı hiç
okumamak (`/pull`); gösterim oyunun dilinde (14 Ekim).

**Çözülenler (✅, FINDINGS'te):** sayfa açma Guid'le (`Screens.cs`, §8.162); onay pencereleri yuvayla (§8.165); çeviri
katmanı + ters tablo (§8.167–8.168); yazının veri yolu, takvim / antrenman / mutluluk / atıcılar / penaltı / teklif /
rakip raporu yollarla (§8.169); motor yazıları (§8.171); para biçimi `/money` (`MoneyFormat`, §8.172); CLI'ın Türkçe
sözlükleri kalktı, gösterim `GameWords` (§8.173–8.174); mesaj gövdesi/ekleri kimlikle (§8.176); lig `Human.Team.
Division.Name` (eski iki ad tahmini `League.cs:66`, `Reads.cs:1057` kalktı); altyapı takımları `Club.Teams` (§8.164);
önbellekler `~/.config/fm-cli/cache/<dil>/<menajer - kulüp>/` (`CacheFile`); kayıt kuralları notu
`squadreg.ExtraSquadRules`; kayıt/taktik klasörü oyuna soruluyor (`/folders`, `GameFolders.cs`, `FM_USER_DIR`).

**Hâlâ açık (taramadan, CHECKLIST'e girmeyen ayrıntılar):**
- Sayfa düğmeleri (Next, Back, Apply, sekmeler…) yazıyla; başlıkla tanınan 5 pencereden **ikisi** (`Replace Existing
  File`, `Delete File`) artık `Type` ile bulunuyor (CHECKLIST B, §8.237), kalan üçü (Instant Result, Team Selection,
  Injured Players) hâlâ başlıkla — onlar da `Type` ile değiştirilecek.
- Cümle kalıpları: `^\d+ year-old` (`Happiness.cs`, `Dynamics.cs`, `Offers.cs`, `Youth.cs`), `Registration.cs:157-162`
  ("Players of the age of…", "You have N spaces…"), `MatchControls.cs:289-293` ("Add team instruction:", "Tell X:"),
  `OfferScreen.cs:408-412` ("Bonus upon reaching the…"), `Intensity.cs:142`, `TrainingSchedule.cs:406`, `Offers.cs:147`,
  `ScoutFocus.cs:316`, "This round will be drawn on"; durum sözcükleri ("Offer made", "No Tactic Set", "No Data
  Available"…).
- `…String` alanları (`PositionCombinedString`, `MoraleString`, `JobString`, `TransferValueRangeString`,
  `FullContract.EndDateDescription`…) değer olarak çevrilir; mevki artık `PositionCombined` maskesinden.
- Ayarlar: tarih biçimi `d/M/yyyy` varsayımı (`Contracts.cs:90`, `Doctor.cs:39`, `Matches.cs:144`; ABD biçiminde yanlış
  okur), maaş dönemi "p/w" (`Club.cs:159`; Finances artık veriden), ondalık ayırıcı `InvariantCulture`, boy/mesafe birimi
  (`Compare.cs:54`).
- Ekran yapısı: 119 yerde kontrol adı (`SecondaryTabDropdownButton` 9, `layout-tile-interaction-button` 10…; en yoğun
  `Guided.cs`, `Program.cs`, `Reads.cs`, `Scouting.cs`), 76 yerde `up=N` (N 0…18), sanal tablolarda çizilen satır sayısı
  pencere boyutuna / ölçeğe bağlı olabilir.
- Kariyere özel: rakip altyapısı `( U\d\ds| Yth Cands)$` (`Calendar.cs:89`), "Senior Squad"/"U19 Squad"
  (`OfferScreen.cs:362-363`), personel görev listesi sabit (`Staff.cs:20-23`), turnuva ağacı "Men's"/"Under" klasör
  adları (`Stages.cs:431`, `Reads.cs:892`), kılavuz çekimi Fenerbahçe oyuncularıyla (`tools/manual/shots.sh:22-28`,
  `drive.py:31`).
- Gömülü sabitler: ay/gün adlarının birkaç kopyası (`Calendar.cs:20`, `Clock.cs:9`, `Program.cs:1190`, `Reads.cs:854`),
  oyunun yazım hatasına yama (`Guided.cs:1038` "Atacking"), sabit seçenek listeleri (ayarlar `Settings.cs:27-84`, 36
  özellik adı `Compare.cs:43-46` + `Reads.cs:380-383`, mevkiler `StaffSearch.cs:174-178`, duran top türleri `Takers.cs`,
  teklif şartları `OfferScreen.cs`), eklentide `Offer.cs:65-74` ve `:131-140` etiketleri, `Opposition.cs:15` sütun adları.
- Platform (§G, en sonda): `Launcher.cs` Steam yolu + `~/fm26_bep/fm.app` + `run_bepinex_arm64.sh` (`FM_HOME`/`FM_APP`);
  macOS araçları `osascript`, `PlistBuddy`, `pgrep`/`pkill`, `.app`; arm64 yamalı BepInEx (Windows x64 resmi paket
  denenmedi); interop DLL'leri oyun sürümüne bağlı. Yapılacak: yollar ve başlatma platforma göre tek yerde.

**Ölçüm planı (kalanlar):** para birimi Euro + maaş aylık + tarih ABD biçimiyle `club`, `finances`, `contracts`,
`offers`; başka skin / arayüz ölçeğiyle `tools/sweep.sh`; Almanya kariyeri (önce önbellek temizliği, sonra sweep).
