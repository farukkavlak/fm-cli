# Bağımlılık dökümü: oyun dili, ekran / tema, kariyer, gömülü sabitler

13 Ekim 2026, kod taraması (oyun kapalıyken; hiçbir şey oyunda denenmedi). Amaç: fm-cli başka dilde, başka temada,
başka kulüpte / ligde açılınca nerede kırılır, listesini çıkarmak. Çözüm seçenekleri en sonda; karar birlikte
verilecek (PLAN §14 "Taşınabilirlik").

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
  Bridge 103 109 127 147 · Clock 148 157 179 · Coaches 53 88 · Contracts 57 · Finances 247 · Guided 161 174 1035 ·
  Happiness 148 195 · IndividualTraining 40 50 · Intensity 43 54 106 · Jobs 231 · Launcher 213 269 · League 243 ·
  MatchDay 181 314 368 · MatchReport 35 · Matches 82 · Mentoring 45 188 205 217 246 285 297 331 · Numbers 158 177 ·
  OfferScreen 259 · Offers 99 · OppositionCard 55 91 · Program 589 693 2112 2153 2232 · Propose 66 80 · Reads 114 131 ·
  Registration 32 115 301 305 · Release 57 123 · Responsibilities 283 388 · ScoutFocus 107 237 349 537 576 724 ·
  Scouting 100 · Settings 226 · Staff 74 · StaffSearch 91 100 110 407 · Stages 62 97 116 132 209 · StatusFlags 54 ·
  TacticFiles 181 · Takers 206 · TrainingReport 82 · TrainingSchedule 345 · TraitTraining 32 · Transfers 96 128 131 139
  - [x] Bridge 103 109 127 147 (`Pull`, `Numbers`, `Strings`): dördü de döngüde yoklama aralığı (250 ms), "bekle sonra
    yap" yok; değişiklik gerekmedi. Ölçüm (Türkçe, status/board/club/squad/table/health, squad 3 kez): bağlanan değer
    her yerde 2. okumada (~30 ms sonra, bir kare) geliyor; üst sınır `Pull` 6 okuma ≈ 1,5 sn, `Numbers`/`Strings` 4 ≈ 1 sn
    → yaklaşık 5 kat pay. Eklentinin cevabında "yol boş" ile "henüz gelmedi" aynı (`{}`), olumlu bir "yok" işareti yok.
  - [x] `table`: olmayan `Human.Team.CurrentComp5..8` her biri tam üst sınırı bekliyordu (~6,3 sn). Artık `league`'in
    `ClubCompetitions`'ı: sekiz ad tek okumada `Human.Team`'den, iki tur aynı gelince bitiyor (3 tur); eklenti değişmedi.
    Türkçe: `table` 8,7 → 2,5–2,9 sn (ana sayfa / başka sayfa açık / art arda), `table Carabao` / `Sky Bet` / `Kupa` /
    `xyzzy` aynı sonuç. Yeni bağlanan ad 3. okumada geldi; boş turlar "aynı" sayılmıyor.
  - [ ] Clock 148 157 179 (`next`'in `Settled`, `Unemployed`): 148/157 yoklama aralığı ama "4 sn içinde değişmezse sakin
    say" ve "1,4 sn boşta = bitti" süreye dayanıyor; ölçmek için zaman ilerlemeli. 14 Ekim akşam ertelendi: kaptan seçimi
    mesajı cevap bekliyor (Faruk'un kararı), `next` 0,3 sn'de NeedAction'a düşüyor. 179 için işsiz bir kayıt lazım.
  - [x] Coaches 53 88 (`coaches --auto`, atama): ikisi de "bekle sonra oku"ydu. Ölçüm (Türkçe, 50 ms yoklama): atamada
    kutu ~0,1 sn'de dönüyor ama yıldız/iş yükü eski, sayfa 0,3–0,55 sn'de yeniden kuruluyor (arada form eksik); şimdi
    "kutu istenen halde ve yıldız yalnızca atandığı yerde" beklenir (üst sınır 5 sn). `--auto` sayfayı ~0,4–0,55 sn'de
    değiştiriyor; değişince biter, yardımcının seçimi zaten oradaysa hiçbir işaret yok, 3 sn sınırında biter (eskiden
    sabit 1,5). 20 atama (ana sayfa açık / Gelen kutusu / Kadro açıkken) + 2 `--auto`, hepsinde çıktı taze okumayla aynı.
    İngilizcede denenmedi (işaret kutu değeri ve yıldız sayısı, dilden bağımsız).
  - [x] `coaches` ilk okumada 10 antrenörden yalnızca 3'ünü gösterdi: sayfa parça parça kuruluyor (kutular yıldızsız,
    satırlar adsız geliyor) ve okuma ilk adsız satırda durup eksik tabloyu kabul ediyordu. Artık ızgara ancak "ad, görev,
    dokuz kutu+yıldız" diye iş yüküne kadar kesintisiz okununca kabul. 30 ms yoklamayla 12 açılış (gelen kutusu / kadro /
    kulüp / yönetim / finans / zaten açık): 17 ara hal (33–267 öğe, 0–6 adlı satır) reddedildi, 12/12 çıktı 10 antrenör;
    atama + geri alma ×2 değişmedi. Kural yapıdan (öğe türü), dilden bağımsız; İngilizcede denenmedi.
- [ ] **Kısa `Until` (≤ 3 sn) — 16 yer;** "gelmezse yok say" anlamına gelenler tehlikeli: Actions 126 · Budget 70 ·
  Calendar 157 · ContractTalks 364 · Finances 238 · League 216 · MatchControls 131 266 · Prep 124 · Program 986 1918 2118 ·
  Reads 892 · Saves 207 220 · ScoutFocus 55 · StaffSearch 354 · Stages 337 · TacticFiles 29 · TacticSlots 112 ·
  TrainingSchedule 491 506 · TraitTraining 89
