# fm-cli — Araştırma Notları

> 14 Ekim 2026'da sadeleştirildi: aynı bulgular, kısa yazımla. Uzun ilk hali git'te (`git show 515eef0:docs/FINDINGS.md`).
> Yeni bulgu dosyanın SONUNA eklenir (`cat >>`), 3–6 satır: ne ölçüldü, sayı, sonuç. Bölüm numarası sıradaki `8.N`.
> Yapılacaklar burada değil: `PLAN.md` ve `docs/BAGIMLILIKLAR.md`.

## 1. Proje

Gerçek FM26'yı terminalden süren wrapper (oyunu yeniden yazmıyoruz). Oyun arka planda gizli açık, kullanıcı terminalde
(`fm squad --fitness`, `fm next`…). Uzun vadede MCP + doğal dil.

## 2. Ortam

Apple M2, macOS 14.6.1 (Darwin 23.6.0, arm64) · FM26 Steam appid `3551340`,
`~/Library/Application Support/Steam/steamapps/common/Football Manager 26` · Unity 6000.0.52f1, IL2CPP, universal binary
(x86_64 + arm64; Rosetta yedek yol) · `global-metadata.dat` 15.5 MB şifresiz, v31.1 · BepInEx 6.0.0-be.755 (IL2CPP,
.NET 6.0.7) · dotnet SDK 10.0.401 (runtime 8 yok → `--roll-forward Major`).

Gölge bundle: arm64 launcher `~/fm26_bep/fm.app`'e JIT izinli, yeniden imzalı kopya kurar (hardened runtime .NET JIT'ine
izin vermez). Oyun oradan çalışır; log `~/fm26_bep/BepInEx/LogOutput.log`. Eklentiler Steam klasörüne ve gölge bundle'a
birlikte kopyalanır.

## 3. Kurulum / yeniden üretme

- BepInEx arm64: [DadMych/fm26-player-export-macos](https://github.com/DadMych/fm26-player-export-macos) bundle'ı;
  yamalı Il2CppInterop, arm64'te `GenericMethod::GetMethod not found` hatasını çözer.
  `FM26_GAME="<oyun yolu>" FM26_SKIP_STEAM_LAUNCH=1 bash install_macos.sh` (bayraksız Steam'i kapatıp
  `localconfig.vdf`'i düzenler). Başlatma: oyun klasöründe `./run_bepinex_arm64.sh`.
- Metadata dump: Il2CppDumper (`dotnet build … -f net8.0 -c Release`); fat binary verme →
  `lipo -thin arm64 "<oyun>/fm.app/Contents/Frameworks/GameAssembly.dylib" -output GameAssembly.arm64.dylib`,
  `global-metadata.dat` `fm.app/Contents/Resources/Data/il2cpp_data/Metadata/`'den;
  `dotnet --roll-forward Major Il2CppDumper.dll GameAssembly.arm64.dylib global-metadata.dat out` → `dump.cs` (42 MB),
  `DummyDll/` (131 assembly). İlgili: `FM.UI`, `FMGame`, `SI.Bindable`, `SI.UI`, `SI.Core`.

## 4. Temel: `SI.Bindable.Bindings` veri ağacı

Oyunun altında global, yolla adreslenen, gözlemlenebilir bir veri ağacı var; projenin temel taşı. API:
`Get(path|Key|root+rel)`, `Exists`, `Set`, `Bind(path, ValueChangedCallback)` / `BindOneShot` / `Unbind` (değişince
haber verir → yoklama/önbellek sorunu yok), gezinme `GetNode`, `TryGetNode`, `GetDescendantCount`, `GetFullPath`,
`GetPathDebug`, `FindNodeWithName`, `FindContext`, `static MakeKey(path)`, `RootKey`, `RootNode`, `DataSet`.
Değerler `SI.Core.TypedValue`: `IsNull`, `DataType`, `AsString()`, `Get()`, `Get<T>`/`TryGet<T>` (generic, arm64'te
riskli), `Create<T>`. Tipli veri gelir; UI metni ayrıştırılmaz. Metadata'daki adlar (`Condition`, `Fitness`,
`TransferListed`, `ExpectedGoals…`, `FactsAverageAge…`, `ExpiringContract…`, `ExpenditureBreakdown…`,
`FMCCurrentDayTraining…`, `ExpectedTransferValue`, `ExpectedWageDemands`…) kökte değil, referansların altında (§8.4).

## 5. Aksiyon katmanı (ilk keşif)

`FM.UI.FMNavigationManager` semantik girdi eylemleri (`InputActionSubmit/Cancel/ClosePopup/DigIn/DigOut/TableSelectRow/
TableSortBy/…`), metotlar `NavigateToPage`, `SetCurrentPanel`, `GoToPortalScreen`, `ForceOpenTacticsPanel`.
`SI.Bindable.PanelManager.Open(PanelID, Key, callback, anchor, …)`. `ManualSingleton<T>.Instance` generic → yerine
`UnityEngine.Object.FindObjectOfType(Il2CppType.Of<T>())`. (Asıl çözüm event bus oldu, §8.9.)

## 6. arm64 / IL2CPP kısıtları

- `MonoMod has no macOS arm64 support` → managed detour (Harmony) yok; mimari hook'a değil çağrıya dayanır.
- `GenericMethod::GetMethod not found` → enjekte sınıflarda generic metot yok; `Get()` + `AsString()` kullan.
- `Class::GetDefaultFieldValue skipped` → enjekte enum varsayılanları çalışmaz.
- IL unstrip 10561 başarılı / 1452 başarısız (bazı metotlar çağrılamaz); Cpp2IL Mach-O desteği deneysel.
- Eklenti deseni: MonoBehaviour enjekte etme; `IL2CPPChainloader.MainThreadTick += probe.Tick`. Kısayol tuşu için legacy
  `Input` çalışmaz, yeni Input System: `((ButtonControl)Keyboard.current.f10Key).wasPressedThisFrame`.

## 7. Mimari

`fm <komut>` → HTTP localhost:<port> → oyun süreci içinde BepInEx eklentisi: okuma `Bindings.Get` → `TypedValue`, olay
`Bind`, aksiyon event bus. Ekran/fare/görüntü yok. Plan B (gerek kalmadı): ScreenCaptureKit + görüntü. Reddedilen: kayıt
dosyası ayrıştırma (kaydetmek çok uzun). Önbellek: önce ölç; ağaç canlı ve tembel, veri oyunun belleğinden okunduğu için
büyük ihtimalle gereksiz (gerekirse anahtar `(save_id, oyun tarihi, generation)`).

## 8. Bulgular (canlı oyun)

### 8.1 `Bindings` örneğine erişim ✅ (2026-09-21)
`Resources.FindObjectsOfTypeAll(Il2CppType.Of<FM.UI.NewGameHandlerModule>())[0]….m_bindings` (gerçek tip
`SI.Bindable.BindingSubsystem`). `FMNavigationManager`, `PanelManager` `FindObjectOfType` ile.

### 8.2 `ReadOnlySpan<char>` köprüsü ❌ (28 Eylül'de düzeltildi; önce yanlışlıkla "çalışıyor" yazılmıştı)
Span alan girişler (`Get`, `Exists`, `Bind`, `MakeKey`) Il2CppInterop'tan geçmiyor: `Il2CppStructArray<char>` ve
`fixed char*` ikisi de sessizce boş span üretir; her yol aynı key'i verir, olmayan yollar `exists=True` (boş yol = kök).
Çalışan yollar: ağaçta gezinme (`IReadOnlyNode.Name`, `FirstChild`/`NextSibling`) ve `BindingPath.Create(string)`
(ama `BindingBag.Queue` UI elemanı ister).

### 8.2b Key türetmesi çözüldü ✅
`Key` alan overload'lar span istemez; `Key` bir `ulong`. `MakeKey` arm64 disassembly'sinden (VA `0x36C0E90` → hash
`0x36BE5EC`), canlı ağaçta 608/608 doğrulandı, çalıştırmalar arası kararlı. Kod `plugin/FmCliProbe/src/PathKey.cs`.
```
acc = RootKey
her segment için: h = 0xcbf29ce484222325; her c in segment.lower() (UTF-16 kod birimi, byte değil): h = (h ^ c) * 0x100000001B3
                  x = (h ^ acc) * 0xff51afd7ed558ccd; acc = x ^ (x >> 32)
```
Küçük harf → yollar büyük/küçük harf duyarsız; başlangıç kök key → boş yol kök.

### 8.3 Ağaç global ✅
`GetDescendantCount(RootKey) = 6093`, kök 169 çocuk; tüm oyun için tek ağaç ama tembel: bir referansın alt özellikleri
biri o yolu `Bind` edince canlanır. Ekran açmaya gerek yok; `Bind` ekranın perde arkasında yaptığının kendisi.

### 8.4–8.7 Ağacın şekli
`Condition` kökte değil (`KeyNotFoundException`), oyuncu referansının altında. Nesne grafiği:
`game` → `Human` (menajer) → `Team` → `Club` (`PlayersOutOnLoanCount`, `MainTeam`…), `NextMatchOpponent`,
`Human.NextManagedFixture`, `app`. Referans tipleri `PersonReference`, `TeamReference`, `ClubReference`, `CompReference`,
`FixtureReference`, `StadiumReference`, `GameReference`, `DynamicReference`, `NullReference`. Ağaçta UI panel düğümleri
de var (panel GUID'leri, `Subtab9C`); veri düğümleri referans tipinden ayırt edilir. `FM.UI.DatabaseRecordReference`
(`SI.Interop`, C++ çekirdeğe köprü): `GetProperties`, `AcceptsProperty`, `ID`, `Type`, `new(DatabaseTableType, index)`;
şema tahmin edilmez, oyundan sorulur. Değer tipleri `Boolean`, `String`, `DynamicNumber`, `List<TypedValue>`,
`*Reference`; `AsString()` hepsinde çalışır.

### 8.8 Performans ve `runInBackground`
Okuma <100 ms (HTTP ~1 ms + bir kare bekleme, ana iş parçacığı şartı). Unity odak dışında durur → eklenti `Load()`'da
`Application.runInBackground = true`. Küçültülmüş oyunda render CPU'su simülasyona kalır.

### 8.9 Aksiyon katmanı: event bus (UI simülasyonu değil)
`SI.Core.EventSubsystem.SendEvent(EventBase)` (generic değil, güvenli); `Register<T>`/`Unregister<T>` oyun olaylarını
dinler (generic, arm64'te riskli). Erişim: `FM.UI.DevicesInputHandler` (ya da `InputRebindingModule`,
`NativeSettingsHandlerModule`) `.eventSubsystem`, `FindObjectsOfTypeAll` ile. Olaylar havuzdan (`GetPooledEvent`),
bağlam bir `Bindings.Key` ile kurulur (`EventContextBase.Setup/PrepareToSend/IsSendable`). Kayıt yükleme:
`LoadSelectedGameEvent{Record}`, `LoadGameTileSelectionEvent`, `SaveGameReference` (kayıtlar da ağaçta).

### 8.10 Gelen kutusu ve `next`'in gerçek davranışı
`ContinueGameEvent` doğru olay ama "response required" mesajları Continue'yu bloklar → `next` sessiz kalmamalı, bekleyen
mesajları söylemeli. Gelen kutusu yolu: `PortalScreenA…Messages4…MessagesStreamedObjectListA.items0.<0..9>
.BindingRemapper1.NewsItem` (`NewsTitleString`, `NewsFirstBubbleSpeechString`, `NewsItemTimeSent`, `NewsItemHasBeenRead`,
`NewsHasResponded`, `CanRespond`, `NewsItemIsPriority`) ve `.Person` (`Name`, `Initials`, `Team`). Yazılarda gömülü
tıklama-hedefi öneki var (`"BAAAACAx…=Sadettin Saran"`) → §8.17.

### 8.11 Şema statik: oyunun kendi kayıt defteri
Model statik, yalnız değerler canlı. `SI.Bindable.Reference.Core`: `PropertyIdentifierSet.m_idToInfo`
(`Dictionary<uint, PropertyIDInfo{ID, Name, DisplayName, Flag}>`), `ReferenceIdentifierSet`, `ContextIdentifierSet`;
`PropertyID.GetIdentifierSet()` (instance). Açıklamalar release'te boş ama kayıt defteri tam (`g96I` gibi kodlar
isimli). Her `*Reference`'ın static `GetPropertiesInternal`/`GetPropertyTypeInternal`'ı canlı veri olmadan sayar.
`/schema`: 931 referans tipi, 14504 özellik, 0 çözümsüz → `docs/reference/data-model.txt`. Ölçüt baz tip değil static
`GetPropertiesInternal`'ın varlığı (`NewsActionDataReference` `Il2CppSystem.Object`'ten türer; 160 → 931).
`PersonReference` 1245, `ClubReference` 492, `TeamReference` 461, `CompReference` 296, `MatchTacticReference` 227
özellik. `BindingKind` hedef tipi söyler → 1449 kenarlı grafik. Aksiyonlar da statik: `EventBase<T>` türevleri 275 olay
(`tools/sigdump --catalog events`, `docs/reference/events.txt`), ör. `NewsResponseActionEvent{NewsItem, ResponseId,
ResponseValue, ResponseActionId, ResponseActionDataReferences}`; `NewsItemReference` 44 özellik (`NewsActionData`,
`NewsItemActionDataReferences`, `CanRespond`, `ContinueText`, `LatestResponseString`…).

### 8.12 Span köprüsü `ref` ile de ölü
`ref ReadOnlySpan<char>` aşırı yüklemesi de aynı yanlış pozitif (`ZzNotAPathZz.NopeNope: exists=True`). `Create(path,
flags)`, `FindNodeWithName`, `FindContext` kapalı; adresleme §8.2b'deki key hesabıyla.

### 8.13 Canlandırma: doğru olayı gönder, veri ağaçta belirir
`/send?event=SetNewsActionDataOnPanelEvent&NewsItem=<mesaj yolu>&PortalSourceId=0` → ağaç 4198 → 5921 düğüm; mesajın
gövdesi, `SpeakTo.Actions`, `has-required-actions`, `respondedtype`, cevaplar (`Actions8.0.Action`: `ActionTitle`,
`Ticked`, `Recommended`, `IsDisabled`, `IsPopupButton`). Ekranın görünmesi gerekmez. `OpenTabEvent{Target=
"SquadFirstTeam"}` ağacı değiştirmedi (muhtemelen `Template` gerek; kadro §8.16'da çözüldü).

### 8.14 Genel olay göndericisi
Tek yol `/send?event=<Tip>&<Alan>=<değer|yol>`: olayı yansımayla kurar, referans alanları bağlama yolundan, gerisi
yazıdan (`plugin/FmCliProbe/src/Send.cs`); tip adı tam ya da kısa. Cevap zinciri: `NewsItem.NewsActionData` →
`NewsActionDataReference{NewsAction, ActionDataReferences, DynamicCardId, UniqueId}`; `NewsResponseActionEvent`,
`RunNewsActionEvent{NewsItem, SpeakTo, NewsActionID, DynamicCardID, PortalSourceId}`. `Action` düğümü
`DynamicReference` = `Dictionary<uint, TypedValue>`; sayısal kimlikler sözlükte.

### 8.15 Mesaja cevap, zaman ilerledi
`Action` sözlüğünde 31 giriş (ağaç 9 gösterir): `ActionTitle`, `IsNewsActionButton` (hangi olay), `NewsAction`
(Run… id), `ActionResponseId`/`ActionResponseValue`/`ActionID` (Response… alanları), `ActionDataReferences`.
⚠ `ResponseActionDataReferences` null bırakılınca oyun çöker (`NativeThreadAbortException`; işleyici null listeyi
geziyor). Çözüm kaydı olduğu gibi kopyalamak: `/newsaction?news=<mesaj>&action=<aksiyon>` → `NewsHasResponded = True`,
`/next` tarihi ilerletti (08:45 → 11:51 → 12:55). `next` = ilerlet → cevap gerekiyorsa dur → cevapla → ilerlet; bloke
eden mesajı `has-required-actions` ayırır (`CanRespond` cevaplanmışta da `True`).

### 8.16 Ekranlar adıyla: kadro canlandı
`PanelID` bir ScriptableObject → bütün ekranlar `FindObjectsOfTypeAll` ile adıyla (`/panels?filter=squad`:
`SquadScreen` 1355920640…); `/send` `PanelID` alanını adla çözer:
`/send?event=OpenSubtabEvent&SubtabPanelID=SquadScreen&MainTabPanelID=SquadScreen&ObjectUniqueID=0&SecondaryTab=false`
→ A takım + U19 kadrosu (`…SquadScreenAC…FirstTeamC…TileVariables2.1.Items`: `Name`, `PositionCombinedString`, `Age`).
`GameStateCache.Manager.Club.MainTeam.Players.ListLoader0.<n>` iskelet kalır: veri oyuncu nesnesinde değil ekranın
bağladığı yerde birikir; canlanan küme = ekranın kolonları (`Condition`/`Morale` için ilgili görünüm, §8.26).

### 8.17 Gömülü markup: oyunun ayrıştırıcısı
Bağlı yazıların önündeki base64 bloğun ayırıcısı güvenilmez (`=` dolgusu her zaman yok). Regex yerine
`SI.Translation.EmbeddedDataUtilities.ContainsEmbeddedData/RemoveEmbeddedData(string)`. `tools/sigdump --method <ad>`
bir yardımcının hangi tipte durduğunu bulur.

### 8.18 CLI katmanı
Bilgi eklentide, biçim CLI'da. Panel yolları her oturumda GUID'le değiştiği için sabit yol yok: `/squad`, `/inbox`
ağaçta şekil (alanları dolu referans) arar, JSON döner; `cli/fm` tabloya çevirir, pipe'ta sekmeli basar. `squad` veri
yoksa ekranı açıp yeniden okur; `next` önce gelen kutusuna bakar, bekleyen varsa olay göndermez ve listeler.

### 8.19 Oyunu gizlemek: üç parça
Headless olmaz: `-batchmode -nographics` ile köprü ayakta ama `bindings: unavailable` (UI kurulmayınca ağaç yok).
Pencere var ama görünmez olmalı: `LSUIElement` (gölge bundle Info.plist; yoksa Dock/menü), pencereli başlatma
`-screen-fullscreen 0` (yoksa kendi Space'i), `AXMinimized` (yoksa ekranda). Yanlış yollar: ⌘H karşılığı (`visible`
`false` döner ama pencere kalır — yanlış pozitif), ekran dışına taşımak (macOS kırpar, `-4000` → `-600`). `fm start`
üçünü yapar, `--debug` hiçbirini. Yama gölge bundle'a, orijinal plist `.backup`; `FM_APP` başka bundle gösterir.

### 8.20 Çeviri: `TranslationID` kendini çevirir
`SI.Translation.TranslationID.TranslatedString`, `TranslationManager.GetTranslationFromID`. `PanelID.Title` →
menüdeki ad (`SquadScreen → "Squad"`, `docs/reference/panels.txt`). Envanter kısmi: paneller Addressables ile talep
üzerine yüklenir (o oturumda 39).

### 8.21 Özelliği talep üzerine canlandırma — yarım
`GameInteropSubsystem.AddNode(key, parentKey, PropertyID)` ve `OpenChannel(reference, property, key)` hatasız döner ama
düğüm oluşmaz (yaratma `Bindings.Create(span, NodeFlags)`'ta, ölü köprünün arkasında; `Set(ref Key…)` yaratmaz).
`/bindprop` belge olarak duruyor. Veri = ekranın bağladığı küme.

### 8.22 Aynı anda tek ekranın verisi bağlı
`squad` sonrası `inbox` boştu: yeni ekran açılınca öncekinin ağacı çözülür. Her komut kendi ekranını açabilmeli; boşsa
aç ve yeniden oku. Zaman aşımı "oyun yok" demek değil (köprü ana iş parçacığında, meşgul kare): `fm` "FM yanıt vermedi —
meşgul olabilir" (exit 75) ile "FM çalışmıyor" (exit 69) ayırır.

### 8.23 Aksiyon yüzeyinin tamamı kataloglu
UI olayları (`EventBase<T>`) 275, simülasyon olayları `FM.GamePlugin.InteropEventIDs` 680, UI aksiyonları
`FM.GamePlugin.ActionID` 225, enum'lar 2160 / 19119 üye. Aksiyon kaydındaki `Event = 27` →
`InteropEventIDs.EventFmxNewsResponseAction`: bir düğmenin ne gönderdiği `/dyn` ile okunup adla eşlenir. Genel taşıyıcı
`FM.GamePlugin.EventToPlugin{InteropReference Target; InteropEventIDs ID; TypedValue DataToSend}`;
`GameInteropSubsystem.m_handlers` `Dictionary<ActionID, …>`. Taktik enum'ları: `InPossessionRole` 57,
`OutOfPossessionRole` 46, `TeamInstructionPhaseID` 14, `TacticTeamInstructions` 33, `PlayerInstruction` 36 (PI_*),
`TacticalStyles` 13, `TacticMentalities` 8, `Formation` 72; rol enum'ları bit maskesi (bir mevkinin uygun rolleri tek
sayı), faz/mentalite düz. `TacticTeamSelectionDropdownEvent.IsInPossession` iki fazı aynı olayla sürer. Basın:
`PressConferenceEditSessionReference` (10 özellik: `JournalistSpeaking`, `NumberOfQuestionsAsked`…) +
`EventFmxPressConferenceAnswerQuestion/NoComment/EarlyExit/StormOut` — mesaj cevaplamanın aynısı. Hepsi
`docs/reference/COOKBOOK.md`; ham `actions.txt`, `enums.txt`.

### 8.24 Katalog yetenek oldu: enum adla, `TypedValue`, kuru çalıştırma
`/send` enum alanlarını adla çözer; `TypedValue` alanları skalerden kurulur (`TypedValue.Create<T>` generic ama çalışıyor —
arm64 uyarısı yalnız enjekte sınıflar için); `dry=1` olayı kurup raporlar, göndermez
(`/send?event=EventToPlugin&ID=EventFmxPressConferenceAnswerQuestion&dry=1`). Çözülemeyen alan varsa olay hiç gitmez
(uydurma `ID` → hata, oyun ayakta) → yarım yükle çökme (§8.15) bu yoldan olmaz.

### 8.25 Kontroller olaylarını kendileri tarif eder: `/buttons`, `/press`
Her UI olay tipinin `…EventContext`'i (`SI.Bindable.Events.EventContext<T>`) her alan için bir `BindingPath` taşır ve
`FillEvent(T)` ile doldurur (ör. `SpeakToActionEventContext.m_responseIdBinding…`). Context'ler kontrollerde
`SI.Bindable.EventList` olarak durur (`SIButton.Events/RightClickEvents`, `SIDropdown.SelectionEvents`,
`SICheckBox.CheckedEvents`, `FMPitchPosition.Events`, `FMTacticsPanel.SelectionEvent`); kullanılınca
`SI.Core.UIEventQueue.QueueEvents(EventList)` (GameSubsystem). `/buttons` yüklü `UIDocument`'lerdeki her kontrolü döker
(`?type=`, `?text=`, `?limit=`; portal 130, kadro 236 kontrol); `/press?ptr=<adres>[&list=][&dry=1]` listeyi kuyruğa
koyar — yükü oyun kurar, çökme yok. Doğrulandı: dry'de ağaç değişmez; uydurma adres hata; Squad↔Portal 3 tur
4198↔6093; "Tümünü okundu işaretle" Unread 10 → 0 (`EventToPlugin` + `InteropEventIDs` ilk canlı gönderim).
`EventToPlugin` şekli: `Target` bir yoldan (`"human"`), `DataToSend` başka yoldan (`"team"`), `ID` sabit.
Kısıtlar: adresler ekran yeniden kurulunca değişir (nav bar kalıcı), `/press` her çağrıda taze arar; yalnız yüklü
ekranın kontrolleri var; bağlanmamış alanlar listede `-1`; buton yazılarında `<link>`/`<style>` kalır. Basın cevapları
da `SpeakToActionEvent` (`NewsActionHandlerModule`; `SpeakToActionReference` 31 özellik = §8.15 kaydı).

### 8.26 Kondisyon ve moral: tablonun görünümünde
Kadronun açılış sayfası özet pano; tablo "First Team" alt sekmesinde ve yalnız seçili görünümün kolonlarını bağlar
(ağaç 4198 → ~11100). `ViewDropdown` (SIDropdown) 14 görünüm: Selection Info (`Condition`, `Sharpness`, `Morale`,
`PlayingTimeHappinessLevel`, `Goals`, `AverageRating`, son beş maç), General Info (`Wage`, `AgreedPlayingTime`,
`TransferValueRangeString`, `CoachBestRole/Position`, yıldızlar, sözleşme), Dynamics (`Morale`, `MoraleString`,
`HierarchyInfluenceCategoryString`, `SocialGroupString`, `PromisesCount`…), Injuries (`InjurySeverity`,
`InjuryDescription`, `InjuriesTimeMissed`), Risk Assessment (`OverallInjuryRisk`, `InjurySusceptibility`, `Load`,
`MatchLoad`), Playing Time (`ActualPlayingTime`, `FutureAgreedPlayingTime`…), ayrıca Attributes ›, Contract, Transfer,
Home-Grown, Reports, Stats ›, International, Development. Açılır liste olay taşımaz, bağlı değere yazar
(`…BindingVariables0.selectedView`) → `/dropdowns[?text=]`, `/select?name=&choice=[&dry=1]` (ya da `ptr`+`index`),
`SIDropdown.OnSelectionChangedInternal(int)`; seçenek adı `RowData`'da, `ChildList` alt menü. `/press?text=<etiket>
[&name=]` etiketle basar, birden çok uyarsa adayları listeler. Satır birleştirme: hücreler aynı `PersonReference`'ı ayrı
bağlar; `/squad` adın çevresindeki değerleri toplar, yerleşim değişkenlerini (`Is…`, `Can…`, küçük harfliler) eler,
kayıtları adla birleştirir. Satırı tanıtan alan görünüme göre değişir (Dynamics'te yalnız `SquadNumberBlankIfInvalid`).
`Condition = -1` sakat (Talisca, Becão; `StatusTitleShort = Inj`); `StatusTitleShort` ilk durum bayrağı (`Inj`, `Wnt`,
`Hol`…), her görünümde. Doğrulama: `squad --fitness` 27 oyuncu 4,8 sn, `--morale` 6,5 sn, 3 turda tutarlı.

### 8.27 CLI'dan gezinme; menülerdeki ekran haritası
`fm buttons [metin] [--all]`, `fm press "<etiket>" [--name]`, `fm dropdowns`, `fm select <liste> "<seçenek>"`
(`--dry`, `--json`). Etiketlerdeki rich-text (`<color><link="0"><style=…>`) ayrıca ayıklanır; aynı etiket birden çok
kontrolde olabilir (`--name`); basıştan sonra ekran hemen hazır değil → "yok" cevabında 8 sn'ye kadar yeniden sor.
Her ana sekmenin (`Portal`, `Squad`, `Recruitment`, `Match Day`, `Club`, `Career`) `NavTabDropdown`'u ekran kapalıyken
de alt sayfaları sayar: 77 sayfa (`docs/reference/screens.txt`). Menü satırını `/select` etmek ya da `SelectionEvents`'i
`/press` ile kuyruğa koymak ekranı açmaz (çözüm §8.28). ⚠ `SelectionEvents`'i elle kuyruğa koymak
`OpenSubtabEventContext`'i gönderilemez halde kuyruğun başında bırakıp bütün basışları tıkadı (`/send` kuyruk
kullanmadığı için çalışmaya devam etti). Önlem: `/queue[?clear=1]` bekleyeni gösterir/boşaltır; `/press` dolu kuyruğa
basmaz, baştaki olayı söyler. Ders: `/press`'i yalnız kontrolün kendi ana listesiyle kullan. Çalışan gezinme: ana sekme
düğmesi (`OpenPanel`), düz alt sekme (`OpenSubtab`), pano kutusu, elle `OpenSubtabEvent`.

### 8.28 Sayfayı adıyla açmak: menü satırı panel GUID'ini taşır
`/dropdowns?deep=1`: satırın `RowData.PropertyValue`'su `Value` (panel asset GUID'i), `SubtabText`, `PanelID`,
`OpensCard`, `ObjectUniqueID`; ilk satır sekmenin kendi ekranı. 75 satırdan yalnız 9'unun `PanelID` nesnesi yüklü
(paneller ilk ziyarette yüklenir) → `OpenSubtabEvent` çoğu sayfada kurulamaz; `OpenBookmarkEvent{AssetReferenceT<PanelID>
BookmarkPanelID, MainTabPanelID}` asset referansı alır (`new AssetReferenceT<PanelID>(guid)`), yüklemeyi oyun yapar.
`/pages` (77 sayfa: sekme, ad, panel id, GUID, kart mı), `/open?page=<ad>[&tab=][&dry=1]`, `fm pages`, `fm open
"<sayfa>" [--tab]`; ana sayfa sekmesinin adı asset adından (`PortalScreen` → Portal). "Overview" altı sekmede var →
`--tab` ister. 13 sayfa denendi, hepsi açıldı, kuyruk boş. Gözlemler: sayfa bazen öncekinin üstüne kart açılır (alttakinin
verisi de bağlı; `OpensCard` 33 sayfa); menüler açılıştan birkaç sn sonra dolar; panel başlığı sayfa adından farklı
olabilir (Tactics → "Tactics Planner"). `squad` hangi liste bağlıysa onu okuyordu (pano açıkken 47, tablo 27) → artık
adı belli görünümü ("General Info") okur; altyapı ayrı.

### 8.29 Mesaj cevaplamak bir düğmeye basmak: `fm reply`
Mesaj satırı bir düğme (`NewsItemButton`, `SetNewsActionDataOnPanelEvent`); basınca cevaplar `SpeakToAction` düğmeleri
olarak belirir. Context eşlemesi: `m_eventBinding → action.Event`, `m_responseIdBinding → ActionResponseId`,
`m_responseValueBinding → ActionResponseValue`, `m_actionIdBinding → ActionID`, `m_responseActionIdBinding →
NewsAction` (ActionID değil), `m_responseActionDataReferencesBinding → ActionDataReferences`, `m_npcDataBinding → NPC`,
`m_speakToBinding → SpeakTo`. `/newsaction` (yanlış eşleme) silindi; kontrolün kendisine basılır. `fm inbox` numaralı;
`fm reply <#|konu>` cevapları listeler (terminalde sorar), `fm reply <#> <no|"cevap"> [--dry]`. Açık mesaj
`HumanPortalLastSelectedMessage` (`UniqueId`); cevaplar ancak o eşitlenince okunur (§8.178'de yeniden yapıldı).
Messages kart olarak açılınca her mesajın iki düğmesi olur → `/press` `any=1` ile razı olur. Bloke eden mesaj
`NewsItemIsPriority` olan (`CanRespond` yetmez: "Pre-season Preparation" öncelikli değil). `continue-button` etiketi
durumu söyler ("Continue", "Response Required", "Complete Introduction"). Doğrulama: 5 mesaj listesi ham veriyle aynı,
cevapsız mesaj 3,8 sn'de söyleniyor, uydurmalar reddedildi, `--dry` değiştirmedi, 3 cevap bekleyeni 5 → 2'ye indirdi.

### 8.30 Tanıtım turları ve taktik sihirbazı
Tur adımlı bir konuşma: panelin `SpeakTo`'su `CurrentInductionStepIndex`/`MaxInductionStepIndex`, rehberin sözü
`SpeechBubbleList…bubble.DisplayString`. Tur düğmeleri `name=StandardButton`: Continue (son adımda yok), Previous,
Skip Introduction, End Introduction (son adım; mesajı kapatan bu). Mesajdaki "Continue Introduction" turu ilerletmez,
paneli geri getirir. Taktik turu (1/1) düğme sunmaz: taktik yok (`Tactic.HasTactic = False`), taktik oluşunca kapanır;
sihirbazla taktik kurmak mesajı da cevaplanmış sayar. Sihirbaz (`TacticCreatorCurrentStage`): 0 stil (11,
`tac-styles-options`) → "Formation (In Possession)" (`EventFmxTacticCreatorIncrementStage`); 1 topla diziliş
(`all-options-list`) → "Formation (Out of Possession)"; 2 topsuz diziliş → "Confirm" (`EventFmxConfirmedCreateNewTactic`).
Diziliş listesi stile göre değişir; ilerleten düğme seçim yapılana kadar kapalı; giriş "No Tactic Set" kutusu (tur
paneli, Match Day › Overview); `fm open Tactics` taktiksizken boş panel açar. Seçenekler `SIRadioGroup`/`SIRadioButton`
(olay listesi yok), seçmek `SIRadioButton.OnClickedOrSubmitted()`; `/dropdowns`/`/select` radyo gruplarını da kapsar.
`/elements?text=&up=&depth=&members=1` etiketin çevresindeki eleman ağacını tipleriyle döker (olay taşımayan şeye "nasıl
basılır" aracı). Komutlar: `fm tour [--skip]`, `fm tactic new ["<stil>" "<topla>" "<topsuz>"]` (eksik aşamanın
seçeneklerini listeler; numara/tam ad/tek uyan parça). Doğrulandı (iki oturum): tek komutta Control Possession · 4-3-3
DM Wide · 4-1-4-1 DM; uydurmalar reddedildi; Squad turu 7 adım 8 sn; Dynamics turu 6 adım. Zaman ilerledi (30/6 →
1/7/2025). İlerle düğmesinin etiketi bir kez "News Site" oldu (haber açtı) → `next` etikete bakmaz. `reply` sonrası
gelen kutusu bir an geç güncellenir → `reply` mesaj engel olmaktan çıkana kadar (≤8 sn) bekler.

### 8.31 Taktiği okumak/değiştirmek; sayfayı verisiyle açmak
⚠ `OpenBookmarkEvent` sayfayı açar ama verisini bağlamayabilir (Tactics boş geliyordu; §8.28'de yalnız panelin açıldığına
bakılmıştı). Bağlayan yol: sekmenin ilk sayfası (bookmark) → alt sekme çubuğu o sekmeninkine dönene kadar bekle → sayfanın
kendi alt sekme düğmesi. Yalnız "More" menüsündekiler bookmark'la kalır. `/press` `event=` süzgeci aldı. `/tactic`:
`Tactic` (`Name`, `MentalityString`, `TacticalStyle`, `TacticalIntensity`, `TacticalFamiliarity`) + 11 `Position` kaydı
(`TeamSelectionIndex` 0..10, `IPPosition`/`OOPPosition` maske, `IPRole`/`OOPRole` maske + `PlayerRole` = RoleDataEntry
adı, topsuz "OP…", `player-name`). `PositionMask`: 1 GK, 4 DR, 8 DL, 16 DC, 32 WBR, 64 WBL, 128 DM, 256 MR, 512 ML,
1024 MC, 2048 AMR, 4096 AML, 8192 AMC, 16384 ST; orta mevkiler ayrıca 1048576 (sol) / 2097152 (sağ) (`2097168` sağ stoper).
Mentalite ve stil adsız iki açılır liste (`dropdown-base-no-background`), hangisi olduğu kendine özgü bir seçenekten
anlaşılır; seçmek onaysız uygulanır; stil değişince taktik adı ve yoğunluk da değişir (Gegenpress 5, Tiki-Taka 4).
"Quick Pick" (`EventFmxQuickPick`) boş on biri doldurur. Komutlar: `fm tactic [--json]`, `tactic mentality|style [<x>]`,
`tactic pick`. Rol: mevki başına iki liste (topla/topsuz), olay `RoleOptionEvent`, satır `{TacticsRoleName, Role, Selected,
StarRange}`, son iki satır "Go to Role Selection/Player Instructions"; 22 liste mevki sırasıyla ama üstünde mevki yazmaz →
komut listenin `Selected`'ını taktikteki rolle karşılaştırır, uyuşmazsa dokunmaz; seçim `/select?…&fire=1` (`fm tactic
role <mevki> [<rol>] [--oop]`). İlk 11'e oyuncu: mevki başına `TacticTeamSelectionDropdownEvent` listesi (önerilen +
"All Players ›"), satır `{Player, Index, TeamSelectionIndex, PlayerTeamSelectionIndex (−1 kadroda değil), StarRange}`,
isim yok → `/pickplayer?slot=&player=` + alt menü kalıbı. Maç kadrosu `tacticsteamselection` (`Index` 0-10 ilk 11, sonrası
yedek; `PlayerIndex`, yedeklerde `Player.ShirtName`). `fm tactic player DML Fred` → "DML: Guendouzi → Fred". Yalnız
yedekler adla seçilebiliyordu (sonra çözüldü). Doğrulama: mentalite/stil/rol değişimleri ekranla aynı, uydurmalar red.

### 8.32 Oyunun durumu sabit yolda: `next`, `status`
Hangi sayfa açık olursa olsun kökte bağlı: `continue.State` (`ContinueFlowState`, 33 değer: `CanContinue`, `NeedAction`,
`PressConference`, `Tactics`, `TeamNews`, `TeamTalk`, `TunnelInterview`, `Processing`, `Match`, `Saving`, `SubmitTeam`,
`PostMatchReaction`, `NewsAggregator`, `LatestScores`…), `continue.ContinueButtonMainString`, `game.GameProcessing/
GameSaving/GameWaiting/CanSave/IsDuringMatch`, `game.CurrentGameDate`, `game.ImmediateUserRequiredTask` (NewsItemId,
DynamicCardId). Tarih tek sayı (`GameDate.Data1`): bit 17+ yıl, sonraki 9 bit yılın günü (1'den), alt 8 bit saat
(`265467401` → 2025, 182. gün, saat 9); saat birimi çeyrek saat, 05:45'ten sayılıyor gibi (9 → 08:00, 13 → 09:00) —
mesajlardan tahmin, oyundan okunmadı (sonra `/pull` ISO verdi). `fm next` ilerle'ye basar, oyun bir şey değiştirene kadar
(4 sn) sonra iki okuma boşta görünene kadar (≤3 dk) bekler; tarih farkı ve yeni mesajları basar; `next <n>` engel çıkana
kadar tekrarlar. Basışların bir kısmı hiçbir şey değiştirmez, olduğu gibi söylenir. Tuzaklar: yüklemeden hemen sonra
`State = CanContinue` (ilk gerçek durağa kadar güncellenmez) → gelen kutusuna da bak; `NewsAggregator`, `Portal`,
`LatestScores`, `Fixtures`, `Results` duraklarında ilerle sayfa açar → geçilebilir sayılır; maç durumlarında basılmaz.
Temiz kayıttan 1 Temmuz'a kadar bütün yol üç oturumda yalnız CLI ile yürüdü.

### 8.33 Konuşmalar: `fm talk`
Toplantıdaki her cümle `SpeakToActionEvent` düğmesi, şablon `name=StandardButton` (tur da konuşma).
`…SpeakToSenderMessagesRemapper.NewsResponseOptionalButtons.items.N.Action{ActionTitle, IsHeader}` (yaklaşım başlığı) +
`SubActions.items.M.Action{ActionTitle, Hint, Recommended, IsDisabled}`; karşı tarafın sözü
`SpeechBubbleList.items.N.bubble{DisplayString, IsHuman}`; tepki `InteractionBodyLanguageString` + `…Level` (dinleyen
başına). Aynı düğme iki yerleşimde → metinle tekilleştirilir. Son cümle "End Chat"; "Leave Meeting" ayrı
(`continue-button`, `ClosePanelEvent`). `fm talk`, `talk <no|parça> [--dry]`, `talk leave`. `reply` cevabın ne açtığına
bakar (tur → `tour`, konuşma → `talk`, yoksa mesajın kapanmasını bekler). Doğrulandı: takım tanışması 7 cümle baştan sona.
Tepki dökümü oyuncu adıyla eşlenmedi.

### 8.34 Maç günü: duraklar, onay pencereleri, `fm match`
`State` sırası ve o durağın `continue-button`'u: `Tactics` "Review Team Selection" (`ContinueGameEvent`) → `SubmitTeam`
"Go To Match" (onay penceresi "Proceed to match?") → `Match` (yükleniyor, düğüm ~6000 → ~700) → `TeamTalk` "Address the
Team" (`MatchContinueEvent`) → `StepOntoThePitch` "Go to Dugout" (`EventFmxTeamTalkProcessTalk` + `MatchContinue`;
konuşma burada) → `WarmUp` "Kick Off" → `MatchLoading` (top oyunda, adı yanıltıcı) → `TeamTalk` (`Period = HalfTime`) →
`SecondHalf` "Second Half" → `FullTimeTeamTalk` → `LeaveMatch` "Leave Match" → `CanContinue`. Kural: ilerle düğmesi varsa
bas, yoksa bekle. Maç ekranı: `MatchTime`, `MatchHomeScore`, `MatchAwayScore`, `MatchStatePeriod`, `IsHomeTeamHuman`,
`MatchHighlightCommentary`; devre/maç sonu ayrıca `MatchScore`, `TotalShots`, `ShotsOnTarget`, `TotalPossession`,
`ExpectedGoals`, oyuncu `TotalRating`, olaylar (`IncidentType`). Onay pencereleri sessiz: `State` değişmez, iki basış
iki pencere açar; `#dialog-title`/`#dialog-description` ile tanınır; düğmeleri olay listesi taşımaz →
`SIButton.SimulateButtonClick(bool)` (`/click?ptr=`). `fm dialog [<no|yazı>]`, `fm match [--talk]`. Maç sakatlıkta
kendiliğinden durabilir (maç içi taktik ekranı değişiklik bekler, düğme/pencere yok, saat durur) → `match` saat 45 sn
kıpırdamazsa durur. "Dugout" paneli: `EventFmxMatchDugoutConfirmEvent` ("Do it"), `…DismissEvent`, `…JumpToEvent`.
`match --talk` baştan sona 6 dk. Maç içi kontroller: mentalite, "Shouts" (Encourage, Calm Down, Focus, Fire Up, No
Pressure, Demand More, Praise, Berate), oyuncu menüsü, `MatchTogglePlayPause`, `MatchSkipReplay`; hız olayları
`EventFmxHighlightModeChanged/HighlightSpeedChanged/BetweenHighlightSpeedChanged/RequestInstantResult`.
Anında sonuç (`fm match --instant`): "Instant Result" (`OpenPanelEvent`) → "leave this match to your staff?" Confirm
(`InstantResultEvent`) → "Proceed with simulate match?" Yes → butonsuz "Match in progress..." → `CanContinue`; 9 sn.
Oynanmış fikstür: `HasResult`, `ResultLinkableNoSpaces = "1 - 0"`, `ResultType`. Sonuç kayıtta yazılı değil: aynı maç
dört kez dört farklı skor. Oyuncu değişikliği (`fm sub [<çıkan> <giren>]`): sahadaki oyuncunun menüsü (`SIDropdown`,
`MatchPlayerMenuEvent`, veri `modify-data`), "Substitute Off ›" alt menüsü kulübe (`{Type = 7, PropertyValue = <oyuncu>,
DisplayValue, Player2}`); çalışan seçim `UpdateDropdownSelectionBinding(<satır>)` + `SelectionEvents`'i kuyruğa koymak
(`OnSelectionChangedInternal(IReadOnlyList<int>)` çalışmaz) → `/select?ptr=&path=7,3&fire=1`; `MatchTeamHasPendingChanges`
True → ilk duraklamada uygulanır. `sub Musaba Oğuz` 1,3 sn. Maç içi taktik ekranı (`MatchOpenTacticsEvent`, maçı
durdurur): 18 `TacticTeamSelectionDropdownEvent`, 22 `RoleOptionEvent`, 2 `ModifyTactic`.

### 8.35 Kaydetmek ve otomatik kayıt
`SaveGameEvent` kendi dosyasının üzerine kaydeder (`game.GameSaving` kalkıp iner, ~2 sn); `fm save --yes` gönderip bekler,
`--yes`'siz yalnız söyler; eski hal `last save overwrite backup.fm`. ⚠ Oyun kendiliğinden de kaydeder (zaman ilerleyince
ana dosyanın üstüne, eski hal `… (v02).fm`) → zamanı ilerleten oturum kayda yazılabilir.

### 8.36 Duran top sihirbazı, liste cevapları, basın
Set Pieces Introduction "Decline" sunmaz; "Let's go" (`EventFmxSetPieceCreatorStart`) + 6 soru, her birinde `Option`
listesi (2-3 seçenek, biri seçili) ve "Next" (`EventFmxSetPieceCreatorNextStep`, `name=buttons-next-primary-default`),
sonuncuda "Confirm". Sorular: korner nereye (front/penalty spot/back), kavis (in/away), hücumda geride kalan, savunmada
markaj (alan/karışık/adam), bekler, ileride kalan. `fm setpieces new [<no> …]` (verilmeyen soruda oyunun seçimi); Match
Day › Set Pieces rutin yokken sihirbazla açılır. Liste cevaplı soru: basında "What position…strengthening" cevabı
`GenericDropdown` (15 seçenek); seçmek `SelectedAction`'a yazar ama göndermez → `SelectionEvents`'i kuyruğa koymak
gönderir (`/select?…&fire=1`); `talk` liste cevaplarını seçenek sayar. Basın: `State = PressConference`, "Attend Press
Conference"; "Start Press Conference" → 14 soru → "Exit Press Conference"; başlıklar ton (Positive, Disagree, Make
Demands), tepkiler 7 gazeteci; iki oturumda `talk` ile baştan sona. "Expectations and Promises Meeting" aynı şablon.

### 8.37 Lig tablosu ve fikstür (portal kutuları)
`/table`: satır `TablePosition` + `LeaguePoints`, çevresinde ad, `LeagueGamesPlayed/Won/Drawn/Lost`, `LeagueGoalDifference`
(18 satır). `/fixtures`: `Fixture` kaydı `KickoffTime`, `FixtureDate` (paketli), `MatchVenueShort` (H/A/N), `HasResult`;
rakip `Club.ShortName`, turnuva `comp.Name`; portal kutusu sıradaki 7 maç; tarihsizler (başka kulüplerin) atılır.

### 8.38 Oyuncu sayfası ve kişi tabloları
`fm player`: kadrodaki isim düğmesi (`NameButton`, `OpenPanelEvent`) sayfayı açar; `/player`: `Person{Name, Age,
SquadNumber, PositionCombinedStringLong, DateOfBirth, IsGoalkeeper, InternationalCapsAndGoalsString,
TransferValueRange.TransferValueRangeString, FullContract{Wage.DisplayValue, AgreedPlayingTime.DisplayValue,
EndDateDescription}}`, 36 `attribute{AttributeTypeInfo.Name, AttributeValues.PropertyValue}`, `ConditionString`,
`InjuryDescription`, `Personality`, ayak, `Height`, `Nation.Name`, alışkanlıklar (`Text`). Sayfa yalnız onda olan özellik
listesinden bulunur. Bloklar sırayla bağlanmaz; CLI zihinsel (14), fiziksel (8), duran top (4) adlarını bilir, kalanı
teknik/kalecilik. ⚠ Sayfayı açan basıştan hemen sonra kuyruk bir an dolu (`OpenPanelEventContext status=Sending`) — kalıcı
tıkanma değil; CLI "henüz hazır değil" sayıp 8 sn'ye kadar dener. Doğrulama: guendouzi 11 sn (ikinci açılış anında),
Ederson "KALECİLİK" 14 özellik. `fm squad --views` / `--view "<ad>"`: görünümün bütün alanları kolon (veriden; ~15 iç
alan atılır): Contract (SquadNumber, TransferValueRangeString, AgreedPlayingTime, ContractStatus, Wage), Injuries,
Playing Time, Transfer (AskingPrice, TransferOptions), Development (MinValue, MaxValue, TrainingRating). "Attributes ›",
"Stats ›" alt menü. ⚠ Taktik ekranı alttayken Musaba'nın mevki/yaşı boş geldi (iki liste birleşirken eksik). `fm players`
(Recruitment › Player Database): `ViewDropdown` (Overview, General Info, Contract, Transfer, Selection Info, Scout -
Transfer, Scout - Position, Stats ›, International, Attributes ›) + "Quick Filters" (`inputs-combobox-default-default`:
Transfer Listed, Loan listed, Contract Expired/Expiring, Home-Grown, Unhappy, No Preferred Foot, Goalkeepers…,
Extensive Knowledge; olayı `EventToPlugin`, veri `SelectedQuickFilter` → seç + gönder). Satır kişisi `binding` adıyla +
`Index`; `/people` satırı şeklinden tanır. ⚠ İki tablo aynı anda yüklü kalabilir, ikisinin de `ViewDropdown`'u var →
komutlar kendi listesini yalnız onda olan görünümden tanır (kadro "Dynamics", veritabanı "Scout - Transfer").
Süzgeç oyunda kalır, yenisi eskisinin yerine geçer; `--clear` sayfanın "Clear"ı (`EventFmxResetFilter`). Yalnız ekrandaki
satırlar bağlı (21; sonra kaydırmayla çözüldü, §8.128).
İsimle arama (`fm search`, kadro dışı `fm player`): üst çubuk "Search" (`dashboard-hud-search-button`) paneli açar; metin
`SearchBox` (`BindableTextEditBox` içinde Unity `TextField`). `SearchValueChangesEvent{Value}` çalışmaz; kutunun kendisine
yazmak çalışır (`m_textField.value = "Osimhen"`): `/type?name=SearchBox&text=…[&submit=1]` (`OnSubmitEvents`),
`/textboxes`. Sonuçlar `…list.items.N.searchresult` (`SearchRecordType`: Player, Staff, Club…) + `Person.Name`,
`Club.Name`; her sonucun `PlayerSearchButton`'u (`OpenPanel` + `SearchResultClicked`, etiket hep "Player"). Tuzaklar:
önceki aramanın düğmeleri yüklü kalır → `/searchopen?name=` düğmeyi verisini o satırdan alan olarak bulur; açılan oyuncu
sayfaları üst üste kalır → `/player?name=`. Oyunun araması gevşek ("icardi" → Carlos Vicente); `player` adında yazılanı
içerenlere daraltır, tek kalırsa açar. Aynı adlı iki oyuncu o zaman ayırt edilemiyordu (sonra §8.139).

### 8.39 MCP: komutlar araç olarak
`fm mcp` MCP stdio (satır başına JSON-RPC): `initialize`, `ping`, `tools/list`, `tools/call`. Her araç bir komut:
argümanlar kelimelere çevrilir, komut terminaldeki gibi çalışır, bastığı metin sonuç (`isError` = çıkış kodu ≠ 0).
Repo kökünde `.mcp.json`. Hatalar −32700 / −32601 / −32602. Sınırlar: oynanan maç 6-8 dk (istemci zaman aşımı),
yönlendirmeler komut adıyla, düz metin döner.

### 8.40 Span köprüsü çözüldü: ekran açmadan her veri
Sorun span değil Il2CppInterop'un sarmalayıcısı. `ReadOnlySpan<char>` bellekte `{char* pointer; int length}`; native
bellekte elle kurup metodu `NativeMethodInfoPtr_…` ile `il2cpp_runtime_invoke` üzerinden, byref parametreye struct
adresini vererek çağırmak çalışır (`plugin/…/RawPath.cs`): `pathexists ZzNotAPathZz.NopeNope → false`,
`pathbind Human.Team.Name → Fenerbahçe` (hiçbir ekran bağlamamışken). `Bindings.Bind(ref span, cb)` ekranın yaptığını
yapar (düğüm yaratır, kanal açar); değer aynı ya da sonraki karede. Liste elemanı bağlanabilir düğüm değil →
`Bindings.Set(ref span, TypedValue, NodeFlags, SetFlags)` ile `fmcli.<liste>.<n>`'e koyup altına bağla:
`/pull?path=Human.Team.Players&props=Name,Condition&list=1`. İlk çağrı bağlar, değer ikincide; `/pull` idempotent.
Ne okunabilir `/schema`'da statik. → "aynı anda tek ekran" kısıtı okuma için kalktı.

### 8.41 Ayarlar ve maç hızı
`settings` (`SettingsReference`, 106 özellik): `MatchSpeedDuringHighlights` 1.5 (en çok 10),
`MatchSpeedBetweenHighlights` 30, `MatchHighlightMode(s)`; `game`: `AutosaveModeAndIntervalString` ("Saves automatically
every week"), `LastSaveDate`, `NumberTimesSaved`, `GameTimeSinceLastSave`. Değiştirmek Preferences ekranından (§8.61).

### 8.42 Transfer teklifi: `fm offer`
Oyuncu sayfasındaki değer düğmesi (etiketi değer ya da "Not for Sale") aksiyon panelini açar (Make An Offer, Add To
Shortlist, Scout Player, Ask Agent About Availability, Transfer ›…; bir `SpeakTo`). Make An Offer → teklif ekranı
(`transfer-offer-button`/`loan-offer-button`, Negotiable listesi, bonservis `FMCombobox` `name=combobox`, ödeme şekli,
"Add", Undo/Revert, Make Offer). Gönderince kontroller kapanır, `talk` "An offer has been made…", yalnız "Exit Talks".
Bonservisi yazmak: `FMCombobox` değeri ancak sayı alanı odağı kaybedince alır; oyun küçültülü, gerçek odak yok →
olayları elle üretip kutuya ver: `box.OnGainFocusInput(FocusEventBase<FocusInEvent>.GetPooled(field, null,
FocusChangeDirection.unspecified, box.focusController, false)); field.value = 1500000; box.OnLostFocusInput(…FocusOutEvent…)`
(`/setnumber`; 22 sn, eskiden +/− `EventFmxIncrement/DecrementCashValue` ile ~2 dk). İşe yaramayanlar: `m_numericInput`
+ `TryTriggerValidationEvent`, Focus/Blur, doğrulama verisini doldurmak, ⚠ `OnNumericValueChanged` havuzdaki
`TypedValue`'yu yerinde değiştirince oyunu kilitledi.
Teklif ekranını okumak (`/offer`): veri `TransferOfferEditSessionReference` ama listeler `List<TypedValue>`; ekrandan
okumak kolay: `#club-player-text`, `#player-text`, `#Text_TransferCostValue`, `#Text_TotalCostValue`; şartlar
`#frames-contract-terms` satırları, başlıklar `#players-to-exchange-title` ("Base Details"), `#fees-title`,
`#clauses-title`; satır adı `#primary-text`, değeri `FMCombobox` (tutar), `SIDropdown` (seçim) ya da ikisi
("After League Appearances" = "20 games" + "£250K"); `#lock-button-remapper` kilit. Teklif çapında Negotiable ve cevap
süresi (No Deadline · Today · Tomorrow · Next Week); karşı teklifte Current/Previous Offer. Ek ödemeler: Instalments,
After League Appearances, Per League Appearance, After International Appearances, Minimum League Goals, Players To
Exchange, alt menülü bonuslar; maddeler: Loan Back Length, Can Be Recalled From Loan Back, Percentage Of Next Sale,
Percentage Of Profit From Next Sale, Buy Back Price, Selling Team Wage Contribution, Arrange Friendly, Sell Player First.
Değiştirmek: ekleme/seçim `/select … fire=1` (fire'sız satır gelmez); cevap süresi olay göndermez (yazılır); tutar
`/setnumber`; kaldırma `#remove-button-visible` altındaki düğmeye `/click`; kilit `/select index=N fire=1`; alt menü
§8.45. Kilit değerleri (`example.ltf`, "Negotiation Locks UI", FM-45573): 0 Negotiable, 1 Semi-Negotiable, 2
Non-Negotiable, 3 Remove & Exclude (yalnız ek ödeme/madde); 12 aylık taksite Remove & Exclude deyince oyun 6 aylığı da
ekleyip yasakladı. Oyun teklifi baştan reddederse ekran değil diyalog açar ("Cannot sign Orkun…") → `offer` söyler.
Kabulden sonrası: kulüp reddeder ("bid rejected": Negotiate / Interact with Board) ya da karşılık verir ("deal
negotiated": Accept / Withdraw / View Offer). ⚠ "Accept" kulübün kendi rakamını kabul eder (£3M → £8.25M). "offer
accepted" sonrası ilerle düğmesi "Negotiate Contract" olur; genel Continue'ya basmak zamanı ilerletip teklifi geri çeker.
Sözleşme (`fm contract`): mesajda "Enter Contract Negotiations" → önce vaatler ("Add Promise", Finalise Promises) sonra
şartlar (`…Contract Negotiation Contents Block.BlockRemapper0.negotiation`, `ContractEditSessionReference`: `Wage`,
`EndDate`, `AgentFee`, `LoyaltyBonus`, `ContractStatus` `{PropertyValue, DisplayValue}`); menajerin şartları Finalise
Deal, değişmiş şartlar Suggest Terms. ⚠ O sırada genel ilerle düğmesi de "Negotiate Contract" yazar (`continue-button`)
— etiketle arayan kod onu basarsa görüşme kapanır; yalnız `StandardButton`'lara bas. Defrel £160K, menajer £9,250 →
£7,500 kabul, imzalandı.

### 8.43 Kadro kaydı (`fm register`)
Yardımcının listesi kotayı tutmayınca oyun kilitleniyordu (UCL 3. ön eleme; Türkiye'de yetişmiş 6/8, kulüpte 2/4).
Ekran: `OpenPage("Registration")` bazen Squad Overview'da kalır → kutucuktaki turnuva düğmesi ("N assigned"ın önündeki
`OpenPanel`). Satır `TableRowNavigatable`: `StreamedTable+SelectionCell` (Unity `Toggle`, kayıt DEĞİL — tablo seçimi),
`SICheckBox #inputs-switch-toggle-large-default` (kayıt anahtarı, `SimulateCheckboxClick()`, `/regtoggle`), `#StatusFlags`
(HGN, HGC, Inj, Wnt, Trn), ad, yaş, mevki, statü. ⚠ Hücreye uydurma `ClickEvent` `FMCrashReporter.OnClick`'te patlar.
Confirm'e kadar oyuna yazılmaz, `Undo` geri alır. Okuma: `SICheckBox.Value` ilk tıklamaya kadar False → `Bindings.Get(
box.m_valueKey)`; ağaçtaki `squadreg.Selected` kaydırmadan kalan bayat satırları da tutar (aynı oyuncu iki kez); tablo sanal
(~27 satır) → `/regscroll?to=0..1` ile beş noktada oku, adla birleştir. Kural değerleri (`SquadSelectionRuleValue`,
`CurrentlyMeetsRule`) canlı. Düzenleme oturumu (`SquadRegistrationEditSession`) ekran kapanınca bitmez. Tıklama sonraki
karede işlenir (≤1 sn); arada kaydırılırsa başka oyuncuya gider → `Flip` kaydırmadan değer değişene kadar bekler; "dirty
repaint" hatasında yeniden dener. Gönderilmiş liste "Closes now" iken `CanConfirmChanges` kapalı (kurallar ✓ olsa da) →
`register --send` "oyun şu an değişiklik kabul etmiyor" deyip `Undo` eder. "Minimum of 8 homegrown" zorunluluk değil: eksik
her yetişmiş için bir yer boş kalırsa kural ✓ → `CurrentlyMeetsRule`'a bak.

### 8.44 Sözleşme kartı ve yenileme (`fm contract`)
`/offer` okuyucusu genelleşti; `/contract` aynı JSON (+ `Stage`, `Facts`). Yenileme: kendi oyuncunun değer düğmesi →
"Enter Contract Negotiations" → onay penceresi ("…without discussing this with their agent") → Yes. Kök
`Contract Negotiation Promises Block` ya da `… Contents Block`; satırlar `#frames-contract terms` (teklifte tireli),
`#primary-text`, `#secondary-text` (vaat açıklaması / oyunun itirazı: "Contract too short"). Değer `FMCombobox` ya da
`#inputs-combobox-default-default`; kilit `#lock-button-enabled` (şartlarda 0·1·2, vaatlerde ve "Entire offer"da 0·2;
"Entire offer" yalnız yazılır); kaldırma `#remove-button-enabled/visible` (`EventFmxRemoveAddOn`); "Add Promise",
"Add Bonus" (`EventFmxAddContractBonus`), "Add Clause" (`EventFmxAddContractClause`). Düğmeler: Finalise/Suggest
Promises, Negotiate Contract, Finalise Deal/Suggest Terms, Reset (`EventFmxResetTerms`), Use Existing Contract, Cancel
Negotiations (onay; iptal edilen oyuncuyla hemen yeniden görüşülemeyebilir — kural belli değil). Kenar panelleri (maaş
bütçesi etkisi, oyuncu raporu) ayrı; ⚠ alttaki sayfalarda da "Wage" var → ücret "Transfer Value"dan sonraki. Geçersiz
şart: `#warning-visible` içinde `ui-concepts-warning_small`, Suggest Terms kapanır; neden
`ContractEditSessionReference.<Bileşen>ComponentInfo.BaseComponentData.InvalidOrDisabledReason` (ör. menajer ücreti
"Above max allowed %"). Menajerin cevabı: kabul (kart aynı) ya da kendi şartları → kart önce/sonra okunup farklar yazılır;
aynı adlı iki prim ad + sırayla eşlenir. Yenilemede imza hemen olmaz ("I'll be in touch…"), karar günler sonra gelir.

### 8.45 Alt menüden seçmek (kupa/ödül primi, takasa oyuncu)
"Add" listesinin olayı (`EventFmxTransferOfferAddClause`, `m_dataToSendBinding = SelectedClause`) bağlı değeri gönderir;
üst seviye seçim oraya satırın `RowData`'sını (`{PropertyValue, DisplayValue}`) yazar, bizimki menü satırının kendisini
yazıyordu. Çözüm: yaprağın `RowData`'sı + olay (`/select … how=rowdata fire=1`, varsayılan). Yapraklar neyin primi
olduğunu taşır (`/choicedump?ptr=&path=7,0,0`: `Competition{Competition 130286, Stage 0}`; ödüllerde PropertyValue hep
1635214434 + ödül id). Takas yapraklarında `RowData` yok, kayıt kendisi yazılır (`{DisplayValue, PropertyValue:
{DatabaseReferenceField: <kişi id>}}`, `EventFmxTransferOfferAddExchangePlayer`). Adla: `/select names=A>B>C`,
`/menu?ptr=&names=A>B`. Açık listede her satır `DropdownRowNavigatable` + gizli `#ChildDropdown`. Takas satırı
`#exchange-player-row-enabled` (`#NameText`, mevki, Transfer/1–12 Month Loan/…). Sözleşmede "Wage After Reaching Club
League Games ›" da aynı yol ("Clause offers no wage increase" ücret girilince düzelir).

### 8.46 Kiralık teklifi (`offer --loan`)
`loan-offer-button`; ilk blok başlıksız: Playing/Unused Monthly Fee, Playing/Unused Wage Percentage (%0–200), Duration;
`#clause-area-title` iki kez: Loan Clauses (Agreed Playing Time, Preferred Position/Role, primler; Add: Loan To Buy Fee,
Buy Back Price) ve Loan Options (evet/hayır: cup matches, recall…); `#ltb-fees-title` Loan-to-buy Fees (Optional/
Mandatory Future Fee), `#ltb-additional-fees-title`, `#ltb-clauses-title`; toplam "Overall Cost if deal becomes
permanent". Gri seçenekler kayıtta `Disabled = True` (seçmek hiçbir şey eklemez) → `/offer`/`/contract` `Disabled` dizisi
verir, `/menu` listelemez.

### 8.47 Diziliş: taktik yuvaları
Var olan taktiğin dizilişini değiştiren kontrol yok (`FormationOptionEvent` hiçbir kontrolde; `FormationEditCard` adla
açılamadı). Oyunun yolu yeni taktik: ikinci liste (`ModifyTactic`, `add-data`, `IsAddingSlot = True`) "Create New
Tactic" sihirbazı → yeni yuva, etkin olur. En fazla 3 yuva (`TacticReference.TacticCount`, `CurrentTacticSlot` 0'dan);
doluyken "Create New Tactic" kalkar. Etkin olmayan yuvalar numaralı düğmeler (`EventFmxSetTacticSlot`, `slot1`/`slot2`)
— `/press` değiştirmez, `/click` değiştirir. Ana listede başka taktik adını seçmek onu etkin yuvaya KOPYALAR. `fm tactic
new` (yeni yuva), `fm tactic slot [<n>]`.

### 8.48 Takım talimatları
"Team Instructions" (`TutorialActionElement`) görünümü açar; evre başlıkları `#PhaseText` (Overview, Final Third,
Progression, Buildup), talimat hücresi adı `#text`, ayarı `#text-second` (`/instructions`). Ayar kartını açan yalnız
`CellButton` sağ tık listesi (`RightClickEvents → OpenGenericDynamicCard`; `/press?ptr=&list=RightClickEvents`) ya da
`layout-tile-interaction-button` (Overview'daki 7); sol tık başka menü. Kartta `options-list` radyo grubu
(`/select name=options-list choice=`), hemen işler; kart açıkken sıradaki açılmaz → kartın `PanelHistoryClose`'u; kart
kapanınca hücre adresleri değişir. "Final Third/Dribbling" = "Progression/Dribbling" (tek ayar). 16/16.

### 8.49 Rakip talimatları (`fm opposition`)
"Opposition Instructions" (`OpenCardButton`; yeni açılan sayfada ilk basış bazen cevapsız). Rakip oyuncu başına `#NameText`
+ dört adsız `SIDropdown` bu sırayla: `Marking` (Never/Always), `PressingTrigger`, `Tackling` (Normal/Easy/Hard),
`ShowOnToFoot` (Left/Right/Weaker Foot); "-" ayarsız (sıra yardımcı önerisini veriyle karşılaştırarak bulundu). Listeler
olay göndermez, yazılır. "Apply all advice"/"Confirm" `StandardButton`; "Clear Instructions"
`EventFmxOppositionInstructionsClear`. `--advice`, `--clear`, `opposition Kaku Pres=Always "Sert giriş=Easy"`.

### 8.50 Antrenman yoğunluğu
"Training" alt sekme listesi (`SecondaryTabDropdownButton`), "Training Intensity" `/select … fire=1`. Üstte beş takım
kuralı (`EventFmxSetTrainingIntensity`); oyuncu başına liste (`dropdown-base-regular-trailing-icon`,
`EventFmxPlayerTrainingIntensityLevelChanged`): Automatic, Half/Normal/Double, "No pitch or gym work ›" (1–6 Days, 1–2
Weeks), "Send On Holiday ›" (bazı oyuncularda).
"Ask <fitness koçu> to set all training intensities" görüşme düğmesi var. Tablo sanal (~15 satır): `/namedrows?dropdown=<ad>
[&to=0..1]` her `#NameText`'i ardından gelen listeyle eşler ve kaydırır; beş noktada okunup adla birleşir (21 oyuncu).
`fm intensity [<oyuncu> <seviye>] [--auto]`.

### 8.51 Bireysel antrenman
"Training › Individual Training", görünüm "List": satır başına dört liste — mevki/rol eğitimi
(`EventFmxPlayerTrainingPositionChanged`; "AM (R) - Playing position" ya da "Defender (Right) › …"), topsuz rol
(`EventFmxPlayerOOPTrainingRoleChanged`), ek odak (`EventFmxPlayerTrainingFocusChanged`: Endurance, Quickness, Free Kick
Taking…), yoğunluk. İlk üçü aynı eleman adı → `/namedrows?count=3`. "Ask <yardımcı> to assign all individual training" var.

### 8.52 Oynanarak öğrenilenler (5–6 Ağustos 2025)
Anında simüle edilen maçtan sonra `State = Results` (ilerle "Response Required"), `/pages` boş (menüler kurulmamış) →
`OpenPage` listeyi 5 sn bekler. Basın `reply <#> Attend Press Conference` + `talk`. Sezon kaptanı mesajı: "Confirm Captains"
kaptan boşken diyalog açar; "Review Captaincy" mesajda `GenericDropdown`'lar (Select Captain / Change Vice Captain), seçim
bir onay daha (Yes). Kiralık karşı teklifi "View Offer" ile açılır, başlıkta takas bölümü yok (`/offer` kökü başlıktan da
bulunur). Rakip önerisinde "Apply all advice" birkaç yüz ms sonra gelir; uygulanırken tablo bir an eksik.

### 8.53 Oyuncu talimatları (`fm tactic pi`)
Rol listesinde "Go to Player Instructions" (`fire=1`) kartı açar; talimatlar döşeme (`layout-tile-interaction-button`):
adı `#title-text`, ayarı `#name`, `#leftarrowbutton`/`#rightarrowbutton`; döşemeye basmak adsız radyo grubu + açıklama
kartı açar, `/select` kalıcı yazar; kartlar `PanelHistoryClose` ile kapanır (açık kalırsa rol listeleri görünmez). "Apply
to: Position / Player" (`EventFmxDisable/EnablePositionPersonalisedRole`). `/tiles` döşemeleri okur.

### 8.54 Maç içinde mentalite ve bağırış
Maç sırasında sayfa kataloğu yok. Maç ekranı kontrolleri: "Tactics" (`MatchOpenTactics`), "Instructions", bağırış
(`shouts-dropdown`, `TouchlineShoutMenu`), mentalite (`dropdown-base-regular`), oyuncu menüleri. Mentalite olay göndermez;
indeksle seçmek `Selected = -1` yaptı; satır kaydını bağlı değere yazmak (`/select path=N`, rowdata) işledi
(`..MatchUI2D.Tactic.MentalityString`). Bağırış satırı `{Title, Description, TouchlineShoutAction, TouchlineShoutType}`,
seçim olay gönderir (doğrulama §8.62). Lig maçından önce "Defrel is ineligible" → Quick Pick düzeltti.

### 8.55 Transfer dönemi; Combined görünüm; MCP gerçek istemci
`Human.Team.Division.TransferWindowData` (`TransferWindowReference`): `TransferWindowStatus` (1 = açık),
`TransferWindowDaysRemaining`, `StartDate`, `EndDate`, giriş/çıkış sayı ve değerleri; `status` "Transfer: dönem açık, 33 gün
kaldı". ⚠ Saha on bir slotu yalnız "Combined" görünümde dizer (In/Out of Possession/Both'ta `Slots = []`) → `Planner()`
slotlar boşsa "Combined"a basar. MCP Claude Code'dan denendi, çıktı CLI'la aynı; ama `fm mcp` oturum başındaki derlemeyi
tutar → yeni derlemeden sonra istemcide `/mcp`.

### 8.56 Kısa liste
"Shortlists" alt sekmesi personelle karışık genel sayfa; oyuncu kısa listesi Recruitment › Overview'daki "Shortlists"
döşemesinden, tablosu `/people` ile. Teklif verilenler otomatik eklenir. `fm shortlist`.

### 8.57 Rakip raporu
Match Day › "Next Opposition Report" döşemeleri düz metin (Form, Most Used Formation, Top Goalscorer…, Team News). Genel
yol: `/texts?anchor=<metin>&up=N` metni bulup N üst elemana çıkar, altındaki görünen metinleri okuma sırasıyla verir.

### 8.58 Kaptanlar
`Human.Team.Captain` / `ViceCaptain` sayfasız okunur. Squad › Dynamics › Captains: iki `GenericDropdown` ("Change Captain"
/ boşken "Select Captain", "Select Vice Captain"); seçim olay + onay diyaloğu (Yes); seçili "(Selected)" ekli. ⚠ Yardımcı
atandıktan sonra sayfa yalnız kaptan listesini kurar (yardımcıyı değiştiren liste yok; günde bir kez, §8.134).
`fm captains [--captain X] [--vice Y]`.

### 8.59 Oyuncu mutluluğu
Squad › Dynamics › "Happiness": tablo okuyucuları bayat veri verir → düz metin (`/texts`, mutluluk seviyesinden 12 üst): ad,
"N year-old …", seviye (Very Unhappy…Very Happy), durum bayrakları (Wnt, Unr, FtT, Rst); üstte takımın genel cümlesi.

### 8.60 Maç hazırlığı (okuma)
Match Day › "Match Preparation Training" kartı: taktik hazırlık (topla/topsuz) ve duran top hazırlığı, açıklamalarıyla;
`/texts`, `fm prep`. Kartta kontrol yok (değiştirmek §8.130). ⚠ Duran top sırası sabit değil → ad başına göre hücum/savunma.

### 8.61 Oyun ayarları: Preferences (`fm settings`)
Üst çubuktaki etiketsiz `dashboard-hud-fm-button` FM menüsünü açar; "Preferences" döşemesi ("Manage settings for this
game") özet sayfa (Region, Interface, Saves, Sounds); "See all" sekmeli tüm tercihler (Your World, Audio & Visual,
Accessibility, Match Day, Processing, Third Party, Shortcuts; `DetailedPreferencesTabButton`). Bu katman `/buttons`'ta yok →
`/click?text=<yazı>[&name=<düğme>]` metnin üstündeki ilk `SIButton`'a basar. ⚠ "Match Day" hem sekme hem ana menü →
`name=DetailedPreferencesTabButton`. Okuma `/form?anchor=&up=N`: metinler + kontroller okuma sırasıyla (`dropdown`,
`radio`, `slider` Value/Low/High, `checkbox`); ayarın kontrolü başlığının altındaki ilk kontrol; "Every"/"Auto save type"
iki liste sundukları seçenekle ayrılır; sekme birkaç karede dolar. Değiştirmek: liste/radyo `/select` (yerel değişkene
yazar), `SISlider.value = n` (adım indeksi), `SICheckBox.SimulateCheckboxClick` (`/setcontrol?ptr=&value=`); hiçbiri tek
başına işlemez → "Confirm" (`PreferencesConfirmChangesEvent`) şart; çarpı `CancelPreferenceChangesEvent`. Ayarlar kayda
değil oyunun tercih dosyasına. Kaydırıcı adımları: vurgu sırasında 0.5x…10x (0–10, varsayılan 1.5x), arası 4x…40x (0–5,
varsayılan 30x); `HighlightModes`: Full Match, Comprehensive, Extended, Key, Only Commentary.
`Game.AutosaveModeAndIntervalString` `/pull` ile bir kez bağlanınca güncellenmiyor → doğrulamak için sayfayı yeniden oku.
Para birimi Euro'ya alınınca `player` € verdi.

### 8.62 Maç ekranı: bağırış izi, rol, Dugout, saha
Bağırış seçilince liste kapanır (`#shouts-enabled DISABLED`, `Enabled = False`) ve birkaç oyun dakikası sonra açılır;
anlatım da söyler → `shout` kapalı listeye basmaz, kapanmayı görmeden "oldu" demez. Rol: oyuncu menüsünde iki rol satırı
(topla/topsuz), `/select path=3,k fire=1`, etiket hemen değişir; maç içi rol kayıtlı taktiğe yazılmaz. Dugout: sol
döşeme, tavsiyeler dakikalı düğmeler (`DugoutEvent0…5`, `EventFmxMatchDugoutJumpToEvent`), metin `#text`, "Do it"
(`…ConfirmEvent`), kaldır (`…DismissEvent`), önceki/sonraki. Uygulanan tavsiyede "Do it" kapalı, kaldırılan düşer;
eylemsiz yorumlarda/skor kartlarında baştan kapalı; "No Dugout Events" yer tutucusu. ⚠ Döşeme pozisyon oynatılırken
gizlenir (gizliyken düğmeler listelenmez); çizelge hızla kayar (numara iki komut arasında değişebilir).
`MatchDugoutEventCount`/`MatchDugoutTimelineEvent0..5.Text` var, maç referansının yolu bulunamadı. Saat donabilir
(korner sonrası 45 sn+) → anlatım değişiyorsa maç sürüyor sayılır. Kalecinin menüsünde kulübe alt menüsü yok → kulübe dolu
olan ilk menüden. Saha: her oyuncu menüsünden önce mevkisini yazan düğme ("GK", "D (R)", …; `OpenDescendantDropdown`); her
hat sağdan sola (DR, DCR, DCL, DL); `sub` on biri bu mevkilere göre çizer. `fm dugout [<no> uygula|kaldır]`, maç içi
`tactic role <oyuncu> [<rol>] [--oop]`, `match --play` akışında "Yardımcı: …".

### 8.63 Oyuncunun kariyeri (`player --career`)
"Career Stats" döşemesi; `/texts?anchor=Total Apps&up=16`: başlık (Sel., Year, Team, Info, Nation, Division, Total Apps,
Goals | kalecide Cln Sh, Ast, PoM, Avg Rat), satırlar, "Total" + toplamlar. Satır uzunluğu değişken ("Info" yalnız gelişte:
£19.5M, Loan, Free, Undisc.); sabit son yedi hücre; yıl "2025/26" ya da "2020". ⚠ Bazı oyuncularda "Total" genişliksiz
çizilir, çıkmaz → toplamlar son beş sayı. Tablo birkaç karede dolar → iki ardışık okuma aynı olunca al.

### 8.64 Topsuz talimatlar: bir deneme (sonra §8.132'de çözüldü)
Taktik sayfası kökündeki `BindingSetter #InPossessionSetter` kapsamında `inpossession` değişkeni; `/scoped?ptr=&path=
[&from=const.False]` (`PathKey.Of(root, path)`) yazdı ama talimat görünümü değişmedi.

### 8.65 Bütçeler ve transfer ↔ maaş kaydırıcısı (`fm budget`)
Recruitment › Recruitment Budgets döşemeleri düz metin (Transfer Budget, Wage Budget, Scouting). "Adjust Transfer and Wage
Budget" (`SpeakToAction`) → "Budget Adjustment" kartı: kurulun cümlesi, iki bütçe, `SISlider` 0–15, Cancel / "Got It".
`/setcontrol` sonrası rakamlar 1-2 kare sonra güncellenir, yürürlükteki adımdan farklıysa sonda `*` (bayat okumayı ayırır).
Adımlar doğrusal (~£738K transfer ↔ ~£14.2K/hf maaş); kart sayfada iki kopya. `fm budget ayarla [<adım>]`.

### 8.66 Takvim ve bütün fikstür (`fm calendar`, `fixtures --all`)
Fixture Schedule ~10 maç (`/scroll` metnin listesini `to=0..1` kaydırır). Sezonun tamamı Portal › Calendar: `MonthDropdown`
bir ayı ızgara çizer; `/texts?anchor=<Ay Yıl>&up=14`. ⚠ Ay seçici sayfa kapanınca da yüklü kalabilir. Gün: "28 Jul" ya da
"29"; "Training", maç ("Match", turnuva, saat/skor, rakip, lig sırası "7th", H/A/N), altyapı maçı ("U19", "FR"), notlar
(transfer dönemi, kupa, milli maçlar). Skor ev–deplasman ("2-4" deplasmanda 4-2), penaltılı "1-1p". Izgara birkaç karede →
iki ardışık okuma; seçilen ay bazen tutmaz, yeniden seçilir. `fixtures --all` 14 ay ~14 sn, 44 maç.

### 8.67 Gelen transfer teklifi; maç sonrası soru
"X make Y offer": Accept, Reject, View Offer, Delegate. "View Offer" bizim kartla aynı yapı (kontroller `Enabled = False`);
tek konuşma seçeneği "Accept Offer"; reddetmek `reply <#> Reject`. `fm offer` argümansız ekrandaki teklifi çizer. Maç
sonrası "Match Reaction": `CanContinue` derken konuşma açık kalır; Continue onu `EventFmxPressConferenceEarlyExit` ile
geçer → `status` soruyu söyler, `next` durur (`--skip` geçer).

### 8.68 Liste okumasında bayat satırlar (`?gen=`)
Rapor golleri önceki maçın dakikalarıyla verdi: `/pull?list=1` aynı `fmcli.<liste>.<n>` düğümünü yeniden kullanınca
altındaki `TimeString` eski kalıyordu. Artık her okuma `?gen=` ile kendi satırlarını açar (bir okumanın denemeleri boyunca
aynı nesil), her kayıt kendi satır yolunu (`Row`) taşır, iç içe okuma oradan. ⚠ Bedeli: düğümler silinmez, çok uzun
oturumda ağaç büyür.

### 8.69 Sakatlık durağı; penaltı golü, VAR
Sakatlıkta oyun bazen taktik ekranını açmaz: maç ekranında durur (`/state`'te `Playing` yok, saat durur, pencere yok),
Dugout'a not düşer, "Confirm Changes" (`button-primary-leading-icon-label`) belirir. Durak = değişiklik → Confirm Changes
(4 sn içinde `Playing = True`). `match --play` durağı tanır (Playing yok + Confirm Changes), terminalde sorar; `sub` durakta
Confirm Changes'e kendisi basar. Olay listesinde penaltı golü `EventType = 2`, normal gol 0. VAR iptali skor 3-3 → 2-3
("GOL İPTAL (VAR)"). Skor kartları çok satırlı gelebilir → süzgeç satır sonlarını da kapsar.

### 8.70 Turnuva liderleri (`fm league`)
Match Day menüsündeki "Competitions ›" kulübün turnuvaları (sıra değişir → adla: `/select names=Competitions>Super League
&fire=1`, alt menü işaretsiz ad). Turnuva sayfasının "Stats" döşemesi düz metin: Teams (Most Goals, Most Possession…) ve
Players (Highest Average Rating, Most Goals, Most Assists, Most Clean Sheets). Yeni turnuvada isim boş olabilir; turnuva
değişince önceki döşeme bir an kalır. (Tam liste §8.129.)

### 8.71 Kadro sağlığı (`fm health`)
"Injuries" görünümü: `IsInjured`, `InjuriesTimeMissed`, ad, derece (sakat olmayanlarda geçmiş süre). "Risk Assessment":
`OverallInjuryRisk` basamaklı (75, 92, 150, 165, 275, 385), `InjurySusceptibility`, `MatchLoad`, `Load`. `health` sakatları,
kondisyonu %85, maç formu %60 altını ve en yüksek risk basamağını verir.

### 8.72 Sözleşme takvimi (`fm contracts`)
Contract görünümü bitiş tarihini bağlamaz; `Human.Team.Players` satırlarının `Row`'u altında `FullContract.EndDateDescription`
(~1,4 sn, 28 oyuncu). Bir yıldan az kırmızı, iki yıldan az sarı.

### 8.73 Antrenman programı (okuma, `fm schedule`)
Squad › Training › Overview'daki "Training" kutusu bu ve gelecek haftayı çizer. ⚠ Alt sekme menüsündeki "Overview" Youth
Setup / Dynamics / Medical Centre'da da var → `/dropdowns?json=1`'den Label "Training" olanın işaretçisi; Squad'ın kendi
Overview'u açık kalabilir → seçim her seferinde yeniden gönderilir. `/texts?anchor=Thur&up=3` (`#Calendar`): gün başlıkları,
sonra gün + üç seans (seans adı ya da maç günü yer H/A/N).

### 8.74 Antrenman raporu
"Training Rating" döşemesi oyuncunun antrenman sayfasını açar; "Request Training Report" listesi (`GenericDropdown`,
`SpeakToAction`; seçenekler antrenörler) → mesaj hemen gelir ("Fred's training report"). ⚠ Cevap ekranda kalır, liste
"Acknowledge and Move On"a kadar yok. ⚠ Gelen kutusu bağlı değilken okumak (`Bound`) Portal'ı açıp sayfayı kapatır → önce
oku. Ek "Progress Report" (`/texts?anchor=Attachments&up=2`): gidişat, özellik/değer çiftleri, "Training" puanı ("4.70"),
seviye (simge `scaling-icons-condition-level-…`, yük olduğu tahmin), birim. Gövdede oyuncu adı `<link>` içinde → eklenti
görünen metinle de eşler. Kendi oyuncumuz aramada yoksa First Team'deki `NameButton`'dan (`any=1`).

### 8.75 Alışkanlık öğretme
Antrenman sayfası › "Traits" döşemesi: "Start New Trait Training" (`GenericDropdown`; kategoriler Movement, Passing,
Finishing, Defending, Technique alt menülü + "Discipline Training"; yalnız öğrenebilecekleri; `/select names=Kategori>
Alışkanlık&fire=1`), "Remove Trait", "Ask For Trait Training Suggestion"; çalışırken yalnız "Cancel Current Trait Training".
Yardımcının görüşü onay penceresi ("Start training"/"Don't start training"; "Too Old To Change Game", "Really Unsuitable
To Add/Remove"); cevap ekranda kalır → "Acknowledge and Move On". Çalışılan alışkanlık ekranda yazmaz
(`PlayerTrainingReference.Traits → PlayerTraitsReference.TraitInDevelopment` okunamadı).

### 8.76 Serbest bırakma ve karşılıklı fesih (`fm release`)
Oyuncu sayfasındaki maaş düğmesi (`ContractDetails`) sözleşme bölümü: "Discuss New Contract With Agent", "Enter Contract
Negotiations", "DoF To Negotiate Contract", "Release" listesi ("Release On A Free", "Mutual Termination"). Release On A Free
→ `/dialog` "Release Fred?" (tazminat, transfer bütçesinden; Yes/No). ⚠ Mutual Termination onay sormaz. Liste sayfa
otururken yeniden kurulur. `--free` `--yes`'siz "No" der, `--mutual` `--yes`'siz göndermez. "Discuss New Contract With
Agent" menajerle konuşma (dört ilgi derecesi, "End Chat") → `contract <oyuncu> --agent`.

### 8.77 Taktik kopyalama ve yuva boşaltma
Etiketsiz iki menü (`dropdown-base-no-background`): yuvanın taktiği ("Primary Trained Tactic · Edit Tactic · Presets... ›
· Save · Load... · Manage Tactics... · Clear Slot") ve yuva doldurma ("Create New Tactic · Load Tactic · Copy Tactic ›").
`/select names=Copy Tactic>…` yeni yuvaya "Copy of: …" kopyalar, etkin yapar; "Clear Slot" → `/dialog` "Are you sure…" →
Confirm. (Dosyaya Save/Load §8.107.)

### 8.78 Kulüp: genel bakış, personel, finans (`fm club/staff/finances`)
Club › Overview kutuları tek `/texts` okumasında (`anchor=Overall Balance&up=18`): Club Vision (hedef · Required|Desired|
Preferred · On Course|Reserving Judgement|…), Board, Finances, Supporters, Affiliates, Facilities, Job Centre Report,
Performance Feedback (Board notu "C" + hava), Club Site, Transfer History, Staff ("dolu / yer", "N vacant roles"),
Responsibilities; "Board"/"Supporters" Performance Feedback içinde yeniden geçer → kutu sınırı sonraki başlıklardan.
Personel: subtab "Staff" yalnız `StaffReportCard`; kişi listesi Staff kutusunun döşemesinde (`ClubAllStaffC`;
`/click?text=Coaching Team`); "All Staff" sekmesi 9 kolon (Name, Team, Club Job, Age, Nationality, F/PT, Type, Wage,
Expiring), ~27 satır çizilir → `/scroll?anchor=Senior Squad&to=0..1` beş nokta (45 kişi). Finans: bakiye kutusu
(`/click?text=Overall Balance`) kartı açar (`anchor=Remaining Debt&up=16`): Income, FFP, Debts And Loans, Wages + "Player
types and their total basic wage", Sponsorship, Projections (zarar parantezli), Expenditure, Summary, Recruitment Budgets.
⚠ `/open` ara sıra "VisualElements cannot be added to an active visual tree during generateVisualContent…" (olay çizim
anına denk gelince) → hemen tekrar geçer; CLI `OpenBookmark` üç kez dener.

### 8.79 Gelir/gider, sponsorlar, taraftarlar, tesisler, yönetim kartı
Hepsi Club › Overview ya da finans kartı kutularından (`layout-tile-interaction-button`): Sponsorlar
`/click?text=Total Sponsorships income` (`anchor=Value (Per Season)&up=14`; Type · Value · Until · Duration · Total, beşer
hücre). Gelir `/click?text=Turnover`, gider `/click?text=This Month` (gelirinki küçük m'li) → dört dönem sekmesi
(`ThisMonthTab`, `LastMonthTab`, `ThisSeasonTab`, `LastSeasonTab`), Item · Total (gelir 17, gider 21 kalem); liste
sanal → çapayla 0/0,5/1 kaydır; ⚠ kart son bırakılan sekmede açılır. Taraftarlar `/click?text=Supporter influence on the
board` (`anchor=Supporter Profile&up=12`): profil, Season Ticket Holders, takipçiler, seyirci, bekleme listesi, beklenti
tablosu (metin · Required/Desired/Preferred · tür · Pleased/Satisfied/…), rakipler, Matches Feedback; ⚠ Type kolonu
birkaç kare sonra çizilir. Tesisler `/click?text=Facilities` (`anchor=Under-Soil Heating&up=16`): About Club, Stadium
(Name, City, Rent, Capacity, Year Built, Owned By, ısıtma, çatı, zemin, saha ölçüleri, durumu), Corporate, Training, Youth
("Name" iki kez → bölüm başlığından sonra ara). Yönetim `/click?text=1 board member`: üyeler, talep türleri (Build New
Stadium, Facilities, Finance, Networking, Staff, New Contract), Board Requests tablosu.

### 8.80 Taktik akıcılığı, mevki puanları, sakatlık geçmişi, sezon istatistikleri
Akıcılık 10000 üzerinden: takım `Tactic.TacticalFamiliarity` (9089 → %91); oyuncu taktik ekranının alt tablosunda
`…binding.Player.TacticalFamiliarity.AverageFamiliarity` (`PlayerTacticalFamiliarityReference`; `/pull` boş, değer satırın
mevki bağlamına bağlı; yalnız on bir bağlanır; ayrıntı `TacticalFamiliarityDetails` ipucunda, okunmadı). Mevki puanları
`PersonReference.PositionalAbilities` 15 elemanlı (`DisplayValue` "DM"…, `PropertyValue` 1–20; ikinci eleman adsız).
Sakatlık geçmişi `InjuriesHistory` → `PlayerInjuryReference` (`InjuryDescription`, `InjuryDate`, `InjuryDurationDays`,
`IsActive`), özet `InjuriesThisSeason/LastSeason`, `InjuriesPercentageMissedString`. Kadro dışı oyuncu: `/player` sayfanın
`Person`'ını `fmcli.card.p<isim karması>`'na koyup `Path` verir (oyuncu başına ayrı yol, §8.68). Aynı karede bağlanan liste
"is not a list" → `Fm.Pull` tekrar dener. Sezon istatistikleri ekransız: `ClubAppearances/Goals/Assists/AverageRating
ThisSeason`, `StartingAppearances`, `SubAppearances`, `Minutes`, `YellowCards`, `RedCards` (noktalı prop
`Minutes.PropertyValue` çalışır); ⚠ "Club" oyuncunun şu anki kulübü (kiralıktakinin rakamı kiralık kulübünki; `LoanClub.Name`).
Yarışlar: oyuncu sayfasında ad özelliklerden önce bağlanır (özellikler + kariyer gelene kadar ≤4 sn bekle); arama kutusu
yeni açılınca önceki sonuçlar geç belirir (liste durulunca al).

### 8.81 Yönetim/taraftar güveni, bilet; parklar
`Human.Team.TeamConfidence` (`TeamConfidenceReference`, 69 özellik): her konunun cümlesi + notu (`BoardConfidenceOverall
String/Grade`, `BoardMatch…`, `BoardSquadConfidence…`, `BoardTactics…`, `BoardTransferConfidenceOverallSummaryText`,
`BoardVision…`, `BoardCultureProgressString`, `InternationalCompConfidenceStringLong`; taraftar `Supporter…`);
`BoardFeedbackHighlights/Criticism` yazı listeleri (`/pathlist`, `Fm.Strings`). Notların ölçeği doğrulanmadı → yalnız
cümleler. `TeamReference.JobSecurityStatus` = 4, karşılığı bilinmiyor. Bilet: `Human.Team.Club.MatchTicketPrice`,
`SeasonTicketPrice`, `SeasonTicketHolders` (sterlin). Kulüp değeri yalnız `FinanceDebugClubValue` (boş). Parklar (sonra
çözüldü): tam gol listesi (§8.129), başka ligler (§8.105), scout tavsiyeleri.

### 8.82 Kısaltmalar, tamamlama, `fm doctor`; sakatlık süresi
`InjuriesTimeMissed` kaçırılan süre, kalan değil; kalan `CurrentInjury.InjuryReturnDays` (ekransız). `fm doctor` tamamen
`/pull` (~1,8 sn); mutluluk `/pull`'la gelmez. Tamamlama açıklamaları `HelpText`'ten; macOS bash 3.2 `source <(…)` ile
tanımlamaz → `eval "$(fm completion bash)"`; zsh adları `fm __names`'ten. ⚠ Oyun bir kez kendiliğinden çöktü (SIGABRT,
`__cxa_rethrow`, `health` görünüm değiştirirken); tekrarlamadı.

### 8.83 `--json` her komutta; taktik ekranının iki görünümü
`JsonOut.Of` tuple alanlarını yazar (varsayılan `(string,string)`'i `{}` basıyordu), Türkçe harfleri kaçırmaz; `--json`'da
`Ui.Rich`/`Ui.Interactive` kapalı. `league` lider kutusunda etiketler adlardan önce gelir → her liderin adı ve değeri gelene
kadar (~4 sn) bekle. Taktik ekranı "Team Shape" / "Team Instructions" (`ButtonGroup #FMShapeAndInstructionsButtons`);
talimat görünümünde saha yok (`Slots = []`); `#team-shape` `/press`'te yok → `/click?text=Team Shape&name=team-shape`;
`Planner()` bunu yapar. Sahaya dönünce ilk "Go to Player Instructions" seçimi ve sayfa açıldıktan 0,5 sn sonraki "Team
Instructions" basışı yutulabilir → yeniden dener.

### 8.84 Maç maç puanlar (`player --matches`)
`PastMatches`/`PastMatchesRatings` `pathlist` boş; `LastFiveMatchesForm` `/pull`'da boş. Form kutusu ("Last 5 Club Games")
yalnız gerçek tıklamayla (`/click?text=Last 5 Club Games&name=layout-tile-interaction-button`) Match Ratings sayfasını açar;
`/texts?anchor=All Competitions&up=10`: özet (Apps, Max/Min/Average Rating, Goals, Assists), kolonlar (`#, Date, Pos., Role,
Matches, Score, Inf, xG, Ast, Gls, Rating`); `Inf` yalnız girip çıkanlarda ("on 75"); kalecide `xGA, Conc, CS` (CS ikon);
skor ev sahibi önce; oynamadığı maçlar "-" ve 0.00. Liste sanal (19 satır) → `/scroll?anchor=<tarih>&to=0.5|1`. Aynı gün:
`--csv/--md`, `fm export`, `fm diff`, `fm compare` (ikinci sayfa yarım okunuyordu → özellik sayısı iki okumada aynı kalınca),
yazım hatası toleransı.

### 8.85 Gizli özellikler ve potansiyel
`/pull` `PerceivedPotentialAbility` verir (200 üzerinden; kulüp personelinin gördüğü, gerçek PA değil). Gelmeyenler:
`PlayerCurrentAbility`, `PlayerPotentialAbility`, `DebugCurrentAbility`, `…PotentialAbilityStarRange.Min/MaxValue`; kişilik
özellikleri (`AttributeAmbition`…) oyuncuda 0 (modelde `IAgentReference`/`INonPlayerReference`'ta); tutarlılık vb. gizli
özelliklerin adı modelde yok; kişilik yalnız `Personality` metni. `AbilityProgressHistory` dört `DynamicNumber`, ölçeği belirsiz.

### 8.86 `/squad` birleşmede boş kalan alan
Aynı oyuncu iki listedeyken ilk kayıt bir alanı boş tutuyorsa öbürünün dolu değeri `TryAdd` yüzünden yazılmıyordu → artık
boş alan doluyla dolar. Musaba hatasının bu olduğu doğrulanmadı.

### 8.87 Penaltı sırası (okuma, kurallar)
Match Day › Penalties: `/form?anchor=Penalty Takers&up=12` sıra (Order, no, ad, "Starting at STC", Composure, Penalty Taking)
+ üç kural `SICheckBox`; `/setcontrol?ptr=&value=true|false` kalıcı. Satırlar `FMDraggableObject`, tablo `StreamedTable`
(`DragEvents → EventFmxSetPiecePenaltyCandidatesMove`, `RemoveEvents → …Remove`), "Add Player" (`…Add`), ayrıca
`EventFmxSetPieceSwapPenaltyCandidates` (değiştirmek §8.90).

### 8.88–8.89 Antrenman programını değiştirmek (`fm schedule set/style`)
Training Responsibility menajerde ("Leads General Training"). Overview takvimi yalnız özet (hücreler `DynamicItem DISABLED`).
Katalog: `EventFmxConfirmTrainingSession`, `…ConfirmTrainingSessionFromMenu`, `…CreateCustomTrainingSchedule`,
`…SaveCustomTrainingSchedule`, `…ConfirmTrainingSchedule`, `…CancelTrainingSchedule`, `…DeleteCustomTrainingSchedule`; veri
`CustomTrainingScheduleReference`, `CalendarWeekReference.CanEditTrainingSchedule`, `SessionType1..3`. Düzenleyen sayfa
Portal › Calendar'ın "Training" sekmesi (General / Training / Fixtures; sekme düğmeleri `#navigation-tab-default-default`
olay taşımaz → `/click?ptr=`; sayfa açılır açılmaz tıklama boşa gidebilir; her sekmenin kendi `MonthDropdown`'ı, ay sekmeden
sonra seçilir). Izgara: hafta kartı `#TrainingFocusCard` (program adı + tür), günde üç düğme (`FMCalendarEvent
#Training_TrainingSessionDynamicItem`, `DropTarget #View` içinde); maç günü ortadaki maçın kendisi; geçmiş, maç ve "Travel"
kapalı. `/form?anchor=Monday&up=18&buttons=1` (düğme de öğe: `Text`, `Texts`). Seans penceresi: adsız kategori listesi
(General, Match Preparation, Attacking, Defending, Technical, Tactical, Goalkeeping, Set Pieces, Physical,
Extra-Curricular; `…trainingsessioncategory`, `fire=1`), seanslar (`SetDatastoreValue` düğmeleri), anlatım (Primary/
Secondary/Tertiary Focus, Impacts, Priority %, Attributes), Cancel/Confirm (`EventFmxConfirmTrainingSession`) → hafta adı
"(Modified)". Seanslar güne göre değişir → katalog `~/.config/fm-cli/training-sessions.json`, yoksa yeniden okunur; adlar iç
içe ("Rest" ⊂ "Transition - Restrict") → önce tam eşleşme; etiketsiz düğme atlanır. Hafta penceresi: tür listesi
(`…trainingschedulecategory`: "Pre-Season ›" Early/Mid/Late, Tactical Style, Scenario, Training Style), programlar (Training
Style: Defence, Possession, Attack, Physical, Technical, Set Piece, Tactical, Balanced; Tactical Style: Route One, Gegenpress,
Tiki-Taka, …, Clean Slate; Scenario: Boot Camp, Full Week of Rest; Pre-Season: Light, Normal, Heavy, Tactical, Technical),
başlık ("Tactical - No matches"), etki çubukları (okunmadı), "Create New Schedule" (denenmedi), Confirm
(`EventFmxConfirmTrainingSchedule`). Aynı programı yeniden uygulamak "(Modified)"i kaldırır (şablon o anki fikstüre göre
yeniden kurulabilir). İki pencerede aynı etiketli kontroller ("Tactical") → `/press?text=…&event=SetDatastoreValue` ya da
işaretçi.

### 8.90 Penaltı sırasını değiştirmek: sürüklenen tablo
`StreamedTable` bırakılınca `OnMoveItemInternal(int from, int to)` (taşınanı `m_dragKey` "itemmoved"e yazar, `DragEvents`'i
gönderir), çıkarmak `OnRemoveItemInternal(int index)` (`m_removeKey`, `RemoveEvents`) → `/rowmove?ptr=&from=&to=`,
`/rowremove?ptr=&index=` (0'dan). Tablo işaretçisi `/buttons?json=1` (`Type` "StreamedTable"). "Add Player" seçeneklerinde
`DisplayValue` `FM.UI.PersonReference`, `PropertyValue` kişinin `Index`'i → köprü `#<Index>` diye adlandırır; ad
`/pull?path=Human.Team.Players&props=Name,Index&list=1`; `/select?choice=%23<Index>&fire=1` sona ekler. Kalıcı ✓. Tablo satır
satır yeniden çizilir → bütün hücreler iki okumada aynı kalana kadar bekle. `OnAddItemInternal(object, int)` kullanılmadı.

### 8.91 Duran top atıcıları (`fm takers`)
Set Pieces bir seferde tek tür; tür listesi (ilk seçeneği "Corners"): Corners, Free Kicks › (Shooting Range, Wide, Deep),
Throw-Ins › (Defensive/Middle/Attacking Third); "Set Piece Type" yazısının yanındaki metin açık türü söyler. Atıcı düğmesi
(`taker-button`; taçta `taker-picker-button`) iki liste açar (Right/Left Footers; taçta Right/Left Side), ikisi de
`StreamedTable` (`EventFmxSetPieceTakerMove`, `…TakerRemoveFromList`) + "Add Player" (`…TakerAddToList`) + "default taker
from both sides" (`…TakerBothSides`); `/buttons`'taki ilk tablo sağ. Yalnız hücum bölgesi tacında atıcı seçilir. "Add Player"
`PropertyValue` kayıt `{SetPieceType, SetPieceSide, Index, Player}`; liste yalnız o ayağı kullananlar. ⚠ Sayfa adım adım
çizilir: açılır açılmaz tür seçimi kaybolur ama liste o türü göstermeye devam eder; eski türün atıcı düğmesi bir süre kalır
→ türü her seferinde yeniden seç, liste + "Set Piece Type" + düğme iki okumada aynı kalsın, açılan tablolar öncekinden farklı
olsun. ⚠ `/rowremove` bir kez "cannot be marked for dirty repaint…" döndü (satır yine çıkmıştı) → önce sayfaya bak.

### 8.92 Serbest oyuncular; kişi listesine sızan kadro; iki park
Serbest oyuncular = "Contract Expired" süzgeci (durum `FrA`) → `players --free`. ⚠ `/pull`'un `fmcli.` düğümleri bağlı kaldığı
için `/people` onları da satır sayıyordu (serbestler arasında Ederson) → `/people` ve `/squad` `fmcli.` altını atlar. Park:
maç hazırlığı odağı (§8.130'da çözüldü), gelişim grafiği (`AbilityProgressHistory` liste açılmıyordu; §8.113;
`TrainingProgressReportReference.AbilityGraphDataFull/Recent`, `AbilityProgression` var).

### 8.93 Duran top rutini: vuruş tipi, hedef, görevler (`fm setpieces <tür>`)
Vuruş tipi iki `SIButton` (`EventFmxSetPieceRoutineDeliverySelect`, `const.Zero/One`); adları seçimi göstermez, seçim
`routine.SetPieceDeliveryType` (0 Inswinger, 1 Outswinger); değişince atıcı öbür ayağın listesinden gelir. Hedef "Aim For"
`SIDropdown` (`EventFmxSetPieceStrategyConfirmed`): korner Mixed · Short · Near Post · Far Post · Edge of Area · Centre;
kanat/derin serbest vuruş Mixed · Short · Long · Near Post · Cross Centre · Far Post · Best Header; şut mesafesinde yok. Taçta
aynı olay iki düğmede ("Work Into Box"/"Long Throw", `routine.SetPieceStrategy` 0/1); Work Into Box'ta görev verilemez.
Görevler: her yer (A1…, B1, C1…, D1…) için `SIDropdown` (`EventFmxSetPieceInstrChanged`), bazılarında ikinci liste
(`…SecondaryInstrChanged`); grup ("Aerial Threats", "Box Threats", "Creators", "Recovery Defenders") "(3/9)" sayacından önceki
satır. Önceki türün kontrolleri sayfada kalır → kontrol yalnız sayfa başlığını ("Delivery Type", "Aim For", "Throw-In
Instructions") yazıyorsa alınır. ⚠ Sayfayı açan basış ara sıra yutulur (10'da 3) → `ShowSetPieceKind` 10 sn gelmezse bir kez
yeniden açar (12/12).

### 8.94 Kulüplere önerme, kilitli görüşme kartı, bire bir konuşma
Kulüplere önerme = değer düğmesi › "Offer via TransferRoom" (diğerleri: Set Transfer Status, Hire An Intermediary…, Ask
Agent About Market Interest…; maaş düğmesi sözleşme aksiyonlarını açar). Küçük teklif ekranı: "Transfer Offer"/"Loan Offer"
(`EventFmxTransferOfferSelectOfferType`), "Targets (0)", bonservis `FMCombobox` (`/setnumber`), ek ücret/madde, "Offer Player"
(`SpeakToAction`, onaysız; sonra iki seçenek listeden kalkar). Kapalı dönemde de gönderilir. `/offer` bu ekranı görmez →
`fm propose [--loan]`. ⚠ Oyuncu sayfasında aksiyon listesi açık kalınca `status` "açık soru" sanıyordu → `propose` bitince
Portal'a döner. Kilitli görüşme kartı (Defrel, bir gün; bir hafta sonra normal açıldı, nedeni bilinmiyor). Bire bir
konuşma `talk` şablonuyla aynı (tepki "Expressionless → Encouraged (+1)"); konuşmadan sonra mesaj hâlâ tedavi seçimi bekler ama
"cevap bekliyor" işaretli değil; Continue "Address Injury".

### 8.95 Soyunma odası (`fm dynamics`)
Squad › Dynamics › Social Groups: grup adı ("Core Social Group", "Secondary Social Group A/B", "Others"), grubun cümlesi,
oyuncu başına ad, "N year-old …", hiyerarşi (Team Leader, Highly Influential Player, Influential Player, Others), kişilik.
Hierarchy sayfasında durum bayrakları SONRAKİ oyuncuya ait. `/pull`: `Human.Team.ManagerSupportPlayers{Supportive,Neutral,
Opposing}Count`, `CurrentDressingRoomAtmosphere` (ham sayı); oyuncu başına `HierarchyInfluenceCategoryString` boş. Tek kayıtta
bilinmeyen özellik istenince `/pull` `{}` döner. Vaat eklemek `EventFmxAddPreContractPromise` (`contract --term "Will appoint
player as captain"`); vaat aşamasında ne "Cancel Negotiations" ne "Exit Talks" → `--cancel` sayfadan çıkar.

### 8.96 Sözleşmenin primleri ve maddeleri (`fm contracts <oyuncu>`)
`FullContract` şartları `displayvaluewithsort`: `<Alan>.DisplayValue` (`AppearanceBonus`, `GoalBonus`, `CleanSheetBonus`,
`UnusedSubBonus`, `LoyaltyBonus`, `WageAfterTax`, `ContractType`, `RelegationWageDecrease`, `MinimumFeeReleaseClauseValue`,
`SellingOnFeePercentage`…), olmayan "-", evet/hayır "No"; maç sırasında da okunur. İlk istekte boş (bağlanma bir kare) → önce
iste sonra oku. `ContractBonuses`/`ContractClauses` liste açılmaz → kupa/ödül primleri görünmez.

### 8.97 Haber sitesi (`fm news [<#>]`)
Portal › News Site: her haber `NewsItemReference` (`NewsTitleString`, `HeadlineSubTitle`, `NewsItemAgeString`, büyük karoda
`NewsBodyString` ilk cümle, `MediaNewsCategory`); `/tree?depth=40`. Gelen kutusu mesajları da bağlı ama yaşsız → öyle
ayrılır. ⚠ Sayfa karo karo dolar, yarım haliyle bir an sabit → üç okuma şartı (16/16). Haberin tamamı `#newsBodyText`; karo
düğmesi (`RunNewsAction, MarkMediaNewsAsRead`) mesaj görünümünde açar (`#BodyText`).

### 8.98 Gelen teklife mesajsız cevap (`fm offers`)
Mesaj birkaç gün sonra listeden düşer ama teklif bekler (Continue "Review Transfer Offer"). Recruitment › Transfer Activity
"Most Recent Offers": oyuncu, kulüp, durum, bedel; "Offer Made" (`transfer-link-button`) teklif ekranı. Satır ikonları:
`accept-button` (`EventFmxTransferCentreResponseAccept`), `stall-button` (`…ResponseStall`; pazarlıksızda kapalı),
`cancel-reject-button` (`…ResponseCancelReject`, onaysız). `offers` liste / `view` / `stall` / `accept` / `reject --yes`.

### 8.99 Tercihlerin kalan sekmeleri; bir kilitlenme
Bildirim / "detay seviyesi" ayarı yok. Processing: Continue Game Timeout, Match Scheduling Options, Speed to Process Other
Teams' Matches, Match Storage Space, Continue Button Legacy Mode, Automatically Open a Must Respond Message, Disable Fixtures
and Results Forced Screen; Your World: Match Odds, tarih ayracı, para biçimleri, ondalık/basamak simgeleri. `settings`'e oran
biçimi, diğer maçların hızı, maç takvimi, okumadan ilerleme eklendi. ⚠ Processing'de listeler başlıktan sonra dolar →
`PreferencesTab` dolmayı ve iki okumada aynılığı bekler. ⚠ Art arda 13. ayar değişikliğinde köprü "the main thread did not run
this request within 30s", oyun bir daha cevap vermedi (yeniden başlatıldı); "Automatically Open a Must Respond Message" CLI'a
eklenmedi.

### 8.100 Tablo: hangi turnuvanın? (`fm table [turnuva]`)
Portal karosu sıradaki maçın turnuvasının tablosunu gösterir ve `/table` ekrandaki bütün tabloları sıra numarasıyla tek
tabloda birleştiriyordu → `/table?groups=1` her tabloyu ayrı verir (yolun son iki adımı atılınca kalan). Varsayılan lig
(`LeagueName`); karo yalnız `Human.Team.NextFixture.Competition` istenen turnuvaysa alınır, değilse turnuva adlı `SIButton`'a
basılıp yeni tablo beklenir. Avrupa lig aşaması 36'nın 33'ü bağlı (sanal); kupada tablo yok.

### 8.101 Milli oyuncular (`fm internationals`)
`/pull`: `Nation.Name`, `InternationalCapsAndGoalsString` ("85 caps / 3 goals", "No Caps"), `InternationalAppearances/
GoalsThisSeason`; `InternationalReturnDate` paketli (çözülmedi). Bilinmeyen özellik listede yalnız o alanı düşürür. Tablo
satırlarının yolu `/pathbind` ile bağlanamıyor (`IndexOutOfRange`).

### 8.102 Mentorluk (`fm mentoring [--auto]`)
Squad › Training alt listesinde "Mentoring" (`fm open` adla bulmaz; `SecondaryTabDropdownButton`). "Create New Mentoring
Group" (`EventFmxAddMentoringGroup`), "Ask <yardımcı> to assign mentoring groups" (`SpeakToAction`) → "Acknowledge and Move
On". Üyeler metinde yok; grafik (`MentoringGroupRelationshipN`) `Person.Personality`, `Person.Name`, ikili
`MentoringGroupRelationshipScore` (0-2).

### 8.103 Kolay çıkmayan iki okuma
Mutluluğun nedeni: `HappinessNegatives/Positives`, `HappinessPlayingTime` (`HappinessCategoryReference`: `Description`,
`DetailedString`, `ProsList`, `ConsList`) `/pull`'da gelmiyor (§8.127'de çözüldü). Scout odakları: Recruitment › Recruitment
Focuses metni (odak · durum · scout/analist · Assignments), sonuç sekmeleri "Matched/Nearly Matched/In Progress"
(`EventFmxRecruitmentFocusChangeResultsTable`), "Edit/Remove/Create a New Recruitment Focus".

### 8.104 Kayıt tarayıcısı (`save --as`, `saves delete`, `load`, `start --menu`)
FM menüsü › "Save Game As": isim kutusu (`BindableTextEditBox #EditBox`, yüklü kaydın adını veren tek yer; `game.GameName`
boş), "Name / Last saved" tablosu, `Delete` (`DeleteSelectedGame`), `Cancel`, `Save` (`SaveGame`). Farklı kaydet: `/type` +
`Save` (~2 sn; ad varsa "Replace Existing File?"). Sil: satırı seç → `Delete` → "Delete File" (No/Yes); satır seçmek kutuya o
adı yazar → silmeden sonra `Cancel`. Satır seçmek `TableRowNavigatable.TableParent.SetListSelection(row.Index)`
(`/rowselect?text=[&dry=1][&expand=]`). ⚠ Devre dışı `Delete`'e `/click` oyunu dondurdu → `/click` devre dışını reddeder
(`force=1`). ⚠ Cevapsız eski pencere açıkken yenisi görünmez. Oyun içinde "Load Game" yok: ana menüdeki "Load an FM26
career…" döşemesi (`LoadGameTileSelection`) → aynı tablo + `Load` (`LoadSelectedGame`); satır seçilince özet ve `Load` açılır.
Liste sanal (gizli pencerede 3 satır) → `/scroll`, çapa satırın tarihi. macOS dosya adını ayrık harfle verir ("c" + sedil);
CLI ICU'suz → `string.Normalize()` boş → adlar elle birleştirilir (`Composed`). `load` ana menüden 14 sn, yeniden başlatmayla
~80 sn.

### 8.105 Stages sayfası: kupa turları, eşleşmeler, her ülkenin ligi (`fm stages`)
Portal › Stages: dört liste — turnuva ("Competitions ›" alt menülü; etiketi seçili turnuva), aşama ("First Round…", "League
Phase", "Main Path ›"; sonda "xG Table"), görünüm (kupada Round/Tree, ligde Overall/Home/Away/Last 5…), sezon; her açılışta
sıradaki maçın turnuvasına döner. Turnuva menüsü dünyanın tamamı: Competitions › kıta › (ülke ›) Men's/Women's › turnuva
(› Under 19s…), 195 turnuva; `/menu?ptr=&names=a>b`, `/select?ptr=&names=Competitions>Europe>Türkiye>Men's>Turkish Cup`.
Başka ülkenin lig tablosu da burada (`/table?groups=1` son grup). Turnuva seçilince listelerin işaretçileri değişebilir →
her adımda yeniden bul; seçimin tuttuğu etiket + işaretçi + aşama seçenekleri değişince anlaşılır. Sayfanın adı menüdekinden
kısa olabilir ("English FA Cup" → "FA Cup"). Eşleşme `#MainPart`: `#LeftSide` (ev), `#Versus`, `#RightSide`; ad `SIText
#text`, yanında `SIText #SIText` iki maçlı turda toplam skor; `#Versus`'ta skor ya da "V";
`#HomeResultTypeVisible`/`#AwayResultTypeVisible` turu geçende "p"/"e" → `/ties`. ⚠ `SIButton` da `TextElement` (kendi metni
boş) → "TextElement'se dur" demek içindeki yazıyı kaçırır. Liste sanal (18 eşleşme çizilir) → `/scroll?anchor=<ev takımı>`.
Kurası çekilmemiş tur "This round will be drawn on …"; sayfa kupada Final'de açılır → çekilmiş son tur ikiye bölerek aranır.
Turnuva listesi `~/.config/fm-cli/competitions.json` (ad bulunamazsa bir kez yeniden yürünür).

### 8.106 Elle mentorluk grubu (`mentoring new/remove`)
Grup tam üç oyuncu (`MentoringGroupPlayerOne/Two/Three`). Grup listesi `SIRadioGroup`; grafik yalnız seçili grubun
(`selectedmentoringgroup`) → `/select?name=SIRadioGroup&index=N` + grafiğin yetişmesini bekle (§8.102'nin "her blok ayrı grup"
varsayımı yanlıştı). "Create New Mentoring Group" (`EventFmxAddMentoringGroup` + `OpenPanel`) grubu HEMEN yaratır,
düzenleyiciyi açar ("0/3 Selected", `EditBox`, tablo, `Confirm` = `EventFmxEditMentoringGroup` + `…EditMentoringGroupName`,
`Auto Select`, `Undo Changes`, `Clear All`); onaysız çıkılırsa boş grup kalır → CLI siler. Satır işaretlemek: Unity `Toggle`'ı
yazmak saydırmaz; `row.TableParent.ListElement.SelectionInternal.SetSelected(row.Index, true)` (`/rowselect?text=&check=1|0`).
Aynı ad arkadaki tabloda da satır → `check` isteyen çağrı kutulu satırı arar; `Shows` derinliği 28. Kaydırma çapası iş yükü
hücresi. Silme `EventFmxDeleteMentoringGroup` ikonu, sormadan. ⚠ Radyo grubu sayfadan bir an sonra gelir: ilk yazımda
"önceki gruplar" boş okundu, temizlik bütün grupları sildi → adlar sayfa metni ile radyo grubu aynı olana dek beklenir,
temizlik yalnız "tam bir fazla grup" varken.

### 8.107 Taktiği dosyaya kaydetmek/yüklemek (`tactic save/saved/load`)
Taktik menüsü › Save: başlık ve açıklama `EditBox`, `Save` (`SaveAddon`) → `…/Football Manager 26/tactics/<başlık>.fmf`
(1 KB) ve etkin taktiğin adı başlık olur; Save başlık yazıldıktan bir an sonra açılır; aynı ada sorusuz üzerine yazar.
Load...: tablo, `Delete` (`EventDeleteAddOn`), `Load` (`EventLoadAddOn`); `/rowselect` → yüklenen etkin yuvadakinin yerine
geçer; yeni dosya ilk açılışta görünmeyebilir. Yüklemeden sonra planlayıcı yeniden bağlanmaz. ⚠ Sayfa açıldıktan hemen sonra
bulunan menü eski sayfanınki olabilir → panel 3 sn'de açılmazsa menüyü yeniden bul (`TacticPanel`).

### 8.108 Yönetimden talep (`fm board request`)
Club › Board › "N board member" kartı: dört `GenericDropdown` (Facilities, Finance, Networking, Staff; bazıları alt menülü)
+ iki `StandardButton` (Build New Stadium, New Contract), hepsi `SpeakToAction`; `/dropdowns?deep=1` `Submenus`. 33 talep.
`/select?ptr=&choice=<talep>&fire=1` iletir ("The board will get back to you shortly…", "Acknowledge and Move On"). Yapılan
talep listeden kalkar; üç talepten sonra kartta hiç seçenek kalmadı, kararlar gelince geri geldi. "Board Requests" tablosu
dolmadı.

### 8.109 Scout odakları: okuma (`fm scout`)
Recruitment Focuses bir odağı gösterir; odak yalnız durum seçenekli adsız listeden seçilir (`/select?ptr=&index=N`); başlık
metinde (ad, durum, "Assigned to:", scout/analist, "Assignments (4)", "Matched (a)", "Nearly Matched (b)", "In Progress (c)").
Sonuç satırı: [durum işareti] ad, ülke, yaş, mevkiler, en iyi rol, kulüp, lig, değer aralığı, not (A–E), tavsiye; satır başı
"ad + üç harfli ülke + yaş" kalıbından. Sekmeler `matched-tab`/`nearly-matched-tab`/`in-progress-tab` (`/press` değiştirmez,
`/click` değiştirir). Tablo sanal (18). Sihirbaz 1/2: mevki düğmeleri (`EventFmxSelectPosition`), roller, yeterlilik; 2/2: ad
(≤25 harf), en düşük yetenek/potansiyel, yaş, bölgeler, öncelik, scout/analist, "Include results found in other…";
`Cancel` → "Are you sure you want to cancel this Focus?".

### 8.110 FM menüsünden dünya ve kayıt bilgisi (`fm world`)
FM menüsü döşemeleri metin: "Selected Nations · 5 · Selected Leagues · 12 · Approximate player count · 34500 · Game Status ·
<menajer> · <kulüp> · …" ve kayıt bloğu (kayıt adı, son kayıt, otomatik kayıt, oyun süresi). Döşemeler sırayla dolar → iki
okuma aynı olmazsa kayıt adının yerinde "Credits" okunur. Menü `PanelHistoryClose` ile kapanır. Yüklü kaydın adını veren
ikinci yer.

### 8.111 Personel sorumlulukları (`fm responsibilities`)
Club › Responsibilities: sekiz grup, 29 görev, sanal liste. Grup: ad + `TakeControlAll` + `DelegateAll`. Görev: başlık,
açıklama, "Take Control"/"Delegate" (`buttons-segmented-button-row-…`), `AssignableStaff` listesi (etiketi görev kimdeyse),
unvan; `/form?anchor=&up=12&buttons=1`. ⚠ Düğmenin adı geçerli olanı söylemez: geçerli olan basılamayandır (ikisi de kapalıysa
kilitli). "Delegate" önerilen kişiye sorusuz verir; etiket bir an sonra güncellenir. Yalnız personel işleri burada.

### 8.112 Altyapı (`fm youth`)
Youth Setup alt sekmeleri: Overview, Youth Environment, Youth Performance, Youth Candidates, Under 19s Competitions, U19
Fixtures (`fm open` adla bulmaz; `fire=1`). Youth Environment iki metin bloğu: genel not, aday oyuncular, alım tarihi ("11th
March 2026 · (141 days to go)"); kulüp atmosferi, akademi/işe alım/seviye, personel sayıları.

### 8.113 `/pull` liste değerlerini açıyor; gelişim (`fm progress`)
`List<TypedValue>` özellikler artık virgüllü öğeler (ilk okuma boş olabilir). `AbilityProgressHistory` (kendi oyuncularımız):
son dört aylık ölçüm, her biri yetenek × 220 (35860 → 163); küsuratlılar sakatlık/form düşüşüne denk; `PerceivedPotentialAbility`
ile aynı ölçek (/200). Başka kulüp oyuncusunda gelmez.

### 8.114 Rakibin sezon sayıları ve takım formu
Opposition Report "General Performance": "<lig>, This Season", değer/etiket çiftleri (Gl/Gm, Np-xG/90, Conc/Gm, xGA/Gm,
Sh/Gm, Shot %, Pas %, Tck %), hüküm + cümle; sayılardan önce bir an "No Data Available" (ligi yüklü olmayan rakipte kalır).
"Form" çizim, yazısı yok → "-". Takım formu `Human.Team.RecentFormValuesAllComps` (kodlar §8.115).

### 8.115 Tabloda form
Stages lig tablosunda form hücresi takımı ve `RecentFormValues`'ı bağlar (satır › hücre › `BindingRemapper` › `binding`);
`/table` satırın atalarından en yakın `RecentFormValues`'ı ekler. Kodlar: 3 galibiyet, 0 yenilgi, 1 golsüz beraberlik, 2
gollü beraberlik; eskiden yeniye, beş maç. Portal karosunda ve turnuva sayfasında bu hücre yok → `table` önce Stages'i dener
(turnuva kendi adıyla ya da "<ülke> … <lig>" tek çıkıyorsa; ülke `Human.Team.Nation.Name`), ~9 sn; olmazsa eski yol.

### 8.116 Personel veritabanı (`fm staff find`)
Recruitment › Staff Database satırları `/people` (`Name`, `Age`, `JobString`, `ContractStatus`, `Wage`, `StatusTitleShort`;
"FrA" serbest). Süzgeç kutusu "Interested staff - …"; görünüm `ViewAltDropdown` (Contract, Rating, Coaching, Medical, Mental,
Scouting; nitelikler 20 üzerinden). Sanal, ada göre sıralı (~9000). Arama paneli (`EventFmxFilterStoreFilter`, "Edit
Search (2)"): Staff Role (Coaching › / Recruitment Team › / Medical ›, 16 görev), Employed (Any/No/By Club/By Nation),
Reputation, Add Attribute, Cancel/Apply; arama sayfada kalır, `Clear` (`EventFmxResetFilter`).

### 8.117 Oyuncu veritabanında gerçek arama (`fm players --…`)
Arama paneli: Position (`#position-widget`, tıklanamadı), Role (In/Out of Possession), Aged Between (15–45, iki liste),
Minimum Scouting Grade (A+…F), Contract Status, Transfer/Loan Status, Add Attribute, Exclude. Olaylar
`EventFmxSelectFilterValue`, yaşta `…MinValue`/`…MaxValue`. ⚠ Art arda hızlı seçimler kaybolur → her seçimden sonra panelin
yeni değeri göstermesini bekle. Cancel/Apply olay taşımaz (`/form?anchor=Apply&buttons=1`). ⚠ `Choose` sayıyı sıra numarası
sayıyordu ("17" → 31 yaş) → önce yazılanla birebir seçenek aranır. Mevki koşulu "Add Attribute › Position" ("Is Natural At…" ·
uzun mevki adları) → `--position DL`; değer koşulu "Transfer Value · Is At Most · [£0 … £100M]" → `--max-value` aşmayan en büyük
hazır tutar; nitelik koşulu "Attributes › Technical/Mental/Physical/Goalkeeping › nitelik" → "Is At Least/At Most/Between/Not
Between", değer bir DÜĞMENİN yazısı (hazır listeden en yakını + −/+); koşullar "And". `--attr "pace>=16"`.

### 8.118 Kişinin sağ tık menüsü; personel işe alma/kovma (`fm actions`, `staff hire/fire`)
Kişi adının geçtiği hemen her yerde ad `FM.UI.PluginContextMenuBindings` içinde (tablodaki "Actions" `#ActionsDropdown` da);
olay listesi yok, `SIButton` değil. Açmak: `m_manipulator` (`SI.UI.ContextMenuDescriptionManipulator`) →
`OpenOnShortcutEvent(evt)`, `evt` hedefi o öğe olan `NavigationSubmitEvent`; menü bir an sonra dolar. Okumak:
`SI.UI.ContextMenuSubsystem.m_popupContext.Description` (`MenuDescription`), `m_root.FirstChild`/`Next`: `Name`, `Kind`
(Item/Folder), `Flags` (Visible 1, Enabled 2, Selected 4), `HandleInteraction`. Seçmek `HandleInteraction.Invoke()` +
`CloseContextMenu()`. Köprü `/actions?open=1&text=` (ya da `name=ActionsDropdown`, `ptr=`), `/actions` (`Path` "A>B>C"),
`/actions?choose=<Path>`, `/actions?close=1`; ⚠ hedefsiz açtırma arkadaki ilk öğeninkini açar. Menü kime göre değişir:
başka kulüp oyuncusu ~190 giriş (Make An Offer, Transfer › Offer Trial/Make Enquiry/Declare Interest…, Scout Player, Fully
Scout, Add To Shortlist ›, Report › Assign Scout…, Misc ›); dışarıdaki personel (Approach To Sign, Add To Staff Shortlist ›…;
teklif sonrası Contract › Change/Withdraw Contract Offer); kendi personelimiz (Contract › Enter Contract Negotiations / DoF…
/ Terminate Contract / Mutual Termination, Misc › Send On Coaching Course). Kişiye oyunun aramasıyla ulaşılır (sonuç satırları
da menüyü taşır). İşe alma: "Approach To Sign" aynı sözleşme masası (`/contract` Stage "Terms": Job ›, Secondary Job,
Contract Status, Wage, Start Date, süre). Kovma: "Terminate Contract" → `/dialog` "Release …?" tazminat; listeden çıkması
sonraki ilerlemede ("… sacked"). "Mutual Termination" CLI'da onay ister.

### 8.119 Oyuncuya scout, kısa liste, ilgi, soru, deneme
Hepsi sağ tık menüsünden: Scout `Report>Assign to Scouting Pool>` (Until Extensive Knowledge, 1 Week…3 Months, 1–4 Matches)
ya da `Report>Assign Scout><ad>>`; atanınca "Scouting In Progress" + `Report>Cancel Assignment`. Kısa liste `Add To
Shortlist>` süre / "Remove From Shortlist (Default)". Soru `Transfer>Make Enquiry` (oyun teklif sayar → "Withdraw Transfer
Offer"). İlgi `Transfer>Declare Interest>` As Top Target / For Transfer / For Loan → Yes; iz ertesi sabah haberde.
Deneme `Transfer>Offer Trial>` 1–4 Week(s) → Yes → "Trial Offered". ⚠ Cancel Assignment işlemiyor (Yes'e her yolla basılsa da;
§8.124). ⚠ Açık pencere varken menüden seçim yeni soruyu altına dizer → `PersonMenu` açık pencerede başlamaz. Ad eşleme:
yazılanı adında kelime olarak taşıyan tek kişi. Deneme daveti ve soru da teklif sayılır (cevap "bid rejected" / "want £…").

### 8.120 Scout odağı kurmak, durdurmak, yeniden başlatmak (`scout new/remove/restart/areas/staff`)
"Create/Edit/Remove/Restart … Recruitment Focus" `SpeakToAction`; `/press` açmaz, `/click` açar. Sihirbaz
`/form?anchor=<Next|Back>&up=12&buttons=1`. 1/2: "Any Position" kutusu (`/setcontrol value=false`) kalkınca saha düğmeleri; biri
tıklanınca "Next" ve "Role (In Possession)" açılır; düzenlemede "Or — Find a replacement". 2/2: ad `/type`; yaş iki liste
(`/select` "no events to send" der ama tutar; üst liste alttakine göre daralır); bölge iç içe (Competition › / Nation › /
Region › / Continent › / Recently Selected ›; `/select?names=Region>Europe>Northern Europe&fire=1`); öncelik
`Priority-Tab`/`Standard-Tab`/`Ongoing-Tab`; Confirm. Remove silmez: odak listede kalır, "Assignments (n)" bir azalır, düğmesi
"Restart Recruitment Focus" olur (`Focus.Running` buradan; gün dönünce "<ad> (<tarih>)" "Complete"). Restart yeni kopya başlatır.
⚠ Sayfanın Create/Remove/Restart düğmeleri bazen yok (nedeni bilinmiyor) → "Assignments (n)" (`/click`) "Scouting Assignments"
paneli: scout listesi (`/rowselect`), seçili scoutun notları, "Regions/Nations", odakları (düzenle/kaldır ikonları,
`button-base-leading-icon`), "Create a New Recruitment Focus"; `scout new/remove` buradan da yürür. Yarım form geri gelir →
`OpenFocusForm` Cancel → Confirm ile atar. Durdurmanın güvenilir izi "Assignments (n)"nin azalması. Maç sonrası basın
beklerken sayfalar eksik okunur.

### 8.121 Rakip raporunun kalanı (`fm opponent`)
`/texts?anchor=Manager Information&up=10` (111 metin): Manager Information (ad, ülke, Win %, Pfd. Formation, Playing
Mentality, Pressing Style, Marking Style), Summary (tek cümle), Previous Meetings, birim döşemeleri "<Konu> - <Birim>" iki
biçim: (a) kapsam + değer/etiket + hüküm, (b) hüküm + öne çıkan oyuncu cümleleri (kilit oyuncu). `--json`: `Manager`,
`Expectation`, `PreviousMeetings`, `Units`.

### 8.122 (yanlış sonuç, §8.125 düzeltti) Youth Performance / Candidates bulunamadı sanıldı.

### 8.123 Oyuncunun scout raporu (`player --report`)
İzlenmiş başka kulüp oyuncusunda "Scouting Report" döşemesi ("C | Would be a useful signing."); düğmenin etiketi cümle →
`/click?text=<cümle>`. Rapor `/texts?anchor=Pros&up=12`: Scout/Analyst, son izleyen, tavsiye, Current/Potential Ability
(sözle), Transfer/Loan Interest, Transfer Value, Estimated Wage, Pros, Cons, Matches Watched, mevki, Best Role, Squad
Comparison. Oyuncu sayfası kaldığı sekmeyi hatırlar → döşeme ya da "Pros", hangisi önce gelirse.

### 8.124 Ekran görüntüsü; odakta yıldız, belirli scout, düzenleme; iptal penceresi
`/shot?path=<dosya.png>` (`ScreenCapture.CaptureScreenshot`; gizli pencerede de, 2940×1838, dosya 1-2 kare sonra) — metnin
söylemediğini (yıldız, seçili sekme, ikon) görmenin yolu. ⚠ `scale=3` oyunu kilitledi → 4096 pikseli aşan ölçek 1'e iner.
Yıldızlar `FM.UI.FMStarRatingTemp`: `m_unmappedMinValue` oyunun sayısı, `m_minValue` çizilen yarım yıldız; `/form` `Kind:
stars`; ölçek 1–15 (1–10 yarım yıldız adımları; 11–15 yeniden 1–5 yarım yıldız çizilir); `SetStars` 1–10'a iner. Belirli
scout: "Assign Scouts (n) › Edit" → "Assigned Scout" ("Assign Any Scouts" kutusu kapatılınca satırlar `/rowselect?text=&check=`).
Düzenleme aynı sihirbazı dolu açar; onaylayınca eski hal "<ad> (<tarih>)" "Complete" olarak eklenir. `/dialog?data=1`
(`DialogManager.m_currentDialog`): Cancel Assignment penceresi Type `Confirm1198281291` (= `ActionID.PersonStopScoutPlayersClub`),
"Yes" `Callback` + `PluginContextActionsModule.DialogPayloadData`, "No" `EventID=close`; `/dialog?press=primary`
(`FMDialogHandler.PressPrimaryButton`) de kaldırmıyor → oyunun kendi yolu da işlemiyor; park. ⚠ `pkill` bazen öldürmez (%100
CPU'da asılı) → `pkill -9 -f 'fm26_bep/fm.app'`.

### 8.125 Youth Performance / Candidates çiziliyormuş
Youth Performance = "Under 19s Training Review" tablosu (`/texts?anchor=Under 19s Training Review&up=10`; satır beş metin).
Youth Candidates alıma kadar boş ("0 Players | Awaiting prospects"; ortadaki yazı `/texts`'e gelmez). Ders: sayfa "okunmuyor"
görünüyorsa önce ekran görüntüsü. `youth --training`.

### 8.126 Forma numaraları (`fm numbers`)
"Squad Numbers" yalnız First Team sayfasından "More › Squad Numbers" ile açılınca dolar (`OpenPage("First Team")` +
`/select?name=SecondaryTabDropdownButton&choice=Squad Numbers&fire=1`); doğrudan açılan sayfa takımı bilmez. Tablo
`/form?anchor=Prom.&up=14&buttons=1`: numara seçici (`SIDropdown`; `Label`, `Enabled`, `Choices` — "7. Fred" dolu, "8" boş,
"12. (Dedicated to the fans)" emekli), istenen numara (Pref.), ad, mevki, statü, yaş; 42 satır, 24 kurulu. `/scroll?anchor=<ad>`
yanlış listeyi kaydırıyordu → `/scroll?ptr=<kontrol>`; kaydırınca satırlar yeniden kullanılır. Kilit: numaralar yalnız sezon
başı / yeni transferde; kilitliyken seçiciler ve dört düğme (Undo Changes, Clear All, Ask Assistant to Assign, Confirm Squad
Numbers) kapalı. `PreferredSquadNumber` `/pull`'la gelir, `SquadNumber` gelmez; `PromisedSquadNumber` var. `numbers <oyuncu>
<no> --send`, `--auto`.

### 8.127 Mutluluğun nedeni (`happiness <oyuncu>`, `--why`)
Happiness sayfasının sağ paneli satır seçilince dolar: ad, tanım, Happiness, Morale, Hierarchy, Positives, Negatives
(ağırlıklı: "(Pre-concern) …", "(Minor Issue) …"), "Actions"; `/texts?anchor=Morale&up=3`. `/rowselect?text=<ad>` alttaki First
Team satırını seçiyordu; tanım iki oyuncuda aynı olabilir → `&and=<ikinci yazı>`. ⚠ Eski hatalar: durum bayrakları addan önce
(önceki oyuncuya yazılıyordu); "Delighted" seviyesi listede yoktu.

### 8.128 Odakta analist; siyah ekran görüntüsü
"Assign Analysts (n) › Edit" → "Assigned Analyst" (kutu "Assign Any Analyst", tekil), satırlar `/rowselect`; `--analysts`.
⚠ `/shot` simsiyah dönebilir (nedeni §8.129: Match Day › Overview). ⚠ Seçme penceresinin tablosu yalnız görünen satırları kurar
(o oturumda tek satır) → üç derinlikte oku; satırın bayrakları kendi kaydırıcısında → köprü kaydıracak şeyi olmayan kaydırıcıyı
atlayıp yukarıdakine çıkar. ⚠ `scout edit <#>` listenin sırasını değiştirir → adla ver.

### 8.129 Tam istatistik listeleri ekransız; bir verinin istediği değişkenler (`league --stat/--stats`)
`InteropReference.GetContexts(propertyId, list)` bir özelliğin istediği bağlam değişkenlerini verir (ad
`ContextID.GetIdentifierSet().GetInfo(id).Name`) → `/contexts?path=<referans>` (lig 90 satır: `PlayerStats: Type, Stage`,
`PlayerTopStats`, `TeamStats`, `TopTeamStat`, `PlayerStatsType: StatCategory`, `Fixtures: IsForcedScreen, Date`,
`TransferInOutList: Year, ViewID, Number`, `AllStages: NextSeason, Year`). `/pull` ile boş dönen özelliğe önce buna bak.
Değişken vermek = yanına aynı adla düğüm koymak (sayfalar `BindingVariables` ile yapar): `/pathset?to=<yol>&int=<n>` ya da
`&from=<yol>`; `fmcli.x.Type = 2`, `fmcli.x.Stage = -1`, `fmcli.x.comp = Human.Team.Division` → `fmcli.x.comp.PlayerStats`.
Oyun -1…100'ü hazır tutar (`TypedValue.s_commonInts`); ötesi `TypedValue.Create<int>`; `from` ile konan referans o çağrıda
bağlandıysa değer bir kare sonra ("has no value yet"). Listeler: `comp.PlayerStats` → `StatValueHeader`,
`ContextStatValueHeader1/2`, `Players` (97–100 satır: `Position` "1st"/"=", `StatValue`, `ContextStatValue1/2`, `Player`,
`Team`); `comp.TeamStats.Teams` değerleri `DisplayValuePair` (form türlerinde `PropertyValue` maç maç liste). `Stage = -1`
turnuvanın tamamı. Tür numaraları 0…160 sorularak çıkarıldı (`PlayerTopStats/TopTeamStat.ShortString`): 127 oyuncu, 100
takım (ör. oyuncu 2 Goals, 5 Assists, 27 Average Rating, 68 xG, 103 Saves; takım 5 Form, 15 Goals, 28 Average Possession);
oyuncu ve takım dizileri ayrı. `PlayerStatsType` her kategoride aynı 19'u verdi (kullanılmadı). Katalog
`~/.config/fm-cli/league-stats.json` (~25 sn, bir kez; `--refresh`). Kulübün turnuvaları `Human.Team.CurrentComp1…8.Name`;
`league` hiç sayfa açmaz. Stats kartı döşemeye `/click?ptr=` ile açılır (yalnız liderler). Siyah ekranın tetikleyicisi
Match Day › Overview'dı (`OnThePitchScreen`; kök neden §8.144). Teşhis: `/shot?cams=1`, `/shot?rt=<doku>&path=`.

### 8.130 Maç hazırlığı odağı ekransız (`fm prep <topla|topsuz|duran1|duran2> [odak]`)
`Human.Team.TeamTraining.TrainingMatchFocus` (`MatchFocusReference`), değişken `FixtureHasResult` (False):
`/pathset?to=fmcli.mf.FixtureHasResult&from=const.False`, `/pathset?to=fmcli.mf.training&from=Human.Team.TeamTraining` →
`fmcli.mf.training.TrainingMatchFocus`. Şimdiki: `MatchFocusInPossessionType`, `…OutOfPossessionType`, `…FirstSetPiece`,
`…SecondSetPiece` (`MatchFocusSessionName`, `…Description`, `…Value`, `…IsSuggested`). Seçenekler
`MatchFocusInPossessionValues` (Build Up · Progression · Final Third), `…OutOfPossessionValues` (Low Block · Mid Block · High
Press), duran toplar `MatchFocusSetPiecesDropdown` (`{RowData, Disabled}`; Attacking/Defending Corners/Free Kicks,
Penalties). Değiştirmek `EventToPlugin`, hedef `TeamTraining`, `EventFmxMatchFocusSelectInPossessionType/
…OutOfPossessionType/…FirstSetPiece/…SecondSetPiece`, veri seçeneğin kendisi (`/send … DataToSend=path:<yol>`; sayı
gönderilince hep ilk seçenek). Oyun iki duran top yuvasına aynı odağı kabul eder → CLI reddeder. Oyunun kartıyla doğrulandı.

### 8.131 `fm targets`; biriken arama koşulları
Bütçe Recruitment Budgets'tan, ince mevkiler `PositionCombinedString`'ten (sakat/kiralıktaki sayılmaz; sağlam ≤ 1).
`targets <mevki>` = `players --position <mevki> --max-value <bütçe>`. ⚠ Arama paneli koşulları biriktiriyordu
(`conditions-list`, her `ApplySearch` yeni satır) → `players` önce "Clear" (`EventFmxResetFilter`). Satır silme
`EventFmxRemoveFilterCondition`, aç/kapa `…SelectFilterEnabled`, ve/veya `…SelectFilterLogicalOperator`.

### 8.132 Takım talimatları ekransız; topsuz (`tactic instructions [--oop]`)
Talimat = numara: hücreler (`BindingVariablesNoPhase`, `…Phase0/1/2`, `thisphase`) `ID` ile `data.TacticTeamInstruction` →
`Name`, `PhaseID`, `TeamInstructionOptionsList`, `HasExtraInstruction`; ayar adı `data.TacticTeamInstructionOption`. Ekransız:
`/pathset?to=fmcli.x.ID&int=<n>`, `/pathset?to=fmcli.x.data&from=data`. Numaralar 1–31 (2, 13, 14 boş). Topla: 1 Passing
Directness, 3 Attacking Transition, 4 Attacking Width, 5 Creative Freedom, 6 Play for Set Pieces, 7 Time Wasting, 16 GK
Distribution, 17 GK Distribution (Speed), 18 Patience, 19 Dribbling, 20 Pass Reception, 21 Tempo, 24 Build-Up Strategy, 26
Crossing Style, 27 Shots from Distance, 28 Supporting Runs, 29 Goal Kicks, 30 Progress Through. Topsuz: 8 Line of Engagement,
9 Defensive Line, 10 Trigger Press, 11 Defensive Transition, 12 Tackling, 15 Defensive Line Behaviour, 22 Cross Engagement, 23
Short Goalkeeper Distribution, 25 Pressing Trap; 31 ikinci "Supporting Runs" (sayfada yok); ek ayarlı 9, 16, 28. Seçili ayar
`Human.Team.Tactic` (`Boolean` değişken; `True` sayfanın taktiği, `False` başka kopya) `.SelectedTeamInstructionOption` +
`TeamInstructionID`; `Tactic.DefensiveLine`, `Tackling`, `Tempo`… aynı numara. Değiştirmek: önce o yolu bir kez oku (bağla),
sonra numarayı yaz, yeni bir yerden oku (bağlanmadan yazılan değer düğümde kalır). `EventFmxSetTacticTeamInstruction` hiçbir şey
değiştirmedi. `InPossession/OutOfPossessionInstructionList` kartın özet satırları. ⚠ `data.TacticTeamInstructionsForPhase`
(`List<Int32>`) zorlanırken oyun bir kez çöktü; gruplama CLI'da sabit.

### 8.133 Oyuncu talimatları ekransız; topsuz (`tactic pi <mevki|oyuncu>`)
Gruplar `data.TacticPlayerInstructionGroup` + `PlayerInstructionGroupID`: `Name`, `PlayerInstructionGroupOptionsInt`,
`HasExtraInstruction`. 0 Attacking Width, 1 Dribbling Direction, 2 Dribbling Frequency, 3 Forward Runs, 4 Freedom of Movement,
5 Hold Up Ball, 6 Shooting, 7 Passing Strategy, 8 Passing Risk, 9 Crossing Strategy, 10 Cross From (topla); 11 Trigger Press, 12
Marking, 13 Tackling, 14 Marking Target (topsuz); ek ayarlı 7, 9. Ayar adı `data.TacticPlayerInstructionOption` +
`PlayerInstructionGroupID` + `playerinstructionoptionid` (numara; 0 her grupta "Standard", öbürleri 1–35, tekrar etmez). Slotun
ayarı `Human.Team.Tactic` (True) + `TeamSelectionIndex`, `PlayerInstructionGroupID`, `InPossession` →
`SelectedPlayerInstruction`, `PlayerInstructionGroupIsLocked` (rol belirler), `PlayerInstructionGroupOptions` (`{PropertyValue,
Disabled}`); yanlış `InPossession` ile grup kilitli görünür. Değiştirmek takımınkiyle aynı; yazılan mevkiye gider.

### 8.134 Yardımcı kaptan günde bir kez
Kaptanlar sayfası yardımcı antrenörle konuşma; "Change Vice Captain" seçilince konuşma geçmişine yazılır ve liste o gün bir
daha gelmez; ertesi gün (ya da yeniden başlatınca) gelir. Yardımcı adayları sınırlı (3), kaptan adayları bütün kadro.
`PlayerMakeViceCaptain`/`TeamSelectViceCaptain` `ActionID` (olay değil).

### 8.135 Sorumluluğu belirli kişiye (`responsibilities give`)
`AssignableStaff` satırları yalnız `PersonReference` → `/rowput?ptr=<liste>&i=<n>&to=<yol>` + `/pull?path=<yol>&props=Name`;
ilk satır menajer. Liste olay taşımaz; `/select?ptr=&index=<n>` yeter, etiket bir an sonra güncellenir.

### 8.136 Serbest bırakma gün ilerleyince işler
`release --free --yes`: pencere Yes ile kapanır ama oyuncu kadroda, bütçe aynı; bir gün sonra haber "given free transfer",
kadrodan çıkar, tazminat bütçeden düşer → komut "gün ilerleyince" der.

### 8.137 Maç içinde takım/oyuncu talimatları
Maç sırasında `Human.Team.Tactic` maçın taktiği değil; maçınki `MatchUI2E.Tactic` (`MatchReference.Tactic`,
`MatchTacticReference`, aynı özellikler, `Boolean` istemez, `IsDuringMatch = True`; düğüm adı oturuma göre değişir →
`/find?name=Tactic`, `..MatchUI….Tactic`). Yazma aynı yol; `MatchTeamHasPendingChanges = True` olur ve ilk ölü topta
kendiliğinden uygulanır; kayıtlı taktik değişmez.

### 8.138 Aracı başaramayınca mesaj yeniden açılır
"Ask X to Resolve Issue" başarısız olunca ilk mesaj yeniden seçenek verir ama `NewsHasResponded = True` kalır (bayraklardan
ayırt edilemez) → `status`/`next` başka bloke eden yokken öncelikli + `CanRespond` mesajları "yeniden cevap bekliyor
olabilir" sayar. `actions … "Available for loan"` kiralığa açar (kartta görünmez). `actions néné` başka kulüpten "Nenê"yi
buldu (`player` doğru).

### 8.139 Sakatlık durağı canlıda
"Confirm Changes" durağın hemen başında yok, birkaç sn sonra gelir → `sub` 5 sn bekler; saat-durdu mesajı `sub`/`match`
yolunu söyler. Dugout bu durakta kim sakat yazmayabilir.

### 8.140 Bayat gelen kutusu; yönetim toplantısı
Daha önce yüklenmiş gelen kutusu başka sayfaların altında bağlı kalır, eski haliyle okunur → `inbox` her seferinde portalı
yeniden açar (`FreshInbox`); `status`/`next` bekleyen bulamazsa bir kez taze okur. "Board request meeting to discuss your
future": Attend → sorular → End Chat; gitmemek fesih demek.

### 8.141 Gelen kiralık baştan sona; numara
Kiralık kabulünden sonra mesajda yalnız "Delegate … to <DoF>" (görüşmeyi açan kontrol yok). "set to move on loan" (öncelikli,
ilerle "Confirm Transfer"): Accept / Delay Transfer / Cancel / Delegate → "loan completed". `numbers <oyuncu> 19` köprüden
"dropdown has no events to send" döner (hata değil) → `--send` ile gider.

### 8.142 Kovulmak
İkinci "Board request meeting…"te kötü cevaplar → "Decision to terminate your contract", "… sacked". Sonra `Human.Name` var,
`Human.Team` boş → `status`/`squad` "İşsizsin … İş ilanları" der (`Unemployed()`).

### 8.143 Kapanmayan oyun, çizim hataları, iş ilanları (yarım; §8.144 çözdü)
`pkill` (TERM ya da -9) sonrası süreç `UE`'de kalıp 7777'yi tuttu; ona bağlanan `fm start`'lar da takıldı → eklenti port
doluysa 7778… alır, `~/.config/fm-cli/port`'a yazar; `fm start` yoklaması 3 sn. spindump: süreç "(suspended) (zombie)", ana
iş parçacığında aynı yönetilen çerçevenin onlarca kez iç içe çağrıldığı özyineleme (`tlv_get_addr`). `Player.log`'da sayfalar
ilk kurulurken çok sayıda "Assertion failed" (`UIR.VisualChangesProcessor`) — oyunun kendisi üretir. İş ilanları paneli
çizilmiyordu (sebep mod, §8.144); Palace'ta "Apply For Job" (`EventFmxApplyForJob`) kapalı, "Declare Interest In Job" açık.

### 8.144 Boş paneller Display Fix modundanmış; oyunun kendi çıkışı (`fm stop`, `fm jobs`)
`FM26DisplayFix` modu ekranı 16:10'a zorluyor (2880×1620 → 2940×1838); ızgara yerleşimi uymuyor, `#GridLayout` 12 piksele
sıkışıp içerik ekranın altına düşüyordu (yerleşim hatası, çizim değil). Mod `~/fm26_bep/disabled-plugins/`'e taşındı → sayfalar
tam; siyah ekran da çözüldü. Teşhis: `/visible?text=`, `/layers?name=&depth=`. `/repaint`'in `doc` hali arayüzü oturum boyunca
siler (kaldırıldı). Çıkış: `Application.Quit()` "Quit Game And Exit?" açar; Yes → 2 sn'de temiz kapanır, `Application.quitting`
dinleyiciyi kapatır. `fm stop`: `/quit` → onayla → köprü sussun, süreç bitsin (~5 sn); olmazsa `pkill`, `-9`. İş bulma baştan
sona: `jobs <kulüp> apply --yes` → "job application received" → günler sonra "offer job interview" (Attend) → mülakat `talk`
ile → "approach <menajer>" (Enter Contract Negotiations) → sözleşme `talk` ile (Finalise Deal / Delay / Cancel) → Exit Talks →
ertesi `next`'te iş başlar. `talk` sözleşme maddelerini göstermiyor (yalnız "Here is our initial offer:").

### 8.145 Yeni kulübün ilk günleri
Üç bloke tanıtım (Tactics / Responsibilities / Squad); Tactics turu taktik ister (`tactic new` kapatır). Tur adım numarası
yazısından önce değişir, adımlar arasında bir an "yok" → `tour` yazının değişmesini bekler, 3 okuma yoksa bitti sayar. "Choose
<kulüp> captain", "Set Code of Conduct" (eki ceza tablosu) → `reply` "Attachments" bölümünü gösterir (gövdenin ilk 40 harfiyle
eşlenir). `setpieces new` seçenekler dolmadan okuyordu → doluncaya kadar bekler (canlı doğrulanmadı). Tanıtım basını eki
konular + gazeteciler; seçenek grupları ([Positive]/[Neutral]/[Negative]) her soruda farklı sırada ("hep 1" kör seçimi riskli).
Yeni kulüpte maçtan önce `tactic pick`.

### 8.146 Başlamadan status, maç sonu, kaydırılmış gelen kutusu
Düdükten önce `state`'te skor/`HumanIsHome` yok, `MatchTime` boş → "Maçtayız, başlamadı" (ev sahibi fikstürden); takım
konuşması bekliyorsa `talk`'a yönlendirir. Son düdükten sonra `Period`/`HumanIsHome` düşer, `State` doğrudan
`FullTimeTeamTalk`/`LeaveMatch` ("FullTime" görünmez). Portal gelen kutusu sanal (`StreamedListView`, ~16–20 satır) ve kaydırma
yerini korur → `FreshInbox` bir başlıkla `/scroll?anchor=…&to=0` yapıp yeniden okur. "View Highlights" `SIDropdown`
(`SpeakToAction`) konuşma sanılmasın: söylenmiş söz ve düğme yoksa açılır menü konuşma değil. Kayıt ekranının sol paneli
muafiyetleri yazar ("Players of the age of 20 or younger…", "Goalkeepers do not have to be registered";
`/texts?anchor=Registration Open&up=6`).

### 8.147 Antrenör görevleri (`fm coaches`)
Squad › Training › "Coach Assignments" (`SecondaryTabDropdownButton`); `/form?anchor=Set Coach Assignments&up=3`: dokuz alan
başlığı, sonra antrenör başına ad, görev, dokuz kez (`checkbox` + `stars`; `Shown` yarım yıldız, yalnız atanmış hücrede;
işinin izin vermediği kutu `Enabled=False`). Altta dokuz iş yükü (Light/Average/Heavy) + baş antrenör yıldızı (kimse yoksa
öğe yok). Atama `/setcontrol?ptr=<kutu>&value=1`, anında; ikinci alan verince ilkindeki yıldız düşer. (Bekleme §8.180.)

### 8.148 Deneme ve durum bayrakları
Deneme: "Trial Offered" → aynı akşam "X set to go on trial" (bloke; Accept / Cancel / Delegate) → "X on trial" ve oyuncu kadroya
düşer (numara -1, maaş N/A, Unregistered + Free Agent; ayrı "denemede" bayrağı yok). Aynı adlı iki kişi → parantezli ek
(`"Nathan Ferguson (25 yaş)"`, `"(hartlepool)"`) aday etiketinde (yaş · mevki · ülke · kulüp) aranır. Durum bayraklarının tamamı
`PlayerStatusFlagsForTeam` (`PlayerStatusFlagReference`: `StatusTitleShort/Full`, `StatusReason`, `StatusType`,
`IneligibilityReason`); değişkenler `team` ve `PlayerStatusSortType` (`/pathset?to=<üst>.team&from=Human.Team`,
`…PlayerStatusSortType&int=0`; aynı nesille yeniden çekmek siler). Rst = Rest, Unr = Unregistered, FtT = Fitness Test, ESC = ESC
Work Permit, Loa = Loan listed, Lst = Transfer listed, Inj = Injured; ikinciler Yellow card warning, Free Agent. 30 oyuncu <1 sn.

### 8.149 Personel alımı uçtan uca; karşılıklı fesih
Serbest personelin kulübü/maaşı yok → kart hazır işareti "iki aynı okuma". Job listesi iç içe (`/select names=Coaching>Coach`
"no events to send" der ama değer değişir). Suggest Terms → Finalise Deal → "I'll be in touch…" → yalnız Exit Talks / Adjust
Budgets; 3–4 gün sonra "<Soyad> set to sign" (Accept / Delay Transfer / Cancel / Delegate; ekte onay kartı) → "completes …
move". Aynı adlı personeli ayırmak: arama sonucunun `JobString`'i (`team` + `human` bağlamı). Karşılıklı fesih: `--mutual
--yes` → ertesi gün oyuncu konuşma ister → ikna → "agrees to contract termination for a fee of £160K" (Confirm), bedel transfer
bütçesinden.

### 8.150 Sorumlulukların dokuz alanı; basın devri
Responsibilities › Delegation'ın solunda `SIRadioGroup #StaffResponsibilitiesRadioGroup`: Board, Staff Responsibilities,
Scouting, Transfer and Contract, Media, Training, Tactics, Match Day, Set Pieces; sağdaki liste seçili alanın (§8.111'deki
"yalnız personel" yanlıştı). `/setcontrol?ptr=<radyo>&value=1` → `SIRadioButton.OnClickedOrSubmitted(kendisi)`. Yazıyla bulmak:
`/elements?text=…&up=6&limit=10`'dan `SIRadioButton` olanı. 105 görev (sayaç 118 der). Medya: New Signing / League / Domestic
Cup / Continental Cup Press Conferences, Broadcast Interviews. Hız: Club sayfaları her açılışta 8 sn bekliyordu (çubuktaki
etiketsiz ikon düğmesi) → düzeldi; `/scroll` cevabı görünmeyen pikseli verir → adım sayısı ondan; görev→alan eşlemesi
`duty-areas.json` (delegate/take 39 → 4 sn).

### 8.151 Maç içi sakatlık, kart, puan
Maç ekranı her mevkiye `Position.MatchPlayer` (`MatchPlayerReference`) bağlar: `MatchInjuryType` (0 sağlam), `YellowCardedInMatch`,
`RedCardedInMatch`, `MatchRating`, `Player.Name`, `PitchShirtShowUnavailableFlag` (kırmızı kart değilse sakat). `/find?name=
MatchPlayer` yolları ".." ile başlar, `/pull` alamaz → önce `/pathset to=fmcli.mp.pN from=<yol>`. Saha birkaç karoda; her
takımdan bir karo. `MatchPlayer` düğümleri maçın yaklaşık yarısında var (pozisyon anlarında yok) → hiç okunamamışken 5 sn'de
bir, sonra 30 sn (80'den sonra 10); son dolu okuma saklanır. Fikstürün `HomeEventInfos`/`AwayEventInfos`'unda yalnız goller.

### 8.152 Altyapı alımı adayları (`youth candidates`)
"Youth Candidates" sekmesi hep var (sayfa "Youth Environment"tan bulunur). `YouthReportSiteYouthCandidatesTool` ›
`CandidatesTable` beş grup (`EliteProspectsBinder`, `TopProspectsBinder`…); başlıklar sayılı ("Elite Prospects (3)") → sayılar
lejanttan (`/texts?anchor=Elite&up=3`), sonra `/form?anchor=Elite Prospects (3)&up=7`. Satır: ad, ülke, mevki, rol, iki
`FMStarRatingTemp` (yetenek; potansiyel aralık `m_minValue`–`m_maxValue` → `/form` `ShownMax`). Yaş `/people`'dan.

### 8.153 İzlenen maç (U18 – altyapı adayları)
"Attend Game" maçına girince `NextMatchOpponent` A takımınki kalır → ekranın maçı `/find?name=Match` (`..MatchStateCache32.Match`)
→ `HomeTeam.Name`/`AwayTeam.Name`; ikisi de kulübün adı değilse "izlenen maç" (ev sahibi önce, A takım raporu yok, biz/rakip
ayrımı yok).

### 8.154 Sakatlık durağı, ikinci kez: sakat sahada kalamaz
Bu kez iki §8.151 işareti de boş; Planner tablosunda satırın "Inf" sütunu "Inj" (`/texts?anchor=F. Draper&up=11`; ad kısa yazımla).
"Confirm Changes" değişiklik yokken kapalı; "Cancel" planner'ı kapatır ama sakat sahadaysa "Injured Players" (tek "Ok") ile geri
döner. Pencereden sonra sakatın saha menüsü kalkabilir → `tactic player ST Collins` (`/pickplayer`) yuvaya koyar; kapalı Confirm'e
`/press` yine çalıştı. Artık `sub` sahada bulamazsa planner yuvasından değiştirir; `match` kapalı Confirm'e de basar, "Injured
Players"ı kapatıp kimin çıkması gerektiğini söyler. Maç sonrası `State=Results` "Breaking News" sayfasında menü/arama yok →
"önce geç: next". `player --matches`: oynamadığı maç 0.00 ve "Min. Rating" 11.00 → "-".

### 8.155 Kulübe göre değişenler
Youth Performance başlığı kulübün altyapı takımını söyler ("Under 19s/21s Training Review") → sayfa her kulüpte olan "Avg.
Training Rating"ten (`up=14`). Lincoln'de kadro görünümlerinde "Overview" yok → `depth`, `squad --u19` "General Info" okur.
Lincoln'de kadro tablosu yalnız A takım. Takvimde altyapı satırının kodu yarışma ("3N", "FR"), yaş grubu değil.

### 8.156 Maç sonrası tablo
`/table?groups=1` maçtan hemen sonra haber kutularından 3 satırlık tablolar da verir → en uzunu (eşitte sütunları dolu olanı).
Yeri kesinleşen takımın işareti ("C", "Pl", "R") da `Name` adıyla bağlı ve adı ezer → `ShortName`'den ad geri alınır, işaret
`Settled`'a ("Bolton (şampiyon)"). Kalıcı çözüm eklentide (aynı adlı ikinci özellik ilkini ezmesin) beklemede.

### 8.157 Sonuç ekranında sayfa açınca zaman duruyor
`State=Results` ("Breaking News") iken sayfa açan komut sonrası `next` hiç ilerlemedi (`CanContinue`, kuyruk boş, gerçek tıklama
da değil); kaydı yeniden yüklemek düzeltti (oturuma ait). `NotNow` `Results`'ta "önce geç: next" der (tek gözleme dayanır).

### 8.158 Cevapsız mesajlarda ek
"Cevap seçeneği yok" dalı eki hiç yazmıyordu. Ek mesajdan bir an sonra gelir (≤1,5 sn). Satır satır yazılan ekler:
Responsibilities, Training (iki hafta, gün başı üç seans), Recruitment Focuses, Advice Summary, Next Opposition Report,
Homegrown ("N years old" ile satır). Düz dökülenler: Team of the Week, Advice Summary'nin son bölümü, Scouting Report kartı.

### 8.159 Sezon sonu
Son lig maçından sonra `status` "Sıradaki maç"ı bırakır. Stages oynanmakta olan aşamada açılır ("Playoffs Semi Final") → `table`
lig aşamasını ayrıca ister. İşaretler "C", "P" (yükseldi), "Pl", "R". Gelen mesajlar: Squad Planning, Board Set Initial
Budgets, Seasonal Performance Analyst Report, End of Season Review, Club vision and expectations meeting, End of Season Team
Meeting, Season Injury Report, Pre-season start date, Supporter Profile Update. Ekler (Competitions, Recruitment Budgets, Club
Vision, Season Summary) satır satır.

### 8.160 Kulüp vizyonu toplantısı; liste olan cevap (`fm vision`)
"Pre-season start date" cevabı `SIDropdown` (`GenericDropdown`, `SpeakToAction`; seçenekler tarihler). `/press` ile basınca olay
`Initialising` halinde kuyrukta kaldı ve her basışı tıkadı (`/queue?clear=1`) → doğrusu `/select?ptr=&choice=&fire=1`; `reply`
liste seçeneklerini "1 1", "1 2" diye yazar. Vizyon toplantısı (Negotiate → `State=NeedAction`): `/texts?anchor=Board
Visions&up=3`: konuşan, söz, sekmeler (Board Vision, 5-Year Plan), Actionable/Informational Objectives (hedef + ağırlık),
Undo/Reset/Confirm/Suggest. Düğmeler: hedef başına etiketsiz `EventFmxRemoveClubVisionObjective` (zorunlu hedefte yok),
`EventFmxConfirmTerms`, `EventFmxSuggestTerms`, `EventFmxUndo`, `EventFmxRevert`, `EndMeetingButton`. Yönetim önerileri
reddedip nedenini yazdı.

### 8.161 Birden çok oyuncuya gelen teklifler
Transfer Activity solunda teklif alan oyuncular radyo grubu (`transfers-list-radio-group`; seçenek yazıları durumlar, ad değil);
sağda seçilinin teklifleri → `/select?ptr=&index=N&fire=1`; sağ taraf parça parça dolar → iki okuma aynı ve "N Unresponded/Active
Offers (…)" gelince oku. Satır: kulüp, tür, sıfır+ şart, durum, bedel (kiralıkta "Wages: 90% (90%)"); bedel
`^£…( \(…\))?( p/m)?$` ile tanınır, durum ondan önceki yazı. Satış/kiralama/ret/bekletme canlıda.

### 8.162 Sayfalar kimlikle: Guid, takım kimliği, alt sekme düğmesi
Menü satırındaki varlık Guid'i kalıcı (Fenerbahçe ve Lincoln kaydında aynı); 78 sayfadan 74'ü Guid'le tek. First Team / Under
21s / Under 18s / Match Squad tek panel (`TeamSquadReport`), fark `ObjectUniqueID` (A takım = `Human.Team.UniqueId`) → eskiden
`open "Under 21s"` A takımı açıyordu. Takımlar veriden: `Human.Team.Club.Teams` (`UniqueId`, `TeamType` A 0 / U21 10 / U18 12,
`TeamTypeStringDefault`; ilk okuma "not a list"), kısa yol `Club.HighestLevelYouthTeam`/`SecondHighestLevelYouthTeam`. Açmak:
menü satırını seçmek (`OnSelectionChangedInternal(i)` + `SelectionEvents`); taktik Portal'dan tek adımda dolu gelir. Alt menülü
satır (Training ›) seçilince açılmaz, bookmark boş açar → alt sekme çubuğundaki düğmenin tıklama olayı (`SIDropdown`,
`LeftClickEvents → OpenSubtab`), düğme ilk satırının Guid'iyle bulunur. Gizli çubuklar: önceki sekmenin çubuğu arkada kalır →
düğme yalnız kendisi ve bütün ataları görünürken sayılır. "More"dakiler bookmark'la (dolu) ama Medical Centre açılmıyor (açık).
Sekme önemli: Opposition Report iki sekmede aynı Guid → `open?under=<sekmenin ilk sayfasının Guid'i>`. Opposition Instructions
kendi Guid'iyle (`f3a5a1da…`) açılır.

### 8.163 Rakip raporu tek panel, açık kaldığı biçimi korur
Match Day'deki "Next Opposition Report" ile Portal'ın "Team Report"u aynı panel (`b96211c5…`). `mentoring`/`staff`/`coaches`
sonrası panel "Team Report" biçiminde kalıp Match Day satırıyla yeniden kurulmuyordu → `OpenPage(…, under:)` önce Portal ›
Overview açar (~0,2 sn); 6/6, tarama 49/50.

### 8.164 Altyapı takımları veriden
Kadro satırının takım adı `Human.Team.Club.Teams` ile karşılaştırılır (`\bU\d\ds?\b` kalıbı kalktı). `SquadView` tablonun kimin
olduğuna bakmıyordu (U21 açıkken `depth` onu okuyordu) → A takım satırı yoksa A takım sayfası açılır. `squad --u19` her altyapı
takımını `ObjectUniqueID`'siyle açar; tablo bir an eksik → iki okuma aynı sayıyı verene kadar.

### 8.165 Onay pencereleri yuvayla
`DialogManager.m_currentDialog` (`DialogData`): `Type` (metin kimliği, ör. `Confirm1214855239`) ve dört yuva (`PrimaryButton`…
`QuaternaryButton`: `Text`, `EventID`); `/dialog` her düğmeye `Slot`/`EventID` ekler; eleman adı da yuvayı söyler
(`buttons-button-primary-default`). Kural: ilerleten birincil, geri çeken ikincil → `DialogAnswer(dialog, agree)`, 11 yer
taşındı. Başlıkla tanınan beş pencere kaldı (Instant Result, Team Selection, Injured Players, Replace Existing File, Delete File).
`staff` düşüyordu (Overview yeniden kurulurken tıklama boşa) → kart 3 sn'de gelmezse yeniden tıkla; `?text=` tıklaması gizli
elemanları atlar (`Pages.Shown`).

### 8.166 Düğmeler olayla: katalog ve sınırı
`/press?event=EventToPlugin:EventFmxQuickPick[&name=]` yazısız basar. `tools/buttons-catalog.py` 75 sayfanın olaylı
kontrollerini `docs/reference/buttons.txt`'e döker (~7200 satır, ~4 dk). Sınır: CLI'ın yazıyla bastığı 27 düğmeden 20'si hiçbir
sayfada yok (mesaj içi, sihirbaz, kayıt penceresi, kart içi), bulunanların olayı çoğunlukla genel; kutular
(`layout-tile-interaction-button`) olay taşımaz, dilden bağımsız tutamağı ata elemanın UXML adı (`#AllStaffHero` › `#coaches`).
Sonuç: düğme düğme olay aramak az kurtarır; asıl çözüm oyunun çeviri tablosu (§8.167).

### 8.167 Oyunun çeviri tablosu
`SI.Translation.TranslationManager` (SI.Core): `CurrentLanguage` ("english", "turkish"), `IsAvailable`, `ShowIDs`,
`CppTranslator` (`GetTranslationFromID(int, Span)`, `GetTranslationFromString(string, Span)`; boş değişken listesi
`Il2CppSystem.Span<AddVariableData>.Empty`, `default` NullReference). Arayüz tablosu kimlik 0–~41.000, 40.758 dolu;
`new TranslationID { GameID = n }.TranslatedString` o anki dilde; İngilizce döküm `docs/reference/translations-en.tsv`.
Kimlik dilden bağımsız (1 Job Centre → İş Piyasası, 362 Confirm → Onayla, 6680 Coaching Team → Antrenör Ekibi, 22542 Next
Opposition Report → Sonraki Rakip Takım Raporu). Aynı İngilizce farklı çeviri: 5.403 İngilizce birden çok kimlikte (Confirm
362 Onayla / 369 Kabul; Cancel 370 İptal Et / 3833 İptal; Next 4687 İleri / 8010 Sonraki) → bütün kimliklerin çevirileri
birlikte aranır. `GetTranslationFromString` çevirmez. Motor yazıları bu tabloda yok (`languages/example.ltf` 149 MB, 546.614
`KEY-n`, çalışma anında çevrilmez; çözüm §8.171). Dil tercihi `Preferences/version 6/current_language.xml`.

### 8.168 Çeviri katmanı: Türkçe oyunda tarama
Eklenti İngilizce tabloyu (DLL'e gömülü) yükler; oyun İngilizce değilse "gösterilen → İngilizce" ters tablo (Türkçe 11.537
girdi, 2.376 çakışma, <1 sn); `Text.Clean`/`Text.Plain`/`Report.Json` ekrandaki yazıyı İngilizceye çevirir; `TranslationID`
değerler (`Text.Of`) kimlikten, belirsizliksiz. İngilizce oyunda katman kapalı. Türkçe tarama 21 → 12 hata; parçalardan kurulan
yazılar (mevkiler "D/KB (Sl)", para "17,25B £ aylık", taktik adı) Türkçe kalır. Düz yazı taşıyan yerlerde CLI'ın İngilizcesini
bütün adaylarla eşlemek gerek.

### 8.169 Yazının veri yolu: sayfaları dilden bağımsız okumak
`SI.Bindable.SIText.m_textBinding.Direct.Path.Path` yazının bağlı olduğu yol (`day.DayOfTheMonth`, `Event.Competition.Name`,
`binding.Name`, `Person.PlayerDescription`…; birleştirilmiş metinde boş). `/texts`, `/form` `?paths=1`; `?anchorpath=<yol>`;
`?most=1` aynı çapa alttaki sayfada da varsa en çok yazı toplayanı seçer. Listeler: `SIDropdown.SelectionValueBinding.Path`,
`SelectedValue`; `SICheckBox.SelectionBinding.Path`; `/dropdowns?json=1` `Within` (üst öğe adları), `Value`, `Binds`, `Values`;
`/select?value=<n>` (alt menülü satırın sayısı ilk girişininkini tekrarlar). ⚠ Ağaç gezmek (`KeyPaths()`, ~40.000 düğüm) her
yoklamada oyunu bir kez kilitledi, bir kez kapattı → kimliği ucuz yerden al (`Within`, `Binds`); üst seviye seçenek
`OnSelectionChangedInternal(index)` ile. Alt sekmeler Guid'le (Kaptanlar `f6086796…`, Hiyerarşi `5c93feb8…`, Arkadaş Grupları
`f0783a3c…`, Mutluluk `236ce198…`); `/open?guid=` sekme menülerinde yoksa görünen alt sekme çubuğunda arar (`OpenSubpage`).
`/click?within=Facilities&name=layout-tile-interaction-button`. Ters tablo tuzakları: tek harfli kodlar (Türkçe E/D/T) → kodun
kendi kimlikleri (`Localisation.EnglishAmong`, H/A/N = 27584–27586, 27718–27719); çift çeviri; tekil/çoğul çakışması ("Memnun"
→ Pleased, Content olmalı); `/translate` kendi çıktısını çeviriyordu. Veriden okunanlar: mevkiler `PositionCombined` bit
maskesi (§8.31) + `PositionAptitudeList`; tesisler `Club.Training/Youth/CorporateFacilityRating` (`Rating` + `Description`),
`YouthCoaching`, `YouthRecruitment`, `Stadium.*`; rakip `NextFixture.TeamOpponent` (`Description`, `MostUsedFormation`,
`RecentFormValues`, `NextOppositionThreatLevelAssessment`, `TopGoalscorerPlayer/MostAssistsPlayer/HighestAverageRatingPlayer`);
`MatchVenue`; mutluluk `HierarchyOverallHappinessLevel` (-20…80, eşikleri bilinmez → sayfa yazısı). Görünüm numaraları:
kadro Selection Info 0, General Info 1, Dynamics 12, Injuries 14, Risk Assessment 15; duran top korner 0, serbest vuruş şut 1 /
kanat 3 / derin 4, taç hücum 2 / orta 5 / savunma 6; takvim ay listesi 0 = oyunun ayı. Türkçe tarama 13 → 1, İngilizce 49/50.

### 8.170 Veri yolu okumasının devamı
Veriden: maaş `Human.Club.CurrentWageSpending`/`WageBudget` (haftalık, sterlin); personel ekipleri
`Human.Team.Staff{Coaching,Recruitment,Medical}SubGroup` (`Count`, `MaxValue`, `LongString`, `StaffSubGroupCountFeedbackString`,
`StaffSubGroupJobsData`); taraftar `Human.Club.SupporterProfile.FanPercentage*`; scout odakları
`Human.ScoutingInfo.RecruitmentFocusesList` (`IsFocusComplete`, `ScoutCount`, `AnalystCount`, `RecommendedPlayerPoolCount` =
eşleşen, `NearMissPlayerPoolCount`, `CurrentPlayerPoolCount`); altyapı `Human.Club.IntakeDate`, `YouthCandidatesCount`; lig
`Human.Team.Division.Name`. `/pull` `GameDate`'i `yyyy-MM-dd` verir (`/state` ham okur). Derin satırlar: altyapı adayının adından
sayfaya 30 üst, rakip raporunda `Team.Description`'dan 14 üst; tutunma noktası her dilde yazılan bir yol olmalı. Radyo grupları
`Items` + `Binds` verir. Kulüp karoları `Supporters`, `ClubAllStaff`; scout raporu karosu `player-recommendation-cell-remapper`;
altyapı alt sekmeleri Environment `1ebaf239…`, Performance `80bb0128…`, Candidates `e88d1df4…`. `offers <#> view` kartı açık
kalınca direktör konuşması "açık soru" görünüyordu (portal açılarak kapatılır).

### 8.171 Motorun yazıları: `languages.fmf`
Simülasyonun yazdığı her şey (kişi menüsü, seans adı, kişilik, scout yorumu, rol adları) `fm.app/Contents/PlugIns/
game_plugin.bundle/Contents/Resources/data/game_simulation/languages.fmf`'te. `ShowIDs` açılınca UI yazılarının başına kimlik
gelir (`14593:Genel Bakış`), motor yazılarına gelmez. Biçim: 26 baytlık başlık (`02 01 'fmf.'`), 4 KB zstd çerçeveleri + atlanabilir
çerçeveler (`5E 2A 4D 18` + uzunluk); açılınca ~840 MB, dil başına iki blok (40 blok, 20 dil): `03 01 'ctl.' 10 00`, `u32 boyut`,
`u32 0`, `u32 ad uzunluğu`, ad, `u32 ilk` (1001 büyük / 0 küçük), metin havuzu (`u8 n` + n kez `u32 uzunluk + UTF-8`; n>1
varyantlar), havuz `blok başı + 8 + boyut`'ta biter, sonra dizin (`u32 sayı` + `u32 anahtar` (alt 24 bit kimlik, üst bayt
varyant) `u32 konum`, konum havuzdaki en küçüğe göre). Kimlik bütün dillerde aynı. CLI (`EngineTexts.cs`, ZstdSharp) oyun
İngilizce değilse bir kez açar (~4 sn), `~/.config/fm-cli/engine/<dil>.tsv` yazar (Türkçe 4,8 MB), `/translate?engine=1&reload=1`
ile yükletir; UI karşılığı öncelikli; parçalı metinler (`[%male#1]…`) ve 4 harften kısa kodlar alınmaz ("D" → "A" mevkileri
bozuyordu); birden çok İngilizceye karşılıksa en sık olanı. Önbellekler dile/kariyere ayrılınca `competitions.json` yeniden
kurulur.

### 8.172 Para biçimi oyunun biçimlendiricisinden; menü satırlarının kimlikleri
`FM.UI.ChartCurrencyVisualFunctionLibrary.TranslateCurrencyValue(double)` → `/money?values=`. Türkçe: 999 → "999 £", 1500 →
"1,5B £", 1,5M → "1,5M £", 1,5 milyar → "1,5MR £"; 673621 → "673B £" (keser). İngilizcede "B" milyar, Türkçede bin. CLI
(`MoneyFormat.cs`) süreç başına bir kez 999/1500/1,5M/1,5MR örneklerinden harfleri, ondalık işaretini, simgenin yerini öğrenir;
ayar değişince yeniden. `/menu?…&ids=1` satırın `ID`'si (dört harf: 'pval', 'ppos') ve kaydın `Value`'su; nitelik satırları
'pf20', niteliğin kendisi içte (Pace 'Ppac', One on Ones 'Pooo', Heading 'Phea', Vision 'Pcre', Leadership 'Pinf'); mevki satır
kimlikleri mevki bitleri; koşul listesi `conditiontoadd`. 'RTid' çeviri numarası değil. Oyuncu kartı nitelikleri
`AttributeTypeInfo.ID` (0 Aerial Reach … 10 One On Ones, 11–24 teknik, 25–38 zihinsel, 39–46 fiziksel; 42 Pace) → ad numaradan.
Geri çevirinin sınırı: motor tablosunu öne almak "Build Up"ı Game Setup yaptı (geri alındı) → doğru çözüm kimlik ya da gösterimde
oyunun yazısı. Dil değişince ekranlar hemen, motor yazıları oyun yeniden açılınca.

### 8.173 Gösterim oyunun dilinde (Faruk: "Adamın oyunu tr ise tr görecek")
İçeride komutlar İngilizceyle eşleşir; kullanıcıya oyunun o an gösterdiği yazı çıkar, geri çeviri yok. Eklenti `Localisation.Saw`
her çeviride oyunun gösterdiğini `Seen`'e yazar, cevap `X-Shown` başlığıyla döner (base64, `ingilizce<TAB>gösterilen`; İngilizce
oyunda yok). CLI `GameWords` öğrenir; `Table.Write` hücreleri, `Ui.Safe` markup'ı çevirir; `Run` çıktıyı `GameWordsLines` ile
sarar (sekme, 2+ boşluk, ` · `, `, `, `: `, parantez parçaları; hizayı korur; tek harf değişmez); `--json` dokunulmaz. CLI'ın oyun
yazısından parça kestiği yerlerde önce `GameWords.Of`. `shortlist` döşemeye ilk basış yutuluyordu → 3 sn'de bir yeniden basar.

### 8.174 CLI'ın kendi yazıları oyunun dilinde
Eklenti her cevaba `X-Language` (`TranslationManager.CurrentLanguage`); CLI `Labels.Heard` tutar, `~/.config/fm-cli/language`'a
yazar. `L($"…")`: `Labels.Template` interpolated string handler (delikler `{0}`, `{1,-24}`, `{2:0.0}`), şablon
`cli/fm/labels-english.tsv`'de aranır, yoksa yazıldığı gibi; `await` içeren delik → `Lf("… {0} …", değer)`. Türkçe oyunda CLI
Türkçe, başka her dilde İngilizce. Araçlar `tools/labels/`: `wrap.py [--all]`, `dicts.py`, `keys.py --missing`, `merge.py`
(delikler tutmazsa reddeder, kenar boşluklarını korur), `unwrap.py`, `rest.py`. ~2.300 şablon. CLI'ın kendi Türkçe sözlükleri
(mutluluk, hiyerarşi…) kalktı, oyunun kelimesi `GameWords` ile; `GameWords.InLine` sütun kaymasını taşır. Kendi etiketiyle
karşılaştıran kod `L(...)` ile. İngilizce eş girdiler (`targets "left back"`, `dugout <no> apply`, `prep in|out|sp1|sp2`).

### 8.175 Rol adları; İngilizce oyunda oynama akışı
Rol yalnız kod adıyla tutulur (`BallPlayingGoalkeeper`); taktik ekranındaki rol listeleri rol bitini (`Roles`) ve oyunun
dilindeki adı taşır → CLI `"ip:4096" → "Ayağı İyi Kaleci"` eşlemesini bir kez okuyup `role-names.json`'a yazar (bazı satırları
oyun çizerken çevirir → `GameWords.Of`). Sarma aracı İngilizcede de kelime olan Türkçe yazıları kaçırmıştı (`"Boy"`, `"GOL"`…).
İngilizce oyunda `reply`, `offer`, `contract`, `talk`, `register`, `numbers`, `match --play` çalıştı. Teklif ekinde "Coach
Report" yerine "Scouting Report" olabilir. Etikete tek boşlukla yapışan oyun kelimesi `InLine`'da ayrı parça sayılmaz →
yapıştırmadan önce `GameWords.Of`. Kayda kuruluş dilinde yazılan adlar (ülke, sakatlık, "Hazırlık") dil değişince eski dilde kalır.

### 8.176 Mesajın gövdesi ve ekleri kimlikle
Açık mesajın gövdesi `MessageBodyText` öğesi → `/texts?anchorname=MessageBodyText&up=5&paths=1&most=1`: gönderen, başlık
(`newsitem.NewsTitleString`), saat, gövde (`newsitem.NewsBodyString`), "Attachments", ek; mesaj başlığıyla tanınır. Eskiden
gelen kutusunun `NewsFirstBubbleSpeechString`'iyle aranıyordu; cevaplanmış mesajda o alan son cevabı taşır. Yolla okunan ekler:
antrenman özeti, kondisyon testleri (`Team.PreMatchFitnessTestString`), personel ekipleri, sorumluluklar
(`teamsettings.ResponsibilitiesAssignedTo{Human,Staff}Count`), antrenman programı (`day.DayOfTheMonth`, `Event.SessionName`,
`Event.MatchVenueShort`), oyuncumuza teklif (`Person.PlayerDescription`, `feestring`, `report.ScoutedGradeInfo.GradeString`,
`row.TransferOfferType/TotalTransferCost`), imza kartı (`contract.Wage/WageAfterTax/ContractStatusWithType.DisplayValue`,
`contractaddon.AddOnNameString/ValueString`), teklif şartları (`binding.PropertyValue/DisplayValue`), transfer odakları
(`Human.ScoutingInfo.RecruitmentFocuses.*FocusesCount`, `focusitem.Name`), oyuncu raporu, önerilen kadro
(`teamselectionadviceentry.TeamSelectionReason`), takım dinamikleri (`binding.ProConsLongString`), kayıt kartı
(`nextsquadreg.RegistrationDaysLeft`), forma numaraları (`Team.SquadNumbersAssigned`). "Teklif Yap" eklentide "Make An Offer"
dönüyordu, CLI "Make Offer" arıyordu → `Labelled` artık `GameWords.Same` (iki İngilizce oyunun dilinde aynıysa aynı). Genel
okuyucu eki yalnız `newsitem.NewsBodyString` varsa ya da yola bağlı yazı ≤ 3 ise "haber" sayar. Oyunun dilinde yazılmayanlar:
konuşma tepkileri, seçilen söz, mesaja verilen cevap, `next`'in "İlerle butonu". Haber yaşa göre sıralanamıyor (açık).

### 8.177 Konumla okumaların taraması, yeni ek türleri, yüzde
93 konumla okuma tarandı: `After("Supporters")` dilden bağımsız (eklenti etiketleri İngilizce verir); kırılanlar yalnız motorun
kurduğu cümleleri İngilizce parçayla arayanlar (`budget ayarla` → `budget.BudgetAdjustmentBoardString`; training-report →
oyuncunun adıyla). Yeni ek okuyucuları: Progress Report (`row.Name`/`row.PropertyValue`), Scout Report (`row.ProConsLongString`),
"Report" kartı, Player Recommendations, Data Hub (`row.AnalysisPointString`), maç hazırlığı (`row.MatchFocusSessionName`),
sözleşmesi bitecekler, galibiyet primleri. Prim kartında seçili düğme veride yok (`SIStyleSetter.m_addedClass`'ta "-active-").
stderr ve tırnak içi yazılar da artık çevriliyor. Maç içi değişiklik Türkçede yoktu (kulübe satırının yazısı Choices'a gelmiyor)
→ menüler `Within` `BindingRoot_SquadOverviewPlayerBlock` ile, kulübe ilk satırı sayaç olan alt menüyle. Yüzde: Türkçe "%70";
işaretin yeri "50%" kaydının bu dildeki yazısından (`/translate?words=50%`, kimlik 21068); CLI `InvariantGlobalization`.

### 8.178 Mesajın cevapları: önceki mesajınkiler bir süre kalır
Başlık (`newsitem.NewsTitleString`) hemen yenilenir, cevap başlıkları (`action.ActionTitle`) ve `SpeakToAction` düğmeleri ~1 sn'ye
kadar öncekinde kalır (işaretçiler havuzdan, ayırt edilemez). "Başlık sayısı kadar düğme" yanlıştı. Tutan işaret: başlık bu
mesajın + cevap başlıkları ile düğmeler aynı kümeyi söylüyor + küme önceki mesajınki değil; cevapsızda `CanRespond = False`.
Türkçe 24/24, İngilizce 14/14, başka sayfa açıkken 4/4; 1–2 sn (en çok 4). Aynı cevaplı iki mesaj art arda ayırt edilemez (8 sn
sınırında ekrandaki alınır, doğru).

### 8.179 `/pull` yeniden okumaları
`Bridge.Pull`/`Numbers`/`Strings` 250 ms arayla yeniden okur (6 / 4 okuma); yeni bağlanan özellik ikinci okumada gelir, ~5 kat
pay. Olmayan yol ile henüz gelmemiş değer aynı cevabı verir (`{}`) → `table` olmayan dört yarışmayı sınırına kadar bekliyordu;
sekiz ad `Human.Team`'den tek okumada (`CurrentComp{i}.Name`) istenince olmayanlar yalnız eksik kalır (`PullEach` iki dolu tur
aynı gelince biter): 8,7 → ~2,6 sn. Bağlı ama `IsNull` kökün "yok" bildirilmesi denenmedi.

### 8.180 Antrenör ataması: kutu hemen, sayfa yarım saniye sonra
`setcontrol` sonrası kutu ~0,1 sn'de döner, yıldızlar/iş yükü eski; 0,3–0,55 sn'de sayfa yeniden kurulur (arada form eksik: 271
yerine 231–244 öğe). Atanmışın o alanda yıldızı var, alınanınki yok → bitti işareti. "Ask Assistant to Assign"
(`EventFmxAssistantAssignCoaches`, hedef `currentteam.TeamTraining`) ~0,4 sn'de değiştirir; seçim zaten oradaysa işaret yok. Sayfa
sırayla dolar: başlıklar + iş yükü (33 öğe) → yıldızsız kutular → yıldızlar → en son antrenör adları (bir süre satırda yalnız
görev yazısı). Her kutunun yıldız öğesi var (atanmayanda `Shown` 0) → tam formun işareti "her satır ad + görev + dokuz kutu/yıldız,
iş yüküne kadar".

### 8.251 `fm settings` onayı artık olayla (`PreferencesConfirmChanges`) basılıyor (19 Ekim, TR + EN)

- Nerede: `Settings.cs` yazma yolu, ayar değiştikten sonra `click ?text=Confirm&name=ConfirmButton`. Etiket oyunun
  kendi sözü (TR'de bugün "Confirm" geliyor, başka dilde çevrilebilir); `Name` şablon adı. Preferences sayfasında
  `/buttons?json=1`: `ConfirmButton` → `Events = PreferencesConfirmChanges, PrivacyPolicyConfirmSelections,
  ClosePanel`, `Enabled=True`; TR + EN'de birebir. Yeni kod bu olayı taşıyan açık düğmeyi işaretçisiyle basıyor
  (`click ?ptr=0x425b90800`), bulamazsa eskisi gibi yazıya düşüyor.
- Kanıt: `fm settings tarih` (TR) / `fm settings "date format"` (EN) ile `mm/dd/yyyy` ↔ `dd/mm/yyyy`, üç durum
  (başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / preferences elle açık bırakılmış) × iki dil; eski
  derleme (`git stash push -- cli/fm/Settings.cs` + build): altı koşu iki dilde de **birebir**, rc=0, çıktılar
  `dd/mm/yyyy → mm/dd/yyyy` / tersi. İz (eski → yeni): `click ?text=Confirm&name=ConfirmButton` →
  `buttons ?json=1&gen=…` + `click ?ptr=…`; onaydan sonraki "All Preferences kapandı mı" beklemesi aynı.
- Not: değişiklik yalnız yazma yolunda; okuma yolu (`PreferencesTab`) ve diğer ayarlar dokunulmadı. Bu turda
  paylaşılan koda (Bridge/OpenPage/reply/ortak yardımcı) dokunulmadı; dil TR'ye döndü, tarih biçimi `dd/mm/yyyy`.

## 8.x Ek: kısaltırken metinden düşen adlar (bölümüyle)

- §4–5: `Bindings.s_pathComparer` (yol karşılaştırıcısı, büyük/küçük harf duyarsız), `EmbeddedDataHandler`,
  `NavigationManager.SetNavigationState/SetPanelFocus`, `get_Instance()`, `IGameModule`, `IBindableElement`; ilk keşifte
  `strings global-metadata.dat`; ilgili DLL'ler `FM.UI.dll`, `SI.Core.dll`, `SI.UI.dll`, `FMGame.dll`.
- §8.2/8.21: denenen span yolu `fixed (char* p = s)`; köprü denemeleri `rawexists`, `HandlerAccess.Set`, `AddAlias`.
- §8.11: `GetPropertyDescriptionInternal` release'te boş. §8.13: `npcaction`. §8.16: iskelet düğümde `IsPersonFemale`,
  kolon `OverallAppearances`. §8.19: ⌘H denemesi `set visible of process ... to false`. §8.25: `EventToPluginContext`,
  `"selectedadviceoption"`. §8.26: `CoachBestPosition`. §8.27: `…NavigationTabPortalBindingVariables12.selectedSubtabElement`
  (`selectedSubtabElement.PropertyValue`), `SelectedIndex`. §8.28: `ClubScreen` (`PanelID = 3706171392`).
- §8.30: `TacticCreatorTacticalStyle` (stil seçilmeden −1). §8.23/8.33: `ConversationRoomAtmosphere`, `TotalNumberOfQuestions`.
- §8.34/8.62: `EventFmxHighlightSpeedChanged`, `EventFmxBetweenHighlightSpeedChanged`, `EventFmxRequestInstantResult`;
  `HavePendingTouchlineShout` bağlanmadı; `MatchHighlightModes`. §8.36: `ProcessSelectionEvents()` göndermedi.
- §8.38: `SearchFor`, `OpenPlayer`. §8.39: MCP `mode=play`. §8.42: `TransferOfferTransferBaseOptions`,
  `TransferOfferAdditionalClauses`, `…transfer-offer-remapper7…binding-variables0.transferoffer`,
  `…cash-list-remapper.combobox-validation` + `ValidateValueEvents` (işe yaramadı), kilit
  `…clause-variables1.lock-button-remapper7.lock.LockStatus`, `…binding-variables0.SelectedClause`; alt menü
  `SIDropdown.m_childrenDropdowns`, `m_selectionPath`, `HandleDropdownSelection(ChangeDropdownSelectionEvent)`.
- §8.43: `OpenRegistration`, `RegisteredPlayers`, `m_onClick`/`m_onSubmit` (seçim kutusu), `Registration.Scroll`,
  `EventFmxSelectPlayer` (24). §8.44: `WeeklyWage` (geçersizlik nedeni bileşeni). §8.45: `SIDropdown.ShowDropdown()`,
  `UpdateDropdownSelectionBinding(chosen)` (yanlış: menü satırını yazıyordu). §8.48: `TacticsTeamInstructionShowMenu`.
  §8.49: veri `OppositionPlayer…`, `…OppositionInstructionTable…Items.N.binding`. §8.62: `ptyout.py` (stdin kapalı sahte
  terminal). §8.64: `m_inPossessionBindingPath`, `TacticRoleMenuShowEvent`, `AdviceOptionsSelectionEvent`.
- §8.80: `ClubAppearancesThisSeason`, `ClubGoalsThisSeason`, `ClubAssistsThisSeason`, `ClubAverageRatingThisSeason`,
  `InjuriesLastSeason`, `valueCount`, `fmcli.card.p…`. §8.81: `BoardConfidenceOverallString/Grade`, `SupporterFeedback…`,
  `CompetitionStatsPlayerStatsListReference`, `CompetitionSiteStatsCard`, `TileInteraction = False`. §8.84: `VirtualisedList`.
  §8.85: `CoachPotentialAbilityStarRange`, `ScoutPotentialAbilityStarRange`, `…Professionalism`, `…Temperament`.
- §8.88: `TrainingSessions`, `CurrentTrainingSession`, `TrainingSessionCategoryDropdown`, `TrainingScheduleDropdown`,
  `LoadTrainingScheduleType`; takvim kapalı hücre `enabledInHierarchy = false`. §8.90: `IsDragCellActive`, `…playerselected`,
  `EventFmxSetPiecePenaltyCandidatesAdd`. §8.92/8.113: `AbilityGraphDataRecent`. §8.93: `const.One`, `…-inactive-regular`.
- §8.95: `SocialGroupFeedbackString`. §8.96: `ContractBonusesCount`, `contractaddonlistreference`. §8.101:
  `InternationalAppearancesThisSeason`, `InternationalGoalsThisSeason`. §8.102: `MentoringGroupRelationship1`.
  §8.103: `HappinessPositives`. §8.104: `ExpandedToggle`. §8.106: `PanelHistoryBack`.
- §8.116/8.118: `GenericActionEvent` (bir `ActionReference` ister). §8.119: `/click … arg=1`, `keep=1`. §8.124:
  `EventFmxCancelScoutingPlayer`. §8.128: `CGWindowList` (pencere 1470×920, ekranda değil).
- §8.129: `PlayerStatsDropdownCategories: Gender`, kart ağacında `StatFlagC › Type = 27`, `ColourSpaceConversion`,
  `UIR.RenderChain.ProcessChanges`, `EncodeToPNG` interop'tan çağrılamaz. §8.130: `MatchFocusAvailability` = 2 (anlamı
  bilinmiyor), olaylar `…SelectOutOfPossessionType`, `…SelectFirstSetPiece`, `…SelectSecondSetPiece`. §8.131: `BudgetOverview`.
- §8.132: `Tactic.InPossessionInstructionList`, `ShowCurrentTeamInstructions`. §8.134: `SpeechBubbleHistory`,
  `OptionalActions`. §8.137: `PlayerFromTeamSelectionIndex`. §8.143: `HttpListener` (dinleyici), `MigratePanelIDBinding`
  exception, `ClubButton`, `JobMarketTableBlock`, `EventFmxDeclareInterestInJob`, spindump `~/Desktop/fm-hang.txt`, `jobs.py`.
- §8.144: `PanelManager-container › Card › JobMarketReport`, `GridLayoutElementContent`, `Unload`. §8.146: `Says` (`/talk`).
- §8.148: `StatusTitleFull`. §8.149: `fmcli.who.team`, `fmcli.who.human`. §8.154: `MatchPlayer.PlayerStatus` (`Tactic`
  değişkeni ister, verilince de boş), `m_hasPendingTacticalChanges`. §8.163: `Next(label)`, `page[at + 2]` (eski konumla okuma).
- §8.169: `setpiece.SetPieceTypeDropdownListSelectedValue`, `setpiece.PenaltyChangeIfMissed`, `PlayerSearchMainContentBlock`,
  `Club.TrainingFacilityRating/YouthFacilityRating/CorporateFacilityRating`, `TrainingGround`, zemin `Surface`. §8.170:
  `Reply`/`WriteAttachment` (~45 konumla okuma). §8.172: eski `Money()`. §8.173: `Map()`, `Shown(...)` (Program'da
  ad çakışması → sınıf `GameWords`), `Console.WriteLine`. §8.176: `team.Staff*Count`, `HappinessPositivesCount`.
  §8.177: `#low-/#middle-/#high-button`. §8.179: `FM_TRACE=1` (izleme), `Human.Team.CurrentComp5`.

## 9. Kaynaklar

- [oasis1701/footballmanager-accessibility](https://github.com/oasis1701/footballmanager-accessibility) — NVDA ekran okuyucu
  eklentisi; `FMNavigationManager.CurrentFocus` kullanımının canlı örneği.
- [DadMych/fm26-player-export-macos](https://github.com/DadMych/fm26-player-export-macos) — arm64 BepInEx bundle + yamalı
  Il2CppInterop; bu proje onsuz M2'de yürümezdi, eklenti deseni de buradan.
- [Thunderstore BepInExPack FM26](https://thunderstore.io/c/football-manager-26/p/BepInEx/BepInExPack_FootballManager26/) — resmi pack (x64).
- [FM26 AI Dialogue mod](https://www.fmscout.com/a-fm26-mod-ai-dialogue.html) — eklenti içinden localhost'a HTTP atılabildiğinin kanıtı.
- [Perfare/Il2CppDumper](https://github.com/Perfare/Il2CppDumper) — dump aracı.

FM'i dışarıdan bir ajana/CLI'a oynatan mevcut proje bulunamadı (Nexus, ModDB, Thunderstore, FM Scout, sortitoutsi; 2026-09).

## 10. Yeni bulgular (en yeni en altta)

Buradan sonrası `cat >>` ile eklenir: `### 8.N Başlık (tarih, dil)` + 3–6 satır.

### 8.181 `next` beklemesi ölçüldü: başlama ≤1,24 sn, bitişte ≤0,53 sn titreme (14 Ekim, Türkçe)
`/next` sonrası `/state` ~25 ms'de bir okundu, 16 ilerleme (27 Tem → 1 Ağu, mesaj/teklif/maç günü durakları dahil). İlk
değişiklik 0,34–1,24 sn'de (`Settled` 4 sn bekliyor). `Processing` bitince durum kısa süre oynuyor: CanContinue →
NewsAggregator → CanContinue (0,53 sn), CanContinue → NeedAction (0,1 sn), CanContinue → Fixtures → Tactics (0,42 sn);
"iki okuma 700 ms arayla aynı" bunu örtüyor. Bir adımda `Processing` hiç gelmedi, yalnız saat 62 → 65. Durak sonrası
"basış yutuldu" bu 16'da görülmedi. `game.GameManagersNotContinuedCount` (1 → 0) `Processing` ile aynı anda değişiyor,
ayrı bir işaret değil. Kod değişmedi.

### 8.182 Sözleşme alanları: bağlanmamış `{}`, boş şart "-" (14 Ekim, Türkçe)
`contracts <oyuncu>` `FullContract`'ın 26 alanını tek tek soruyor. Hiç okunmamış bir satırda ilk soru `{}` dönüyor; bir
sonraki okumada (ilk sorudan 0,4–0,9 sn sonra, araya bekleme koymadan) 26 alanın hepsi gelmiş: değeri olmayan şart "-",
"No" ya da "N/A" — hiçbiri `{}` kalmıyor. Yani `{}` yalnızca "henüz gelmedi". Sabit 300 ms kaldırıldı; `{}` dönen alan
0,1 sn arayla 5 sn'ye kadar yeniden okunuyor. 27 oyuncu iki kez okundu (ilki çoğunda ilk okuma): iki çıktı aynı, 27/27 tam.
İngilizcede denenmedi (işaret yazıdan bağımsız).

### 8.183 Finans: kart değil kulübün verisi (14 Ekim, Türkçe)
`finances` ekrandan yazıyla okunuyordu ("Overall Balance" kutusu, "Remaining Debt"ten 16 üst, "Item" başlığından sonrası,
"Total"in yanı). Türkçede döküm hiç çalışmıyordu: sütun başlığı "Madde" eklentiden "Clause" diye dönüyor (§8.167'nin
kopya çevirisi), "Item" bulunamıyor. Döküm kartı açık kalınca sonraki çalıştırma da düşüyordu (sayfa yeniden açılınca kart
kapanıyor, "Genel Durum" kutusuna tıklama kapanan kartın altına gidiyordu; kartın varlığı `#Card` katmanının `#Body`'si
dolu/boş diye görülür). Kartın her kutusu kulüp verisine bağlı; hepsi `/pull` ile, sayfa açmadan geliyor:
- Bakiye `Human.Club.OverallBalance`; bu ay `TotalIncomeThisMonth`, `TotalExpenditureThisMonth`; borç `TotalDebt` /
  `OriginalLoanDebt`; sponsor toplamı `TotalSponsorshipIncomeThisSeason`; FFP `FfpData.IsPassingFfp`,
  `FfpData.Competition.Name` (`FFPDetailsReference`, 30 alan; `/schema?filter=FFPDetails`).
- Döküm `{Income|Expenditure}Breakdown{ThisMonth|LastMonth|ThisSeason|LastSeason}` (liste; `Name`, `PropertyValue`
  sterlin, `DisplayValue` kısaltılmış), toplam `Total{Income|Expenditure}{dönem}`, ciro `TotalIncome{dönem}Turnover`.
  Gider 21, gelir 17 kalem; 6 bileşimde kalemlerin toplamı toplamı birebir tutuyor. Ekrandan okuma kaydırmada kalem
  atlıyordu (geçen sezonun en büyük kalemi Oyuncu Maaşları 3,9M yoktu).
- Maaş türleri `WagesData.PlayerTypeWageData` (`Name`, `Quantity`, `TotalBasicWage` oyunun gösterim döneminde); son satır
  bütün kadro (sayısı ötekilerin toplamı). Sponsorlar `SponsorshipsData` (`Name`, `SponsorshipValuePerSeason`, `DateString`,
  `Duration`, `PropertyValue` = toplam). Aylık bakiye `FinanceBalanceHistory` (`GraphPointX` ISO tarih, `GraphPointY`).
  Projeksiyon `ProjectionBalance` / `ProjectionTurnover` `.ProjectionEndOfSeason{One|Two|Three}` (`ThisSeason`,
  `PropertyValue`); kart Two ve Three'yi gösteriyor. `Title` oyunun dilinde ("2027/28 sezon sonu"), kullanılmıyor.
- Listeler tembel bağlanıyor: ilk soru "is not a list", sonra alanları boş satırlar, sonra tam (`Fm.Pull` bunu bekliyor).

Kaydırma ölçümü (artık kullanılmıyor, Happiness/Numbers/Matches/Staff'ın 400–500 ms'leri için): `/scroll` `to` verilmezse
yalnızca konumu söyler ("0 of 586.2"); kaydırınca görünen satırlar tek adımda, ara durum olmadan yenileniyor (7 denemede
50–130 ms). Doğru işaret: konum zaten hedefteyse bekleme yok, değilse görünen satırlar değişene kadar. Eklentiye
`texts?within=<öğe adı>` eklendi (adlı bir panelin içindeki yazılar, `paths=1` ile). Kulüp sayfasındaki kutular
`BindingRemapper #Finances`; kartın kutuları `#FinancesIncome`, `#FinancesExpenditure`, `#FinancesDebtsAndLoans`,
`#FinancesFfp`, `#FinancesProjections`, `#FinancesSponsorshipsTile`; döküm kartında sekmeler `#ThisMonthTab`…, seçili olanın
`#Selected`'ı dolu, toplam `#TotalExpenditure`.


### 8.184 Quick Pick: seçim tek adımda ~0,45 sn'de, "bitti" işareti yok (14 Ekim, Türkçe)
Basıştan sonra `/tactic` 0,1 sn aralıkla izlendi: yerine yedek konmuşsa on bir tek adımda, ara durum olmadan değişiyor
(8 denemede 0,33–0,54 sn); zaten seçiliyse hiç değişmiyor. Kuyruk (`/queue`) 0,10–0,15 sn'de boşalıyor, değişiklikten
önce; `/state` hiç değişmiyor — başka işaret yok. `tactic pick` artık 0,25 sn'de bir okuyor: değiştiyse bir sonraki aynı
okumada, değişmediyse 1,5 sn sessizlikten sonra bitiyor (≈3 kat pay). Eskiden her okumadan önce 1 sn bekleyip iki aynı
okuma istiyordu: 3,7 → 1,2 sn (değişen) / 2,2 sn (değişmeyen); 10 çalıştırmada doğru. Ayrıca başka sayfa açıkken
`/tactic` bağlı kaldığı için taktik sayfası açılmıyor, "no control labelled 'Quick Pick'" ile 9 sn sonra düşüyordu: önce
düğme kuru basışla aranıyor, yoksa sayfa açılıyor (Mesajlar'dan 3/3, 2,2–2,4 sn).

### 8.185 Mutluluk listesi: kaydırınca satırlar iki adımda diziliyor (14 Ekim, Türkçe)
§8.183'teki "kaydırınca görünen satırlar tek adımda yenileniyor" Happiness listesinde tutmuyor: 1 → 0 ve 0 → 1 kaydırmada
önce 15 satırlık bir ara hal (0,14–0,25 sn), sonra 21 satır (0,2–0,33 sn); 0 → 0,5'te tek adım (27 satır, liste 27 oyuncu,
uzunluk 670,9). 0,1'lik her adım görünen satırları değiştiriyor (1–2 satır). `happiness` artık: takımın oyuncuları kulüp
verisinden (`Human.Team.Players`), liste 0 / 0,5 / 1 (gerekirse aradaki onda birler) kaydırılarak her oyuncunun satırı
okunana kadar geziliyor; her adımda okuma "kaydırmadan öncekinden farklı ve art arda iki kez aynı, boş değil" olunca kabul
(konum değişmediyse beklemeden). Oyuncu seçerken de satırı görünür olana kadar aynı yol. Ölçüm: zaten açık 5/5, ortaya ya da
sona kaydırılmış 2/2, Mesajlar'dan 17/17 aynı çıktı; tek oyuncu (üst/orta/alt) 4/4 ~3,8 sn, `--why` 27/27 11 sn.
Başka sayfadan açılışta 1/10 "okunamadı": önceki adımın `fm open Messages`'ı geç bitip Mutluluk'un üstüne gelmiş (yol
"Takım Dinamikleri › Mutluluk › Mesajlar"); Messages oturduktan sonra 12/12. `open` açılış bitmeden dönüyor (A "Alt sayfa").
Dil: seviye yazısının veri yolu yok, `HierarchyOverallHappinessLevel` (80 Delighted, 60 Very Happy, 40 Happy, 20 Slightly
Happy, -4/-20 Content, -40 Slightly Unhappy, -64 Very Unhappy) sırayı tutuyor ama eşikleri bilinmiyor; satırdaki simge
`morale-happiness-1..5` + bir ara simge, 8 seviyeye bire bir değil. "Biraz Mutlu"/"Biraz Mutsuz" ne UI tablosunda ne motor
yazılarında (motor 268883224–231: Delighted, Very Happy, Happy, …, Unhappy, Very Unhappy; 227–229 İngilizce dökümde yok) →
Türkçede bu iki seviye çevrilmeden geliyor, sıralama ve renk yanlış. Moral (`Person.MoraleString`) İngilizce gösteriliyor.

### 8.186 Bireysel antrenman: veriden okuma, satır kimliğiyle değiştirme (15 Ekim, Türkçe)
Okuma sayfasız: `Human.Team.Players` satırlarının yanına `Judge = Human` (`/pathset`; `OOPTraining`, `AdditionalFocus` ve
seçenek listeleri bu değişkeni istiyor, `/contexts`), sonra `<oyuncu>.PlayerTraining` → `TrainingPosition.NewPositionAndRoleRow`,
`OOPTraining.IndividualOOPTrainingRoleRow`, `AdditionalFocus.IndividualTrainingTypeRow` (`DisplayValue` + `PropertyValue`;
mevki/rol bit maskesi 4096, 274877906944 → int'e sığmıyor). `PlayerTraining.IsVisible` = sayfadaki tabloya girenler
(rehabdaki iki oyuncu False; tablo 25, takım 27). Eski kaydırma taraması bu iki oyuncuyu zaten göstermiyordu; 7,1 → 2,3 sn.
Değiştirme: tablodaki listeler `EventToPlugin` gönderiyor (`m_targetHandler` "training" = oyuncunun `PlayerTraining`'i,
`m_dataToSendBinding` "trainingposition"/"additionalfocus" = `TrainingPosition`/`AdditionalFocus` nesnesi; liste seçilen satırı
bu nesnenin içine yazıp nesneyi gönderiyor) → sayfasız göndermek nesneyi yerinde değiştirmeyi gerektirir (kilitlenme riski),
denenmedi. Bunun yerine eklentide `/rowcontrols?event=<id>&gen=`: o olayı gönderen her liste, satırının öğesiyle
(`StreamedListView.m_boundElements` satır öğesi → kaynak sırası, `GetItemFromSource`; `IndexOf` satırı değil kaydırma kutusunu
buluyor) — öğe `fmcli.rowctl.g<gen>.<k>`'ya konur, CLI `UniqueId`'yi okur. Olaylar: `EventFmxPlayerTrainingPositionChanged`,
`EventFmxPlayerOOPTrainingRoleChanged`, `EventFmxPlayerTrainingFocusChanged`. Sonuç oyuncunun verisinden doğrulanır (değişene
kadar; gelmezse "oyun değişikliği almadı"). Görünüm listesi `Binds == "selectedView"`, Liste = değer 1 (Ayrıntılı = 0'da tabloda
liste yok, kart oyuncu seçilince açılıyor). Bu tablo satırları sanallaştırmıyor: sayfa 806 px kaydırılınca da 25 satır bağlı;
kaydıran dış sayfa, tablonun `ScrollToElement`'i (`OnScrollToElementBindingValueChanged`) konumu değiştirmedi → kaydırma yazılmadı,
satır bulunmazsa açık hata. Ölçüm: liste 3/3 aynı (eskisiyle aynı 25 satır), değiştirme 10/10 + 3'lü tek komut + alt menüler
(mevki, topsuz rol) doğru ve yalnızca hedef oyuncuda (tam liste farkı), başka sayfadan / sayfa açıkken 3–4 sn. Girdi seçeneğin
eklenti yazısıyla ve oyundaki karşılığıyla (`GameWords.Of`) eşleniyor ("Çabukluk", "Cabuk" → Quickness); eskiden yalnız
eklentinin İngilizcesiyle. Türkçe oyunda İngilizce alt rol adı tutmuyor (rol adları oyunun dilinde geliyor). İngilizcede ve
küçük ekranda denenmedi.

### 8.187 Antrenman yoğunluğu: veriden okuma, Bireysel Antrenman tablosundan değiştirme (15 Ekim, Türkçe)
Okuma sayfasız: `<oyuncu>.PlayerTraining.TrainingIntensityOrRest` (`team` değişkeni ister → satırların yanına
`team = Human.Team`): `IndividualTrainingIntensityRow.DisplayValue/PropertyValue`, `RestDaysRemaining`, `HolidaysRemaining`,
`IntensityRecommendation`, `CanEdit`. `PropertyValue` listenin seçenek numarasıyla aynı ve dilden bağımsız: Otomatik −1,
Yarım 0, Normal 1, Çift 2, dinlenme 1–6 gün 10–15, 1 hafta 16, 2 hafta 17, tatil 1–4 hafta 20–23 (`/select?value=` alt
menüdekileri de seçer). Öneri `10000 + numara` (0 = öneri yok). Eski kaydırma taramasıyla aynı 25 oyuncu, ekrandaki 25
listeyle (satırın `UniqueId`'si) birebir; 5,9 → 2,3 sn. Görünmeyen oyuncular (rehab, `IsVisible` False) daha az alan
döndürüyor → `PullEach` onları "tam" sayamayıp turlarca okuyordu; artık `complete` ile tam sayılıyor (Bireysel Antrenman da
hızlandı). Değiştirme: yoğunluk kartının tablosu sanal (13 satır kuruyor), Bireysel Antrenman'ınki 25'ini de kuruyor ve aynı
olayı (`EventFmxPlayerTrainingIntensityLevelChanged`) gönderen yoğunluk listesi orada da var → `TrainingControl` (§8.186)
kullanılıyor; eklenti `/rowcontrols` ve `/buttons?json=1` artık her kontrolün `Page`'ini veriyor: olayın kaynak düğümünden
yukarı, adı sayfa Guid'iyle başlayan ilk düğüm (`<guid>B0`). Sonuç oyuncunun verisinden (`PropertyValue` = istenen).
Oyunun davranışları: Otomatik seçilince oyun kurallara göre hemen bir seviye seçiyor (veride Otomatik hiç görünmedi, 25
yoklamada) → otomatikte "değer değişti" kabul. Tatildeki oyuncu (`HolidaysRemaining` > 0) başka seviye almıyor; dinlenme üstüne
biniyor, dinlenme kalkınca tatil geri geliyor. Eski kodun Türkçede kırık iki yeri: dinlenme alt menüsü "3 Days" diye seçiliyordu
(Türkçede "3 Gün", "2 Wks" geliyor), `--auto` düğmesi "Ask … training intensities" yazısıyla aranıyordu (Türkçede "Snowden'den …
iste"). `--auto`: düğme = `SpeakToAction` + `Page == TrainingIntensity`; basınca her seferinde "Holiday warning" onay
penceresi (`Confirm2037149489`, 3/3, ~0,1–0,3 sn) — eski kod bunu beklemiyordu. Onaysız birincil olmayan yuva ("Hayır",
hiçbir şey değişmiyor), `--yes` ile birincil; bitti = düzenlenebilir ve önerisi olan herkes önerisinde. Kart ara sıra yeniden
kuruluyor ve önceki düğmeye basış kayboluyor → düğme her basıştan hemen önce bulunuyor; pencere gelmeden düğmenin işaretçisi
değişirse bir kez daha basılıyor (16 → 6–9 sn). Ölçüm: değiştirme 22/22 (dört seviye, dinlenme 1/2/3/6/7/14, tatil 1/2, otomatik,
oyunun kendi kelimesi "Duble") yalnızca hedef oyuncuda; üç başlangıç durumu (başka sayfa / Bireysel Antrenman / yoğunluk
kartı); `--auto` 7/7. İngilizcede denenmedi. Not: denemelerde Tom Hamer (1 hafta) ve Ryley Towler (2 hafta) tatile gitti.

### 8.188 Mentorluk: veriden okuma, düğmeler kimlikle, oyunun aynı gün tuzağı (15 Ekim, Türkçe)
Okuma sayfasız: `Human.Team.MentoringGroups` (liste; sayfanın grup listesi `Items = team.mentoringgroups`, aynı sıra) →
`Name`, her satırın `MentoringGroupPlayerOne/Two/Three` → `Name`, `UniqueId`, `Personality`. Eski kod her yoklamada
`/tree?depth=40` tarıyordu; 8,4 → 0,7 sn. `Personality`, `PlayerTraining.Workload` ve `HierarchyInfluenceCategoryString` (ister
`team`) eklentiden İngilizce; oyunun kelimesi okunduğu anda (`GameWords.Of`) konmalı: "Average" iş yükünde "Orta", takım
dinamiklerinde "Vasat" — düzenleyiciden çıkıp sayfa okununca ikincisi öğreniliyordu.
Yardımcı: eklentide `/buttons?json=1&gen=<n>` her `SpeakToAction` düğmesinin eylemini (`<kaynak>`ın üstündeki en yakın
`action` düğümü) `fmcli.speak.g<n>.<k>`'ya koyar (`Action`): sor = `ActionID` 1882280514 (`TeamSetRecommendedMentoringGroups`,
`Event` 44 `EventFmxDoAction`), cevabı kapatan "Acknowledge and Move On" = `ActionID` 0, `Event` 31 (`EventFmxClearChanges`;
Türkçe oyunda da etiketi İngilizce). Cevap kapatılmazsa sayfada kalır ve sor düğmesi gelmez. Gruplar varken sor →
`Confirm1882280514` penceresi ("bütün gruplar silinecek"). Aynı istek `EventToPlugin` olaylarının gönderdiğini de koyar
(`Sends` + `SendsProps`, `m_dataToSendBinding`'in ilk adı kaynaktan yukarı aranır): silme düğmesi
`selectedmentoringgroup.UniqueId` gönderir, seçim değişince hemen izliyor.
Düzenleyici (`57590a1a52e2c334485fd4134fa6e0cc`): "Create New Mentoring Group" grubu hemen listenin sonuna ekler; seçimler o
grubun `SelectedPlayers` / `Count` / `IsValid`'inde. `TeamTraining.NewlyCreatedMentoringGroup` güvenilmez (10 sn eski grubu
gösterdi). Tablo: eklentide `/checkrows?gen=` kutulu satırlı her tablonun (`TableRowNavigatable` + `Toggle` →
`TableParent` `StreamedListView`) bütün öğelerini koyar; seçim (`ListElement.SelectionInternal`) GÖRÜNÜM sırasıyla sayar
(kaynak sırası değil: 2 → Rob Street yerine Ivan Varfolomeev işaretlendi), öğeler bu yüzden `GetItemFromView(i)`.
`/rowselect?table=&index=&check=1|0` satır çizilmemişken de işaretler (24. satır, kaydırmasız). Tablo önce kaynak sırasıyla
çiziliyor, bir an sonra sıralanıyor → sıra her işaretten hemen önce okunuyor, sonuç `SelectedPlayers`'tan. Ad kutusu
`BindableTextEditBox`, gönderdiği olay yok, `m_bindingKey` → `newgroupname` (eklenti `/textboxes` `Binds`); ad onayla gider.
⚠ Sayfanın grup listesi oyunun asıl kaydının ara kopyası; değişiklik gün işlenince yerleşir. Silmeden sonra aynı gün
"Create New" eski listeyi geri yükler (silinen grup geri görünür) ve yeni gruba var olan bir grubun `UniqueId`'sini verir;
üstüne bir silme daha başka grupları siler (bir gecede dört grup karıştı). Ayrıca silmeden sonra `UniqueId` ve oyunun verdiği
adlar ("2. Mentorluk Grubu") yeniden numaralanıyor → bir grubun kimliği üyeleri. Gün ilerletilerek doğrulandı: `--auto`, tek
ve aynı gün iki silme, aynı gün iki kurma, kurup aynı gün silme hep kalıcı ve doğru; CLI `new`'ü aynı oyun gününde bir
`remove`'dan sonra reddediyor (kariyer önbelleğinde `mentoring-removed` = günün tarihi). İngilizcede denenmedi.

### 8.189 Mutluluk: sıra ve renk seviyenin yazısına değil oyunun puanına bakıyor (15 Ekim, TR + EN)
`happiness` listeyi `Array.IndexOf(HappinessLevels, p.Level)` ile sıralıyordu; oyunun Türkçede kendi yazısıyla
gösterdiği iki seviye ("Biraz Mutlu"/"Biraz Mutsuz") İngilizce dizide olmadığından indeks -1 çıkıyor, bu iki seviye
listenin başına düşüyor ve `LevelColour` onlara kırmızı veriyordu. Sıra/renk artık kulüp verisindeki puanla:
`Human.Team.Players` › `HierarchyOverallHappinessLevel` (ölçülen: 80 Delighted, 60 Very Happy, 40 Happy, 20 Slightly
Happy, -14/-20 Content, -40 Slightly Unhappy, -65 Very Unhappy; `Unhappy` arada). Bant sınırları oyunun bıraktığı
boşluklarda (`score < -30` kırmızı, `< 30` sarı, üstü yeşil); puan okunamazsa eski yazı indeksinden `LevelScores`
ile düşülüyor. Moral/konum/seviye yazıları `GameWords.Of` ile oyunun diline çevriliyor ("Poor"/"Extremely Good"
artık Türkçede "Kötü"/"Aşıri İyi"). Ölçüm: TR Mesajlar'dan / zaten açık / Mesajlar açıkken 27/27 aynı, puan sırası
artan (-65…80); EN'de aynı sonuç, `Very Unhappy`/`Slightly Unhappy` kırmızı, `Content`/`Slightly Happy` sarı,
`Happy`+ yeşil. Puanın veri yolu her dilde aynı; yazı yalnız gösterimde.

### 8.190 Diffin baktığı hâl önbellekleri dil ve kayda göre ayrıldı (15 Ekim, TR + EN)
`CacheFile` bütün önbellekleri (rol adları, görev alanları, talimatlar, sezon/istatistik, gelen kutusu numaraları,
mentorluk) `~/.config/fm-cli/cache/<dil>/<yönetici - kulüp>/` altına koyuyor; `diff`in baktığı hâl ise tek istisnaydı:
`Diff.cs` onu üst klasörde yalnız takım adıyla (`diff-<takım>.json`) tutuyordu. İçinde oyunun kendi kelimeleri
geçiyor (`FullContract.EndDateDescription`, `CurrentInjury.InjuryDescription`), o yüzden paylaşılan dosya dil
değişince yanlış "değişti" üretiyor: TR'de bakış kaydedilip İngilizceye geçilince `diff` her sözleşme tarihini
değişmiş sayıyordu (ölçüldü: `Jamie Pardington` "N/A" → "Yok"). Yol `CacheFile($"diff-{takım}.json")` oldu; dil
başına ayrı dosya (`cache/turkish/...`, `cache/english/...`). Ölçüm (üç durum + iki dil): ilk bakış → `First:true`,
ikinci bakış → `Contracts:[]`, başka dilin dosyası okunmuyor (TR ilk bakış EN'in yanında yeniden `First:true`).
Üstte kalan `diff-*.json` ve `snapshot.json` önceki sürümlerin artığı; okunmuyor, silinmedi.

### 8.191 "Acknowledge and Move On" etiketle değil gönderdiği olayla basılıyor (16 Ekim, TR + EN)
Yardımcının son sözünü geçen düğme 8 yerde `press text="Acknowledge and Move On"` ile basılıyordu; yazı oyunun
kendi cümlesi olduğu için başka dilde çevrilebilir. Eklenti `/buttons?json=1&gen=` yardımcının kartındaki her
`SpeakToAction` düğmesine `Action` (oyunun aksiyon düğümü) veriyor; o düğümün `Event`'i 31
(`EventFmxClearChanges`, `ActionID` 0) ise düğme yalnızca söyleneni kapatan düğmedir — etiketten bağımsız (bu
kimlik mentorlukta kullanılıyordu, §8.188). Ölçüldü: TR ve EN'de "Acknowledge and Move On" → `{"Event":"31",
"ActionID":"0"}`; aynı karttaki menü düğmeleri 27/0/0/44, hiçbiri 31 değil. Yeni `Acknowledge()` yardımcısı
(Program) bu düğmeyi bulup işaretçisiyle basıyor, yoksa hiç basmıyor (eski kod her turda boşuna deniyordu).
Ölçüm (training raporu isteği, `FM_TRACE=1`): eski kod 3 `press ?text=Acknowledge and Move On`; yeni kod tek
`press ?ptr=0x…`. Üç durumda (kart zaten açık / başka sayfa açıkken / sayfa ilk açılırken) ve iki dilde rc=0;
negatif: yardımcı kartı yokken (`propose` teklif ekranı) yeni kod hiç basmıyor (0 `press ?ptr=`). `tools/sweep.sh`
50/50. Not: `propose` "Önerme ekranı açılmadı" hatası eski koddada aynı (bu değişiklikten değil). BoardRequests'te
aynı karşılaştırma da kimliğe bağlandı; `Release`'teki "Is there anything else" cümle süzgeci ayrı iş.

### 8.192 Mesaj eki okuması: sabit 1,5 sn ölçüldü, yerine geçecek işaret bulunamadı (16 Ekim, TR)
`WriteAttachment` (Program ~993) mesajın altındaki "Attachments" başlığından sonrasını okuyup ek gelmezse
"ek yok" diyor; başlık her mesajda ekranda olduğu için bekleme yalnızca zamanla sınırlı. Ölçüm (butona basıp
`/texts?anchor=Attachments&up=8&paths=1` yoklaması, 30 ms aralıkla): 0,13 sn'de hâlâ **önceki** mesajın eki
(Mansfield raporu, 110 metin) duruyor; 0,20–0,40 sn'de başlık var, içerik yok (boş ara durum); 0,47–0,55 sn'de
kısmi (9 metin, "Data Hub · No Data"); ≥0,60 sn'de tam (22 metin). Yani içerik ~0,6 sn'de oturuyor, sabit
1,5 sn'nin ~2,5 kat payı var — bu makinede yanlış okumuyor, bu yüzden değişiklik kanıtlanamadı.
İşaret arandı: `/elements` ağacında `#AttachmentsTitleContainer`, `#Attachments`, `FMUnbindDynamicBindingRooot`,
`SIVisible`, `BindingRoot #MessagesToolBindingRoot`, `BindingExpect #EventTrigger` **hem içerikli hem boş
durumda birebir aynı**; tek fark her kartın (`VariableSizedGridElement` › `BindingRemapper #Portal Card
Template Element`) içeriğinin bağlanması (`TemplateContainer` › `BindingExpect #TileBindingExpect`). Eki
olmayan mesaj türü (haftalık scout güncellemesi/öneri mesajları) bu kayıtta yok: `fm reply` ile 14 mesaj
denendi, hepsinde "Ek:" ya da "Haber:" satırı var; gelen kutusu verisi (`/inbox`) ek ile ilgili bir alan
taşımıyor (yalnız `CanRespond`, `NewsTitleString`, `UniqueId`…). Negatif durum ölçülmeden "kartlar bağlandı
mı" işareti eki olmayan mesajı "ek var" sayar; madde bu yüzden `[?]` park edildi.

### 8.193 `reply` mesaj geçişinde boş ara durum yok: oyun cevap kümesini yerinde değiştiriyor (16 Ekim, TR + EN)
`Reply` (Program ~800) cevapları "sayfadaki başlıklar = düğmeler ve öncekinden farklı" olunca kabul ediyor; `shown ==
previous` iken boş sonuç yalnızca `CanRespond != "True"` ise geçiyor. Risk: cevaplı bir mesajdan cevaplı bir mesaja
geçerken eski düğmeler silinip yenileri gelmeden boş bir an varsa `shown=[] != previous` tutar ve cevaplar "yok" sayılır.
Ölçüm: üç cevaplı mesaj (Monthly Performance / Lincoln fall to late defeat / Street ends goal drought, TR + EN) arasında
tüm sıralar; `/buttons?json=1` (SpeakToAction) ~60 ms aralıkla yoklandı, ayrıca `/texts?...paths=1` ile sayfa başlıkları.
Boş okuma: **0/80 örnek**. Geçiş, başlığın önce değişmesi (`title = B`, düğmeler hâlâ A) ve düğmelerin boşalmadan
eskisinden yenisine geçmesiyle oluyor (2→2, 2→1, 1→1); bir kez kısmi görüldü (`ans=['View Goal']`, sayfada ikisi) ve
o an `shown == page titles` tutmadığı için kodun kendisi reddediyor. Üretim seviyesinde `fm reply A; fm reply B`
döngüsü (TR 10 + EN 6 geçiş, "başka sayfa açık", "mesaj zaten açık", "sayfa ilk açılıyor" durumları): her seferinde
doğru cevap listesi (2 ve 1), hiç "(cevap seçeneği yok)". Negatif/boş yol: cevaplı mesajdan cevapsız `Fitness tests`'e
geçişte "(cevap seçeneği yok)" doğru geliyor. Kanıtlanmış bir hata olmadığı için kod değişmedi (kural 4, churn yok).

### 8.194 Sözleşmede kutuya girilen ücret oyunun maaş döneminde sayılıyor; CLI'nin "haftalık" sözü yanlıştı (18 Ekim, TR + EN)
`--wage` / `--term "Wage=…"` tutarı `OfferScreen.Type` ile `/setnumber`'a olduğu gibi gidiyor; kutunun dönemi
oyunun kendi "Wages" ayarı (Preferences › Your World), kartın gösterdiği dönem. Ölçüm (aynı komut, aynı oyuncu,
yalnız ayar değişiyor): TR + Aylık'ta `fm contract --term "Wage=5000"` kartı `5.000 £ aylık` (≈21,7B £/hafta),
TR + Haftalık'ta `5.000 £ hft`, EN + Aylık'ta `5.000 £ p/m`. Yani aynı sayı ayara göre haftada ~4,3 kat farklı
ücrete denk geliyor. Kural 3 gereği çevrim (haftalık → kartın dönemi) yapılmadı: dönemi okuyacak ucuz/sağlam bir
yol yok, 52/12 tahmini yeni bir bağımlılık olurdu. Bunun yerine CLI'nin kendi sözü düzeltildi — help (TR + EN),
MCP `fm_contract` / `fm_staff_hire` / `fm_staff`: tutar artık "oyunun maaş döneminde" diye anılıyor, "haftalık"
değil; davranış değişmedi. Yan bulgular: (a) `fm finances` toplamı haftalık (96.565 £) ama oyuncu statüsü satırları
oyunun döneminde (aylık; satırların toplamı 282.609 £) — aynı tabloda iki dönem; (b) görüşme kartındaki "Wage"
bilgisi (oyuncunun şimdiki ücreti) terimler aylıkken haftalık çıkabiliyor ("3B £ hft"), oyuncu raporu panelinden
okunuyor; (c) oyunun `DisplayValue` yazıları bağlandığı an donuyor: `fm settings maaş` değişince eski okumalar
eski dönemi göstermeye devam ediyor (squad "hft" kalırken `contracts` "aylık"), bir gün ilerletmek
(2 Ağu 09:15 → 10:00) hepsini tazeledi.

### 8.195 Maç geçmişi tarihi oyunun kendi sırasına bırakıldı: gg/aa/yyyy varsayımı iki ayarda satırları siliyordu (18 Ekim, TR + EN)
`Matches.cs` oyuncunun maç puanları tablosunu sayfanın yazılarından kuruyor ve satırı "oyunun numarası + tarih"
diye tanıyordu; tarih `^\d{2}/\d{2}/\d{4}$` ile aranıyor, gösterimdeki kısa biçim `TryParseExact("dd/MM/yyyy")`
ile çözülüyordu. Oyunun kendi ayarı (Preferences › Your World › Format, `fm settings tarih`) dört sıra sunuyor;
aynı maç (20 numaralı, Bristol Rovers, 1 Ağustos 2026) sayfada şöyle yazılıyor: `dd/mm/yyyy` → `01/08/2026`,
`mm/dd/yyyy` → `08/01/2026`, `yyyy/mm/dd` → `2026/08/01`, `yyyy/dd/mm` → `2026/01/08`. Yani tarihin *sırası*
oyunun ayarından geliyor, CLI'nin varsayımından değil. Ölçüm (`fm player "Ben House" --matches --json`, aynı
oyuncu, yalnız ayar değişiyor): **önce** 19 / 19 / **0** / **0** satır; `mm/dd/yyyy`'de satırlar geliyordu ama
zengin tablo tarihi `8 Jan` (TR `8 Oca`) yazıyordu — oysa maç 1 Ağustos. **Sonra** dördünde de 19 satır, tarih
oyunun kendi yazısı: `Rows` tarihi artık sıraya bakmadan tanıyor (dört haneli yıl bir uçta, ayırıcı `/ . -`),
tablo kısa biçimi bıraktı ve oyunun yazdığı tarihi gösteriyor (kural 3: dokunulan fonksiyondaki tarih sırası
varsayımının tamamı gitti; `GameDay` dokunulmadı, o verinin ISO alanını okuyor). Üç durum (kart ilk kez açılırken
Mesajlar açık / kart zaten açık / Takım Dinamikleri açıkken) TR + EN'de aynı 19 satırı verdi; `mm/dd/yyyy` ve
`yyyy/mm/dd` ile de denendi. Not: `fm settings` seçenekleri insan çıktısında oyunun kelimeleriyle (`gg/aa/yyyy`)
ama komuta **oyunun ham adı** verilmeli (`dd/mm/yyyy`), yoksa "bir seçenek değil" diyor. Zengin tabloda tarih
sütunu 80 sütunda sarıyor (10 karakter, sütun 9) — veri doğru, kozmetik.
Yan ölçüm (aynı maddenin boy/kilo yarısı): oyuncunun `Height` alanı ayardan bağımsız, her zaman ham santimetre
(`fm settings boy Fit` iken de `fm player --json` → `Height: 178`), yani CLI'nin " cm" yazısı veriyle tutuyor;
oyunun birim ayarına uymak için Preferences sayfasını açmak gerekiyor (~7 sn, kural 3'e aykırı) — Faruk'a soru.

### 8.196 İş ilanları satırları ve paneli konum/ağız yazısıyla değil veri yoluyla bulunuyor (18 Ekim, TR + EN)
`fm jobs` Türkçede hiç çalışmıyordu ("İş ilanları okunamadı."): satırlar 8'li adımlarla sayılıyor ve lig sırası
`^\d+(st|nd|rd|th)$` ile doğrulanıyordu; oyun Türkçede sırayı "7." / "20." yazıyor (İngilizce kalıntı "3rd" de var),
ilk satırda kontrol düşüp liste boş kalıyordu. Ayrıca seçilen işin paneli `<kulüp> Managerial Position` cümlesinin
son sözcüğüyle bulunuyordu; Türkçede başlık "Celesti Menajerlik Görevi" (birleşik cümle olduğu için çeviri tablosuna
girmiyor). Şimdi: `/texts?…&paths=1` ile her yazı kendi veri yoluyla okunuyor, satırın kulüp hücresi `team.name`
(yönetici hücresi "" ya da `Manager.Name`), panel başlığı da yolu olmayan yazılar arasında kulüp adını içeren son
yazı; "Declare Interest In Job" panelin sonu. `ReputationScope.DisplayString`, `Manager.Name` gibi yollar zaten
vardı. Ölçüm (TR): önce `fm jobs --json` → "İş ilanları okunamadı."; sonra Celesti…Norway 15 ilan doğru alanlarla
(League `comp.Name`, Nation, Rating, Position oyunun kendi yazısı, Security `binding.Status`, CanApply düğmeden);
üç durum (başka sayfa açık / zaten açık / başka sekmeden) ve EN'de aynı 12–15 satır (EN adları: `N.Ireland`,
`Ivory Coast`), detay paneli (`fm jobs "York"`) iki dilde aynı: kural cümlesi, Reputation/Finances/Wage Bill/Training
Facilities. ⚠ Yeni bulgu: liste yalnız görünen pencereyi veriyor — aynı durumda 12 ya da 15 satır (sayfa
kaydırıldığında farklı kulüpler), yani listede olan bir ilan (`fm jobs "Celesti"` pencerede değilken) "ilanlarda
yok" diyebiliyor; tam liste için listeyi kaydırarak gezmek ya da oyunun verisinden okumak gerekiyor (checklist'e
madde olarak eklendi). Panel, satırı görünmeyen bir iş seçilirse boş kalabiliyor (Danimarka) — hata vermiyor.

### 8.197 İş listesi kendi kaydırması boyunca geziliyor: 15 değil 321 ilan (18 Ekim, TR + EN)
Tablo yalnız görünen pencereyi (12–20 satır) döşüyor; `fm jobs` bu yüzden listenin başını okuyordu. Artık liste
kendi kaydırması boyunca geziliyor: `scroll?anchor=Current Manager&to=<0..1>` ile pencere pencere iniliyor ve
pencereler sırayla birleştiriliyor. Kaydırmadan sonra ilk okuma **boş** geliyor (satırlar bir-iki kare sonra
döşeniyor), o yüzden bir pencere ancak bir öncekinden farklı ve iki okumada aynı olunca kabul ediliyor. Adım sabit
değil: bir pencere, önceki pencerenin gösterdiği satır sayısına yakın sayıda *yeni* satır getirdiyse (yani pencereyi
neredeyse hiç kesmediyse — küçük ekranda satır sayısı az) adım yarıya iniyor, iyi kesiştiyse geri büyüyor; böylece
adımı ekranın kaç satır sığdırdığı belirliyor (ölçüm: bu makinede 20 pencere, ~0,05 pay).
Satır seçmek için de satır önce görünüme kaydırılıyor (`rowselect text=…&and=…&dry=1` ile soruluyor, sonra seçiliyor):
pencerede olmayan satır hiç seçilemiyordu ve panel başka kulübün cevabını veriyordu.
Ölçüm (TR): önce `fm jobs --json` → 15 ilan, 32 sn, hepsi penceredeki kulüpler; sonra **321 ilan** (Celesti…
New Zealand, oyunun kendi sırası), 22–29 sn; `fm jobs "Barcelona"` (61. sıra, pencerede değil) doğru panel.
EN'de aynı 321 ilan ve aynı sıra; üç durumda (başka sayfa açık / zaten açık / başka sekmeden) aynı liste.
Negatif: `fm jobs "ZZZ yok"` → "ilanlarda yok", `fm jobs 400` → "ilanlarda yok"; ayrıca başka sayfa açıkken
(İletiler) liste yine 321 (kaydırma yanlış listeye gitmiyor). Panel gelmediğinde (Danimarka, ABD: ulus işlerinde
panelde "Training Facilities" satırı yok) artık uyarı stderr'e yazılıyor, başka kulübün ayrıntısı basılmıyor.
Not: panel yazıları panel bağlı kaldıkça eskisini gösterebiliyor (§8.194) — York'un ayrıntısında bir kez Celesti'nin
"Wage Bill/Training Facilities" satırları okundu; düğme durumu (CanApply) seçili satırı izliyor, o doğruydu.

### 8.198 İki tuzak: uzun oturumda kayıt kendiliğinden boşalabiliyor; EN'de ulus takımı adları çevriliyor (18 Ekim)
- Sweep'in 39. komutundan sonra oyun ana menüye döndü ve kayıt boşaldı: `fm status` "kayıt yüklü değil", sweep'in
  kalan 10 komutu `rc=69 "Kayıt yüklü değil"` verdi (`numbers` tam o anda 30 sn'de yanıt vermedi). Kod hatası değil:
  aynı sweep kayıt yeniden yüklenince **50/50 temiz** (`fm start` ile "Son kayıt yükleniyor… / Hazır."). Uzun bir
  turda (jobs tam listeyi geziyor, ~27 sn × birkaç koşu) bu olabiliyor; sweep'te üst üste `rc=69` görülürse önce
  `fm status`, sonra `fm start`.
- `fm jobs --json` EN'de de 321 ilan ve **aynı sıra**, aynı BAŞVURU değerleri; yalnız 15 ulus takımının adı oyunun
  dilinde yazılıyor (`United States`/`U.S.A.`, `Fildişi Sahilleri`/`Ivory Coast`, `S. Arabistan`/`Saudi Arabia`).
  Yani iki dilin çıktısı kulüp adına göre karşılaştırılamaz; sıra ve sayı karşılaştırılmalı.

### 8.199 `fm actions`: ekrandaki konuşan düğmeler konuşma sanılıyordu (18 Ekim)
- `AfterAction` "oyun bir konuşma açtı mı" diye `TalkOptions()`a bakıyordu; o da ekranda `SpeakToAction` olayı taşıyan
  her `StandardButton`ı sayıyor. Oyuncu menüsü herhangi bir ekranın üstünde açılabildiği için, gazete maddesinin
  isteğe bağlı cevapları (ör. "Adjust Scouting Budget"/"Adjust Transfer and Wage Budget", bütçe kartı ekrandayken)
  da konuşma sayılıyordu.
- Ölçüm (önce, TR): o ekranda `fm actions "Ben House" "Contract>Enter Contract Negotiations"` oyunun sorusu yerine
  bütçe seçeneklerini "konuşma" olarak bastı; oysa oyunun onay penceresi ("Enter contract negotiations?") seçimden
  **0,24 sn** sonra geliyordu (`/dialog`) ve cevapsız kalıyordu. Aynı ekranda `/buttons?json=1` seçimden önce ve
  sonra birebir aynı iki düğmeyi veriyor (aynı işaretçi, aynı yazı) — yani seçim onları değiştirmiyor.
- Düzeltme: konuşma, seçimden önceki düğme kümesinden **farklı** bir kümeyle anlaşılıyor (`TalkButtons()` = işaretçi|yazı).
  Ölçüm (sonra, aynı ekran): TR + EN'de oyunun sorusu basılıyor (`Cevaplamak için: dialog <No | Yes>`), üç durumda
  (başka sayfa açık / sayfa ilk kez açılıyor / zaten açık) aynı; gerçek konuşma (`Interaction>Praise>…`) hâlâ konuşma
  olarak açılıyor (seçimden 0,29 sn sonra 6 yeni düğme, başka sayfa Guid'i); sonuçsuz bir iş ("Training>Rest Player>1 day")
  "yapıldı" diyor ve veriden doğrulandı (`fm intensity` → Ben House `RestDays: 1`). `tools/sweep.sh`: 50 komut, 0 hata.

### 8.200 Bütçe ayarının slider adımları: işaret oyunun kendi yazısı, 3 sn sınırına ≥8 kat pay (18 Ekim, TR + EN)
`fm budget ayarla` slider'ı her adıma koyar, sonra kartın rakamlarını okur; okuma ancak "rakam değişti **ve**
yürürlükteki adımı ayıran yıldız adıma uydu" olunca kabul edilir (3 sn, 0,2 sn aralık). Bu, oyunun kendi işareti:
adım yerleşince `budget.BudgetAdjustmentEditedTransferValue` / `...EditedOtherValue` yazıları değişiyor ve kart
yürürlükteki adımı yıldızla ayırıyor. Ölçüm (`FM_TRACE=1 fm budget ayarla`, 7 koşu × 16 adım; sayfa ilk kez
açılıyor / sayfa zaten açık / kart zaten açık; TR + EN): `setcontrol` bittikten sonra kabul eden okuma 20–371 ms;
112 adımın 11'inde ikinci okuma gerekti (en çok kart zaten açıkken: 4/16). Sınır en kötü hâlin ≥8 katı → kod
değişmedi. TR ile EN'de rakamlar birebir aynı (yalnız "Transfers/Wages" etiketleri çevrili).
Ölçüm tuzağı: ekranda açık kalmış bir "Budget Adjustment" kartı sayfanın düğmelerini örtüyor ve `ayarla`
"no button shows 'Adjust Transfer and Wage Budget'" ile düşüyor (kendi bıraktığım karttı; `Cancel` ile kapanınca
aynı koşu hatasız). Kart önceden açıkken komutun yine çalışması ayrı bir iyileştirme olarak duruyor.

### 8.201 `fm settings` ayarı başlık yazısıyla arıyor; Almanca oyunda dil ayarı bulunamıyor (18 Ekim, DE)
Ayar tanımları `Heading: "Game Language"` gibi İngilizce başlıklarla eşleşiyor. Almanca oyunda `/form` bu başlığı
**"Language"** diye verdi: oyunun Almanca yazısı ("Sprache") hem "Language" hem "Game Language" İngilizcesine
karşılık geliyor, eklenti tablosu ilkini döndürüyor (FINDINGS §8.167'deki çakışma). Ölçüm: Almanca oyunda
`fm settings` listesinde dil satırı yok; `fm settings language "Türkçe"` → "'language' ayarı Your World sekmesinde
bulunamadı." Öğe yerindeydi (Your World sekmesinin ilk açılır listesi, `Binds app.nativesettings`, 20 dil seçeneği);
dil `/select?ptr=…&index=13` ile değiştirilip Türkçeye dönüldü. Kalıcı yol başlığı oyunun kendi numarasıyla
(TranslationID; eklentiye `Id` alanı eklemek gerekir, oyunu yeniden başlatmadan etkisi olmaz) ya da öğeyi kendi
adıyla (`GameLanguagePreferencesBlock` — E bölümü) bulmak; kabul edilen başlık listesi yeni bir kırılgan
bağımlılık olurdu.

### 8.202 `takers`: duran top türü seçildikten sonraki 500 ms gereksizdi (18 Ekim, TR + EN)
`Takers.cs` `ShowSetPieceKind`, Set Piece Type menüsünden türü seçtikten sonra sabit 500 ms bekliyor, sonra
`Still(...)` ile oyunun kendi işaretini arıyordu: menü bu türü gösteriyor **ve** atıcı düğmesi iki okumada aynı
duruyor. Bekleme, bu işaretin zaten kapsadığı bir şeydi; kaldırıldı (kural 2: sabit bekleme yerine oyunun işareti).
Ölçüm (`FM_TRACE=1 fm takers --json`, beş tür): `select ?value=<tür>` ile doğru atıcı düğmesine basma arası önce
~1,50 sn, sonra ~1,50 sn (seçim→ilk yoklama artık beklemesiz). Üç durum (başka sayfa açık / sayfa zaten açık /
başka sayfadan) TR ve EN'de aynı çıktı (`md5` `8eb5533…` TR, `f42fb27…` EN); süre 3+3 koşuda önce ~19,2 sn, sonra
~19,3 sn — yani nötr: kaldırılan şey yalnızca makine hızına dair bir varsayım, kazanç yok çünkü işaret zaten
vardı. `Still` bir tür daha nadiren üçüncü okuma istiyor (iki okuma aynı olana kadar), o yüzden toplam süre
değişmiyor; ama `ShowSetPieceKind` içinde sabit bekleme kalmadı.

### 8.203 `fm settings` ayarı artık başlık yazısıyla değil, öğenin bulunduğu blokla buluyor (18 Ekim, DE + TR + EN)
Önce: her ayar `/form`daki başlık yazısıyla (`Heading: "Game Language"`) bulunuyordu; Almanca oyunda o başlık
**"Language"** geldiği için dil satırı hiç bulunamıyordu. Ölçüm (önce, DE): `fm settings --json` → **14** satır,
`dil` yok; `fm settings dil Deutsch` → ayarı yaptı ama okuma dönmedi ("was set to Deutsch, but when the page was
opened again could not be read", rc=1), `/state` → `Language: german`. Aynı sayfayı Almanca veren `/form`da öğe
yerindeydi: `Within` zincirinde `GameLanguagePreferencesBlock` (eklenti `DropdownJson`'un verdiği üst öğe adları,
şablonun kendi adları, dilden bağımsız). Yeni: `Locate` önce `Within` içinde ayarın `Block` adını arıyor, bulamazsa
eskisi gibi başlığa düşüyor; `Block` on bir açılır listeye yazıldı (kayıt sıklığı/türü, dil, para birimi, maaş,
tarih, sıcaklık, boy, kısa/uzun mesafe, oran biçimi). İşlem sekmesinin üç listesi aynı bloğu paylaşıyor ve
slider/onay kutusu/radio için oyun blok adı vermiyor → onlar başlıkla kalıyor.
- Ölçüm (sonra, DE): `fm settings --json` → **15** satır, `language` satırı `German`, 20 dil seçeneği;
  `fm settings language "Türkçe"` → `language: German → Turkish`, rc=0, `/state` → `Language: turkish` (yani
  Almanca oyunda hem okuma hem yazma yolu çalışıyor — eskiden okuma dönmüyordu).
- TR (üç durum: başka sayfa açık `fm open "Messages"` / sayfalar zaten açık / başka sayfadan `fm open "Squad
  Dynamics"`): üçünde de 15 satır ve `md5 e5eb7a89c1678a1181292bc240263388` — **değişiklik öncesi çıktıyla birebir
  aynı**. Yazma yolu (blokla bulunan satır): `fm settings maaş Haftalık` → `Aylık → Haftalık`, sonra `Aylık` →
  `Haftalık → Aylık`, `--json` → `Monthly` (eski değerine döndü). Negatif: `fm settings dil Klingon` ve
  `fm settings "para birimi" Klingon` → seçenek listesiyle red, rc=1.
- EN (üç durum): 19 satır, üçü de aynı; **eski derleme ile yeni derleme birebir aynı çıktı** (`git stash` + yeni
  derleme karşılaştırması). Dönüş: `fm settings language "Türkçe"` → `English → Turkish`.
- Yeni bulgu (madde açıldı): aynı komut "Match Day" sekmesini de yazısıyla açıyor; oyun Türkçe ve Almancada
  sekmeyi **"Matchday"** yazıyor (`click ?text=Match Day&name=DetailedPreferencesTabButton` → "no button shows
  'Match Day'", `click ?text=Matchday` → çalışıyor), İngilizcede "Match Day". Sonuç: `fm settings --json` TR/DE'de
  15, EN'de 19 satır — maç gösterimi/dinamik mod/vurgu hızı/ara hızı TR ve DE'de hiç okunmuyor.

### 8.204 `fm settings` sekmesi artık düğmenin bulunduğu kapsayıcıyla açılıyor (18 Ekim, TR + EN + DE)
Sekme `click ?text=<sekme>&name=DetailedPreferencesTabButton` ile açılıyordu; sekme yazısı oyunun sözü (TR/DE'de
"Matchday", EN'de "Match Day"), bu yüzden TR/DE'de "Match Day" sekmesi hiç açılmıyor, maç gösterimi / dinamik mod /
vurgu hızı / ara hızı okunmuyordu. Sekme düğmeleri `/buttons`ta yok; `/elements?ptr=<TabButtons>&depth=4` her
sekmenin **kapsayıcı adını** veriyor (`#MatchDayTabButton`, `#YourWorldTabButton`, `#ProcessingTabButton`,
`#AudioVisualTabButton`, `#ShortcutsTabButton`…; şablonun adları, dile bağlı değil; düğme adı hepsinde
`DetailedPreferencesTabButton`). Yeni: `PreferencesTab` üç sekmeyi kapsayıcı adıyla açıyor
(`click ?within=MatchDayTabButton&name=DetailedPreferencesTabButton` → tam bir düğme), eşleşme yoksa yazıya düşüyor.
- Öncesi (eski derleme): TR, sayfa kapalıyken `fm settings --json` → **15** satır (dört maç günü ayarı yok, `md5
  e5eb7a89…`); DE → **15** satır; EN → 19 satır (`md5 cb604c52…`).
- Sonrası: TR üç durumda (başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / başka sayfadan `fm open "Squad
  Dynamics"`) **19** satır, üçü de `md5 fa5b6948ada7f605b9f3ca5aacdd17e9`; dördüncü durum (sayfa Processing
  sekmesinde açıkken) da aynı. EN üç durumda 19 satır, `md5 cb604c52…` — **eski derlemeyle birebir aynı**. DE → 19
  satır, `language = German`, maç günü satırları okunuyor. Değerler doğru (`maç gösterimi = Key`, `dinamik mod =
  Kapalı`, `vurgu hızı = 10x`, `ara hızı = 40x`).
- Yazma yolu (aynı sekme): `fm settings "vurgu hızı" 4x` → `10x → 4x`, `4x → 10x` ile eski değerine döndü,
  `fm settings "vurgu hızı" --json` → `10x`. Negatif: `fm settings "vurgu hızı" 7x` → "bir seçenek değil", rc=1.
- Dönüş: `fm settings language "Türkçe"` → `German → Turkish` (`/state` → `Language: turkish`); final TR koşusu 19
  satır, ilk koşuyla aynı `md5`.
### 8.205 `fm staff`: kaydırmadan sonraki 400 ms sabit duraklama işarete çevrilemedi — oyun "tam olmayan pencereyi" de sakin gösteriyor (18 Ekim, TR + EN)
Amaç: `Staff 74`deki `Task.Delay(400)`u (All Staff tablosu 5 kaydırma noktasında okunmadan önce) oyunun kendi işaretine
çevirmek. Denenen işaret: "pencere kaydırmadan öncekinden farklı **ve** art arda iki okumada aynı" (+ kaydırmanın
oynadığını `scroll` cevabını `to`suz çağrıyla karşılaştırarak anlamak).
- Ölçüm (öncesi): beş noktada satırlar 93, 272, 125, 93, 213 ms'de oturuyor (iki okuma aynı olunca); 400 ms duraklamanın
  payı yalnız ~1,5–4 kat. Sabit duraklama bu makinede hep doğru okudu: TR üç durumda (başka sayfa / sayfa açık / başka
  sekmeden) 48 satır `md5 2d0ad32bf80a03049e0e82e6b2b6e57c`, EN üç durumda 48 satır `md5 5c9e418ec3a04046dcd4872be1357c2e`
  (değerler 18 Ekim oturumunun; para biçimi oturuma göre değişiyor), ve arka arkaya **12/12 koşu 48 satır**.
- Denenen sürüm hızlıydı (TR ~4,5–5,0 sn → ~2,8–3,2 sn) ama **pencere kaybetti**: 18 koşunun 2'sinde 34 ve 42 satır.
  Nedenleri veriden: (1) oyun ara yerleşimi "sakin" gösterebiliyor — aynı **20 satır** art arda iki okumada dönüyor
  (`agree=True differs=True`, ~17–64 ms arayla) ve pencere tam sanılıyor, (2) bir koşuda kaydırma sorgusu hata döndü
  (çapa o an bulunamadı) ve "oynamadı" sanılıp gezinti kesildi; eski kod kaydırma cevabına hiç bakmadığı için bu anları
  duraklamayla atlıyor, (3) 50 ms'lik sık yoklama köprüyü sıkıştırıyor (60 çağrılık bir döngü 120 sn'yi aştı) → cevaplar
  yerleşimin gerisinde kalıyor.
- Karar (kural 4): kanıtlanmış iyi hâl yok → `git restore cli/fm/Staff.cs`, madde `[?]`, soru BAGIMLILIKLAR'da. Not:
  "Mutluluk"taki gibi çapayı **görünen son satırın kendi yazısına** taşıyıp listeyi baştan sona gezmek ayrı bir iş.
### 8.206 `fm training <oyuncu> --trait`: döşemeleri açan iki sabit 500 ms oyunun kendi işaretine bağlandı (18 Ekim, TR + EN)
`cli/fm/TraitTraining.cs`: alışkanlık eğitimi sayfası iki döşemeyle açılıyor ("Training Rating" oyuncu kartından,
"Traits" onun açtığı sayfadan) ve her basıştan sonra sabit 500 ms bekleniyordu. Yeni: basılacak döşeme **basılmadan**
soruluyor (`press ?text=…&name=layout-tile-interaction-button&any=1&dry=1`, 0,25 sn aralıkla, en çok 8 sn) ve ancak
ekranda görününce basılıyor; sabit bekleme yok. Döşeme gelmezse adıyla birlikte hata yazılıyor.
- Kanıt: `fm training "Ben House" --trait` (okuma yolu; hiçbir şey öğretmiyor) üç durumda — sayfa ilk kez açılıyor
  (`fm open "Messages"` sonrası) / sayfa zaten açık (hemen ikinci koşu) / başka sayfa açık (`fm open "Squad
  Dynamics"`) — TR'de 10 satır `md5 8a43caeda16d227f818785976e7fa906`, EN'de 10 satır
  `md5 9e92fe93ca38f07f0d9f2d20a30c8b56`; ikisi de değişiklik **öncesiyle birebir aynı** (aynı alışkanlıklar ve
  öğretilebilecekler listesi).
- Süre (hedef değil, işaretin tuttuğunu gösterir): döşeme→döşeme aralığı önce 0,57 / 0,60 / 0,59 sn, sonra
  0,43 / 0,53 / 0,52 sn (sabit 500 ms + bir istek yerine 0,25 sn'lik yoklama: ilk sorusu çoğu kez boşa, ikincisi
  tutuyor). Toplam süre aramadan gelen gürültüyle nötr: önce TR 7,7–8,8 / EN 7,5–10,7 sn, sonra TR 8,4–9,5 / EN
  8,5–9,7 sn.
- Yan ölçüm: "Traits" döşemesi oyuncu kartında **yok** — kartın döşemeleri Very Unhappy, Fitness, Last 5 Club Games,
  Discipline, Training Rating, Season Stats, Career Stats (kart sayfası `77188c7b…`, eğitim sayfası `41012275…`) —
  yani gecikmesiz basış yanlış döşemeye gidemez; bulunamayan basış yoklamayı devreye sokar.
- Negatif (aynı yoldan): TR `fm training "Ben House" --trait-remove "ZZZ yok"` → rc=1, seçenekler karttan listeleniyor;
  EN `… "ZZZ nope"` → rc=1. Dönüş: `fm settings language "Türkçe"` → `English → Turkish`, `/state` → `Language: turkish`.
- Yeni bulgu (B): TR oyunda `--trait-suggest` çalışmıyor — düğmenin yazısı bizim tabloya İngilizce karşılığıyla
  geçmemiş ("Oyun Karakteristiği İdmanı Önerisi İste"); CLI İngilizce yazıyla bastığı için `error: no control labelled
  'Ask For Trait Training Suggestion'`. `git stash` + eski derlemede **birebir aynı** → bu değişiklikten değil; B'nin
  "İngilizce yazıyla basma/tıklama — 44 yer" maddesinin ölçülmüş bir örneği.

### 8.207 `fm opposition <oyuncu> <talimat>=<değer>`: sabit 500 ms yerine sütunun kendi değeri, ve her talimat satırın adıyla yeniden bulunuyor (18 Ekim, TR + EN)

`cli/fm/OppositionCard.cs`. Bir talimat seçildikten sonraki sabit 500 ms kaldırıldı; onun yerine okuma "o sütun seçilen
değeri gösteriyor" olana kadar (0,25 sn aralık, en çok 8 sn; gelmezse stderr'e uyarı) sürüyor. Ölçüm (`python3` +
`/opposition`, dört sütunun bütün seçenekleri): değer seçimden sonra **301–494 ms**'de görünüyor (10 ölçüm), yani 500 ms'in
payı neredeyse yoktu. `FM_TRACE=1` izi: `select` 0,38 s → ilk okuma 0,45 (eski değer) → oturan okuma 0,77 s.

- Aynı fonksiyondaki **sıra bağımlılığı** da kalktı: kart bir talimat oturunca satırı yeniden çiziyor ve denetimlere
  **yeni işaretçi** veriyor, ama ikinci talimat hâlâ baştan okunan işaretçiyle seçiliyordu. İki kez yakalandı: eski
  derlemede TR "başka sayfa açık" durumunda `Marking=Always Pressing=Never` istendi, kartta `Always` kaldı (yazılan tablo
  da `Her Zaman` dedi, rc=0); EN'de `Pressing=Always Marking=-` istendi, `Marking` `Always` kaldı. Yeni kod her seçimden
  önce satırı **adıyla** yeniden buluyor (satır ara çizimde kaybolabiliyor: `Until` ile bekleniyor) ve işaretçiyi taze
  okumadan alıyor. Aynı iki senaryo yeni kodda kartın kendi okumasıyla doğrulandı (istenen değerler oturdu).
- Yan bulgu (B, yeni madde): seçenek adları İngilizce geliyor (`Pres=Always` çalışır, `Pres=Her Zaman` çalışmaz) ama hata
  mesajı ve yardım satırı seçenekleri oyunun sözleriyle yazıyor — gösterilen ad geri yazılamıyor. TR oyunda çeviri
  tablosu sıkı giriş seçeneklerini `Normal · Minor · Gusty` diye veriyor (EN: `Normal · Easy · Hard`), yani adlar da
  güvenilir değil. BAGIMLILIKLAR B'ye madde olarak yazıldı.
- Ölçüm (üç durum TR + EN: sayfa ilk kez açılıyor / zaten açık / başka sayfa açık; komut `fm opposition "Kyle Knoyle"
  "Marking=Always" "Pressing=Never"`): yazılan tablolar öncesiyle **birebir** (`md5` TR `97059da1…` / `ed1cd387…`, EN
  `9e41298d…` / `8704a667…`) — yalnız "başka sayfa açık" durumunda tablo artık **doğru** (eskiden ikinci talimat
  uygulanmadan `Always` yazıyordu). Her koşudan 1,5 sn sonra kartın kendisi okunup yazılan tabloyla karşılaştırıldı;
  8/8 birebir. İki dilde 6'şar tekrar (ilk talimat gerçekten değişecek şekilde) → 12/12 iki talimat da oturdu.
- Negatif (TR): `"ZZZ yok" "Pressing=Always"` → rc=1 "Rakipte 'ZZZ yok' yok." · `"Pressing=zzz"` → rc=1, seçenekler ·
  `"Pressing"` / `"Yok=Always"` → rc=64, biçim satırı · kısmi ad (`"Knoyle"`) → rc=0, satır tam adla bulunuyor.
- `--advice` / `--clear` yolundaki 500 ms **kaldı** (satır 55): "Apply all advice" bastıktan sonra kart ara yerleşimde iki
  okumada **aynı** görünüyor (192–281 ms aynı satırlar, gerçek sonuç 384 ms), yani "iki okuma aynı" işareti erken kabul
  ederdi (Staff 74'ün tuzağı, §8.205); düğmenin etkin/olay durumu uygulama boyunca değişmiyor. İşaret bulunamadı →
  madde `[?]`. Regresyon: `tools/sweep.sh` → 50 komut, 0 hata. Kayıt test kaydı: talimatlar `--clear` ile başlangıçtaki
  boş hâline döndürüldü, saat ilerletilmedi, maç oynanmadı.

### 8.208 `fm player <oyuncu> --matches`: sabit 500 ms kalktı, dal ilk kez çalıştı — en eski maç artık düşmüyor (18 Ekim, TR + EN)

- Sayfadaki maç listesi yalnız görünen pencereyi döşüyor (19 satır); `if (rows.Count < apps)` koşulu oyun
  sayısını beklediği için bu dal **hiç çalışmıyordu** (`Apps` 12, satır 19) → her oyuncunun en eski maçı (#1,
  Ben House: Plymouth 07/02/2026) sessizce eksik kalıyordu: 19 satır, `2..20`. Sayfa başka bir bakıştan
  kaydırılmış kaldığında eksik satır da değişiyordu (`19..1`), yani okuma **nereden bakıldığına** bağlıydı.
- Yeni giriş: oyunun kendi cevabı. `scroll?anchor=<ilk satır>&to=0` cevabı "0 of 211,2" (" of 0" ise pencere
  bütün listeyi tutuyor — sayı tablosundaki okuma, `Numbers.cs`), pencere kaydırmadan sonra "değişti **ve** iki
  okumada aynı" olunca kabul (kaydırılan satırlar 30 ms'de geliyor; ölçüm `python3` + `/texts`, 30 ms aralık).
- Öncesi: `fm player "Ben House" --matches --json` → **19** satır (`2..20`), üç durumda TR `md5 d2186ce9…`,
  EN `5934f197…`, süre 8,5–8,9 / 1,9–2,5 sn; `git stash` ile eski derlemede aynı. Dört oyuncuda da 19 satır
  (Jeacock, Bradley, Hamer, Draper).
- Sonrası: **20** satır (`1..20`), üç durumda TR `md5 d4b4b0ea…` (5 koşu), EN `md5 d9ddd02b…` (4 koşu); dört
  oyuncuda da 20 satır. Süre soğuk 8,8–9,9 sn, sıcak 3,5–3,8 sn (yeni iş ~1,6 sn: bir `scroll` + iki pencere
  okuma turu + başa dönüş). İnsan çıktısı artık son satır olarak `1 07/02/2026 Plymouth 4 - 1` yazıyor.
- Negatif/tuhaç: maçı olmayan oyuncuda davranış aynı (Cody Tomlinson → "form kutusu yok", rc=1; Gael Mande →
  `Matches: []`, `Apps: 9`) — **eski derlemede de birebir aynı**, bu değişiklikten değil. Gael'de kart açıldıktan
  sonra `/texts` bir an **önceki oyuncunun** tablosunu (Ben House, Apps 12) döndürüyor; FM'in bağlı kalan
  panelleri (§8.194, §8.197) — ayrı madde olarak duruyor.

### 8.209 `fm training <oyuncu> --trait-suggest`: düğme artık İngilizce yazısıyla değil, oyunun eylem kimliğiyle bulunuyor (18 Ekim, TR + EN)

- TR oyunda düğmenin yazısı `Oyun Karakteristiği İdmanı Önerisi İste`; CLI `press text="Ask For Trait Training
  Suggestion"` diyordu ve eklenti sorgu metnini tablodan o dile çeviriyor — ama bu sözün İngilizcesi tabloda
  **hiç yok** (`/translate?words=…` onu boş bırakıyor; beş komşusu — "Start New Trait Training", "Remove Trait",
  "Training Rating", "Traits", "Cancel Current Trait Training" — çevriliyor). Sonuç: TR'de üç durumda da
  `error: no control labelled 'Ask For Trait Training Suggestion' on the loaded screens`, rc=1; EN'de çalışıyordu.
- Düğmenin kimliği eklentinin `Action` düğümü: `ActionID` = **1263235426** =
  `FM.GamePlugin.InteropEventIDs.PlayerAskStaffToSuggestPlayerTraitTraining` (`docs/reference/enums.txt`),
  `Event` 44; aynı oyunda açılan onay penceresinin `Type`'ı da `Confirm1263235426`. Eklenti `/buttons?json=1&gen=`
  ile her `SpeakToAction` düğmesine bu yolu veriyor (Acknowledge/Mentoring'in kullandığı yol).
- Yeni: düğme `Action` → `ActionID` ile bulunuyor (0,25 sn aralıkla, en çok 8 sn) ve işaretçisiyle basılıyor;
  cevapsız (konuşmada cümle) durumdaki cevap da yazı yerine **düğmenin kendi sözüyle** aranıyor. Ölçüm: TR'de
  `Shaw'dan bireysel antrenmanlarla ilgilenmesini iste` hem `StandardButton` yazısı hem `action.ActionTitle`
  metni olarak **birebir aynı** — tablonun çeviremediği bir söz için ekranın gösterdiği şey düğmenin yazısı.
- Öncesi (eski derleme): TR üç durum `error: no control labelled …`, rc=1; EN üç durum öneri penceresi, rc=0.
  Sonrası: TR üç durum `Oyuncu Karakteristiği Hakkında Öneri … Onaylamak için aynı komutu --yes ile çalıştır.`,
  rc=0; EN üç durum `Player Trait Suggestion …`, rc=0 (öneri metni her soruşta değişebiliyor, bu yüzden biçim
  karşılaştırıldı). Negatif: `fm training "ZZZ yok" --trait-suggest` → rc=1, "kadroda da aramada da yok".
- Cümleli cevap yolu bu kayıtta üretilemedi (kadrodaki her oyuncuya öneri geldi, pencere çıktı) → düğme sözünün
  doğru çapa olduğu yukarıdaki ölçümle, öncesinde TR'de o satıra hiç gelinmediği için (basış düşüyordu) gerileme
  **olmadığı** kanıtıyla bırakıldı. Regresyon: `tools/sweep.sh` → 50 komut, 0 hata; kayıt test kaydı (2 Ağustos
  2026 14:00) — hiçbir alışkanlık öğretilmedi (her koşu "Don't start training" ile kapandı), saat ilerletilmedi.

### 8.210 `fm contract <oyuncu> --agent`: menajeri sorma satırı artık oyunun eylem kimliğiyle, sabit 1 sn yerine konuşmanın kendi işaretiyle (18 Ekim, TR + EN)

- TR oyunda `fm contract "Adam Reach" --agent` üç durumda da `menajerle görüşme seçeneği yok (yakın zamanda
  konuşulmuş olabilir).` rc=1 veriyordu; oysa satır ekranda duruyordu: `/buttons?json=1` → `Temsilci İle Yeni
  Sözleşme Hakkında Görüş`, `StandardButton`, `Enabled=True`, `Action` → `ActionID` **1882353528** =
  `InteropEventIDs.PersonAskAgentAboutClientContractRenewal` (`docs/reference/enums.txt`). Eski kod
  `press text="Discuss New Contract With Agent"` diyordu; bu söz oyunun kendi konuşma cümlesi ve bu dilin
  tablosunda İngilizcesi yok → basış tamamen tabloya bağlıydı (§8.209'un aynısı).
- Sabit 1 sn de bir tahmindi: basıştan sonra cevap satırları **744 ms'de boşalıp 1145 ms'de doluyor**
  (`/buttons?json=1`, 50 ms aralık, TR; `/scroll` gibi bir işaret yok) — yani 1 sn tam boşluğa düşebiliyor.
  EN'de bir koşuda menajerin ilk cümlesi boş geldi (`  ` sonra `[Very Interested]`), sonraki koşularda doldu.
- Yeni: satır `SpeakToAction(1882353528)` ile bulunuyor (eklentinin `Action` düğümünün `ActionID`'si; yardımcı
  `Acknowledge.cs`'e taşındı, `training --trait-suggest` de onu kullanıyor), işaretçisiyle basılıyor; sonra
  "cevap satırları değişti **ve** boş değil" bekleniyor (`TalkButtons()`, 0,25 sn aralık, 10 sn üst sınır —
  süre yalnız üst sınır, §8.199'un işareti). Bulunamazsa eski hata satırı aynen yazılıyor.
- Öncesi/sonrası (her koşu **taze** oyuncuyla: sorma hakkı basışla o oturumda tükeniyor, satır kayboluyor):
  TR üç durum (başka sayfa açık / kart zaten açık / başka sekme) önce `… seçeneği yok`, rc=1; sonra
  `Merhabalar hocam. Benimle hangi konuda görüşmek istemiştiniz?` + 4 cevap, rc=0. EN üç durum önce/sonra
  birebir aynı (`What can I do for you?` + 4 satır, rc=0). Negatif: hakkı tükenmiş oyuncuda eskisiyle birebir
  rc=1; `"ZZZ yok"` → "kadroda da aramada da yok", rc=1. Kayıt: 2 Ağustos 2026 14:00, hiçbir şey söylenmedi
  (her konuşma `PanelHistoryClose` ile kapatıldı), saat ilerletilmedi. `tools/sweep.sh` 50/50.
- Kapanış (bu tur, aynı iş): değişiklik derlenip üç durumda iki dilde koşuldu. EN üç durum — başka sayfa
  (`fm open "Squad Dynamics"`) Reeco Hackett, kart zaten açık Jack Moylan, sayfa ilk kez (`fm open "Messages"`)
  Ryley Towler — üçünde rc=0, 12 satır (`What can I do for you?` / `Hey! What did you want to talk about?` + 4 cevap).
  TR üç durum (Oscar Thorn, üç durumda da aynı oyuncu) rc=0, 12 satır (`Benimle mi görüşmek istediniz hocam?` + 4).
  Her koşu `PanelHistoryClose` ile kapatıldı; hiçbir şey söylenmedi, saat 14:00'te kaldı.
- Kalan (yeni madde): TR'de kimi oyuncu/koşuda satır bulunamıyor — Tom Hamer ve Zach Jeacock aynı oturumda önce
  rc=0 sonra `seçenek yok`; Josh Honohan, Ben Waine, Erik Ring EN'de rc=0, TR'de `seçenek yok`. Yani tümden dile
  bağlı değil, koşuya bağlı görünüyor; bu sırada ekranda kalan bir sözleşme görüşmesi kartı görüldü
  (`/buttons?json=1` → `Finalise Promises` / `Cancel Negotiations`, ikisinin de `Action` alanı **boş**), yani
  kartın hangi oyuncunun kartı olduğu/işareti ayrı ölçülmeli (§8.197/§8.208'in bağlı kalan panel tuzakları).

### 8.211 `fm numbers`: alt sayfa ve kaydırma sabit duraklamaları oyunun işaretine bağlandı; boş okuma sayfayı yeniden istiyor (18 Ekim, TR + EN)

- Öncesi: `OpenNumbers` alt sayfa düğmesinden (`SecondaryTabDropdownButton`) önce sabit 1,5 sn bekliyor, `ReadNumbers`
  her kaydırmadan sonra sabit 400 ms. İkisi de makine hızını tahmin ediyordu; bir dil değişimi ekranı yeniden çizerken
  1,5 sn yetmeyip sekme hiç seçilmemişti (kod yorumu, ölçülmemiş), kaydırma sonrası okuma da bayat pencereyi 24 satır
  sanabiliyordu.
- Bulunan gerçek hata (işareti vardı, kod yoktu): okuma anında sayfa boş gelebiliyor — `fm open Messages` sonra
  `fm numbers --json`, EN'de eski kodda 6 koşunun **2'sinde** `Forma numaraları okunamadı.` rc=1 (0,94 sn'de); tablo
  bir an duruyor, okuma gelince gitmiş oluyor. Yeni kod boş okumada sayfayı yeniden açıp okuyor → 6/6 rc=0, 37 oyuncu.
- Yeni: düğme seçilmeden önce menünün o seçeneği sunduğu soruluyor (0,3 sn aralık, 10 sn üst sınır); kaydırmada
  `scroll` zaten istenen yerdeyse tek okuma, değilse pencere "değişti **ve** iki okumada aynı" olunca kabul (0,1 sn
  aralık, 5 sn üst sınır). Süreler yalnız üst sınır (§8.199'un işareti).
- Ölçüm: üç durum (başka sayfa açık / sayfa zaten açık / sayfa ilk kez) TR `md5 a7fde227…` + EN `md5 e7bf7ceb…`
  eskisiyle **birebir**; TR'de `fm numbers "Adam Reach" 44` (`stopAt` yolu) rc=0, EN'de `fm numbers "Oscar Thorn" 45`
  rc=0, negatif `fm numbers "Nobody Here" 3` → "kadroda yok", rc=1. Süre nötr (EN "zaten açık" 4,9 ↔ 5,0 sn).
- `tools/sweep.sh` 50 komut, 0 hata (`numbers` 6 sn, 42 satır). Kayıt geri alındı: verilen iki numara `-`e çevrildi,
  `md5` baştaki değere döndü; dil Türkçe, saat 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı.

### 8.212 `fm opposition <oyuncu> <talimat>=<değer>`: değer artık hem İngilizce hem ekranda görünen sözle kabul ediliyor (18 Ekim, TR + EN)

`cli/fm/OppositionCard.cs`. Eklenti seçenekleri İngilizce veriyor (`Never`/`Always`, `Normal`/`Easy`/`Hard`, `Left/Right/Weak
Foot`), ama çıktı `GameWords` yazıcısından geçtiği için hata ve yardım satırı onları oyunun sözleriyle yazıyor
("Seçenekler: - · Hiçbir Zaman · Her Zaman", "Pres=Always Sert giriş=Hard") — yani ekranda gösterilen ad geri yazılamıyordu
(`Pressing=Her Zaman` → `Pres: 'Her Zaman' yok`, satır `-` kalıyordu). Çözüm: değer, seçeneklerin kendi İngilizcesiyle
olduğu gibi `GameWords.Of(seçenek)` (oyunun o söz için verdiği karşılık) ile de eşleşiyor; sabit söz/tablo yok, liste
hâlâ eklentiden geliyor. Ters sözlük gerekmedi (ileri eşleme yetiyor).

- Öncesi (TR): `fm opposition "Kyle Knoyle" "Pressing=Her Zaman"` → `Pres: 'Her Zaman' yok. Seçenekler: - · Hiçbir Zaman · Her Zaman`,
  kartta `Pressing` `-`. Ölçülen gösterilen adlar (TR): markaj/pres `Hiçbir Zaman`/`Her Zaman`, sıkı giriş `Normal`/`Hafif`/`Sert`,
  ayak `Sol Ayak`/`Sağ Ayak`/`Zayıf Ayak` (İngilizce: `Normal`/`Easy`/`Hard`, `Left Foot`/`Right Foot`/`Weak Foot`).
- Sonrası (TR), üç durum — kart zaten açık / başka sayfa açık (`fm open Messages`) / kart ilk kez (`fm open Squad` sonrası):
  `Pressing=Her Zaman`/`Hiçbir Zaman`, `Tackling=Sert`/`Hafif`, `Foot=Sol Ayak`, `Tackling=Hard` hepsi rc=0 ve kartın kendi
  okuması istenen değeri yazdı (`Always`/`Never`/`Gusty`/`Minor`/`Left Foot`/`Weak Foot`). Negatif `Pressing=zzz` → rc=1,
  seçenekler satırı. `--clear` sonrası dört sütun `-`.
- İngilizce (dil `English`), aynı üç durum: `Always`/`Never`/`Hard` rc=0, kart doğru; negatif `Pressing=zzz` → `Pressing: no 'zzz'. Options: - · Never · Always`.
  Dönüş: `fm settings language "Türkçe"` → "English → Turkish"; son TR koşusu yine doğru. Kayıt test kaydı, saat ilerletilmedi,
  maç oynanmadı; talimatlar `--clear` ile boşaltıldı. Paylaşılan koda dokunulmadı → sweep gerekmedi.

### 8.213 `fm scout <odak>`: sonuç tablosunu kaydırdıktan sonraki sabit 700 ms, pencerenin kendi işaretine bağlandı (18 Ekim, TR + EN)
Oyuncu listesi gibi odak tablosu da yalnız görünen pencereyi çiziyor; `rows.Count < expected` olunca `scroll` ile
0.5/1 payına iniliyordu ve her kaydırmadan sonra sabit 700 ms bekleniyordu. Yeni: kaydırmadan sonra pencere
"öncekinden **farklı** ve art arda iki okumada **aynı**" olunca kabul ediliyor (0,25 sn aralık, en çok 5 sn).
Fark şartı şart: kaydırma sonrası ilk okuma bir an **eski** pencereyi veriyor, "iki okuma aynı" tek başına onu
kabul ederdi (Staff 74'ün tuzağı).
- Ölçüm (okuma yolu, `python3` + `/scroll` + `/texts?anchorpath=selectedfocus.Name&paths=1`, 30 ms aralık):
  kaydırmadan sonra pencerenin satır adları 207–485 ms'de değişiyor, tam içerik (ad + değer) iki okumada
  690–1080 ms'de oturuyor. Yani eski 700 ms tam sınırdaydı — makine hızına dair bir varsayım.
- `FM_TRACE=1 fm scout "England" --json` (yeni): `scroll` 8,27 sn → okumalar 8,33 (eski pencere, red) / 8,64
  (yeni pencere, bekle) / 9,06 (aynı, kabul). Kaydırmadan kabule 0,79 sn; eski 0,70 sn + tek okuma → nötr.
- Üç durum × iki dil, `fm scout "England" --json` (20 satır, tablo penceresi 18 satır olduğu için dal çalışıyor):
  TR (başka sayfa açık / sayfa açık / başka sayfadan) `md5 6915efcb…`; EN aynı üç durum `md5 86ccb87b…` —
  ikisi de eski derlemeyle **birebir** (`diff` boş). Satırların tamamında değer ve derece dolu.
- Negatif: `fm scout "ZZZ yok"` ve `fm scout 999` → rc=64, odak listesi; Türkçe dönüş `fm settings language
  "Türkçe"` → `English → Turkish`. Kayıt test kaydı: saat ilerletilmedi, maç oynanmadı.
- Not (bu maddenin dışı, eski davranış): `fm open "Messages"` hemen ardından odak listesi bir kez boş
  okunabiliyor (`Odak: 'England' diye bir seçenek yok`, rc=64) — §8.185'teki "sayfa açılmadan dönüyor" hâli.

### 8.214 `fm player <oyuncu> --career`: tablonun satırları başlık yazısıyla değil, satırın kendi ilk hücresiyle bulunuyor; Türkçede hiç çalışmıyordu (18 Ekim, TR + EN)
`cli/fm/Career.cs` satırların başladığı yeri (`start`) tablonun **başlık yazısıyla** buluyordu: `texts.IndexOf("Avg Rat") + 1`.
Ama başlık oyunun sözü ve eklenti onu her dilde başka bir İngilizceye çeviriyor — İngilizce oyunda `Info` / `Avg Rat`,
Türkçe oyunda aynı sütunlar `Information` / `Avg. Rating` (ölçüldü, hem kaleci hem normal oyuncu sayfasında). Türkçede
`IndexOf` -1 dönüyor, `start` 0 kalıyor ve `start <= 0` koruması devreye giriyordu: `fm player "Ben House" --career` →
`Kariyer tablosu okunamadı.`, rc=1. Yeni: `start = texts.FindIndex(SeasonMark.IsMatch)` — satırın kendi ilk hücresi
sezon (`2026/27`), başlık hiç okunmuyor; koruma `start < 0`. Kalecinin sütununu ayıran `Cln Sh` iki dilde de aynı
geldiği için dokunulmadı.
- Öncesi (eski derleme, `git checkout cli/fm/Career.cs` + derle; `fm player "Ben House" --career --json`):
  TR üç durum (kart ilk kez açılıyor `fm open "Messages"` sonrası / zaten açık / başka sayfa açık `fm open
  "Squad Dynamics"`) + kaleci (`Zach Jeacock`) → hepsi `rc=1`, `md5 a797f52cb11838fd711552e432048867`,
  "Kariyer tablosu okunamadı.". EN aynı üç durum → `rc=0`, `md5 0dfdaa7473271f10f0e28766a6d1d2e7`.
- Sonrası: TR üç durum + kaleci → `rc=0`, `md5 505029b89e88f455a296737967297581` (hepsi aynı); EN üç durum →
  `rc=0`, `md5 0dfdaa7473271f10f0e28766a6d1d2e7` — **eski derlemeyle birebir** (İngilizce çıktı hiç değişmedi).
- İçerik doğrulaması (TR): 15 sezon, `Name/Seasons/Fees/Totals` dolu; `Fees 30B £`, `Totals ["206","43","2","2"]`
  (EN koşusuyla aynı sayılar). İnsan çıktısı artık tam: `Toplam: 206 maç, 43 gol · ödenen bonservisler 30B £`,
  "GELİŞ" sütununda `kiralık`/`bedelsiz` (satırlar `Loan`/`Free` geliyor, `How()` eşliyor — iki dilde de doğru).
- Negatif: `fm player "ZZZ yok" --career` → rc=1, "'ZZZ yok' adında bir oyuncu kadroda da aramada da yok.";
  kariyeri olan diğer oyuncular (`Zane Okoro` 4 sezon, `Jenson Jones` 5 sezon, `Gael Mande`, `Cody Tomlinson`)
  rc=0 ve alanlar dolu. (Bu kayıtta kariyeri boş bir oyuncu yok, `marks.Count == 0` yolu ölçülemedi.)
- Not (bu maddenin dışı): JSON'da `How` hâlâ oyunun ham sözü (`Loan`/`Free` Türkçede, `loan`/`free` İngilizcede);
  insan çıktısı `How()` ile CLI'nin kendi sözünü yazıyor. Ayrıca dil değiştirildikten sonra sayfa yenilenmezse
  bağlı yazılar eski dilde kalıyor (`Nation: İngiltere` İngilizce oyunda) — §8.194'ün bilinen hâli.

### 8.215 `fm responsibilities give`: listeyi başa alma sabit 500 ms yerine listenin kendi cevabıyla (18 Ekim, TR + EN)
`cli/fm/Responsibilities.cs`. `GiveDuty`/`SetDuty` uzun görev listesini `scroll ?anchor=<TakeControlAll|DelegateAll>&to=0`
ile başa alıp **sabit 500 ms** bekliyor, sonra okuyordu. Ölçüm (python3, 40 ms aralıkla `/form?anchor=Take Control of All&up=12`):
kaydırmadan sonra satırlar ~0,35 sn'de oturuyor (iki ardışık okuma aynı) — yani 500 ms yalnız satırların ne zaman
çizildiğine dair bir tahmindi. Oyunun kendi cevabı da bu işi görüyor: `scroll` listenin **nereye oturduğunu** söylüyor —
`to=1` → `728,6 of 728,6`, `to=0` → `0 of 728,6`, `to=0.5` → `322 of 644`; yani baştayken ilk sayı `0` (virgül oyunun
ondalık işareti, dile bağlı değil). Yeni `ToTop()`: `scroll …&to=0` gönderiliyor ve **cevabın ilk sayısı `0` olana kadar**
(`Until`, 0,3 sn aralık, 6 sn üst sınır) bekleniyor; satırların çizilmesini hemen ardından gelen "iki okumada aynı"
işareti (`Duties`'in kendi okuması) karşılıyor. `fm responsibilities give "Assigning Scouts"` dört durumda (ilk açılış /
zaten açık / başka sayfa açık / liste elle dibe kaydırılmış) TR + EN **eski derlemeyle birebir**: TR
`md5 570b1bd243e4d67a2f4800b7588ab50b` (11–15 sn), EN `md5 0030afd6c876a5bc3197cca3da59e1b7` (11–12 sn); negatif
`fm responsibilities give "ZZZ yok"` → rc=64, `'ZZZ yok' diye bir görev yok` (EN: `There is no duty called 'ZZZ nope'`).
Kayıt değişmedi (kişi verilmediği için hiçbir görev devredilmedi), saat ilerletilmedi, dil Türkçeye döndü.
Yazma yolu da aynı yardımcıdan geçiyor: `take "Assigning Scouts" --yes` → `artık sende.` (14 sn), geri
`give "Assigning Scouts" "Jez George" --yes` → `Faruk Kavlak → Jez George` (11 sn); `fm responsibilities scout --json`
ile `Assigning Scouts | delegated | Jez George` doğrulandı. `tools/sweep.sh` 50 komut, 0 hata.

### 8.216 `schedule set`: antrenman takvimi iki sekmenin ay listesini karıştırınca okunamıyor; seçenek açıklamasının oturması ölçüldü (18 Ekim, PARK)
`fm schedule`/`fm schedule set` takvimi okurken ara sıra `Takvimin antrenman sekmesi okunamadı.` (rc=1) veriyor:
takvim sayfası (`Portal › Calendar`) kendi `MonthDropdown`'ını **her sekmede ayrı** tutuyor (`General` ve `Training`),
`/dropdowns` ikisini birden veriyor. Sekmeler ayrı aylara bakabiliyor (biri Ağustos, öteki Haziran) ve
`CalendarMonths()` hangi ayın gösterildiğini listenin **etiketinden** okuyor; etiket boş gelince ya da öteki sekmenin
listesi okununca `TrainingCalendar` yanlış aya (`value=0` = Haziran) geçiyor ve aranan gün ızgarada bulunamıyor.
Aynı koşu dizisinde 3 ardışık `fm schedule set 3 1 <seans>` çağrısının 1'i geçti, 2'si düştü; eski ve yeni derlemede
birebir aynı (koda bağlı değil). Çare: `General` sekmesinin ayı da elle Ağustos'a çekilip iki kez seçtirildiğinde
(`value=1` → `value=2`) koşular geçiyor. Ölçüm yolu: `python3` + `/dropdowns?json=1` + `/select?ptr=&value=`.
Aynı turda `PressAndConfirm`'in sabit 500 ms'i ölçüldü: seçenek basıldıktan sonra açıklama (`/form?anchor=Confirm&up=10`)
birkaç karede değişiyor — yeni metin 107–371 ms'de yerine oturuyor, arada **boşaltılmış** bir an var (6 metin, 312 ms)
→ 500 ms'in payı 1,3–2,2 kat. Değişiklik yazılıp derlendi, TR'de iki durumda eski derlemeyle **birebir** çıktı verdi
(`Physical → General` `md5 0aaf9592…`, `General → Physical` `md5 414fade6…`), ama üçüncü durum + İngilizce
tamamlanamadı (yukarıdaki ay karışıklığından); kural 4 gereği **kod geri alındı**, madde `[?]`.
İngilizce denemede ayrıca `'General' diye bir seans yok` çıktı: dil değiştikten sonra sayfa yenilenmediği için
seçenek adları hâlâ oyunun o günkü sözüyle geliyor (§8.194'ün devamı).

### 8.217 `fm propose`: öneri ekranının iki düğmesi artık eylem/kimlikle bulunuyor; Türkçe oyunda hiç çalışmıyordu (18 Ekim, TR + EN)
Kulüplere önerme akışı iki düğmeyi **oyunun İngilizce sözüyle** arıyordu: listedeki "Offer via TransferRoom"
(`press ?text=…`) ve teklif ekranının gönder düğmesi "Offer Player". İkincisinin sözü oyunun çeviri tablosunda
başka bir metne bağlı ("Offer Player" → "Oyuncuyu Takasa Kat"), bu yüzden Türkçe oyunda etiketi hiç İngilizce
gelmiyor: düğme ekranda `Oyuncuyu Kulüplere Öner` olarak duruyor ve `/translate?words=Offer Player` yanlış sözü
veriyor (`Propose` 8 sn bekleyip `Önerme ekranı açılamadı.` diyordu; üç durumda da rc=1). Kimlik ölçümü (ikisi de
dilden bağımsız, `docs/reference/enums.txt`): "Offer via TransferRoom" → eylemin `ActionID`'si **1164992875**
(`PersonOfferToClubs`), gönder düğmesi → eylemin gönderdiği olay **32** (`EventFmxTransferOfferResponse`,
`IsExitButton: True`); ikisi de TR ve EN oyunda aynı (`/buttons?json=1&gen=` + `/pull?path=…`).
Yeni: `cli/fm/Propose.cs` düğmeleri `SpeakToAction` / yeni `SpeakToActionEvent` (Acknowledge.cs) ile buluyor,
yazı aranmıyor. TR: üç durum + iki oyuncu aynı çıktı (`md5 55976f65…`, kiralamada `3faf6e00…`), öncesi rc=1;
EN: üç durum eski derlemeyle **birebir** (`md5 f97a8688…`, kiralamada `d030b63f…`), süre 10,5 → 9,4–10,7 sn.
Gönderme yolu da denendi (`--loan --yes` → "Önerildi.", sonra seçenek listeden kalkıyor — §8.94'ün beklenen hâli).
Yan bulgu: `fm actions` oyuncunun bu aksiyon listesini göremiyor ("No 'Remove From Loan List' in the menu" —
oysa düğme ekranda); `fm propose` bitince Ben House önerilmiş olarak kaldı.

### 8.218 `fm penalties`: "MAÇA" sütunu oyunun cümlesine bağlı — TR'de ham Türkçe, EN'de Türkçe "ilk 11" (18 Ekim, PARK)
Tablonun yer hücresi `tactic.TeamSelectedPositionStringFromPlayer` yolundan, yani oyunun kendi cümlesinden geliyor;
eklenti onu ancak tabloda birebir karşılığı varsa İngilizceye çeviriyor. Ölçüm (`fm penalties --json`, 2 Ağustos 2026,
tek taker James Collins + `penalties add "Tom Bayliss"`, sonra geri alındı): TR'de kulübedeki `"On Subs bench"`,
ilk 11'de başlayan `"İlk 11'de (DOSMSğ)"`; EN'de (`fm settings dil English`, aynı iki oyuncu) `"On Subs bench"` /
`"Starting at DMCR"`. Yani aynı alan aynı kayıtta dil başına başka bir söz: ilk 11 vakası mevki kodu taşıdığı için
çevrilemeyip ham kalıyor.
`cli/fm/Penalties.cs` `Starting()` yalnız `Starting at ` önekini tanıyor ve öneki sabit Türkçe yazıyor: TR'de
eşleşme olmadığı için insan çıktısı ham cümleyi, EN'de `ilk 11 · DMCR` (İngilizce tabloda Türkçe söz) yazıyor;
`--json` `Where` alanı da dile bağlı. `/form` bu satır için yalnız o metni ve yolunu veriyor (dile bağsız
"ilk 11/kulübe" alanı yok); `/tactic` Slot'ları kısaltılmış ad (`Bayliss`) ve sayısal `Position` maskesi taşıyor,
ceza tablosundaki tam adlarla birleştirmek kırılgan. Dilden bağımsız yol bulunmadan değiştirilmedi (PARK).

### 8.219 `jobs apply|interest`: iş panelinin iki cevabı olayla bulunuyor; basıştan sonraki sabit 1 sn yerine oyunun cevabı bekleniyor (18 Ekim, TR + EN)
`fm jobs "<kulüp>" apply|interest` iki düğmeyi İngilizce etiketle (`ButtonEnabled("Apply For Job")`,
`JobButton(label)`) arıyordu ve basıştan sonra **sabit 1 sn** bekleyip `dialog` okuyordu. Artık ikisi de oyunun
kendi olayıyla bulunuyor: `/buttons?json=1` → `Events` içinde `EventFmxApplyForJob` / `EventFmxDeclareInterestInJob`
(TR + EN'de aynı; pointer'lı basış), ve bekleme oyunun cevabına bağlı — "`dialog` doldu **veya** düğme artık
sunulmuyor" (0,25 sn aralık, en çok 6 sn).
Ölçüm (python, `/press?ptr=` + 40 ms aralıkla `/dialog` ve düğmenin `Enabled`'i): basıştan sonra düğme
**0,467 sn** (York apply) ve **0,629 sn** (York interest) yavaşlıyor, `dialog` 13,5 sn boyunca boş kalıyor →
eski kodun 1 sn'si makine hızına dair bir tahmindi ve tam o anda boş `dialog` okuyordu. Derleme izi (TR):
eski kod `press` 3,32 → tek `dialog` 4,43 (1,11 sn) → "Gönderildi…"; yeni kod `press` 4,35 → okumalar
4,41/4,45/4,79/4,84/4,91 (düğme `Enabled=False`) → aynı satır 0,56 sn'de. Üç durum (sayfa ilk kez açılıyor /
zaten açık / başka sayfa açık) × iki dil: TR York apply + Celesti interest + Udinese interest, EN York interest +
Celesti interest + Udinese interest → hepsi rc=0, çıktı "Gönderildi. Cevap gelen kutusuna düşer: inbox" /
"Sent. The answer will come to the inbox: inbox" (basış → kabul 0,20–0,75 sn). Veriden doğrulama: `fm jobs "York"`
artık "Başvuru kapalı…" (başvuru gerçekten gitmiş). Negatif: TR `fm jobs "Villarreal" apply --yes` →
"Villarreal başvuru kabul etmiyor…" rc=1; EN'de ulus işleri (`Denmark`, `Brazil`) panelde "Training Facilities"
olmadığı için `CanApply` zaten bilinmiyor, komut eskisi gibi "does not take applications" diyor (bu değişiklikten
değil; eski kodda da etiketle aranan düğme panel yokken bulunmuyordu). Ölçüm basışları test kaydında; kayıt
`fm load … --no-save` ile baştan yüklendi (dosyaya yazılmadı), dil Türkçeye döndü.

### 8.220 `fm jobs <ilan>`: iş paneli artık oyunun kendi kabından okunuyor; ulus ilanlarında başvuru durumu yanlış söyleniyordu (18 Ekim, TR + EN)
Panel, ekran yazılarıyla bulunuyordu: başlıkta kulübün adı (`Contains(job.Club)`) **ve** panelde "Training Facilities"
satırı olması gerekiyordu. İkisi de dile/kayda bağlı: ulus ilanlarının başlığı oyunun kendi sözüyle geliyor
(TR "Danimarka Menajerlik Görevi", satırın hücresi `team.name` = "Denmark") ve ulus panelinde "Training Facilities"
satırı yok — bu yüzden `shown` hiç true olmuyor, `CanApply` `null` kalıyordu. Sonuç: `fm jobs "Denmark"` "Başvurmak
için: jobs "Danimarka" apply" diyordu, oysa `apply` doğru biçimde "başvuru kabul etmiyor" (rc=1) diyordu; ölçüm:
panelde `Apply For Job` düğmesi `Enabled=False` (`/buttons?json=1`, olay `EventToPlugin:EventFmxApplyForJob`).
Yeni: panel oyunun kendi kabıyla okunuyor — `/texts?within=job-advert-container&paths=1` (şablonun adı, dilden
bağımsız; panel kapalıyken `[]`). Kap doluyor ama değerleri sonra geliyor: satır seçilince panel bir an yalnız
başlık + iki cevabı tutuyor (3 metin, **0 veri yolu**), sonra o ilanın satırları geliyor (York 16, Denmark 9 metin).
Bekleme oyunun kendi işareti: kap **veri yolu taşıyan** bir metin taşıyor **ve** art arda iki okuma aynı (0,25 sn
aralık, en çok 5 sn). Ölçüm (40 ms aralıkla): satır seçildikten 0,259 sn sonra 3 metin/0 yol (eski başlık duruyor),
0,466 sn'de yeni ilanın 9 metni hazır. Öncesi/sonrası: TR `jobs "Denmark" --json` `CanApply:null` + "panel açılmadı"
uyarısı, 8–9 sn → `CanApply:false`, uyarı yok, 3–5 sn; `jobs "Fildişi Sahilleri"` aynı; `jobs "United States"`
`CanApply:true` ("Başvurmak için…", düğme `Enabled=True`); menajersiz ulus ilanlarının liste sütunu artık "?" değil
"açık"/"kapalı" (menajeri olanlarda değişen yok: sütun yine "?", çünkü onlar hiç sorulmuyor). Kulüp ilanı (`jobs "York" --json`, `md5 34c583c8…` TR / `1ee49ee3…` EN) üç durumda **eskisiyle
birebir**; her komut üç durumda (başka sayfa açık / panel zaten açık / başka sayfaya geçtikten sonra) aynı `md5`.

### 8.221 `fm world`: dünya bilgisi artık menünün yazılarıyla değil veri yoluyla okunuyor (18 Ekim, TR + EN)
`World()` değerleri etiketlerin yanına/öncesine bakarak alıyordu (`Before("Save Game")`, `After("Save Game As")`,
`texts.FirstOrDefault(t => t.StartsWith("Saves automatically"))`, `Before("Estimated game speed:")`,
`After("Game Status", 3)`) ve menüyü `anchor=Manage settings for this game` ile buluyordu — hepsi oyunun kendi
sözü. Ölçüm (`/texts?anchorpath=game.GameTime&up=12&paths=1`): FM menüsünün bütün değerleri bağlı ve yolları
dilden bağımsız — `game.RealTimeSinceLastSave` ("Last saved less than a minute ago"), `game.GameTimeSinceLastSave`
(TR "Oyun içindeki süreye bakarsak 09:15 gibiydi"), `game.AutosaveModeAndIntervalString`, `game.GameTime`
(TR "1 Gün, 10 Saat, 26 Dakika"), `game.ActiveNationsCount`/`ActiveLeaguesCount`/`EstimatedPlayerCount`,
`game.DetailLevelClub/International.DisplayValue`, `Person.Name`/`Person.ManagedTeamsString`,
`app.LastSaveSummary.GameSummaryGameName`; "Saves automatically…", "Estimated game speed:", "Save Game" gibi
etiketlerin **yolu yok**, yalnız yazıları var. Yeni kod sayfayı `anchorpath=game.GameTime` ile buluyor, değerleri
yolla alıyor; bekleme "iki okuma aynı **ve** `game.GameTime` ile `game.RealTimeSinceLastSave` geldi" (ilk okuma
0,90 sn, işaret 1,47 sn). Tek bir yazı yolla alınamıyor: menajerin durum rozeti (TR "Oyun", EN "Playing") bağlı
değil — o, kulüp değerinin (`Person.ManagedTeamsString`) hemen ardındaki yazı olarak, etiketle değil komşusuyla
alınıyor (JSON'daki İngilizcesi TR'de "Game Information" geliyor; bu eklentinin ters tablosunun işi, okuma değil).
Ölçüm: TR + EN, üç durum (başka sayfa açık / menü zaten açık / başka sayfadan) → alan alan ve insan çıktısı
`md5` (TR `899c9313…`, EN `b4399afa…`) **eski derlemeyle birebir**; `sweep.sh` 50 komut, 0 hata (`world` 2 sn, 6 satır).

### 8.222 `fm release`: liste etiketle aranıyordu, oyunun etiketi artık "Set For Release" — komut hiç çalışmıyordu (18 Ekim, TR + EN)
`Release()` listeyi `dropdowns?json=1` içinde `Label == "Release"` ile arıyordu; oyunda o listenin etiketi
**"Set For Release"** (TR'de de aynı, eklentinin çevirdiği söz) → liste hiç bulunamıyor, komut her oyuncuda
"serbest bırakma seçeneği yok." (rc=1) diyordu ve `--free`/`--mutual` yolu hiç açılmıyordu. Ölçüm (kart
açıkken `/dropdowns?json=1`): `{"Name":"GenericDropdown","Binds":"selectedaction","Label":"Set For Release",
"Choices":["Bedelsiz Serbest Bırak","Mutual Termination"],"Within":[…,"PlayerContractCard",…]}`. Yeni: liste,
`Within` zincirinde **PlayerContractCard** (şablonun adı, dilden bağımsız) geçen `Binds == selectedaction`
listesidir; etiket eşleşmesi yedek kaldı. İkinci borç (kural 3): `select choice=` İngilizce sözle yapılıyordu
ama satırlar oyunun kendi sözüyle geliyor — TR'de `--free` "no choice 'Release On A Free'" veriyordu; artık
satır, oyunun `/translate` ile öğrenilen sözüyle (`Release On A Free` → "Serbest Bırak", oyunun satırı
"Bedelsiz Serbest Bırak" onunla **bitiyor**) ya da İngilizcesiyle bulunuyor ve `select` seçilen satırın kendi
yazısıyla yapılıyor (`Mutual Termination` iki dilde de İngilizce geliyor, o yüzden değişmedi).
Kanıt: TR `release "Ben House"` → iki seçenek; `--free` (onaysız) üç durumda tazminat penceresi ("£350B" = 350
bin) + "Vazgeçildi" (hiçbir şey değişmedi); EN aynı ("£350K"); izde `select ?choice=Bedelsiz Serbest Bırak&fire=1`
(eskiden `no choice`); negatif `release "ZZZ yok"` değişmedi, listesi olmayan oyuncuda (Oscar Thorn) yine "seçenek yok".

### 8.223 `fm stages`: kurası çekilmemiş turun tarihi İngilizce cümleye bağlıydı — TR'de hiç yazılmıyordu (18 Ekim, TR + EN)
Tur açıklaması (`StageContent`) "Bu tur … çekilecek / oynanacak" cümlesini `StartsWith("This round will be drawn")`
ile arıyor ve tarihi önekin arkasından kesiyordu. Cümleyi oyun kuruyor (içinde tarih var), eklentinin çeviri
tablosundan geçmiyor: TR'de `/texts` → `"Bu tur 13/8/2026 tarihinde oynanacak"` (yol yok), EN'de
`"This round will be drawn on 13/8/2026"`; aynı tur, aynı tarih. TR'de önek tutmadığı için not `null` kalıyor,
komut boş aşamada "Bu aşamada gösterilecek bir şey yok." diyordu (`--json` → `Note:null`); EN'de doğruydu.
Yeni: turun metinleri içinde **dört haneli yıl + ayırıcı** taşıyan ilk sözün tarihi alınıyor (`Matches.Rows`un
tarih tanıması gibi, sıra oyunun ayarından — §C); İngilizce söz araması kalktı. Kanıt: TR üç durumda
`Note:"13/8/2026"` / "Kura henüz çekilmedi; çekiliş 13/8/2026."; EN üç durumda ve insan çıktısında eskisiyle
birebir ("The draw has not been made yet; the draw is 13/8/2026."); TR `--all` 8 turun 6'sına tarih veriyor
(3. Tur 27/8, 4. Tur 3/9, Çeyrek 30/10, Yarı 18/12, Final 3/2/2027), lig tablosu (`League Stage`) ve oynanmış
turlar (`Ties`) değişmedi; negatif `stages "Carabao Cup" "ZZZ"` aynı "aşama yok" listesi. `StageContent` dışına
dokunulmadı; `sweep.sh` `stages`/`table` koşuyor → 50/50.

### 8.224 `fm player --matches`: maç puanları sayfası artık yazıyla değil oyunun kabıyla okunuyor (18 Ekim, TR + EN)
Sayfa üç İngilizce sözle tanınıyordu: bekleme `texts.Contains("Match Ratings") && texts.Contains("Inf")`,
okuma `anchor=All Competitions&up=10`, satırların başı `texts.IndexOf("Inf")`. Üçü de eklentinin çeviri
tablosundan geçiyor (TR oyunda da İngilizce geliyor: `/texts?within=PlayerFormCard` → `"Match Ratings"`,
`"Inf"`), ama dilden bağımsız değil. Yeni: oyun sayfayı kendi adıyla söylüyor — `/texts?within=PlayerFormCard`
sayfa açıkken içeriği, kapalıyken `[]` veriyor (ölçüldü: karta basılmadan `[]`, basıştan 0,30 sn sonra 13
metin, 0,42 sn sonra `formstats.GamesPlayed` bağlı, 0,59 sn sonra 19 satır oturmuş); her satır `paths=1` ile
bağlı olduğu **veri yoluyla** okunuyor ve "yüklendi" işareti özetin kendi bağı (`formstats.GamesPlayed`) +
satırların iki okumada aynı olması. `Rows()` artık `Inf` aramıyor: satırlar zaten numara + tarih çiftiyle
bulunuyor (`^\d+$` ardından tarih), tablonun üstünde bu çifti veren başka bir şey yok. `--json` alanları ve
satır çözümü değişmedi (Draper 20 maç, kaleci Jeacock 11 maç, hiç oynamamış Pardington 0 maç — üçünde de
`--json` md5'i eski derlemeyle birebir). Aynı fonksiyondaki bir dil artığı daha: puan sütununun başlığı
`heads.Add("PUAN")` ile sabit yazılıydı, `L("PUAN")` yapıldı — İngilizce oyunda tablo başlığı "PUAN"
kalıyordu, artık "RATING" (insan çıktısındaki tek fark; TR md5'i birebir).
Yan bulgu (bu maddenin dışı, eski derlemede de aynı): `fm open "Messages"` sonrası `player --matches`
arama yolundan gidiyor ve "form kutusu yok" ile düşebiliyor — eski derlemede 4 koşunun 2'si, yeni
derlemede de ara sıra; `fm open "Squad Dynamics"` (kadro tablosu yolu) her koşuda geçiyor.

### 8.225 `fm release --mutual`: asistanın cevabından sonra oyunun kendi kapanış sorusu TR'de cevap gibi yazılıyordu (18 Ekim, TR + EN)
Karşılıklı fesih teklifinden sonra `/talk` üç satır veriyor: `» Mutual Termination` (bizim seçimimizin yankısı),
asistanın cevabı ve oyunun kapanış sorusu. Son satır oyunun kendi sözü ve **dile göre kuruluyor** — TR
"Sizin için yapabileceğim başka bir şey var mı hocam?", EN "Is there anything else I can do for you?" — bu yüzden
eski `StartsWith("Is there anything else")` süzgeci TR'de tutmuyor ve o soru asistanın cevabıymış gibi yazılıyordu
(TR'de iki satır, EN'de tek satır). Ölçüm (`/talk` Says): TR `["» Mutual Termination", "Tamamdır hocam, …",
"Sizin için yapabileceğim başka bir şey var mı hocam?"]`; EN `["» Mutual Termination", "Alright, I'll pass on your
decision to him…", "Is there anything else I can do for you?"]`; `/translate?words=Is there anything else` boş
(cümleyi oyun kuruyor, tabloda yok).
Yeni: kapanış sorusu, konuşmanın **genel kapatma düğmesinin** bitirdiği satır olarak ayıklanıyor — düğme
`EventFmxClearChanges` (31) olayını gönderiyor (`buttons?json=1&gen=…` + `pull …&props=Event`; ölçüm: TR ve EN'de
kapatma düğmesi `Event:31`, diğer cevaplar `Event:44`), yani düğme varsa son insan-dışı satır düşülüyor; metin
aranmıyor. Düğme yoksa satırlar olduğu gibi yazılıyor (asistanın gerçek sorusu kaybolmasın).
Kanıt: TR üç durum (başka sayfa açık `Squad Dynamics` / sayfa ilk kez `Messages` / kart zaten açık) → yalnız
asistanın cevabı + "karşılıklı fesih teklif edildi" satırı; eskiden cevabın altında "Sizin için yapabileceğim
başka bir şey var mı hocam?" vardı. EN üç durum → eskisiyle birebir (tek satır). Negatif: serbest bırakma listesi
olmayan oyuncu (Cody Tomlinson, Gael Mande, Adam Reach) iki dilde de "seçenek yok" (değişmedi).
Dönüş/geri: kayıt `fm load … --no-save` ile iki kez baştan yüklendi; verilen karşılıklı fesih teklifleri geri
alındı, dil `fm settings language "Türkçe"` (`/state` → `turkish`), saat 2 Ağustos 2026 14:00, maç oynanmadı.
Dosya: `cli/fm/Release.cs` (yalnız karşılıklı fesih çıktısı); `AcknowledgeButton` yalnız okundu, `sweep.sh`
`release` koşmuyor.

### 8.226 `reply`: veri merkezi bulgularından tabloda olan cümle TR oyunda İngilizce kalıyordu (18 Ekim, TR + EN)
Bir mesajın eki "veri merkezi" (`row.AnalysisPointString`, Program.cs) analist cümlelerini yazıyor. Cümleler oyunun
kendi sözü: içinde sayı/isim taşıyanları eklenti çeviremiyor ve oyunun dilinde geliyor (TR'de dördü Türkçe), ama
**tabloda birebir duran** cümleyi eklenti İngilizceye çeviriyor ve geri eşini göndermiyor → TR oyunda o satır
İngilizce kalıyordu. Ölçüm (TR, `fm reply "Lincoln 1-2"`): bulguların dördü Türkçe, sonuncusu
"We've regained possession well in central areas of our own third of the pitch."; aynı cümle EN oyunda da aynı.
`/translate?words=…` yanıtı (X-Shown başlığı): `We've regained possession well in central areas of our own third of
the pitch.\tYarı sahamızın orta kısmında çok top kazandık.` — yani oyunun tablosunda eş var, CLI yalnız sormuyordu.
Yeni: bulgular yazılmadan önce `GameWords.Ask(Fm, points)` ile (bir kez) soruluyor ve satır `GameWords.Of(point)` ile
yazılıyor; `GameWordsLines`'ın satır bölmesi "· " ile yapıştığı için cümle oradan geçmiyordu, bu yüzden tam eş
`Of` ile alınıyor. Tabloda olmayan cümle (`Ask` boş yanıt) olduğu gibi kalıyor.
Kanıt (üç durum: başka sayfa açık `Squad Dynamics` / mesaj zaten açık / `Messages` sonrası): TR'de tam dosya farkı
yalnız o satır — `· We've regained possession…` → `· Yarı sahamızın orta kısmında çok top kazandık.`; EN'de bulgular
üç durumda da eskisiyle **birebir**. Türkçe cümleler için tablo boş yanıt veriyor (yanlış eş yazılmıyor).
Yan bulgu (bu değişiklikten değil): ekin okunuşu duruma göre eksik gelebiliyor — ilk koşuda "Goller / Maçın oyuncusu /
Taktikler" satırları varken ikinci koşuda yok, bir EN koşusunda takım adları boş geldi ("data hub —  1 - 2  "). Yani
`WriteAttachment`'ın 1,5 sn'si (§8.192) yarım bağlanmış sayfayı okuyabiliyor; sabit bekleme maddesi aynı kalıyor.
Dosya: `cli/fm/Program.cs` (yalnız veri merkezi eki dalı); `GameWords.Ask/Of` var olan yardımcılar, değişmedi.
`sweep.sh` **50 komut, 0 hata** (İngilizce oyunda koşuldu; `inbox` 1 sn/16 satır).

### 8.227 `fm scout areas`: "Scouting Areas (0)" başlığı yazıyla aranıyordu, TR oyunda liste hiç bulunmuyordu (18 Ekim, TR + EN)
Recruitment Focus formunun ikinci adımında bölge listesinin başlığı oyunun kurduğu bir cümle ve içinde sayı var:
TR oyunda `Gözlemleme Yerleri (0)`, EN oyunda `Scouting Areas (0)`. Eklentinin çeviri tablosunda bu cümle birebir
olmadığı için İngilizce sözü de yok; `ScoutAreas` listeyi `FormAfter(form, "Scouting Areas", "dropdown")` ile
arıyordu → TR'de `fm scout areas` her durumda "Bölge listesi bulunamadı." (rc=1, ~10,5 sn; 8 sn boşa yoklama +
formu kapatma). EN'de çalışıyordu (tabloda "Scouting Areas" var).
Liste aslında formun kendi bağını taşıyor: `Binds = focusitem.possiblescoutingareasdropdownselectedoption`
(`/form?anchor=Back&up=12&buttons=1`, adı dilden bağımsız). Yeni `FormBound(form, binds)` yardımcısı denetimi
`Binds` ile buluyor; `ScoutAreas` yalnız onu kullanıyor. Başlık metni hiç okunmuyor.
Kanıt — öncesi: TR üç durum (`fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` sonrası)
hepsi rc=1 "Bölge listesi bulunamadı."; EN rc=0 `["Competition ›","Nation ›","Recently Selected ›"]`. Sonrası: TR üç
durum rc=0, aynı üç seçenek, `md5 fed56895c45431dd9820725651421c2b`; EN üç durum rc=0, aynı md5 — EN çıktısı
değişiklik öncesiyle **birebir**. İşaret ölçümü (`FM_TRACE=1`): `click ?text=Next` 2,13 sn → ilk `form?anchor=Back`
2,20 sn'de bağlı denetimi taşıyor (ilk okumada hazır; 8 sn üst sınırın payı büyük). Negatif: `scout areas "Nation"` →
`Europe ›` + "altı var" satırı; `scout areas "Nope"` → `error: no submenu 'Nope'` (eskisi gibi).
Geri: her koşu formu `Cancel` + onay penceresiyle kapatıyor (izde `Confirm` yok); sonrasında `fm scout --json` yine
**4** odak (yeni odak açılmadı), dil TR (`/state` → turkish), saat 2 Ağustos 2026 09:15, maç oynanmadı.
Dosya: `cli/fm/ScoutFocus.cs` (`FormBound` + `ScoutAreas`; `ScoutNew`/`AddAreas` yolu bilerek dokunulmadı — orada
aynı başlık `before/after` sayısı için okunuyor ve fonksiyonda temizlenmesi gereken başka sabit beklemeler var).
`sweep.sh` gerekmedi (paylaşılan koda dokunulmadı; `scout areas` sweep'te yok).

### 8.228 `fm scout new/edit --area`: bölge listesi yazma yolunda da yazıyla aranıyordu, TR'de komut çöküyordu (18 Ekim, TR + EN)
Okuma yolu (§8.227) `FormBound`a geçmişti; yazma yolu (`ScoutNewForm`'un bölge döngüsü) hâlâ
`FormAfter(second, "Scouting Areas", "dropdown")` ile başlık yazısını arıyordu ve `before` yazısını da
`StartsWith("Scouting Areas")` ile buluyordu. Başlık oyunun kendi cümlesi: TR "Gözlemleme Yerleri (0)", EN
"Scouting Areas (0)". TR'de liste `null` olunca `.Value` **yakalanmamış** `InvalidOperationException` attı:
`scout edit 1 --area "Türkiye"` → `Unhandled exception … Nullable object must have a value` (satır 552); form
açık kalıyordu. Yeni: liste `Binds` ile (`possiblescoutingareasdropdownselectedoption` — okuma yoluyla aynı
denetim) ve "eklendi" işareti listenin **hemen önündeki** yazının değişmesi (`FormHeadingBefore`); hiçbir sabit
söz aranmıyor, iki dilde de aynı öğe karşılaştırılıyor.
- Ölçüm (kimlik): TR adım-2 formu (`/form?anchor=Back&up=12&buttons=1`) → 47 öğe; `dropdown` `Binds:
  focusitem.possiblescoutingareasdropdownselectedoption`, hemen öncesinde `text` "Gözlemleme Yerleri (0)";
  `select …&names=Nation>Europe>England&fire=1` sonrası aynı iki öğe: "Gözlemleme Yerleri (1)" + ardından
  `button` "England". Yani işaret, aynı başlığın sayısı — dil değil.
- Öncesi (eski derleme): TR `fm scout edit 1 --area "Türkiye"` → yukarıdaki çökme (rc≠0, form açık); EN aynı
  komut `--area "Competition>England>Sky Bet Championship"` → bölge ekleniyor, komut scout adımına geçiyor
  (`select` 6,92 sn; sonra `--by "ZZZ yok"` hatası).
- Sonrası (yeni derleme), üç durum × iki dilde aynı komut (`--area "Competition>England>Sky Bet Championship"
  --by "ZZZ yok"`; bölge adımından **sonra** bilerek geçersiz scout → form `Cancel` + onayla kapanır, kayıt
  değişmez):
  - TR, başka sayfa açık (`fm open "Squad Dynamics"`) → `select` 9,00; ilk form okuması 9,06, değişim 9,22'de
    (0,16 sn) → komut scout adımına geçti ("Formda scout seçme butonu yok."), "Bölge eklenemedi" yok.
  - TR, hemen ikinci koşu → aynı; TR, `fm open "Messages"` sonrası → aynı.
  - EN, üç durum (Squad Dynamics / hemen ikinci / Messages) → üçünde de `select` sonrası komut `Assign Any
    Scouts` penceresine geçti ("Scout: there is no option 'ZZZ yok'"), yani bölge adımı geçti. EN eski
    derlemede de aynı yolu geçiyordu → İngilizce davranış birebir.
- Negatif / değişmeyen: zaten seçili bölge (`Nation>Europe>England`) iki dilde de eskisi gibi eklenmiyor
  ("Bölge eklenemedi: Nation › Europe › England" — başlık sayısı değişmiyor, oyun yinelenen bölgeyi almıyor).
- Geri: her koşu formu `Cancel` + onay penceresiyle kapattı, hiçbir odak `Confirm` edilmedi; `fm scout --json`
  yine **4** odak (Complete kopya yok), dil TR (`/state` → turkish), saat 2 Ağustos 2026 09:15, maç oynanmadı.
- Yan bulgu (bu turda düzeltilmedi): `scout new/edit --by` / `--analysts` seçim penceresini açan `Edit` düğmesi
  `FormAfter(form, "Assign Scouts", "button")` ile bulunuyor; başlık TR'de "Gözlemci Görevlendir (1)" →
  "Formda scout seçme butonu yok." İki `Edit` düğmesi (scout/analist) ad, yazı ve olay bakımından **birebir
  aynı** (`Name: button-base-trailing-icon-label`, `Events: OpenPanel`); pencere başlığı (`Assign Any Scouts`)
  da yazıyla. Ayırt eden tek şey formdaki sırası ve pencere başlığı → ayrı iş.
- Dosya: `cli/fm/ScoutFocus.cs` (`FormHeadingBefore` + bölge döngüsü), `docs/BAGIMLILIKLAR.md`, bu defter.
  `sweep.sh` gerekmedi (paylaşılan koda dokunulmadı; `scout` sweep'te yok).

### 8.229 `fm scout new/edit --by` / `--analysts`: satır ve pencere yazıyla değil, rol ve bağla bulunuyor (18 Ekim, TR + EN)
`AssignFocusScouts` düğmeyi `FormAfter(form, "Assign Scouts", "button")` + `Text == "Edit"` ile, pencereyi de
`anchor="Assign Any Scouts"` ile arıyordu; başlık oyunun cümlesi (TR "Gözlemci Görevlendir (1)") → TR'de "Formda scout
seçme butonu yok." Ölçüm (TR, `/form?anchor=Back&up=12&buttons=1`): iki satırın `Edit` düğmesi **birebir aynı** (`Name:
button-base-trailing-icon-label`, `Events: OpenPanel`, `Action` boş, `Binds` yok, satır kapsayıcıları da aynı adlar);
ayırt eden tek şey yanındaki insanın rozeti — `Texts: ["George Herbert", "(Gözlemci)"]` / `["Jack Coles", "(Oyuncu
Analisti)"]`. Yeni: rol sözü oyunun kendi tablosundan (`/translate?words=Scout` → "Gözlemci", `Analyst` → "Analist"),
satır rozetten (yoksa form sırası: gözlemci önce). Pencere artık kutusunun **bağıyla** doğrulanıyor
(`/form?anchor=Assign Any Scouts` → `Binds: focusitem.anyscout`; analistte `focusitem.anyanalyst`) — yanlış satıra
basılırsa liste taranmadan hata veriyor. "Kaç seçildi" cümlesi de yazıyla aranmıyor: sayı, oyunun kendi kabından
(`/texts?within=SIVisible-selected-scouts-number` → "1 gözlemci seçildi"; analistte `…-analysts-number`) okunuyor.
Pencere başlıkları TR oyunda da İngilizce ("Assign Any Scouts", "World Knowledge", "Assigned Scout") — oyun bu
etiketleri çevirmiyor, yani yazı orada da aynı; asıl kırılan başlık `Assign Scouts` cümlesiydi.
Kanıt (hepsi `rc=0`): TR üç durum (`fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"`
sonrası) `fm scout edit "England" --by "Kenny Morrison, Simon Rowley"` → "Odak güncellendi: İngiltere · Kenny Morrison
+ Simon Rowley", odak "2 gözlemci"; `--analysts "Jack Coles"` → "analist: Jack Coles". EN aynı üç durum → "Focus
updated: England · Kenny Morrison + Simon Rowley"; `--analysts` → "analyst: Jack Coles". Negatif TR `--by "ZZZ yok"` →
rc=64 "Gözlemci: 'ZZZ yok' diye bir seçenek yok. Seçenekler: …". Süre 17,7–27,9 sn (eskiden TR'de hiç çalışmıyordu).
Dönüş: odak eski hâline (`--by "George Herbert"` → "1 gözlemci · 1 analist"), dil `fm settings language "Türkçe"`,
saat 2 Ağustos 14:00, maç yok. ⚠ Uzun oturumda köprü iki kez "the main thread did not run this request within 30s"
ile sustu (aynı komut yeniden başlatmadan sonra çalıştı; §8.99'daki gibi); EN negatif koşusu bu yüzden rc=1 kaldı,
TR negatifi rc=64.

### 8.230 `fm staff`: kaydırmadan sonraki sabit 400 ms yarım tabloyu okuyabiliyordu — artık satırlar "oturdu" işaretiyle (19 Ekim, TR + EN)
Personel tablosu kaydırıldıktan sonra satırlarını **iki adımda** diziyor: önce tablonun tepesindeki takım, bir-iki
kare sonra gerisi. Ölçüm (`/texts?anchor=All Staff&up=12&paths=1`, 40–70 ms aralıklı okumalar, `/scroll` sonrası):
`to=1` **6 → 10 → 27** satır, `to=0.75` **13 → 17 → 34**, `to=0.5` **21 → 25 → 36**, `to=0.25` **27 → 31 → 34**;
yarım hâller 0,1–0,2 sn duruyor (en uzunu `to=0.75`'te 13 satır, 0,03→0,23 sn) ve `/scroll`un bildirdiği konum ilk
okumada bile hedefte (`455,9 of 911,8`), yani konum değil **diziliş** gecikiyor. Sabit 400 ms'in payı bu yüzden yalnız
~2 kat; yavaş bir makinede (ya da daha kalabalık bir kulüpte) 6/13/17 satır okunup oyuncular sessizce eksiliyordu.
Yeni: her konum, satırlar **0,25 sn arayla üç okumada aynı** olana kadar okunuyor (oyunun kendi "diziliş bitti"
işareti; `Until`, üst sınır 4 sn).
Kanıt: TR (insan `md5 4df8a2aa85db8c96b94348fd3a41cd1c`, `--json` `0d24d6ceeaba9cf15553bb4421613e42`) ve EN
(`8b2e842f7efbd5ac7dbce534a5ce5ef2` / `50d543a83c2259ce25b069d6fd1a7c8f`) üç durumda (başka sayfa açık / tablo
zaten açık / başka sayfaya geçtikten sonra) **eski derlemeyle birebir**, 48 personel eksiksiz; izde konum başına
dört okuma (ör. 4,62 / 4,91 / 5,20 / 5,53 sn). Bedel: komut ~1,5–2 sn yavaş (TR 5–8 → 6–8 sn; EN 4–5 → 6–7 sn).
`sweep.sh` **50 komut, 0 hata** (Türkçe oyunda). Dosya: `cli/fm/Staff.cs` (`Staff()` içindeki `Settled()`).
Yan not: aynı fonksiyonda kalan iki yazı bağımlılığı — kaydırma çapası `Senior Squad` ve sekme düğmesi `All Staff`
(ikincisinde `name` de veriliyor) — bu turda dokunulmadı; ikisi de oyunun tablo sözünden geliyor ve TR + EN'de
çalıştığı görüldü, ayrı bir madde olarak duruyor.

### 8.231 `fm stages` kupa turu: kaydırma menzili oyunun ondalık işaretiyle çözülmüyordu; düzeltilince gezinti çalışıyor ama kararsız (19 Ekim, TR + EN)

Carabao Cup 1. Tur'da 35 bağ var, pencerede ise 18–25 satır duruyor; liste `to=0..1` ile geziliyor ve önce menzil
okunuyor (`Scroll(first, null)` → `"0 of 1401,8"`). Bu sayıyı oyun **kendi dilinin ondalık işaretiyle** yazıyor
(TR "1401,8", EN "1636.6"); `Stages.cs` onu `double.TryParse(..., InvariantCulture)` ile çözdüğü için Türkçe oyunda
`extent = 0` oluyor ve **gezinti hiç çalışmıyor** (yalnız ilk pencere okunuyor). Ölçüm (aynı durum, TR): eski
derleme 3/3 **25** bağ, izde 0 kaydırma adımı; `--all`da 1. Tur **18** bağ (EN'de 24). `GameNumbers.Read` ile
çözülünce gezinti çalışıyor (12 `to=` adımı) ama sonuç **kararsız**: TR 35/35/35 (aynı durumda), sonraki durumlarda
35/19/19; EN 35/35/19. Sebep koda değil gezintinin kendisine ait: çapa, okunan pencerenin ilk bağının **yazısı**
(`/scroll?anchor=<kulüp>`) ve kaydırma ilerleyince o satır yerleşimden çıkıyor → `/scroll` cevapsız kalıp döngü
kırılıyor (elle ölçüm: `to=0.4` çalıştı, `to=0.5` cevapsız kaldı ve pencere bir daha değişmedi); ayrıca "iki okuma
aynı" işareti kaydırmadan **önceki** pencereyi de kabul edebiliyor (Scouting 100 / Matches 82'deki tuzağın aynısı).
İkisi birlikte düzeltilmeli: sabit bir çapa (satırın işaretçisi ya da listenin kabı) **ve** "pencereden farklı **ve**
iki okumada aynı" işareti. Değişiklik kanıtlanmadığı için `git restore cli/fm/Stages.cs` ile geri alındı (§8.205 kuralı).
Yan bulgu: aynı menzil okuması `Responsibilities.cs` 109'da da var (`parts[1].Trim()` → `InvariantCulture`); orada
çözülemeyince `hidden = 6000` yedeği devreye girip **en çok** adım (12) gezildiği için sonucu bozmuyor, yalnız
gereksiz turlar atıyor (TR'de her `fm responsibilities` 12 adım). `Jobs.cs` 177 aynı sayıyı `Replace(',', '.')` ile
doğru okuyor; üçünün de `GameNumbers.Read`e geçmesi ayrı ve küçük bir iş.

### 8.232 `fm prep`: gösterilen odak adı geri yazılamıyordu, `--json` İngilizce kalıyordu (19 Ekim, TR + EN)

Odakların adı ve açıklaması oyunun kendi sözleri (`MatchFocusSessionName`/`...Description`); veri İngilizceyi
taşıyor ("Progression", "Focus on ball progression up the pitch in possession."), oyunun tablosunda Türkçesi var
(`/translate` → "Kaydedilen İlerleme"). İnsan çıktısı `Ui.Safe`/`GameWords` ile çevriliyordu, ama
`fm prep <yuva> <odak>` seçeneği ham `Name` üzerinden arıyordu → TR'de **ekranda görünen ad** yazılınca
"'Kaydedilen İlerleme' diye bir seçenek yok", rc=64; yalnız İngilizcesi ("Progression") çalışıyordu. `--json` da
`Name`/`Means`/`Options` alanlarını ham İngilizce veriyordu (TR insan çıktısı Türkçe olduğu hâlde).
Yeni: seçenek `Matching` ile önce gösterilen sözde, sonra verinin İngilizcesinde aranıyor (sayıyla seçme duruyor);
hata listesi, interaktif liste, "zaten/→" satırları ve `--json` alanları `GameWords.Of` ile yazılıyor; EN için
değişen satır `L()`ya alındı. Kanıt: TR `fm prep --json` önce `md5 50611761b5f34661f55fc4722c919043` (İngilizce
adlar), sonra `e9a22f165f7aaf41c3150d1e3861dc61` (Türkçe) — üç durumda (başka sayfa açık / zaten açık / başka
sayfadan) aynı; `prep topla "Kaydedilen İlerleme"` rc=64 → rc=0 "zaten Kaydedilen İlerleme"; `prep topla 2`
sayıyla aynı sonuç; negatif `zzz` Türkçe seçenek listesiyle rc=64. EN: `git stash` + eski derleme ile üç durum
**birebir** (aynı md5). ⚠ Dil değiştirildikten sonra bu kayıtta oyunun bazı bağlı sözleri eski dilde kalıyor
(`fm training --position` EN'de "İkinci Forvet", `prep` odakları Türkçe) — yeni kodun değil, oyunun bağlı
değerleri tazelememesinin sonucu; temiz EN ölçümü için oyun yeniden açılmalı (§8.194'ün devamı).
Yan bulgu (aynı turun sweep'i): arka arkaya 50 komutta `health` ("Kadro tablosunda 'GeneralInfo' görünümü
bulunamadı", 20 sn sonra rc=1) ve `youth --training` ("Altyapı antrenman tablosu açılmadı") düştü; ikisi de tek
başına (ve `fm offers` sonrası) rc=0 — sayfa/görünüm durumuna bağlı, `SquadView`'ın "görünüm listesi 20 sn boyunca
gelmedi" dalı. Tekrar üretilemedi; ayrı madde açılmadı, ölçüm için sweep'in aynı sırası ve sayfa durumu gerekli.

### 8.233 `fm calendar`: "Today" basıldıktan sonraki bekleme oyunun ay **değişti** cevabına bağlandı (19 Ekim, TR + EN)

`CalendarMonths` (Calendar.cs 146–158) takvim sayfasının ay listesini "Today" düğmesine basıp o ayı bugünün ayı sayıyor;
bekleme "art arda iki okuma aynı ve boş değil" idi. O işaret basıştan **önceki** ayı da kabul eder: ölçüm — düğme
`/buttons?json=1`'de basıştan hemen sonraki ilk okumada hâlâ eski ay geliyor (üç denemede 126 / 95 / 128 ms), yeni ay
198 / 252 / 304 ms'de oturuyor. Yani pay bir okuma aralığından (0,3 sn) az; yavaş makinede iki okuma da eski ayı verir,
ay haritası (hangi ay hangi tarih) yanlış aydan sayılır ve **bütün ayın maçları** yanlış listelenir. Ayrıca "Today" düğmesi ay zaten
bugünün ayındayken `Enabled=False` (ölçüldü: selected 6/10/13 iken True, 2 iken False) ve basış "error: … is disabled"
dönüyor — o hâlde beklemeye gerek yok. Yeni işaret: okunan değer basıştan önceki değerden **farklı** ve sonra iki
okumada aynı (üst sınır 8 sn); basış reddedilirse hiç beklenmiyor. TR + EN `fm calendar --json` üç durumda (başka sayfa
açık / zaten açık / başka sayfaya geçtikten sonra) ve ay elle uzağa kaydırılıp denendiğinde eski derlemeyle **birebir**
(TR `0dd9e2647a65b730035f4f24925b8beb`, EN `025897203ed1add7dc0f55748250abc5`); zaten açıkken komut 1,0 sn (yeni
dal beklemiyor), ay uzaktayken 2,0 sn ve liste doğru aya dönüyor. `fm schedule` ve `fm fixtures` (aynı yardımcıyı
kullanan iki komut) çalışıyor.

### 8.234 `scout remove`/`restart`: sayfanın düğmeleri artık gönderdikleri olayla bulunuyor; "süren odak" işareti etiketten okunduğu için yarışıyor (19 Ekim, TR + EN)

Odak sayfasının iki düğmesi `press/click text="…"` ile, yani oyunun çeviri tablosundan gelen İngilizce sözle
bulunuyordu. Ölçüm (`/buttons?gen=…&json=1` + `pull?path=<Action>&props=ActionID,Event`, TR): odak sayfasındaki
dört düğmenin dördü de `SpeakToAction` ve eylemleri **Remove → `EventFmxRecruitmentFocusRemove` (53)**,
**Restart → `EventFmxRecruitmentFocusRestart` (54)**, "Create Recommended Focus" → `1245866784`
(`…CreateRecommendedFocus`) gönderiyor. Yani düğme, `Acknowledge`/§8.209/§8.217'deki yardımla (`SpeakToActionEvent`)
olayından bulunabiliyor: artık işaretçisiyle basılıyor, etiket hiç okunmuyor (bulunamazsa `remove` eskisi gibi görevler
panelindeki kaldır simgesine düşüyor). **"Edit Recruitment Focus" ve "Create a New Recruitment Focus" ise
`ActionID 0` + `Event 0`** — oyun bu ikisine hiçbir kimlik vermiyor; ikisi de etiketiyle kalıyor (etiket oyunun kendi
çeviri kaydından gelen İngilizce söz, §8.209'daki tür; TR'de ölçülen dört etiket İngilizceydi).
Kanıt: TR üç durum (başka sayfa açık → `restart` rc=0 "Odak yeniden başlatıldı: Sky Bet League Two"; sayfa zaten açık →
`remove 4 --yes` rc=0 "Odak durduruldu"; başka sayfaya geçtikten sonra → durmuş odağa `remove` rc=1 "zaten durmuş",
süren odağa `restart` rc=1 "zaten sürüyor"), EN aynı koşular (`Focus restarted` / `Focus stopped` rc=0, negatifler rc=1);
sonuç `fm scout --json`ten doğrulandı (durdurulan odak `Stopped/False`, yeniden başlayan `In Progress/True`).

Yan bulgu (düzeltilmedi, ayrı iş): `FocusAt` (`Scouting.cs` 153) bir odağın **süren** olup olmadığını
`buttons` listesinde `Label != "Restart Recruitment Focus"` ile anlıyor — yani yine bir etiket ve **sayfa değişiminden
sonra okunan** bir liste. Bir `restart`tan sonraki ilk `fm scout` iki kez yanlış okudu: 1 numaralı odak ("England",
gerçekte sürüyor) "Durduruldu" göründü; bir sonraki koşuda yine "Devam Ediyor" oldu ve `--json` `Running:true` dedi.
Sebep §8.231/§8.233'ünkiyle aynı: liste seçim değiştikten sonra **eski odakların** düğmelerini bir süre taşıyor ve
"art arda iki okuma aynı" işareti bu eski hâlde oturuyor (etiketle okuyan işaret yalnız kendini doğruluyor). Doğru
işaret "düğme kümesi önceki seçimin kümesinden **farklı** ve sonra iki okumada aynı" ya da `Remove`/`Restart`
varlığının 53/54 olayından okunması + aynı "değişti" koşulu; ölçülüp ayrı bir turda yapılmalı (§8.120 bu düğmelerin
her zaman çıkmadığını zaten söylüyor).
Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (Türkçe oyunda). Değişiklik yalnız `ScoutFocus.cs` içinde;
`SpeakToActionEvent` ve `Fm.Get` dokunulmadı. Not (ölçümün kayda etkisi): remove/restart çalıştığı için odak listesi
4 → 7 oldu (üç yinelenen "Sky Bet League Two": biri sürüyor, ikisi durmuş; `restart` eskisini listede "durmuş"
bırakıyor), aynı bölgeyi süren bir odak durduğu için tarama kapsamı bozulmadı; silme yolu olmadığından temizlenmedi.

### 8.235 `fm budget ayarla`: kartı açan döşeme ve kartın iki düğmesi artık yazıyla değil kimliğiyle basılıyor (19 Ekim, TR + EN)
Ne: `cli/fm/Budget.cs`. Üç basış da oyunun **İngilizce** sözüne dayanıyordu: kartı açan döşeme
`click text="Adjust Transfer and Wage Budget"`, adımı tutan düğme `click text="Got It"`, karttan çıkış
`click text="Cancel"` — üçü de oyunun kendi sözü, bu dilin tablosundan geçiyor. Ölçüm: döşeme bir
`SpeakToAction`, eylemi **1885623898** (`ClubBudgetAdjustmentTransfersAndWages`; yanındaki "Adjust Scouting Budget"
1332374646 `…Scouting` — `docs/reference/enums.txt`); kartın "Got It"i `Events` içinde oyunun
**EventFmxBudgetAdjustmentConfirm** olayını gönderiyor (enum 146), "Cancel" ise **aynı panelin** yalnız kapatın
düğmesi (`ClosePanel`; kart açıkken `/buttons?json=1` panel `c16a10db…`te dört düğme veriyor ve panel Guid'i kapatıp
yeniden açınca da aynı). Artık üçü işaretçisiyle basılıyor, kimlik bulunamazsa eskisi gibi yazıya düşülüyor.
Öncesi/sonrası: TR + EN, üç durum (başka sayfa açık / sayfa zaten açık / başka sayfaya geçtikten sonra) × üç yol
(`ayarla 99` rc=1 hata+Cancel, `ayarla 15` "zaten" +Cancel, `ayarla 14` gerçek değişim+Got It, sonra `ayarla 15` ile
geri) — on beşer koşu iki dilde **eski derlemeyle birebir** (`md5`), izde eskiden `click ?text=…`, yeni derlemede
`buttons` + `click ?ptr=…`, rc dağılımı aynı; `fm budget --json` değişimden sonra 584B £ / 1.517 £ aşılmış, geri
alındıktan sonra 626B £ / 2.320 £ (EN: 584K/626K) — kayıt başladığı yere döndü. Değişmeyen: kartın **okuma** yolu
(`BudgetFigures` "Wage Budget"/"Transfer Budget", `BudgetOverview` "Transfer Budget"/"Scouting" çapaları, `BudgetCard`
"Budget Adjustment") yazıyla kalıyor — ölçüldü, o sayıların sayfada veri yolu yok (`/texts?paths=1` yalnız
`club.RemainingWageBudgetWithText`, `club.CurrentWageSpendingWithText` veriyor), yani ayrı bir iş.

### 8.236 `fm save --as` / `fm saves delete`: kayıt tarayıcısının "Cancel" ve "Save"i artık gönderdikleri olayla basılıyor (19 Ekim, TR + EN)
`SaveAs` (Saves.cs 289) ve `CloseSaveBrowser` (240) düğmeyi İngilizce sözüyle basıyordu (`click ?text=Save`,
`?text=Cancel`). Oysa tarayıcının dört düğmesi **aynı şablonu** taşıyor (`Name` = `button-base-leading-icon-label`:
Browse, Delete, Cancel; `button-primary-leading-icon-label`: Save) — yani `Name` ayırt etmiyor; ayırt eden gönderdikleri
olay. Ölçüm (TR, tarayıcı açıkken `/buttons?json=1`, panel `008eea55baa5a3c49b3e446596ede765`): **Cancel** `ClosePanel`,
**Save** `SaveGame`, Delete `DeleteSelectedGame`, Browse `SetDatastoreValue`; hepsi `Enabled=True`. Artık yardımcı
(`BrowserButton(save)`) önce `Events == SaveGame` düğmesini buluyor (Save onu basıyor), Cancel için **aynı `Page`teki**
`Events == ClosePanel` düğmesi alınıyor; kimlik bulunamazsa eskisi gibi yazıya düşülüyor.
Kanıt: TR + EN, üç durum (başka sayfa açık `fm open "Messages"` / FM menüsü zaten açık / başka sayfaya geçtikten sonra
`fm open "Squad Dynamics"`) × iki yol: `saves delete "<yüklü kaydın adı>" --yes` (tarayıcı açılır, Cancel kapatır, rc=1
"… şu an yüklü olan kayıt; silinmez.") ve `save --as "<aynı ad>" --yes` (tarayıcı açılır, Save basılır, "Replace Existing
File" onaylanır, rc=0 "Kaydedildi: …"). Altı koşu iki dilde **eski derlemeyle birebir** (`md5` stdout: TR delete boş
`d41d8cd9…`, TR save `d4d552c7…`, EN save `46212f7f…`; rc dağılımı aynı); izde eskiden `click ?text=Cancel` /
`?text=Save`, yeni derlemede `click ?ptr=0x…`. Değişmeyen: `SaveAs` aynı adı yazınca oyun "Replace Existing File" diye
soruyor ve onaylanıyor (yeni dosya açılmadı, yüklü kaydın adı değişmedi); negatifler (`saves delete "zzz yok"` rc=1,
`save --as` onaysız rc=1) hiç tarayıcı açmıyor. FM menüsünün "Save Game As" satırı **yazıyla kalıyor**: satırın kimliği
`FMMenuOptionFromID` ve seçeneğin kendi numarası eklentinin `Events` özetinde yok (Tur 45 notu, eklenti değişikliği
ister). Aynı sınıfta kalan: tarayıcının `Delete` (silme Faruk'un kararı, ölçülemedi) ve yükleme ekranının `Load`u.
Regresyon: `tools/sweep.sh` → **50 komut, 0 hata** (Türkçe oyunda; `saves` rc=0, 7 satır). Değişiklik yalnız
`Saves.cs` içinde (yeni yerel yardımcı); paylaşılan koda dokunulmadı. Not (ölçümün kayda etkisi): kayıt yalnız
**okundu** ve aynı adla üzerine yazıldı (yeni dosya yok, `fm saves --json` yine dört kayıt, yüklü kaydın adı
değişmedi; oyunun kendi "last save overwrite backup"ı her üzerine yazmada yenileniyor — normal); 2 Ağustos 2026 14:00
durumu, saat ilerletilmedi, maç oynanmadı; dil EN
ölçümünden sonra `fm settings language "Türkçe"` ile geri alındı (`/state` → `turkish`). Tuzak: TR oyunda ayarın adı
da oyunun sözü — `fm settings dil "English"`, dönüş `fm settings language "Türkçe"`.

### 8.237 Onay pencereleri artık başlık yazısıyla değil oyunun kendi adıyla (Type) bulunuyor (19 Ekim, TR + EN)
`Saves.cs` `DialogButton` bir pencereyi **başlık yazısıyla** arıyordu (`Title` "Replace Existing File"/"Delete File" ile
başlıyor mu). Oysa eklenti (`/dialog`) her pencerenin oyunun kendi adını da veriyor (`Type`, `DialogManager`'dan) ve o ad
dile bağlı değil. Ölçüm (TR oyunda canlı, `dialog?data=1`): `save --as "<var olan ad>"` sonrası
`Type=ConfirmSaveOverrideDialog`, `Title=Replace Existing File?`, düğmeler `Slot=primary EventID=ConfirmSaveGame` /
`Slot=secondary EventID=close`; tarayıcıda başka bir kaydın satırını seçip `DeleteSelectedGame` düğmesine basınca
`Type=confirmDeleteSelectedSave`, düğmeler sırayla "Yes"/"No" (`EventID` boş) — ikisi de TR'de de **İngilizce** başlık
taşıyor, yani yazı yolu bugün bu makinede çalışıyor ama dil/skin değişince kimsenin sözü değil.
Artık `DialogButton(type, title)` önce `Type`e bakıyor (`ConfirmSaveOverrideDialog`, `confirmDeleteSelectedSave`),
bulunamazsa eskisi gibi başlığa düşüyor.
Kanıt: TR + EN, üç durum (başka sayfa açık `fm open "Messages"` sonrası / hemen ikinci koşu / başka sayfaya geçtikten
sonra `fm open "Squad Dynamics"`) × `save --as "<yüklü kaydın adı>" --yes` → altı koşu **eski derleme (git stash +
`dotnet build`) ile birebir** (`md5` TR `d4d552c7844b8005649f054ac42d8413`, EN `46212f7f30265749e8e115da9b1f1683`,
Tur 47'nin kaydıyla da aynı); izde `dialog` → `click ?ptr=0x…` (onay). Silme yolu **sıfırdan ölçüldü**: kayıt
klasörüne yalnız kendi kopyamız kondu (`cp …(v03).fm "t48 test.fm"`), `fm saves delete "t48 test" --yes` → rc=0
"Silindi: t48 test" (iz: `rowselect` → `click ?text=Delete` → `dialog` → `click ?ptr=0x…` = pencerenin "Yes"i);
klasörde Faruk'un dört dosyası değişmeden duruyor. Negatif/değişmeyen: `save --as` onaysız rc=1, `saves delete
"zzz yok" --yes` rc=1 — ikisi de tarayıcıyı hiç açmıyor; pencere hiç çıkmazsa `Type` boş kaldığından eşleşme yok.
Kalan (aynı sınıf, ayrı iş): `MatchDay`in üç penceresi (`Instant Result`, `Team Selection`, `Injured Players`) hâlâ
başlık yazısıyla aranıyor; tarayıcının `Delete` düğmesi hâlâ `click ?text=Delete` (§8.236 sonu).
Regresyon: `tools/sweep.sh` → 50 komut, 0 hata (Türkçe oyunda). Değişiklik yalnız `Saves.cs` içinde (yerel yardımcı);
paylaşılan koda dokunulmadı. Kayıt durumu: kayıtlar yalnız okundu + aynı adla üzerine yazıldı, t48 test kopyası
silindi; 2 Ağustos 2026 09:15, saat ilerletilmedi, maç oynanmadı; dil TR'ye döndü (`/state` → turkish).

### 8.238 `fm saves delete`: kayıt tarayıcısının `Delete`i artık gönderdiği olayla basılıyor (19 Ekim, TR + EN)
`DeleteSave` (Saves.cs 407) düğmeyi İngilizce sözüyle basıyordu (`click ?text=Delete`) ve ondan önce `Label == "Delete"`
+ `Type == "SIButton"` + `Enabled` ile bekliyordu. Oysa tarayıcının dört düğmesi aynı şablon ailesinden ve hepsinin
etiketi o dilin sözü (§8.236) — ayırt eden gönderdikleri olay: **Delete `DeleteSelectedGame`** (satır seçilene kadar
`Enabled=False`, yani "olay + açık" işareti hem bekleme hem seçim işi). Artık `DeleteButton()` bu olayı gönderen açık
düğmeyi bulup `?ptr=` ile basıyor; kimlik yoksa eskisi gibi yazıya düşülüyor.
Kanıt: kayıt klasörüne **yalnız kendi kopyamız** kondu (`cp "…(v03).fm" "t49 s1.fm"` … `s3`/`e1`…`e3`), TR + EN × üç
durum (başka sayfa açık `fm open "Messages"` sonrası / hemen ikinci koşu / başka sayfaya geçtikten sonra) → altı koşu
**eski derlemeyle (git stash + `dotnet build`) birebir** (`md5` TR `efb3dc45…` / `fd3c4108…` / `feb30e74…`, EN
`ff8c9d93…` / `da1d75f0…` / `6d5b52d6…`; stdout yalnız adı taşıyor), izde eski `click ?text=Delete` → yeni
`buttons ?json=1` + `click ?ptr=0x…`; silme uçtan uca (dosya yok oldu, rc=0 "Silindi/Deleted"). Negatif: `saves delete
"zzz yok" --yes` rc=1 (tarayıcı hiç açılmıyor), yüklü kaydı silme rc=1 ("şu an yüklü olan kayıt", tarayıcı Cancel'la
kapanıyor). Faruk'un dört dosyası (ana kayıt, overwrite backup, v02, v03) değişmeden yerinde.
Regresyon: `tools/sweep.sh` → 50 komut, 0 hata (Türkçe oyunda). Değişiklik yalnız `Saves.cs` içinde (yeni yerel
yardımcı + tek çağrı); paylaşılan koda dokunulmadı. Tuzak: ölçümde bir EN koşusu tarayıcıyı açamadı (bilinen
`OpenFmMenu`/`OpenSaveBrowser` aksaklığı, §8.236) — tekrar edildi; ayrıca İngilizce oyunda `fm open "Squad Dynamics"`
"no page named" diyor, doğrusu `fm open "Dynamics"` (sayfa adı o dilin sözü — ayrı iş).
Kayıt durumu: yalnız kendi kopyalarımız silindi, kayıt okunmadı/yazılmadı; 2 Ağustos 2026 09:15, saat ilerletilmedi,
maç oynanmadı; dil `fm settings language "Türkçe"` ile geri alındı (`/state` → turkish).

### 8.239 `fm staff find` arama panelinin `Apply`/`Cancel`i artık gönderdikleri yükle basılıyor (19 Ekim, TR + EN)
`StaffFind`/`ApplySearch` (StaffSearch.cs) panelin iki düğmesini de yazıyla basıyordu: gönder yolu `click ?text=Apply`,
iptal yolu panelin form okumasından (`Text == "Cancel"`). Ölçüm (TR oyun, panel açık, `/buttons?json=1&gen=1`): ikisi
de **aynı olayı** gönderiyor (`PanelHistoryClose, EventToPlugin:EventFmxFilterRestoreFilter`) — ayırt eden gönderilen
**yük**: Apply `SendsProps True`, Cancel `False` (dile ve skine bağlı değil; `Name` de farklı — `button-primary-…` /
`button-base-label` — ama o şablon sınıfı, kural 3 gereği yüke öncelik verildi). Artık `PanelSent(apply)` bu olayı ve
yükü taşıyan düğmeyi `?ptr=` ile basıyor (iki çağrı yeri: `StaffFind`in kendi iptali + `ApplySearch`in `PanelButton`ı);
kimlik yoksa eskisi gibi panelin form okuması + yazı.
Kanıt: TR + EN × üç durum (başka sayfa açık `fm open "Messages"` / hemen ikinci koşu / başka sayfaya geçtikten sonra)
× iki yol (`staff find --role "Coach" --json` = Apply, `staff find --role "zzz yok"` = Cancel) → **eski derlemeyle
(git stash + `dotnet build`) birebir**: stdout `md5` TR `278f3c93afe416f0d6c62353a517cd67`, EN oturmuş
`2ba97887d7c948522efd6e8436749986`; rc 0 / 64, stderr `md5` TR `323377898b24fb0011935d1f72ddaf29` / EN
`52c49a4e6158ce3d2d73b48d1f300ccf`. İzde eski `click ?text=Apply` → yeni `buttons ?json=1&gen=1` + `click ?ptr=0x…`
(altı koşunun altısında da; iptal yolu da form okumasından yüke geçti, `form ?anchor=Apply&…&buttons=1` yalnız yedek).
Yan bulgu (yeni, düzeltilmedi): **dil değişiminden hemen sonraki** ilk koşularda personel tablosu yalnız süzülmüş
satırları veriyor (21 satır), sayfa oturduktan sonra 69 (48 satırın `JobString`i boş) — **eski derlemede de aynı**
(§8.194/§8.232'nin devamı); panelin *okuma* yolu (`Under`/`Row`) hâlâ `form ?anchor=Apply` ile kelimeyle çapalı.
Kayıt durumu: kayıt yalnız **okundu**, hiçbir şey yazılmadı; sayfanın arama süzgeci (tur başında 1 kural duruyordu)
`--clear` ile boşaltıldı, sayfa artık süzgeçsiz; 2 Ağustos 2026 14:00, saat ilerletilmedi, maç oynanmadı; dil
`fm settings language "Türkçe"` ile geri alındı (`/state` → turkish). Regresyon: `tools/sweep.sh` → 50 komut, 0 hata;
aynı `ApplySearch`i kullanan `fm players "Aaron" --attr "Pace>=15"` (Reads.cs 802) iki derlemede **birebir**
(`md5 d8cd16daa30d08417f6a34d9007d3e6e`, rc=0).

### 8.240 `fm players` arama paneli artık yazıyla değil oyunun bağladığı yollarla okunuyor (19 Ekim, TR + EN)
Panel `form ?anchor=Apply&up=8` ile, yani **düğmenin İngilizce sözüyle** bulunuyordu; `/form?anchorpath=` ile
oyunun bağladığı başlık (`filter.FilterRulePlayerPosition.Name`) **birebir aynı 55 öğeyi** veriyor (ölçüldü), yani
kelimeye gerek yoktu. Kurallar da kendi denetimlerinin yazdığı yoldan bulunuyor (`filter.filterruleage.minvalue`,
`Filter.FilterRuleScoutingGrade.MinValue`, `filter.filterrulecontract.filtervalue`,
`filter.filterruletransferstatus.filtervalue`, `filter.filterruleloanstatus.filtervalue`, `filter.FilterRuleIpRole.filtervalue`)
ve satırın iki denetimi `rule.selectedcompareflag` / `rule.minvalue`: "Is At Least", "20" gibi İngilizce seçenek sözleri
kalktı. Bunun **gerçek** sonucu: TR oyunda başlık "Aged Between (Yıl Olarak)" geldiği için `fm players --age 18-23`
üç durumda da rc=1 `error: arama panelinde 'Aged Between (Years)' yok` veriyordu (eski derleme), şimdi rc=0.
Aynı kayıtta ölçüldü: `--attr`/`--grade`/`--role`/`--max-value` iki derlemede birebir (md5'ler §8.239'un arkasında,
quest defteri Tur 51). Dil geçişinden hemen sonraki koşularda tablo yine kısa geliyor (22/25 satır; eski derlemede
de aynı — §8.239 yan bulgusu), ölçüm oturmuş durumlar karşılaştırıldı. Panelde kalan: `PanelButton`in yedeği hâlâ
yazıyla (asıl yol `PanelSent`, §8.239) ve `Row()`in satır adı, niteliğin oyunun menüsünden gelen kendi sözü.

### 8.241 Recruitment focus formunun adım düğmeleri (Next/Back) artık gönderdikleri yükle bulunuyor (19 Ekim, TR + EN)
`ScoutFocus` formunun "açıldı / yarım kalmış" işareti ve `fm scout areas`in bölge adımına geçişi düğmenin yazısına
bağlıydı (`FormButton(FocusForm("Next"), "Next")`, `click ?text=Next`). Ölçüm (`/buttons?json=1&gen=1`, form açık):
adım düğmeleri **aynı olayı** gönderiyor (`EventToPlugin:EventFmxRecruitmentFocusMoveToStep`, enum 1227976013);
ayırt eden **yük** — ileri `SendsProps One`, geri `MinusOne` — ve göstermeyen adımın kendi düğmesi `Enabled=False`.
Artık `FocusSteps()` olayı + yükü taşıyan açık düğmeyi arıyor (kimlik yoksa yazıya düşülüyor): TR + EN dört durum
(başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra / form ikinci adımda bırakılmış) `fm scout
areas --json` eski derlemeyle birebir (`md5 fed56895c45431dd9820725651421c2b`, rc=0); odağı kurmayan `scout new`
yolunda rc=64 ve odak sayısı değişmiyor. Aynı formda **Cancel ve Confirm** hâlâ yazıyla: ikisi de
`RecruitmentFocusEditConfirmationDialog` gönderiyor ve ayırt eden alan (`m_cancelEdit` = True/False) yalnız
`/buttons?text=` bloğunda var, `/buttons?json=1` özetinde **yok** — oraya eklemek eklenti değişikliği + oyun
restartı ister. Confirm'e basmak pencere açmıyor, formu kapatıp odağı **kuruyor** (ölçümün doğal sonucu bir "All"
odağı bıraktı).

### 8.242 `scout new/edit`in adım geçişi ve adım işaretleri de düğmelerin olayına bağlandı (19 Ekim, TR + EN)
§8.241 `scout areas`in yolunu kimliğe çevirmişti; aynı formun `scout new`/`scout edit` yolu hâlâ yazıyla
yürüyordu: "ikinci adım açıldı mı" işareti `FormButton(FocusForm("Back"), "Confirm")`, "birinci adım açık mı /
mevki oturdu mu" işareti `FormButton(FocusForm("Next"), "Next")` + `Enabled`, ileri basışı `click ?text=Next`.
Ölçüm (TR, form açıkken `/form` + `/buttons?json=1&gen=1`): adım düğmeleri **iki dilde de İngilizce** yazıyor
("Next"/"Back"/"Cancel"/"Confirm"), yani yazı yolu bugün tesadüfen çalışıyor — ama okuma çapası da yazı:
`/form?anchor=Next` adım 1'de **52 öğe**, adım 2'de **0**; `/form?anchor=Back` tersi (adım 2'de 47, adım 1'de 0).
Yeni `FormStep()` bunu adım düğmelerinin olayından okuyor (`FocusSteps`: `EventFmxRecruitmentFocusMoveToStep` +
`One`/`MinusOne`, yalnız **açık** olan): "next" / "back" / null (form yok); ileri basışı da `?ptr=`. Kanıt: TR +
EN, üç durum (başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra) × iki yol (`scout edit 1
--area "ZZZ yok"`, `scout new --position GK --priority top --area "ZZZ yok"`) eski derlemeyle **birebir** (rc=64,
stderr md5 TR `3e18cfaa…` / EN `f66c35f8…`); izde eski `click ?text=Next` → yeni `click ?ptr=0x…`. Yan bulgu
(ayrı iş): `scout edit <#>` sayfa **yeni açıldığında** odak listesini ara sıra boş okuyor ("Odak: '1' diye bir
seçenek yok"; eski derlemede 8 koşunun 4'ü, yeni derlemede 4'ün 1'i) — `ScoutFocuses()`/`FocusList()` sayfa
oturmadan dönüyor, bu turun değişikliğiyle ilgisiz.

### 8.243 `scout new/edit`in "form kapandı" işareti de düğmelerin olayına bağlandı (19 Ekim, TR + EN)
§8.242 formun *adım* işaretlerini `FormStep()`e taşımıştı; Confirm akışı hâlâ yazıyla yürüyordu: `ScoutNew`in
`catch` dalındaki "form açık mı" (`FormButton(FocusForm("Next"), "Next") != null || FormButton(FocusForm("Back"),
"Confirm") != null`) ve Confirm sonrası "form kapandı mı" (`FormButton(await FocusForm("Back"), "Confirm") == null`)
— yani **hem** `form ?anchor=Next`/`Back` yazı çapası **hem** "Next"/"Confirm" etiketi (ikisi de oyunun sözü; bugün
iki dilde de İngilizce geldiği için tesadüfen çalışıyordu). Üçü de artık `FormStep() != null` (açık) / `== null`
(kapandı): işaret, adım düğmelerinin gönderdiği olay + yük (`EventFmxRecruitmentFocusMoveToStep`, `One`/`MinusOne`,
yalnız açık düğme). Ölçüm (canlı, `python3` + eklenti): Confirm'e basmadan önce düğmeler `[Back, Confirm]`,
`/form?anchor=Back` **47** öğe, odak listesi **5**; tık 0,04 sn'de döndü ve **0,271 sn**'deki ilk yoklamada üçü de
dönmüş (düğmeler boş, form **0** öğe, liste **6**) → eski ve yeni işaret aynı anda dönüyor. Kanıt: `fm scout edit
"<odak>" --name "<yeni>"` (Confirm'e basan yol) TR üç durum × 2 + EN üç durum = 9/9 rc=0, çıktı aynı; izde
Confirm'den sonraki iki yoklama eski derlemede `form ?anchor=Back&up=12&buttons=1`, yenide `buttons ?json=1&gen=1`;
hata yolu (`scout edit 1 --area "ZZZ yok"`, değişen satırlara girmiyor) TR + EN üç durumda rc=64, stdout
`d41d8cd9…`, stderr md5 TR `3e18cfaa…` / EN `f66c35f8…` = §8.242 ile birebir. Kalan yazı çapaları: Confirm
basışı (`click ?text=Confirm`) ve `AbandonFocus`in `click ?text=Cancel`i — ikisi de `m_cancelEdit` taşır ve bu alan
`/buttons?json=1` özetinde yok (eklenti + oyun restartı ister); ayrıca okuma çapası `form ?anchor=Next`/`Back`.
Yan not: bu ölçüm gerçekten odak kurdu; kayıtta 12 `t54 …` artığı kaldı (Complete odakları CLI silemiyor).

### 8.244 Gözlemci seçme penceresinin Cancel/Confirm'i artık gönderdiği olayla bulunuyor (19 Ekim, TR + EN)
`AssignFocusScouts`in penceresindeki iki düğme (`Close`) pencere formunda **yazıyla** aranıyordu
(`FormButton(await Window(), "Cancel"/"Confirm")`); aynı fonksiyonun `Show`u satır düğmesini de onlar
"Cancel"/"Confirm" yazmadığından seçiyordu. Ölçüm (TR oyun, pencere açık, `/buttons?json=1&gen=1`): pencerenin
kendi iki düğmesi `EventToPlugin:EventFmxRecruitmentFocusScoutAnalystEditCancel, ClosePanel` ve
`…EditConfirm, ClosePanel` gönderiyor (`Enabled=True`; şablon adları `button-secondary-label` /
`button-primary-label`, kural 3 gereği olaya/yüke öncelik), **formun** kendi Cancel/Confirm'i başka bir olay
taşıyor (`RecruitmentFocusEditConfirmationDialog`) — karışmıyorlar. Aynı iki olay adı **İngilizce oyunda
birebir** (canlı ölçüm: pencere 13 yoklamada aynı iki satırı verdi). Artık yerel `EventButton(sent)` bu olayı
taşıyan açık düğmeyi bulup `?ptr=` ile basıyor (kimlik yoksa eskisi gibi yazıya düşülüyor) ve `Show` satır
düğmesini aynı iki düğmenin **işaretçisinden** dışlıyor. Pencerenin okuma çapası (`form ?anchor=Assign Any
Scouts&up=8`, yazı) kalıyor: `anchorpath=focusitem.anyscout&up=8&buttons=1` ölçüldü, **0 öğe** döndürüyor
(yol çapası bu denetimde tutmuyor). `Rows()`in başlık sözü ("World Knowledge") da kalıyor — ayrı iş.
Kanıt (`scout edit 1 --by "ZZZ yok"` = iptal, `--by "Kenny Morrison"` = onay; üç durum: `fm open "Messages"`
sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` [`Dynamics`] sonrası): İngilizce'de **eski derlemeyle
birebir** (aynı başlangıç durumu `86fe1bcc…`; iptal üç durumda rc=64, stdout boş `d41d8cd9…`; onay rc=0 ve
`190cf72a…` / `00e361e1…` / `82814c43…` — eski derlemenin aynı koşuları `190cf72a…` / `00e361e1…` /
`82814c43…`; yalnız yeni derlemenin s3-onay koşusu köprü sustuğu için (rc=1) düştü, §8.99). Türkçe'de iptal
yolu üç durumda rc=64 + boş stdout (`d41d8cd9…`, iki derlemede de), onay yolu rc=0 ve yeni derlemede üç
durumda da `537180b3…` (eski derlemede de rc=0; koşular arasında hedef odak kaydığı için md5'ler orada
karşılaştırılamıyor — birebir karşılaştırma EN'de yapıldı). İz: eski `form ?anchor=Assign Any Scouts&up=8&
buttons=1` (yazı) → yeni `buttons ?json=1&gen=1` (olay); basış iki derlemede de `click ?ptr=`. Yan not:
`scout edit <#>` her onayda oyunun eski kaydı listede 'Complete' bırakması yüzünden liste kayıyor, o yüzden
aynı numara başka bir odağı gösterebiliyor (ölçümün doğal sonucu: birkaç odak kaydı daha kuruldu).

### 8.245 Home-grown (altyapı) kartı: satırlar artık yol + oyunun işaretiyle okunuyor, başlık/yazı araması kalktı (19 Ekim, TR + EN)
"Yetiştirilmiş oyuncular güncellemesi" mesajının eki (`reply`) Türkçede ve İngilizcede ham bir dökümdü: özet dalı
`rest[0] == "Homegrown" && rest.Contains("Home-Grown Status")` ile kendini tanıyordu ama kartın ilk sözü oyunun kendi
cümlesi ("Upcoming Status Changes") ve sütun başlığı da "Homegrown Status" (tire yok) — yani dal **her iki dilde de**
hiç çalışmıyordu; ayrıca satırlar `rest[i+1].EndsWith("years old")` ile bulunuyordu (TR'de "19 yaşında" → hiç eşleşmez).
Artık kart oyunun kendi bölüm başlığıyla (`Upcoming Status Changes`) tanınıyor ve satırlar oyunun bağladığı veri
yolundan okunuyor (`binding.Name` ile başlar; ardından yaş, ülke, kadro, statü, tarih gelir — satırın sonu 3. `binding`
hücresinde kesilir, çünkü kartın yanındaki panel de yazı taşır). Bu, hem başlığı hem yaş cümlesini dilden bağımsız kılar.
Sütunlar sırayla bağlandığı için okuma oyunun kendi işaretiyle (satırların beşi de dolana kadar) yineleniyor; sabit süre yok.
Ölçüm (canlı, TR + EN, `fm open "Squad Dynamics"` sonrası `fm reply "<konu>"`): **yeni derleme** TR'de 4 koşunun 3'ünde
birebir `md5 aa45275675be3b9694c33e268e53631a` (12/12 satır: `Zane Okoro (19, USA, As Takım) — 30/6/2027 (kulüp 15-21)` …),
EN'de 3 koşunun 2'sinde birebir `md5 424f3646599cf300e81ad7fedabdce5f` (12/12 satır); **eski derleme** (git stash +
`dotnet build`) aynı durumda hiç satır vermiyor, başlığı ve yan paneli tek satıra yayıyordu
(`md5 be2c6235235c02eb0c367f67a27ac1cf` / `00d691986fbc38e117a0ae9ff4ad7d39`). Kalan oynaklık paylaşılan okumadan
geliyor: `WriteAttachment`ın 1,5 sn'si bazen eki hiç yakalamıyor (mesaj bulunamadı / ek gelmedi) — §8.192'nin park
ettiği iş; bu değişiklik onu iyileştirmiyor, yalnızca yakalanan okumada doğru satırları veriyor. Negatif: komşu mesajlar
(`Kondisyon testleri`, veri merkezi) eski derlemeyle birebir; `sweep.sh` 50/50.

### 8.246 Alt sayfa açmada sabit 1,5 sn yerine oyunun kendi işaretleri (19 Ekim, TR + EN)
`OpenSubpage` (Program.cs) ana sayfayı açtıktan sonra **sabit 1,5 sn** bekliyordu; sebebi, ana sayfanın biraz sonra
açılıp çubuğundan açılan sayfanın üstüne binmesiydi (Training › Mentoring, 14 Ekim). Ölçüm (canlı, eklenti, 4 durum):
ana sayfa istendikten sonra çubuk **0,16–0,46 sn**'de cevap veriyor (`open?guid=<sayfa>&check=1` artık hata değil
"not shown" diyor), ondan **önce** basılan satır kayboluyor (cevap: "no page with guid … (a subtab's page: open the
page whose bar lists it first)") ve o basış bir daha kendiliğinden gelmiyor. Yeni: çubuk cevap verene kadar bekleniyor
(0,25 sn aralık, üst sınır 6 sn), sonra satır seçiliyor ve sayfa ancak **çubuk o satırda dururken** (`check=1`:
"shown") **ve** içerik görünürken, üst üste iki okumada alınıyor; olmazsa dört kez yineleniyor (kural 2, 3).
`fm coaches` TR + EN, üç durum (başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra): çıktı eski
derlemeyle **birebir** (`md5` TR `241ea50f…`, EN `f7af385e…`), süre TR 3,83/2,3/2,3 → **2,56/1,28/1,82 sn**;
`fm intensity --json` TR + EN üç durumda 25 satır ve `md5` birebir (`1a826717…` / `013460b4…`); `sweep.sh` 50/50.
Aynı madde içinde kalan: `fm open X` (OpenPage) hâlâ açılış bitmeden dönüyor; çağıran, sayfanın hazır olduğunu
kendi işaretiyle beklemeli (Mesajlar › Mutluluk'ta 1/10 düşme, §8.185) — bu ayrı iş.

### 8.247 Gözlemci seçme penceresinin okuması: sabit 400 ms yerine satırların oturması, başlık sözü yerine satırın kendi sayısı (19 Ekim, TR + EN)
`cli/fm/ScoutFocus.cs` (`AssignFocusScouts`). İki bağımlılık vardı: (1) `Show()` her kaydırmadan sonra sabit
`Task.Delay(400)` bekliyordu; (2) `Rows()` satır bölgesini oyunun başlık sözüyle buluyordu
(`SkipWhile(t => t != "World Knowledge").Skip(1)`).
- Ölçüm (canlı, pencere açık): pencere (`/form?anchor=Assign Any Scouts&up=8&buttons=1`) 58 öğe; hücrelerin hiçbiri
  veri yolu taşımıyor — yalnız "Assign Any Scouts" onay kutusu `focusitem.anyscout`a bağlı. Başlık hücresi
  `/elements` ile: `Button` › `TemplateContainer #TIFO-TableHeaderTemplate` › `#data_display-table-heading-default-default`;
  gövde satırları `StreamedTable #scouts-table` › `DropTarget #View` altındaki `TableRowNavigatable`lar (satır başına
  bağ yok). Yani satırlar için CLI düzeyinde hazır bir veri yolu yok.
- Satırın kendisi zaten işaretli: 3. hücresi "Active Focuses" **sayısı** (tamsayı); başlık bloğunda o hücre sayı değil
  (Name/Role/Active Focuses/Jud A/Jud P/Ada/World Knowledge → tarama hiç eşleşmiyor), bu yüzden `SkipWhile` kaldırıldı
  ve `Rows()` doğrudan `texts[i+2]` tamsayı olan `i`leri satır adı sayıyor (satır işareti zaten buydu). "World Knowledge"
  bugün eklentinin tablo sözü çevirisinden geldiği için her dilde İngilizce; ama artık hiçbir söze bakılmıyor.
- Kaydırma: `/scroll` satırların kabına gidiyor; üç kaydırma konumunda da satır kümesi ilk okumada aynı
  (78 / 73 / 87 ms) → 400 ms kör duraklamaydı. Yerine oyunun kendi işareti: satırlar **art arda iki okumada aynı**
  (boş değil), üst sınır 2 sn (`Until`).
- Kanıt (üç durum: `fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` [EN `Dynamics`]
  sonrası; iki dil; `fm scout edit 1 --by "ZZZ yok"` = iptal yolu okunan gözlemcileri listeliyor): rc hep 64, stdout
  boş (`d41d8cd98f00b204e9800998ecf8427e`), stderr **eski derlemeyle birebir** — TR `fc708400b832c3d94035af82890c387f`,
  EN `b11015ec53078666c1abe1499cce0271`; liste 6 isim (Joe Hutchinson · Kenny Morrison · James Sanderson ·
  Finlay McKenna · George Herbert · Simon Rowley). `fm scout --json` her koşunun öncesi/sonrası aynı (TR
  `a62e4f7fd029311158d73cc2bae94f62`, EN `86fe1bcc76180061e6b35cee1def2ac4`) → hiçbir odak kurulmadı/değişmedi.
- Eski derleme `git stash push -- cli/fm/ScoutFocus.cs` + `dotnet build -c Release`; klasörler
  `/private/var/…/opencode/t58/{new-tr,new-tr2,old-tr,new-en,new-en2,old-en2}`. Kayıt yalnız okundu; saat 2 Ağustos
  2026 **09:15**, ilerletilmedi, maç oynanmadı; dil TR'ye döndü (`fm settings dil` → `Türkçe`).
- Not (tuzak): tur ortasında FM menüsü açılmaz oldu (`texts?anchor=Manage settings for this game` boş; dil ayarı/
  `fm world` de düştü) — `pkill -9` + `fm start </dev/null` + `fm continue` sonrası kayıt aynı yerden (09:15) yüklendi,
  aynı çıktılar alındı (§8.236/§8.99).
- Dokunulmayanlar (aynı pencerede): satırların seçilmesi (`rowselect ?text=<ad>`, ad oyunun sözü), pencereyi açan
  `Edit Recruitment Focus` ve formu açan `Create a New Recruitment Focus` basışları (kimlik yok, §8.234).

### 8.248 Personel arama panelinin kuralları artık veri yolundan bulunuyor; seçimler sabit 500 ms yerine oyunun kendi hâliyle (`--free` TR'de çöküyordu) (19 Ekim, TR + EN)

`cli/fm/StaffSearch.cs` (`StaffFind`). Panelin üç kuralı (`--role`, `--free`/`--employed`, `--reputation`) o güne kadar
**seçenek sözleri** ile bulunuyordu — `Has(d, "Medical ›")` (görev), `Has(d, "By Nation")` (çalışma durumu),
`Has(d, "Exceptional")` (itibar) — ve her seçimden sonra sabit `Task.Delay(500)` bekleniyordu.

- Ölçüm (canlı, sayfa açık, TR + EN): paneldeki her kural oyunun **kendi alanına bağlı** ve yol dilden bağımsız:
  `filter.filterrulestaffrole.filtervalue`, `filter.filterruleemployed.filtervalue`,
  `Filter.FilterRuleWorldReputation.FilterValue`. Liste, o alanın seçeneklerini ve **o an durduğu** seçeneği kendi
  `Label`ında taşıyor (TR: Any→"All", By Club→"At the club", By Nation→"Milli Takım'da"; EN: "Any"/"By Club"/"By Nation").
- Bug (ölçüldü): Türkçe oyunda çalışma durumu listesinin seçenekleri eklentinin çeviri tablosundan geçtiği için
  "By Nation" diye bir söz kalmıyor → `lists.FirstOrDefault(...)` eşleşmiyor, ardından `list.GetProperty("Choices")`
  **yakalanmamış** `InvalidOperationException` veriyor: `fm staff find --free --json` → **rc=134**, stdout boş
  (`StaffSearch.cs:98`). `--role "Coach" --free --reputation "Nominal"` de aynı satırda düşüyordu. Aynı yollar
  İngilizce oyunda çalışıyordu (EN'de söz "By Nation" olarak geliyor) — kırılma tamamen dile bağlıydı.
- Bu yüzden üç kural artık **bağlı olduğu yoldan** bulunuyor, `--employed` dalına listenin yokluğu denetimi eklendi
  (çökme yerine dürüst hata) ve seçim sonrası beklenen şey oyunun kendi cevabı: **kuralın listesi seçilen sözde
  duruyor** (`Until`, üst sınır 6 sn; olmazsa "… listesine oturmadı" deyip panel kapanıyor).
- Sabit beklemeler gerçekten gereksizdi (ölçüm): `select ?ptr=…&fire=1` dönerken liste **zaten** seçilen sözü
  gösteriyor (rol "All" 186 ms'de, "Coach" 284 ms'de; yazının kendisi 125/154 ms) ve art arda **0 ms arayla**
  yapılan iki seçim de oturuyor (4 tur: 178/348/280/693 ms), ikinci listenin işaretçisi de değişmiyor.
- Kanıt (üç durum: `fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` [EN `Dynamics`]
  sonrası; iki dil; eski derleme `git stash push -- cli/fm/StaffSearch.cs` + `dotnet build -c Release`):
  **EN'de her yol eski derlemeyle birebir** — `--role "Coach"` yeni 3/3 `44f56f55d7197eeafb0ed19e1caa2098` = eski s3
  (eski s1/s2 `5b380c79ecdd5f8c20579f44aadde061`, tablonun bilinen geçici hâli §8.239/§8.194), `--free`
  `ebb9ec51337cd439270314e9307432d2` (3 durumda da), `--reputation "Nominal"` `816e0cb7a5742c6eb0ba468f58be665e`,
  **üç kural birlikte** `dc9c9659d32e80f1cd8852dfa4b51aa6` (eski derlemede de rc=0: sabit beklemelerin kalktığı yer
  birebir aynı sonucu veriyor). **TR'de** `--role` yeni 3/3 `278f3c93afe416f0d6c62353a517cd67` = eski **s3** (eski
  s1/s2 yine geçici 21 satır: `7f4956affc9d5a0989739834069849c8`), `--reputation` `8830aae3909ba502b537e3bcfd91b98c`
  iki derlemede de; `--free` ve üç kural birlikte **eskiden rc=134, şimdi rc=0** (70 satır) ve `--free` üç durumda da
  aynı; TR'nin 70 kişisi EN'nin 70 kişisiyle **aynı küme**, üç kural birlikte ise aynı küme **aynı sırada**.
  Sayfanın kendi sayacı seçimin uygulandığını söylüyor: `Aramayı Düzenle (1)` / `Edit Search (1)`.
  Negatifler: `--role "ZZZ yok"` ve `--reputation "ZZZ yok"` iki dilde rc=64; `--views`, `--filters`, `--clear`,
  `--view`, ad süzmesi ve `fm players "Aaron" --attr "Pace>=15"` (aynı dosyadaki dokunulmayan `ApplySearch`) çalışıyor.
- Bir kerelik oynaklık (bu turun işiyle ilgisiz): yeni derlemenin bir TR s3 koşusu `6f1bef2e…` verdi — 69 satırın
  hepsi aynı, yalnız **Nick Adkins**'in `StatusTitleShort`/`StatusType` alanları henüz bağlanmamıştı; sonraki 3 koşu
  `278f3c93…`. Kalan (aynı ailede): `ApplySearch` 438 (`fm players --max-value`'dan sonra sabit 1200 ms) ve
  `PanelButton("Apply"/"Cancel")`in yazı yedeği.

### 8.249 `fm players --max-value`: figür seçildikten sonraki sabit 1200 ms yerine listenin tuttuğu ham sayı (19 Ekim, TR + EN)
`ApplySearch` (StaffSearch.cs 407) `--max-value` figürünü listeden seçtikten sonra `await Task.Delay(1200)` yapıyordu.
Ölçüm (canlı, panel açık): figürü taşıyan liste kendi `Value` alanını (`/dropdowns?json=1`, ham sayı dizesi: `"500000"`)
seçimden sonra **bir yoklamada** gösteriyor (`select` dönüşü 37–132 ms; `Value` hedefe ulaşma 61–131 ms) — yani 1200 ms
tamamen kör bir duraklamaydı. İz (`FM_TRACE=1`): eski derlemede `select ?ptr=…&choice=500B £` 7,42 sn'de bitiyor,
sıradaki çağrı 8,63 sn → **1,21 sn boşluk**; yeni derlemede `select` 7,36 sn, `dropdowns ?json=1` hemen ardından,
`buttons ?json=1&gen=1` 7,61 sn.
- Artık bekleme oyunun kendi işareti: liste seçilen figürü **tutuyor** (`Value == maxValue`), üst sınır 4 sn / 0,25 sn
  aralık; oturmazsa `error: <figür> değeri panele oturmadı` + panel kapanır. Seçim komutunun kendisi "no events to
  send" diyor (bu liste olaysız), eskisi gibi yok sayılıyor. Liste, başka bir dile/para biçimine bağlı olmayan bir
  kuralla bulunuyor: seçenekleri arasında 7.500.000 ham değeri olan liste.
- Kanıt (üç durum: `fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` [EN `Dynamics`]
  sonrası; iki dil; eski derleme `git stash push -- cli/fm/StaffSearch.cs` + `dotnet build -c Release`):
  `fm players --max-value 500K --json` **oturmuş durumda TR'de eski ve yeni derleme üç durumda da birebir**
  (`md5 b8ffd0a52a89f7e3f706c9a98ec78d97`, 70 satır; EN oturmuş `83aed8ac1c686dbdf185050f5780638f`).
  Dil geçişinden hemen sonraki ilk koşularda tablo geçici olarak kısa geliyor (22 satır) — aynı kısalık **eski
  derlemede de** var (§8.239/§8.194); bu yüzden karşılaştırma hem oturmuş md5'lerle hem de **veri** karşılaştırmasıyla
  yapıldı: satır kümesi ve bütün alanlar iki dilde, altı durumda **özdeş** (ham metin md5'i yalnız bağlanma sırası
  değiştiği için oynuyor; aynı isimli satırların JSON'u birebir). `--max-value 20K` yeni derlemede 22 satır ve hepsi
  20B £ sınırının altında → değer Apply'dan önce gerçekten panele oturuyor; `--max-value "abc"` rc=64 ("para olmalı").
- Not: değişiklik `StaffSearch.cs` içindeki `ApplySearch`in yalnız `--max-value` dalında; `ApplySearch`i yalnız
  `fm players` çağırıyor (`Reads.cs:804`), paylaşılan koda dokunulmadı. `sweep.sh` 50 komut, 0 hata. Regresyon:
  `fm players "Aaron" --attr "Pace>=15"`, `--max-value 500K --role "Goalkeeper"`, `--age 18-23` rc=0.
  Bir kerelik aksaklık (bu turun işiyle ilgisiz): `fm players --max-value 300K` bir kez `error: no control labelled
  'Clear' on the loaded screens` ile rc=1 verdi (Clear basışı `ApplySearch`ten **önce**, §8.236 ailesi); tekrarında
  rc=0. Aynı panelde kalan: `PanelButton("Apply"/"Cancel")`in yazı yedeği (asıl yol `PanelSent`, §8.239) — ayrı alt madde.

### 8.250 `contract <oyuncu> --agent` "seçenek yok": dil değil, açık kalan konuşma/kart durumu (19 Ekim, TR + EN)

- Ölçüm (komutlar ve rc; çıktı dosyaları `/tmp/t61-*.txt`): TR temiz sayfada `fm contract "Adam Reach" --agent` /
  `"Dom Jefferies"` / `"Erik Ring"` / `"Freddie Draper"` → **rc=0**, menajerin konuşması ve dört cevap basılıyor.
  Aynı oyuncuya hemen ikinci kez (`"Conor McGrandles"` ilk koşu rc=0 → ikinci koşu **rc=1** "menajerle görüşme
  seçeneği yok") ve açık yenileme kartı varken (`fm contract "George Wickens"` rc=0 → `--agent` **rc=1**) aynı hata.
- EN aynı: `"Jack Moylan"` **ilk koşu rc=0**; hemen ikinci ve `fm open "Dynamics"` sonrası üçüncü **rc=1**
  ("There is no option to talk …"); `"George Wickens"` kartı açıkken rc=1, `contract --cancel` sonrası da rc=1.
  Yani eski notun "EN'de hep bulunuyor" gözlemi tek koşuya dayanıyor; iki dilde davranış aynı.
- Kart açıkken `/buttons?json=1` oyuncu sayfasının üç `SpeakToAction` satırını `Action=""` (bağlanmamış) veriyor;
  `SpeakToAction` bu satırları eliyor. `fm contract "George Wickens" --cancel` kartı kapatıp rc=0 diyor ama
  `--agent` yine bulamıyor → oyun aynı oyuncuyla yakın zamanda konuşmayı da kapatıyor olabilir; ikisi ayrıştırılamadı.
- Yan bulgu: `fm open "Messages"` sonrası bir EN koşusu ("George Wickens") "not our player, or their contract cannot
  be opened" verdi — oyuncu sayfası örtülüyken `ContractDetails` düğmesi düğme listesinde yok; `OpenPlayer`
  önbellekteki kartı isim eşleşmesiyle kabul ediyor. Aynı durum TR'de ("Adam Reach") "seçenek yok" verdi, yani
  hangi dala düştüğü sayfanın o anki hâline bağlı.
- Karar: kod değişmedi (kural 4). Düzeltme iki davranış kararı istiyor: (a) CLI açık konuşmayı kendiliğinden kapatsın mı
  (menajer/oyuncu bundan hoşlanmayabilir), (b) `OpenPlayer` örtülü sayfayı öne getirsin mi? Faruk'a soruldu;
  kayıt test kaydı, saat 2 Ağustos 2026 14:00'te kaldı, dil TR'ye döndü.

### 8.251 `tactic clear --yes`: silme onayı sabit 3 sn yerine "pencere **veya** yuva sayısı düştü" (19 Ekim, TR + EN)
Seçimden sonra oyun silme onayını soruyor; eski kod 3 sn boyunca yalnız `dialog` yokluyor ve süre dolunca "soru yok"
sayıyordu. Ölçüm (TR, üç durum): pencere basıştan sonra **0,74 / 1,14 / 2,39 sn**de geldi — bir koşuda 3 sn'lik sınıra
0,6 sn kalıyordu; yavaş makinede pencere geç gelirse hiç cevaplanmıyor ve komut "yuva boşalmadı" ile düşecekti. Artık
bekleme oyunun kendi cevabı: `dialog` doldu **veya** yuvanın `TacticCount`'u düştü (`Until`, 8 sn üst sınır, 0,25 sn);
pencere geldiyse birincil düğmesi basılıyor, sonra sayı düşene kadar yine bekleniyor. Kanıt: TR üç durum (yuva zaten
açık / `Messages` açık / `Messages`+`Squad` sonrası) ve EN üç durum rc=0, çıktı eski derlemeyle **birebir**
(`md5` TR `3851a52a…` ×2, `9b2bb072…`; EN `cc748d6e…` ×2, `b92f07e1…`); negatif `tactic clear 9 --yes` rc=1.
EN'in bir durumunda "Slot 3 is already active" satırı bir kez çıktı, üç tekrarda 2/3 çıkmadı — o satır dokunulmayan
ilk okumadan (sayfa örtülüyken `CurrentTacticSlot`) geliyor, değişikliğe bağlı değil. İz: eski `dialog` yoklamaları
0,5 sn aralıkla, yeni `dialog` + `tactic` 0,25 sn. Kayıt test kaydı; dil TR'ye döndü (`fm settings language "Türkçe"`).

### 8.252 `fm propose`: ücret kutusunun okuması sabit 600 ms yerine kutunun kendi para yazısı (19 Ekim, TR + EN)
`Propose` ücreti `setnumber` ile yazdıktan sonra 600 ms bekleyip `FMCombobox.Label`ı okuyordu. Ölçüm (canlı): ilk okuma
(33 ms) hâlâ "Unspecified"; etiket 188/216 ms'de `£100B`/`£100K` oluyor, sonra değişmiyor. Artık etiket para yazısına
dönüşüp öncekinden farklı olana kadar okunuyor (üst sınır 4 sn, 0,2 sn aralık; ekranda aynı ücret duruyorsa bekleme yok).
TR + EN, üç durum + `0`/`1`/`250000`/`30000000`: çıktı ve rc eski derlemeyle birebir. Kiralık düğmesi (`Propose 77`)
sonrası 1200 ms kaldı: basış dışa vurulan buttons/combobox kümesini 2,6 sn boyunca hiç değiştirmiyor, yani işaret yok.

### 8.253 `staff find`in Apply/Cancel yedeği kaldırıldı: düğme yalnız olayı ve yüküyle bulunuyor (19 Ekim, TR + EN)
`StaffFind` paneli kapatırken ve uygularken önce `PanelSent`i deniyor, olmazsa İngilizce söze düşüyordu (`?text=Apply`,
`form ?anchor=Apply` + `Text == "Cancel"`). Ölçüm: `buttons ?json=1&gen=1` iki düğmeyi de `EventFmxFilterRestoreFilter`
ile veriyor, ayırt eden `SendsProps` (`True` Apply, `False` Cancel) — söz hiç gerekmiyor. Yedek yol silindi; event yoksa
Apply "arama panelinin Apply düğmesi bulunamadı" diyor, Cancel yalnız panel açıksa basıyor. TR + EN, üç durum (başka
sayfa / hemen ikinci koşu / başka sayfaya geçtikten sonra) × iki yol (`--role "Coach"`, `--role "zzz yok"`): rc ve
stdout/stderr md5 eski derlemeyle **birebir** (TR `7f4956af…`/`447a8d36…`, EN `5b380c79…`/`44f56f55…`; iptal TR
`32337789…`, EN `52c49a4e…`). İz: yalnız `buttons ?json=1&gen=1` → `click ?ptr=…`; hiçbir koşuda `text=` yok.

### 8.254 `fm players` arama panelinin Apply/Cancel yedeği kaldırıldı: basış yalnız oyunun yüküyle (20 Ekim, TR + EN)
`ApplySearch`in `PanelButton`ı `PanelSent`i deniyor, olmazsa `form ?anchorpath=…&buttons=1` içindeki `Text ==
"Apply"/"Cancel"`e düşüyordu; panelin iki düğmesi aynı olayı (`EventFmxFilterRestoreFilter`) gönderiyor, ayırt eden
`SendsProps` (`True` Apply, `False` Cancel) — söz hiç gerekmiyor. Yedek silindi: Apply `PanelSent(true)`, Cancel
`PanelSent(false)`, kimlik yoksa Apply "arama panelinde Apply düğmesi bulunamadı" diyor ve panel kapatılmıyor.
TR + EN, üç durum × `players "Aaron" --age 18-23 --json` / `players --age 120-130`: Apply satırları (durum sütunu
dışında) eski derlemeyle birebir, EN s1/s2 bayt bayt aynı; Cancel iki dilde rc=64 + boş stdout + aynı stderr; izde
`click ?ptr=…`, `text=` yok. Kalan tek yazı yolu `Row()`in nitelik adı (oyunun kendi sözü) — ayrı iş.

### 8.255 Recruitment focus formunun ileri adımı ve gözlemci penceresinin kapanışı yazıyla değil kimlikle (20 Ekim, TR + EN)
`ScoutAreas`in ileri basışı ve `ScoutNew`/`ScoutEdit`in ikinci adıma geçişi `FocusSteps` düğmesini bulamazsa
`click ?text=Next`e düşüyordu; `AssignFocusScouts` içindeki `Close` de pencerenin `EventFmxRecruitmentFocusScoutAnalystEditCancel/Confirm`
olayını bulamazsa "Cancel"/"Confirm" sözüne düşüyordu. Üç yedek de silindi: düğme yoksa form kapatılıp dürüst hata
veriliyor, pencere kapanmadıysa söyleniyor. TR + EN × üç durumda `fm scout areas --json` aynı üç seçeneği verdi
(`["Competition ›","Nation ›","Recently Selected ›"]`), izde ileri basışı `click ?ptr=…` (yazı yok); `scout edit 1
--by "ZZZ yok"` iki dilde rc=64 ve `fm scout --json` md5 koşu öncesi/sonrası aynı (TR `a62e4f7f…`, EN `86fe1bcc…`).

### 8.256 Opposition card: the advice/clear path reads the table when it stands still, and presses Clear by its event (20 Ekim, TR + EN)
`fm opposition --advice` / `--clear` basıştan sonra tabloyu bir kez "öncekinden farklı" diye okuyor, sonra **sabit
500 ms** bekleyip bir kez daha okuyordu. Ölçüm (canlı, TR, 40 ms yoklama): öneri basışından 77 ms sonra 11 satır
değerlerle geliyor, **519 ms'de 8 satır**, 612 ms'de yine 11 → duraklama yarım dizilişin içine düşebiliyor (üç oyuncu
kaybolur). Artık tablo **öncekinden farklı ve art arda iki okumada aynı** olana kadar okunuyor (`Until`, 6 sn üst
sınır, 0,25 sn); temizleme yolu da "Clear Instructions" sözü yerine düğmenin gönderdiği
`EventFmxOppositionInstructionsClear` olayıyla basılıyor, düğme yoksa dürüst hata veriyor. İz: eski
`translate ?words=Clear Instructions` → yeni `buttons ?json=1` + `press ?ptr=…`; öneri yolu 1,48 → 1,16 sn, temizleme
1,37 → 1,03 sn. TR + EN × üç durum (başka sayfa açık `Messages` / hemen ikinci koşu / `Squad Dynamics` sonrası)
`--advice` + `--clear` çıktısı eski derlemeyle **birebir** (TR `05bb4f30…` / `63635b68…`, EN `2c162b6c…` /
`659a220b…`, 11 oyuncu, 5 değer); "zaten temiz" hâli rc=0 ve 11 satır `-`. Kayıt test kaydı; rakip talimatları tur
sonunda `--clear` ile boş bırakıldı, saat ilerletilmedi, maç oynanmadı.

### 8.257 Arama düğmesi etiketiyle değil, açtığı panel ve şablon adıyla basılıyor (20 Ekim, TR + EN)
`SearchFor` (Reads.cs) arama kutusu kapalıyken düğmeyi `?text=Search&name=dashboard-hud-search-button` ile
basıyordu; "Search" oyunun kendi sözü. Artık `?event=OpenPanel&name=dashboard-hud-search-button` — olay + şablon
adı, dil/skin yazısı yok. Ölçüm (`FM_TRACE=1`): TR + EN × üç durum (başka sayfa açık / hemen ikinci koşu / başka
sayfaya geçtikten sonra) `fm search "Ben House" --json` rc=0, dört sonuç (Moisés Del Hoyo, Moise Kean, Louie Rea,
Ben House) eski derlemeyle **birebir** (isim/kulüp/tür; ham md5 yalnız arama sayfasının GUID'inde oynuyor, iki
derlemede de). İz: eski `press ?text=Search&name=…` → yeni `press ?event=OpenPanel&name=…`; kutu zaten açıkken
hiç basış yok. `sweep.sh` 50 komut, 0 hata.

### 8.258 `fm players` hızlı süzgeci "Clear" sözüyle değil, düğmenin olayıyla temizleniyor (20 Ekim, TR + EN)
`Players` (Reads.cs) iki yerde — `--clear` ve her aramanın başındaki temizleme — düğmeyi
`?text=Clear&event=EventToPlugin` ile basıyordu; "Clear" oyunun kendi sözü. Sayfadaki tek Clear düğmesi
`EventToPlugin:EventFmxResetFilter` (id 515611) gönderiyor ve şablon adı `button-secondary-label`. Artık basış yalnız
bu olayla: `press ?event=EventToPlugin:EventFmxResetFilter`; düğme yoksa `Arriving` dürüst hata veriyor. Temizlemenin
oturduğu işaret de yazıyla değil: eski `Label == "Edit Search"` TR'de hiç tutmuyordu (düğme "Aramayı Düzenle (1)"
diyor), yani her aramada 4 saniyelik `Until` boşa doluyordu; artık `EventFmxFilterStoreFilter` gönderen düğme
aranıyor. TR'de üç koşu `players --age 18-23 --json`: **12,67 / 12,50 / 12,58 → 9,27 / 8,34 / 9,07 sn**, çıktı
birebir (isim/sıra/alanlar). Ölçüm
(`FM_TRACE=1`, TR + EN × üç durum: başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra × iki yol
`players --clear` ve `players "Aaron" --age 18-23`): TR çıktıları eski derlemeyle **birebir** (22/22, 70/70 satır;
`md5` s1/s2 apply `fce5bc8c…`, clear `79aceda6…`, s3 apply `22ba2e6c…`, clear `5c775573…`; s1/s3 apply'ın
yeni `md5`'i yalnız oyunun oynak `StatusTitleShort`/`StatusType` sütunu yüzünden değişiyor — o sütun iki derlemede
de koşular arasında oynuyor). EN'de oturmuş durum bayt bayt aynı
(`491c409d…` / `40784872…`, 70 satır); dil geçişinden sonraki kısa tabloda yeni derleme daha çok satır okudu, ilk 22
satır isim/alan olarak eskiyle aynı. Negatif: `players --age 120-130` iki dilde rc=64 ve aynı seçenek listesi;
Player Database'in arkasında kalan Clear düğmesi başka sayfadayken `DISABLED` görünüyor, olay yine tek. Kayıt test
kaydı; saat ilerletilmedi, maç oynanmadı, dil TR'ye döndü.

### 8.259 Haber gövdesi ekrandan yazı aramasıyla değil, bağlanan metinle okunuyor (20 Ekim, TR + EN)
`fm news <#>` açık haberin tam metnini `elements ?text=<gövdeden seçilen ASCII parça>&up=0&depth=0` ile arıyor, sonra
`BodyText "` işaretinden kesiyordu: arama sözcüğü gövdenin kendisinden türetiliyor ve yalnız ASCII harflerle
(`[\x20-\x7E]+`) seçiliyordu — yani gövde hangi dilde yazılmışsa ona göre değişen bir ekran araması. Oysa ağaç haberi
zaten `NewsBodyString` altında **paragraf paragraf** bağlıyor (açık haberde başlıktan 7 satır sonra, 4 paragraf tek
blok; listedeki karoda çoğu zaman yalnız açılış cümlesi). Artık gövde bu bağdan, satır devamlarıyla birlikte okunuyor;
`NewsBody` ve `elements ?text=` yolu silindi. Kanıt: `news 1/2/9` × üç durum (başka sayfa açık / hemen ikinci koşu /
başka sayfaya geçtikten sonra) × TR + EN, eski derlemeyle **birebir** (TR `77730c9b…`/`4959a60b…`/`5e7cf08e…`,
EN `0b4e206c…`/`215cf311…`/`a2bd57ff…`, 9/9 aynı md5); iz: eski `elements ?text=has promised to put an end&up=0&depth=0`
→ yeni hiç `elements` çağrısı yok. Negatif `news 99` iki dilde rc=1. Yan bulgu: yaş çözümleyicisi İngilizce birim
arıyor (`(\d+) (day|hour|minute)`), TR'de her yaş 0'a düşüyor → liste sıralaması ve `news <#>` numaraları dile göre
değişiyor (TR'de 2 numara 2 günlük, EN'de başka haber); ayrı iş olarak CHECKLIST B'ye yazıldı.

### 8.260 Haber listesi yaş cümlesiyle sıralanmıyor; sıra oyunun kendi bağlama sırası (20 Ekim, TR + EN)
`NewsAge` yaşı `(\d+) (day|hour|minute)` ile okuyordu: `NewsItemAgeString` oyunun **kendi dilinde** bir cümle
(TR "16 saat 30 dakika önce"), yani TR'de her yaş 0'a düşüp sıralama hiçbir şey yapmıyor, EN'de ise listeyi
yeniden diziyordu — aynı `news <#>` numarası iki dilde başka haberi gösteriyordu (2 günlük haber TR'de #2, EN'de #16).
`NewsItemReference` şemasında dil-bağımsız `Date`/`UniqueId` **var** (`/schema?filter=NewsItemReference`) ama ağaçta
bağlı değil; `bindprop` UI düğümünde değer vermiyor. `/pathset?to=fmcli.news&from=<NewsItem düğümü>` + `/pull` ile
`Date` geliyor ("2026-08-01", `UniqueId 189184`) — ileride yaş sıralaması gerekirse yol bu, ama haber başına iki çağrı
ve yeniden kullanılan düğümün eski değeri tutması riski var. Şimdilik sıralama kaldırıldı: liste tarama sırasında geliyor, yaş
oyunun cümlesi olarak basılıyor (TR "16 saat 30 dakika önce", EN "16 hours 30 minutes ago"). TR çıktısı eski
derlemeyle bayt bayt aynı; EN'de ortak haberlerin sırası TR'yle birebir. Ayrı yan bulgu: EN karo listesinde bir
haber fazla ("Hadžiahmetović makes Stoke City debut"); TR ağacında o karo yok — eski derlemede de öyleydi, bu
değişiklikle ilgisiz.

### 8.261 Antrenman programı seçicisi olayıyla onaylanıyor; açıklama sabit 500 ms yerine kendi değişimini bekliyor (20 Ekim, TR + EN)
`schedule set/style` seçicisi `press ?text=Confirm&event=EventToPlugin` ve `press ?text=Cancel&event=ClosePanel`
ile, okuması da `form ?anchor=Confirm` ile oyunun sözüne bağlıydı; seçenek basıldıktan sonra açıklama sabit
**500 ms** beklenip bir kez okunuyordu. Ölçüm (canlı, TR): seçenek basıldıktan **276 ms** sonra açıklama metinleri
23 → 64 satıra çıkıyor ve bir daha değişmiyor; 500 ms'lik duraklama yavaş karede eski seçeneğin açıklamasını
okuyabilirdi. Seçici artık kendi **veri yoluyla** okunuyor (`binding.Description` — oturum seçici,
`currentschedule.Description` — hafta programı; ikisi de çevrilen bir etiket değil), onay düğmesinin gönderdiği
olayla (`EventFmxConfirmTrainingSession` / `EventFmxConfirmTrainingSchedule`), iptal `ClosePanel` + şablon adıyla
(`button-base-label`) basılıyor. Açıklama, basıştan **önceki** metin listesinden farklı olup art arda iki okumada
aynı olana kadar okunuyor (üst sınır 2 sn, 0,2 sn aralık); böylece "hangi işaret" sorusunun cevabı süre değil,
seçicinin kendi yeniden çizimi. İz (EN, s1): eski `press ?ptr=…` 5,40 sn → `press ?text=Confirm…` 6,11 sn (710 ms,
500 ms'i kör); yeni `press ?ptr=…` 5,48 sn → açıklama okumaları 5,78/6,05/6,29 sn → `press ?event=…` 6,38 sn.
Kanıt: TR + EN × üç durum (başka sayfa açık / sayfa zaten açık / başka sayfaya geçtikten sonra) × iki yol
(seans `Physical ↔ Defending`, program `Defence ↔ Possession`) → 24/24 rc=0, çıktı eski derlemeyle **birebir**
(TR seans `9d26b11c…`/`98475996…`, program `e6ad2d3a…`/`292bf40a…`; EN seans `43db8c57…`/`29e9b88f…`, program
`ee65a3cb…`/`9c236d3f…`); izde `text=Confirm`/`text=Cancel` **0**, onay `press ?event=…`, iptal
`press ?event=ClosePanel&name=button-base-label`. Negatif `schedule set 3 1 "ZZZ yok"` rc=1, program değişmedi.
Yan bulgu (kod değişmedi): aynı seçicinin `About()` çözümleyicisi hâlâ İngilizce başlıkları arıyor
("Primary Focus", "Attributes", "Slightly Increased") → TR'de yalnız açıklama cümlesi, EN'de özellik/etki satırları
basılıyor; bu okuma yolu ayrı iş. Kayıt test kaydı (2 Ağustos 2026 09:15); seans/program değişiklikleri tur sonunda
geri alındı (`Fiziksel`, `Savunma`), saat ilerletilmedi, maç oynanmadı; dil EN ölçümünden sonra
`fm settings language "Türkçe"` ile geri alındı.

### 8.262 Antrenman seçicisinin açıklaması birincil odağı yazıyla değil bağlandığı yolla buluyor (20 Ekim, TR + EN)
Seçicide birincil odak `binding.SessionGroupFocusName` ile başlıyor; metni EN'de "Primary Focus", TR'de
"Priority" (oyunun kendi sözünün eklentideki İngilizcesi). `About()` "Primary Focus" aradığı için TR'de hiç
bulamıyor, hafta programı dalına düşüp yalnız açıklama cümlesini basıyordu. Artık odaklar yol ile ayrılıyor,
açıklama `binding.Description`dan; özellikler satır sayısı en çok olan başlıktan (Attributes) ilk seviyeye kadar;
etkiler `binding.Name` + `row` çiftlerinden okunuyor. Etki başlıkları çizilen metin olmadığı için eşleri gelmiyor →
`GameWords.Ask` ile oyunun tablosundan bir kez soruluyor (Mutluluk, Takım Uyumu, Sakatlık Riski, Kondisyon).
Ölçüm: TR üç durumda eski derleme yalnız cümle, yeni derleme cümle + `Özellikler` + `Etkiler` (Mutluluk ↑ ·
Takım Uyumu ↑ az · Sakatlık Riski ↑ az · Kondisyon ↓ az); EN üç durumda çıktı eski derlemeyle **birebir**
(Attacking 14 özellik, Defending Engaged 6). Hafta programı (`schedule style`) yolu değişmedi: `Retain Possession`
↔ `Tiki-Taka` denendi, cümle basıldı ve program geri alındı. Negatif `set 2026-08-04 1 "ZZZ yok"` rc=1.
Ölçüm sırasında takvim sekmesi ara sıra `Takvimin antrenman sekmesi okunamadı.` ile düştü (§8.216), aynı
derlemede tekrar denemede geçti.

### 8.263 Yarım kalmış odak formu kapanışını beklemeden yeniden açılıyordu (21 Ekim, TR + EN)
`OpenFocusForm` bir önceki koşudan kalan yarım formu `AbandonFocus` ile attıktan sonra sabit 800 ms bekliyordu.
Ölçüm (canlı): onay düğmesine basıldıktan sonra formun adım düğmeleri 0,03–0,10 sn'de kalkıyor; yeni kod
`FormStep() == null` olana kadar (üst sınır 6 sn, 0,2 sn aralık) bekliyor ve yeniden açılış 0,87–0,90 sn yerine
0,12–0,44 sn sonra oluyor. TR + EN × üç durum (başka sayfa açık / form zaten yarım / başka sayfaya geçtikten sonra)
6/6 rc=0 ve `fm scout areas --json` aynı üç seçenek; eski derlemeyle birebir. Form hiç kapanmazsa eski davranış gibi
yeniden açma deneniyor, bu kez `again` dalı döngüye girmiyor.

### 8.264 Bütçe kartının yazma yolunda yedek yazı kalmadı (21 Ekim, TR + EN)
`budget ayarla` kartı açan döşemeyi eylem kimliğiyle (1885623898) buluyordu, ama kimlik yoksa
`click ?text="Adjust Transfer and Wage Budget"`e; kartın "Got It"/"Cancel"i de olay/düğme bulunamazsa
İngilizce söze düşüyordu. Üç yedek de silindi: döşeme yoksa "Bütçe ayarı kartını açan döşeme bulunamadı.",
onay/iptal düğmesi yoksa "… düğme bulunamadı." deniyor. Ölçüm: TR + EN × üç durum (başka sayfa açık /
hemen ikinci koşu / başka sayfaya geçtikten sonra) × iki yol (iptal; `14`'ü tut + `15`'e geri) 18/18 rc=0,
çıktı eski derlemeyle **bayt bayt aynı** (TR iptal `3bcdc7a1…`, tut `93cbe81d…`; EN `6f04af3c…` /
`cd196064…`). İzlerde eski ve yeni hep `click ?ptr=…`, `text=` sayısı **0** — yedek hiç kullanılmıyordu;
negatif `ayarla 99` rc=1, kart yine `?ptr=` ile kapanıyor, bütçe 15. adımda kalıyor.

### 8.265 `fm load` soğuk açılışta yükleme listesini hiç açamıyordu (21 Ekim, TR + EN)
Oyun kapalıyken/kayıttayken `fm load "<ad>"` yeniden başlatıp yükleme ekranındaki "kariyer yükle" döşemesini
**İngilizce cümlesiyle** (`?text=Load an FM26 career …`) basıyordu; TR oyunda o döşemenin yazısı Türkçe olduğu
için basış hiçbir şeye denk gelmiyor (`error: no button shows …`), hata yok sayılıyor ve liste hiç açılmadan
`rowselect` deneniyordu → dört denemede rc=1, "Oyunun yükleme listesinde … yok" (iz: 2 dk, 28 başarısız
`rowselect`, `click ?text=…`). Aynı ekrandaki `Load` düğmesi de yazıyla aranıyordu. Artık ikisi de oyunun
kendi kimliğiyle: döşeme `Events` içinde **`LoadGameTileSelection`** taşıyan düğme, yükleme düğmesi
**`LoadSelectedGame`** + `Enabled=True` olan düğme; basış `?ptr=`, kimlik yoksa dürüst hata. Ölçüm: eski
derleme TR soğuk açılış rc=1 (2:02) → yeni TR/EN × üç durum (başka sayfa açık / liste zaten açık / başka
sayfadan sonra) **6/6 rc=0**, soğuk açılış 1:16–1:17, liste açıkken 11 s; çıktı iki dilde "Hazır/Ready:
2 Ağustos 2026", saat ilerletilmedi, maç oynanmadı. Kalan: `Last saved`/`Back to last saves` çapaları ve
satırın kendi adı hâlâ yazıyla (satır adı kullanıcının verdiği ad; başlık/bağlantı ayrı iş).

### 8.266 Kayıt tarayıcısının Save/Cancel/Delete'i yedek yazıya düşmüyor (21 Ekim, TR + EN)
`BrowserButton`/`DeleteButton` birincil kimliği (`SaveGame`, aynı panelin `ClosePanel`i, `DeleteSelectedGame`
+ `Enabled=True`) bulamazsa `?text=Save`/`Cancel`/`Delete`e düşüyordu; bu sözler oyunun dilindeki etiketin
çeviri tablosundan geçmiş hâli. Üç yedek silindi: kimlik yoksa dürüst hata veriliyor, yazıya düşülmüyor.
Ölçüm: `save --as "<yüklü kayıt>" --yes` ve `saves delete "<yüklü kayıt>" --yes`, TR + EN × üç durum (başka
sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra), eski derlemeyle çıktı **birebir** (TR
`23f3ca56…`/`a299b2d4…`, EN `46212f7f…`/`a299b2d4…`); izlerde tarayıcı basışları hep `click ?ptr=…`, `text=`
yalnız FM menüsünün `Save Game As` satırı (eklentinin `Events` özetinde yok, ayrı iş). Silme yolu kendi
kopyamızla uçtan uca: `t78 test.fm` → rc=0 "Silindi/Deleted", iki derlemede de `click ?ptr=…`; Faruk'un
dört kaydı yerinde, yüklü kayıt `save --as` ile aynı adla yenilendi.

### 8.267 `register --auto` sabit 1500 ms yerine oyunun "değişiklik aldım" işaretini bekliyor (21 Ekim, TR + EN)
Yardımcının seçimi (`Ask Assistant to Pick`) İngilizce yazısıyla aranıyor ve basıldıktan sonra sabit **1500 ms**
bekleniyordu. Düğme kendi olayını taşıyor: **`EventToPlugin:EventFmxAutoSelect`** (ölçüm: `/buttons?json=1&gen=1`),
yanındaki `Undo Changes` de **`EventFmxRevertChanges`**; ikisi de artık olayla basılıyor/aranıyor. Bekleme, oyunun
kendi işareti oldu: basıştan sonra `Undo Changes` **`Enabled=True`** olana kadar (`Until`, üst sınır 6 sn, 0,15 sn).
Ölçüm (EN, liste `--out` ile değiştirilmişken, `FM_TRACE=1`): basış 5,39 → ilk yoklama 5,52 sn (**0,13 sn**), eski
kod aynı yerde 1,5 sn kör bekliyordu. TR + EN × üç durum (başka sayfa açık / hemen ikinci koşu / başka sayfadan
sonra) `register --auto --json` eski derlemeyle isim/açık-kapalı/kural **birebir** (27 oyuncu, 20 açık); hiç
değişiklik yokken ikinci koşu da aynı listeyi veriyor. `Flags` sütunu bilinen geç bağlanma yüzünden koşular
arasında oynuyor (iki derlemede de); karşılaştırma isim/açık-kapalı/kural üzerinden yapıldı.

### 8.268 `fm responsibilities` görev çiftini sözle değil sayfanın sırasıyla buluyor (21 Ekim, TR + EN + DE)
`ParseDuties` bir görevin iki düğmesini ("Take Control" / "Delegate") metinleriyle arıyordu. Almanca oyunda
delegate düğmesi **"Delegation"** olarak geliyor (eklentinin çeviri tablosunda İngilizcesi yok), hiçbir çift
kurulmuyor ve `fm responsibilities --json` **rc=1** "Sorumluluklar sayfası okunamadı." veriyordu. Artık çift,
sayfanın çizdiği sıradaki iki `buttons-segmented-button-row-*` düğmesi olarak bulunuyor (önce Take Control,
sonra Delegate); söz araması kalktı. Ölçüm: TR eski/yeni üç durumda (başka sayfa açık / hemen ikinci koşu /
başka sayfaya geçtikten sonra) **birebir** `fa0ac05b…` (105 görev), EN `55e8337f…` (105); DE eski rc=1,
yeni üç durumda rc=0 ve aynı `57c24ed4…` (105 görev; 54 sende / 51 devredilmiş). Üç dilde görevli kümesi
(Clive Nates, Faruk Kavlak, Tom Clayton…) aynı; görev adları oyunun dilinde kalıyor.

### 8.269 Gözlemci odağı formunun Cancel'i sözle değil olay + şablonla basılıyor (21 Ekim, TR + EN)
`AbandonFocus` formu `click ?text=Cancel` ile kapatıyordu; "Cancel" oyunun dilindeki etiketin eklentinin çeviri
tablosundan geçmiş hâli. Düğme kendi olayını gönderiyor: `/buttons?json=1&gen=1` → `RecruitmentFocusEditConfirmationDialog`
(formun ikinci adımında `Confirm` de aynı olayı gönderiyor), şablonu `button-base-label`; artık ikisi birlikte
aranıp `?ptr=` ile basılıyor. Kanıt: `scout new "t81 test" --area "Competition>England>Sky Bet League One"
--priority top --by "ZZZ yok"` TR + EN × üç durum (başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten
sonra) eski ve yeni derlemede **12/12 rc=64**, stdout boş (`d41d8cd9…`), stderr birebir (TR `fc708400…`, EN
`b11015ec…`); izde eski `click ?text=Cancel` → yeni `buttons ?json=1&gen=1` + `click ?ptr=…`, sonrası aynı
`dialog`/onay. `scout areas` çıktısı iki derlemede de aynı; hiç odak oluşmadı, form açık kalmadı.

### 8.270 Gözlemci odağı formunun öncelik sekmesi ve yaşları oyunun adlarıyla; TR'de ongoing hiç kurulmuyordu (21 Ekim, TR + EN)
Üçüncü öncelik sekmesi `FormButton(second, "Ongoing")` ile çeviri tablosundan geçmiş İngilizce sözüyle aranıyordu;
form TR'de o sekmeyi **`Obscure`** diye veriyor (oyunun "Belirsiz" sözünün tablodaki İngilizcesi), düğme null
kalıyor ve `scout new --priority ongoing` **rc=134** ile çöküyordu (`ScoutFocus.cs:747`, hiç odak kurulmadan).
Sekmeler artık oyunun kendi adlarıyla (`Priority-Tab` / `Standard-Tab` / `Ongoing-Tab`); yaş listeleri başlık
sırasıyla değil yazdıkları yolla (`focusitem.minagevalue` / `maxagevalue`) bulunuyor ve seçim, listenin kendi
`Selected` indeksi istenen yaşı gösterene kadar bekleniyor (60–70 ms); `Confirm` de sözle değil şablonu
(`button-primary-leading-icon-label`) + `RecruitmentFocusEditConfirmationDialog` olayıyla basılıyor. İki sabit
400 ms kalktı (yaş: listenin seçimi; öncelik: oyunun olay kuyruğu, sekme→Confirm 190 ms, eskiden 450 ms).
Kanıt: TR üç durum + son koşu, EN üç durum `--priority ongoing --age 18-23` **7/7 rc=0**; odak verisinde
`Priority=2`; eski derleme EN'de aynı çıktı (`click ?text=Confirm`), TR'de rc=134. Test odakları durduruldu,
kayıt yeniden yüklendi (dört gerçek odak yerinde), dil Türkçe.

### 8.271 `fm tactic pick`in "Quick Pick"i düğmenin olayıyla basılıyor (21 Ekim, TR + EN)
Düğme `?text=Quick Pick&name=button` ile basılıyordu; "Quick Pick" oyunun kendi sözünün ("Hızlı Seçim")
eklentinin çeviri tablosundan geçmiş hâli, yani basış tabloya bağlıydı. Düğme kendi olayını gönderiyor:
`EventToPlugin:EventFmxQuickPick`; artık basış yalnız o olayla, `--dry` de aynı olayı soruyor.
Kanıt: `fm tactic pick` TR + EN × üç durum (başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten
sonra) **6/6 rc=0**; çıktı eski derlemeyle birebir (TR `md5 15786eb3…`, EN `43a4a177…`, ilk 11 aynı:
Wickens, Darikwa, Elerewe, Towler, Honohan, House, Varfolomeev, McGrandles, Hackett, Moylan, Draper).
İzde eski `press ?text=Quick Pick&name=button` (koşu başına 1–2 kez, olay kuyruğu tıkanınca `Arriving`
yineler) → yeni `press ?event=EventToPlugin:EventFmxQuickPick`; `text=Quick Pick` sayısı **0**.
İlk durumda düğme sayfada olmadığı için `dry` "error: no" dönüyor, `Planner` sayfayı açıyor, sonra basılıyor
(yani "sayfa ilk kez açılıyor" yolu da aynı). Kayıt test kaydı; saat 2 Ağustos 2026 09:15, ilerletilmedi,
maç oynanmadı; dil Türkçe'ye döndü. Dosyalar `…/opencode/t82/{base,new}-{tr,en}-s{1,2,3}.{out,err}`.

### 8.272 `fm table`in ülke eşi ölçüldü: 3 sn sınırı ~100 kat pay (21 Ekim, TR + EN)
Lig tablosunun adı ülkeyle eşleştirilirken `Human.Team.Nation.Name` en çok 3 sn (0,5 sn aralık) bekleniyordu.
Ölçüm (`FM_TRACE=1`): istek üç durumda da **26–30 ms**'de dolu değerle dönüyor (TR 26/28/28, EN 30/28/28) — yani
sınır bir yoklama aralığından çok uzak, pay ~100 kat. Değer hiç gelmezse yanlış tablo riski yok: ülke eşleşmesi
atlanıyor, portal yolunda tablo `NextFixture.Competition` adıyla karşılaştırılıyor, eşleşmezse yarışma adıyla
açılıyor. Üç durum (başka sayfa açık / hemen ikinci koşu / başka sayfaya geçtikten sonra) × TR + EN: `fm table`
çıktısı dil içinde bayt bayt aynı (TR `07136d20…`, EN `986b328b…`); kod değişmedi.
Dosyalar `…/opencode/t84/tr-s{1,2,3}.{out,err}`, `en-s{1,2,3}.{out,err}`.

### 8.273 `fm settings` yazma yolunda iki yazı yedeği kaldırıldı: onay olayla, sekme kapsayıcıyla (21 Ekim, TR + EN)

Preferences'in yazma yolu iki yerde oyunun sözüne düşebiliyordu: `PreferencesConfirmChanges` olayını taşıyan açık
düğme bulunamazsa `click ?text=Confirm&name=ConfirmButton`, sekme `TabButtons` eşlemesinde yoksa
`click ?text=<sekme>&name=DetailedPreferencesTabButton`. İkisi de bu dilin çeviri tablosundan geçen etiketlerdi
("Confirm" → Onayla/Kabul; sekme TR/DE'de "Matchday"). Kapsayıcı adları ve olay adı dilden bağımsız olduğundan
yedekler silindi: onay olayı yoksa `error: ayarların onay düğmesi bulunamadı`, sekme eşlemede yoksa
"Ayarlarda {0} sekmesi okunamadı." (rc=1). Ölçüm (üç durum: `fm open Messages` sonrası / hemen ikinci koşu /
`fm open Squad Dynamics`·`Dynamics` sonrası; TR + EN; `dd/mm/yyyy ↔ mm/dd/yyyy`): eski ve yeni derlemede **6/6 rc=0**
ve aynı çıktı (TR s1 `dd/mm/yyyy → mm/dd/yyyy`, s2 tersi, s3 s1; EN aynı sıra). İzde sekme
`click ?within=YourWorldTabButton&name=DetailedPreferencesTabButton`, onay `click ?ptr=…`; `text=Confirm` **0**.
Yedekler hiç ateşlenmiyordu (eski izde de onay `?ptr=`), yani davranış değişmedi; FM menüsünün
"Manage settings for this game" / "See all" satırları hâlâ yazıyla (ayrı iş, düğme kimliği yok).
Dosyalar `…/opencode/t85/{base,new}-{tr,en}-s{1,2,3}.{out,err}`.

### 8.274 `fm league`in istatistik kapsamı beklemesi 3 sn'lik sınırın çok altında (21 Ekim, TR + EN)

`StatScope` her istatistik için `pathset … .comp`in hatasız dönmesini 3 sn'ye kadar bekliyordu ("gelmezse yok
say" riski). Ölçüm: her `fm league` koşusunda 20 bağlamanın 20'si de **ilk denemede** geçti (TR 29–34 ms, EN
29–40 ms; hepsi benzersiz, tekrar yok) → 3 sn ~75–100 kat pay; hemen ardından gelen `pull` satırlarla dönüyor.
Üç durum (başka sayfa `Messages` açık / hemen ikinci koşu / `Squad Dynamics`·`Dynamics` sonrası) × TR + EN:
6/6 rc=0, çıktı dil içinde bayt bayt aynı (TR `fab0d284…`, EN `725bbf7e…`; TR `--stat Gol` `43f684e8…`).
Kod değişmedi. Dosyalar `…/opencode/t86/`.

### 8.275 Dil değişince `fm league --stat` eski dilin istatistik adlarıyla kalıyor (21 Ekim, TR → EN)

İstatistik kataloğu `cache/<state.Language>/…/league-stats.json`a yazılıyor, ama adlar oyunun **kendi sözleri**
ve yeniden açılışa kadar eski dilde kalıyor: `fm settings dil "English"` sonrası `fm league --stats` hâlâ
"Gol/Asist" listeliyor; İngilizce klasördeki katalog Türkçe adlarla yazıldığı için `--stat "Goals"` da
`--stat "Gol"` de rc=64 ("no player stat called …"). `--refresh` kataloğu yeniden okuyor (TR'de 50 sn) ama
yeniden açılış öncesi adlar değişmiyor. Çözüm: katalog adlarının dilini saklamak ya da açılıştan sonra bir kez
tazelemek. Ölçüm sırasında görüldü, bu turda kapsam dışı (CHECKLIST B'ye yazıldı).

### 8.276 Seçenek eşlemesi gösterilen sözü de kabul ediyor: `training --trait` Türkçe adda çalışmıyordu (21 Ekim, TR + EN)
`Matching` (`Guided.cs`) bir seçeneğin adını yalnız verideki İngilizcesiyle karşılaştırıyordu; komutun bastığı
liste ise `GameWords` ile oyunun sözüne çevriliyor. Bu yüzden `fm training "Ben House" --trait "Toplara Plase
Vurur"` (ekranda görünen ad) TR'de rc=1 "Öğretilebilecekler arasında … yok." verirken İngilizce "Places Shots"
aynı oyuncuda koçun itirazını (rc=0) gösteriyordu. Artık `Matching` hem veri adını hem `GameWords.Of(...)` sözünü
karşılaştırıyor (yazım payı da ikisi üzerinden); EN'de gösterilen söz İngilizce olduğu için davranış değişmiyor.
TR üç durum (Mesajlar açık / hemen ikinci koşu / Squad Dynamics sonrası): önce rc=1 `d41d8cd9…`, sonra rc=0
`89ae67bb…` 3/3. EN üç durum: eski ve yeni derleme `e968358e…` 3/3. Kısmi ad ("Sol Kanattan") iki seçeneği
listeliyor (rc=1), eğitim başlamıyor (`fm training` hâlâ "çalışmıyor"). `sweep.sh` 50/50.

### 8.277 Taktik paneli kendi çizilişini bildiriyor: 3 sn'lik sınır 2,6 kat paydaydı (21 Ekim, TR + EN)

`TacticPanel` kaydetme/yükleme panelini seçimden sonra 3 sn'ye kadar bekliyordu; panelin hazır olması "kaydetme
panelinde `SaveAddon` düğmesi + iki kutu, yüklemede `Load` düğmesi etkin" işaretiyle okunuyordu. Üç durumda
(`fm open "Messages"` sonrası / hemen ikinci koşu / `fm open "Squad Dynamics"` sonrası) ölçüm: panel seçimden
**0,76–1,17 sn** sonra çiziliyor, yani eski 3 sn yalnız ~2,6 kat pay bırakıyordu (TR: 1,17 / 0,76 / 1,15 sn;
EN: 1,00 sn ve altı). Üst sınır 8 sn'ye çıkarıldı; sabit bekleme eklenmedi, panel yine kendi çizilişiyle
bekleniyor. TR + EN × üç durum `tactic save "t88b/t88c test" --yes`: **6/6 rc=0**, her koşuda tek `select`,
panel ilk denemede, çıktı eskisiyle aynı; test dosyaları silindi. Dosyalar `…/opencode/t88/`.
Not: `tactic save "<ad>"` etkin taktiğin adını da o ada çeviriyor (kaydetme kutusu taktiğin adı); ölçüm
sırasında ad değişti, aynı taktik `tactic save "t179 test" --yes` ile eski adına döndürüldü.

### 8.278 Denetim bekleme sınırı (`Arriving` 8 sn) 7,1 kat pay bırakıyor; kod değişmedi (21 Ekim, TR + EN)
`Arriving` bir denetim gelene kadar 1 sn arayla yokluyor ve en çok 8 sn bekliyor. TR + EN × üç durumda
(s1 başka sayfa `Mesajlar`/`Messages` açık / s2 hemen ikinci koşu / s3 `Takım Dinamikleri`/`Dynamics` sonrası)
`fm scout areas --json` + `fm players "Aaron" --age 18-23 --json`: **12/12 rc=0**, en uzun tek bekleme **1,12 sn**
(oyuncu panelinin ilk yoklaması 1,05–1,11 sn, alanlarda ≤0,97 sn) → 8 sn ≈ 7,1 kat pay, projenin 7–10 kat ölçütünde.
Çıktı: `scout areas` altı koşuda da `fed56895…`; `players` TR 3/3 `f3ea1312…`, EN s1/s2 `253b0c5c…` (s3'te bilinen
kısa tablo 22/70 satır, ilk 22 satır aynı, §8.239). `Bound`un 20 sn'si portal bağlanması 0,30–0,50 sn'ye karşılık
geliyor (~40 kat); maç akışındaki 3 sn'ler maç günü olmadığı için ölçülmedi ve CHECKLIST'te açık kaldı.
Dosyalar `…/opencode/t89/`. Ölçüm tuzağı: TR oyunda ayarın adı `dil` — `fm settings language "English"` "diye bir
ayar yok" diyor; EN'e `fm settings dil English`, geri `fm settings language "Türkçe"` ile dönülüyor.

### 8.279 `fm prep`in iki `pathset`i ilk denemede geçiyor: 3 sn sınırı 83–230 kat pay (21 Ekim, TR + EN)
`FocusSlots` hazırlık okuması için iki bağlama yapıyor (`fmcli.mf<gün>.FixtureHasResult`, `.training`) ve her biri
`pathset` hatasız dönene kadar en çok 3 sn bekleniyordu. TR + EN × üç durumda (s1 `fm open "Messages"` sonrası /
s2 hemen ikinci koşu / s3 `Squad Dynamics` sonrası) `FM_TRACE=1 fm prep`: **6/6 rc=0** ve her koşuda iki `pathset`
de ilk yoklamada döndü (TR 13–36 ms, EN 13–32 ms) → 3 sn ≈ 83–230 kat pay. Kod değişmedi; gelmezse `set` false
dönüp `FocusSlots` null oluyor ve hazırlık hiç basılmıyor, yani "gelmezse yok say" yanlış veri üretmiyor. Çıktı dil
içinde bayt bayt aynı (TR `511c845d…` 3/3, EN `9f6e4f59…` 3/3); TR'ye dönüşte aynı. Dosyalar `…/opencode/t90/`.

### 8.280 Dil değişince `fm prep`in odak adları yeniden açılışa kadar eski dilde kalıyor (21 Ekim, TR → EN)
`fm settings dil English` sonrası `fm prep` başlıkları ("In possession", "To change") İngilizce, ama odak adları ve
açıklamaları (`Kaydedilen İlerleme`, `Orta Bölgede Pres`…) hâlâ Türkçe: bunlar oyunun `MatchFocusSessionName` /
`MatchFocusSessionDescription` verisi ve oyun onları yeniden açılışa kadar eski dilde tutuyor. CLI'nin kendi sözleri
dile uyuyor; §8.275'teki `league --stat` kataloğuyla aynı sınıf, kod tarafında düzeltilecek bir şey yok. TR'ye dönüş
`fm settings language "Türkçe"` ile yapıldı; çıktı tur başındaki `511c845d…` ile aynı.

### 8.281 Taktik yükleme panelinin kapanışı sabit 800 ms değil kendi `Load` düğmesini bekliyor (21 Ekim, TR + EN)
`TacticLoad`ın `close`u paneli `PanelHistoryClose` ile kapattıktan sonra sabit 800 ms bekliyordu; artık panelin kendi
`Load` düğmesi (`Events` içinde `EventLoadAddOn`) ekrandan kalkana kadar bekleniyor (`Until`, 0,15 sn, üst sınır 8 sn).
Ölçüm (geçersiz `t93 bogus.fmf`, üç deneme = iki kapanış): kapanış→sayfa eski kodda **0,83–0,88 sn**, yenisinde
**0,39–0,50 sn**; TR + EN × üç durum (başka sayfa / panel zaten açık / başka sayfaya geçtikten sonra) **6/6 rc=1**,
stderr birebir, her denemede panel kapanıp yeniden açıldı. Geçerli `t93 good` ile yükleme TR + EN **rc=0**, çıktı
eskisiyle aynı; kopya silindi, etkin taktik `t179 test`. Dosyalar `…/opencode/t93/` (`{tr,en}-{old,new}-s{1,2,3}.*`, `old/`).

### 8.282 Teklif kartı sabit 1,5 sn yerine kendi verisini bekliyor (21 Ekim, TR + EN)
`fm offers <#> view` satırın bağlantısına bastıktan sonra kartı okumak için sabit 1,5 sn bekliyordu. Artık kartın
kendi işareti bekleniyor: oyuncu ve kulüp satırdakiyle aynı, ilk terimi dolu ve kart iki okumada aynı (`Until`,
0,25 sn aralık, üst sınır 8 sn); gelmezse "teklif kartı açılmadı" rc=1. Ölçüm: TR'de basış→okuma eski kodda
**1,61–1,74 sn** sessizlik, yenide **0,85–1,22 sn**; izde `press` → 0,25 sn aralıkla `offer` yoklamaları. TR + EN ×
üç durum (s1 `Messages` sonrası / s2 `Transfer Activity` açıkken / s3 `Squad Dynamics` sonrası) **6/6 rc=0**, çıktı
iki dilde eski derlemeyle birebir (TR `e6d9188d…` 3/3, EN `1fdfda8d…` 3/3). Kart ilk yoklamada satırlarını boş
veriyor (0,1 sn sonra doluyor), bu yüzden "ilk terim dolu + iki okuma aynı"; kiralık teklifin olmayan maddeleri
("—") beklenmiyor. Dosyalar `…/opencode/t94/`, `old-bin/` eski derleme.

### 8.283 `fm propose --loan`: kiralık sekmesinden sonraki 1,2 sn yerine ücret kutusunun kalkması bekleniyor (21 Ekim, TR + EN)
`cli/fm/Propose.cs`. `--loan` yolunda "Loan Offer" sekmesine basıldıktan sonra kart okunmadan önce sabit **1,2 sn**
bekleniyordu; sonra liste bir kez daha okunuyordu. Sekmenin oyun içindeki cevabı, transferin ücret kutusunun
(`Type: FMCombobox`) panelden kalkması: ölçümde basıştan **0,14 sn** sonra düğme sayısı 185 → 179 oluyor ve kutu yok
(ara boş kare değil, kiralık panelinin kendisi). Artık kutu kalkana kadar (`Until`, 0,1 sn aralık, üst sınır 8 sn)
bekleniyor; gelmezse "Kiralık teklif sekmesi açılmadı." rc=1. Kullanılmayan yeniden okuma da kalktı (loan + ücret
zaten rc=64).
Ölçüm: `FM_TRACE=1 fm propose "Adam Reach" --loan` — eski derlemede basış→sonraki istek **1,33 sn**, yenide
**0,14–0,16 sn** (TR), **0,05–0,15 sn** (EN). Dört durum (başka sayfa `Messages` / hemen ikinci koşu / `Squad Dynamics`
sonrası / oyuncu kartı açıkken) TR 4/4 + EN 4/4 rc=0, çıktı dil içinde aynı. Gönderme yolu bir kez gerçekten denendi
(EN, "Oscar Thorn", `--loan --yes`): "Offered.", sonra aynı oyuncuda seçenek yok (rc=1); gönderim anında panel
kiralık hâldeydi (ücret kutusu yok). Negatif: `"ZZZ yok"` rc=1; transfer yolu (`500K`) iki dilde değişmedi.
Dosyalar `…/opencode/t95/`. Aynı desen `Transfers.cs` 96'da (teklif verirken loan sekmesi, sabit 2 sn) hâlâ duruyor;
ayrı madde.

### 8.284 `fm offer --loan`: kiralık sekmesinden sonraki 2 sn yerine kiralık panelinin çizilmesi bekleniyor (21 Ekim, TR + EN)
`cli/fm/Transfers.cs`: `--loan` yolunda "Loan Offer" sekmesine basıldıktan sonra kart okunmadan önce sabit **2 sn**
bekleniyordu. Sekme paneli iki adımda yeniden çiziyor: önce transferin satırları boşalıyor (`Rows` 0, `TotalCostTitle`
değişiyor), ~0,44 sn'de kiralığın 12 satırı geliyor. Oyunun kendi cevabı bu: `OfferScreen.Read` kiralık satır kümesini
(`IsLoan`, `Playing Monthly Fee`) gösterene kadar bekleniyor (`Until`, 0,1 sn aralık, üst sınır 8 sn = ~18 kat pay);
gelmezse "Kiralık teklif sekmesi açılmadı." rc=1. Sekmenin yazısı oyunun sözü, satır kümesi eklentinin kendi şeması.
Ölçüm (canlı, TR): basış→satırlar 0 (167–268 ms) → kiralık 12 satır **439 ms**. İz: eski derlemede basış→sonraki istek
TR **2,09 sn**, EN **2,18–2,33 sn**; yenide TR **0,40–0,46 sn**, EN **0,49–0,60 sn** (arada `offer` yoklamaları).
TR + EN × üç durum (s1 `Messages` sonrası / s2 hemen ikinci koşu [teklif açık] / s3 `Squad Dynamics`·EN `Dynamics`
sonrası) **12/12 rc=0**; çıktı dil içinde eski derlemeyle bayt bayt aynı (TR `981f3974…` 6/6, EN `cd76ddc5…` 6/6).
Transfer yolu (`--loan` yok) değişmedi; kiralıkta ücret kutusu yine "Playing Monthly Fee"ye yazılıyor (500K, çıktı
aynı). Dosyalar `…/opencode/t96/` (`old/` = değişiklik öncesi Release kopyası, `run2.sh`, `probe4.py`).
Aynı fonksiyonda kalan `Task.Delay`ler (128/131/139) `+`/`−` ile ücret yürütme döngüsünün okuma aralıkları; bu yolda
ölçülmedi, ayrı alt madde.

### 8.285 `fm settings` dil değişiminden sonra sayfayı sabit 3 sn bekleyip yeniden deniyordu (21 Ekim, TR + EN)
`cli/fm/Settings.cs`: ayar değişip onaylandıktan sonra tercihler sayfası kapanıyor ve değeri doğrulamak için yeniden
açılıyordu; açılamazsa `Task.Delay(3)` ile üç kez (9 sn) yeniden deniyordu. Oysa ilk açılış zaten onaydan
**0,37–0,41 sn** sonra oluyor (iz: `click ?ptr` → `texts All Preferences` → `texts Manage settings`), yani 3 sn
makine hızına dair kör bir varsayımdı. Artık sayfa açılana kadar 0,3 sn aralıkla soruluyor (`Until`, üst sınır 12 sn;
eskisinin 9 sn'sinden cömert). İlk okuma zorla boş bırakılan denemede yeniden sorma aralığı eski derlemede **3,08 sn**,
yeni derlemede **0,67 sn** (0,3 sn bekleme + okuma); toplam 43,0 → 24,7 sn (aynı iş). Normal koşullarda retry hiç
kullanılmıyor: TR→EN ve EN→TR × üç durum (başka sayfa `Messages` açık / tercihler Match Day sekmesinde açık /
Your World sekmesinde açık) **6/6 rc=0**, her koşuda değer doğru okundu ("dil: Turkish → English" /
"language: English → Turkish"), `/state` altı koşuda da doğru dil, kayıt 2 Ağustos 2026 14:00 (saat ilerlemedi,
maç oynanmadı). Değişiklik yalnız `Settings.cs` içinde → paylaşılan kod yok, `sweep.sh` gerekmedi.

### 8.286 `scout remove` onaydan sonra sayfayı sabit 800 ms bekliyordu (21 Ekim, TR + EN)
`cli/fm/ScoutFocus.cs`: odak durdurma onayı basıldıktan sonra `Task.Delay(800)` ile beklenip odak sayfası yeniden
açılıyordu; sonra atama sayısının düşmesi zaten bekleniyordu. Oysa onay penceresi (`/dialog`) basıştan **0,07–0,09 sn**
sonra boşalıyor (en geç 0,26 sn), sayfa hemen açılabiliyor. Artık pencere kalkana kadar `Until` (üst sınır 5 sn,
0,2 sn); basış→sonraki istek TR 0,84 → 0,08–0,27 sn, EN 0,85 → 0,07–0,08 sn. TR + EN × üç durum (başka sayfa açık / odak sayfası açık /
başka sayfaya geçtikten sonra) **6/6 rc=0**, her koşuda odak durdu (`Running=false`), `scout restart` geri aldı;
negatif odak adı rc=64 + seçenek listesi. Not: `scout restart` eskisini listede durmuş bırakıp yeni bir odak ekliyor
ve gözlemcileri yeniden dağıttığı için başka bir odağı durdurabiliyor; test sonrası kayıt yeniden yüklendi
(`fm load … --no-save`, 4 odak, 2 Ağustos 2026 09:15, TR).

### 8.287 Maç ekranının iki etiketi sabit Türkçeydi ("puan", "biz:") — İngilizce oyunda da Türkçe kalıyordu (21 Ekim, TR + EN)
`cli/fm/MatchDay.cs` (21 Ekim'de İngilizce oyunda denenmemiş değişiklikler): `sub` listesindeki oyuncu puanı
`$"puan {r:0.0}"` diye doğrudan yazılıyordu ve satır sonundaki kart özeti `$"biz: …"` ile kuruluyordu; ikisi de
`L(...)`den geçmediği için katalog devreye girmiyordu — İngilizce oyunda `puan 7.0` ve `Cards: biz: … · opponent: …`
(aynı satırın `rakip:`i çevriliyken). Artık ikisi de `L("puan")` / `L($"biz: {0}")` ile yazılıyor; `labels-english.tsv`e
`puan→rating`, `biz: {0}→ours: {0}` eklendi. Kanıt (canlı maç, aynı derleme öncesi/sonrası): önce EN `puan 7.0` /
`Cards: biz: Oisin Gallagher, Dom Jefferies · opponent: Louis Reed`; sonra EN `rating 6.8` ve
`Cards: ours: Ryley Towler` / `Our ratings: …`; TR `puan 6,8` ve `Kartlar: biz: Oisin Gallagher, Tom Hamer` (değişmedi).
Aynı turda İngilizce oyunda `reply` (konuşma + cevaplar), yeni ekler (altyapı kartı, rakip raporu), yüzde (`67%`)
ve `sub` (oyuncu değişti, sahadaki liste doğruladı) çalışıyor; dil canlı değişince motor yazıları (sakatlık adı,
diziliş, rol) yeniden açılışa kadar eski dilde kalıyor — §8.275'le aynı sınır.

### 8.288 İstatistik kataloğu artık oyunun İngilizce adlarıyla: dil değişince `fm league --stat` iki dilde de çalışıyor (21 Ekim, TR + EN)

Katalog `cache/<dil>/…/league-stats.json`a oyunun **gösterdiği** adlarla yazılıyordu; `fm settings dil` ile
`/state` hemen yeni dili söylerken oyunun motor sözleri (istatistik adları) geciktiği için katalog yeni dilin
klasörüne eski dilin adlarıyla düşüyordu. Ölçüm (önce): EN'de `fm league --stats` "Gol/Asist" listeliyor,
`--stat "Goals"` ve `--stat "Gol"` ikisi de rc=64. Artık katalog ada göre değil oyunun kendi tablosundan çevrilen
**İngilizce** adla tutuluyor (`translate?english=<ad>`, kurulumda ad başına bir kez), gösterirken tek toplu
`translate?words=` çağrısıyla o dilin sözüne çevriliyor; `--stat` hem İngilizceyi hem ekranda görünen sözü kabul
ediyor. (Oyunun tablosu istatistik adları için yalnız gösterilen→İngilizce yönünü veriyor; İngilizce→gösterilen
karşılığı olmayan ad "Goals" gibi İngilizce kalıyor — `--stat` yine iki adda da çalışıyor.) Ölçüm (sonra): TR'de `--stat "Gol"`/`"Goals"`/`"Sarı Kart"` rc=0, EN'de `Goals`/`Gol` rc=0; ilk kurulum
57 sn (160×2 okuma), sonraki koşular **0,35–0,39 sn** ve iki dilde de yeniden kurulum yok; `--stats` TR üç durumda
(başka sayfa açık / hemen ikinci koşu / Squad Dynamics sonrası) aynı `md5 0696aea7…`; `--json` 127 oyuncu/106 takım;
negatif `zzz` rc=64. Motor sözlerinin yeniden açılışa kadar eski dilde kalması (§8.287) sürüyor; katalog artık ona
bağlı değil.

### 8.289 İşsizlik iki kör okumayla değil menajerin kendi kaydıyla anlaşılıyor (21 Ekim, TR + EN)

`Unemployed()` takım adını iki kez okuyup arada sabit 400 ms bekliyordu; işsizken `Human.Team.Name` boş döndüğü
için `Bridge.Pull` cevabı "iki özellik de gelmedi" sayıp her okumada 6×250 ms yineliyordu (`{}` ile "henüz
bağlanmadı" aynı). İstifa edilmiş kayıtta eski `fm status`: 3,49–6,81 sn arasında 12 takım okuması + 400 ms,
ardından `Human.Name` 7,48 sn → tespit **~4,0 sn**. Artık menajerin kendi kaydı ham okunuyor: çalışan menajerde
`Human.Team` = `FM.UI.TeamReference` (tek okuma, hemen "değil"), işsizde `Team` özelliği yok ve
`Human.Club` = `SI.Bindable.NullReference` (tek okuma, hemen "işsiz"); ikisi de boşsa kayıt hâlâ bağlanıyordur,
0,2 sn arayla en çok 5 kez soruluyor. Ölçüm: işsiz TR + EN × üç durum (Messages sonrası / hemen ikinci /
Squad Dynamics sonrası) ve çalışan TR + EN × aynı üç durum **12/12 rc=0**, her koşuda tek `Human` okuması,
işsiz satırı iki dilde doğru; işsiz tespiti ~10–20 ms. `sweep.sh` 50/50. Dosyalar `/tmp/t101/`.

### 8.290 `fm players --attr "<nitelik><=N"` "Is At Most" yolunda hep düşüyordu: satır yarı çizilmişken okunuyordu (21 Ekim, TR + EN)

Karşılaştırma "Is At Most"a çevrildiğinde panel satırı iki adımda çiziyor: önce söz değişiyor, değer düğmesi
birkaç yüz ms sonra geliyor; ayrıca değer listesinin veri yolu `rule.minvalue` yerine `rule.maxvalue` oluyor.
Kod sözü görür görmez satırı hazır sayıp değeri okuyor (`Shown() = -1`), sonra listeyi eski yolda aradığı için
`<=` her zaman "değer N yapılamadı (panelde -1)" ile rc=1 veriyordu (TR'de finishing ≤7/≤20, pace ≤10,
passing ≤12: 3/3). Artık satır, değer düğmesi/listesi çizilene kadar bekleniyor ve liste iki bağlamadan da
bulunuyor; `finishing<=7` TR + EN × üç durum 6/6 rc=0. Ölçüm: değer 0,25–0,30 sn'de geliyor, hazır değer
0,02–0,05 sn, adım 0,02–0,04 sn; `>=` yolu değişmedi (rc=0, `finishing>=8` ile kesişim boş). Dosyalar
`/tmp/opencode/t102/`.

### 8.291 `fm staff`ın "All Staff" sekmesi ve tablosu yazıyla değil oyunun kendi kaplarıyla (21 Ekim, TR + EN)

Sekme `click ?text=All Staff&name=navigation-tab-default-default` ile basılıyor, tablo da `texts ?anchor=All
Staff&up=12` ile okunuyordu; ikisi de oyunun sözü ("Tüm Personel") üzerinden. Eklenti etiketi İngilizceye
çevirdiği için bugün çalışıyordu, ama çeviri tablosunda karşılığı olmayan bir sözde sekme hiç açılmazdı. Artık
sekme, düğmenin durduğu **kabın** adıyla (`AllStaffTabGridCompositionSet`) bulunuyor (şablonun adı, dilden
bağımsız; aynı desen `fm settings` sekmelerinde, §8.204), tablo da kendi panelinden (`StaffContractsTable`)
okunuyor; satırlar yine `binding.Name` ile başlıyor. Üç durum (başka sayfa açık / hemen ikinci koşu / başka
sayfaya geçtikten sonra) × TR + EN: **6/6 rc=0**, çıktı eski derlemeyle birebir (TR `md5 a3d8f866…`, EN
`ec69a3e1…`, 63 satır / 27 personel); izde `click ?text=All Staff` **0**, `click ?within=AllStaffTabGridCompositionSet`
ve `texts ?within=StaffContractsTable`. Süre 6,5–6,6 sn (eskisiyle aynı). Dosyalar `/tmp/t103/`.

### 8.292 `fm offer`in `+`/`−` yürütme döngüsü ücreti sabit 150 + 300 ms sonra okuyordu (21 Ekim, TR + EN)

`cli/fm/Transfers.cs` (teklif bedelini hedefe yürüten döngü): her basıştan sonra `Task.Delay(150)`, kutu
değişmemişse bir kez daha `Task.Delay(300)` ve tek okuma; ikisi de tutmazsa döngü sessizce yanlış bedelle
çıkıyordu. Artık işaret oyunun kendi cevabı: ücret kutusunun **yazısı basıştan öncekinden farklı** olana kadar
(`Until`, 0,1 sn aralık, üst sınır 4 sn); "bir üst adıma geç" düzeltmesi de aynı işareti bekliyor. İz
(`FM_TRACE`): kutu basıştan **~0,25 sn** sonra yeni bedeli gösteriyor (ilk yoklama 0,10 sn'de eski, ikinci
0,26 sn'de yeni) → 4 sn ≈ 16 kat pay. TR + EN × üç durum (`fm offer "Joe Bennett" 20001 --dry`) **6/6 rc=0**,
çıktı eski derlemeyle birebir (TR `md5 2e2a0e1b…`, EN `150a0009…`); `--dry` göndermiyor. Canlı dil
değişiminden hemen sonra EN'de taze açılışta eski derleme 2/2 kez ekranı okuyamadı (motor yazıları henüz
oturmamış, §8.275); dil oturduktan sonra yeni derleme taze yolda da kartı çizdi. Dosyalar `/tmp/opencode/t104/`.

### 8.293 `fm training --trait`in itiraz penceresi 3 sn'ye kadar bekleniyor; ölçüldü, pay 20–75 kat (21 Ekim, TR + EN)

`cli/fm/TraitTraining.cs` `Confirmed()`: seçimden sonra asistanın itiraz penceresi (`/dialog`) 3 sn'ye kadar
1 sn aralıkla soruluyor; gelmezse "itiraz yok" sayılıp devam ediliyor. Ölçüm (`FM_TRACE=1`,
`fm training "Ben House" --trait "Places Shots"`; pencere çıkıyor, `--yes` yok): pencere `select …&fire=1`
isteğinden **TR 0,15 / 0,09 / 0,05 sn**, **EN 0,06 / 0,08 / 0,04 sn** sonra geliyor, yani 3 sn ≈ 20–75 kat
pay; kod değişmedi. Üç durum (başka sayfa `Messages` açık / hemen ikinci koşu / `Squad Dynamics`·EN
`Dynamics` sonrası) × iki dil **6/6 rc=0**, çıktı dil içinde aynı; pencere "stop" ile kapandı, antrenman
başlamadı (`fm training "Ben House"` odak: Şut), saat ilerletilmedi. Dosyalar `/tmp/opencode/t105/`.

### 8.294 Sözleşme görüşmesinden çıkış düğmesi yazıyla değil oyunun çıkış olayıyla bulunuyor (21 Ekim, TR + EN)
`CallOff`un vaatler aşamasındaki çıkışı `press ?text=Exit Talks` ile basılıyordu; bu söz eklentinin çeviri
tablosundan geçen İngilizce etiket. Düğmenin kimliği ölçüldü: `SpeakToAction` eylemi **Event 42**
(`EventFmxExitNegotiations`) + `IsExitButton=True`; basış artık `?ptr=`, düğme yoksa yazıya düşülmüyor.
Ölçüm: vaatler kartı kilitliyken (`contract "Ben House"` rc=1 → `contract --cancel`) TR + EN × üç durum
(başka sayfa `Squad Dynamics` açık / `Messages` sonrası / hemen ikinci koşu) **6/6 rc=0**, çıktı dil içinde aynı
("Görüşme iptal edildi."/"Negotiation cancelled."). İz: eski `press ?text=Exit Talks` 0,42 sn; yeni `press ?ptr=…`
0,64–0,78 sn, görüşme ekranı basıştan **~0,3–0,5 sn** sonra kapanıyor (3 sn ≈ 6–8 kat). Negatif: açık görüşme
yokken `contract --cancel` rc=1. Dosyalar `/tmp/opencode/t106/`.

### 8.295 `fm schedule`ın antrenman sekmesi ve ızgarası 3 sn'de "gelmedi" sayılıyordu (21 Ekim, TR + EN)
`TrainingSchedule.TrainingCalendar`: takvimin antrenman sekmesi (`TrainingFocusCard`) ve ızgarası ("art arda iki
okuma aynı") en çok **3 sn** bekleniyordu; ikisi de oyunun kendi işareti, yalnız üst sınır dardı. Ölçüm
(`FM_TRACE=1`; `select` → son `texts ?anchorpath=day.DayOfTheMonth`): TR **1,92 / 0,61 / 1,31 sn**, EN
**1,37 / 0,78 / 1,19 sn** (üç durum: başka sayfa `Messages` açık / hemen ikinci koşu / `Squad Dynamics`·EN
`Dynamics` sonrası) → 3 sn en yavaş koşuda yalnız **1,6 kat** pay. Üst sınır sekme için **8 sn**, ızgara için
**12 sn** (≥6,3 kat). TR + EN × üç durum `fm schedule --json`: **6/6 rc=0**, çıktı eski derlemeyle bayt bayt aynı
(TR `md5 288ca6d1…` 3/3, EN `bba27efa…` 3/3). Dosyalar `/tmp/opencode/t107/sched/`.

### 8.296 Sözleşme görüşmesini iptal eden düğme artık olayıyla; iptalden sonra boşuna 6 sn beklenmiyordu (21 Ekim, TR + EN)
`ContractTalks.CallOff`: terimler aşamasındaki "Cancel Negotiations" düğmesi yazıyla (`Labelled`) bulunuyordu ve
iptal + onaydan sonra `LeaveTalks()` o düğme sanılan "Exit Talks"ı 6 sn arıyordu; oysa oyun görüşmeyi zaten
kapatıyor. İz: eski `contract --cancel` **7,13 sn**, sonunda 22 boş `buttons` yoklaması + `translate?words=Exit Talks`;
yeni aynı akış **1,57–1,77 sn**, `translate` yok. Düğmenin kimliği `EventFmxWithdrawContractNegotiationProcess`
(**811955833**; `docs/reference/enums.txt:859`), basış `?ptr=`. TR + EN × üç durum (doğrudan açılan görüşme /
hemen ikinci koşu / başka sayfa sonrası) **6/6 rc=0**, çıktı dil içinde aynı ("Görüşme iptal edildi." /
"Negotiation cancelled."), sonrasında `contract` rc=1 ("açık görüşme yok"); vaatler aşamasındaki olay-42 çıkışı
negatifte hâlâ çalışıyor (kilitli kart, rc=0). Dosyalar `/tmp/opencode/t108/`.

### 8.297 Teklif menajerde kaldığında görüşmeyi kapatma iki dilde de yanlıştı (21 Ekim, TR + EN)
`ContractTalks.Propose`ın "yalnız çıkış kaldı" dalı `"Adjust Budgets"` (çoğul) sözünü arıyordu; ekrandaki ham söz
**"Adjust Budget"** (tekil). TR'de dal hiç açılmıyor, `contract --sign --yes` "Sırada: Görüşmeyi Bitir · Bütçeyi
Ayarla" deyip görüşmeyi açık bırakıyordu (izde çıkış basışı **0**). EN'de dal açılıyordu ama `LeaveTalks` yazıyla
bulduğu düğmeye basmasına rağmen ekran kapanmıyor, komut yine de "görüşme kapandı" diyordu — yanlış "oldu".
Artık çıkış düğmesi eylemin kendi `IsExitButton=True` işaretiyle, "değiştiren" düğme kendi olayını göndermesiyle
(bütçe ayarı `Event 0`) bulunuyor; basış `?ptr=`, kapanma `contract` boşalana kadar bekleniyor (üst sınır 6 sn).
TR + EN × üç durum (başka sayfa `Messages` açık / hemen ikinci koşu / `Squad Dynamics` sonrası): **6/6 rc=0**,
`/contract` boş, çıkış basışı öneriden 0,55–0,96 sn sonra, ekran basıştan 0,06–0,10 sn sonra kapanıyor; çıktı
"Görüşme kapandı…" / "The offer is with the other side…". Dosyalar `/tmp/opencode/t109/`.

### 8.298 Arama panelinde nitelik değerinin adımı 3 sn'de "olmadı" sayılıyordu (21 Ekim, TR + EN)
`ApplySearch` (`StaffSearch.cs`, `fm players --attr`): `+`/`-` basışından sonra değerin satırda görünmesi en çok
**3 sn** bekleniyordu; işaret doğruydu (oyunun kendi "değer değişti" hâli), yalnız üst sınır dardı. Ölçüm
(`FM_TRACE=1`; `select …choice=20` → iki `-` adımı, basış → değişmiş `form ?anchorpath=filter.…`): TR
**0,34–0,51 sn**, EN **0,34–0,39 sn** (üç durum: başka sayfa `Messages` açık / hemen ikinci koşu /
`Squad Dynamics`·EN `Dynamics` sonrası) → 3 sn en yavaş koşuda yalnız **~6 kat** pay. Üst sınır **8 sn** (~16 kat).
Değişiklik hata yolunda: guard bitince değer yine değişmezse "değer … yapılamadı" (rc=1), sessiz "oldu" yok.
`fm players --attr "Pace>=18" --json` iki dilde 6/6 rc=0 ve aynı boş çıktı; `Pace>=15` satır kümesi eski
derlemeyle birebir (TR 17 ad, aynı sıra). `sweep.sh` 50/50. Dosyalar `/tmp/opencode/t110/`.

### 8.299 `fm prep`in iki `pathset`i 3 sn'de "gelmedi" saymıyor (21 Ekim, TR + EN)
`FocusSlots` sıradaki maçın hazırlığını okumadan önce `FixtureHasResult` ve `training` için iki `pathset` kuruyor
(üst sınır 3 sn, 0,2 sn aralık); düşerlerse komut "Maç hazırlığı okunamadı" ile rc=1 veriyor. Ölçüm (`FM_TRACE=1`,
`fm prep --json`): TR + EN × üç durum (başka sayfa `Messages` / hemen ikinci koşu / `Squad Dynamics`·EN `Dynamics`
sonrası) **6/6 ilk denemede**, yanıt **31–33 ms** → 3 sn ≈ **90–95 kat** pay; değişiklik gerekmedi. Üç TR ve üç EN
çıktısı bayt bayt aynı (`md5 262424c7…`; `--json` adları oyunun kayıttaki dili, veri yolu dilden bağımsız).

### 8.300 Gözlem odağının yıldız adımı 3 sn'de "olmadı" sayılıyordu (21 Ekim, TR + EN)
`SetStars` (`ScoutFocus.cs`, `fm scout new/edit --ability/--potential`): `+`/`-` basışından sonra yıldızın
değeri değişene kadar en çok **3 sn** bekleniyordu. Ölçüm (`FM_TRACE=1`; `fm scout edit "<odak>" --ability …`,
TR + EN × üç durum: başka sayfa `Messages` açık / hemen ikinci koşu / `Squad Dynamics`·EN `Dynamics` sonrası):
form açıldıktan sonraki ilk adım **0,52–0,65 sn**, sonraki adımlar **0,22–0,31 sn** → 3 sn en yavaş koşuda
yalnız **~4,6 kat** pay bırakıyordu. Üst sınır **8 sn** (~12 kat). 6/6 rc=0, çıktı "Odak güncellendi … /
Focus updated …", odak gerçekten değişti; ölçüm sonrası ana kayıt `--no-save` ile yüklendi (4 odak, dil TR).
Dosyalar `/tmp/t112/`.

### 8.301 `fm table`in sütunları önce açık olan sayfaya göre değişiyor (21 Ekim, TR + EN)
`LeagueTable` önce Stages sayfasında yarışmayı seçip tam tabloyu (O G B M AV P FORM) okumayı deniyor; o yol
tutmayınca portal karosunun dört sütunlu tablosuna (`# TAKIM O P`) sessizce düşüyor. Ölçüm (TR, `FM_TRACE=1`,
`fm open "Messages"` / `"Squad Dynamics"` sonrası `fm table`): `Messages` açıkken altı koşunun beşinde portal
(`md5 3f7d685f…`), `Squad Dynamics` sonrası 2/2 tam tablo (`387cc3c4…`); EN'de üç durumda da portal
(`7d4a7441…`), rc hep 0. İz: `select ?names=Competitions>…>Sky Bet League One` dönüyor ama sayfa kupa
aşamasında kalıyor, `ties` + altı `scroll ?anchor=<takım>` sonrası Stages vazgeçiyor. Sütunlar eksik geldiği
için "oldu" demek yanıltıcı; yarışma seçimi oyunun kendi işaretiyle (tablonun/aşamanın başlığı) doğrulanmalı.
Kod değişmedi; dosyalar `/tmp/opencode/t113/`.

### 8.302 Alt sayfanın içeriği 3 sn'de "gelmedi" sayılıyordu (21 Ekim, TR + EN)
`OpenSubpage` (Program.cs) çubuğun o sayfada durmasını bekledikten sonra içeriği en çok **3 sn** bekliyordu.
Ölçüm (`FM_TRACE=1`, `fm happiness --json`; çubuğun ilk cevabından sayfanın okunduğu ana kadar): TR
**1,07–1,55 sn**, EN **0,90–1,10 sn** (sekiz koşu) → 3 sn en yavaş koşuda yalnız **~1,9 kat** pay. Üst sınır
**8 sn** (~5–9 kat); işaret değişmedi (çubuk `check=1` "shown" + içerik, üst üste iki okuma, olmazsa dört tur).
TR + EN × üç durum (`Messages` açık / hemen ikinci koşu / `Messages`'a dönüp) **6/6 rc=0**, çıktı eski
derlemeyle bayt bayt aynı (TR `60e1e6fe…`, EN `bd3dc6a1…`); `sweep.sh` **50/50**. Dosyalar `/tmp/t114/`.

### 8.303 Mesaj gövdesi 3 sn'lik sınırı hak ediyor (21 Ekim, TR + EN)
`MessageBody` (Program.cs) haber gövdesini `newsitem.NewsBodyString` bağıyla okuyor, en çok **3 sn**
bekliyor. Ölçüm (mesaj basışından gövdenin bağlandığı ilk ana kadar, 30 ms'lik dış yoklama; TR + EN ×
üç durum — mesaj ilk kez / açıkken / `Squad Dynamics` sonrası): TR **0,31 / 0,43 sn**, EN **0,29 / 0,29 sn**;
mesaj açıkken gövde basıştan önce de bağlı. Yani 3 sn ≈ **7–10 kat** pay, "gelmezse" dalı yalnız açılış
cümlesine düşüyor → **kod değişmedi** (3 sn'nin 8 sn'ye çıkarılması denendi, kanıt iyileşme göstermedi;
`git restore`). Çıktı iki dilde de aynı (`d222f967…` / `525d3aa5…`).

### 8.304 Maç günü pencereleri oyunun DialogManager'ında değil (21 Ekim, TR)
`fm match --instant`in "Instant Result" ve "Team Selection" (dört yedek) pencerelerinde `/dialog` yalnız
`Title` veriyor; `Type` **boş** ve `?data=1` `visible=False` diyor — yani bu pencereler `SI.Bindable.
DialogManager` üzerinden geçmiyor. Bu yüzden B "yazıyla basma" maddesindeki üç pencereyi (Instant Result /
Team Selection / Injured Players) `Saves.DialogButton` gibi **Type** ile bulmak mümkün değil; kimlik
düğmenin kendi olayı/ActionID'si ya da sayfanın kabı olmalı. Ölçüm: `/dialog` 30 ms'lik yoklamada başlık +
düğme işaretçilerini verdi, `Type` alanı hiç gelmedi; `fm match --instant` 3,1–5,0 sn'de iki pencereyi de
başlıkla geçti. Dosyalar `/tmp/fm_dialogs.log`, `/tmp/fm_dialogdata.log`.

### 8.305 `Bound` veriyi 2 sn'lik sabit uykudan sonra soruyordu (21 Ekim, TR + EN)
`Program.Bound` (portal paneli bağlanmamışken `next`/durum yolunun kullandığı ortak okuma) önce 2 sn
uyuyup sonra `/inbox`ı soruyordu. Ölçüm (`FM_TRACE=1`, `fm next`): alt sekme gönderimi 0,17 sn → ilk cevap
**2,24 sn** (eski, TR); yeni kodda gönderim 0,19–0,23 sn → cevap **0,47–0,53 sn** (bekleme **0,30 / 0,34 sn**,
iki TR koşusu). Panel zaten bağlıyken (`Messages` açık ya da taze yükleme) ne eski ne yeni kodda bekleme var
(0 ms); EN'de bu koşullarda yeniden bağlama hiç tetiklenmedi, 20 sn sınırı 0,30–0,50 sn'lik bağlanmaya
**40–60 kat** pay bırakıyor. Değişen tek şey yoklama aralığı (2 sn → 0,25 sn); `sweep.sh` **50/50**.
Dosyalar `/tmp/opencode/t115/`.

### 8.306 Kayıt listesinde satır hazır işareti oyunun kendi cevabı (21 Ekim, TR + EN)
`SaveRowInView` (Saves.cs ~218, ~231) satırın listede olup olmadığını `/rowselect?text=…&dry=1`in hatasız
dönmesiyle anlıyor; 3 sn ve kaydırmadan sonraki 1,5 sn yalnız üst sınır. Ölçüm: liste çizildikten sonra
satır **0,04–0,11 sn**de seçilebilir oluyor (oyun içi kayıt paneli TR s1/s2/s3 0,04 / 0,11 / 0,09 sn,
EN 0,06 / 0,06 / 0,07 sn; bekleme ekranında TR 0,05, EN 0,05 sn), yani 3 sn ≈ 27–75 kat pay. Liste sanal
olsa da (gizli pencerede 3 satır) `/rowselect` bütün satırları buluyor: en eski kayıt (2020 damgalı kopya,
alt sıra) hem oyun içi panelde hem bekleme ekranında kaydırma yapmadan seçildi (`scroll` izde 0). Satır
gerçekten yoksa komut sessizce devam etmiyor: kaydırma denemelerinden sonra "listede yok" deyip rc=1
veriyor. Bu yüzden sınırlar değişmedi (churn yok).

### 8.307 FM menüsünün `Save Game As` satırı oyunun kendi seçeneğiyle bulunuyor (21 Ekim, TR + EN)
`OpenFmMenu` menünün açık olup olmadığını `texts ?anchor="Save Game As"` ile, `OpenSaveBrowser` satırı
`click ?text="Save Game As"` ile buluyordu; ikisi de eklentinin çeviri tablosundan gelen İngilizce söze bağlıydı.
Oysa düğmenin olayı `FMMenuOptionFromID` ve seçenek olayın kendi alanında: `/buttons?type=FMMenuOptionFromIDEventContext`
kataloğu `m_selectedOption = SaveGameAs` diyor (`FM.UI.FMMenuOption`, dilden bağımsız). JSON `Events` özeti bu
alanı taşımıyor, metin kataloğu taşıyor; yani eklenti değişikliği gerekmedi. Ölçüm (`FM_TRACE=1`,
`saves delete ".t117 yok" --yes`; boş bir `.t117 yok.fm` dosyası konup silindi): TR + EN × üç durum (menü kapalı /
menü zaten açık / `Messages` açık) **6/6 rc=1**, çıktı eski derlemeyle birebir ("… listesinde '.t117 yok' yok." /
"'…' is not in the game's list of saves."); izde `texts ?anchor=Save Game As` + `click ?text=Save Game As` →
`buttons ?type=…` + `press ?ptr=…`. Menü zaten açıkken tarayıcı 0,33–0,50 sn'de açılıyor; `fm world` TR + EN
rc=0, çıktı eskisiyle aynı; `sweep.sh` 50/50. Dosyalar `/tmp/opencode/t117/`.

### 8.308 `player --career` kariyer sekmesini döşemenin panel adıyla açıyor (21 Ekim, TR + EN)
`Career.cs` kariyer döşemesini `press ?text="Career Stats"` ile buluyordu; söz eklentinin çeviri tablosundan
geliyor, yani düğme dile bağlıydı. Döşemenin dilden bağımsız tutamağı, içinde dizildiği panel:
`#CareerStats` (BindingRemapper) › `layout-tile-interaction-button`. Artık `/click?within=CareerStats&name=…`;
izde `press ?text=Career Stats` → `click ?within=CareerStats&name=layout-tile-interaction-button`.
Kanıt: TR + EN × üç durum (oyuncu sayfası ilk kez açılıyor / kariyer sekmesi zaten açık / başka sayfadan
oyuncuya dönüş) **6/6 rc=0**, çıktı eski derlemeyle bayt bayt aynı (TR `md5 3724a170…`, EN `205f72cb…`).
Okuma dışı iş yok: `fm save` yok, tarih 9 Ağustos 2026 05:45, kayıt damgası 10:40. Dosyalar `/tmp/opencode/t121/`.

### 8.309 `player --matches` form döşemesini `PlayerForm` bağıyla açıyor (21 Ekim, TR + EN)
`Matches.cs` form döşemesini `click ?text="Last 5 Club Games"&name=layout-tile-interaction-button` ile buluyordu;
söz oyunun kendi metni, yani düğme dile bağlıydı. Döşemenin dilden bağımsız tutamağı, içinde dizildiği bağ:
`BindingRemapper #PlayerForm` (kaynak izinde `…B.PlayerForm9.TileVariables0`). Artık
`click ?within=PlayerForm&name=layout-tile-interaction-button`; `name` gerekli, çünkü form sayfası açıkken aynı
`PlayerForm` içinde 58 düğme var, `within` tek başına belirsiz. Kanıt: TR + EN × üç durum (oyuncu sayfası ilk kez
açılıyor / oyuncu sayfası zaten açık / form sayfası zaten açık) **6/6 rc=0**, çıktı eski derlemeyle bayt bayt aynı
(TR `md5 43c6f1a5…`, EN `md5 01570650…`). Dosyalar `/tmp/{trA,trB,trC,enA,enB,enC,trOld,enOld}.json`.

### 8.310 `training --report` antrenman döşemesini `PlayerTrainingOverview` paneliyle açıyor (21 Ekim, TR + EN)
`TrainingReport.cs` "Training Rating" döşemesini `press ?text="Training Rating"` ile buluyordu; söz çeviri
tablosundan, yani döşeme dile bağlıydı. Dilden bağımsız tutamak, döşemenin içinde dizildiği
`BindingRemapper #PlayerTrainingOverview`. Artık `click ?within=PlayerTrainingOverview&name=layout-tile-interaction-button`;
oyuncu sayfası eğitim sekmesinde bırakılmışsa döşeme o sekmenin altında kalıyor (`click` "no button … inside" der),
o hâlde koşullar aynı yere varır: antrenör listesi (`Request Training Report`) zaten ekrandadır ve bu başarı sayılır.
Kanıt: TR + EN × üç durum (başka sayfa `Messages` / hemen ikinci koşu, eğitim sekmesi açık / `Squad Dynamics`
sonrası) **6/6 rc=0**, çıktı eski derlemeyle bayt bayt aynı (TR `md5 a287fa00…`, EN `85a396a7…`); uçtan uca
`--coach "Tom Shaw"` TR + EN raporu okudu. Aynı fonksiyonda kalan (ayrı iş): liste `Label == "Request Training Report"`
ile, rapor eki `texts anchor="Attachments"` + `T.Rat` ile bulunuyor — bunlar dile bağlı, bu turda ele alınmadı.

### 8.311 `training --trait-cancel` düğmesi oyunun kendi eylemiyle bulunuyor (21 Ekim, TR + EN)

- "Cancel Current Trait Training" düğmesi `press ?text=` ile basılıyordu; yazı oyunun kendi sözlüğünden gelir.
  Düğmenin `Action` düğümü **ActionID 911493462** (`PersonTrainingNewPreferredMove`) taşıyor ve bastığı onay
  penceresi de `Confirm911493462`; ikisi de dilden bağımsız. Aynı karttaki "Start New Trait Training" / "Remove
  Trait" **SIDropdown**'ları da aynı ActionID'yi taşıdığı için düğme `Type == SIButton && Name == StandardButton`
  ile ayrılıyor (öneri düğmesi 1263235426, §8.209).
- Kanıt (`FM_TRACE=1`, altı koşu): TR + EN × üç durum (başka sayfa `Messages` / hemen ikinci koşu, sayfa açık /
  `Squad` sonrası) **6/6 rc=0**, çıktı eskisiyle aynı; izde `press ?text=Cancel Current Trait Training` **0**,
  `press ?ptr=` **1** (TR `tr-s1..3`, EN `en-s1..3`). Negatif: eğitim yokken `--trait-cancel` "çalışmıyor",
  rc=0, düğmeye basmıyor. Oyun "iptal edildi" dedikten sonra `--trait` listesi eğitimi bitmiş gösteriyor.
- Aynı fonksiyonda kalan (ayrı iş): "Training Rating"/"Traits" döşemeleri ve "Start New Trait Training"/"Remove
  Trait" listeleri hâlâ yazıyla bulunuyor; ayrıca `--trait` başlatınca kalan "Acknowledge and Move On" kartı
  `Acknowledge()`'in aradığı Event 31'e sahip değil, bu yüzden `--trait-cancel` öncesinde kartın kapanması
  gerekiyor (testte kart kullanıcı gibi basıldı; eski derlemede de aynı).

### 8.312 `settings` FM menüsündeki ayarlar döşemesini `PreferencesFMMenu` bağıyla buluyor (21 Ekim, TR + EN)
`Settings.cs` döşemeyi `click ?text="Manage settings for this game"` ile basıyordu; etiket o dilin sözü.
Döşemenin kendi kimliği, `OpenPanelEventContext` olayının `source` yolundaki **`PreferencesFMMenu`** (menü öğesinin
oyun tarafındaki adı); düğme artık `buttons?type=OpenPanelEventContext` listesinden bu adla bulunup `click ?ptr=` ile
basılıyor. Menü kapalıyken önce FM düğmesi (`dashboard-hud-fm-button`) basılıyor, döşeme görünene kadar yoklanıyor.
Kanıt (`FM_TRACE=1`, `fm settings --json`): TR + EN × üç durum (menü kapalı + başka sayfa / hemen ikinci koşu /
menü zaten açık) **6/6 rc=0**, çıktı eski derlemeyle bayt bayt aynı (TR `md5 fa5b6948…`, EN `cb604c52…`); izde
`texts ?anchor=Manage settings` + `click ?text=` **0**, `click ?ptr=` döşeme için **1**. Aynı fonksiyonda kalan
(ayrı iş): "See all" ve "All Preferences" hâlâ oyunun sözüyle aranıyor; onlar basış kutusu değil, çapa.

### 8.313 taktik planlayıcısı "Team Shape" düğmesini kendi düğme grubuyla basıyor (21 Ekim, TR + EN)
`Guided.cs` (`Planner`) talimat görünümünden sahayı geri getiren düğmeyi `click ?text="Team Shape"&name=team-shape`
ile basıyordu; etiket o dilin sözü. Düğmenin kendi yeri oyunun düğme grubu: `#FMShapeAndInstructionsButtons` içinde
iki düğme var (`#team-shape`, `#team-instructions`); basış artık `click ?within=FMShapeAndInstructionsButtons&name=team-shape`,
yazı hiç okunmuyor. Kanıt (`FM_TRACE=1`, `fm tactic`, talimat görünümünde `Slots=0`): TR + EN × üç durum (talimat
görünümü / hemen ikinci koşu / `Messages`'tan sonra sayfa yeniden açılıp) **6/6 rc=0**, çıktı eski derlemeyle bayt
bayt aynı (TR `md5 45360944d45966ddf152cc66231fd6fe`, EN `60570f71a42385ac711f736e0db2bc59`); izde
`click ?text=Team Shape` **0**, `click ?within=FMShapeAndInstructionsButtons&name=team-shape` her koşuda 1, `Slots`
0 → 11. `sweep.sh` **50/50**. Ayrı not (bu değişiklikten bağımsız): sayfa talimat görünümünde bırakılıp `Messages`
açıldığında `fm tactic` eski derlemede de `(boş)` veriyor — `Planner` sayfayı öne getirmiyor, ayrı iş.

### 8.314 teklif şartının ekranda görünmesi sabit 200 ms beklemeden sonra değil, hemen okunuyor (21 Ekim, TR + EN)
`OfferScreen.Settles` her şart değişikliğinden (kutu, seçim, ekleme, kaldırma) sonra **200 ms uyuyup** ekranı bir kez
okuyordu; değişiklik çoğu zaman o 200 ms içinde zaten gelmiş oluyordu, yani bekleme boşa gidiyordu. Artık değişiklik
gönderilir gönderilmez ekran okunuyor (şartın kendi değeri işaret), sonra paylaşılan `Program.Until` temposuyla
(0,2 sn) üst sınıra (4 sn, aynı) kadar yoklanıyor. `Until` `internal` yapıldı: `OfferScreen` `Program`'ın partial
sınıfı değil, yardımcı ona da açıldı; davranışı değişmedi.
Kanıt (`FM_TRACE=1`, `fm offer "Joe Bennett" 22001 --dry --term "Bonservis=22001"` / EN `Fee=22001`): `setnumber`
ile ilk `offer` okuması arası eski derlemede **~200 ms** (TR 9,48→9,71; EN 4,83→5,06), yenide **~0–10 ms** (TR
9,68→9,71; EN 9,10→9,12). Çıktı eski derlemeyle bayt bayt aynı: TR `md5 a5b86f950c18148bff7fbe2a727d089e` 3/3,
EN `9757dfedf9c93c5b4935cf0a93c1fdb4` 3/3; üç durum (`Messages` sonrası / hemen ikinci koşu / `Squad Dynamics` ·
`Dynamics` sonrası) **6/6 rc=0**. İkinci tüketici (ekleme yolu) `--term "Sonraki Satıştan Yüzde=%5"` rc=0, satır
kartta. `sweep.sh` **50/50**. Not: ilk EN koşusunda kart kapalıyken bir kez "ekran açık değil" (rc=1) alındı; hemen
aynı komut rc=0 — sayfa durumu, `Settles` ile ilgisi yok (hata şart değişikliğinden önce).

Ölçüm ayrıntısı (geçici iz, sonra kaldırıldı): `Settles`'a sayaç kondu. Kaldırma yolu
(`--term "Sonraki Satıştan Yüzde=remove"`) **ilk okumada 25 ms'de** "oldu" (satır ekrandan hemen kalkıyor; eski kod
her hâlükârda 200 ms uyuyordu → ~225 ms). Kutu yolunda (`Bonservis=22001`) ilk okuma 22 ms'de eski değeri görüyor,
ikinci okuma **236 ms**'de yeni değeri — eski kodun 200 ms + okuma süresiyle (~240 ms) aynı, yani kutu yolunda
gerileme yok. Bu yüzden değişiklik yalnız hızlı cevaplanan yollarda kazanç; üst sınır ve tempo aynı (4 sn / 0,2 sn).

### 8.315 yükleme ekranının dosya tarayıcısından dönüş, düğmenin kendi panelinden bulunuyor (21 Ekim, TR + EN)
`fm load`, yükleme ekranı dosya tarayıcısı hâlinde bırakıldıysa listenin başına dönmek için düğmeyi
`click ?text="Back to last saves"` ile basıyordu — düğmenin sözü dilin sözü. Düğme artık kendi kabından
bulunuyor: `click ?within=back-to-last-saves-SIVisible&name=button-base-leading-icon-label` (kabın adı
`back-to-last-saves-SIVisible`, kimlik dil değişince aynı kalıyor). Söz hiç okunmuyor.
Kanıt (`FM_TRACE=1`, oyun ana menüde, yükleme ekranı Browse ile dosya tarayıcısında bırakılmış):
eski derleme TR `click ?text=Back to last saves` (34 ms), yeni TR `click ?within=back-to-last-saves-SIVisible&name=…`
(38 ms), EN 34 ms; ekran basıştan **27–34 ms** sonra liste hâline dönüyor (`texts Back to last saves` boşalıyor).
TR + EN × üç durum (`browse ilk kez` / `geri dönüp Browse ikinci kez` / `liste hâli — düğme yok`): ilk ikisinde
tek eşleşme + basış, üçüncüsünde "no button … inside" ve `fm load` izinde **0** basış → **6/6 rc=0**, kayıt yüklendi.
Dosyalar `/tmp/opencode/t127/`.

### 8.316 arama kutusu kendi cevabını söylüyor: aynı sözü aramak 3 saniye beklemiyor (21 Ekim, TR + EN)
`fm search` (ve `player`/`targets`ın arama yolu) `SearchFor`'da iki sabit beklemeyle çalışıyordu: yazmadan önce
kutunun eski sonuçlarını oturtmak için 300 ms'lik okumalar, yazdıktan sonra da "oyun bitti demeden" 3 saniyelik
bir yedek. Kutu **aynı sözü** zaten taşıyorsa yeni sonuç eskiyle aynı oluyor, `found != before` hiç tutmuyor ve
komut her seferinde 3 saniyeyi dolduruyordu (TR 3,50 sn, EN 3,43 sn; tip→çıkış).
Artık yazmadan önceki oturma oyunun `SearchIsFinished` işareti + iki eşit okuma; yazdıktan sonra cevap, işaret
"bitti" derken listenin iki okumada aynı kalması ve **satırların dolu olması** (bayrak satırlardan önce dönüyor, bu yüzden
boş liste cevap sayılmıyor; boş liste yine 3 sn üst sınırından sonra inanılıyor). Kutunun kendi `Text`i aranan
sözle aynıysa liste zaten o sözün cevabı; yeni arama beklenmiyor.
Kanıt (`FM_TRACE=1`, üç durum: `inbox` sonrası kutu kapalı→açılıyor / kutu açık başka söz / kutu açık aynı söz):
eski→yeni tip→çıkış TR **3,50→1,08** · **1,56→0,95** · **3,49→0,25** sn, EN **1,50→0,65** · **1,51→0,65** ·
**3,43→0,25** sn; TR + EN × üç durum **6/6 rc=0** ve çıktı eski derlemeyle bayt bayt aynı (TR `4488265d…`,
`b5d65871…`; EN `228809ae…`, `c1c82e07…`). Negatif: sonuçsuz söz (`zzzzqqq`) rc=0, `(boş)`, 3,8 sn — eskisiyle
aynı. Dosyalar `/tmp/opencode/t128/`.

### 8.317 `squad`ın durum bayrakları yoklamasındaki 250 ms aralık borç değil (21 Ekim, TR + EN)
`StatusFlags` bir oyuncunun bayraklarını okurken liste ilk okumada bağlanıp sonraki turlarda doluyor;
`Task.Delay(250)` her turun **sonunda**, yalnızca hâlâ cevap bekleyen satır varsa çalışıyor — ilk okumadan
önce hiç beklemiyor. Ölçüm (`FM_TRACE=1`, `fm squad`): 27 satır her koşuda üç turda doldu (27+27+15 bayrak
okuması), iki aralık **0,28–0,29 sn** (250 ms + istek payı) tam olarak tur sınırlarında (#26 ve #53'ten sonra);
ilk okuma komutun 1,80. saniyesinde, beklemesiz. TR + EN × üç durum (`inbox` sonrası sayfa kapalı / hemen
ikinci koşu / `table` sonrası): **6/6 rc=0**, çıktı dil içinde bayt bayt aynı (TR `1916a8c5…`, EN `d6c3e0a3…`,
her biri 3/3). Aralık kaldırılırsa döngü boşa döner, uzatılırsa bayraklar geç gelir: **borç değil**, canlı
listede `[~]`. Dosyalar `/tmp/opencode/t129/`.

### 8.318 koç görevlerini yardımcıya bırakma, oyunun "atama değişti" işaretini bekliyor; 3 sn üst sınır borç değil (21 Ekim, TR + EN)

`fm coaches --auto` düğmeye bastıktan sonra sayfanın yeniden kurulmasını **atamaların değişmesi** işaretiyle
bekliyor (`Until`, 3 sn, 0,25 sn aralık). Ölçüm (`FM_TRACE=1`; basış → değişmiş atama okuması): TR **0,45 / 0,52**,
EN **0,40 / 0,42** sn; üç durum (sayfa başka sayfadan ilk kez açılıyor / hemen ikinci koşu / `Dynamics`·`Squad
Dynamics` sonrası) ve iki dilde **8/8 rc=0**, çıktı dil içinde aynı. Yani 3 sn, işaretin **5,8–7,5 katı**.
Yardımcı zaten doğru olanı seçtiğinde sayfada hiçbir şey değişmiyor ve oyun "bitti" demiyor: o durumda bekleme
haklı olarak üst sınırda bitiyor (TR 2,90–3,00 / EN 2,85–2,87 sn) — bu yüzden sınırı büyütmek yaygın "değişiklik
yok" hâlini yavaşlatırdı. Sabit "bekle sonra oku" yok: okuma basıştan hemen sonra başlıyor, süre yalnız üst sınır.
**Borç değil**, canlı listede `[~]`. Dosyalar `/tmp/opencode/t130/`.

### 8.319 lig istatistiğinin yarışma bağı ilk `pathset`te tutuyor; 3 sn üst sınır borç değil (21 Ekim, TR + EN)

`fm league --stat <istatistik>` her koşuda günün kendi yerinde (`fmcli.lg<gün>.<yarışma>.t<tür>`) yeni bir
bağ kuruyor ve `comp` bağının tutmasını `Until` ile **3 sn** üst sınıra kadar (0,2 sn aralık) bekliyor — yorumdaki
"bu çağrının bağladığı referans bir sonraki kareye kadar boştur" durumu için. Ölçüm (`FM_TRACE=1`; `pathset`
`Type`→`Stage`→`comp`): `comp` her koşuda **ilk denemede** döndü, TR **34 / 31 / 23 ms**, EN **30 / 30 / 31 ms**;
izde koşu başına tek `comp` çağrısı var, yani `Until` hiç tur atmadı. 3 sn, işaretin **~90–130 katı**.
TR + EN × üç durum (`Messages` sonrası / hemen ikinci koşu / `Squad Dynamics`·`Dynamics` sonrası) **6/6 rc=0**,
çıktı dil içinde bayt bayt aynı (TR `ce3771458e…`, EN `3b93f5b9df…`, her biri 3/3). Sabit "bekle sonra oku" yok:
ilk okuma komutun kendi çağrısı. **Borç değil**, canlı listede `[~]`. Dosyalar `/tmp/opencode/t131/`.

### 8.320 `PullEach`in 250 ms'i okumalar arasında tempo; ilk okuma beklemesiz (21 Ekim, TR + EN)
`League.PullEach` (tek okuma birden çok yol) tur arasında 250 ms uyuyor. `FM_TRACE=1 fm table` izinde ilk `pull`
komutun kendi çağrısıyla **0,16 s**de başlıyor; 250 ms yalnızca **tamamlanmamış** bir turdan sonra, iki okuma
eşitlenene kadar (yani oyunun kendi "değişmiyor" işareti) sürüyor: tur aralıkları TR 0,27–0,29 / EN 0,26–0,28 sn.
`ClubCompetitions` 8 property sorup kulüpte 6 yarışma olduğu için "hepsi geldi" hiç tutmuyor; bu yüzden her koşuda
3 tur (≈0,5 sn) atılıyor — bu bir sabit bekleme değil, iki-eşit-okuma işaretinin bedeli. TR + EN × üç durum
(`Messages` sonrası / hemen ikinci koşu / `Squad` sonrası) **6/6 rc=0**, çıktı dil içinde bayt bayt aynı
(TR `3f7d685f…`, EN `7d4a7441…`, her biri 3/3). **Borç değil**, canlı listede `[~]`. Dosyalar `/tmp/opencode/t132/`.

### 8.321 kayıt listesindeki satır `rowselect dry`nin kendi cevabıyla bulunuyor; 3 sn üst sınır borç değil (21 Ekim, TR + EN)
`SaveRowInView` (silme ve yükleme yolları) satırın listede olmasını `/rowselect?text=…&dry=1` hatasız dönmesiyle
bekliyor; o çağrı oyunun kendi "satır seçilebilir" cevabı, sabit bir duraklama değil. Ölçüm (`FM_TRACE=1`,
`fm saves delete "t133 probe" --yes`, boş bir `.fm` dosyası listeye konup silindi): tarayıcı açıldıktan sonraki
ilk `dry` TR **40 / 32 / 37 ms**, EN **32 / 32 / 24 ms** ve her koşuda **ilk denemede** döndü (izde `scroll` 0),
yani 3 sn, işaretin **~80–125 katı**. TR + EN × üç durum (`Messages` sonrası / hemen ikinci koşu / `Squad
Dynamics` sonrası) **6/6 rc=0**, "Silindi"/"Deleted", dosya gerçekten gitti. **Borç değil**, canlı listede `[~]`.
Dosyalar `/tmp/opencode/t133/`.

### 8.322 lig tablosunun ülke okuması ilk `pull`da geliyor; 3 sn üst sınır borç değil (21 Ekim, TR + EN)
`Reads.LeagueTable` (json olmayan yol) yarışma adını ülkeyle eşleştirmek için `Human.Team.Nation.Name`i
`Until` ile 3 sn'ye kadar bekliyor. Ölçüm (`FM_TRACE=1 fm table`; üç durum: `Messages` sonrası / hemen ikinci
koşu / `Squad Dynamics` sonrası): izde her koşuda **tek** `pull` var — TR **33 / 27 / 28 ms**, EN **27 / 26 / 26
ms** — ve hemen ardından 30 ms sonra `state` geliyor, yani `Until` hiç tur atmıyor, 0,5 sn'lik tempo hiç
çalışmıyor; 3 sn yalnız üst sınır. TR + EN × üç durum **6/6 rc=0**, çıktı dil içinde bayt bayt aynı
(TR `2d8cb6279f…`, EN `ccf41acd08…`, her biri 3/3). **Borç değil**, canlı listede `[~]`. Dosyalar
`/tmp/opencode/t134/`.

### 8.323 maç hazırlığının `pathset`i ilk denemede bağlanıyor; 3 sn üst sınır borç değil (21 Ekim, TR + EN)
`Prep.FocusSlots`in `set()`i (`Prep.cs:142`) günün kapsamını (`fmcli.mf20260809.FixtureHasResult`,
`.training`) `Until` içinde `pathset` ile bağlıyor; işaret oyunun kendi cevabı — `pathset` "error" ile
başlamıyorsa bağ kurulmuştur, sabit duraklama yok. Ölçüm (`FM_TRACE=1`, `fm prep duran1 …`; üç durum:
`fm inbox` sonrası / hemen ikinci koşu / `fm squad` sonrası; TR + EN **6/6 rc=0**): her koşuda dört `pathset`
çağrısı var (ikisi `FocusSlots` iki kez okunduğu için) ve **hepsi ilk denemede** döndü — iki `set` arası
**21–32 ms**, ilk `set` 0,21–0,22 sn'de bitti; `Until` hiç tur atmadı (tempo 0,2 sn hiç çalışmadı), yani 3 sn
işaretin ~100–140 katı. Değer değişimini doğrulayan ikinci bekleme zaten ayrı ve 5 sn (`Prep.cs:120`).
**Borç değil**, canlı listede `[~]`. Dosyalar `/tmp/{tr_a,tr_b,tr_c,en2_a,en2_b,en2_c}.err`.
Yan gözlem: arayüz EN iken `prep`e eski dilin (TR) adı verilince değer değişiyor ama komut "oyun değişikliği
almadı" diyor (`en_a`, rc=1) — ham `Name` ile gösterilen sözün eşleşmemesi; ayrı bir dil sınırı, bu tur ölçülmedi.

### 8.324 yardımcının itiraz penceresi seçimden 70–230 ms'de geliyor; 3 sn üst sınır borç değil (21 Ekim, TR + EN)
`TraitTraining.Confirmed` (`TraitTraining.cs:98`) alışkanlık eğitimi başlatılırken/silinirken/iptal edilirken
oyunun itiraz penceresini `Until(dialog dolu, 3 sn, 0,5 sn)` ile bekliyor; işaret oyunun kendi `dialog` bağı.
Ölçüm (`FM_TRACE=1`, `fm training "Ben House" --trait "<alışkanlık>"`; pencereyi dışarıdan 20 ms'de bir `/dialog`
yoklayan geçici `python3`): TR `Topa Falso Verir` (oyun yolu `Technique Training>Curls Ball`) ve EN `Curls Ball`
× üç durum (`Messages` sonrası / hemen ikinci koşu / `Squad Dynamics` sonrası) **6/6 rc=0**, çıktı dil içinde
birebir aynı (TR "Eklemek İçin Hiç Uygun Değil", EN "Very Unsuitable To Add"); pencere seçimden **70–230 ms**
sonra ekranda, izde 6/6 koşuda ilk okuma boş kalıp **ikinci yoklamada** (≤0,55 sn) doluyor, 7 koşunun 1'inde
ilk okuma zaten doluydu. Yani 3 sn, en kötü ölçümün (230 ms) **~13 katı** ve sabit duraklama değil; **borç
değil**, listede `[~]`.
Dosyalar `/tmp/measure_trait.py`, `/tmp/tt_{tr,en}_*`.

### 8.325 bir portal sayfası açıldıktan hemen sonra arama kutusunu açan basış düşebiliyor (21 Ekim, EN)
`fm open "Messages"` sonrası `fm training "Ben House" --trait` (oyuncuyu oyunun aramasıyla açan her komut) iki kez
`error: no text box like that on the loaded screens` ile döndü; bir koşuda stderr
`error: the event queue is blocked by 1 event(s) that never resolved … head: OpenPanelEventContext status=Sending`
geldi. Aynı komut tekrarlandığında rc=0; elle `/press?event=OpenPanel&name=dashboard-hud-search-button` sonrası
`/textboxes` `SearchBox`ı gösteriyor (kutu "Ben House" yazısıyla duruyor). Yani ilk basış sayfa yeniden kurulurken
kuyruğa takılıyor; `SearchFor` (`Reads.cs:99`) basışı bir kez yapıp 10 sn bekliyor, basışı yinelemiyor. Ayrı
madde: basışı "kutu görünene kadar" yinelemek. (Aynı akışta bir kez `fm search --json` "Unhandled exception:
System.InvalidOperationException" ile düştü; tek örnek, kök neden ölçülmedi.)
