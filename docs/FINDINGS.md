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