- [x] **`reply` cevapları:** sabit "1 sn arayla iki okuma" yerine sayfanın cevap başlıkları ile düğmeler aynı ve önceki
  mesajınkilerden farklı olunca (cevapsızda `CanRespond`); ilk ölçülen "başlık sayısı kadar" hali eski cevapları
  gösteriyordu. Türkçe 24/24, İngilizce 14/14, başka sayfa açıkken 4/4, 1–2 sn (FINDINGS §8.178).
- [ ] **`reply` ara boş an riski:** yeni kural "başlıklar = düğmeler ve öncekinden farklı"; cevaplı bir mesajdan cevaplı
  bir mesaja geçerken eski düğmeler silinip yenileri gelmeden boş bir an varsa, "boş = boş, öncekinden farklı" o an tutar
  ve cevaplar "yok" sayılır. Geçiş sırasında sık yoklayıp (her okuma) boş ara durum olup olmadığını ölç; varsa boş
  sonucu yalnızca mesaj gerçekten cevapsızsa kabul et (Program.cs ~800).
- [ ] **Mesaj eki için 1,5 sn** (`WriteAttachment`, Program ~986): ek gelmezse "ek yok". Ekin var olup olmadığını söyleyen
  işaret bulunamadı (bağlama adları sürümde gizli); eksiz bir mesajda `#AttachmentsTitleContainer` durumu ölçülecek.
- [ ] **Alt sayfa açmada sabit 1,5 sn** (`OpenSubpage`, Program ~2112). Denenen çözüm (çubuğun seçili satırı) ölçülünce
  yetersiz çıktı: ana sayfa zaten açıkken çubuk eski seçimde kalıyor → geri alındı. Eklentide `open?guid=&check=1`
  (yalnızca soru, hiçbir şey açmıyor) duruyor, commit edilmedi. Önce "ana sayfanın açılması bitti" işareti ölçülecek.
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
  - [ ] Intensity "Ask … training intensities" · Mentoring "assign mentoring groups" · Jobs "Managerial Position" ·
    Stages "This round will be drawn" · Saves "Saves automatically" / "Autosave" · Penalties "Starting at " / "On Subs bench" ·
    Release "Is there anything else" · Matches "Match Ratings"+"Inf" · Saves "Estimated game speed:"
  - [~] ContractTalks "final decision" / "be in touch": İngilizce dışında tahmin etmiyor, dürüstçe "ya imzalandı ya da
    birkaç gün içinde" diyor.
- [ ] **İngilizce yazıyla basma/tıklama — 44 yer** (`press/click text="Acknowledge and Move On"`, "Cancel", "Confirm",
  "Save Game As"…): eklentinin çeviri tablosundan geçiyor; tabloda 2.376 çakışma var ("Confirm" → Onayla/Kabul). Her biri
  öğe adı / olay adıyla basılabilir mi bakılacak.
- [ ] Veri merkezi bulgularından ikisi İngilizce çıktı ("We're showing a good touches-to-shots ratio…"): oyun da mı
  İngilizce gösteriyor, yoksa eş öğrenilmiyor mu — ekranla bakılacak.
- [ ] Haberde "Portugal" çevrilmedi (news yolu eşi öğrenmiyor); "Defence (Core)" birim adı İngilizce kaldı.
- [ ] İngilizce oyunda bugünkü değişiklikler (yeni ekler, yüzde, sub, reply) denenmedi.

### C. Oyunun ayarları (dil dışında)

- [x] Para birimi ve kısaltmalar oyunun biçimlendiricisinden (`/money`), ondalık işareti oyundan.
- [ ] **Maaş dönemi:** Türkçe kayıtta maaşlar "aylık" gösteriliyor (ayar), CLI yardımı `--wage` için "haftalık" diyor;
  teklif/sözleşmede girilen tutarın hangi dönem sayıldığı denenecek.
- [ ] Boy/kilo birimleri, tarih biçimi (gün/ay sırası) — oyundan öğrenilmiyor, ölçülecek.

### D. Lig / takım / kayıt

- [x] Kodda lig, takım, kulüp ya da kayıt adı yok (tarama: yalnızca para biçiminin öğrenilene kadarki varsayılanı "£").
- [ ] Kariyer başka ülkede (Almanya, kupa adları, kayıt kuralları, Avrupa kupası) hiç denenmedi — Faruk'un kararı bekliyor.
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

---

## 0. Kısaca

- fm-cli bugün **oyunun İngilizce olduğunu varsayıyor**. CLI'ın 87 kaynak dosyasının 73'ünde oyunun ekranında İngilizce yazı
  arayan ya da İngilizce yazıyı ayrıştıran kod var; kaba sayımla ~1.400 sabit İngilizce metin. Oyun Türkçe ya da
  Almanca olursa komutların çoğu "bulunamadı" der ya da boş döner.
- Bunun yanında **ekranın iç yapısına** bağlıyız: 119 yerde kontrol adı (`SecondaryTabDropdownButton`,
  `layout-tile-interaction-button`…), 76 yerde "bu yazıdan N basamak yukarı çık" (`up=N`), birçok yerde "başlıktan
  sonraki 2. yazı" gibi sıra numarasıyla okuma. Bunlar dilden bağımsız ama oyun güncellemesi / skin ile kırılabilir.
- **Kariyere özel** varsayımlar az ama sinsi: lig adı "League" içeriyor sanılıyor (Bundesliga içermiyor), altyapı
  takımı "U19" sanılıyor, para birimi "£" sanılıyor, `~/.config/fm-cli/` altındaki önbellekler hangi kayda / dile ait
  olduklarını bilmiyor.
