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
  ~~Happiness 148 195~~ · ~~IndividualTraining 40 50~~ · ~~Intensity 43 54 106~~ · Jobs 231 · Launcher 213 269 · League 243 ·
  MatchDay 181 314 368 · MatchReport 35 · ~~Matches 82~~ · ~~Mentoring 45 188 205 217 246 285 297 331~~ · ~~Numbers 158 177~~ ·
  OfferScreen 259 · Offers 99 · OppositionCard 55 91 · Program 589 693 2112 2153 2232 · Propose 66 80 · Reads 114 131 ·
  Registration 32 115 301 305 · Release ~~57~~ 123 · Responsibilities 283 388 · ScoutFocus 107 237 349 537 576 724 ·
  Scouting 100 · Settings 226 · Staff 74 · StaffSearch 91 100 110 407 · Stages 62 97 116 132 209 · StatusFlags 54 ·
  TacticFiles 181 · ~~Takers 206~~ · TrainingReport 82 · TrainingSchedule 345 · ~~TraitTraining 32~~ · Transfers 96 128 131 139
  - [x] Bridge 103 109 127 147: dördü döngüde yoklama aralığı (250 ms), değişiklik gerekmedi; değer 2. okumada, ~5 kat
    pay. "Yol boş" ile "henüz gelmedi" aynı cevap (`{}`) — FINDINGS §8.179.
  - [x] `table`: olmayan `CurrentComp5..8` artık beklenmiyor (`ClubCompetitions`, tek okuma); 8,7 → 2,5–2,9 sn, üç
    durumda ve dört turnuva adıyla aynı sonuç (§8.179).
  - [x] Clock 148 157 (`next`'in `Settled`): 16 ilerlemede ölçüldü, değişiklik gerekmedi. Basıştan sonra ilk değişiklik
    0,34–1,24 sn (sınır 4 sn, ≥3 kat pay); iş bitince durum 0,1–0,53 sn titriyor (CanContinue→NewsAggregator/NeedAction/
    Fixtures→…), "1,4 sn sakin" bunu örtüyor (2,6 kat). Ayrı bir "bitti" işareti yok: `GameManagersNotContinuedCount`
    `Processing`'i birebir izliyor; bir adımda `Processing` hiç görünmedi, yalnız saat değişti (§8.181).
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
- [ ] **Kısa `Until` (≤ 3 sn) — 16 yer;** "gelmezse yok say" anlamına gelenler tehlikeli: Actions 126 · Budget 70 ·
  Calendar 157 · ContractTalks 364 · ~~Finances 238~~ · League 216 · MatchControls 131 266 · Prep 124 · Program 986 1918 2118 ·
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
- [ ] **Alt sayfa açmada sabit 1,5 sn** (`OpenSubpage`, Program ~2112). Denenen çözüm (çubuğun seçili satırı) ölçülünce
  yetersiz çıktı: ana sayfa zaten açıkken çubuk eski seçimde kalıyor → geri alındı. Eklentide `open?guid=&check=1`
  (yalnızca soru, hiçbir şey açmıyor) duruyor (14 Ekim commit edildi, CLI henüz kullanmıyor). Önce "ana sayfanın açılması bitti" işareti ölçülecek. Ayrıca `fm open X` açılış bitmeden dönüyor: hemen ardından
  açılan sayfanın üstüne gelebiliyor (Mesajlar › Mutluluk'ta 1/10, §8.185).
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
    "injuries this season" · home-grown "years old" · Coach Report / Player Report " years old" Replace
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
  - [ ] Stages "This round will be drawn" · Saves "Saves automatically" / "Autosave" · Penalties "Starting at " / "On Subs bench" ·
    Release "Is there anything else" · Matches "Match Ratings"+"Inf" · Saves "Estimated game speed:"
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
- [ ] Veri merkezi bulgularından ikisi İngilizce çıktı ("We're showing a good touches-to-shots ratio…"): oyun da mı
  İngilizce gösteriyor, yoksa eş öğrenilmiyor mu — ekranla bakılacak.
- [ ] `contract <oyuncu> --agent`: TR'de kimi koşuda satır bulunamıyor (aynı oyuncu bir koşuda rc=0, başka koşuda
  `seçenek yok`; aynı oyuncular EN'de hep bulunuyor). Ekranda kalan bir sözleşme görüşmesi kartı (`Finalise
  Promises` / `Cancel Negotiations`, `Action` alanı boş) görüldü — kartın kendi işareti ölçülecek (§8.210 sonu).
- [ ] **`opposition` seçenek adları:** seçenekler eklentiden İngilizce geliyor, kullanıcı da onları yazmak zorunda
  (`Pres=Always` çalışır, `Pres=Her Zaman` çalışmaz), ama hata mesajı ve yardım satırı seçenekleri oyunun sözleriyle
  yazıyor ("Seçenekler: - · Hiçbir Zaman · Her Zaman", "opposition <oyuncu> Pres=Always Sert giriş=Hard") — yani
  gösterilen ad geri yazılamıyor (§8.207). Çevirisi bozuk olanlar da var: TR oyunda sıkı giriş `Normal · Minor · Gusty`,
  İngilizcede `Normal · Easy · Hard` (çeviri tablosu "Easy"→"Minor", "Hard"→"Gusty" veriyor). Bakılacak: seçenek
  eşleşmesi gösterilen sözü de kabul etsin mi (ters sözlük `GameWords`te yok), yoksa yardım/mesaj İngilizce adı mı yazsın.
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
- Sayfa düğmeleri (Next, Back, Apply, sekmeler…) yazıyla; başlıkla tanınan 5 pencere (Instant Result, Team Selection,
  Injured Players, Replace Existing File, Delete File) `Type` ile değiştirilecek.
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
