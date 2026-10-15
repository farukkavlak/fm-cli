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
  MatchDay 181 314 368 · MatchReport 35 · Matches 82 · ~~Mentoring 45 188 205 217 246 285 297 331~~ · Numbers 158 177 ·
  OfferScreen 259 · Offers 99 · OppositionCard 55 91 · Program 589 693 2112 2153 2232 · Propose 66 80 · Reads 114 131 ·
  Registration 32 115 301 305 · Release 57 123 · Responsibilities 283 388 · ScoutFocus 107 237 349 537 576 724 ·
  Scouting 100 · Settings 226 · Staff 74 · StaffSearch 91 100 110 407 · Stages 62 97 116 132 209 · StatusFlags 54 ·
  TacticFiles 181 · Takers 206 · TrainingReport 82 · TrainingSchedule 345 · TraitTraining 32 · Transfers 96 128 131 139
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
  - [ ] "Acknowledge and Move On" yazısıyla basan 8 yer (BoardRequests 99 101, Captains 61, Propose 44, Release 47 80 156,
    TraitTraining 45 110, TrainingReport 40 86): Türkçede de bu İngilizce geliyor ama yazı; kimliği `Event` 31
    (`EventFmxClearChanges`, eklenti `/buttons?gen=` `Action`), mentorlukta ölçüldü (§8.188).
  - [ ] Guided 161 174 (bedel sözü penceresi: onaydan sonra 1 sn, açılmasını 800 ms bekleme): bedel isteyen bir söz
    konuşması lazım; o an oyunda yok, ölçülmeden değiştirilmedi. Not: etkileşimli `talk` her cevaptan sonra 800 ms bekliyor.
  - [x] Coaches 53 88: atamada "kutu istenen halde ve yıldız yalnız atandığı yerde" (≤5 sn), `--auto`'da sayfa değişimi
    (değişmezse 3 sn; eskiden sabit 1,5); 20 atama + 2 `--auto` üç durumda doğru (§8.180). İngilizcede denenmedi.
  - [x] `coaches` 3/10: okuma yalnız tam ızgarayı ("ad, görev, dokuz kutu+yıldız", iş yüküne kadar) kabul ediyor; 12
    açılışta 17 ara hal reddedildi, 12/12 tam (§8.180). Kural yapıdan, dilden bağımsız.
- [ ] **Kısa `Until` (≤ 3 sn) — 16 yer;** "gelmezse yok say" anlamına gelenler tehlikeli: Actions 126 · Budget 70 ·
  Calendar 157 · ContractTalks 364 · ~~Finances 238~~ · League 216 · MatchControls 131 266 · Prep 124 · Program 986 1918 2118 ·
  Reads 892 · Saves 207 220 · ScoutFocus 55 · StaffSearch 354 · Stages 337 · TacticFiles 29 · TacticSlots 112 ·
  TrainingSchedule 491 506 · TraitTraining 89
- [x] **`reply` cevapları:** başlıklar = düğmeler ve önceki mesajınkinden farklı (cevapsızda `CanRespond`); Türkçe 24/24,
  İngilizce 14/14, başka sayfa açıkken 4/4 (§8.178).
- [ ] **`reply` ara boş an riski:** yeni kural "başlıklar = düğmeler ve öncekinden farklı"; cevaplı bir mesajdan cevaplı
  bir mesaja geçerken eski düğmeler silinip yenileri gelmeden boş bir an varsa, "boş = boş, öncekinden farklı" o an tutar
  ve cevaplar "yok" sayılır. Geçiş sırasında sık yoklayıp (her okuma) boş ara durum olup olmadığını ölç; varsa boş
  sonucu yalnızca mesaj gerçekten cevapsızsa kabul et (Program.cs ~800).
- [ ] **Mesaj eki için 1,5 sn** (`WriteAttachment`, Program ~986): ek gelmezse "ek yok". Ekin var olup olmadığını söyleyen
  işaret bulunamadı (bağlama adları sürümde gizli); eksiz bir mesajda `#AttachmentsTitleContainer` durumu ölçülecek.
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
  - [ ] Jobs "Managerial Position" ·
    Stages "This round will be drawn" · Saves "Saves automatically" / "Autosave" · Penalties "Starting at " / "On Subs bench" ·
    Release "Is there anything else" · Matches "Match Ratings"+"Inf" · Saves "Estimated game speed:"
  - [~] ContractTalks "final decision" / "be in touch": İngilizce dışında tahmin etmiyor, dürüstçe "ya imzalandı ya da
    birkaç gün içinde" diyor.
- [ ] **İngilizce yazıyla basma/tıklama — 44 yer** (`press/click text="Acknowledge and Move On"`, "Cancel", "Confirm",
  "Save Game As"…): eklentinin çeviri tablosundan geçiyor; tabloda 2.376 çakışma var ("Confirm" → Onayla/Kabul). Her biri
  öğe adı / olay adıyla basılabilir mi bakılacak.
- [ ] Veri merkezi bulgularından ikisi İngilizce çıktı ("We're showing a good touches-to-shots ratio…"): oyun da mı
  İngilizce gösteriyor, yoksa eş öğrenilmiyor mu — ekranla bakılacak.
- [ ] **Mutluluk seviyesi:** "Biraz Mutlu" / "Biraz Mutsuz" çevrilemiyor (tabloda yok) → `happiness` sıralaması ve rengi
  bu iki seviyede yanlış; seviyenin veri yolu yok, `HierarchyOverallHappinessLevel` sırayı tutuyor ama eşikleri bilinmiyor
  (§8.185). Moral yazısı ("Extremely Good") İngilizce gösteriliyor.
- [ ] Haberde "Portugal" çevrilmedi (news yolu eşi öğrenmiyor); "Defence (Core)" birim adı İngilizce kaldı.
- [ ] İngilizce oyunda bugünkü değişiklikler (yeni ekler, yüzde, sub, reply) denenmedi.

### C. Oyunun ayarları (dil dışında)

- [x] Para birimi ve kısaltmalar oyunun biçimlendiricisinden (`/money`), ondalık işareti oyundan.
- [ ] **Maaş dönemi:** Türkçe kayıtta maaşlar "aylık" gösteriliyor (ayar), CLI yardımı `--wage` için "haftalık" diyor;
  teklif/sözleşmede girilen tutarın hangi dönem sayıldığı denenecek.
- [ ] Boy/kilo birimleri, tarih biçimi (gün/ay sırası) — oyundan öğrenilmiyor, ölçülecek.

### D. Lig / takım / kayıt

- [x] Kodda lig, takım, kulüp ya da kayıt adı yok (tarama: yalnızca para biçiminin öğrenilene kadarki varsayılanı "£").
- [ ] Kariyer başka ülkede (Almanya, kupa adları, kayıt kuralları, Avrupa kupası) ve sıfırdan başlayan bir kayıt hiç
  denenmedi — yayın hedefi için şart (H).
- [ ] `~/.config/fm-cli/` önbellekleri hangi kayda/dile ait olduğunu biliyor mu (rol adları dile göre ayrıldı; diğerleri?).

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