- İyi haber: oyunun içinde dilden bağımsız kimlikler var ve bir kısmını zaten kullanıyoruz (veri yolları, olay adları,
  talimat kimlikleri). Sayfaların da dilden bağımsız kimliği (panel id / GUID) var; bugün adıyla açıyoruz.

---

## 1. Oyuna neyle tutunuyoruz — sağlamdan kırılgana

| Dayanak | Örnek | Dil değişince | Skin / güncelleme |
|---|---|---|---|
| Veri yolu + sayı / evet-hayır alanı | `Human.Team.Players` → `Age`, `IsInjured`, `Condition` | sağlam | sağlam (güncellemede ad değişebilir) |
| Olay adı, enum | `EventFmxDeleteMentoringGroup`, `ContinueFlowState.PressConference` | sağlam | sağlam |
| Oyunun kendi kimlik numarası | takım talimatı `Id`, seçenek `Id` (team-instructions.json) | sağlam | sağlam |
| Panel id / GUID | `/pages` her sayfa için `PanelID`, `Guid` veriyor | sağlam | sağlam |
| Veri yolu + **…String** alanı | `PositionCombinedString` ("D (C)"), `MoraleString`, `JobString` | **[olası] değer çevrilir** | sağlam |
| Kontrol / öğe adı | `ViewDropdown`, `continue-button`, `LeftSide` | sağlam | **kırılabilir** |
| Ağaçta yukarı çıkma | `texts?anchor=Attachments&up=8` | (çapa yazıysa kırılır) | **kırılabilir** |
| Sıra numarasıyla okuma | `rest[i + 2]`, "başlıktan sonraki ilk sayı" | (araya giren yazıya bağlı) | **kırılabilir** |
| Ekrandaki yazı (etiket, başlık, çapa) | `"Apply"`, `"Next Opposition Report"`, `"Acknowledge and Move On"` | **kırılır** | sağlam |
| Ekrandaki cümleyi ayrıştırma | `^\d+ year-old`, `^Bonus upon reaching the (.+)` | **kırılır** | sağlam |

---

## 2. Oyun diline bağımlılık

### 2.1 Sayfa açma — adla **[kesin]**

`OpenPage("Tactics", "Match Day")` gibi ~50 çağrı sayfayı ve sekmeyi İngilizce adıyla arıyor
(`cli/fm/Program.cs:1588`, eklentide `Pages.Open` → `SubtabText` ile karşılaştırma, `plugin/.../Pages.cs:49`).
Türkçe oyunda sayfa "Taktikler" diye geçer; komut `no page named 'Tactics'` der.

Kullanan komutlar (çoğu): tactic, prep, setpieces, takers, penalties, training, intensity, coaches, dynamics,
captains, mentoring, happiness, squad, registration, youth, numbers, club, board, vision, finances, facilities,
supporters, responsibilities, staff, budget, scout/focus, offers, targets, opponent, opposition, news, calendar,
stages, jobs, settings, saves.

Dilden bağımsız karşılık **var**: aynı katalog her sayfanın `PanelID` ve `Guid`'ini taşıyor, açma da zaten GUID ile
yapılıyor. Sadece arama adla.

**Durum (13 Ekim 2026): çözüldü.** Komutlar sayfaları `cli/fm/Screens.cs`'teki Guid'lerle açıyor (`OpenPage(Screen.Tactics)`);
eklenti sayfanın menü satırını seçiyor, alt menülü sayfada alt sekme düğmesini ilk satırının Guid'iyle buluyor. Aynı
ekranın takımları (First Team / U21 / U18) satırdaki takım kimliğiyle ayrılıyor; takım veriden
(`Club.HighestLevelYouthTeam`…). Elle yazılan `fm open <ad>` oyunun dilindeki adla çalışmaya devam ediyor (kullanıcının
girdisi). Ayrıntı: FINDINGS §8.162. Açık: Medical Centre hiçbir yolla açılmıyor (kullanan komut yok).

### 2.2 Buton, menü ve sekme etiketleri **[kesin]**

Komut ekrandaki kontrolleri listeliyor (`/buttons`, `/dropdowns`, `/dialog`, `/actions`), sonra **etiketine bakarak**
birini seçiyor. Örnekler:

- Onay pencereleri: `"Confirm"` (27 yer), `"Cancel"`, `"Yes"` / `"No"`, `"Ok"`, `"Close"`
- Mesaj cevapları: `"Acknowledge and Move On"` (13 yer), `"Response Required"`, `"Continue"`
- Sağ tık menüsü yolları: `"Contract>Terminate Contract"`, `"Transfer>Make Enquiry"`, `"Report>Assign Scout>"`,
  `"Copy Tactic>{ad}"`, `"Attributes>{grup}>{özellik}"`
- Sihirbaz / form düğmeleri: `"Next"`, `"Back"`, `"Apply"`, `"Finalise Deal"`, `"Suggest Terms"`, `"Quick Pick"`,
  `"Create New Tactic"`, `"Ask Assistant to Pick"`, `"Save Game As"`, `"Replace Existing File"`…
- Görünüm / sekme seçimi: `"General Info"`, `"Overview"`, `"Match Ratings"`, `"Last 5 Club Games"`…

**13 Ekim:** onay pencereleri artık yazıyla değil yuvayla cevaplanıyor (birincil = ilerlet, ikincil = vazgeç;
FINDINGS §8.165). Pencereyi tanımak için başlığa bakan 5 yer kaldı (Instant Result, Team Selection, Injured Players,
Replace Existing File, Delete File) — `Type` ile değiştirilecek. Sayfa düğmeleri (Next, Back, Apply, sekmeler…) duruyor.

Dilden bağımsız karşılık: her kontrolün **olay listesi** var (`Events`, ör. `EventFmxDeleteMentoringGroup`) ve çoğunun
**adı** var. Hepsinde tek başına ayırt edici mi, **[ölçülecek]** (aynı adlı `SIButton` çok).

### 2.3 Çapa metinleri: "şu yazıyı bul, oradan yukarı çık" **[kesin]**

`/texts?anchor=<yazı>&up=N` ve `/form?anchor=…` ile 56 farklı çapa: `"Total Apps"`, `"Attachments"`,
`"Transfer Budget"`, `"Monday"`, `"Thur"`, `"Mentoring Group Name"`, `"3/3 Selected"`, `"Prom."`,
`"Elite Prospects (…)"`, `"Manage settings for this game"`… Çapa yazısı çevrilirse sayfa hiç bulunmuyor.

### 2.4 Ekrandaki yazıyı okuyup anlamlandırma **[kesin]**

Sayfa bulunsa bile içi İngilizce sözcüklerle bölümlere ayrılıyor:

- Başlık → değer: `After("Wages", 2)`, `texts.IndexOf("Fee")` … (`Finances.cs`, `Club.cs`, `ClubCards.cs`,
  `Opponent.cs`, `ScoutReport.cs`, `Youth.cs`)
- Mesaj ekleri: `Program.cs:900-1420` arası, ekin türünü İngilizce başlıktan tanıyor ("Season Summary",
  "Injury Table", "Coach Report", "Likely Talking Points", "Youth Candidates"…) — ~90 sıra numaralı okuma.
  **14 Ekim gece (FINDINGS §8.176):** mesaj öğe adıyla (`MessageBodyText`) bulunup başlığıyla tanınıyor; rakip raporu,
  antrenman programı/özeti, kondisyon testleri, sorumluluklar, tavsiye özeti, teklif, imza kartı veri yolundan. Başlık
  çapaları eklentinin çevirisiyle Türkçede de tutuyor. Hâlâ sırayla: sezon özeti, sakatlık özeti, kiralıklar,
  altyapı adayları, milli takımlar, maç kartı, home-grown, scout odakları, bütçe, vizyon, basın toplantısı.
- Cümle kalıpları (regex):
  - `^\d+ year-old` — mutluluk, dinamikler, teklifler, altyapı listeleri (`Happiness.cs:153`, `Dynamics.cs:55`,
    `Offers.cs:181`, `Youth.cs:61`, `Program.cs:1088`)
  - `^Players of the age of (\d+) or younger on (\S+)…`, `^You have (\d+) spaces? available…` (`Registration.cs:157-162`)
  - `^Add team instruction: (.+)$`, `^Tell (.+?): (.+)$` (`MatchControls.cs:289-293`)
  - `^Bonus upon reaching the (.+) of the (.+)$`, `^Qualifying for the (.+)$` (`OfferScreen.cs:408-412`)
  - `^(\d+) (Days?|Weeks?)$` (`Intensity.cs:142`), `^(Slightly |Greatly )?(Increased|Reduced)$` (`TrainingSchedule.cs:406`)
  - `^\d+ \w+ Offers \(` (`Offers.cs:147`), `^Assignments \((\d+)\)$` (`ScoutFocus.cs:316`), `This round will be drawn on`
- Durum sözcükleri: `"Offer made"`, `"Contract Expired"`, `"Injured"`, `"Inj"`, `"Wnt"`, `"Playing"`, `"Selected"`,
  `"No Tactic Set"`, `"No Dugout Events"`, `"No Data Available"`, `"None Selected"`

### 2.5 Oyunun sözcüğü → bizim Türkçemiz: çeviri tabloları **[kesin]**

Oyundan gelen İngilizce değeri Türkçe göstermek için dosya dosya dağılmış sözlükler var. Oyun başka dildeyse anahtar
tutmuyor; ya ham metin görünüyor ya da (anahtar bir karara bağlıysa) yanlış dal çalışıyor.

| Dosya | Ne çeviriyor |
|---|---|
| `OfferScreen.cs:269-383` | teklif / sözleşme şartları, vaatler, kilit durumları (~110 satır) |
| `Club.cs:22-28`, `ClubCards.cs:14-20` | yönetim beklentisi (Required/Desired…), memnuniyet, itibar, tesis kalitesi |
| `Happiness.cs:15-20` | mutluluk seviyeleri (Very Unhappy … Delighted) |
| `Finances.cs:15-18` | oyuncu tipleri (Star Player, Squad Player…) |
| `Jobs.cs:246-251`, `Coaches.cs:15-28`, `Intensity.cs:17-18` | itibar, mali durum, antrenör alanları, yoğunluk |
| `Dynamics.cs:13-23`, `Staff.cs:20-23`, `StaffSearch.cs:174-178` | hiyerarşi, sosyal gruplar, personel görevleri, mevkiler |
| `Settings.cs:27-84` | oyun ayarlarının adları ve seçenekleri |
| `Takers.cs:16-31`, `TrainingSchedule.cs:424-429`, `Scouting.cs:128` | duran top türleri, antrenman etkileri, scout durumu |

Bu tablolar aynı zamanda **uygulamanın kendi yerelleştirmesinin** de parçası (bkz. §6): bugün "oyunun İngilizcesi →
bizim Türkçemiz" tek adımda yapılıyor.

### 2.6 Veri yolundan okunan ama çevrilmiş olabilecek değerler **[olası]**

Veri yolları (`Human.Team.Players`…) dilden bağımsız; ama adı `…String` ile biten alanlar oyunun **gösterdiği metni**
veriyor. Dil değişince değer değişir, kod ise İngilizcesini bekliyor:

- `PositionCombinedString` ("D (C)", "AM (RL), ST (C)") → `depth`, `targets`, `doctor`, `squad`, `query` mevkiyi
  bu metinden ayrıştırıyor (`Depth.cs:53`, `Targets.cs:73`). Türkçe oyunda mevki kısaltmaları farklı.
- `MoraleString`, `ConditionString`, `JobString`, `RightFootednessString`, `InternationalCapsAndGoalsString`,
  `TransferValueRangeString`, `FullContract.EndDateDescription`, `Board…String` / `Supporter…String`.
- `CurrentInjury.InjuryDescription`, kulüp / turnuva adları — bunlar gösterim için, karar için kullanılmıyorsa sorun
  değil.

Karşılığı: çoğunun yanında sayı ya da enum bir alan olması muhtemel (ör. moral için sayı). **[ölçülecek]**

### 2.7 Tarih, ay, gün adları **[kesin]**

- `Calendar.cs:20-22`: ay kısaltmaları `Jan…Dec` ve `DateTime.ParseExact(…, "MMMM", InvariantCulture)` — takvim ay
  başlığını İngilizce okuyor.
- Çapa olarak gün adı: `"Monday"` (`TrainingSchedule.cs:478`), `"Thur"` (`Schedule.cs:37`), `"Sunday"` (`Calendar.cs:39`).
- `Program.cs:1187`: `"Mon"`, `"Sun"`; `"H"` / `"A"` / `"N"` (iç saha / deplasman / tarafsız, `Calendar.cs:89`).
- Haber yaşı `"Just Now"` (`News.cs:150`), maç dakikası / süre `"1 Week"`, `"{n} Days"` (`Intensity.cs:85`).

### 2.8 Eklentinin kendisi **[kesin]**

Eklenti çoğunlukla dilden bağımsız (veri yolu, öğe adı, olay). İstisnalar:
- `Offer.cs:65-74`: sözleşme kartında `"Cost"`, `"Current"`, `"Avg."`, `"Remaining"`, `"Wage"`, `"Contract Expires"`,
  `"Transfer Value"` etiketleri ve değerin `"p/w"` içermesi.
- `Offer.cs:131-140`: `"Bonuses & Clauses"`, `"Additional Promises"` başlıkları; `"Add"` ile başlayan açılır listeler.
- `Opposition.cs:15`: sütun adları `Marking`, `Pressing`, `Tackling`, `Foot` (bunlar ekran yazısı mı öğe adı mı,
  **[ölçülecek]**).

### 2.9 Dile bağlı olmayanlar (şimdilik rahat)

`status` / `next` akışı (oyunun `ContinueFlowState` enum'u), `squad` / `health` / `contracts` / `internationals` /
`diff` / `export`'un sayı alanları, takım ve oyuncu talimatlarının kimlikleri (`TeamInstructions.cs`), maç skoru ve
dakikası (`State.cs`), kupa eşleşmeleri (`Ties.cs` — öğe adlarıyla), sayfa açmanın kendisi (GUID).

Bilinen tek koruma: `Settings.cs:204` oyun dilini İngilizce dışına alırken "komutlar kör kalır" diye onay istiyor.
Açılışta dili kontrol eden bir şey yok.

---

## 3. Oyun **ayarlarına** bağımlılık (dil dışında)

Oyunun tercihlerinde para birimi, tarih biçimi, birimler var (`Settings.cs:72-78` bunları zaten listeliyor).
Kod bunların İngiltere varsayılanında olduğunu sanıyor:

- **Para birimi £** ✅ 14 Ekim: oyunun biçimlendiricisinden öğreniliyor (`MoneyFormat`, FINDINGS §8.172); para simgeyle
  tanınıyor, CLI'ın yazdığı tutarlar oyunun biçiminde. Eski hali **[kesin]**: `Diff.cs:187-192` ve `Finances.cs:156` kendi çıktısında `£` yazıyor;
  `Offers.cs:191` bonservisi `^(£[\d.,]+[KMB]?|Free|-)` ile tanıyor; `Contracts.cs:57` `"£0"` arıyor. `Career.cs:54/82`
  ve `Diff.cs:156` `€` / `$`'ı da kabul ediyor — tutarsız.
- **Haftalık maaş "p/w"** (14 Ekim: eklentinin sözleşme kartı ve `budget` artık "p/w" aramıyor; `Query` maaşı sayıdan) **[kesin]**: `Club.cs:159`, `Finances.cs:92`, `Budget.cs:134`, eklentide `Offer.cs:65`.
  Maaşı aylık / yıllık gösteren ayarda ("p/m", "p/a") eklenti değeri bulamaz.
- **Tarih biçimi `d/M/yyyy` / `dd/MM/yyyy`** **[kesin]**: `Contracts.cs:90`, `Doctor.cs:39`, `Matches.cs:144`.
  ABD biçiminde (ay/gün) sözleşme bitişleri yanlış okunur.
- **Ondalık ayırıcı** **[olası]**: sayılar `InvariantCulture` (nokta) ile okunuyor; oyun Türkçe / Almancada `7,25`
  yazarsa puanlar okunmaz.
- **Boy / mesafe birimi** **[olası]**: `Height` gösterim için; karşılaştırmada kullanılıyorsa (`Compare.cs:54`) cm / feet
  farkı sorun.

---

## 4. Ekran / UI ve tema bağımlılığı

Dilden bağımsız ama oyunun arayüz yapısına sıkı bağlı. Oyun güncellemesi, skin ya da arayüz ölçeği bunları
değiştirebilir. Skin'in gerçekten değiştirip değiştirmediği **[ölçülecek]**.

- **Kontrol / öğe adları** (119 yer): `SecondaryTabDropdownButton` (9), `layout-tile-interaction-button` (10),
  `SIButton`, `StandardButton`, `ViewDropdown`, `MonthDropdown`, `NavTabDropdown`, `in-progress-tab`,
  `matched-tab`, `taker-picker-button`, `frames-contract-terms`, `dropdown-base-no-background`,
  `continue-button`, `dashboard-hud-search-button`… En yoğun: `Guided.cs` (12), `Program.cs` (9), `Reads.cs` (9),
  `Scouting.cs` (9), `Mentoring.cs` (7), `Takers.cs` (7), `Offers.cs` (6).
- **`up=N` ile ağaçta yukarı çıkma** (76 yer, N = 0…18): çapa yazısından kaç kat yukarıda sayfanın kökü olduğu
  sayılarak yazılmış. Ara bir kutu eklenir / çıkarılırsa yanlış bölüm okunur. En çok: `TrainingSchedule.cs`,
  `Responsibilities.cs`, `Mentoring.cs` (5'er), `StaffSearch`, `Settings`, `ScoutFocus`, `Budget` (4'er).
- **Sıra numarasıyla okuma**: `Program.cs` mesaj ekleri (~90), `Finances.cs` (7), `Youth.cs` (6), `Calendar.cs` (6),
  `Saves.cs`, `Opponent.cs` (5'er). "Başlıktan sonraki 2. yazı" — araya bir yazı girerse kayar. Lincoln'de Youth başlığı
  böyle kırıldı (FINDINGS §8.155).
- **Sanal çizilen tablolar ve kaydırma**: `scroll`, `regscroll` — yalnızca ekranda çizili satırlar okunuyor; pencere
  boyutu / arayüz ölçeği satır sayısını değiştirir **[olası]**. `fm start` pencereyi küçültüyor; küçük pencere ne kadar
  satır çizdiriyor, **[ölçülecek]**.
- **Görünüm listesi kulübe göre değişiyor** (§8.155): Lincoln'de kadro tablosunda "Overview" görünümü yoktu.

---

## 5. Kariyere / kulübe / lige özel varsayımlar

- **Lig hangisi?** ✅ 14 Ekim: takımın ligi verisinden (`Human.Team.Division.Name`), iki tahmin de kalktı. Eski hali:
  İki ayrı tahmin, ikisi de ada bakıyor **[kesin]**:
  - `League.cs:66` — adında `"League"` geçen ve `"UEFA"` ile başlamayan ilk turnuva. Almanya'da "Bundesliga" bunu
    sağlamaz; ilk turnuvaya düşer (lig ilk sıradaysa şans eseri tutar).
  - `Reads.cs:1057` — adında `"UEFA"`, `"Cup"`, `"Friendly"`, `"Champions"` geçmeyen ilk turnuva. "DFB-Pokal" bunlardan
    hiçbirini içermez; ligden önce gelirse lig sanılır **[olası]**.
  - Dilden ve ülkeden bağımsız karşılık adayı: `SI.Match.CompetitionType` enum'u ve `IsLeagueStage` / `IsCupStage`
    alanları (`docs/reference/enums.txt:10765`, `data-model.txt:2075`) **[ölçülecek]**.
- **Altyapı takımının adı** **[kesin]**: ~~`U\d\ds?` (`Program.cs:316`)~~ → 13 Ekim: kulübün takımları veriden
  (`Human.Team.Club.Teams`, `TeamType` ≠ 0), `squad --u19` her altyapı takımını kimliğiyle açıyor (FINDINGS §8.164).
  Kalanlar: `( U\d\ds| Yth Cands)$` (`Calendar.cs:89`, rakibin altyapısı — takvim veriden okununca kalkar),
  `"Senior Squad"` / `"U19 Squad"` (`OfferScreen.cs:362-363`), "Under 19s / Under 21s Training Review" (§8.155). Almanya'da
  "U19" / "II" (ikinci takım) / "U23" düzeni farklı.
- **Kadro kaydı kuralları** (14 Ekim: not bloğu `squadreg.ExtraSquadRules` yolundan, tanınmayan cümle oyunun dilinde
  gösteriliyor) **[kesin]**: `Registration.cs:133-162` kural adlarını ve açıklama cümlelerini İngiltere /
  Türkiye'deki hallerine göre tanıyor ("Homegrown in Nation", "Homegrown at Club"…). Başka federasyonun kuralı adsız kalır.
- **Personel görevleri listesi sabit** **[kesin]**: `Staff.cs:20-23`. Başka ülkede "President" yerine başka unvan,
  ya da listede olmayan görev olabilir.
- **Turnuva ağacı** **[kesin]**: `Stages.cs:431`, `Reads.cs:892` "Men's" / "Under" klasör adlarına bakıyor.
- **Önbellekler kayda ve dile bağlı değil** ✅ 14 Ekim: `~/.config/fm-cli/cache/<dil>/<menajer - kulüp>/` (eklentinin
  `/state`'i oyunun dilini veriyor; `CacheFile`). `snapshot.json` ve `diff-<takım>.json` yerinde kaldı. Eski hali **[kesin]**: `~/.config/fm-cli/` altında `competitions.json`,
  `duty-areas.json`, `team-instructions.json`, `player-instructions.json`, `training-sessions.json`,
  `league-stats.json`, `snapshot.json`; `$TMPDIR/fm-cli-inbox-numbers.json`. Hepsi İngilizce ad taşıyor ve hangi
  kayıttan / dilden çıktığını bilmiyor. Yeni kariyerde (başka veritabanı, başka lig seçimi) ya da dil değişince eski
  önbellek okunur. `diff-<takım>.json` takım adıyla ayrılıyor — tek doğru örnek.
- **Kılavuz ekran görüntüleri** **[kesin]**: `tools/manual/shots.sh:22-28` ve `drive.py:31` Fenerbahçe oyuncularıyla
  yazılı ("Ederson", "Arda", "Kerem", "Emre Mor"). Başka kayıtta çalışmaz.
- Yardım metninde örnek isim: `Program.cs:1950` "coaches Cohen topla taktik" (zararsız).

---

## 6. Koda gömülü, dağınık sabitler ("hardcoded saçmalıklar")

1. **Çeviri sözlükleri 15+ dosyaya dağılmış** (§2.5). Aynı sözcük farklı dosyada farklı çevrilebiliyor; yerelleştirme
   gelince hepsi tek yerde toplanmalı.
2. **Ay ve gün adları birkaç kopya**: İngilizce ay kısaltmaları `Calendar.cs:20`; Türkçe ay adları `Clock.cs:9`; Türkçe gün
   kısaltmaları hem `Program.cs:1190` hem `Reads.cs:854`; `ShortDays` / `Days` ayrı ayrı.
3. **İki ayrı lig tahmini** (§5), iki farklı kuralla.
4. ✅ (14 Ekim, `MoneyFormat`) **Para birimi kendi çıktımızda da £** (`Diff.cs:187`, `Finances.cs:156`): oyun Euro gösterirken `diff` sterlin yazar.
5. **Oyunun yazım hatasına yama**: `Guided.cs:1038` `"Atacking"` → `"Attacking"`. Oyun düzeltirse zararsız, ama dil
   değişince anlamsız.
6. **Sabit seçenek listeleri**: oyun ayarları (`Settings.cs:27-84`), 36 oyuncu özelliğinin adları (`Compare.cs:43-46`,
   `Reads.cs:380-383`, iki kopya), personel görevleri, mevki adları (`StaffSearch.cs:174-178`), duran top türleri
   (`Takers.cs`), teklif şartları (`OfferScreen.cs`). Oyun bunları kendisi listeliyor; biz kopyalamışız.
7. **Platform**: yalnızca macOS + Steam + arm64 BepInEx (`Launcher.cs:22-37`, `Saves.cs:14`, `TacticFiles.cs:14`:
   `~/Library/Application Support/Sports Interactive/Football Manager 26`). `FM_HOME` / `FM_APP` ile değişiyor; Windows
   yolu yok. (Taşınabilirlik planının dışında ama listede dursun.)

---

## 6.5 Platform ve mağaza (Faruk, 13 Ekim 2026: en sonda denenecek)

Mağaza (Steam / Epic / Game Pass), işletim sistemi (macOS / Windows) ve oyun sürümü. Kod taraması **[kesin]**, hiçbiri denenmedi.

- **Kurulum yeri:** `Launcher.cs` oyunu `~/Library/Application Support/Steam/steamapps/common/Football Manager 26` altında
  arıyor, çalıştırılan kopya `~/fm26_bep/fm.app` ve `run_bepinex_arm64.sh`. Epic'te kurulum yeri başka; Windows'ta hepsi başka.
- **Kayıt ve taktik klasörü:** `Saves.cs`, `TacticFiles.cs` → `~/Library/Application Support/Sports Interactive/Football Manager 26/`.
  Windows'ta `Documents\Sports Interactive\…`. **14 Ekim gece:** klasör oyuna soruluyor — eklentinin `/folders`'ı
  (Unity `persistentDataPath`, kurulum yeri, platform, sürüm); `fm start` cevabı `~/.config/fm-cli/user-folder`'a
  yazıyor, `saves`/taktik dosyaları oradan (`GameFolders.cs`). Oyun hiç cevap vermediyse platformun Unity varsayılanı;
  `FM_USER_DIR` ikisini de geçer.
- **macOS'a özel araçlar:** `osascript` (pencereyi küçültme), `PlistBuddy` (Dock'tan çıkarma), `/usr/bin/pgrep` / `pkill`
  (kapatma), `.app` paketi. Windows'ta karşılığı yok; gizli başlatma oradan yeniden yazılmalı.
- **BepInEx:** bugün arm64 için yamalı paket (Il2CppInterop arm64'te bazı kancaları atlıyor). Windows x64 resmi paketle
  çalışır, ama eklentinin arm64'e göre koyduğu çekinceler orada farklı davranabilir **[ölçülecek]**.
- **Oyun sürümü:** interop DLL'leri oyunun o sürümünden üretiliyor. Mağazalar arasında sürüm farkı (ya da bir güncelleme)
  tip / alan adlarını, enum değerlerini değiştirebilir. Sayfa Guid'leri büyük olasılıkla mağazadan bağımsız **[ölçülecek]**.
- **Oyunun içi:** veri yolları, olay adları, Guid'ler, ekran ağacı mağazaya ve işletim sistemine göre değişmemeli; ekran
  çözünürlüğü / ölçek ayrı madde (§4).

Yapılacak: yollar ve başlatma tek yerde, platforma göre (ya da ayarla) seçilsin; Windows'ta başlatma / kapatma ayrı.

## 7. Çözüm seçenekleri (karar birlikte)

**A. Dilden bağımsız kimliğe taşı** — her ekran yazısını, oyunda karşılığı olan bir kimlikle değiştir:
sayfa → `PanelID` / asset adı; düğme → olay adı (+ gerekirse kontrol adı); durum → enum / sayı alanı; lig → `CompetitionType`;
mevki → sayı alanı. En sağlam yol, ama komut komut iş; her biri oyunda yeniden doğrulanmalı. Karşılığı olmayan yerler
(mesaj eklerinin İngilizce başlıkları, cümle kalıpları) bununla çözülmez.

**B. Oyunun kendi çeviri tablosunu kullan** — **13 Ekim: çalışıyor, eklentide** (FINDINGS §8.167–8.168). Türkçe oyunda
taramadaki hatalar 21'den 12'ye indi; kalanlar mevki / para biçimi, düz yazı seçeneklerdeki çakışma ve takvimin ay adları.
—  — İngilizce sabitleri koruyup çalışma anında oyunun o anki diline
çevirmek. Oyunda bir yerelleştirme altyapısı görünüyor (`NativeSettingsHandlerModule+LanguageChanges.CurrentLanguage`,
`LocalizedResources`, `EventFmxLanguageChanged`), ama metin anahtarlarına erişip erişemeyeceğimiz bilinmiyor
**[ölçülecek]**. Tutarsa kodun büyük kısmına dokunmadan tüm dilleri açar.

**C. Oyunu İngilizce zorunlu tut** — bugünkü fiili durum. Açılışta oyunun dilini okuyup İngilizce değilse açıkça
söyle; para birimi / tarih biçimi / maaş gösterimi için de aynı kontrol. En ucuz, hemen yapılabilir. Kullanıcıya
yine de **fm-cli'ın kendi çıktısı** kendi dilinde verilebilir (§6.1 sözlükleri tek yere toplayıp).

Önerim: **C'yi hemen** (koruma + net mesaj), **B'yi araştır** (bir oturumluk deneme: eklentiden bir metin anahtarının
Türkçesini okuyabiliyor muyuz), B olmazsa **A'yı en çok kullanılan komutlardan** başlayarak kademeli yap. §5'teki
kariyer hataları (lig tahmini, önbellekler, altyapı adı) dilden bağımsız; Almanya kariyerinden önce ayrıca düzeltilmeli.

**Karar (Faruk, 13 Ekim 2026 akşam):** dil sorunu **B** ile çözülecek (oyunun çeviri tablosu). Sıra numarasıyla okuma
("başlıktan sonraki 2. yazı") kötü uygulama sayıldı; ayrıca çözülecek (§7.1).

### 7.1 Sıra numarasıyla okumanın yerine

Bugün eklenti bir bölümdeki yazıları **düz bir liste** olarak veriyor (`/texts`), CLI de listede İngilizce başlığı bulup
"ondan sonraki 2. yazı" diyor. Ekrandaki yapı (hangi yazı hangi kutuda, hangisi başlık hangisi değer) eklentide kayboluyor.

Adaylar, sağlamdan zayıfa:
1. **Yazının bağlı olduğu veri yolu.** Oyunun ekran öğeleri gösterdikleri değeri bir veri yolundan alıyor; eklenti bunu
   görebiliyor (`Elements.cs` `members=1` → `BindingPath` alanları, ör. `-> "team"`). `/texts` her yazıyı yoluyla
   birlikte verirse CLI "maaş" değerini konumuyla değil yoluyla (`…Wage…`) tanır — dil, sıra ve araya giren yazı fark
   etmez. Yazı öğelerinin de yol taşıyıp taşımadığı **[ölçülecek]**.
2. **Öğe adları ve kutu yapısı.** Değer öğesinin adı (`title-text`, `primary-text`, `secondary-text`…) ve hangi kutuda
   durduğu. Eklenti listeyi düz değil, `{kutu, ad, yazı}` ağacı olarak verir; CLI "şu kutudaki `value` adlı öğe" der.
   Dilden bağımsız, skin'e bağlı.
3. **Ekranı hiç okumamak.** Değer zaten veri yolundan okunabiliyorsa (`Fm.Pull`) sayfa açılmaz. Mesaj ekleri, mali
   tablo gibi yerler için oyunun verisinde karşılığı aranmalı.

**Ölçüldü (14 Ekim 2026, FINDINGS §8.169):** 1. seçenek çalışıyor — `SIText.m_textBinding.Direct.Path` yazının veri
yolunu veriyor; `/texts`, `/form` `?paths=1` ve `?anchorpath=` ile. Taşınanlar: takvim, antrenman programı, mutluluk,
arkadaş grupları, duran top atıcıları, penaltılar, teklifler, rakip raporu. Kalan konumla okumalar için `rest[i + N]`,
`After(...)`, `IndexOf(...) + N` taranacak.

## 8. Ölçüm planı

1. Oyunu Türkçe (ve/veya Almanca) yapıp `tools/sweep.sh` — kırılanların listesi bu belgedeki tahminle karşılaştırılır.
2. Aynı dilde `/pages`, `/buttons?json=1` çıktısı: sayfa adları ve olay adları gerçekten nasıl değişiyor.
3. `PositionCombinedString`, `MoraleString` vb. Türkçe oyunda ne veriyor; yanlarında sayı alanı var mı.
4. Para birimini Euro, maaşı aylık, tarihi ABD biçimi yapıp `club`, `finances`, `contracts`, `offers`.
5. Başka bir skin / arayüz ölçeğiyle `tools/sweep.sh`.
6. Almanya kariyeri: önce §5 düzeltmeleri ve önbellek temizliği, sonra `tools/sweep.sh`.

Sayımlar kaba: İngilizce metin sayısı "büyük harfle başlayan, çok sözcüklü ya da bilinen düğme adı olan sabit" diye
sayıldı; JSON alan adları (`Name`, `Label`…) hariç tutuldu ama birkaç yanlış pozitif / negatif olabilir.
