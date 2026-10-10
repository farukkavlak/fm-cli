# fm-cli — Araştırma Notları

> Son güncelleme: 2026-09-21
> Durum: **probe v2 canlı testte başarılı** — veri ağacına erişildi, string path çalışıyor, oyun verisi okundu.

---

## 1. Proje nedir

Gerçek **Football Manager 26**'yı terminalden oynatan bir wrapper. Oyunu sıfırdan yazmıyoruz;
gerçek oyunu dışarıdan sürüyoruz.

Hedeflenen kullanım:

```
$ fm squad --fitness
$ fm results "Süper Lig"
$ fm transfers list
$ fm next
```

Uzun vadede MCP + sesle doğal dille oynama.

**Oyun ekranda görünmeyecek.** Pencere arka planda açık kalır, kullanıcı terminaldedir.

---

## 2. Ortam

| | |
|---|---|
| Makine | Apple M2, macOS 14.6.1 (Darwin 23.6.0), arm64 |
| FM26 | Steam appid `3551340` |
| Oyun yolu | `~/Library/Application Support/Steam/steamapps/common/Football Manager 26` |
| Motor | Unity **6000.0.52f1**, **IL2CPP** |
| Binary | Universal (x86_64 + arm64) — Rosetta kaçış yolu mevcut |
| `global-metadata.dat` | 15.5 MB, **şifresiz**, metadata v31.1 |
| BepInEx | 6.0.0-be.755 (IL2CPP), .NET 6.0.7 runtime |
| dotnet SDK | 10.0.401 (runtime 8 yok → `--roll-forward Major` gerekiyor) |

### Gölge bundle

arm64 launcher, `~/fm26_bep/fm.app` altında **JIT izinli, yeniden imzalanmış** bir kopya
oluşturuyor. macOS hardened runtime .NET'in JIT'ine izin vermediği için şart.
Oyun oradan çalışıyor; BepInEx log'u da `~/fm26_bep/BepInEx/LogOutput.log`.

Plugin'ler hem Steam klasörüne hem gölge bundle'a kopyalanmalı (ikisi senkron).

---

## 3. Kurulum / yeniden üretme

### BepInEx (arm64)

[DadMych/fm26-player-export-macos](https://github.com/DadMych/fm26-player-export-macos) bundle'ı
kullanıldı. arm64'te ClassInjector'ı öldüren `GenericMethod::GetMethod not found` hatasını
yamalı Il2CppInterop ile çözüyor.

```bash
FM26_GAME="$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26" \
FM26_SKIP_STEAM_LAUNCH=1 \
bash install_macos.sh
```

`FM26_SKIP_STEAM_LAUNCH=1` bilerek verildi: script aksi halde **Steam'i kapatıp**
`localconfig.vdf`'i düzenliyor. Gerek yok, oyunu kendimiz başlatıyoruz.

### Oyunu başlatma

```bash
cd "$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26"
./run_bepinex_arm64.sh
```

### Metadata dump

```bash
git clone --depth 1 https://github.com/Perfare/Il2CppDumper.git
dotnet build Il2CppDumper/Il2CppDumper.csproj -f net8.0 -c Release

# universal binary'yi tek mimariye indir — Il2CppDumper'a fat binary vermek riskli
lipo -thin arm64 "<oyun>/fm.app/Contents/Frameworks/GameAssembly.dylib" -output GameAssembly.arm64.dylib
cp "<oyun>/fm.app/Contents/Resources/Data/il2cpp_data/Metadata/global-metadata.dat" .

dotnet --roll-forward Major Il2CppDumper.dll GameAssembly.arm64.dylib global-metadata.dat out
```

Çıktı: `dump.cs` (42 MB, tüm sınıf/metot imzaları), `DummyDll/` (131 assembly).

İlgili assembly'ler: `FM.UI.dll`, `FMGame.dll`, `SI.Bindable.dll`, `SI.UI.dll`, `SI.Core.dll`

---

## 4. Ana bulgu: `SI.Bindable.Bindings`

Oyunun altında **global, path ile adreslenen, gözlemlenebilir bir veri ağacı** var.
Bu projenin temel taşı.

```csharp
// namespace SI.Bindable
public class Bindings
{
    public static Bindings.Key RootKey { get; }
    public IReadOnlyNode RootNode { get; }
    public IReadOnlyList<IReadOnlyData> DataSet { get; }

    // OKUMA — path ile
    public TypedValue Get(ReadOnlySpan<char> path);
    public TypedValue Get(in Bindings.Key key);
    public TypedValue Get(in Bindings.Key root, in ReadOnlySpan<char> relativePath);
    public bool Exists(ReadOnlySpan<char> path);

    // YAZMA
    public Bindings.Key Set(in ReadOnlySpan<char> path, TypedValue value, ...);

    // DEĞİŞİKLİK BİLDİRİMİ  ← cache invalidation + "gün ilerledi" olayı buradan
    public Bindings.Key Bind(in ReadOnlySpan<char> path, Bindings.ValueChangedCallback callback);
    public Bindings.Key BindOneShot(in ReadOnlySpan<char> path, ...);
    public void Unbind(...);

    // AĞAÇTA GEZİNME  ← neyin mevcut olduğunu keşfetmek için
    public IReadOnlyNode GetNode(in Bindings.Key key);
    public bool TryGetNode(in Bindings.Key key, out IReadOnlyNode node);
    public int GetDescendantCount(in Bindings.Key key);
    public void GetFullPath(in Bindings.Key key, ref Utf16ValueStringBuilder builder);
    public string GetPathDebug(in Bindings.Key key);
    public Bindings.Key FindNodeWithName(in Bindings.Key key, in ReadOnlySpan<char> name);
    public Bindings.Key FindContext(in Bindings.Key key, in ReadOnlySpan<char> context);

    public static Bindings.Key MakeKey(in ReadOnlySpan<char> path);
}
```

`Bind(path, callback)` özellikle önemli: **cache invalidation problemini çözüyor.**
Poll etmeye gerek yok, veri değişince oyun bize haber veriyor.

### Değerler: `SI.Core.TypedValue`

```csharp
public abstract class TypedValue : IEquatable<TypedValue>, IDisposable
{
    public abstract bool IsNull { get; }
    public abstract Type DataType { get; }
    public abstract string AsString();
    public abstract object Get();
    public abstract TVal Get<TVal>();              // ⚠ generic — arm64'te riskli
    public abstract bool TryGet<TVal>(out TVal value, bool referenceReturnsTrueWhenNull = false);
    public static TypedValue Create<TVal>(TVal value);
}
```

**Tipli veri geliyor** — UI metni parse etmiyoruz.

### Metadata'da görülen gerçek veri isimleri

`strings global-metadata.dat` çıktısından, hedeflediğimiz komutlara doğrudan karşılık gelenler:

```
Condition                              ← yorgunluk
Fitness                                FitnessCoach   FitnessTestResult
TransferListed                         TransferListOverviewReference
ExpectedGoals  ExpectedAssists  ExpectedGoalsPer90  ExpectedGoalsPrevented
FactsAverageAge  FactsAverageWage  FactsOldestPlayer  FactsYoungestPlayer
ExpiringContractsHeading  ExpiringContractRow
ExpenditureBreakdownThisSeason / ThisMonth / LastSeason / LastMonth
ExhaustedAndRecurringInjuries          ExpectInjuriesChange
ExpectedPlayingTimeNextFiveMatches     ExpectedVsPromisedPlayingTimeText
FMCCurrentDayTraining  FMCNextWeekTrainingFocus  FMCLast8WeeksTraining
ExpectedTransferValue  ExpectedWageDemands  ExpectedLoanInterest
```

> ⚠ Bunlar `strings` çıktısı — **isim** olduklarını biliyoruz, gerçek **path** olduklarını
> henüz doğrulamadık. Probe bunu test edecek.

---

## 5. Aksiyon katmanı

### `FM.UI.FMNavigationManager`

```csharp
public sealed class FMNavigationManager : MonoBehaviour, INavigationManager, IInitialisationStep
```

Ham tuş simüle etmiyoruz — oyunun kendi **semantik input action** sistemi var:

```
InputActionSubmit              InputActionCancel           InputActionClosePopup
InputActionDigIn               InputActionDigOut
InputActionTableSelectRow      InputActionTableSortBy      InputActionJumpToTopOfTable
InputActionSwitchPrimaryTabGroup   InputActionSwitchSecondaryTabGroup
InputActionNavigateListPagination  InputActionJumpToRibbon
InputActionEdit                InputActionDropdownQuickChangeOption
```

İlgili metotlar: `NavigateToPage`, `SetCurrentPanel`, `SetPanelFocus`, `GoToPortalScreen`,
`SetNavigationState`, `ForceOpenTacticsPanel`

### `SI.Bindable.PanelManager`

```csharp
public class PanelManager : ManualSingleton<PanelManager>, IInitialisationStep, ITickable
{
    public Panel Open(PanelID panelID, in Bindings.Key key, Panel.TransitionCompleteCallback onOpenCallback,
                      string anchor, RebindMethod rebindMethod = 0, uint identifier = 0);
    public PanelManager.PanelBuilder Build(PanelID panelID, Bindings.Key key, ...);
    public static bool TryGetLayerOfElement(VisualElement element, out int layer, out int layerSection, out Panel panel);
}
```

Ekranı doğrudan açmanın yolu bu olabilir — navigasyonu simüle etmekten temiz.

### Singleton deseni

```csharp
// namespace SI.Core
public class ManualSingleton<T> : MonoBehaviour
{
    public static T Instance { get; set; }
}
```

⚠ `get_Instance()` generic — arm64'te riskli. Bunun yerine
`UnityEngine.Object.FindObjectOfType(Il2CppType.Of<T>())` tercih edilmeli.

---

## 6. arm64 / IL2CPP kısıtları

BepInEx başlangıç log'undan çıkan **gerçek** kısıtlar:

| Kısıt | Etki |
|---|---|
| `MonoMod has no macOS arm64 support` | **Managed detour (Harmony hook) YOK.** Native detour var ama managed taraf görmüyor. → Mimari hook'a değil **çağrıya** dayanmalı. |
| `GenericMethod::GetMethod not found (arm64: likely inlined)` | Enjekte edilmiş sınıflarda generic metotlar çözülmüyor. → `Get<T>()` yerine non-generic `Get()` + `AsString()`. |
| `Class::GetDefaultFieldValue skipped on arm64` | Enjekte enum'larda varsayılan değerler çalışmaz. |
| IL unstrip: 10561 başarılı / **1452 başarısız** | Bazı metotlar hiç çağrılamayabilir. Hedeflerimiz listede mi, test edilecek. |
| `Mach-O Support is experimental` (Cpp2IL) | Genel kırılganlık uyarısı. |

### Plugin deseni (arm64'te çalışan)

MonoBehaviour **enjekte etme**. BepInEx'in ana thread pump'ını kullan:

```csharp
public override void Load()
{
    _probe = new Probe();
    IL2CPPChainloader.MainThreadTick += _probe.Tick;
}
```

Hotkey için legacy `Input` **çalışmıyor** — FM26 yeni Input System'de:

```csharp
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

if (Keyboard.current == null) return;
if (!((ButtonControl)Keyboard.current.f10Key).wasPressedThisFrame) return;
```

---

## 7. Mimari

```
   fm squad --fitness
        │
        │  HTTP  localhost:<port>
        ▼
┌──────────────────────────────────────────────┐
│  FM26 süreci İÇİNDE (BepInEx plugin)          │
│                                               │
│   okuma   →  Bindings.Get(path) → TypedValue  │
│   olay    →  Bindings.Bind(path, cb)          │
│   aksiyon →  FMNavigationManager / PanelManager│
└──────────────────────────────────────────────┘
        ▲
        │  ne fare, ne ekran, ne vision
```

**Plan B (kullanılmayacak, cepte):** ScreenCaptureKit ile arka plan pencere yakalama + vision.
Plan A tuttuğu için gerek kalmadı.

**Reddedilen yol:** save dosyası parse etme — FM'de kaydetmek çok uzun sürüyor, kullanıcı deneyimi
kabul edilemez.

### Cache

Şu an **kod yazılmayacak.** Önce ölç. Yavaşsa `(save_id, oyun_içi_tarih, generation)` anahtarıyla
eklenir; `Bindings.Bind` push invalidation sağladığı için invalidation problemi zaten çözük.

Probe v2 sonrası not: ağaç zaten canlı ve tembel-materialize olduğu için cache'in gereksiz çıkma
ihtimali yüksek — veri oyunun kendi belleğinden okunuyor, kopyalanmıyor.

---

## 8. Probe sonuçları (canlı oyun, 2026-09-21 23:47)

Dört açık sorunun dördü de cevaplandı.

### 8.1 `Bindings` instance'ına erişim ✅

`FM.UI.NewGameHandlerModule` (ScriptableObject) → `m_bindings`. Gerçek tip `SI.Bindable.BindingSubsystem`
(`Bindings`'in alt sınıfı, sadece `Initialise/Deinitialise` ekliyor). Erişim:

```csharp
var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FM.UI.NewGameHandlerModule>());
var bindings = all[0].TryCast<FM.UI.NewGameHandlerModule>().m_bindings;
```

`FMNavigationManager` ve `PanelManager` de `UnityEngine.Object.FindObjectOfType` ile bulunuyor.

### 8.2 `ReadOnlySpan<char>` köprüsü ❌ ÇALIŞMIYOR

> Bu bölüm 2026-09-28'de düzeltildi. Önceden "çalışıyor" yazıyordu; yanlıştı.

`Bindings`'in string alan bütün girişleri (`Get`, `Exists`, `Bind`, `MakeKey`) `ReadOnlySpan<char>`
istiyor ve **bu tip Il2CppInterop sınırından geçmiyor.** İki yöntem de denendi, ikisi de sessizce
boş span üretti:

1. `Il2CppStructArray<char>` → `new ReadOnlySpan<char>(array)`
2. `fixed (char* p = s)` → `new ReadOnlySpan<char>((void*)p, s.Length)`

Kanıt: her string aynı key'i veriyor ve var olmayan yollar `true` dönüyor.

```
'GameStateCache25': exists=True  makeKey=12638124528392833969
'zzzNotARealPath':  exists=True  makeKey=12638124528392833969
```

Sessizce başarısız olduğu için ilk testlerde fark edilmedi: `Exists("Game") = true` sonucu
gerçek değil, boş path'in köke çözülmesiydi.

**Sonuç:** path string'i ile adresleme kullanılamaz. Çalışan iki yol var:

- **Ağaçta gezinme** — `IReadOnlyNode.Name` düz `System.String`, `FirstChild`/`NextSibling` ile
  geziliyor. Okuma bununla yapılıyor ve sorunsuz.
- **`BindingPath.Create(string)`** — string kabul ediyor, ama onu tüketen `BindingBag.Queue`
  bir `IBindableElement` (UI elemanı) istiyor, yani view'a bağlı.

### 8.2b Key türetmesi çözüldü — path adresleme geri geldi ✅

Span çalışmıyor ama `Get`/`Bind`/`Exists`'in **`Bindings.Key` alan overload'ları span istemiyor.
`Key` sadece bir `ulong`. Yani key'i kendimiz hesaplarsak span'a hiç ihtiyacımız yok.

`Bindings.MakeKey`'in arm64 disassembly'sinden çıkarıldı (`GameAssembly.arm64.dylib`,
`il2cpp` bölümü, VA `0x36C0E90` → asıl hash `0x36BE5EC`), canlı ağaçtan alınan
**608 örneğin 608'inde** doğrulandı.

```
key(path):
    acc = Bindings.RootKey
    for segment in path.split('.'):
        h = 0xcbf29ce484222325                    # FNV-1a 64 offset basis
        for c in segment.lower():
            h = (h ^ c) * 0x100000001B3           # ⚠ UTF-16 kod birimi, byte DEĞİL
        x = (h ^ acc) * 0xff51afd7ed558ccd        # MurmurHash3 fmix64 çarpanı
        acc = x ^ (x >> 32)
    return acc
```

Üç kritik ayrıntı:

1. **UTF-16 kod birimi üzerinde** hash — byte üzerinde denediğimiz ~20 varyantın tutmama sebebi buydu
   (`ldrh w21, [x8, x23, lsl #1]` → 2 byte'lık okuma)
2. **Küçük harfe çevriliyor** — path'ler büyük/küçük harf duyarsız (`Bindings.s_pathComparer` ile tutarlı)
3. **Başlangıç kök key** — bu yüzden boş path kökü veriyor, span bozukken gördüğümüz davranış

Kod: `plugin/FmCliProbe/src/PathKey.cs`

Key'ler çalıştırmalar arası **kararlı** (600/600 aynı), yani saf fonksiyon — cache'lenebilir.

### 8.3 Ağaç global mi? ✅ EVET

`GetDescendantCount(RootKey) = 6093`. Kök 169 çocuklu. Ağaç **oyunun tamamı için tek ve global**,
sadece açık ekran değil. Ama **tembel**: bir referansın alt özellikleri, biri o path'i `Bind` edince
canlanıyor. (`Club [7]` — bir panel o 7 özelliği bağladığı için dolu.)

→ Ekran açmaya gerek yok. `Bind(path)` yapmak, ekranın perde arkasında yaptığı şeyin kendisi.

### 8.4 Metadata isimleri gerçek path mi? Kısmen

`Exists("Condition")` → `KeyNotFoundException`. İsim doğru ama **kökte değil**, oyuncu referansının
altında. Path'ler iç içe: `game.Human.Team.<...>.Condition` gibi.

### 8.5 Ağacın şekli: nesne grafiği

```
game (GameReference)
  Human (PersonReference)              ← menajer (sen)
    Team (TeamReference)
      Club (ClubReference)
        PlayersOutOnLoanCount = (DynamicNumber) 15      ← CANLI VERİ
        InternationalPlayersCount = (DynamicNumber) 0
        PlayersOutOnLoan = List<TypedValue>
        MainTeam (TeamReference)
      NextMatchOpponent (TeamReference)
      CanViewSquad = True
    NextManagedFixture (FixtureReference)
  app (ApplicationReference)
```

Görülen referans tipleri: `PersonReference` (IPlayerReference, INonPlayerReference, IAgentReference...),
`TeamReference`, `ClubReference`, `CompReference` (lig), `FixtureReference`, `StadiumReference`,
`GameReference`, `DynamicReference`, `NullReference`.

Ağaç aynı zamanda UI panel yapısını da içeriyor (`17dc969b...AD` panel GUID'leri, `Subtab9C`,
`style-class-on-visible`) — veri ile UI durumu iç içe. Veri düğümlerini referans tiplerinden ayırt ediyoruz.

### 8.6 Referanslar kendini tanımlıyor ve veritabanına bağlı

```csharp
// FM.UI.DatabaseRecordReference : SI.Interop.InteropReference
public void GetProperties(List<PropertyID> properties);     // "hangi özelliklerin var?"
public bool AcceptsProperty(uint property);
public ReferenceID ID { get; }
public DatabaseTableType Type { get; }
public DatabaseRecordReference(DatabaseTableType type, int index);   // herhangi bir kayda referans üret
```

`SI.Interop` = Unity UI katmanı ile FM'in **C++ çekirdeği** arasındaki köprü. Referanslar native
veritabanı kayıtlarını sarıyor; kararlı ID'leri var. Şemayı tahmin etmeyeceğiz, oyundan soracağız.

### 8.7 Değer tipleri

`TypedValue.DataType.Name` olarak görülenler: `Boolean`, `String`, `DynamicNumber`, `List\`1`
(List<TypedValue>), ve tüm `*Reference` tipleri. `AsString()` hepsinde çalıştı.

## 8.8 Performans beklentisi ve `runInBackground` tuzağı

Okuma komutları `~100 ms` altı: HTTP ~1 ms + bir frame bekleme (main thread şartı) + bellekte hash lookup.
Render/animasyon olmadığı için UI'dan hızlı. Bekleme sadece oyunun kendi simülasyonunda (`fm next`).

⚠ **Unity oyunları odak dışında durur** (`Application.runInBackground` varsayılan `false`).
Plugin `Load()` içinde `Application.runInBackground = true` yapmalı, yoksa kullanıcı terminale geçince
oyun donar ve komutlar cevap vermez. Ayrıca oyun küçültülünce render CPU'su simülasyona kalır →
`fm next` normal oynamaktan hızlı olabilir.

## 8.9 Aksiyon katmanı: event bus (UI simülasyonu DEĞİL)

FM26 aksiyonları bir **global event bus** üzerinden yürütüyor. Menü gezme / tuş simülasyonu
gerekmiyor — olayı doğrudan gönderiyoruz.

```csharp
// SI.Core.EventSubsystem : ISubsystem
public void SendEvent(EventBase evt);
public void Register<TEventType>(EventDispatcher.EventCallback<TEventType> callback);
public void Unregister<TEventType>(EventDispatcher.EventCallback<TEventType> callback);
```

`Register` ayrıca **oyun olaylarını dinlememizi** sağlıyor (gün ilerledi, maç bitti...) — hook'a
gerek yok, ki zaten arm64'te managed detour yoktu. Bu, §7'deki "poll ederiz" planını gereksiz kılıyor.

⚠ `Register<T>` generic → arm64'te riskli olabilir. `SendEvent(EventBase)` non-generic, güvenli.

### Erişim (Bindings ile aynı desen)

`eventSubsystem` alanını tutan ScriptableObject'ler: `FM.UI.DevicesInputHandler`,
`FM.UI.InputRebindingModule`, `FM.UI.NativeSettingsHandlerModule` — hepsi `IGameModule`.

```csharp
var m = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FM.UI.DevicesInputHandler>())[0]
    .TryCast<FM.UI.DevicesInputHandler>();
var events = m.eventSubsystem;
```

### Event anatomisi

```csharp
public sealed class LoadSelectedGameEvent : EventBase<LoadSelectedGameEvent>
{
    public Record LoadGameDataRecord;
}

// SI.Bindable.Events.EventContextBase : IEvent, IEventOwner
public abstract EventBase GetPooledEvent();
public void Setup(IEventOwner owner, VisualElement target, Bindings.Key overrideKey);
public void PrepareToSend();
public virtual bool IsSendable();
```

Event'ler havuzdan alınıyor (`GetPooledEvent`), context bir `Bindings.Key` ile kuruluyor —
yani event ile veri ağacı aynı adresleme sistemini paylaşıyor.

### Kayıt yükleme

`LoadSelectedGameEvent` + `LoadGameTileSelectionEvent` + `SaveGameReference` mevcut.
`SaveGameReference` bir `*Reference` tipi → kayıtlar da binding ağacında adreslenebilir.
Yani `fm saves` / `fm load` event göndererek yapılabilir görünüyor; kariyer *oluşturma*
sihirbazı ise çok adımlı, sonraki milestone.

## 8.10 Gelen kutusu okunuyor + `fm next`'in gerçek davranışı

`ContinueGameEvent` (alansız, `new` edilip gönderiliyor) doğru event ama **oyun reddedebiliyor**:
kariyer başında "response required" mesajları Continue'yu blokluyor, tarih ilerlemiyor.

→ `fm next` sessizce başarısız olmamalı, "bloke: N mesaj cevap bekliyor" demeli.

Gelen kutusu ağaçta, yolu:

```
PortalScreenA2.1.1.1.4.Messages4.0.1.BindingSetterA.MessagesStreamedObjectListA.items0.<0..9>
  .BindingRemapper1.NewsItem (NewsItemReference)
     NewsTitleString  NewsFirstBubbleSpeechString  NewsItemTimeSent
     NewsItemHasBeenRead  NewsHasResponded  CanRespond  NewsItemIsPriority
  .Person (PersonReference)
     Name  Initials  Team
```

Okunan gerçek veri: başkandan karşılama mesajı, basın sorumlusunun notu, 10 mesaj, 5'i
`CanRespond = True`.

**`Name` geçerli bir `PersonReference` property'si** — burada çalışıyor. Yani kadro oyuncularında
eksik olması isim yanlışlığından değil, sadece canlanmamış olmasından.

⚠ String'lerde gömülü veri öneki var:
`"BAAAACAxMTEB...=Sadettin Saran"` — muhtemelen `EmbeddedDataHandler`'ın tıklanabilir bağlantı
markup'ı. `=` sonrası alınmalı; CLI'a basmadan önce temizlenecek.

## 8.11 Şema statik: oyunun kendi kayıt defteri

Şu ana kadarki hata, veri modelini canlı ağacı dürterek keşfetmeye çalışmaktı. Model statik —
sadece *değerler* canlı. Oyun kendi şemasını üç kayıt defterinde tutuyor:

```csharp
// SI.Bindable.Reference.Core
PropertyIdentifierSet.m_idToInfo   : Dictionary<uint, PropertyIDInfo>
ReferenceIdentifierSet             // referans tiplerinin adları
ContextIdentifierSet               // bağlam adları

PropertyIDInfo { uint ID; string Name; string DisplayName; int Flag; }
PropertyID.GetIdentifierSet()      // instance metot — generic static'e dokunmaya gerek yok
```

`GetPropertyDescriptionInternal` release build'de boş dönüyor (§bugünün bilançosu'nda
"açıklamalar ayıklanmış" diye not edilen şey), ama **kayıt defteri ayıklanmamış**. Yani property
ID'lerinin okunaksız dört karakterlik kodlar (`g96I`, `7upK`) olması bir engel değil: her ID
isimli olarak kayıtlı.

Her `*Reference` tipinin `GetPropertiesInternal` ve `GetPropertyTypeInternal` metotları **static**.
Yani bir düğümü canlandırmaya hiç gerek yok: tip kendi property'lerini canlı veri olmadan sayıyor.

Sonuç — `/schema` tek çağrıda: **931 referans tipi, 14504 property, hepsi isimli, 0 çözümsüz.**
`docs/reference/data-model.txt`.

⚠ Tipleri `InteropReference`'dan türeyenlerle sınırlamak yetmiyor: bir kısmı (ör.
`NewsActionDataReference`) doğrudan `Il2CppSystem.Object` üzerinde duruyor. Ölçüt baz tip değil,
**static `GetPropertiesInternal`'ın varlığı** — bu düzeltme modeli 160 tipten 931'e çıkardı.

| Tip | Property |
|---|---|
| `PersonReference` | 1245 |
| `ClubReference` | 492 |
| `TeamReference` | 461 |
| `CompReference` | 296 |
| `MatchTacticReference` | 227 |

`BindingKind` ayrıca property'nin referans olup olmadığını ve **hangi tipe gittiğini** söylüyor
(`Reference -> person`), yani düz liste değil gezilebilir bir grafik: 1449 kenar.

### Aksiyon yüzeyi de statik

`EventBase<T>`'den türeyen her tip = FM26'da yapılabilen her aksiyon. Interop DLL'lerinden,
oyun çalışmadan: **275 event**, alanlarıyla. `tools/sigdump --catalog events`,
`docs/reference/events.txt`.

Mesaja cevap verme bu yüzden artık tahmin değil:

```csharp
NewsResponseActionEvent {
    NewsItemReference NewsItem;  int ResponseId;  int ResponseValue;
    int ResponseActionId;        List<TypedValue> ResponseActionDataReferences;
}
```

`NewsItemReference`'ın 44 property'sinden ilgili olanlar: `NewsActionData`,
`NewsItemActionDataReferences`, `NewsItemAction`, `CanRespond`, `ContinueText`,
`LatestResponseString`.

## 8.12 Span köprüsü `ref` ile de ölü

§8.2'deki değer geçen aşırı yükleme çalışmıyordu; API `ref ReadOnlySpan<char>` de sunuyor ve
pointer geçtiği için sağ kalması beklenirdi. Kalmıyor:

```
'app.LastSave'          : exists=True
'ZzNotAPathZz.NopeNope' : exists=True   ← çöp yol da True
```

Aynı yanlış pozitif. Yani `Create(path, flags)`, `FindNodeWithName`, `FindContext` — span alan
her şey kapalı; adresleme §8.2b'deki kendi key hesabımızla kalıyor.

## 8.13 Canlandırma çözüldü: doğru event'i gönder, veri ağaçta belirir

Tembel ağaç problemi (§bugünün bilançosu) ekranı *açtırmayı* gerektirmiyor; doğru event
yetiyor. Gelen kutusunda bir mesaj için:

```
curl --get localhost:7777/send \
  --data-urlencode event=SetNewsActionDataOnPanelEvent \
  --data-urlencode "NewsItem=<mesajın yolu>" \
  --data-urlencode PortalSourceId=0
```

Ağaç **4198 → 5921** düğüm. Belirenler: mesajın tam gövde metni, `SpeakTo.Actions`,
`npcaction`, `has-required-actions`, `respondedtype`, ve cevap seçenekleri:

```
Actions8.0.Action
  ActionTitle = Cancel Training Camp
  Ticked = False   Recommended = False   IsDisabled = False   IsPopupButton = False
```

Yani "oyuncu özelliklerini canlandır" maddesi de artık bir keşif değil, doğru event'i bulma
işi — ekranın görünür olmasına gerek yok.

⚠ `OpenTabEvent{Target="SquadFirstTeam"}` denendi, ağaç değişmedi; muhtemelen `Template`
(AssetReference) alanı da gerekiyor. Kadro için doğru event hâlâ aranıyor.

## 8.14 Genel event göndericisi

Aksiyon yüzeyi kapalı bir küme (275 tip, hepsi düz alanlar), o yüzden aksiyon başına route
yazmak yerine tek route event'i reflection ile kuruyor: referans alanları **binding yolundan**,
geri kalanı string'den parse ediliyor.

```
/send?event=<TypeName>&<Alan>=<değer|yol>
```

Tip adı tam ya da kısa verilebiliyor. Böylece kataloglanan her aksiyon, kataloglandığı anda
erişilebilir hale geliyor — `plugin/FmCliProbe/src/Send.cs`.

### Cevap zinciri (henüz tetiklenmedi)

```
NewsItem.NewsActionData -> NewsActionDataReference { NewsAction, ActionDataReferences,
                                                     DynamicCardId, UniqueId }
NewsResponseActionEvent { NewsItem, ResponseId, ResponseValue, ResponseActionId,
                          ResponseActionDataReferences }
RunNewsActionEvent      { NewsItem, SpeakTo, NewsActionID, DynamicCardID, PortalSourceId }
```

Eksik tek parça: `Action` düğümü bir `DynamicReference` — yani `Dictionary<uint, TypedValue>`,
property id → değer torbası. Aksiyonun **sayısal id'si** ağaçta isimli çocuk olarak görünmüyor;
sözlüğü doğrudan gezip kayıt defteri isimleriyle basmak gerekiyor.

## 8.15 Mesaja cevap verildi, zaman ilerledi

Aksiyonun bütün kimliği `Action` düğümünün sözlüğünde duruyor — ağaç 9 çocuk gösteriyordu,
sözlükte **31 giriş** var:

```
ActionTitle               Cancel Training Camp / Decline Introduction / Accept Current Vision
IsNewsActionButton        hangi event'in gideceğini söylüyor
NewsAction                RunNewsActionEvent için aksiyon id'si
ActionResponseId          NewsResponseActionEvent.ResponseId
ActionResponseValue       NewsResponseActionEvent.ResponseValue
ActionID                  NewsResponseActionEvent.ResponseActionId
ActionDataReferences      List<TypedValue> — **atlanırsa oyun çöküyor**
```

⚠ Alanları elle doldurup `ResponseActionDataReferences`'ı null bırakmak oyunu düşürdü:
`libc++abi: terminating due to uncaught exception of type NativeThreadAbortException`.
İş parçacığı sorunu değil (köprü zaten `MainThreadTick` üzerinde); handler null listeyi geziyor.
Çözüm: alanları tek tek yazmak yerine **aksiyon kaydını olduğu gibi kopyalamak** — `/newsaction`.

```
/newsaction?news=<mesaj yolu>&action=<aksiyon yolu>
```

Sonuç: `NewsHasResponded = True`, oyun ayakta. Beş bloke mesajın dördü cevaplandı
(iki "Decline Introduction", bir "Accept Current Vision", bir "Begin Introduction") ve
**`/next` tarihi ilerletti**: gelen kutusu 08:45 → 11:51 → 12:55. Üst üste çalışıyor.

`fm next`'in gerçek semantiği böylece netleşti: ilerlet → cevap gerekiyorsa dur → cevapla →
ilerlet. Bloke eden mesajı `has-required-actions` ayırt ediyor; `CanRespond` yetmiyor
(cevaplanmış mesajda da `True` kalıyor).

## 8.16 Kadro canlandı: ekranlar isimle adreslenebiliyor

`PanelID` bir `ScriptableObject` — yani `Resources.FindObjectsOfTypeAll` ile **bütün ekran
envanteri** adıyla listelenebiliyor, property kayıt defterinin bir katman üstü:

```
/panels?filter=squad
  1355920640  SquadScreen
  3249775616  InternationalSquadScreen
```

Navigasyon event'i bu yüzden artık elle asset bulmayı gerektirmiyor; `/send` bir `PanelID`
alanını **adıyla** çözüyor:

```
/send?event=OpenSubtabEvent&SubtabPanelID=SquadScreen&MainTabPanelID=SquadScreen
     &ObjectUniqueID=0&SecondaryTab=false
```

Ağaç 4198 → 5732. Hem A takımı hem U19 kadrosu canlandı:

```
..SquadScreenAC.1.1.2.FirstTeamC.0.02.1.4.TileVariables2.1.Items   (42 satır, 27 oyuncu)
  Name = Marco Asensio          PositionCombinedString = M (C), AM (RLC)   Age = 29
  Name = N'Golo Kanté           PositionCombinedString = DM, M (C)         Age = 34
  ...
Team: Name = Fenerbahçe   PlayerCount = 27   CanViewSquadNumbers = True
```

⚠ `GameStateCache.Manager.Club.MainTeam.Players.ListLoader0.<n>` düğümleri **hâlâ iskelet**
(sadece `UniqueId`, `IsPersonFemale`...). Canlanan şey ekranın kendi satır listesi. Yani veri
oyuncu nesnesinde değil, **ekranın bağladığı yerde** birikiyor — `fm squad` o yolu okumalı.

Dolayısıyla canlanan property kümesi = ekranın kolonları (`Name`, `PositionCombinedString`,
`Age`, `Nation`, `AverageRating`, `OverallAppearances`). `Condition`/`Morale` gibi alanlar için
ilgili görünümü/kolon setini açtırmak gerekecek.

## 8.17 Gömülü markup: oyunun kendi parser'ı

Bağlı string'ler metnin önünde base64 bir tıklama-hedefi bloğu taşıyor ve ayırıcı **güvenilir
değil** — `=` dolgusu yalnızca bloğun uzunluğu gerektirdiğinde çıkıyor, bu yüzden
`"...cHJvZmlsZQ==Fenerbahçe"` ile `"...wcm9maWxlN'Golo Kanté"` aynı kuralla ayrışmıyor.
Regex yerine oyunun parser'ı:

```csharp
SI.Translation.EmbeddedDataUtilities.ContainsEmbeddedData(string)  // span almıyor
SI.Translation.EmbeddedDataUtilities.RemoveEmbeddedData(string)    // -> temiz metin
```

`tools/sigdump --method <ad>` bu arayışta eklendi: bir yardımcının hangi tipte durduğunu
namespace tahmin etmeden buluyor.

## 8.18 CLI katmanı

Bilgi plugin'de, biçim CLI'da. Panel yolları her oturumda GUID'lerle değiştiği için hiçbir
sabit yol yok: `/squad` ve `/inbox` ağaçta **şekil** arıyor — alanları dolmuş bir referans —
ve JSON döndürüyor.

```
/squad   -> [{Name, Age, PositionCombinedString, Team}]
/inbox   -> [{NewsTitleString, Sender, NewsItemTimeSent, CanRespond, NewsHasResponded, ...}]
```

`cli/fm` bunları tabloya çeviriyor; çıktı pipe'a giderse sekmeli basıyor (`fm squad | awk`).

İki davranış notu:

- `fm squad` veri yoksa `SquadScreen`'i kendisi açıp tekrar okuyor (canlanma bir-iki kare alıyor).
- `fm next` önce gelen kutusuna bakıyor; cevap bekleyen varsa event'i hiç göndermiyor,
  **bloke eden mesajları listeliyor** — §8.10'da not edilen sessiz başarısızlığın karşılığı.

⚠ Kadro ekranı ülke **adını** bağlamıyor (bayrak kullanıyor), o yüzden `fm squad`'da ülke
kolonu yok. Kondisyon/moral de aynı sebeple yok: canlanan küme ekranın kolon setine eşit.

## 8.19 Oyunu tamamen gizlemek: üç ayrı parça

Headless çalışmıyor, bu ölçüldü. `-batchmode -nographics` ile:

```
süreç: çalışıyor   köprü: ayakta   bindings: unavailable  (4 dakika boyunca)
```

UI boot etmeyince binding ağacı hiç kurulmuyor — bütün veri yolumuz ekranların veriyi
bağlamasına dayandığı için headless bizi veriden kesiyor. **Pencere var olmak zorunda, ama
görünmek zorunda değil.** Görünmemesi için üç ayrı şey gerekiyor ve biri eksik kalırsa oyun
ekranda duruyor:

| Parça | Olmazsa |
|---|---|
| `LSUIElement` (bundle Info.plist) | Dock'ta ikon ve menü çubuğu kalır |
| Pencereli başlatma (`-screen-fullscreen 0`) | Tam ekran kendi Space'ini alır, küçültülemez |
| Pencereyi küçültme (`AXMinimized`) | Pencere ekranda kalır |

İki yanlış yol (ikisini de denedim):

- **⌘H karşılığı** (`set visible of process ... to false`) — `visible` `false` dönüyor ama
  Unity penceresi ekranda kalıyor. Yani bu çağrı FM'de **yanlış pozitif**, §8.2'deki span
  tuzağının aynısı: başarı raporluyor, iş yapmıyor.
- **Pencereyi ekran dışına taşımak** — macOS konumu geri kırpıyor: `{-4000, -4000}` istedim,
  `-600` oldu, pencerenin 40 pikseli görünür kaldı.

Küçültülmüş ve Dock'suz haldeyken veri akmaya devam ediyor (`fm squad` → 27 oyuncu), çünkü
`Application.runInBackground` zaten açık. `fm start` üçünü de yapıyor; `--debug` hiçbirini
yapmaz, pencere normal görünür.

⚠ `LSUIElement` yaması **gölge bundle'a** (`~/fm26_bep/fm.app`) yazılıyor, Steam'in kopyasına
dokunulmuyor. Orijinal plist yanına `.backup` olarak bırakılıyor; geri almak için üzerine
kopyalamak yeterli. `FM_APP` ile başka bir bundle gösterilebilir.

## 8.20 Çeviri tablosu: `TranslationID` kendini metne çeviriyor

```csharp
SI.Translation.TranslationID.TranslatedString   // property, span yok
SI.Translation.TranslationManager.GetTranslationFromID(TranslationID)
```

`PanelID.Title` bir `TranslationID` olduğu için ekran envanteri artık oyunun menülerindeki
adlarla okunuyor: `SquadScreen → "Squad"`. `docs/reference/panels.txt`.

⚠ Envanter **kısmi**: panel'ler Addressables ile talep üzerine yükleniyor, o yüzden
`Resources.FindObjectsOfTypeAll` yalnızca o ana kadar dokunulmuş olanları görüyor
(bu oturumda 39). Tam liste için addressable kataloğunu yüklemek gerekecek.

## 8.21 Property'yi talep üzerine canlandırma — yarısı çalışıyor

Hedef: ekran açmadan tek bir property'yi (ör. `Condition`) ağaca sokmak. Mekanizmanın iki adımı
da span'sız görünüyordu:

```csharp
FM.GamePlugin.GameInteropSubsystem.AddNode(Bindings.Key key, Bindings.Key parentKey, PropertyID property)
FM.GamePlugin.GameInteropSubsystem.OpenChannel(InteropReference reference, PropertyID property, Bindings.Key key)
```

İkisi de hatasız dönüyor, **ama düğüm oluşmuyor**: `rawexists` hedef anahtar için `False`.
Sebebi `AddNode`'un ağaca düğüm yaratmaması — yaratma `Bindings.Create(ReadOnlySpan<char>, NodeFlags)`
üzerinde ve §8.12'deki ölü span köprüsünün arkasında. `Bindings.Set(ref Key, ...)` span almıyor
ama `NodeFlags` parametresi de yok, yani var olmayan anahtarı oluşturması beklenmiyor.

Yani: kanalı açabiliyoruz, kanalın yazacağı düğümü yaratamıyoruz. `/bindprop` route'u bu yarım
mekanizmayı belgelemek için duruyor. Çözüm için denenecekler: `HandlerAccess.Set` ile düğüm
oluşup oluşmadığı (bir `TypedValue` üretmek gerekiyor), `AddAlias`, ya da `Bindings.Create`'e
span'ı farklı bir yolla geçirmek.

Pratik sonuç: veri hâlâ **ekranın bağladığı kümeye** eşit. Kadro ekranında ülke adı yok
(`Nation` düğümü çocuksuz — ekran yalnızca bayrak görselini kullanıyor), kondisyon/moral de yok.
Bunlar için ilgili ekranı/görünümü açtırmak gerekiyor.

## 8.22 Aynı anda tek ekranın verisi bağlı

`fm squad` çalıştırdıktan sonra `fm inbox` **boş** dönüyordu. Veri kaybı değil: `SquadScreen`
açılınca portal ekranının mesaj ağacı çözülüyor. Ağaç yalnızca o an bağlı olanı taşıyor.

Sonuç: her komut **kendi ekranını açabilmek zorunda**. `fm squad` → `SquadScreen`,
`fm inbox`/`fm next` → `PortalScreen` ("Overview"). Boş cevap gelirse ekran açılıp okuma
tekrarlanıyor; dolu gelirse event hiç gönderilmiyor.

Yani ekran geçişi bizim için bedava değil, ama görünür de değil — oyun küçültülmüş halde
ekranları değiştiriyor.

⚠ Zaman aşımı "oyun yok" demek değil. Köprü oyunun ana thread'inde çalıştığı için meşgul bir
kare (yükleme, maç) zaman aşımına benziyor; `fm` artık ikisini ayırıyor:
"FM yanıt vermedi — meşgul olabilir" (exit 75) ile "FM çalışmıyor" (exit 69).

## 8.23 Aksiyon yüzeyinin tamamı: iki katman, ikisi de kataloglu

§8.11'deki "şema statik" bulgusunun aksiyon tarafındaki karşılığı. Oyunun yapabildiği her şey
derlenmiş metadata'da duruyor, oyun çalışmasına bile gerek yok.

| Katman | Nerede | Adet |
|---|---|---|
| UI event'leri | `EventBase<T>` türevleri | 275 |
| Simülasyon event'leri | `FM.GamePlugin.InteropEventIDs` | **680** |
| UI aksiyonları | `FM.GamePlugin.ActionID` | **225** |
| Enum'lar (roller, direktifler, mentalite…) | her yerde | **2160 enum / 19119 üye** |

**Köprü doğrulandı:** §8.15'te bir aksiyon kaydında `Event = 27` görmüştük ve ne olduğunu
bilmiyorduk. `Event` alanı doğrudan bir `InteropEventIDs` değeri:

```
Action.Event = 27           -> InteropEventIDs.EventFmxNewsResponseAction
Action.Event = 1231635054   -> (RunNewsAction tarafı)
```

Yani **bir ekrandaki herhangi bir butonun ne gönderdiğini `/dyn` ile okuyup adıyla
eşleştirebiliyoruz**, ve tersine aksiyonun adını bilince hangi ID'yi göndereceğimizi biliyoruz.
`FM.GamePlugin.EventToPlugin { InteropReference Target; InteropEventIDs ID; TypedValue DataToSend; }`
bu katmanın genel taşıyıcısı; `GameInteropSubsystem.m_handlers` da `Dictionary<ActionID, …>`.

### FM26'nın iki fazlı taktiği enum düzeyinde

Deneyerek aylar alacak şey tek dökümde çıktı:

```
FM.UI.InPossessionRole        57 üye   (InvertedFullBack, WideCentreBack, Mezzala, Regista…)
FM.UI.OutOfPossessionRole     46 üye   (OPPressingFullBack, OPScreeningCentralMidfielder…)
FM.UI.TeamInstructionPhaseID  14 üye   (BuildUp, Progression, FinalThird / HighPress,
                                        MidBlock, LowBlock, PressingTrap…)
FM.UI.TacticTeamInstructions  33 üye   FM.UI.PlayerInstruction  36 üye (PI_*)
FM.UI.TacticalStyles          13 üye   FM.UI.TacticMentalities   8 üye
FM.UI.Formation               72 üye
```

⚠ Rol enum'ları **bit maskesi** (1, 2, 4 … 1.4e17), yani bir pozisyonun uygun rolleri tek
sayıda toplanıyor. Faz ve mentalite düz sayı.

`TacticTeamSelectionDropdownEvent`'in `IsInPossession` alanı taşıması, iki fazın aynı event'le
farklı bayrakla sürüldüğünü gösteriyor.

### Basın toplantısı gelen kutusuyla aynı şekle sahip

`PressConferenceEditSessionReference` (10 property: `JournalistSpeaking`,
`NumberOfQuestionsAsked`, `TotalNumberOfQuestions`, `ConversationRoomAtmosphere`…) artı
`EventFmxPressConferenceAnswerQuestion`, `…NoComment`, `…EarlyExit`, `…StormOut`.

Yani oturum referansı + cevap seçenekleri + seçimi gönderen event — mesaj cevaplamanın
(§8.15) birebir aynısı. Aynı desenle çözülmesi bekleniyor.

Hepsi `docs/reference/COOKBOOK.md`'de alan alan toplandı; ham kataloglar `actions.txt` ve
`enums.txt`.

## 8.24 Katalog artık yetenek: enum'lar isimle, TypedValue çalışıyor, kuru çalıştırma var

§8.23'teki kataloglar `/send` tarafından kullanılabilir hale geldi:

```
/send?event=EventToPlugin&ID=EventFmxPressConferenceAnswerQuestion&dry=1
  -> dry run, NOT sent: FM.GamePlugin.EventToPlugin
       ID = EventFmxPressConferenceAnswerQuestion
```

Üç ekleme:

1. **Enum alanları isimle** çözülüyor (`InteropEventIDs`, roller, direktifler — hepsi enum).
2. **`TypedValue` alanları** scalar'dan kuruluyor. `TypedValue.Create<T>` generic ve arm64'te
   generic il2cpp çağrıları riskliydi (§3'teki `GenericMethod::GetMethod not found` uyarısı),
   **ama çalışıyor** — o uyarı yalnızca *enjekte edilmiş* sınıflar için geçerliymiş.
   `Data = TypedValue '5'` kuruldu.
3. **`dry=1`** — event'i tam kurup payload'ı raporluyor, göndermiyor. Yeni bir aksiyonu kayda
   dokunmadan doğrulamanın yolu.

Negatif testler de yapıldı (§bu projede yanlış pozitif bol):

```
ID=ZzYokBoyleBirSeyZz&dry=1  -> <'ZzYokBoyleBirSeyZz' is not a InteropEventIDs>
ID=ZzYokBoyleBirSeyZz        -> error: alan çözülemedi, gönderilmedi   (oyun ayakta)
```

Yani bir alan çözülemezse event **hiç gönderilmiyor** — yarım payload'la oyunu çökertme
(§8.15'te yaşadığımız şey) bu yoldan tekrarlanamaz.

## 8.25 Kontroller event'lerini kendileri tarif ediyor: `/buttons` ve `/press`

Bir aksiyonun payload'ını nasıl dolduracağımızı şimdiye kadar tahmin ediyor ya da kaydı elle
kopyalıyorduk (§8.15). Buna gerek yokmuş: oyun bu eşlemeyi kendisi tutuyor.

**Mekanizma.** Her UI event tipinin bir `…EventContext` sınıfı var
(`SI.Bindable.Events.EventContext<T>`). Context, event'in her alanı için bir `BindingPath`
taşıyor ve `FillEvent(T)` ile event'i onlardan dolduruyor:

```
SpeakToActionEventContext
    m_responseIdBinding, m_responseValueBinding, m_actionIdBinding, m_actionDataBinding,
    m_responseActionIdBinding, m_responseActionDataReferencesBinding, m_eventBinding, …
```

Context'ler kontrollerin üzerinde `SI.Bindable.EventList` olarak asılı: `SIButton.Events`,
`SIButton.RightClickEvents`, `SIDropdown.SelectionEvents`, `SICheckBox.CheckedEvents`,
`FMPitchPosition.Events`, `FMTacticsPanel.SelectionEvent`… Kontrol kullanılınca listesi
`SI.Core.UIEventQueue.QueueEvents(EventList)`'e gidiyor; `UIEventQueue` bir `GameSubsystem`.

**`/buttons`** yüklü bütün `UIDocument`'leri gezip event listesi taşıyan her kontrolü döküyor:

```
0x3f0d3c000  SIButton  ""  name=MarkAllReadButton
  Events[0] EventToPluginContext  source=…Messages4.0.1.BindingSetterA
      m_targetHandler = -> "human"
      m_dataToSendBinding = -> "team"
      m_id = EventFmxMarkAllHumanMessagesAsRead
```

Filtre: `?type=` (blokta geçen metin, ör. context ya da event adı), `?text=` (etiket),
`?limit=`. Portal ekranında 130, kadro ekranında 236 kontrol var.

**`/press?ptr=<adres>[&list=<ad>][&dry=1]`** kontrolün listesini kuyruğa koyuyor. Payload'ı biz
kurmuyoruz, oyun kuruyor; yarım payload'la çökertme (§8.15) bu yoldan mümkün değil.

Doğrulama:

| Deneme | Sonuç |
|---|---|
| `dry=1` ile "Squad" | ağaç 4198'de kaldı |
| uydurma adres (`0xdeadbeef`) | `error: no control at …`, oyun ayakta |
| "Squad" → "Portal", üç tur | her turda 4198 → 6093 → 4198, `/squad` 49 satır → boş |
| "Tümünü okundu işaretle" (`EventToPluginContext`) | "Unread (10)" → "Unread (0)"; `dry=1`'de 10'da kaldı |

Sonuncusu §8.23'teki simülasyon katmanının (`EventToPlugin` + `InteropEventIDs`) ilk canlı
gönderimi. Tek gözlem — işlem tekrarlanamıyor.

**`EventToPlugin`'in gerçek şekli** de buradan okunuyor: `Target` bir binding yolundan
(`"human"`), `DataToSend` bir başkasından (`"team"`, `"selectedadviceoption"`), `ID` sabit.
Yani simülasyon event'leri de çoğunlukla "kime + hangi veriyle" ikilisi.

⚠ Kısıtlar:

- Adresler ekran yeniden kurulunca değişiyor; nav bar gibi kalıcı parçalarınki değişmiyor.
  `/press` adresi her çağrıda taze gezintide arıyor, bayat adres hata dönüyor.
- Yalnızca **yüklü** ekranın kontrolleri var. Bir ekrandaki butona basmak için önce o ekrana
  gitmek gerekiyor — ama gitmek de bir butona basmak.
- Bağlanmamış alanlar `-1` görünüyor (`m_newsActionId = -1`): context bağımlılıklarını
  kuyruğa girince çözüyor, listelemede henüz çözülmemiş olabilir.
- Buton metinlerinde `<link>`/`<style>` markup'ı kalıyor.

Basın toplantısı için bunun anlamı: cevaplar `SpeakToActionEvent` ile gidiyor, onu da inbox'la
aynı modül (`NewsActionHandlerModule`) işliyor; `SpeakToActionReference`'ın 31 property'si
§8.15'teki 31 girişli aksiyon kaydının ta kendisi. Toplantı ekranı açıldığında cevap butonları
`SpeakToActionEventContext` olarak listelenmeli. Henüz canlı görülmedi.

## 8.26 Kondisyon ve moral: veri tablonun görünümünde

§8.21'den beri açık olan soru: kondisyon/moral neden ağaçta yok? Çünkü kadro ekranının açılış
sayfası ("Overview") bir özet panosu; asıl tablo **"First Team"** alt sekmesinde ve tablo
yalnızca **seçili görünümün** kolonlarını bağlıyor.

"First Team"e basınca ağaç 4198 → ~11100 düğüm. Görünüm seçici `ViewDropdown` adlı bir
`SIDropdown`, 14 seçenekli:

```
Selection Info · General Info · Attributes › · Contract · Transfer · Home-Grown Status ·
Playing Time · Dynamics · Reports · Injuries · Risk Assessment · Stats › · International ·
Development
```

| Görünüm | Satırda gelen |
|---|---|
| Selection Info | `Condition`, `Sharpness`, `Morale`, `PlayingTimeHappinessLevel`, `Goals`, `AverageRating`, son beş maç |
| General Info | `Wage`, `AgreedPlayingTime`, `TransferValueRangeString`, `CoachBestRole`, `CoachBestPosition`, yıldızlar, sözleşme bitişi |
| Dynamics | `Morale`, `MoraleString`, `HierarchyInfluenceCategoryString`, `SocialGroupString`, `PromisesCount`, `HierarchyOverallHappinessLevel` |

Diğer görünümler seçilebiliyor (hepsi denendi, etiket ve ağaç boyutu değişti) ama satırları
henüz okunmadı.

**Açılır liste event taşımıyor.** Görünüm değiştirmek bir event göndermiyor, bağlı bir değere
(`…BindingVariables0.selectedView`) yazıyor; o yüzden `/buttons`'ta görünmüyor. İki route:

```
/dropdowns[?text=<metin>]                          listeler, seçenekleri ve yazdıkları yolla
/select?name=<liste adı>&choice=<seçenek>[&dry=1]  ya da ?ptr=<adres>&index=<n>
```

Seçim `SIDropdown.OnSelectionChangedInternal(int)` üzerinden — açık listede bir satıra
tıklamanın vardığı yer. Seçenek adı kaydın `RowData` girişinin içinde; `ChildList` taşıyanlar
alt menü (`Attributes ›`, `Stats ›`).

**Etiketle adresleme.** Adresler ekranla birlikte öldüğü için `/press?text=<etiket>[&name=<ad>]`
eklendi. Etiket tek bir kontrole uymuyorsa basmıyor, adayları listeliyor.

**Satır birleştirme.** Tablo bir oyuncuyu hücrelere dağıtıyor: her hücre aynı `PersonReference`'ı
bir iki alan için ayrı bağlıyor (`Morale` bir hücrede, `Condition` satırın kendisinde). `/squad`
artık adı bağlı düğümün çevresindeki bütün değerleri topluyor; hangi alanların geleceğini isimle
sormuyor, yerleşimin kendi değişkenlerini (`Is…`, `Can…`, küçük harfle başlayanlar) eliyor. Özet
panosu tabloyla aynı anda bağlı kaldığı için aynı oyuncu iki listede çıkıyor; kayıtlar isimle
birleştiriliyor.

⚠ Satırı tanıtan alan görünüme göre değişiyor: çoğunda `PositionCombinedString`, Dynamics'te
yalnızca `SquadNumberBlankIfInvalid`. İlk denemede Dynamics satırları bu yüzden hiç görünmedi.

Doğrulama (her biri oyun yeniden başlatıldıktan sonra):

| Deneme | Sonuç |
|---|---|
| `fm squad --fitness`, portal ekranından | 27 oyuncu, 4,8 sn |
| `fm squad --morale`, portal ekranından | 27 oyuncu, 6,5 sn |
| fitness → inbox → morale → düz kadro, üç tur | her turda 27 / 12 / 27 / 47 satır, aynı ilk satırlar |
| `/select` geçersiz indeks, olmayan seçenek, `dry=1` | hata ya da değişiklik yok |
| `/press?text=ZzYokZz` | `error: no control labelled …` |

`Condition = -1` sakat oyuncu demek. İki oyuncuda görüldü ve ikisi de "Injuries" görünümünde
sakat: Talisca (`InjuryDescription = A moderate hamstring strain`) ve Rodrigo Becão
(`A torn ACL`); ikisinin de `StatusTitleShort`'u `Inj`, tabloda başka `Inj` yok. CLI kondisyonu
"-" basıp durumu ayrı kolonda gösteriyor. `StatusTitleShort` oyuncunun ilk durum bayrağı
(`Inj`, `Wnt`, `Hol`…) ve her görünümde geliyor.

Aynı turda okunan diğer görünümler: Injuries (`InjurySeverity`, `InjuryDescription`,
`InjuriesTimeMissed`), Risk Assessment (`OverallInjuryRisk`, `InjurySusceptibility`, `Load`,
`MatchLoad`), Playing Time (`AgreedPlayingTime`, `ActualPlayingTime`,
`FutureAgreedPlayingTime`, `PlayingTimeHappinessLevel`).

## 8.27 CLI'dan gezinme, ve menülerin sakladığı ekran haritası

§8.25 ve §8.26'daki route'lar CLI'a çıktı:

```
fm buttons [metin] [--all]          etiketli kontroller ve ne gönderdikleri
fm press "<etiket>" [--name <ad>]   butona bas
fm dropdowns [metin]                açılır listeler ve seçenekleri
fm select <liste> "<seçenek>"       listeden seç
```

`--dry` ikisinde de var; `--json` listelerde. Plugin tarafında `/buttons?json=1` ve
`/dropdowns?json=1`. "Ne gönderir" kolonu context adının kısası; simülasyon taşıyıcısında
event'in adı da geliyor (`EventToPlugin:EventFmxQuickPick`).

Üç pratik ayrıntı:

- **Etiketlerde rich-text etiketi var** (`<color><link="0"><style="Norml">Talisca…`).
  `EmbeddedDataUtilities` bunlara dokunmuyor, ayrıca ayıklanıyor; yoksa oyuncu adıyla buton
  bulunamıyor.
- **Aynı etiket birden çok kontrolde olabiliyor** ("All": bir buton, bir de eğitim katmanı).
  `/press` o durumda basmıyor, adayları adlarıyla listeliyor; `--name` ile daraltılıyor.
- **Ekran bir basıştan sonra hemen hazır değil.** `press "Squad"` ardından `press "First Team"`
  ilk denemede "yok" dönüyordu; CLI "yok" cevabında 8 sn'ye kadar yeniden soruyor.

### Menüler bütün ekranları sayıyor

Her ana sekmenin (`Portal`, `Squad`, `Recruitment`, `Match Day`, `Club`, `Career`) bir
`NavTabDropdown`'u var ve ekran açık olmasa da alt sayfalarını listeliyor: toplam 77 sayfa.
`Recruitment` altında Player Database, Shortlists, Transfer Activity, Contracts; `Match Day`
altında Tactics, Set Pieces, Opposition Instructions; `Club` altında Finances, Staff, Board…
Tam liste `docs/reference/screens.txt`. §8.20'deki 39 panellik envanterin yerini alıyor.

### ⚠ Ama menüden açılmıyor

| Deneme | Sonuç |
|---|---|
| `/select` ile menü satırı (Finances, Tactics, Messages) | `SelectedIndex` değişti, ekran değişmedi |
| ardından menünün `SelectionEvents`'ini `/press` ile kuyruğa koymak | kuyruğa girdi, ekran değişmedi |
| "Dynamics" açılır alt sekmesinin `LeftClickEvents`'i | kuyruğa girdi, ekran değişmedi |

Seçim gerçekten yazılıyor: `…NavigationTabPortalBindingVariables12.selectedSubtabElement`
artık `{DisplayValue = "First Team", PropertyValue = DynamicReference}`. Menünün event'i
(`OpenSubtabEventContext`) panelini `selectedSubtabElement.PropertyValue`'dan okuyor, oysa o
değer panel değil, iç içe bir kayıt. Her menü satırı ayrıca bir `ChildList` taşıyor. Yani
açılacak sayfa büyük ihtimalle bir kat aşağıda — doğrulanmadı.

### ⚠ Çözülemeyen bir event kuyruğu tıkıyor

İkinci denemenin bir yan etkisi vardı: menünün `SelectionEvents`'i kuyruğa konunca içindeki
`OpenSubtabEventContext` gönderilemiyor ve **kuyruğun başında kalıyor**. Arkasındaki her şey
bekliyor — bizim basışlarımız da, oyunun kendi tıklamaları da.

```
/queue  ->  1 event(s) waiting
              head: OpenSubtabEventContext  status=Sending  sendable=False
```

Dışarıdan görünüşü: `/press` "queued" diyor, ekran değişmiyor. `/send` ile giden event'ler
kuyruğu kullanmadığı için çalışmaya devam ediyor (`fm inbox`, düz `fm squad`), bu yüzden fark
edilmesi gecikti: ilk olayda Portal, Squad, First Team, Under 19s ve bir pano kutusu art arda
basıldı, hiçbiri ekranı değiştirmedi; oyun yeniden başlatılınca düzeldi.

İki önlem:

- **`/queue[?clear=1]`** kuyrukta bekleyeni gösteriyor ve boşaltıyor.
- **`/press` tıkalı kuyruğa basmıyor.** Kuyruk her karede boşaldığı için, bir istek geldiğinde
  hâlâ dolu olması tıkanma demek; hata dönüyor ve baştaki event'i söylüyor.

Doğrulama: temiz başlangıçta sırayla denendi. Menü satırını seçmek kuyruğu tıkamadı (0 bekleyen,
sonraki basış çalıştı); `SelectionEvents`'i kuyruğa koymak tıkadı (1 bekleyen), sonraki
`fm press Squad` reddedildi; `clear=1` sonrası Squad → 6098, Portal → 4206 düğüm, yani basışlar
geri geldi. Aynı tıkanma sonraki üç denemede **tekrar üretilemedi** (aynı context ikinci kez
kuyruğa girmedi), dolayısıyla tıkanma iki kez, temizleme bir kez gözlendi.

Ders: `/press`'i yalnızca kontrolün kendi ana listesiyle (bir butonun `Events`'i) kullan.
Verisi başka bir etkileşimle dolan listeleri (`SelectionEvents`) elle kuyruğa koyma.

(Sonradan çözüldü, §8.28: menünün verisinden `OpenBookmarkEvent` kurarak.)

Çalışan gezinme yolları: ana sekme butonu (`OpenPanel`), düz alt sekme butonu ("First Team",
`OpenSubtab`), pano kutusu ("Medical Centre", `OpenPanel`) ve §8.16'daki elle
`OpenSubtabEvent`.

## 8.28 Sayfayı adıyla açmak: menü satırı panelin GUID'ini taşıyor

§8.27'de açık kalan soru. Menü satırlarının içi (`/dropdowns?deep=1`):

```
Club Vision
  RowData
    DisplayValue = Club Vision
    PropertyValue
      Value          = eeeab88fa446f5147a744eefc51bf031     panel asset'inin GUID'i
      SubtabText     = Club Vision
      PanelID        = 2362315008
      OpensCard      = False
      ObjectUniqueID = -1
  ChildList = (çoğunda boş)
```

Yani her satır açacağı paneli iki şekilde söylüyor: asset GUID'i ve panel id'si. Menünün ilk
satırı sekmenin kendi ekranı (`PanelID = 3706171392` = `ClubScreen`). `ChildList` her satırda
var ama yalnızca birkaçında dolu (Finances, Supporters, Job Centre Report) — §8.27'deki "asıl
sayfalar bir kat aşağıda" tahmini yanlıştı.

**Neden seçmek yetmiyordu:** 75 menü satırından yalnızca 9'unun `PanelID` nesnesi yüklü.
Paneller ilk ziyarette yükleniyor, o yüzden §8.16'daki `OpenSubtabEvent` (panel *nesnesi*
istiyor) çoğu sayfa için kurulamıyor. `OpenBookmarkEvent` ise asset *referansı* alıyor:

```csharp
OpenBookmarkEvent { AssetReferenceT<PanelID> BookmarkPanelID; AssetReferenceT<PanelID> MainTabPanelID; }
```

`new AssetReferenceT<PanelID>(guid)` interop'tan kurulabiliyor; yüklemeyi oyun yapıyor.
Açılışlardan sonra yüklü panel sayısı 39'dan 49'a çıktı.

```
/pages                                    77 sayfa: sekme, ad, panel id, GUID, kart mı
/open?page=<ad>[&tab=<sekme>][&dry=1]     adıyla aç
fm pages [metin]      fm open "<sayfa>" [--tab <sekme>]
```

Sekme adı menünün ilk satırındaki panelin başlığı; ana sayfanınki "Overview" çıktığı için orada
asset adı kullanılıyor (`PortalScreen` → Portal).

Doğrulama:

| Deneme | Sonuç |
|---|---|
| olmayan sayfa, `dry=1` | hata / ağaç değişmedi |
| "Overview" (altı sekmede var) | reddedildi, altı sekme listelendi; `--tab Club` ile açıldı |
| Finances, Tactics, Player Database, Staff, First Team, Messages, Calendar, Competitions, Training, Club Vision ×2, First Team ×2 | hepsinde ilgili panel aktif, kuyruk boş |
| fitness → inbox → düz kadro → morale → düz kadro, üç tur | 27 / 12 / 27 / 27 / 27 satır, her turda |

⚠ Gözlemler:

- Sayfa bazen bir öncekinin **üstüne kart** olarak açılıyor (ekmek kırıntısı "Calendar ›
  Competitions"); alttaki sayfanın verisi de bağlı kalıyor. `OpensCard = True` olanlar (33 sayfa)
  zaten kart.
- Menüler açılıştan birkaç saniye sonra doluyor; `fm open` o arada "yok" cevabında bekliyor.
- Panel başlığı sayfa adından farklı olabiliyor (Tactics → "Tactics Planner", Staff →
  "Staff Report").

### `fm squad` hangi liste bağlıysa onu okuyordu

`/squad` ağaçta adı bağlı her oyuncuyu topluyor. Özet panosu açıkken 47 kişi (A takım + U19),
yalnızca tablo açıkken 27 kişi geliyordu; `/open` panoyu atlayıp doğrudan tabloya gidince düz
`fm squad` önceki komuta göre farklı sonuç vermeye başladı. Artık o da adı belli bir görünümü
okuyor ("General Info": yaş, statü, maaş, piyasa değeri). Altyapı kadrosu bu yüzden çıktıdan
düştü; "Under 19s" sayfasından ayrıca okunması gerekiyor.

## 8.29 Mesaj cevaplamak bir butona basmak: `fm reply`

Mesaj listesindeki her satır bir buton (`NewsItemButton`, `SetNewsActionDataOnPanelEvent`).
Basınca mesaj açılıyor ve **cevapları buton olarak** beliriyor:

```
Begin Introduction     SIButton  SpeakToAction
Decline Introduction   SIButton  SpeakToAction
```

Yani inbox cevabı da `SpeakToActionEvent` — §8.25'te basın toplantısı için beklenen event.
Context, §8.15'te elle çıkarmaya çalıştığımız eşlemeyi açıkça veriyor:

```
m_eventBinding                        -> action.Event
m_responseIdBinding                   -> action.ActionResponseId
m_responseValueBinding                -> action.ActionResponseValue
m_actionIdBinding                     -> action.ActionID
m_responseActionIdBinding             -> action.NewsAction          (ActionID değil)
m_responseActionDataReferencesBinding -> action.ActionDataReferences
m_npcDataBinding -> NPC      m_speakToBinding -> SpeakTo
```

`/newsaction` `ResponseActionId`'yi `ActionID`'den dolduruyordu; oyunun kendi eşlemesi
`NewsAction`. O route silindi, yerini kontrolün kendisine basmak aldı.

```
fm inbox                    mesajlar artık numaralı
fm reply <# | konu>         mesajı aç, cevaplarını listele; terminaldeyse seçimi orada sor
                            (numara ya da metin, x: vazgeç); script'te yalnızca listeler
fm reply <#> <no | "cevap"> cevapla: listedeki numarasıyla ya da metninin bir parçasıyla
                            (--dry ile yalnızca söyler)
```

**Hangi mesaj açık?** Ekrandaki cevaplar o an açık mesaja ait; yeni mesaj açılırken bir an
öncekinin cevapları duruyor. `HumanPortalLastSelectedMessage` açık mesajın `UniqueId`'sini
tutuyor; `fm reply` cevapları ancak o değer istenen mesaja eşitlenince okuyor, sonra iki okuma
üst üste aynı gelene kadar bekliyor (cevabı olmayan mesaj hiç dolmuyor).

**Aynı liste iki kez ekranda olabiliyor.** Messages sayfası portalın üstüne kart olarak
açılınca her mesajın iki `NewsItemButton`'ı oluyor; ikisi de aynı event'i gönderiyor. `/press`
buna `any=1` denirse razı oluyor, kendiliğinden seçmiyor.

**Bloke eden mesaj `NewsItemIsPriority` olan.** `CanRespond` yetmiyor: "Pre-season
Preparation" cevaplanabilir (tek seçenek: Cancel Training Camp) ama öncelikli değil ve oyunun
"Tasks" sayacında yok. CLI onu "cevaplanabilir" diye gösteriyor.

**Oyunun ilerle butonu neyi beklediğini yazıyor.** `continue-button`'ın etiketi duruma göre
"Continue", "Response Required" ya da "Complete Introduction". `fm next` bloke olduğunda bunu da
basıyor.

Doğrulama:

| Deneme | Sonuç |
|---|---|
| `reply 4`, `5`, `6`, `7`, `8` listeleme | her mesajın kendi seçenekleri (3 / 1 / 2 / 2 / 1), ham buton verisiyle aynı |
| `reply 1` (cevabı yok) | "cevap seçeneği sunmuyor", 3,8 sn |
| `reply 99`, `reply Fenerbahçe` (7 mesaja uyuyor), `reply 5 "Zzz"` | üçü de reddedildi |
| `reply 7 Decline --dry` | bekleyen sayısı değişmedi |
| `reply 7 Decline`, `reply 6 Decline`, `reply 4 "Accept Current"` | bekleyen 5 → 4 → 3 → 2, kuyruk boş |

### Tanıtım turu başlıyor, bitmiyor — çözüldü (§8.30)

`reply 5 Begin` ("Fenerbahçe Tactics Introduction") mesajı kapatmıyordu: tur paneli açılıyor,
mesajdaki buton "Continue Introduction"a dönüyor, ona basmak hiçbir şeyi değiştirmiyordu. Sebep
§8.30'da: o tur ekranda bir şey yapılmasını bekliyordu, mesajdaki buton turu ilerletmiyor.

## 8.30 Tanıtım turları ve taktik sihirbazı

**Tur, adımlarını sayan bir konuşma.** "Begin Introduction" bir panel açıyor; panelin
`SpeakTo` referansı `CurrentInductionStepIndex` / `MaxInductionStepIndex` taşıyor, rehberin
söylediği de yanındaki `SpeechBubbleList…bubble.DisplayString`'te. Turun kendi butonları var
ve mesajdakilerden **adlarıyla** ayrılıyor (`name=StandardButton`):

```
Continue            sonraki adım            (son adımda yok)
Previous            önceki adım
Skip Introduction   turu atla
End Introduction    yalnızca son adımda; mesajı kapatan bu
```

Mesajın içindeki "Continue Introduction" butonu turu ilerletmiyor, yalnızca tur panelini geri
getiriyor — dün on kez basıp sonuç alamamamızın sebebi buydu.

**Bazı adımlar senden iş bekliyor.** Taktik turu tek adım (1/1) ve hiçbir tur butonu
sunmuyor; rehber "select the tactical display shown" diyor. Takımın taktiği yok
(`Tactic.HasTactic = False`) ve tur ancak taktik oluşturulunca kapanıyor. "Decline" sunmayan
tek tur olması da bundan: taktiksiz devam edilemiyor.

**Tur açmadan da olur.** Taktik sihirbazla oluşturulunca "Tactics Introduction" mesajı
cevaplanmış sayılıyor; "Begin"e hiç basmak gerekmiyor.

**Taktik sihirbazı üç seçim, üç buton:**

| Aşama (`TacticCreatorCurrentStage`) | Seçilen | Liste | İlerleten buton → event |
|---|---|---|---|
| 0 | stil (11 seçenek) | `tac-styles-options` | "Formation (In Possession)" → `EventFmxTacticCreatorIncrementStage` |
| 1 | topla diziliş | `all-options-list` | "Formation (Out of Possession)" → aynı event |
| 2 | topsuz diziliş | `all-options-list` | "Confirm" → `EventFmxConfirmedCreateNewTactic` |

Dizilişlerin listesi seçilen stile göre değişiyor (Gegenpress 12 / 10, Control Possession
farklı sıra), yani önceden listelenemiyor. İlerleten buton seçim yapılana kadar kapalı.
Sihirbaza giriş "No Tactic Set" kutusu: tur panelinde ve Match Day › Overview'da var.
`fm open Tactics` taktik yokken boş bir panel açıyor, sihirbazı değil.

**Seçenekler radyo grubu.** Stil ve diziliş kutuları event listesi taşımıyor, o yüzden
`/buttons`'ta yoklar: `SIRadioGroup` içinde `SIRadioButton`. Açılır liste gibi bağlı bir değere
yazıyorlar. Seçmek = kutunun kendi tıklama işleyicisi (`SIRadioButton.OnClickedOrSubmitted()`).
`/dropdowns` ve `/select` artık radyo gruplarını da kapsıyor. Geçilen aşamanın grubu ekranda
kalıyor ama etiketleri boşalıyor; bunlar listelenmiyor.

**Etiketin etrafına bakmak: `/elements`.** `/buttons` yalnızca event listesi taşıyan
kontrolleri görüyor. `/elements?text=Gegenpress&up=11&depth=1&members=1` etiketin çevresindeki
eleman ağacını tipleriyle döküyor; radyo grubu böyle bulundu. Event taşımayan bir şeye
"nasıl basılır" sorusunun aracı.

```
fm tour               açık turu sonuna kadar yürüt, her adımda rehberin dediğini bas
fm tour --skip        turu atla (tur sunuyorsa)
fm tactic new         sihirbazı aç, stilleri listele
fm tactic new "<stil>" "<topla>" "<topsuz>"   taktiği oluştur; eksik bırakılan aşamanın
                      seçeneklerini listeler. Seçim: numara, tam ad ya da tek uyan parça
```

Doğrulama (temiz kayıttan iki ayrı oturum):

| Deneme | Sonuç |
|---|---|
| `tactic new` (tur açık değil, Messages sayfasından) | sihirbaz açıldı, 11 stil |
| `tactic new Zzz` / `Tiki` (iki stile uyuyor) / `Gegen --dry` | red / red / `TacticCreatorTacticalStyle` −1 kaldı |
| `tactic new Gegen` → `… "4-2-3-1"` → `… "9-9-9"` → `… "4-1-4-1"` | aşama aşama ilerledi, uydurma diziliş reddedildi, taktik oluştu |
| ikinci oturum: `tactic new "Control Poss" 4 3` | tek komutta oluştu: Control Possession · 4-3-3 DM Wide · 4-1-4-1 DM |
| taktik varken `tactic new` | "zaten bir taktiği var" |
| `reply 7 Begin` + `tour` (Squad, 7 adım) | 7 adım basıldı, mesaj kapandı, 8 sn |
| `reply 6 Begin` + `tour --skip` | mesaj kapandı |
| taktik turunda `tour` / `tour --skip` | "senden bir şey bekliyor, `tactic new`" / "atlanamıyor" |
| elle: Dynamics turu (6 adım) Continue ×5 + End | adım sayacı 1→6, mesaj kapandı |

**Zaman ilerledi.** Dört mesaj cevaplanınca `fm next` iki oturumda da çalıştı: saat 08:45 →
11:51, ikinci basışta 30/6 → 1/7/2025. Sonraki engeller sırayla: "Set Code of Conduct" (üç
cevap, `fm reply` ile geçildi) ve "Dynamics Introduction" turu.

**İlerle butonu her zaman "ilerle" değil.** Etiketi bir kez "News Site" oldu; basınca zaman
ilerlemedi, haber sayfası açıldı. `fm next` etikete bakmıyor.

**`fm reply` cevabın işlenmesini bekliyor.** Gelen kutusu cevaptan bir an sonra güncelleniyor;
hemen ardından gelen `fm next` mesajı hâlâ engel sanıyordu. Artık mesaj engel olmaktan çıkana
kadar (8 sn'ye kadar) bekliyor.

## 8.31 Taktiği okumak ve değiştirmek; sayfayı verisiyle açmak

**⚠ `OpenBookmarkEvent` sayfayı açıyor ama verisini bağlamayabiliyor.** §8.28'de "ilgili panel
aktif" diye doğrulanan Tactics sayfası aslında boş geliyordu: planlayıcı bir takım ve taktik
referansı bekliyor, bookmark bunları vermiyor. Dün yalnızca panelin açıldığına bakılmıştı.

| Açılış yolu (iki tur, her seferinde Messages'tan) | `Tactic.MentalityString` |
|---|---|
| `OpenBookmarkEvent` (eski `fm open Tactics`) | bağlanmadı |
| bookmark, ardından "Tactics" alt sekme butonu | bağlanmadı (zaten o sayfadayken buton bir şey yapmıyor) |
| sekmenin ilk sayfası (Match Day › Overview), ardından "Tactics" alt sekme butonu | bağlandı |

`fm open` artık böyle çalışıyor: sekmenin ilk sayfasını bookmark'la açıyor, alt sekme çubuğu o
sekmeninkine dönene kadar bekliyor (iki sekmede aynı adlı sayfa olabiliyor), sonra sayfanın
kendi butonuna basıyor. Yalnızca "More" menüsünde olan sayfaların butonu yok; onlar yine
bookmark'la açılıyor ve verileri bağlanmayabilir. `/press` bunun için `event=` süzgeci aldı:
aynı etiketi taşıyan kontrolleri gönderdikleri event ayırıyor.

**Taktik, planlayıcının bağladığı veriden okunuyor** (`/tactic`): başlık `Tactic` düğümünde
(`Name`, `MentalityString`, `TacticalStyle`, `TacticalIntensity`, `TacticalFamiliarity`), on bir
mevki `Position` kayıtlarında:

```
TeamSelectionIndex  0..10        IPPosition / OOPPosition   PositionMask bit maskesi
IPRole / OOPRole    bit maskesi  yanında PlayerRole = RoleDataEntry adı (topsuz olan "OP…" ile başlıyor)
player-name         seçili oyuncu (11 seçilmemişse boş)
```

`PositionMask`: 1 GK, 4 DR, 8 DL, 16 DC, 32 WBR, 64 WBL, 128 DM, 256 MR, 512 ML, 1024 MC,
2048 AMR, 4096 AML, 8192 AMC, 16384 ST; ortadaki mevkiler ayrıca 1048576 (sol) ya da 2097152
(sağ) taşıyor (`2097168` = sağ stoper).

**Ayarlar açılır liste, ilk 11 tek buton.** Mentalite ve stil planlayıcıda adsız iki açılır
liste (`dropdown-base-no-background`); hangisi olduğu yalnızca kendisinde bulunan bir seçenekten
anlaşılıyor. Seçmek doğrudan uygulanıyor, onay sormuyor. Stili değiştirmek taktiğin adını ve
yoğunluğunu da değiştirdi (Gegenpress 5, Tiki-Taka 4). "Quick Pick" butonu
(`EventFmxQuickPick`) boş on biri doldurdu.

```
fm tactic                     taktiği bas (--json ham veri)
fm tactic mentality [<x>]     göster / değiştir        fm tactic style [<x>]
fm tactic pick                ilk 11'i oyuna seçtir
```

| Deneme | Sonuç |
|---|---|
| taktik yokken `fm tactic` | "taktiği yok", 12 sn |
| `tactic new 2 3 3` sonrası `fm tactic` (Messages'tan) | 11 satır, roller ekrandaki rol butonlarıyla aynı, 2,5 sn |
| `mentality` / `Zzz` / `"Very Att" --dry` | liste / red / değişmedi |
| `mentality Attacking`, `mentality 4`, `mentality Balanced` | Positive → Attacking → Balanced, üçüncüsü "zaten" |
| `style Gegen` | Tiki-Taka → Gegenpress, taktik adı da değişti |
| elle Quick Pick (11 boşken) | 11 oyuncu geldi; `tactic pick` dolu on birde aynı on biri bildirdi |
| `open` gerilemesi: First Team, Finances, Club Vision, Match Rules, Fixture Schedule (iki sekmede) | açıldı / belirsiz olan reddedildi; `squad --fitness`, `inbox` aynı |

**Rol değiştirme: `fm tactic role`.** Planlayıcıda mevki başına iki liste var (topla / topsuz),
event'i `RoleOptionEvent`. Satırın gösterdiği şey düz metin değil kayıt: `{ TacticsRoleName,
Role (bit maskesi), Selected, StarRange (oyuncuya uygunluk) }`; son iki satır "Go to Role
Selection" ve "Go to Player Instructions". 22 liste mevki sırasıyla geliyor ama üstünde hangi
mevkinin listesi olduğu yazmıyor; komut, listenin `Selected` işaretlediği rolü taktikteki rolle
karşılaştırıp uyuşmazsa dokunmuyor. Seçim: satırı seç + listenin event'ini kuyruğa koy
(`/select?…&fire=1`).

| Deneme (temiz kayıt, yeni taktik) | Sonuç |
|---|---|
| `tactic role` / `role ZZ` / `role DM` | mevki listesi / red / "DMR, DML'ye uyuyor" |
| `role GK`, `role GK --oop` | üçer rol, yürürlükteki işaretli (Goalkeeper / Sweeper Keeper) |
| `role GK Ball --dry`, `role GK Ball`, tekrar | değişmedi / Goalkeeper → Ball-Playing Goalkeeper / "zaten" |
| `role DML Dropping --oop`, `role ST 1` | topsuz rol ve forvet rolü değişti; `fm tactic` üçünü de yeni haliyle gösterdi |
| elle (önceki oturum): aynı listeden ikinci satır | aynı değişim |

**İlk 11'e oyuncu koymak: `fm tactic player`.** Her mevkinin bir seçim listesi var
(`TacticTeamSelectionDropdownEvent`): üstte önerilen oyuncu, altında "All Players ›" alt menüsü.
Satırın gösterdiği kayıt `{ Player, Index (oyuncunun kendi numarası), TeamSelectionIndex (dolduracağı
mevki), PlayerTeamSelectionIndex (şu an nerede; −1 kadroda değil), StarRange }`. Satırda isim yok;
satır "şu oyuncu şu mevkiye" diye iki sayıyla bulunuyor (`/pickplayer?slot=&player=`), sonra alt
menü kalıbıyla seçiliyor (bağlı değere yaz + event'i kuyruğa koy). Maç kadrosu
`tacticsteamselection` kayıtlarında: `Index` 0-10 ilk on bir, sonrası yedekler; `PlayerIndex` ve
yalnızca yedeklerde `Player.ShirtName`.

| Deneme | Sonuç |
|---|---|
| `fm tactic` | tablonun altında "Yedekler: Mert Günok, Söyüncü, Kanté, D. Nene, Mert Müldür, Fred, Brown" |
| `tactic player` / `player ST zzz` / `player DML Fred --dry` | kullanım + yedekler / red / değişmedi |
| `tactic player DML Fred` | "DML: Guendouzi → Fred"; `fm tactic` Fred'i on birde, Guendouzi'yi yedekte gösterdi |

Yalnızca yedekler adıyla seçilebiliyor: ilk on birdekilerin ve kadro dışındakilerin adı bu
ekranda bağlı değil, o yüzden iki as oyuncunun yerini değiştirmek ya da kadro dışından birini
almak henüz yok.

Açık kalanlar: diziliş değiştirme, takım talimatları, taktik yuvaları
(planlayıcıda "Create New Tactic · Load Tactic · Copy Tactic" ve "Save · Load… · Clear Slot"
listeleri var), "Pick Rotated Squad" gibi diğer seçim menüsü satırları.

## 8.32 Oyunun durumu sabit bir yolda: `fm next` ve `fm status`

Ekranların verisi GUID'li panel yollarının altında ve ekrana göre gelip gidiyor. Ama kökte iki
kayıt **hangi sayfa açık olursa olsun** bağlı ve yolları sabit (`PathKey` ile okunuyor):

```
continue.State                     ContinueFlowState: oyun neyi bekliyor
continue.ContinueButtonMainString  ilerle butonunun yazısı
game.GameProcessing / GameSaving / GameWaiting / CanSave / IsDuringMatch
game.CurrentGameDate               paketlenmiş tarih
game.ImmediateUserRequiredTask     sıradaki zorunlu iş (NewsItemId, DynamicCardId)
```

`ContinueFlowState` 33 değerli (`enums.txt`): `CanContinue`, `NeedAction`, `PressConference`,
`Tactics`, `TeamNews`, `TeamTalk`, `TunnelInterview`, `Processing`, `Match`, `Saving`,
`SubmitTeam`, `PostMatchReaction`, `NewsAggregator`, `LatestScores`… Yani maç gününün bütün
durakları tek alandan okunabiliyor; gelen kutusundan ya da buton yazısından çıkarım gerekmiyor.

**Tarih tek sayı.** `GameDate.Data1`: bit 17 ve üstü yıl, sonraki 9 bit yılın günü (1'den),
alt 8 bit saat. `265467401` → 2025, 182. gün (1 Temmuz), saat 9. Saat birimi çeyrek saat ve
05:45'ten sayılıyor gibi: 9 → 08:00, 13 → 09:00, 17 → 10:00, 30 → 13:15. Bu dört durağın
saatine çevresindeki mesajlardan bakılarak yapılmış bir **uydurma**, oyundan okunmuş değil.

**`fm next` artık bekliyor ve anlatıyor** (temiz kayıttan, üç mesaj cevaplandıktan sonra):

```
$ fm next 5
30 Haziran 2025 Pazartesi 09:00 → 12:00
  yeni: Super League registration window open
  yeni: Fenerbahçe player contracts set to expire
  yeni: Fenerbahçe staff contracts set to expire
  yeni: Fenerbahçe Home Grown Status Update
İlerle denildi ama ne saat ne gelen kutusu değişti.
30 Haziran 2025 Pazartesi 12:00 → 13:15
  yeni: Match Preparation Training: Sarıyer
İlerle denildi ama ne saat ne gelen kutusu değişti.
30 Haziran 2025 Pazartesi → 1 Temmuz 2025 Salı 08:00
  yeni: Set Code of Conduct  (cevap bekliyor)
Bloke: 1 mesaj cevap bekliyor.
  Now  Set Code of Conduct
Cevaplamak için: reply <#>   (numaralar için: inbox)
```

Beş basışın ikisi hiçbir şeyi değiştirmedi; neden olduğu bilinmiyor, komut bunu olduğu gibi söylüyor.

İlerle'ye basıyor, oyun bir şeyi değiştirene kadar (4 sn), sonra iki okuma üst üste boşta
görünene kadar (3 dk'ya kadar) bekliyor; tarih/saat farkını ve gelen yeni mesajları basıyor.
`next <n>` bir şey engel olana kadar n kez tekrarlıyor.

⚠ İki tuzak:

- **Yükledikten hemen sonra `State = CanContinue`**, oysa dört mesaj cevap bekliyor; alan ilk
  gerçek durağa kadar güncellenmiyor. `next` ve `status` bu yüzden gelen kutusuna da bakıyor.
- **`NewsAggregator` gibi duraklarda ilerle butonu sayfa açıyor**, zaman ilerlemiyor. Bunlar
  "geçilebilir" sayılıyor (`NewsAggregator`, `Portal`, `LatestScores`, `Fixtures`, `Results`):
  `next` basıp geçiyor ve "oyun önce bir sayfa gösterdi" diyor. Maçla ilgili durumlarda
  (`PressConference`, `Tactics`, `TeamTalk`, `Match`…) basmıyor, yalnızca neyi beklediğini söylüyor.

Temiz kayıttan 1 Temmuz 10:00'a kadar olan bütün yol (taktik oluştur, 11 seç, üç mesajı cevapla,
davranış kurallarını devret, Dynamics turunu reddet) üç ayrı oturumda yalnızca CLI ile yürüdü.

## 8.33 Konuşmalar: `fm talk`

"Meet the Players → Introduce yourself to the Squad" bir toplantı açıyor. Beklendiği gibi
(§8.25, §8.29) söylenebilecek her cümle bir `SpeakToActionEvent` butonu; ama mesaj cevaplarından
farklı bir şablonda: `name=StandardButton`. Tanıtım turunun Continue / Skip / End butonları da
aynı türden, yani **tur da bir konuşma**.

```
…SpeakToSenderMessagesRemapper.NewsResponseOptionalButtons.items.N
    Action        { ActionTitle = "Convince", IsHeader = True }        yaklaşımın adı
    SubActions.items.M.Action { ActionTitle = <cümle>, Hint, Recommended, IsDisabled }
…SpeechBubbleList.items.N.bubble { DisplayString, IsHuman }             karşı tarafın dediği
…binding { InteractionBodyLanguageString = "Pleased", …Level = 7 }      dinleyen başına bir tane
```

Aynı buton panelde iki kez duruyor (iki yerleşim), ikisi de aynı kaynağa bağlı; metne göre
tekilleştiriliyor. Toplantı yedi cümle sürdü, sonuncusu "End Chat"; ondan sonra mesaj
cevaplanmış sayıldı. "Leave Meeting" ayrı bir buton (`continue-button`, `ClosePanelEvent`).

```
fm talk              ne dendi, tepkilerin dökümü, başlığıyla söylenebilecekler
fm talk <no|parça>   söyle; cevabı ve yeni seçenekleri bas      fm talk leave   çık
```

`fm reply` artık cevabın ne açtığına bakıyor: tur açıldıysa `tour`, konuşma açıldıysa `talk`
diyor; ikisi de değilse mesajın kapanmasını bekliyor.

| Deneme | Sonuç |
|---|---|
| konuşma yokken `talk` | "açık bir konuşma yok" |
| `reply "Meet the Players" 1` | "konuşma açıldı… talk" |
| `talk Zzz` / `talk 1 --dry` | red / seçenekler aynı kaldı |
| `talk 1`, `talk attacking`, ardından altı kez `talk 1` | her seferinde karşı cevap + yeni seçenekler; "End Chat" sonrası "konuşma bitti", mesaj kapandı |
| elle aynı toplantı (önceki oturum, yedi basış) | aynı akış |

Tepki dökümü 20 oyuncuya karşı 21 kayıt sayıyor ve cümleden cümleye neredeyse hiç değişmedi;
oyuncu adıyla eşlenmedi. Basın toplantısı ve bire bir oyuncu konuşması henüz görülmedi; aynı
şablonu kullanıyorlarsa `fm talk` olduğu gibi çalışır.

## 8.34 Maç günü: duraklar, onay pencereleri, `fm match`

Maç günü `continue.State` sırayla şu değerleri alıyor; her durakta ekranda **o durağın kendi
ilerle butonu** var (`name=continue-button`), etiketi ve event'i durağa göre değişiyor:

| `State` | Ekrandaki buton → event | Ne oluyor |
|---|---|---|
| `Tactics` | "Review Team Selection" → `ContinueGameEvent` | taktik ekranı açılıyor |
| `SubmitTeam` | "Go To Match" → `ContinueGameEvent` | **onay penceresi**: "Team Selection — Proceed to match?" |
| `Match` | (yok) | maç sahnesi yükleniyor; bağlı düğüm sayısı ~6000'den ~700'e düşüyor |
| `TeamTalk` | "Address the Team" → `MatchContinueEvent` | maç öncesi konuşma ekranı |
| `StepOntoThePitch` | "Go to Dugout" → `EventFmxTeamTalkProcessTalk` + `MatchContinue` | konuşma seçenekleri burada (`fm talk`) |
| `WarmUp` | "Kick Off" → `MatchContinue` | birkaç saniye sonra kendiliğinden başlıyor |
| `MatchLoading` | (yok) | **top oyunda** — adı yanıltıcı; ilk yarı ve ikinci yarı boyunca bu |
| `TeamTalk` (`Period = HalfTime`) | "Address the Team" | devre arası özeti (istatistik, olaylar) |
| `SecondHalf` | "Second Half" → `TeamTalkProcessTalk` + `MatchContinue` | devre arası konuşması burada |
| `FullTimeTeamTalk` | "Address the Team" | maç sonu özeti |
| `LeaveMatch` | "Leave Match" → `TeamTalkProcessTalk` + `MatchContinue` | maç sonu konuşması burada |
| `CanContinue` | "Continue" | normal ekrana dönüş; `DuringMatch = False` |

Kural tek: **ekranda ilerle butonu varsa bas, yoksa bekle** — butonun olmadığı an topun oyunda
olduğu an. `fm match` bunu yapıyor.

**Skor ve dakika** maç ekranının panelinde, ada göre bulunuyor (`/state` maç sırasında ekliyor):
`MatchTime` ("23:44"), `MatchHomeScore`, `MatchAwayScore`, `MatchStatePeriod` (KickOff,
FirstHalf, HalfTime, SecondHalf, FullTime), `IsHomeTeamHuman`, `MatchHighlightCommentary`
(o anki anlatım cümlesi: "GOAL FOR FENERBAHÇE A.Ş.!!", "It's a corner"). Devre arası ve maç
sonu ekranları ayrıca `MatchScore`, `TotalShots`, `ShotsOnTarget`, `TotalPossession`,
`ExpectedGoals`, oyuncu başına `TotalRating` ve olay listesini (`IncidentType`) bağlıyor;
bunlar henüz okunmuyor.

**Onay pencereleri sessiz.** "Go To Match"e basınca pencere açılıyor ama `State` değişmiyor,
buton yerinde duruyor; iki kez basınca **iki pencere üst üste** açıldı. Pencere sabit adlı iki
metinden tanınıyor (`#dialog-title`, `#dialog-description`). Butonları event listesi taşımıyor
(işleyicisi kodda), o yüzden `/press` ile basılamıyor: `SIButton.SimulateButtonClick(bool)`
gerçek tıklamayı taklit ediyor (`/click?ptr=`). Yalnızca onay isteyen pencerenin tek butonu var
ve üstünde yazı yok ("Injured Players").

```
fm dialog            açık pencereyi göster      fm dialog <no|yazı>   cevapla
fm match             maç gününü baştan sona oyna; golleri, devreyi ve sonucu bas
fm match --talk      konuşma duraklarında ilk (önerilen) cümleyi söyle
```

**Maç kendiliğinden durabiliyor.** İkinci denemede 19:49'da bir oyuncu sakatlandı; oyun maç
içi taktik ekranını açıp değişiklik bekledi. Bu sırada ilerle butonu yok, pencere yok, saat
duruyor. `fm match` bunu saatin 45 sn kıpırdamamasından anlayıp duruyor ve söylüyor. Elle
"Cancel"a basınca "Injured Players — …you still have an injured player on the pitch" penceresi
çıktı. Ekranda ayrıca "Dugout" paneli vardı (`EventFmxMatchDugoutConfirmEvent` = "Do it",
`…DismissEvent`, `…JumpToEvent`): yardımcının maç içi tavsiyeleri. İçeriği okunmadan kapatıldı;
sakat oyuncuyu değiştirme tavsiyesi büyük olasılıkla oradaydı. Değişikliğin kendisi artık var
(aşağıda `fm sub`), ama sakatlık durağında denenmedi.

Doğrulama:

| Deneme | Sonuç |
|---|---|
| elle, durak durak (ilk maç) | yukarıdaki tablo bu maçtan; devre arası 0-3 öndeydik, maç sonu skoru not edilmedi (sonraki haber: "Fenerbahçe hammer Sarıyer") |
| `fm match` (Messages sayfasından) | kadroyu gönderdi, pencereyi geçti, 13:47'de golü bildirdi; 19:49'daki sakatlıkta takıldı, sonunda "Injured Players" penceresini görüp durdu |
| `fm match --talk` (maç günü kaydından, yeni oturum) | baştan sona tek komut, 6 dk: üç durakta konuştu, devre 0-0, maç sonu 0-0, "Maç bitti: 0-0", oyun `CanContinue` |
| maç sırasında `fm status` / `fm inbox` | skor ve dakika / "maç sırasında okunmuyor" |
| `fm dialog` pencere yokken | "açık bir onay penceresi yok" |

"Saat 45 sn durursa dur" kuralı yazıldıktan sonra tetiklenmedi (ikinci maçta sakatlık olmadı);
`fm dialog <cevap>` da canlı pencerede denenmedi — pencereyi `fm match` kendi tıkladı.

Maç içinde ayrıca şunlar buton/liste olarak duruyor, denenmedi: mentalite listesi, "Shouts"
listesi (Encourage, Calm Down, Focus, Fire Up, No Pressure, Demand More, Praise, Berate), oyuncu
başına menü (rol, talimat, "Substitute Off ›"), `MatchTogglePlayPause`, `MatchSkipReplay`.
Hız için katalogda `EventFmxHighlightModeChanged`, `EventFmxHighlightSpeedChanged`,
`EventFmxBetweenHighlightSpeedChanged`, `EventFmxRequestInstantResult` var; ilk maç gizli
pencerede yaklaşık 7 dakika sürdü.

### Anında sonuç: `fm match --instant`

Kadro gönderme ekranında "Instant Result" butonu var (`OpenPanelEvent`). Sırayla üç pencere
açıyor: "Instant Result — Are you sure you want to leave this match to your staff?" (Cancel /
Confirm; Confirm `InstantResultEvent` gönderiyor), "Team Selection — Proceed with simulate
match?" (No / Yes), ve simülasyon sürerken butonsuz bir "Match in progress..." bildirimi.
Ardından `State = CanContinue`; maç sahnesi hiç yüklenmiyor.

Oynanmış maç fikstür kaydında saat yerine sonuç taşıyor: `HasResult = True`,
`ResultLinkableNoSpaces = "1 - 0"`, `ResultType`. `fm fixtures` sonucu gösteriyor.

| Deneme (maç günü kaydından) | Sonuç |
|---|---|
| elle: Instant Result, `fm dialog Zzz` / `Confirm --dry` / `Cancel` | red / değişmedi / pencere kapandı |
| elle: Instant Result, `fm dialog 2`, `fm dialog Yes` | ikinci pencere açıldı, sonra maç simüle edildi (0 - 3) |
| `fm match --instant` | 9 sn: "Maç simüle edildi: Sarıyer maçı 1 - 0"; saat 14:00 → 16:00 |
| `fm fixtures` sonrasında | 7 Temmuz satırında sonuç "1 - 0", saat boş |
| maç yokken `fm match` | "şu an oynanacak maç yok" |

Aynı maç bu gece dört kez oynandı ve dördü de farklı bitti (devre arası 0-3, 0-0, 0 - 3,
1 - 0): sonuç kayıtta yazılı değil, her seferinde yeniden üretiliyor. Sonuç dizgisinde hangi
tarafın biz olduğu yazmıyor; tarafsız sahada ayırt edilemedi. `ResultType` "0 - 3"te 1 idi,
başka skorda bakılmadı.

### Maç içi oyuncu değişikliği: `fm sub`

Maç ekranında sahadaki her oyuncunun bir menüsü var (`SIDropdown`, `SelectionEvents` =
`MatchPlayerMenuEvent`, veri `modify-data`'dan). Satırları: ad, ruh hali, kondisyon, iki rol
alt menüsü, "Player Instructions", "Touchline Shouts ›", **"Substitute Off ›"**. Sonuncunun alt
menüsü kulübe: ilk satır sayaç ("15/15 Substitutes"), sonrakiler `{ Type = 7, PropertyValue =
<oyuncu>, DisplayValue = "N'Golo Kanté - DM, M (C)", Player2 = <kulübe sırası> }`.

**Alt menü satırı nasıl seçilir.** Üç yol denendi, canlı maçta:

| Yol | Sonuç |
|---|---|
| `OnSelectionChangedInternal(IReadOnlyList<int>)` (indeks yolu "7,3") | hata yok, `modify-data` boş kaldı, değişiklik olmadı |
| aynısı + `SelectionEvents`'i kuyruğa koymak | olmadı |
| `UpdateDropdownSelectionBinding(<satır>)` + `SelectionEvents`'i kuyruğa koymak | **oldu**: Guendouzi'nin yerinde Kanté, `MatchTeamHasPendingChanges = True`, iki dakika sonra `False` ve Kanté'nin ruh hali "Seemed calm coming onto the pitch" |

Yani satırın kendisi listenin bağlı değerine yazılıyor, sonra listenin event'i gönderiliyor
(liste cevaplı basın sorusuyla aynı kalıp, §8.36). `/select?ptr=&path=7,3&fire=1` bunu yapıyor;
`/dropdowns?json=1&deep=1` alt menüleri de veriyor. Maç durmuyor; oyun değişikliği ilk
duraklamada uyguluyor.

```
fm sub                  sahadakiler (ruh hali, kondisyon) ve kulübe
fm sub <çıkan> <giren>  değiştir (isimlerin bir parçası yeter)
```

| Deneme (canlı maç, 4. dakika) | Sonuç |
|---|---|
| maç başlamadan `sub` | "yalnızca maç sırasında" |
| `sub` | 11 oyuncu, 7 kişilik kulübe |
| `sub Zzz Kanté` / `sub Guendouzi Zzz` / `… --dry` | red / red / değişmedi |
| `sub Musaba Oğuz` | 1,3 sn; Oğuz kulübeden çıktı, kulübe 5 kişi kaldı |

Maç içi taktik ekranındaki ("Tactics" butonu, `MatchOpenTacticsEvent`; maçı durduruyor) listeler
de aynı kalıpta ve denenmedi: 18 `TacticTeamSelectionDropdownEvent` listesi ("All Players ›" alt
menüsüyle; ilk 11 ve yedekler), 22 `RoleOptionEvent` listesi (rol değişimi), 2 `ModifyTactic`.
Sakatlık durağında (`fm match`'in bıraktığı yer) `fm sub` denenmedi; orada ayrıca "Confirm
Changes"e basmak gerekecek.

## 8.35 Kaydetmek, ve oyunun kendi kaydı

`SaveGameEvent` (alansız) oyunu kendi dosyasının üzerine kaydediyor; `game.GameSaving` kalkıp
iniyor, 2 sn sürdü. `fm save --yes` bunu gönderip bitmesini bekliyor; `--yes` olmadan yalnızca
neyin üzerine yazacağını söylüyor. Oyun eski dosyayı `last save overwrite backup.fm` adıyla
yanında bırakıyor.

**⚠ Oyun kendiliğinden de kaydediyor.** Oturumlar "kaydetmeden yeniden başlat, temiz kayda dön"
varsayımıyla yürüyordu. Oyun 7 Temmuz sabahına geçerken otomatik kayıt aldı: ana dosyanın
üzerine yazdı, eski hali `… (v02).fm` oldu. Bir sonraki `fm start` 30 Haziran yerine 7 Temmuz'u
yükledi. Yani **zamanı ilerleten bir oturum kayda yazılabiliyor**; "test tıklamaları kayda
yazılmıyor" (PLAN günlüğü, 5 Ekim) yalnızca aynı gün içinde doğru.

## 8.36 Duran top sihirbazı ve liste cevapları

**Set Pieces Introduction da "Decline" sunmuyor** (taktik turu gibi): "Begin" bir sihirbaz
açıyor. "Let’s go" (`EventFmxSetPieceCreatorStart`), ardından altı soru; her birinde `Option`
adlı bir liste (2-3 seçenek, biri hazır seçili) ve "Next" (`EventFmxSetPieceCreatorNextStep`,
`name=buttons-next-primary-default`); son soruda aynı butonun etiketi "Confirm". Onaylayınca
mesaj kapanıyor.

| Soru | Seçenekler (ilk kelimeleri) |
|---|---|
| 1 korner nereye | front of the penalty area · around the penalty spot · back of the penalty area |
| 2 kavis | bend in towards goal · bend away from goal |
| 3 hücumda geride kalan | more defenders back · a few stay back · fewer stay back |
| 4 savunmada markaj | cover key areas (alan) · karışık · tightly mark (adam) |
| 5 bekler | defend the opposition · near-side full-back on the post · both on the posts |
| 6 ileride kalan | all defend the box · a few left forward · one or two left forward |

`fm setpieces new [<no> …]` sihirbazı yürütüyor; numara verilmeyen soruda oyunun seçimi kalıyor.
Tura "Begin" demeden de çalışıyor: Match Day › Set Pieces sayfası rutin yokken sihirbazla açılıyor.

| Deneme (temiz kayıttan, 1 Temmuz) | Sonuç |
|---|---|
| `setpieces new 9` | "1. soru: '9' diye bir seçenek yok" |
| `setpieces new --dry` | ilk soruyu listeledi, durdu |
| `setpieces new 2 1 3` | altı soru basıldı (1→2., 3→3. seçenek, kalanı oyunun seçimi), "kuruldu"; Set Pieces mesajı engel olmaktan çıktı |
| ardından `setpieces new` | "sihirbaz açılmadı, zaten kurulmuş olabilir" |
| elle (önceki oturum): Let’s go, Next ×5, Confirm | aynı altı soru, mesaj kapandı |

Seçilen cevapların oyunda kaldığı ayrıca okunmadı; yalnızca seçim çağrısının hata vermediği ve
sihirbazın bittiği görüldü.

**Bazı sorular listeden cevaplanıyor.** Basın toplantısında "What position do you feel the squad
could do with strengthening in?" sorusunun cevabı buton değil, `GenericDropdown` adlı bir
`SIDropdown` (15 seçenek: mevkiler ve "No Comment"). Seçmek (`OnSelectionChangedInternal`)
değeri `SelectedAction`'a yazıyor ama **göndermiyor**; `ProcessSelectionEvents()` de göndermedi.
Listenin `SelectionEvents` listesini buton gibi UI kuyruğuna koymak gönderdi — context alanlarını
`SelectedAction.*` yollarından dolduruyor. `/select?…&fire=1` artık bunu yapıyor; `fm talk`
liste cevaplarını seçenek olarak sayıyor.

**Basın toplantısı** `continue.State = PressConference`, buton "Attend Press Conference". Mesajı
("…to meet the media": Attend / Decline) cevaplayınca konuşma açılıyor: "Start Press
Conference", ardından on dört soru (biri liste cevaplı), sonunda "Exit Press Conference". Şablon
toplantıyla aynı (§8.33): başlıklar cevabın tonunu veriyor (Positive, Disagree, Make Demands),
tepkiler gazetecilerin (yedi kişi). İki ayrı oturumda `fm talk` ile baştan sona yürüdü; ikincisinde
liste sorusu `fm talk "No Comment"` ile cevaplandı ve sıradaki soru geldi. (İlkinde liste
cevabı henüz tanınmıyordu: `talk` "konuşma bitti" dedi, toplantı açık kaldı.)

"Expectations and Promises Meeting" de aynı şablon: konu menüsü (lig / Şampiyonlar Ligi / kupa
hedefi, sözler, davranış kuralları), her konuda bir hedef cümlesi, sonunda "End Chat".

## 8.37 Lig tablosu ve fikstür

İkisi de portalın ana sayfasındaki kutulardan okunuyor (`/table`, `/fixtures`).

- Tablo satırı `TablePosition` + `LeaguePoints` taşıyan kayıt; takım adı ve `LeagueGamesPlayed`,
  `…Won`, `…Drawn`, `…Lost`, `LeagueGoalDifference` çevresinde bağlı. 18 satır geldi.
- Fikstür satırı `Fixture` kaydı: `KickoffTime`, `FixtureDate` (paketlenmiş tarih, §8.32),
  `MatchVenueShort` (H / A / N), `HasResult`; rakibin adı yanındaki `Club.ShortName`'de, turnuva
  `comp.Name`'de. Portal kutusu sıradaki yedi maçı veriyor. Başka kulüplerin lig maçları da aynı
  türden kayıtla bağlı ama tarihsiz; `fm fixtures` tarihsiz olanları atıyor.

```
$ fm fixtures
TARİH      SAAT   YER       RAKİP       TURNUVA
7 Tem Pzt  14:00  Tarafsız  Sarıyer     Friendly
9 Tem Çar  20:00  Tarafsız  Boluspor    Friendly
12 Tem Cmt 19:30  Tarafsız  Adana D.S.  Friendly
```

Sezon başlamadığı için tablo sıfırlarla ve alfabetik geldi; puanlı bir tabloda ve oynanmış
maçın sonuç kolonunda (`Result`) denenmedi. Tam fikstür sayfası (yedi maçtan fazlası) açılmadı.


## 8.38 Oyuncu sayfası: `fm player`

Kadro tablosundaki isim butonu (`NameButton`, `OpenPanelEvent`) oyuncunun sayfasını tablonun
üstüne açıyor. Sayfa şunları bağlıyor (`/player`):

```
Person { Name, Age, SquadNumber, PositionCombinedStringLong, DateOfBirth, IsGoalkeeper,
         InternationalCapsAndGoalsString, TransferValueRange.TransferValueRangeString,
         FullContract { Wage.DisplayValue, AgreedPlayingTime.DisplayValue, EndDateDescription } }
attribute { AttributeTypeInfo.Name = "Technique", AttributeValues.PropertyValue = 13 }   × 36
ConditionString, InjuryDescription, Personality, Right/LeftFootednessString, Height, Nation.Name
Text = "Dictates Tempo"   (oyuncu alışkanlıkları)
```

Alttaki kadro tablosu da yaş ve isim bağladığı için sayfa önce yalnızca onda bulunan şeyden
(özellik listesi) bulunuyor, gerisi onun içinden okunuyor. Özellikler hangi bloğa ait olduğunu
söyleyen bir sırayla bağlanmıyor; CLI zihinsel (14), fiziksel (8) ve duran top (4) adlarını
biliyor, kalanı teknik ya da kalecilik sayıyor.

**⚠ Sayfa açılırken buton kuyruğu bir an dolu.** Sayfayı açan basıştan hemen sonraki `/press`
"kuyruk tıkalı" (`OpenPanelEventContext status=Sending`) diye reddedildi; bu §8.27'deki kalıcı
tıkanma değil, birkaç saniyede kendiliğinden boşalıyor. CLI artık bu cevabı da "henüz hazır
değil" sayıp 8 sn'ye kadar yeniden deniyor.

| Deneme | Sonuç |
|---|---|
| `player` / `player Zzz` / `player a` | kullanım / 29 isimle red / beş isimle "birden çok" |
| `player guendouzi` (Messages'tan) | kimlik, sözleşme, 3 alışkanlık, 36 özellik dört blokta; 11 sn |
| `player Kerem` iki kez | aynı çıktı; ikincisi sayfa zaten açık olduğu için anında |
| `player Ederson` | blok başlığı "KALECİLİK", 14 kaleci özelliği |
| `player Talisca` | "Sakatlık: A moderate hamstring strain" (kadro tablosundaki sakatla aynı oyuncu) |

Kadro tablosunun listelemediği oyuncular oyunun aramasıyla bulunuyor (aşağıda). Sezon istatistikleri, form ve
mutluluk ayrıntısı sayfada bağlı ama okunmuyor.

### Kadro tablosunun bütün görünümleri: `fm squad --view`

Tablonun on dört görünümü var (`fm squad --views`): Selection Info, General Info, Attributes ›,
Contract, Transfer, Home-Grown Status, Playing Time, Dynamics, Reports, Injuries, Risk
Assessment, Stats ›, International, Development. `fm squad --view "<ad>"` görünümü seçip
satırların taşıdığı **bütün** alanları kolon yapıyor; hangi kolonların geleceği veriden çıkıyor,
yalnızca tablonun her görünümde bağladığı on beş kadar iç alan atılıyor.

| Görünüm | Gelen kolonlar |
|---|---|
| Contract | SquadNumber, TransferValueRangeString, AgreedPlayingTime, ContractStatus, Wage |
| Injuries | Sharpness, Condition, InjuriesTimeMissed, InjurySeverity, InjuryDescription |
| Playing Time | PlayingTimeHappinessLevel, AgreedPlayingTime, ActualPlayingTime, FutureAgreedPlayingTime |
| Transfer | TransferValueRangeString, AskingPrice, TransferOptions |
| Development | MinValue, MaxValue, TrainingRating |

Kolon başlıkları oyunun alan adları, çevrilmedi. "Attributes ›" ve "Stats ›" alt menü; içleri
seçilemiyor. Sözleşme bitiş tarihi Contract görünümünde gelmedi (paketlenmiş tarih alanları
satır kaydına alınmıyor). ⚠ Taktik ekranı altta bağlıyken bir oyuncunun (Musaba) mevki ve yaş
kolonları boş geldi: `/squad` aynı adı bağlayan iki listeyi birleştirirken eksik kalıyor; düz
`fm squad`'da da aynı. Çözülmedi.

### Oyuncu veritabanı: `fm players`

Recruitment › Player Database, kadro tablosuyla aynı türden bir kişi tablosu: aynı adlı görünüm
listesi (`ViewDropdown`: Overview, General Info, Contract, Transfer, Selection Info, Scout -
Transfer, Scout - Position, Stats ›, International, Attributes ›) ve bir "Quick Filters" listesi
(`inputs-combobox-default-default`: Transfer Listed, Loan listed, Contract Expired, Contract
Expiring, Home-Grown, Unhappy, No Preferred Foot, Goalkeepers, Defenders, Midfielders, Attackers,
Extensive Knowledge). Süzgeç listesinin event'i var (`EventToPlugin`, veri `SelectedQuickFilter`),
yani seçmek + event'i göndermek gerekiyor; görünüm listesi yalnızca değer yazıyor.

Satırın kişisi `binding` adıyla, yanında satırın `Index`'i ile bağlı; ad dışındaki her şey
(kulüp, değer, mevki, durum) komşu hücrelerde ve görünüme göre değişiyor. `/squad` kişinin kendi
kaydında mevki ya da forma numarası aradığı için bu tabloyu görmüyordu; `/people` satırı
şeklinden tanıyor.

| Deneme | Sonuç |
|---|---|
| `players --filters` / `--views` | 12 süzgeç / 10 görünüm |
| `players` | 21 satır: yaş, değer, kulüp, mevki, "Scouting Required" |
| `players --filter zzz` | red |
| `players --filter "Transfer Listed"` | 21 satır, durum kolonunda "Lst"; oyunun sayacı "players found (Filtered)" |
| `players --view transfer` | değer, AskingPrice |
| `players --filter "contract expiring" --view Contract` | maaş aralığı, sözleşme türü |
| ardından `fm squad`, `--fitness`, `--morale`, `--view injuries` | kadro tablosu kendi listesinden okundu |

⚠ İki tablo aynı anda yüklü kalabiliyor ve ikisinin de `ViewDropdown`'u var; komutlar kendi
listesini yalnızca onda olan bir görünümden tanıyor (kadro: "Dynamics", veritabanı: "Scout -
Transfer"). Bunu ayırmadan önce `fm squad`, `fm players`'tan sonra "görünüm seçildi ama veri
gelmedi" diye düşüyordu.

Süzgeç oyunda kalıyor; yenisi eskisinin yerine geçiyor ("Edit Search (1)" sayacı 1'de kaldı),
`--clear` sayfanın kendi "Clear" butonuna basıyor (`EventFmxResetFilter`). "Unhappy" süzgeci sıfır
satır verdi; boş mu, yoksa bilgi mi yetmiyor, bakılmadı.

Sınırlar: yalnızca ekrandaki satırlar bağlı (21), liste kaydırılamıyor; isimle arama ("Edit
Search", `EventFmxFilterStoreFilter`) yok; kolon başlıkları oyunun alan adları.

### Metin kutusuna yazmak ve isimle arama: `fm search`, kadro dışı `fm player`

Üst çubuktaki "Search" butonu (`dashboard-hud-search-button`) arama panelini açıyor; panel
metni `SearchBox` adlı elemandan alıyor (`BindableTextEditBox`, içinde bir Unity `TextField`).
`SearchValueChangesEvent { Value }` elle gönderildi: hata yok, `game.Search.Name` boş kaldı,
sonuç gelmedi. **Metin kutunun kendisine yazılmalı**: `m_textField.value = "Osimhen"` kutunun
değişim olayını tetikliyor, arama çalışıyor (`/type?name=SearchBox&text=…`; `/textboxes`
ekrandaki kutuları listeliyor; `submit=1` kutunun `OnSubmitEvents`'ini de kuyruğa koyuyor).

Sonuçlar `…list.items.N.searchresult` (`SearchRecordType`: Player, Staff, Club…) ve aynı satırın
içinde `Person.Name`, `Club.Name`. Her oyuncu sonucunun kendi butonu var (`PlayerSearchButton`,
`OpenPanel` + `SearchResultClicked`), hepsinin etiketi "Player"; buton verisini kendi satırının
içinden alıyor (`…items.N.1.PersonRemapper…`).

İki tuzak, ikisi de art arda arama yapınca çıktı:

- **Önceki aramanın sonuç butonları yüklü kalıyor.** "Sıradaki n. buton" diye basmak ilk aramada
  çalıştı, ikincisinde eski butona bastı. `/searchopen?name=` butonu, verisini o sonucun
  satırından alan buton olarak buluyor.
- **Açılan oyuncu sayfaları üst üste yüklü kalıyor.** `/player` ilk bulduğunu veriyordu, yani
  hep ilk açılanı. Artık `?name=` ile isteneni veriyor.

Oyunun araması gevşek: "icardi" Carlos Vicente'yi, "mauro icardi" dört oyuncuyu getirdi. `fm
player` sonuçları adında yazılanı gerçekten içerenlere daraltıyor; bir tane kalırsa açıyor.

| Deneme | Sonuç |
|---|---|
| `search Osimhen` / `Galatasaray` / `zzzqqq` | Player Victor Osimhen (Galatasaray) / iki Club satırı / boş |
| `player osimhen`, `icardi`, `muslera` art arda | üçünün de sayfası; maaş aralık olarak ("£400K - £550K p/w"), kişilik "Scouting Required" |
| araya `player kante` (kadrodan) ve yine `player osimhen` | ikisi de doğru sayfa |
| `player vicente` / `pizarro` | "2 / 3 oyuncuya uyuyor", isimler ve kulüpleriyle |
| `player zzzqqq` | "kadroda da aramada da yok" |

Açık: aynı adı taşıyan iki oyuncu ayırt edilemiyor (iki "Carlos Vicente"); "Edit Search" ve
"farklı kaydet" de aynı metin kutusu yolunu bekliyor, denenmedi.

## 8.39 MCP: komutlar araç olarak

`fm mcp`, Model Context Protocol'ün stdio taşımasını konuşuyor (satır başına bir JSON-RPC
mesajı): `initialize`, `ping`, `tools/list`, `tools/call`. 29 araç var ve **her araç bir
komut**: argümanlar kullanıcının yazacağı kelimelere çevriliyor, komut terminaldeki gibi
çalışıyor, bastığı metin sonuç oluyor (`isError` = çıkış kodu sıfır değil). Davranış tek yerde
duruyor; CLI'da çalışan her şey araçta da çalışıyor.

```
fm_status  fm_next  fm_inbox  fm_reply  fm_talk  fm_tour  fm_dialog
fm_squad  fm_player  fm_players  fm_search  fm_table  fm_fixtures
fm_tactic  fm_tactic_set  fm_tactic_role  fm_tactic_player  fm_tactic_pick  fm_tactic_new  fm_setpieces_new
fm_match (varsayılan: instant)  fm_sub  fm_save (confirm: true ister)
fm_pages  fm_open  fm_buttons  fm_press  fm_dropdowns  fm_select
```

Repo kökündeki `.mcp.json` sunucuyu `fm mcp` olarak tanımlıyor; Claude Code projeyi açınca
onay soracak.

| Deneme (elle beslenen JSON-RPC, borudan) | Sonuç |
|---|---|
| `initialize` (istemcinin sürümüyle), bildirim, `tools/list` | sürüm geri verildi, bildirime cevap yok, 25 araç |
| bozuk JSON / bilinmeyen metot / bilinmeyen araç | −32700 / −32601 / −32602 |
| maç sırasında `fm_status`, `fm_sub`, `fm_fixtures` | skor ve dakika / saha ve kulübe / "maç sırasında açılmıyor" (`isError`) |
| maç dışında 15 araç art arda | status, fixtures (sonuçla), table, tactic, mentalite listesi ve kuru değişim, player, inbox, squad fitness, open, next çalıştı; olmayan mesaj, onaysız save, maç yokken match, olmayan buton `isError` döndü |

**Gerçek bir MCP istemcisiyle (Claude Code / Desktop) denenmedi**; yalnızca protokol elle
konuşuldu. Bilinen sınırlar: oynanan maç (`mode=play`) altı-sekiz dakika sürüyor ve istemcinin
araç zaman aşımına takılabilir; çıktılar Türkçe ve yönlendirmeler komut adıyla ("Oynamak için:
match"), araç adıyla değil; araçlar düz metin döndürüyor, `--json` çıktısı sunulmuyor.

## 8.40 Span köprüsü çözüldü: ekran açmadan her veri

§8.12 ve §8.21'in kilidi açıldı. Sorun span'ın kendisi değilmiş, Il2CppInterop'un
`ReadOnlySpan<char>` sarmalayıcısıymış: üretilen kod span'ı yolda kaybediyor. `ReadOnlySpan<char>`
bellekte yalnızca `{ char* pointer; int length; }`. Bunu native bellekte elle kurup metodu
il2cpp `MethodInfo`'suyla (`NativeMethodInfoPtr_…` statik alanı) `il2cpp_runtime_invoke` ile
çağırınca, byref parametreye struct'ın adresini vererek, her yol doğru çalışıyor
(`plugin/…/RawPath.cs`):

```
pathexists  app.LastSave            → true
pathexists  ZzNotAPathZz.NopeNope   → false      (§8.12'de bu da true dönüyordu)
pathbind    Human.Team.Name         → (String) Fenerbahçe     ← hiçbir ekran bağlamamıştı
pathbind    Human.Team.NextMatchOpponent.Name → Sarıyer
pathbind    Human.Team.Nation.Name  → Türkiye
```

`Bindings.Bind(ref ReadOnlySpan<char> path, callback)` bir ekranın yaptığını yapıyor: düğümü
yaratıyor, üstteki referansı buluyor, property'nin kanalını açıyor. Değer çoğu zaman aynı
karede, bazen bir sonrakinde geliyor.

**Listeler.** `Human.Team.Players` 27 `PersonReference` tutan bir liste, ama `Players.0.Name`
bağlanınca boş kalıyor: listenin elemanı ağaçta bağlanabilir bir düğüm değil. Ekranın tablosu
her satıra kendi düğümünü veriyor; aynısını yapıyoruz: elemanı `Bindings.Set(ref span, TypedValue,
NodeFlags, SetFlags)` ile `fmcli.<liste>.<n>` düğümüne koy, property'leri onun altına bağla.

```
/pull?path=Human.Team.Players&props=Name,Condition&list=1
  {"Index": "0", "Name": "Dorgéles Néné", "Condition": "100"}
  {"Index": "4", "Name": "Talisca", "Condition": "-1"}      (sakat)
  …27 satır
```

İlk çağrı bağlıyor, değerler ikinci çağrıda geliyor; `/pull` idempotent, eksik kalmayana
kadar tekrar sorulur. Hangi referansın hangi property'leri kabul ettiği `/schema` kataloğunda
(§8.11) statik olarak var, yani "ne okunabilir" sorusu deneme-yanılma değil.

Sonuç: §8.22'deki "aynı anda tek ekranın verisi bağlı" kısıtı okuma için kalkıyor. Kadro,
kondisyon, rakip, fikstür gibi okumalar ekran açmadan, birbirini bozmadan yapılabilir.
Komutları bu yola taşımak ayrı iş (PLAN.md).

## 8.41 Ayarlar ve maç hızı: nerede duruyorlar

§8.40'taki `/pull` ile ekran açmadan okunanlar:

| Yol | Property | Değer (bu kayıtta) |
|---|---|---|
| `settings` (`SettingsReference`, 106 property) | `MatchSpeedDuringHighlights` | 1.5 (en çok 10) |
| | `MatchSpeedBetweenHighlights` | 30 (`…Max` 5 — birimleri farklı, indeksli olabilir) |
| | `MatchHighlightMode`, `MatchHighlightModes` | referans / liste |
| `game` (`GameReference`) | `AutosaveModeAndIntervalString` | "Saves automatically every week" |
| | `LastSaveDate`, `NumberTimesSaved`, `GameTimeSinceLastSave` | |

Otomatik kayıt **ayarı** burada yalnızca metin olarak okunuyor; değiştirmek için Preferences
ekranındaki listeyi seçmek gerekecek (ürün kararı: kullanıcı açıp kapatabilmeli). Yazma tarafı
(`Bindings.Set` ile bir `TypedValue`) denenmedi: `TypedValue.Create<T>` generic, interop'tan
çağrılabilirliği belirsiz.

## 8.42 Transfer teklifi: `fm offer`

Yol, oyuncunun sayfasından:

1. Sayfadaki **değer butonu** (etiketi değerin kendisi: "£6.8M - £10.5M" ya da "Not for Sale")
   oyuncunun aksiyon panelini açıyor: Make An Offer, Add To Shortlist, Scout Player, Ask Agent
   About Availability, Transfer ›… Bu panel bir `SpeakTo`, yani `fm talk` de görüyor.
2. **Make An Offer** teklif ekranını açıyor: `transfer-offer-button` / `loan-offer-button`,
   Negotiable listesi, bonservis kutusu (`FMCombobox`, `name=combobox`), ödeme şekli
   ("Immediate"), madde ekleme ("Add"), Undo / Revert ve **Make Offer**.
3. **Make Offer** gönderiyor; kontroller kapanıyor, `talk` "An offer has been made for …"
   diyor ve yalnızca "Exit Talks" kalıyor. Teklif, oyuncunun aksiyonlarında "Withdraw Transfer
   Offer" olarak görünüyor; oyuncu kısa listeye de ekleniyor.

Denendi: Icardi'ye £7.25M (elle), Kerem Demirbay'a £1.5M (`fm offer … --yes`).

**Bonservisi yazmak — çözüldü (gecenin sonunda).** `FMCombobox` yazılanı ancak sayı alanı
**odağı kaybedince** alıyor (`OnLostFocusInput`); odağı kazanınca da girişi açıyor
(`OnGainFocusInput`). Oyun küçültülü olduğu için gerçek odak hiç oluşmuyor, `Focus()`/`Blur()`
bir şey yapmıyor. Olayları elle üretip kutunun kendi işleyicilerine vermek yetti:

```csharp
box.OnGainFocusInput(FocusEventBase<FocusInEvent>.GetPooled(field, null, FocusChangeDirection.unspecified, box.focusController, false));
field.value = 1500000;
box.OnLostFocusInput(FocusEventBase<FocusOutEvent>.GetPooled(field, null, FocusChangeDirection.unspecified, box.focusController, false));
```

Kutu "£1.5M" gösterdi; `fm offer … 2.25M` 22 saniyede gitti (eskiden ~2 dk). Aynı desen odakla
çalışan başka kutularda da (metin alanları dahil) işe yarayabilir.

Önceki denemeler (kayıt için): Kutunun + / −'si `EventFmxIncrement/DecrementCashValue`
gönderiyor; adım tutarla büyüyor (0'dan 1, 2, …; £6.75M'de £0.25M, yaklaşık %4). `fm offer`
tutarı bununla yürüyor: £40K'dan £1.5M'ye ~2 dk. Yazma denemeleri (`/setnumber`):

| Deneme | Sonuç |
|---|---|
| `m_numericInput` (LongField) değerini kur + `TryTriggerValidationEvent()` | alan değişti, teklif değişmedi |
| Focus → değer → Blur | aynı |
| Doğrulama verisini (`…cash-list-remapper.combobox-validation`) sayıyla doldurup `ValidateValueEvents`'i ateşlemek | event gitti, teklif değişmedi |
| `OnNumericValueChanged(key, value)` doğrudan | aynı; ⚠ değeri havuzdaki `TypedValue` üzerinde yerinde değiştirdiğim için oyun kilitlendi, kaldırıldı |

### Teklif ekranını okumak ve düzenlemek: `/offer` (7 Ekim 2026 akşamı)

Ekran veriye bağlı (`…transfer-offer-remapper7…binding-variables0.transferoffer`,
`TransferOfferEditSessionReference`) ama şartların listeleri (`TransferOfferTransferBaseOptions`,
`TransferOfferAdditionalClauses`…) ağaçta yalnızca `List<TypedValue>` olarak görünüyor. Ekranın
kendisini okumak daha kolay çıktı:

- Başlık: `#club-player-text` ("Galatasaray"), `#player-text` ("(Mauro Icardi)"); maliyet
  `#Text_TransferCostValue`, `#Text_TotalCostValue`.
- Şartlar `#frames-contract-terms` satırları; üç başlığın altında: `#players-to-exchange-title`
  ("Base Details"), `#fees-title` ("Additional Fees"), `#clauses-title` ("Additional Clauses").
  Satırın adı `#primary-text`. Değeri bir `FMCombobox` (tutar), bir `SIDropdown` (seçim) ya da
  ikisi birden: "After League Appearances" = "20 games" + "£250K". Satırdaki
  `#lock-button-remapper` altında ayrı bir dropdown var (0 · 1 · 2, kilit), değer değil.
- Başlıkların üstünde teklif çapında iki dropdown: Negotiable / Non-Negotiable ve cevap süresi
  (No Deadline · Today · Tomorrow · Next Week). Karşı teklifte bir üçüncüsü: Current Offer /
  Previous Offer, kulübün teklifiyle bizim öncekini değiştiriyor.
- Ek ödeme ve madde başlıklarının her birinde bir "Add" dropdown'u. Ödemeler: Instalments (6/12
  monthly), After League Appearances, Per League Appearance, After International Appearances,
  Minimum League Goals, Players To Exchange, iki alt menülü bonus. Maddeler: Loan Back Length,
  Can Be Recalled From Loan Back, Percentage Of Next Sale, Percentage Of Profit From Next Sale,
  Buy Back Price, Selling Team Wage Contribution, Arrange Friendly, Sell Player First.

Değiştirmek:

| İş | Yol | Not |
|---|---|---|
| Şart ya da madde eklemek | "Add" dropdown'unda `/select … fire=1` | `fire` olmadan seçim görünür ama satır gelmez |
| Seçimi değiştirmek (%20, End Of Season, 20 games) | `/select … fire=1` | aynı |
| Cevap süresi | `/select` | olay göndermiyor; `fire=1` "no events to send" der ama değer yazılmış olur |
| Tutar | `/setnumber` (odak olayları, yukarıda) | ek ödemelerde de çalışıyor |
| Satırı kaldırmak | satırın `#remove-button-visible` altındaki butona `/click` | |
| Pazarlık kilidi | satırdaki `#lock-button-remapper` dropdown'unda `/select index=N fire=1` | aşağıda |
| Alt menülü bonus (kupa, ödül), takas oyuncusu | `/select names=A>B>C fire=1` (yaprağın `RowData`'sı yazılır) | çözüldü, §8.45 |

**Pazarlık kilidi.** Her şartın yanındaki asma kilit, `…clause-variables1.lock-button-remapper7.lock.LockStatus`'a
yazan isimsiz bir dropdown; seçenekleri yalnızca sayı. Adları dil dosyasında (`example.ltf`,
"Negotiation Locks UI", FM-45573): 0 **Negotiable** (açık kilit), 1 **Semi-Negotiable** (kapalı,
turuncu), 2 **Non-Negotiable** (kapalı, kırmızı), 3 **Remove & Exclude** (çöp kutusu; yalnızca
ek ödeme ve maddelerde, temel şartlarda üç seçenek var). Ekranda olmayan bir ödemeye Remove &
Exclude demek için önce eklemek gerekiyor; 12 aylık taksite deyince oyun 6 aylığı da kendiliğinden
ekleyip yasakladı. Yasaklı satırın değeri gizleniyor.

**Alt menü.** "Add" listesi seçimini `…binding-variables0.SelectedClause`'a yazıyor. Alt menünün
yaprakları da üst seviyedekilerle aynı biçimde (`RowData {PropertyValue, DisplayValue}`; ödüllerin
hepsinde PropertyValue aynı, 1635214434). `SIDropdown`'da `m_childrenDropdowns`, `m_selectionPath`,
`HandleDropdownSelection(ChangeDropdownSelectionEvent)` var: oyunda alt menü ayrı bir çocuk
dropdown olarak açılıyor, seçimi o yapıyor olabilir. Denenmedi. "Players To Exchange" ayrı bir
bölüm açıyor; tek seçeneği "Add Player › Other Player From Squad ›", aynı engel.

Doğrulandı: Robin Hack'e £4M + %10 sonraki satış + cevap yarın gönderildi. Gladbach'ın
cevabında "View Offer" aynı ekranı açtı: kulüp %20 istiyordu, "Previous Offer"da bizimki tam
olarak duruyordu (£4M, Immediate, %10, Tomorrow). Icardi'ye giden teklifte ise kulübümüzün
yönetimi "Board set to block Icardi deal" ile araya girdi (rakip kulüp). Mesaj bedeli £5.25M
diyordu, ekran £5.3M (£5M + 20 maç sonrası £300K); farkın nereden geldiği bilinmiyor.

Oyun teklifi baştan reddedince ekranı açmıyor, bir diyalog açıyor ("Cannot sign Orkun: This
player has already agreed to join his loan club."). `offer` artık bunu bekliyor ve söylüyor.

### Teklif kabulünden sonrası (keşfedilmedi, bir tuzak)

Akış: teklif → kulüp ya reddediyor ("… bid rejected": Negotiate / Interact with Board) ya da
pazarlıkla karşılık veriyor ("… deal negotiated": Accept / Withdraw / View Offer). ⚠ "Accept"
kulübün **kendi** rakamını kabul ediyor: £3M teklif £8.25M olarak kabul edildi; mesajın ilk
cümlesi bunu söylemiyordu, `reply` artık en azından o cümleyi basıyor. Ardından "… offer
accepted" geliyor ve oyunun ilerle butonu **"Negotiate Contract"** oluyor. O durumda genel
Continue'ya basmak zamanı ilerletti ve teklif geri çekildi ("Fenerbahçe's offer has been
withdrawn"). Oyuncuyla sözleşme ekranı açılmadı, keşfedilmedi.

### Sözleşme görüşmesi: `fm contract` (çözüldü)

"… offer accepted" mesajında **Enter Contract Negotiations** görüşmeyi açıyor. Görüşme iki
adım: önce vaatler ("Add Promise" listesi, **Finalise Promises**), sonra **Negotiate Contract** ile
şartlar. Şartlar ekranda `…Contract Negotiation Contents Block.BlockRemapper0.negotiation`
altında bağlı (`ContractEditSessionReference`); her şart bir `{PropertyValue, DisplayValue}`
kaydı: `Wage`, `EndDate`, `AgentFee`, `LoyaltyBonus`, `ContractStatus`. Ücret kutusu teklifteki
gibi bir `FMCombobox` ("£9,250 p/w"), aynı odak yöntemiyle yazılıyor. Menajerin kendi şartları
**Finalise Deal** ile kabul ediliyor; değiştirilmiş şartlar **Suggest Terms** ile öneriliyor, menajer
kabul ederse sırada yine Finalise Deal, ardından Exit Talks.

⚠ Bu noktada oyunun genel ilerle butonu da "Negotiate Contract" yazıyor (`continue-button`,
`ContinueGame`). Etikete göre buton arayan kod onu bulup basınca görüşme kapanıyor, teklif geri
çekiliyor. Görüşmenin kendi butonları `StandardButton` adını taşıyor; yalnızca onlara basılmalı.

Denendi: Defrel, Modena'dan £160K; menajer £9,250/hafta istedi, £7,500 önerildi, kabul etti, imzalandı.

## 8.43 Kadro kaydı: listeyi elle düzenlemek (`fm register`)

Yardımcının listesi kotayı tutmayınca (UCL 3. ön eleme, 2 Ağustos 2025: Türkiye'de yetişmiş
6/8, kulüpte yetişmiş 2/4) oyun kilitleniyordu. Çözüm ve yolda öğrenilenler:

**Ekranı açmak.** `OpenPage("Registration")` bazen Squad Overview'da kalıyor (orada kayıt bir
kutucuk). Kutucuktaki turnuva butonu (`UCL`, "N assigned"ın hemen önündeki `OpenPanel`) asıl
ekranı açıyor; `OpenRegistration` önce adıyla, olmazsa bununla açar.

**Satır.** `TableRowNavigatable`; hücreleri: `StreamedTable+SelectionCell` (içinde Unity
`Toggle`), sonra **`SICheckBox #inputs-switch-toggle-large-default`** (listede olmak), sonra
`#StatusFlags` (HGN, HGC, Inj, Wnt, Trn), isim, yaş, mevki, statü.
- İlk hücredeki kutu **kayıt değil**, tablonun çoklu satır seçimi. Ona tıklamak (`m_onClick`,
  `m_onSubmit`) yalnızca vurguyu değiştiriyor.
- Hücreye `SendEvent` ile uydurma `ClickEvent` göndermek oyunun `FMCrashReporter.OnClick`'inde
  patlıyor (her tıklamayı dinliyor, işaretçi bilgisi bekliyor). Kullanma.
- Kayıt anahtarı `SICheckBox`: `SimulateCheckboxClick()` ile çevriliyor (`/regtoggle`).
  Confirm'e kadar oyuna bir şey yazılmıyor; `Undo` geri alır.

**Okumak — tuzaklar.**
- `SICheckBox.Value` ilk tıklamaya kadar hep False dönüyor. Doğrusu anahtarın kendi bağlantı
  anahtarından okumak: `Bindings.Get(box.m_valueKey)`.
- Veri ağacında satır başına `squadreg.Selected` var ama ağaç kaydırmanın geride bıraktığı
  satırları tutuyor: aynı oyuncu iki kez, farklı değerlerle. İsimle eşleştirme yanlış okur.
  Kayıt sütununun anahtarı da sabit değil ("A", kaydırınca "9"…).
- Tablo sanal: yalnızca görünen ~27 satır kurulu. `/regscroll?to=0..1` (`ScrollViewToScroll.
  verticalScroller`) ile kaydırıp beş noktada okuyup isimle birleştiriyoruz.
- Kural değerleri (`SquadSelectionRuleValue`, `CurrentlyMeetsRule`) bağlı ve canlı. Daha önce
  "gönderimden sonra ekran yeniden açılana kadar tazelenmiyor" diye not düşülmüştü; 7 Ekim
  akşamı bakıldı: ekleme, çıkarma, Undo ve yardımcı seçiminden sonra sayılar doğru değişti.
  Başarılı bir gönderimin ardından yeniden denenemedi (aşağıda: oyun artık göndermeye izin
  vermiyor), o kısım doğrulanmadı.
- Düzenleme oturumu (`squadreg`, `SquadRegistrationEditSession`) ekran kapanınca bitmiyor:
  çıkarılan oyuncu başka sayfaya gidip dönünce de çıkarılmış duruyor, `Undo` geri alıyor.
- Oturumun `Players` listesi `/pull ... list=1` ile tam geliyor (31 uygun oyuncu), ama
  `RegisteredPlayers`, `Selected` gibi alanları bu yoldan bağlanmıyor.

**Tıklamak — yarış.** Tıklama oyunun bir sonraki karesinde işleniyor (arka planda 1 sn'yi
bulabiliyor). O arada tablo kaydırılırsa anahtar başka oyuncuya bağlanıyor ve tıklama kayboluyor;
erken yeniden tıklamak da geri çeviriyor. `Flip` tıkladıktan sonra kaydırmadan, değer
değişene kadar bekliyor. Tıklama çizim anına denk gelirse Unity "dirty repaint" hatası veriyor;
yalnızca o durumda yeniden deniyor.

**Gönderilmiş listeyi değiştirmek.** Liste gönderilmiş, `RegistrationDaysLeft = "Closes now"`
iken (UCL 3. ön eleme, 2 Ağustos 2025) oyun `CanConfirmChanges`'ı kapalı tutuyor: oyuncu
çıkarmak, yardımcıya yeniden seçtirmek, hepsi `RuleWarningStrings = "Squad selection is fully
compliant"` iken bile. Oyun yeniden başlatılıp temiz oturumda denendi, aynı. Yani oyunun kararı,
bizim okumamız değil; hangisinin (gönderilmiş olması mı, son gün mü) sebep olduğu ayrılamadı.
`register --send` bunu "kurallar tutuyor ama oyun şu an değişiklik kabul etmiyor" diye söylüyor
ve değişikliği `Undo` ile geri alıyor (önceden bütün kurallar ✓ iken "kurallar tutmuyor"
diyordu).

**Kota mantığı.** "Minimum of 8 homegrown" zorunluluk değil: eksik her yetişmiş oyuncu için bir
yer boş kalınca oyun kuralı ✓ sayıyor (6/8 ✓, liste en fazla 23). `CurrentlyMeetsRule`'a
bakmak yeterli, değere değil.

Statik ipucu: `InteropEventIDs` içinde `EventFmxSelectPlayer` (24) var; kullanmaya gerek kalmadı.

## 8.44 Sözleşme görüşmesi kartı ve yenileme (`fm contract`, 7→8 Ekim gecesi)

Görüşme ekranı teklif ekranıyla aynı parçalardan kurulu, bu yüzden plugin'in `/offer` okuyucusu
genelleştirildi; `/contract` aynı JSON'u verir (+ `Stage`, `Facts`).

**Yenileme.** Kendi oyuncumuzun sayfasında değer butonu aksiyonları açıyor; listede
**Enter Contract Negotiations** var. Basınca oyun bir onay penceresi açıyor ("Enter contract
negotiations? … without discussing this with their agent", Yes / No). Yes görüşmeyi açar.
`fm contract <oyuncu>` bunu yapıyor; terminalde uyarıyı sorar, script'te geçip not düşer.

**İki blok, iki aşama.** Kök, satırdan yukarı çıkınca bulunan `BindingRemapper`:
`Contract Negotiation Promises Block` (vaatler) ya da `… Contents Block` (şartlar).
- Satırlar `#frames-contract terms` (teklifte tireli: `frames-contract-terms`). Adı
  `#primary-text`, açıklaması `#secondary-text` (vaatte "Will want to see progress…", ek
  maddede oyunun itirazı: "Contract too short", "Not allowed to exceed appearance bonus").
- Değer: `FMCombobox` (tutar, `/setnumber`) ya da `#inputs-combobox-default-default` (seçim).
- Kilit: `#lock-button-enabled` altında, seçenekleri kilit değerlerinin kendisi: şartlarda
  "0 · 1 · 2", vaatlerde ve "Entire offer"da "0 · 2". Seçili olan `SelectedIndex` ile.
  "Entire offer" kilidi olay göndermiyor (teklifteki cevap süresi gibi), yalnızca yazılıyor.
- Kaldırma: `#remove-button-enabled` / `#remove-button-visible` altındaki buton
  (`EventFmxRemoveAddOn`); menajerin istediği bazı maddelerde kapalı.
- Ekleme listeleri etiketli: "Add Promise", "Add Bonus" (`EventFmxAddContractBonus`),
  "Add Clause" (`EventFmxAddContractClause`). Kupa / ödül bonusları yine alt menüde (§8.42).
- Görüşmenin butonları (`StandardButton`): vaatlerde **Finalise Promises** (menajerin
  vaatleri olduğu gibi) ya da değiştirilince **Suggest Promises**; ardından
  **Negotiate Contract**. Şartlarda **Finalise Deal** / **Suggest Terms**. Ayrıca
  `Reset` (`EventFmxResetTerms`) ve `Use Existing Contract`.
- **Cancel Negotiations** bir onay penceresi açar ("you may not be able to immediately re-enter
  talks", Cancel / Confirm). Gerçekten de iptal edilen oyuncuyla (Oğuz Aydın) hemen yeniden
  görüşme açılamadı: aksiyonlarda "Enter Contract Negotiations" kayboldu. Mert Müldür'de ise
  açıldı; kural belli değil.

**Kenar panelleri.** Maaş bütçesine etki (`#frames-contract negotiations-wage budget impact`:
Cost / Current / Avg. / Remaining) ve oyuncu raporu (Wage, Contract Expires) görüşme bloğunun
dışında, ayrı ızgara elemanlarında. Ekrandaki bütün `#text`'ler sırayla okunup etiketten sonra
gelen değer alınıyor. ⚠ Alttaki sayfalarda da "Wage" var (vaatler aşamasında başka oyuncunun
£22K'sı okundu); oyuncu raporundaki ücret "Transfer Value"dan hemen sonra gelen.

**Neden geçersiz.** Bir şart kurala uymayınca satırda yalnızca bir uyarı ikonu çıkıyor
(`#warning-visible` içinde `ui-concepts-warning_small`; çerçeve hep var, içi boşsa uyarı yok)
ve öneri butonu kapanıyor (`Suggest Terms`, `Enabled=False`). Sebep görüşme kaydında:
`ContractEditSessionReference.<Bileşen>ComponentInfo.BaseComponentData.InvalidOrDisabledReason`
(`WeeklyWage`, `AgentFee`, `LoyaltyBonus`, `EndDate`, `StartDate`, `ContractStatus`). Örnek:
ücreti £32.5K'dan £30K'ya düşürünce menajer ücreti (£155K) "Above max allowed %" oldu; £100K'ya
inince öneri geri geldi. Kart bu sebebi satırın yanına yazıyor.

**Menajerin cevabı.** Önerilen şartlara menajer ya "kabul" der (kart aynı kalır, Finalise
Deal gelir) ya da kendi şartlarını karta geri koyar (yine Finalise Deal, ama değerler değişmiş).
İkisini ayırmak için kart öneriden önce ve sonra okunup farklar yazılıyor ("Menajerin istediği:
Haftalık ücret £130,000 → £145,000 · Menajer ücreti £1M → £1.1M"). Aynı adlı iki prim olabiliyor
(iki "Seasonal Landmark Goal Bonus"), satırlar ad + sıra ile eşleşiyor.

**Yenilemede imza hemen olmuyor.** Finalise Deal'den sonra menajer "I'll be in touch regarding
my client's final decision in the near future" diyor ve yalnızca Exit Talks kalıyor; karar
günler sonra gelen kutusuna geliyor. Transferde (Defrel, §8.42) aynı noktada imza oluyordu.

Denendi (kaydedilmedi): Mert Günok £13.5K (menajer önce £14.75K istedi), Levent Mercan £22K →
£20K kabul (kartın içinden imzalandı, karar bekleniyor), Anthony Musaba'ya kaptan yardımcılığı
vaadi ("The current vice-captain is Milan Škriniar"; menajer "kaptanlık istemiyor" dedi ama vaadi
kabul etti), Kerem Aktürkoğlu £130K + menajer £800K → £145K, £1.1M karşı teklif.

## 8.45 Alt menüden seçmek: kupa / ödül primleri, takasa oyuncu (çözüldü, 8 Ekim gecesi)

§8.42'deki altı deneme neden tutmadı: "Add" listesinin olayı (`EventFmxTransferOfferAddClause`,
`m_dataToSendBinding = SelectedClause`) bağlı değeri gönderiyor. Üst seviye bir seçim
`SelectedClause`'a satırın **`RowData` kaydını** yazıyor (`{PropertyValue, DisplayValue}`).
Bizim `UpdateDropdownSelectionBinding(chosen)` ise menü satırının kendisini yazıyordu
(`{RowData, ChildList}`), yani oyun bir sarmalayıcı alıp hiçbir şey eklemiyordu.

Yaprakların `RowData`'sı neyin primi olduğunu da taşıyor (`/choicedump?ptr=&path=7,0,0`):

```
Bonus After Competition Achievement › Turkish Super League › Bonus upon winning the Super League
  RowData: PropertyValue 1668116578, DisplayValue "...",
           Competition { Competition 130286, Stage 0, ... }
Bonus After Player Award Achievement › GOAL50
  RowData: PropertyValue 1635214434 (bütün ödüllerde aynı), { <ödül id> 2000061133 }
```

Çözüm: yaprağın `RowData`'sını `UpdateDropdownSelectionBinding`'e verip olayı göndermek
(`/select … how=rowdata fire=1`, artık varsayılan). Takas listesinin yapraklarında `RowData` yok;
kayıt doğrudan `{DisplayValue, PropertyValue: {DatabaseReferenceField: <kişi id>}}`, o zaman
kaydın kendisi yazılıyor. Olay `EventFmxTransferOfferAddExchangePlayer`.

Denendi, hepsi tek seferde satır ekledi: Süper Lig şampiyonluğu, Türkiye Kupası yarı finali,
GOAL50, Altın Top (teklifte); sözleşmede "Competition Bonuses › Turkish Super League › Winning
the Super League", "Award Bonuses › GOAL50", "Wage After Reaching Club League Games › 5 games";
takasa Mert Müldür (6 aylık kiralık).

Yol boyunca bulunanlar:
- Açılır liste açılınca (`SIDropdown.ShowDropdown()`) her seçenek bir `DropdownRowNavigatable`
  satırı; her satırın içinde gizli bir `#ChildDropdown` (`SIDropdown`) var, alt menülü satırınki
  alt menünün seçeneklerini taşıyor. Satıra submit / click vermek çocuğu açmadı; gerek kalmadı.
- Alt menü `/select names=A>B>C` ile adla seçiliyor; `/menu?ptr=&names=A>B` bir seviyenin
  adlarını veriyor (CLI menüde kademe kademe iniyor).
- Takas satırı başka bir şablon: `#exchange-player-row-enabled` içinde `#NameText`,
  `#exchange-person-position-remapper`, transfer / kiralık seçimi (Transfer, 1–12 Month Loan,
  Loan Until End Of Season…) ve serbest duran kaldır butonu. `/offer` artık bunu satır olarak okuyor.
- Sözleşmede alt menülü madde "Wage After Reaching Club League Games ›" (5–30 maç) da aynı yolla
  ekleniyor; eklenince oyun "Clause offers no wage increase" diyor, ücreti girilince düzeliyor.

## 8.46 Kiralık teklifi kartla (8 Ekim gecesi)

`fm offer <oyuncu> --loan` teklif ekranında `loan-offer-button`'a basıp aynı okuyucuyla kartı
çiziyor; kiralık ekranı aynı satırlardan kurulu, başlıkları farklı:
- İlk blok başlıksız: Playing / Unused Monthly Fee (tutar), Playing / Unused Wage Percentage
  (%0–200, yanında haftalık karşılığı), Duration (6 Months / End Of Season).
- `#clause-area-title` iki kez: **Loan Clauses** (Agreed Playing Time, Preferred Position / Role,
  kupa ve ödül primleri; "Add": Loan To Buy Fee, Buy Back Price, alt menülü primler) ve **Loan
  Options** (değeri olmayan evet / hayır maddeleri: Can play in cup matches, Can be recalled…).
- `#ltb-fees-title` **Loan-to-buy Fees** (Optional / Mandatory Future Fee, maç sayısına ya da
  başarıya bağlı), `#ltb-additional-fees-title`, `#ltb-clauses-title`: kalıcı olursa ek ödemeler
  ve maddeler (transferdekilerin aynısı).
- Toplam `#Text_TotalCostValue`, başlığı "Overall Cost if deal becomes permanent (Including
  Monthly Contributions)"; satın alma bedeli kaldırılınca bu satır da kayboluyor. Okuyucu kökü
  artık bir şart satırından da buluyor.
- Yardımcının notları (`talk`'ta): kulübün istediği katkı ve ücretler, satın alma opsiyonu.

**Kapalı seçenekler.** "Add" listelerinde oyunun gri gösterdiği satırların kaydında
`Disabled = True` var (ör. yabancı kulüpten kiralıkta "Can play for parent club", zaten eklenmiş
"Can be recalled"). Seçmek olay gönderiyor ama hiçbir şey eklemiyor. `/offer` ve `/contract`
listelerde `Disabled` dizisini veriyor, `/menu` bunları hiç listelemiyor; CLI menüde göstermiyor,
`--term` ile istenirse "listede kapalı" diyor.

Gönderildi: Matteo Cancellieri (Celesti), sezon sonuna kadar, maaşın %100'ü, aylık £20K oynarsa /
£100K oynamazsa, satın alma opsiyonu yok, cevap süresi bir hafta. Cevap bekleniyor.

## 8.47 Diziliş değiştirmek: taktik yuvaları (8 Ekim)

FM26'da var olan taktiğin dizilişini değiştiren bir kontrol taktik ekranında yok
(`FormationOptionEvent` hiçbir kontrolde görünmüyor; `FormationEditCard` paneli var ama
`OpenPanelEvent`'in `PanelID`'si bir asset GUID'i istiyor, adla açılamadı). Oyunun yolu yeni taktik
kurmak: taktik menüsündeki ikinci liste (`ModifyTactic`, `add-data`, `IsAddingSlot = True`)
"Create New Tactic" ile aynı sihirbazı açıyor (`tac-styles-options` → `all-options-list` ×2).
Yeni taktik yeni bir **yuvaya** giriyor ve etkin oluyor.

- En fazla üç yuva (`TacticReference.TacticCount`, `CurrentTacticSlot` sıfırdan). Üçü doluyken
  "Create New Tactic" listeden kalkıyor.
- Etkin olmayan yuvalar numaralı butonlar (`EventFmxSetTacticSlot`, veri `slot1`/`slot2`); etkin
  yuva adıyla gösteriliyor. ⚠ Butonun olaylarını kuyruğa koymak (`/press`) yuvayı değiştirmedi;
  `/click` (gerçek tıklama) değiştirdi.
- Ana taktik listesinde öteki kayıtlı taktiklerin adları da var; birini seçmek onu etkin yuvaya
  **kopyalıyor** ("Copy of: 4-3-3 DM Wide - Gegenpress"), yuva değiştirmiyor.
- Alışkanlık (`TacticalFamiliarity`) yeni taktikte de 8306 göründü; bu değer taktik başına değil
  gibi, doğrulanmadı.

`fm tactic new` artık taktik varken de çalışıyor (yeni yuva), `fm tactic slot [<n>]` yuvaları
gösterip değiştiriyor. Denendi: 4-2-3-1 DM Wide / 4-4-1-1 2 DM kuruldu, yuvalar 3→2→1→3→1 gezildi.
Kayıtta şimdi üç yuva var (1: asıl 4-3-3, etkin).

## 8.48 Takım talimatları (8 Ekim)

Taktik ekranında "Team Instructions" (`TutorialActionElement`, `/press` ile) talimat görünümünü
açıyor. Topla oynarken talimatlar evre başlıkları altında: `#PhaseText` (Overview, Final Third,
Progression, Buildup), her talimat bir hücre: adı `#text`, ayarı `#text-second`. `/instructions`
bunları evre, ad, değer olarak veriyor.

Değiştirmek: her talimatın üç kontrolü var ve yalnızca biri işe yarıyor:
- `CellButton` sol tık (`TacticsTeamInstructionShowMenu`): başka bir menü, ayarları açmıyor.
- `CellButton` **sağ tık listesi** (`RightClickEvents → OpenGenericDynamicCard`): ayarların
  kartını açıyor. `/press?ptr=…&list=RightClickEvents` ile basılıyor. ✓
- `layout-tile-interaction-button` (yalnızca Overview'daki yedi talimatta): aynı kart.
Kartta ayarlar `options-list` adlı bir radyo grubu; `/select name=options-list choice=…` yazıyor,
oyuna hemen işliyor (sayfadan çıkıp dönünce duruyor). Kart açık kalınca sıradaki açılmıyor; kartın
`PanelHistoryClose` butonu (açılmadan önce olmayan) basılıyor. Kart kapanınca hücreler yeniden
diziliyor, adresleri değişiyor; hücre beklenip basma gerekirse yeniden deneniyor.

"Final Third/Dribbling" ile "Progression/Dribbling" oyunda tek ayar: biri değişince öteki de değişti.

Denendi: Tempo, Dribbling, Patience, Goal Kicks, iki tur ileri geri, 16/16. Topsuz talimatlar:
planlayıcının "Out of Possession" düğmesi bir kez görünüp sonra kayboldu, görünüm değişmedi; açık.

## 8.49 Rakip talimatları (8 Ekim)

Taktik ekranındaki "Opposition Instructions" (`OpenCardButton`, `OpenPanel`) kartı açıyor; sayfa
yeni açıldıysa ilk basış bazen cevapsız kalıyor, ikinci basış açıyor. Kartta rakibin her oyuncusu
için `#NameText` ve ardından dört isimsiz `SIDropdown`, bu sırayla (veride `OppositionPlayer…`):
`Marking` (Never / Always), `PressingTrigger` (Never / Always), `Tackling` (Normal / Easy / Hard),
`ShowOnToFoot` (Left / Right / Weaker Foot); "-" ayarsız. Sıra, yardımcının önerisini uygulayıp
veriyi (`…OppositionInstructionTable…Items.N.binding`) ekranla karşılaştırarak bulundu: Şut'ta
`PressingTrigger 2, Tackling 3` = ikinci ve üçüncü liste "Always", "Hard".

- Listeler olay göndermiyor; seçim yazılıyor ve kart kapatılıp açılınca duruyor.
- "Apply all advice" ve "Confirm" görüşmenin butonları (`StandardButton`, `SpeakToAction`);
  "Clear Instructions" `EventFmxOppositionInstructionsClear`.
- Mevki sütunu ayrı bir liste, aynı sırada; okunmuyor.

`fm opposition` tabloyu gösteriyor; `--advice`, `--clear`, `opposition Kaku Pres=Always "Sert
giriş=Easy"`. Al-Ain hazırlık maçı için yardımcının önerisi uygulandı (kaydedilmedi).

## 8.50 Antrenman yoğunluğu (8 Ekim)

"Training" bir alt sekme listesi (`SecondaryTabDropdownButton`); "Training Intensity"
`/select … fire=1` ile açılıyor. Sayfada:
- Üstte beş takım kuralı (`inputs-combobox-default-default`, `EventFmxSetTrainingIntensity`);
  etiketleri okunmadı, kullanılmadı.
- Oyuncu başına bir liste (`dropdown-base-regular-trailing-icon`,
  `EventFmxPlayerTrainingIntensityLevelChanged`): Automatic, Half / Normal / Double Intensity, "No
  pitch or gym work ›" (1–6 Days, 1–2 Weeks), "Send On Holiday ›" (1–4 weeks; yalnızca bazı
  oyuncularda). Alt menü §8.45'teki yolla seçiliyor.
- "Ask <fitness koçu> to set all training intensities" görüşme butonu.

Tablo sanal (ekranda ~15 satır); `/namedrows?dropdown=<ad>[&to=0..1]` her `#NameText`'i ardından
gelen listeyle eşleştiriyor ve kaydırıyor (`Registration.Scroll`), CLI beş noktada okuyup isimle
birleştiriyor (21 oyuncu). `fm intensity` liste, `intensity <oyuncu> <seviye>`, `--auto`.
Denendi: Kanté yarım → 3 gün dinlenme → normal, Fred çift → normal, iki tur.

## 8.51 Bireysel antrenman (8 Ekim)

"Training › Individual Training", görünüm listesi (`ViewDropdown`) "List" yapılınca satır başına
dört liste: mevki/rol eğitimi (`EventFmxPlayerTrainingPositionChanged`; "AM (R) - Playing position"
ya da "Defender (Right) › …" alt menüsünden bir rol), topsuz rol (`EventFmxPlayerOOPTrainingRoleChanged`;
"Defenders ›"…), ek odak (`EventFmxPlayerTrainingFocusChanged`; Endurance, Quickness, Free Kick
Taking…) ve yoğunluk. İlk üçü aynı eleman adını taşıyor; `/namedrows?count=3` her ismin ardından
ilk üçünü sırayla veriyor. Alt menülü seçim §8.45 yoluyla.

"Ask <yardımcı> to assign all individual training" görüşme butonu da var; kullanılmadı.
Denendi: Kanté odak Endurance ↔ None, mevki "Midfielder (Centre) › Central Midfielder" ↔ asıl
mevki, iki tur.

## 8.52 Oynanarak öğrenilenler (8 Ekim, 5–6 Ağustos 2025 oyun içi)

- **Maç sonrası sayfa listesi boş.** Anında simüle edilen maçtan sonra oyun "Results" durumunda
  (`State = Results`, ilerle butonu "Response Required") ve `/pages` boş dizi dönüyor; navigasyon
  menüleri kurulmamış. Sayfa açmaya çalışan her komut "no page named" diyordu. `OpenPage` artık
  listeyi 5 sn bekliyor, `match --instant` fikstürü okuyamazsa raporu yine basıyor.
- **Basın toplantısı** `reply <#> Attend Press Conference` + `talk` ile soru soru yürüyor
  (soru, gazetecilerin tepkisi, cevap seçenekleri gruplu). Ayrı komut gerekmedi.
- **Sezon kaptanı mesajı** ("Choose … captain"): "Confirm Captains" kaptan boşken bir diyalog
  açıyor ("You must select a captain before confirming"). "Review Captaincy" mesajın içinde
  `GenericDropdown`'lar açıyor (Select Captain / Change Vice Captain, `SpeakToAction`); seçim bir
  onay diyaloğu daha açıyor (Yes). Sonra Confirm Captains kabul edildi.
- **Kiralık karşı teklifi** mesajdaki "View Offer" ile teklif ekranı olarak açılıyor ama başlığında
  takas bölümü yok; `/offer` kökü artık başlıktan da bulunuyor. Karşı teklife kartla cevap gitti.
- **Rakip önerisi** kart açılır açılmaz "Apply all advice" butonu henüz yok; birkaç yüz ms sonra
  geliyor. Öneri uygulanırken tablo bir an eksik satırlarla diziliyor.

## 8.53 Oyuncu talimatları (8 Ekim)

Bir mevkinin rol listesinde (`RoleOption`) "Go to Player Instructions" seçilince (`fire=1`) oyuncu
talimatları kartı açılıyor. Talimatlar döşeme (`layout-tile-interaction-button`): adı
`#title-text`, ayarı `#name`; döşemenin üzerinde ayarı tek tek kaydıran `#leftarrowbutton` /
`#rightarrowbutton` da var. Döşemeye basmak ayarların listesini (isimsiz radyo grubu) ve bir
açıklama kartı açıyor; `/select` yazıyor, kalıcı. Kartlar kendi `PanelHistoryClose` butonlarıyla
kapatılıyor; talimat kartı açık kalırsa taktik ekranının rol listeleri görünmüyor.

Kartta ayrıca "Apply to: Position / Player" (`EventFmxDisable/EnablePositionPersonalisedRole`,
talimatlar mevkiye mi oyuncuya mı) ve rolün kendisi var; dokunulmadı. Döşemeleri `/tiles` okuyor.

`fm tactic pi <mevki> [<talimat> [<değer>]]`. Denendi: DR Shooting / Forward Runs, AML Shooting,
iki tur ileri geri; ayarlar eski haline döndü.

## 8.54 Maç içinde: mentalite ve bağırış (8 Ekim, Samsunspor deplasmanı)

Maç sırasında sayfa kataloğu yok, taktik sayfası açılmıyor. Maç ekranının kendi kontrolleri:
"Tactics" (`MatchOpenTactics`), "Instructions", bağırışlar (`shouts-dropdown`,
`TouchlineShoutMenu`), mentalite listesi (`dropdown-base-regular`, Very Defensive…Very Attacking)
ve her oyuncu için bir menü (roller ›, Player Instructions, Touchline Shouts ›, Substitute Off ›).

- **Mentalite** listesi olay göndermiyor. Satırın indeksle seçilmesi (`OnSelectionChangedInternal`)
  değeri sıfırladı (`Selected = -1`); satırın kaydını bağlı değere yazmak (`/select path=N`,
  §8.45'teki `rowdata`) işledi: maçın taktiği (`..MatchUI2D.Tactic.MentalityString`) Positive,
  Attacking, Balanced, Cautious oldu. `fm tactic mentality` maç sırasında bu listeyi kullanıyor.
- **Bağırış** listesinin satırları `{Title, Description, TouchlineShoutAction, TouchlineShoutType}`;
  seçim olay gönderiyor. Etkisi veriden okunamadı (`HavePendingTouchlineShout` bağlanmadı);
  `fm shout` var ama işlediği doğrulanmadı.
- Lig maçından önce oyun "Defrel is ineligible" diye kadroyu reddetti; Quick Pick düzeltti.

## 8.55 Transfer dönemi (8 Ekim)

`Human.Team.Division.TransferWindowData` (`TransferWindowReference`): `TransferWindowStatus`
(10 Ağustos 2025'te 1, dönem açık), `TransferWindowDaysRemaining` (33), `StartDate`, `EndDate`
(`GameDate`, çözülmemiş), giriş/çıkış sayıları ve değerleri. İlk okumada bağlanıyor, `Pull`'un
yeniden denemesi yetiyor. `fm status` "Transfer: dönem açık, 33 gün kaldı" yazıyor. Kapalı
dönemdeki değer görülmedi (1 dışındaki her şey "kapalı" sayılıyor).

Topsuz takım talimatları için bir deneme daha: "Team Shape" görünümündeki "Out of Possession"
sekmesi (`TutorialActionElement` içinde `SIButton #OOP`?) basıldıktan sonra talimat görünümü yine
topla oynarkenkileri gösterdi. Açık.

⚠ Bu deneme sahayı "Out of Possession" görünümünde bıraktı ve `fm tactic` ilk 11'i boş okudu: saha
on bir slotu yalnızca **Combined** görünümde diziyor (In Possession / Out of Possession / Both'ta
`/tactic` `Slots = []`). `Planner()` artık slotlar boşsa "Combined"a basıp yeniden okuyor.

**MCP gerçek istemciden.** Claude Code oturumunda yüklü `fm` MCP sunucusunun araçları çağrıldı:
`fm_status`, `fm_tactic` çalıştı, çıktı CLI'ınkiyle aynı. Ama sunucu süreci (`fm mcp`) oturum
başında başlamış ve o anki derlemeyi tutuyor: gece eklenen araçlar (`fm_opposition`, `fm_intensity`…)
ve `fm_status`'un transfer satırı yoktu, `fm_contract`'ın açıklaması eskiydi. Yeni derlemeden sonra
istemcide MCP sunucusunu yeniden başlatmak gerekiyor (Claude Code'da `/mcp`).

## 8.56 Kısa liste (8 Ekim)

"Shortlists" alt sekmesi personel kısa listesiyle karışık bir genel sayfa açıyor ve oyuncu tablosu
yok. Oyuncu kısa listesi Recruitment › Overview'daki "Shortlists" döşemesinden
(`layout-tile-interaction-button`) açılıyor; tablosu `/people` ile oyuncu veritabanı gibi okunuyor
(Demirbay, Cancellieri, Icardi, Osimhen — teklif verilenler otomatik ekleniyor). `fm shortlist`.

## 8.57 Rakip raporu (8 Ekim)

Match Day › "Next Opposition Report" özet döşemeleri düz metin: takım, lig, Form, Most Used
Formation, Top Goalscorer / Most Assists / Highest Average Rating (sayı + isim), Team News
satırları. Okumak için genel bir yol: `/texts?anchor=<metin>&up=N` o metni bulup N üst elemana
çıkıyor ve altındaki görünen bütün metinleri okuma sırasıyla veriyor. `fm opponent`.
Denendi: Feyenoord (rövanş öncesi), iki kez.

## 8.58 Kaptanlar (8 Ekim)

Kaptan ve yardımcısı veride: `Human.Team.Captain` / `Human.Team.ViceCaptain` (`person`), sayfa açmadan
okunuyor. Squad › Dynamics › Captains (alt sekme listesinden) iki `GenericDropdown` gösteriyor:
"Change Captain" ("Select Captain" boşken) ve "Select Vice Captain"; seçim olay gönderiyor ve bir
onay diyaloğu açıyor ("Are you sure…", Yes). Seçili olanın adı listede "(Selected)" ekiyle.
⚠ Yardımcı kaptan atandıktan sonra sayfa yalnızca kaptan listesini kuruyor; yardımcıyı değiştiren
liste yok (sayfa yeniden açılınca da). 7 Ağustos'taki sezon başı kaptanlık mesajında ikisi birden vardı.

Bu deneme için Kerem Aktürkoğlu yardımcı kaptan yapıldı (önceki boştu). `fm captains [--captain X]
[--vice Y]`.

## 8.59 Oyuncu mutluluğu (8 Ekim)

Squad › Dynamics alt sekme listesinden "Happiness". Kadro tablosu okuyucuları (`/squad`, `/people`)
burada bayat veri veriyor; sayfa düz metinden okunuyor (`/texts`, bir oyuncunun mutluluk
seviyesinden 12 üste çıkıp): isim, "N year-old …" tanımı, seviye (Very Unhappy…Very Happy), ardından
durum bayrakları (Wnt, Unr, FtT, Rst). Üstte takımın genel durumu bir cümle ("The players are
currently content…"). `fm happiness` en mutsuzdan sıralıyor. 10 Ağustos'ta en düşük "Slightly Happy"
(Álvarez, Kerem).

## 8.60 Maç hazırlığı (8 Ekim)

Match Day › "Match Preparation Training" bir kart olarak açılıyor (`panel …, card`); içinde taktik
hazırlık (topla: Progression, topsuz: Mid Block) ve duran top hazırlığı (Attacking Free Kicks,
Defending Corners), her biri bir açıklama cümlesiyle. `/texts` ile okunuyor, `fm prep`.
Değiştirmek açık: kartta ne `/buttons` ne `/dropdowns` bir kontrol gösteriyor; Match Day
genel bakıştaki "Tactical Preparation" döşemesi de yeni bir şey açmadı. 9 Ekim gecesi `/form` ile
bakıldı: kartta kutu, kaydırıcı, radyo da yok, yalnızca metin. Hazırlık büyük olasılıkla antrenman
takviminden (Training › Schedule'daki maç hazırlığı seansları) geliyor; oradan denenecek.
⚠ Duran top sırası sabit değil: 23 Ağustos'ta önce "Defending Free Kicks" geldi; `fm prep`
"hücum/savunma"yı artık sıraya göre değil adın başına göre yazıyor.

## 8.61 Oyun ayarları: Preferences, kaydırıcı ve aç/kapa (8→9 Ekim gecesi)

**Yol.** Üst çubuktaki etiketsiz `dashboard-hud-fm-button` FM menüsünü açıyor (Save Game,
"Saves automatically every week", Preferences, Hall of Fame… döşemeleri). "Preferences"
döşemesinin butonu ("Manage settings for this game") özet sayfayı açıyor: Region (dil, para
birimi), Interface, Saves, Sounds. "See all" bütün tercihleri sekmeli açıyor: Your World,
Audio & Visual, Accessibility, Match Day, Processing, Third Party, Shortcuts
(`DetailedPreferencesTabButton`). Bu katman `/buttons` listesinde görünmüyor (olay taşıyan
kontrol az), ama `Elements()` içinde; metinle tıklamak için `/click?text=<yazı>[&name=<buton>]`
eklendi: metni bulup üstündeki ilk `SIButton`'a basıyor. ⚠ "Match Day" hem sekme hem ana menü
butonu; `name=DetailedPreferencesTabButton` olmadan ana menüdeki açılıyor ve ayarlar kapanıyor.

**Okumak.** `/form?anchor=<metin>&up=N`: `/texts` gibi, ama metinlerin arasına kontrolleri de
okuma sırasıyla koyuyor (`dropdown`, `radio`, `slider`: Value/Low/High, `checkbox`: Value).
Her ayar başlığının altındaki ilk kontrol. Bütün tercihler için `anchor=All Preferences&up=6`.
Your World'de kayıt bloğunda iki başlık ("Every", "Auto save type") iki listeden önce geliyor;
o ikisi sundukları bir seçenekle ayırt ediliyor (Fortnight / Single File). Sekme birkaç karede
doluyor: başlıklar listelerden önce gelebiliyor.

**Değiştirmek.** Liste ve radyo grubu her yerdeki gibi `/select` (bu listeler olay taşımıyor,
değeri yerel bir değişkene yazıyor). `SISlider` için `slider.value = n` (adım indeksi; etiketi
hemen değişiyor), `SICheckBox` için `SimulateCheckboxClick` — ikisi `/setcontrol?ptr=&value=`.
**Hiçbiri tek başına işlemiyor:** sayfanın altındaki "Confirm" (`PreferencesConfirmChangesEvent`
+ paneli kapat) basılınca işleniyor. Kapatma çarpısı `CancelPreferenceChangesEvent` gönderiyor.
Kanıt: kayıt sıklığı "Day" seçili ama onaysızken iki gün geçişinde kayıt alınmadı; onaydan sonraki
ilk gün geçişinde oyun kaydetti ve FM menüsü "Saves automatically every day" yazdı.
`Settings.MatchSpeedDuringHighlights` onaydan hemen sonra 2'ye döndü ve oyun yeniden açılınca da
öyle kaldı (ayarlar kayda değil oyunun kendi tercih dosyasına).

**Kaydırıcı adımları** (etiketler sayfadan okundu): vurgu sırasında 0.5x, 0.75x, 1x, 1.25x,
1.5x, 1.75x, 2x, 2.5x, 3x, 4x, 10x (indeks 0–10, varsayılan 1.5x); vurgular arası 4x, 8x, 10x,
20x, 30x, 40x (0–5, varsayılan 30x). Maç gösterimi radyo grubu `HighlightModes`: Full Match,
Comprehensive, Extended, Key, Only Commentary.

`Game.AutosaveModeAndIntervalString` `/pull` ile bir kez bağlanınca güncellenmiyor gibi
(değişiklikten sonra "every week" kaldı); FM menüsündeki metin oyun bir sonraki kaydı alınca
değişti. Ayarı doğrulamak için sayfayı yeniden açıp okumak gerek.

`fm settings`: çıplak hali gösterir ve terminalde hangisinin değişeceğini sorar;
`settings <ayar> [<değer>]`. Para birimi Euro'ya alınınca `fm player` değerleri € ile verdi.
Dil değiştirmek denenmedi: komutlar İngilizce metinlere bakıyor, geri dönmek de Türkçe arayüzde
gezinmeyi gerektirir.

## 8.62 Maç ekranı: bağırışın izi, rol, Dugout tavsiyeleri (9 Ekim gecesi, Feyenoord rövanşı)

**Bağırış doğrulandı.** Bağırış seçilince oyun bağırış listesini kapatıyor (`#shouts-enabled`
`DISABLED`, `/dropdowns` `Enabled = False`) ve birkaç oyun dakikası sonra açıyor (22'de Encourage,
28'de yeniden açıktı). Anlatım da söylüyor: Focus'tan sonra "Faruk Kavlak is clearly demanding
concentration from his players now". Liste kapalıyken seçmek hata vermiyor ama bir şey yapmıyor
(eski `fm shout` "başarılı" diyordu); artık reddediyor ve bağırıştan sonra listenin kapandığını
görmeden "oldu" demiyor.

**Rol.** Her oyuncunun maç menüsünde iki rol satırı var: topla ("Centre Forward ›": Deep-Lying
Forward, Centre Forward, Target Forward, Poacher, Channel Forward, False Nine) ve topsuz ("Centre
Forward ›": Centre Forward, Tracking…, Central Outlet…). `fm sub`'daki yol (`/select path=3,k
fire=1`) işliyor; satırın etiketi hemen yeni rol oluyor ("Poacher ›"). Menüde ayrıca oyuncuya
özel bağırış alt menüsü var (komutu yok). Maç içi rol değişikliği takımın kayıtlı taktiğine
yazılmıyor (maç sonrası `fm tactic` yine Centre Forward).

**Dugout.** Maç ekranının sol tarafında bir döşeme: son birkaç tavsiye zaman çizelgesinde,
her biri dakikasıyla bir buton (`DugoutEvent0…5`, `EventFmxMatchDugoutJumpToEvent`). Seçilenin
metni bir konuşma balonunda (`#text`); altında "Do it" (`EventFmxMatchDugoutConfirmEvent`),
kaldır (`…DismissEvent`), önceki / sonraki. Görülen tavsiyeler: "Add opposition instruction for
Hwang In-Beom: Go hard when tackling", "Add team instruction: Discourage Long Shots / Work Ball
Into Box", "Tell Talisca: Shoot Less Often", "If you wish to make a change I think we should
introduce Mert Müldür.", ve eylemsiz yorumlar ("Feyenoord seem to be pushing players forward…").
Çizelge kayıyor: eski olaylar düşüyor. ⚠ Döşeme **pozisyon oynatılırken gizleniyor**, aradaki
hızlı bölümde görünüyor (2 sn'lik yoklamayla 78–79' gizli, 80–95' görünür); gizliyken butonlar
listelenmiyor. Veride `MatchDugoutEventCount`, `MatchDugoutTimelineEvent0..5.Text` var ama maç
referansının yolu bulunamadı (`..MatchUI2D` ile `/pull` oyunda hata fırlattı).

"Do it"e basınca panel bazen bir sonraki tavsiyeye geçiyor; etkisi (Talisca'ya "Shoot Less
Often") maç sonrası taktikte görünmedi — maç içi değişiklikler maçın kendi taktiğinde kalıyor.
**İz şu:** uygulanan tavsiyeye geri dönünce "Do it" kapalı (`Enabled = False`); kaldırılan
tavsiye çizelgeden düşüyor. Göztepe maçında "Discourage Long Shots" ve bir yorum (kaldır) böyle
doğrulandı. Eylemsiz yorumlarda ve skor kartlarında ("FB 0 - 0 GÖZ — Fenerbahçe move up to 13th
position") "Do it" baştan kapalı; maç başında bir de "No Dugout Events" yer tutucusu var.
"Marco Asensio is struggling and wants to be substituted" uygulanabilir göründü (Do it açık;
büyük olasılıkla değişikliği yapıyor), denenemeden çizelgeden düştü. Çizelge hızla kayıyor:
numara iki komut arasında değişebiliyor (terminalde listeden seçmek bu yüzden daha sağlam).

**Saat donabiliyor.** Konyaspor maçında korner sonrası `MatchTime` 45 sn'den uzun 17:57'de kaldı,
anlatım akmaya devam etti; `fm match` "maç durdu, karar bekliyor" deyip çıktı, oyun ise devre arasına
kadar kendi gitti. Artık anlatım değişiyorsa maç sürüyor sayılıyor.

Maç: Fenerbahçe 0-0 Göztepe; Fenerbahçe 1-0 Konyaspor (Kerem 18'). Göztepe maçında `fm sub`'ın kulübeyi boş gösterdiği görüldü: kalecinin
menüsünde kulübe alt menüsü gelmiyor; artık kulübe dolu olan ilk menüden okunuyor.

**Saha.** Maç ekranında her oyuncu menüsünden önce mevkisini yazan bir buton var ("GK", "D (R)",
"D (C)", "DM", "M (C)", "AM (L)", "ST (C)"; `OpenDescendantDropdown`), menülerle aynı sırada. Her
hat **sağdan sola** listeleniyor (DR, DCR, DCL, DL): ilk çizimde stoperler ve merkez orta sahalar
ters düşmüştü. `fm sub` terminalde on biri bu mevkilere göre sahaya çiziyor (Kasımpaşa maçında
denendi, `ptyout.py` ile stdin kapalı sahte terminalde).

`fm dugout` (liste, `<no> uygula | kaldır`), maç sırasında `fm tactic role <oyuncu> [<rol>]
[--oop]`, `fm match --play` akışında yeni tavsiye "Yardımcı: …" satırı.

Maç: Fenerbahçe 1-1 Feyenoord (Talisca pen. 79'); ilk maç 0-1, toplam 1-2.

## 8.63 Oyuncunun kariyeri (9 Ekim gecesi)

Oyuncu sayfasındaki "Career Stats" döşemesi (`layout-tile-interaction-button`) sezon sezon tabloyu
açıyor; `/texts?anchor=Total Apps&up=16` düz metin olarak veriyor: başlık (Sel., Year, Team, Info,
Nation, Division, Total Apps, Goals | kalecide Cln Sh, Ast, PoM, Avg Rat), sonra satırlar, sonra
"Total" ve toplamlar (ödenen bonservislerin toplamı, maç, gol, asist, PoM, puan).

Satır uzunluğu değişken: "Info" yalnızca o kulübe gelişte dolu (£19.5M, Loan, Free, Undisc.).
Sabit olan son yedi hücre (ülke, lig, maç, gol, asist, PoM, puan); baştan yıl ve kulüp; arada
kalan geliş. Yıl "2025/26" ya da takvim yılı ligleri için "2020". ⚠ Bazı oyuncularda "Total"
etiketi genişliksiz çiziliyor ve `/texts`'te çıkmıyor; o zaman toplamlar son beş sayı (önlerinde
para birimiyle başlayan bonservis toplamı). ⚠ Sayfa açılır açılmaz döşeme yok, tablo da birkaç
karede doluyor (ilk okuma bir önceki oyuncunun ya da yarım tabloyu verdi); iki ardışık okuma
aynı olunca alınıyor. Sayfa en son kariyer sekmesinde bırakıldıysa döşeme yok, tablo zaten orada.

`fm player <isim> --career`: Kerem, Talisca, Škriniar, Ederson (kaleci), Osimhen (kadro dışı).

## 8.64 Topsuz takım talimatları: bir deneme daha (9 Ekim gecesi, açık)

Taktik sayfasının kökündeki `BindingSetter #InPossessionSetter` kendi kapsamında `inpossession`
diye bir değişken tutuyor (`True`). Rol menüleri (`TacticRoleMenuShowEvent`,
`m_inPossessionBindingPath`) ve tavsiye listesi (`AdviceOptionsSelectionEvent`) bunu okuyor.
Değişkeni yazmak için köprüye `/scoped?ptr=<BindingSetter>&path=<ad>[&from=<yol>]` eklendi:
setter'ın `Key`'i ile yolun anahtarını hesaplayıp (`PathKey.Of(root, path)`) `from`'daki değeri
(`const.False` / `const.True`) oraya yazıyor — SetDatastoreValue'nun yaptığı gibi. Yazma işledi
(`True -> False`), ama talimat görünümü topla oynarken talimatlarını göstermeye devam etti,
"Team Instructions" yeniden açılınca da. Talimat hücreleri `data.TacticTeamInstruction`
kayıtlarına bağlı; taktikte `InPossessionInstructionList` ve `OutOfPossessionInstructionList`
var ama `Human.Team.Tactic` altında değer yok (planlayıcının taktiği başka bir yolda, `/tactic`
onu ağacı gezerek buluyor). Değişken geri `True` yapıldı. Sıradaki deneme: planlayıcının tactic
düğümünün yolunu bulup `OutOfPossessionInstructionList`'i oradan okumak.

## 8.65 Bütçeler ve transfer ↔ maaş kaydırıcısı (9 Ekim gecesi)

Recruitment › Recruitment Budgets: döşemeler düz metin — "Transfer Budget" (Current, Spent this
season, Original Budget), "Wage Budget" (Available, "Currently spending …"), "Scouting".
"Adjust Transfer and Wage Budget" bir konuşma seçeneği (`SpeakToAction`); basınca "Budget
Adjustment" kartı açılıyor: kurulun cümlesi ("The board will not allow you to increase the transfer
budget as there is not enough excess wage budget to compensate."), maaş ve transfer bütçesi,
**`SISlider` 0–15** (bu kayıtta 15 = transfer en yüksek), "Cancel" / "Got It". `/setcontrol` ile
adım değişince rakamlar bir iki kare sonra güncelleniyor ve yürürlükteki adımdan farklıysa
sonlarına `*` konuyor (bayat okumayı bu yıldızla ayırmak gerekti: bir okumada 13. adım 12'ninki
geldi). Adımlar doğrusal: her adım transferden ~£738K alıp maaşa haftalık ~£14.2K ekliyor; 0'da
transfer £40, maaş £2.34M p/w. Kart sayfada iki kopya (aynı değere bağlı).

Denendi: 14. adım "Got It" → sayfa transfer £10.33M, boşta maaş £36K p/w; 15'e geri → £11.07M,
£22K. Liste üç kez okundu, 16 farklı adım. `fm budget`, `fm budget ayarla [<adım>]`.

## 8.66 Takvim ve sezonun bütün fikstürü (9 Ekim gecesi)

Match Day › Fixture Schedule yalnızca ~10 maçlık bir liste (kaydırınca da 10; `/scroll` eklendi:
metnin içinde durduğu listeyi bulup `to=0..1` kaydırıyor). Sezonun tamamı **Portal › Calendar**'da:
`MonthDropdown` (Haziran 2025 … Temmuz 2026) bir ayı ızgara olarak çiziyor; `/texts?anchor=<Ay
Yıl>&up=14` hafta günü başlıklarından sonra gün gün okuyor. ⚠ Bu ay seçici sayfa kapandıktan
sonra da arka planda yüklü kalabiliyor; Fixture Schedule açıkken görülüp sanki onunmuş gibi
yanıltmıştı.

Bir gün: "28 Jul" (ay başında ayın adıyla) ya da "29"; sonra "Training", maç ("Match",
turnuva, saat ya da skor, rakip, lig sırası "7th" yalnızca ligde, H/A/N), altyapı maçı (kısa kod
"U19" ya da hazırlık için "FR", "KNY U19s", H/A, saat/skor), ve notlar ("Transfer window opens",
"Transfer window closes (midnight)", "Turkish Super Cup Final", "Fenerbahçe enter Turkish Super
Cup", milli maçlar "Bulgaria", "v", "Türkiye"). Skor **ev–deplasman** yazılıyor ("2-4" deplasmanda
4-2 galibiyet), penaltıyla biten "1-1p". Izgara birkaç karede çiziliyor (bir okumada U19 satırı
yarım geldi): iki ardışık okuma aynı olunca alınıyor; sayfa açılır açılmaz seçilen ay bazen
tutmuyor, bir kez daha seçiliyor.

`fm calendar [ay]` (Türkçe/İngilizce ay adı, numara, "2025-09"; `--u19`), `fm fixtures --all`
(14 ay, ~14 sn): 44 maç, hazırlık maçlarından 17 Mayıs'taki son lig maçına.

## 8.67 Gelen transfer teklifi (9 Ekim gecesi, 7 Eylül 2025 oyun içi)

Mesaj: "Kocaelispor make Levent offer" — Accept, Reject, View Offer, Delegate. "View Offer" teklif
ekranını açıyor ve bu ekran bizim teklif kartımızla aynı yapıda: `/offer` kulüp, oyuncu, bütçe
maliyeti, şartlar (bonservis £775K, iki taksit £625K, 20 maç sonrası £155K, sonraki satıştan %20)
okunuyor; kontroller `Enabled = False` (karşı tarafın teklifi). Ekranda tek konuşma seçeneği
"Accept Offer". Reddetmek mesajdan: `reply <#> Reject` (konuşma açıkken de mesaj cevaplanabildi).
`fm offer` artık argümansız çağrılınca ekrandaki teklifi çiziyor.

Maç sonrası "Match Reaction" sorusu: oyun `CanContinue` derken bir konuşma (soru + cevaplar) açık
kalıyor; Continue onu `EventFmxPressConferenceEarlyExit` ile geçiyor. `fm status` artık soruyu
söylüyor, `fm next` duruyor (`--skip` ile geçer).

## 8.68 Liste okumasında bayat satırlar (9 Ekim gecesi)

Gençlerbirliği 4-0 Fenerbahçe maçından sonra `fm report` golleri doğru isimlerle ama önceki maçın
dakikalarıyla verdi (17', 50', 53', 66' — Başakşehir maçındaki gollerimizin dakikaları; anlatımda
goller 2', 25', 60', 81'). Sebep `/pull?list=1`: her girdi `fmcli.<liste>.<n>` düğümüne konuyor ve
özellikleri onun altında bağlanıyor; aynı düğüm sonraki okumada yeniden kullanılınca altındaki
`TimeString` eski değerini veriyor (yeni girdi düğüme yazılsa da çocuklar tazelenmiyor). Artık her
okuma `?gen=` ile kendi satırlarını açıyor (CLI bir okumanın denemeleri boyunca aynı, okumadan okumaya
farklı nesil veriyor) ve her kayıt kendi satır yolunu (`Row`) taşıyor; iç içe okuma (golcünün adı)
oradan yapılıyor. Düzeltmeden sonra rapor 2', 26', 60', 82' verdi, dört okumada aynı.
`fm squad` (terminal görünümü 30 oyuncuyu bu yolla okuyor) yine ~0,2 sn. ⚠ Bedeli: her okuma
bağlama ağacına yeni düğümler ekliyor ve hiçbiri silinmiyor; çok uzun bir oturumda ağaç büyür.
Gerekirse eski nesillerin düğümlerini kaldırmak (ya da satır sayısıyla sınırlı bir halka) eklenebilir.

## 8.69 Sakatlık durağı (9 Ekim gecesi, Fenerbahçe–Nott'm Forest, UEFA Avrupa Ligi)

16:36'da Kanté bileğini burktu. Oyun taktik ekranını **açmadı** (8.34'teki ilk maçta açmıştı);
maç ekranında kaldı ve durakladı: `/state`'te `Playing` alanı yok, saat duruyor, onay penceresi
yok. Dugout'a bir not düştü: "N'Golo Kanté has picked up an injury and we should substitute him as
soon as possible. It looks like he's suffered a twisted ankle." (Do it açık). Ekranda bir de
**"Confirm Changes"** (`button-primary-leading-icon-label`) belirdi.

`fm sub Kanté Fred` değişikliği verdi ("ilk duraklamada uygulanır") ama maç durmaya devam etti;
"Confirm Changes"e (`/press`) basınca 4 sn içinde `Playing = True`, saat 19:34, Fred sahada
("Seemed calm coming onto the pitch"). Yani durak: değişiklik → Confirm Changes.

Artık: `fm match --play` durağı hemen tanıyor (Playing yok + Confirm Changes), yardımcının notunu
yazıyor; terminalde değişikliği soruyor ve sürdürüyor, script'te `sub <çıkan> <giren>` diyor ve
çıkıyor. `fm sub` durakta değişikliği yapıp Confirm Changes'e kendisi basıyor; durakta yeniden
çağrılan `fm match` değişiklik yapmadan sürdürüyor. Yeni akış canlı bir durakta henüz
denenmedi (bu maçta elle yapıldı).

Beşiktaş derbisinde (2-3) rapor üçüncü golü atladı: olay listesinde penaltı golü `EventType = 2`
("pen 56'"), normal gol 0. Rapor artık ikisini de sayıyor; kendi kalesine golün türü görülmedi.
91'de VAR bir golü iptal etti: skor 3-3 olup 2-3'e döndü; akış bunu "GOL İPTAL (VAR)" diye yazıyor.

Aynı maçta diğer maçların skor kartları gol atanla ve çok satırlı geldi ("LIN 0 - 1 VFB Linfield 0
VfB Stuttgart 1 (Bilal El Khannouss 1)\nFenerbahçe drop down to 12th position"); skor kartı
süzgeci artık satır sonlarını da kapsıyor.

## 8.70 Turnuva liderleri (9 Ekim gecesi)

Match Day menüsündeki "Competitions ›" alt menüsü kulübün turnuvalarını veriyor (UEFA Champions
League, UEFA Europa League, Super League, Turkish Cup, Turkish Super Cup; sıra değişebiliyor, ad
ile seçmek gerek: `/select names=Competitions>Super League&fire=1` — `names` alt menü işaretsiz
adı istiyor). Turnuva sayfasında bir "Stats" döşemesi düz metin: "Teams" (Most Goals, Most
Possession, Most Yellow Cards, Most Clean Sheets) ve "Players" (Highest Average Rating, Most Goals,
Most Assists, Most Clean Sheets), her biri etiket, isim, sayı. Yeni başlamış bir turnuvada isim
boş kalabiliyor (Türkiye Kupası'nda üçlüler kaydı); etiketlere göre ayrıştırılıyor. Turnuva
değişince önceki döşeme bir an ekranda kalıyor: değişene ya da iki okuma aynı olana kadar bekleniyor.
Tam gol krallığı tablosu bu döşemede yok (yalnızca birinci). `fm league [turnuva]`.

## 8.71 Kadro sağlığı (9 Ekim gecesi)

Kadro tablosunun "Injuries" görünümü: `IsInjured`, `InjuriesTimeMissed` ("3 days"), sakatlık adı
("A twisted ankle"), derecesi; sakat olmayanlarda da bir süre yazıyor (geçmiş). "Risk Assessment":
`OverallInjuryRisk` basamaklı bir puan (bu kadroda 75, 92, 150, 165, 275, 385; en çok oynayanlar
385), `InjurySusceptibility`, `MatchLoad`, `Load`. Kondisyon ve maç formu her görünümde var.
`fm health` sakatları, kondisyonu %85 altı, maç formu %60 altı olanları ve risk puanı en yüksek
olanları tek raporda veriyor. Puanın oyundaki karşılığı (düşük / yüksek) bilinmediği için
yalnızca en yüksek basamak gösteriliyor.

## 8.72 Sözleşme takvimi (9 Ekim gecesi)

Kadro tablosunun Contract görünümü bitiş tarihini bağlamıyor (yalnızca "Full Time"). Veriden:
`Human.Team.Players` liste okumasının her satırının (`Row`) altında `FullContract.EndDateDescription`
("30/6/2028"). 28 oyuncu önce hepsi bağlanıp sonra okununca ~1,4 sn. Kerem ve Škriniar 30/6/2029,
`fm player` ile aynı. `fm contracts`: en yakın biten önce, bir yıldan az kalan kırmızı, iki yıldan
az sarı.

## 8.73 Antrenman programı (10 Ekim)

Squad › Training › Overview'daki "Training" kutusu bu haftayı ve gelecek haftayı takvim olarak
çiziyor (Training › altında ayrı bir Schedule sayfası yok). ⚠ Alt sekme menüsündeki "Overview"
Youth Setup / Dynamics / Medical Centre menülerinde de var: `/select?name=` ayırt edemiyor,
`/dropdowns?json=1`'den Label "Training" olanın işaretçisiyle seçiliyor. Menü "Overview" seçili
görünse de Squad'ın kendi Overview'u açık kalabiliyor; orada takvim günleri çizilmiyor (kutular
"kapalı"), o yüzden seçim her seferinde yeniden gönderiliyor.

`/texts?anchor=Thur&up=3` (`#Calendar`): "Mon"…"Sun", sonra gün gün: gün ("29 Sep" ızgara ya da ay
başında, sonra "30") ve üç seans — seans adı ("Recovery", "Ball Retention", "Match Tactics",
"Attacking Shadow Play", "Rest", "Travel"…) ya da maç günü maçın yeri ("H", "A", "N"). 5 Ekim'de
okununca: 29 Eylül–12 Ekim, iki deplasman maçı (2 ve 5 Ekim). Üç farklı sayfadan başlayıp aynı
çıktı. `fm schedule [--json]`. Değiştirmek (seans koymak, PLAN "Antrenman programı değiştir")
denenmedi.

## 8.74 Antrenman raporu (10 Ekim)

Oyuncu sayfasındaki "Training Rating" döşemesi (`layout-tile-interaction-button`) oyuncunun antrenman
sayfasını açıyor; konuşma alanında "Request Training Report" bir liste (`GenericDropdown`,
`SpeakToAction`): seçenekleri antrenörler. Biri seçilince cevap "Okay, I'll sort that out for you now."
ve mesaj **hemen** geliyor: "Fred's training report" (oyuncunun kısa adıyla). ⚠ Cevap ekranda kalıyor
ve liste "Acknowledge and Move On" basılana kadar yok; sayfa oyuncudan oyuncuya yeniden kullanılıyor.
⚠ Gelen kutusu verisi bağlı değilken okumak (`Bound`) Portal'ı açıp antrenman sayfasını kapatıyor:
önce okunmalı.

Mesaj: gövde "I've put together a training report on Fred as requested." + bulgu cümleleri; ek
"Progress Report": `/texts?anchor=Attachments&up=2` → gövde, "Attachments", "Progress Report", gidişat
("Player is showing signs of decline" / "…performing steadily"), "Attribute", özellik/değer çiftleri
(oyuncuya göre değişiyor: Fred'de Strength/Agility/Pace/Stamina, Talisca'da yalnız Free Kick Taking),
"Training", gidişat yine, puan "4.70", "T.Rat", bir seviye ("None", "Half"; simgesi
`scaling-icons-condition-level-…`, yoğunluk ya da odak değil — **yük olduğu tahmin**), birim
("Def. Unit"), sonra "Player Report" kartı. Cevaplar: Apply Advice, Delegate…, Criticise …'s Training.

Gövdede oyuncu adı `<link>` içinde: `/elements?text=` ham metinde aradığı için bulamıyordu ve
`reply` gövdeden yalnızca ilk cümleyi gösteriyordu; eklenti artık görünen metinle de eşleşiyor.

`fm player Fred` açılmıyordu: oyunun araması yalnızca ilk birkaç sonucu listeliyor ("Fred" için
dört; bizim Fred yok). Kendi oyuncumuz aramada yoksa First Team tablosundaki `NameButton`'dan
açılıyor (aynı ad iki kez yüklü olabiliyor, `any=1`).

## 8.75 Alışkanlık öğretme (10 Ekim)

Oyuncunun antrenman sayfası ("Training Rating" döşemesi), sonra oradaki "Traits" döşemesi: yardımcı
antrenörle konuşma alanında "Start New Trait Training" (`GenericDropdown`; kategoriler — Movement,
Passing, Finishing, Defending, Technique alt menülü, "Discipline Training" tek başına — ve içlerinde
**yalnızca öğrenebileceği** alışkanlıklar; `/dropdowns?deep=1` alt menüleri veriyor, `/select
names=Kategori>Alışkanlık&fire=1`), "Remove Trait" (sahip oldukları), "Ask For Trait Training
Suggestion". Bir alışkanlık çalışılırken bunların yerine yalnızca "Cancel Current Trait Training".

Yardımcının görüşü **onay penceresi** olarak geliyor (`/dialog`, "Start training" / "Don't start
training", iptalde "Unfinished Training"): "Too Old To Change Game" (Fred, 32), "Really Unsuitable
To Add" / "…To Remove". Öneri varsa o da pencere ("Player Trait Suggestion: … long flat bullet
throws"), yoksa konuşmada cümle ("I can't think of any suitable trait…"). Her işlemden sonra cevap
ekranda kalıyor; "Acknowledge and Move On" ile seçenekler geri geliyor.

Hangi alışkanlığın çalışıldığı ekranda yazmıyor. Veride `PlayerTrainingReference.Traits ->
PlayerTraitsReference.TraitInDevelopment` var ama `Human.Team.Players` satırından `PlayerTraining`
okunamadı (boş döndü); açık. Sahip olunanlar oyuncu kartında (`/player` → `Traits`).

## 8.76 Serbest bırakma ve karşılıklı fesih (10 Ekim)

Oyuncu sayfasında maaş butonu (`ContractDetails`, etiketi "£83K p/w") sözleşme bölümünü açıyor;
konuşma alanında "Discuss New Contract With Agent", "Enter Contract Negotiations", "DoF To Negotiate
Contract" ve "Release" listesi (`GenericDropdown`): "Release On A Free", "Mutual Termination".

- **Release On A Free**: `/dialog` "Release Fred?" — "Fenerbahçe will have to pay £7.5M in
  compensation to Fred. The cost … deducted from the current transfer budget." Yes / No.
- ⚠ **Mutual Termination onay sormuyor**: seçilir seçilmez "Alright, I'll pass on your decision to
  him, and make sure the board are in agreement." Denemede bu yüzden işlendi; oyun kaydedilmeden
  kapatılıp geri alındı (ana kayıt 9 Ekim'den beri yazılmamıştı).
- Liste sayfa otururken yeniden kuruluyor; bayat işaretçi yeniden bulunuyor.

`fm release <oyuncu>` iki yolu listeler; `--free` oyunun penceresini gösterip `--yes` olmadan "No"
der; `--mutual` `--yes` olmadan göndermez. `--yes` ile gerçek bırakma denenmedi.

"Discuss New Contract With Agent" menajerle konuşmayı açıyor: dört ilgi derecesi (Very Interested …
Curious) birer cümle, cevap ("…he doesn't want to, under any circumstances"), "End Chat". `talk`
olduğu gibi yürütüyor; `fm contract <oyuncu> --agent` oraya götürüyor.

## 8.77 Taktik kopyalama ve yuva boşaltma (10 Ekim)

Taktik sayfasında etiketsiz iki menü (`dropdown-base-no-background`): yuvanın taktiği için "Primary
Trained Tactic · Edit Tactic · Presets... › (on hazır stil, her biri alt menülü) · Save · Load... ·
Manage Tactics... · Clear Slot" ve yuva doldurmak için "Create New Tactic · Load Tactic · Copy Tactic ›"
(alt menüde kopyalanabilecek taktikler). `/select names=Copy Tactic>4-3-3 DM Wide - Custom Gegenpress`
yeni yuvaya "Copy of: …" adıyla kopyalıyor ve onu etkin yapıyor (`TacticCount` 1→2). "Clear Slot"
etkin yuvayı siliyor; önce `/dialog` "Are you sure you want to delete this tactic?" → Confirm.
Save / Load… (dosyaya .fmf) denenmedi.

## 8.78 Kulüp: genel bakış, personel, finans (10 Ekim)

**Club › Overview** kutularla dolu ve hepsi tek `/texts` okumasında geliyor
(`anchor=Overall Balance&up=18`). Sıra: Club Vision (Actionable / Informational Objectives, Current
Season Objective; her hedef "metin · Required|Desired|Preferred · On Course|Reserving Judgement|Not
Judging|Satisfied…"), Board ("0 requests"), Finances (Overall Balance, Income/Expenditure "l/m", Wages
"/ £… p/w", yüzde), Supporters (Supporter influence on the board), Affiliates, Facilities (Stadium,
Training, Youth, Corporate), Job Centre Report, Performance Feedback (Board notu "C" + hava,
Supporters hava; Highlights / Criticisms yalnızca sayı), Club Site (Club Reputation, Stadium), Transfer
History, Staff (ekip başına "dolu / yer" ve "N vacant roles (…)"), Responsibilities. "Board" ve
"Supporters" Performance Feedback'in içinde yeniden geçiyor; kutu sınırı yalnızca sonraki başlıklara
bakılarak bulunmalı.

**Personel**: subtab "Staff" yalnızca `StaffReportCard`'ı açıyor (sorumluluklar, antrenör yükü).
Kişi listesi Overview'deki Staff kutusunun `layout-tile-interaction-button`'unda (`OpenPanel`,
`ClubAllStaffC`); `/click?text=Coaching Team` ona basıyor. Kartın Overview sekmesi ekip başına
"N Coaching Staff", rol rol "dolu / yönetimin istediği en az" ve "Board feedback" cümlesi. "All Staff"
sekmesi (`navigation-tab-default-default`) dokuz kolonlu tablo: Name, Team (Senior / U19 Squad), Club
Job, Age, Nationality, F/PT, Type, Wage, Expiring. Yalnızca görünen ~27 satır çiziliyor;
`/scroll?anchor=Senior Squad&to=0..1` ile beş noktada okunup adla birleştiriliyor (45 kişi).
Doktorlar ve başkan "N/A" maaşlı.

**Finans**: subtab "Finances" bir açılır menü (`SecondaryTabDropdownButton`); Overview'deki bakiye
kutusu (`/click?text=Overall Balance`) finans kartını açıyor (`anchor=Remaining Debt&up=16`): Income
"This month", FFP (yarışma + Compliant), Debts And Loans (Remaining / Original), Wages ve "Player
types and their total basic wage:" (sayı · statü · haftalık toplam), Sponsorship (toplam, Sources),
Projections (sezon · Balance · Turnover; zarar parantezli), Expenditure "This Month", Summary (Month ·
Balance satırları), Recruitment Budgets.

**Sayfa açarken render yarışı**: `/open` ara sıra il2cpp hatası döndü — `PanelManager.Open` →
`VisualElement.Add`: "VisualElements cannot be added to an active visual tree during
generateVisualContent callback execution nor during visual tree rendering". Köprü olayı oyunun
çizim anında gönderince oluyor; hemen tekrar göndermek geçiyor. CLI `OpenBookmark` üç kez deniyor.
Kalıcı çözüm eklentide olayı çizim dışı bir ana iş parçacığı anına ertelemek olur (denenmedi).

## 8.79 Gelir/gider dökümü, sponsorlar, taraftarlar, tesisler, yönetim kartı (10 Ekim)

Hepsi Club › Overview'deki bir kutudan (`layout-tile-interaction-button`) ya da finans kartının
kutularından açılıyor:

- **Sponsorlar**: finans kartında `/click?text=Total Sponsorships income` → Sponsorships kartı
  (`anchor=Value (Per Season)&up=14`): Type · Value (Per Season) · Until · Duration · Total Value, beşer
  hücre; sonuncusu "N additional club partner in a deal totalling …" ve "-" tarihli.
- **Gelir dökümü**: finans kartında `/click?text=Turnover` → Income kartı; **gider**: `/click?text=This
  Month` (gider kutusunun alt yazısı; gelirinki küçük m'li "This month") → Expenditure kartı. İkisinde
  de dört dönem sekmesi (`ThisMonthTab`, `LastMonthTab`, `ThisSeasonTab`, `LastSeasonTab`) ve
  "Income/Expenditure Breakdown" altında Item · Total ikilileri (gelirde 17, giderde 21 kalem). Liste
  yalnızca görünen satırları çiziyor; görünen bir kalemi çapa yapıp 0 / 0,5 / 1'e kaydırarak toplanıyor.
  ⚠ Kart en son bırakılan sekmede açılıyor — "bu ay" için de sekmeye basmak gerekiyor.
- **Taraftarlar**: Overview'de `/click?text=Supporter influence on the board` (`anchor=Supporter
  Profile&up=12`): profil "35% · Core" ikilileri, Season Ticket Holders, Social media followers, Average
  Attendance, Season Ticket Waiting List, genel cümle, beklenti tablosu (metin · Required/Desired/Preferred
  · Playing style/Reputation/Signing/Rivalry/Competition · Pleased/Satisfied/Reserving Judgement…),
  sözcünün paragrafı, Fierce / Other Rivals sayısı, Matches Feedback (Board / Supporters). ⚠ Tablonun
  Type kolonu birkaç kare sonra çiziliyor; o ana kadar satırlar üç hücre — ilk iki satır dört hücre
  arayla hizalanınca okunmalı.
- **Tesisler**: `/click?text=Facilities` (`anchor=Under-Soil Heating&up=16`): About Club (Founded,
  lig sırası), Stadium notu ve Name, City, Rent, Capacity, Year Built/Rebuilt, Owned By, Under-Soil
  Heating, Roof, Surface, Pitch Dimensions, Maximum Pitch Dimensions, Pitch Condition; Corporate;
  Training ("Great training facilities", Name); Youth, Youth Stadium, Youth Level, Academy Coaching,
  Youth Recruitment. "Name" iki kez geçiyor; bölüm başlığından sonra aranmalı.
- **Yönetim**: `/click?text=1 board member`: üyeler (Sadettin Saran · President), talep türleri
  (Build New Stadium, Facilities, Finance, Networking, Staff, New Contract — her biri talep açan
  buton), Board Requests tablosu (Request · Status · Started · Outcome · Completed; şimdilik boş).
  Talep etmek denenmedi.

## 8.80 Taktik akıcılığı, mevki puanları, sakatlık geçmişi, sezon istatistikleri (10 Ekim)

- **Akıcılık** 10000 üzerinden tutuluyor. Takımınki `Tactic.TacticalFamiliarity` (9089 → %91; §8.47'de
  "alışkanlık" diye geçen sayı bu). Oyuncununki taktik ekranının altındaki kadro tablosunun Familiarity
  kolonunda: `…binding.Player.TacticalFamiliarity.AverageFamiliarity`. Bu bir `PlayerTacticalFamiliarityReference`;
  ama `/pull` ile `Human.Team.Players.<n>.TacticalFamiliarity` boş dönüyor, çünkü değer satırın mevki/diziliş
  bağlamına bağlı. Okurken satırdan yukarı çıkılıyor; aynı satırda iki seviye aşağıda `PlayerIndex` var.
  Tablo planlayıcıdan biraz sonra doluyor, ilk okumada kolon boş olabiliyor. Bağlanan yalnızca on bir
  oyuncu. Ayrıntılı döküm (`TacticalFamiliarityDetails`: mentalite, tempo, genişlik…) "Familiarity"
  butonunun üzerine gelince açılan ipucunda; tıklamak açmıyor, okunmadı.
- **Mevki puanları**: `PersonReference.PositionalAbilities`, 15 elemanlı bir liste; her eleman
  `DisplayValue` ("DM", "M (L)"…) ve `PropertyValue` (1–20) taşıyan bir `DynamicReference`. İkinci
  elemanın adı boş (hep 1).
- **Sakatlık geçmişi**: `PersonReference.InjuriesHistory` → `PlayerInjuryReference` (`InjuryDescription`,
  `InjuryDate` paketli GameDate, `InjuryDurationDays`, `IsActive`). Özet alanları `InjuriesThisSeason`,
  `InjuriesLastSeason`, `InjuriesPercentageMissedString` ("0% missed").
- Kadro dışındaki oyuncu için bir yol lazımdı: `/player` artık sayfadaki `Person` değerini
  `fmcli.card.p<isim karması>` yoluna koyuyor ve `Path` alanında veriyor; `/pull` oradan okuyor.
  Oyuncu başına ayrı yol açılıyor, çünkü başkası için tekrar kullanılan yol eski oyuncunun değerlerini
  veriyor (§8.68).
- Bu çağrıyla bağlanan bir liste aynı karede boş geliyor ("is not a list"); `Fm.Pull` bunu tekrar deniyor.
- **Sezon istatistikleri** ekransız: `Human.Team.Players` üzerinde `ClubAppearancesThisSeason`, `ClubGoalsThisSeason`,
  `ClubAssistsThisSeason`, `ClubAverageRatingThisSeason`, ayrıca `StartingAppearances`, `SubAppearances`,
  `Minutes`, `YellowCards`, `RedCards` (`valueCount`; `/pull`'da `Minutes.PropertyValue` gibi noktalı
  prop çalışıyor). ⚠ "Club" oyuncunun şu an oynadığı kulüp: kiralıktakilerin rakamları kiralık
  kulübünki. Kiralıkta olan `LoanClub.Name` ile ayrılıyor; bize kiralananın `LoanClub`'ı biziz.
- İki eski yarış daha çıktı: (1) oyuncu sayfasında isim, özelliklerden ve sözleşmeden önce bağlanıyor;
  komut artık özellikler ve lig kariyeri gelene kadar en çok 4 sn bekliyor. (2) Arama kutusu yeni
  açılınca önceki aramanın sonuçları biraz geç beliriyor ve yeni sonuç sanılıyordu ("Arda Güler" →
  Fred Onyedinma); "önceki" hali artık liste durulunca alınıyor (6/6 temiz).

## 8.81 Yönetim ve taraftar güveni, bilet fiyatları; park edilen okumalar (10 Ekim)

- **Güven** ekransız: `Human.Team.TeamConfidence` (`TeamConfidenceReference`, 69 property). Her konunun bir cümlesi
  ve bir notu var: `BoardConfidenceOverallString/Grade`, `BoardMatch…`, `BoardSquadConfidence…`, `BoardTactics…`,
  `BoardTransferConfidenceOverallSummaryText`, `BoardVision…`, `BoardCultureProgressString`,
  `InternationalCompConfidenceStringLong`; taraftar için aynıları `Supporter…` önekli. `BoardFeedbackHighlights` /
  `…Criticism` (taraftarda `SupporterFeedback…`) düz string listeleri: `/pathlist` her birini `(String) …` diye veriyor
  (`Fm.Strings`). Notlar sayı: genel 5 (Club › Overview'de "C · memnun"), maçlar 3 ("disappointed"), taktik 6;
  taraftarın genel notu 4 ama cümlesi yine "satisfied". Ölçek doğrulanmadı, komut yalnızca cümleleri basıyor.
  `TeamReference.JobSecurityStatus` = 4; karşılığı bilinmiyor, kullanılmadı.
- **Bilet**: `Human.Team.Club` üzerinde `MatchTicketPrice` (71.73), `SeasonTicketPrice` (1253.10), `SeasonTicketHolders`
  (25000); para birimi sterlin (oyun £ gösteriyor). Kulüp değeri yalnızca `FinanceDebugClubValue`'da ve boş dönüyor.
- **Park edilenler**:
  - *Tam gol / asist listesi*: `CompReference.PlayerStats` (`CompetitionStatsPlayerStatsListReference`: `StatValueHeader`,
    `Players` → `Player`, `Team`, `StatValue`) ve hangi istatistik olduğunu seçen `PlayerStatsType` var. Ama
    `Human.Team.Division.PlayerStats` `/pull` ile boş dönüyor; değeri ancak onu bağlayan ekran (panel
    `CompetitionSiteStatsCard`, "Stats") doldurur. Turnuva sayfasındaki Stats döşemesi `TileInteraction = False`,
    tıklanmıyor; kartı açan yol bulunamadı.
  - *Başka ligler*: oyunun araması bu kayıtta turnuva döndürmüyor ("Premier", "Bundesliga" → boş), yani
    `fm table <lig>` için önce bir turnuva sayfasını açacak başka bir yol lazım (ya da lig yüklü değil, §12).
  - *Scout tavsiyeleri*: `fm open "Player Recommendations"` Recruitment'ın genel görünümünü açıyor; "Recommendations (9)"
    döşemesi yalnızca ikisini gösteriyor (ad, mevki, "C-" notu). Tam liste ayrı bir kartta, açılmadı.

## 8.82 Kısaltmalar, tab tamamlama, `fm doctor`; health'in sakatlık süresi (10 Ekim)

- **`InjuriesTimeMissed` kalan süre değil**: kadro tablosunun Injuries görünümündeki süre ("10 days") oyuncunun
  şimdiye kadar kaçırdığı süre. Kalan süre `CurrentInjury.InjuryReturnDays` (aynı gün Kanté: kaçırdığı 10 gün,
  kalan 1; sakatlık geçmişinde "A twisted ankle, 10 gün"). `fm health` önceden ilkini "… gün daha" diye yazıyordu.
  `CurrentInjury.InjuryDescription` / `InjuryReturnDays` `Human.Team.Players` üzerinde ekransız okunuyor.
- **`fm doctor`** tamamen `/pull` ile çalışıyor (~1,8 sn): kondisyon, `Sharpness`, sakatlık, `FullContract.EndDateDescription`,
  `PositionCombinedString`, `LoanClub.Name` ve yönetimin eleştiri listesi. Mutluluk (`Morale`, `Happiness…`) `/pull` ile
  dönmüyor; `fm happiness` ekrandan okumaya devam ediyor.
- **Tamamlama**: komut açıklamaları yardım metninden (`HelpText`) alınıyor, ikinci bir liste tutulmuyor. macOS'un bash
  3.2'si `source <(…)` ile fonksiyon tanımlamıyor; bash için `eval "$(fm completion bash)"`. zsh'teki oyuncu adları
  `fm __names`'ten (oyun kapalıyken sessizce boş).
- ⚠ Oyun bir kez kendiliğinden çöktü (SIGABRT, `GameAssembly` içinde `__cxa_rethrow`, BepInEx günlüğünde iz yok); o sırada
  `fm health` kadro görünümlerini değiştiriyordu. Yeniden başlatıp aynı sırayı (stats, status, health, doctor, player,
  board, tactic, health, doctor) yürütünce tekrarlamadı. Neden bilinmiyor.

## 8.83 `--json` her komutta; taktik ekranının iki görünümü (10 Ekim)

- **`--json` tek yazıcıdan**: `JsonOut.Of` tuple alanlarını yazıyor (varsayılan serileştirici `(string, string)`'i `{}`
  basıyordu: `fm league --json` boş nesneler veriyordu) ve Türkçe harfleri `\u00E7` diye kaçırmıyor. `--json` verilince
  `Ui.Rich` ve `Ui.Interactive` kapalı: hiçbir şey çizilmiyor, soru sorulmuyor, komut script yoluna düşüyor. Yeni:
  saves, captains, prep, intensity, opposition, register, training, offer/contract ekranı, tactic slot / mentality /
  style / pi / instructions, `settings <ayar>`, `squad --views`. Eylem komutları (numbers, setpieces, shout, release,
  match, next…) metin kalıyor.
- **`fm league` yarışı**: lider kutusu etiketleri adlardan bir an önce geliyor; "iki okuma aynı" yetmiyordu (3 denemede
  2'si "-"). Artık her liderin adı ve değeri gelene kadar (ya da ~4 sn) bekliyor.
- **Taktik ekranı: "Team Shape" / "Team Instructions"** (`ButtonGroup #FMShapeAndInstructionsButtons`). Talimat
  görünümünde saha yok, `/tactic` `Slots = []` veriyor ve "Combined" butonu da yok; `tactic pi` "Mevki: 'DR' diye bir
  seçenek yok" diyordu. `#team-shape` bir `SIButton`, `/press` onu bulmuyor; `/click?text=Team Shape&name=team-shape`
  gerçek tıklamayla sahayı geri getiriyor. `Planner()` bunu yapıyor. Sahaya dönünce ilk "Go to Player Instructions"
  seçimi yutulabiliyor, `tactic instructions`'ta da sayfa açıldıktan 0,5 sn sonraki "Team Instructions" basışı; ikisi
  de yeniden deniyor. instructions ↔ pi dört kez art arda ✓.

## 8.84 Maç maç puanlar: Match Ratings sayfası (10 Ekim)

- Oyuncuda `PastMatches` / `PastMatchesRatings` (`List<TypedValue>`) var ama `pathlist` boş veriyor (kaptan yoluyla da,
  oyuncu sayfası açıkken de); kart yolu (`fmcli.card.p…`) altında liste değil. `LastFiveMatchesForm` alanları `/pull`'da boş.
- Oyuncu sayfasındaki form kutusu (`layout-tile-interaction-button`, metni "Last 5 Club Games", üstünde son beş maçın
  ortalaması) yalnızca gerçek tıklamayla (`/click?text=Last 5 Club Games&name=layout-tile-interaction-button`) **Match
  Ratings** sayfasını açıyor. `/texts?anchor=All Competitions&up=10`: başlık, takım ve turnuva süzgeçleri, özet (Apps, Max. /
  Min. / Average Rating, Goals Scored, Assists), kolon başlıkları (`#, Date, Pos., Role, Matches, Score, Inf, xG, Ast, Gls,
  Rating`), sonra satırlar. `Inf` yalnızca oyuna girip çıkanlarda metin ("on 75", "off 60"). Kalecide `xGA, Conc, CS`; CS bir
  ikon, metni yok. Skor ev sahibi önce (Gençlerbirliği deplasmanı "4 - 0", Ederson 4 gol yemiş). Milli maçlar da listede;
  kadroda olup oynamadığı maçlar "-" mevki ve 0.00 puanla (Apps 20, satır 22).
- Liste sanal (`VirtualisedList`, 19 satır görünüyor): `/scroll?anchor=<bir satırın tarihi>&to=0.5|1` ile kalanlar geliyor.
- Kolay grup, aynı gün: `--json` her komutta (§8.83), `--csv` / `--md` ve `fm export`, `fm diff`, `fm compare`, yazım
  hatası toleransı. `fm compare`'de ikinci oyuncu sayfası özelliklerin yarısı gelmişken okunabiliyordu; `OpenPlayer` artık
  özellik sayısı iki okumada aynı kalınca alıyor.

## 8.85 Gizli özellikler ve potansiyel (10 Ekim)

- `/pull` ile **`PerceivedPotentialAbility`** geliyor (200 üzerinden; Ederson 160, Semedo 140, Škriniar 155, Kerem 146,
  Osimhen 164); kadro dışı oyuncuda kartın yolundan (`fmcli.card.p…`). Adı "algılanan": kulübün personelinin gördüğü,
  gerçek PA değil. Kartta sayı olarak gösteriliyor; oyunun yıldızları kulübün seviyesine göre göreceli, çevrilmedi.
- Gelmeyenler: `PlayerCurrentAbility`, `PlayerPotentialAbility`, `DebugCurrentAbility` (boş), `PotentialAbilityStarRange` /
  `CoachPotentialAbilityStarRange` / `ScoutPotentialAbilityStarRange` `.MinValue/.MaxValue` (boş). Kişilik özellikleri
  (`AttributeAmbition`, `…Professionalism`, `…Loyalty`, `…Pressure`, `…Temperament`) oyuncuda `PropertyValue = 0`;
  veri modelinde bunlar `IAgentReference` / `INonPlayerReference` üzerinde. Tutarlılık, büyük maç, sakatlığa yatkınlık
  gibi gizli özelliklerin adı veri modelinde yok. Oyuncunun kişiliği yalnızca `Personality` metni ("Perfectionist").
- `AbilityProgressHistory` dört `DynamicNumber`lık bir liste (Škriniar: 34980, 34980, 34980, 34760); ölçeği ve hangi
  dönemleri tuttuğu belli değil, gelişim grafiği için kullanılmadı.

## 8.86 `/squad` iki listeyi birleştirirken boş kalan alan (10 Ekim)

Kadro görünümleri notundaki Musaba hatası (taktik ekranı altta bağlıyken mevki ve yaş boş) bugün taktik ekranı açıkken üç
denemede de çıkmadı. Kodda olası bir neden vardı: aynı oyuncu iki listede bağlıyken ilk bulunan kayıt bir alanı
henüz boş tutuyorsa, öbür listedeki dolu değer `TryAdd` yüzünden yazılmıyordu. Artık boş alan dolu değerle
dolduruluyor. Hatanın bu olduğu doğrulanmadı.

## 8.87 Penaltı sırası (10 Ekim)

- Match Day › Penalties: `/form?anchor=Penalty Takers&up=12` sırayı (Order, forma no, ad, "Starting at STC",
  Composure, Penalty Taking) ve üç kuralı `SICheckBox` olarak (değeriyle) veriyor. `/setcontrol?ptr=…&value=true|false`
  kuralı değiştiriyor; sayfadan çıkıp dönünce duruyor ✓ (denemeden sonra hepsi kapalıya döndürüldü).
- Sırayı değiştirmek: satırlar `FMDraggableObject`; tablo (`StreamedTable`, etiketi "Jump to Top") `DragEvents →
  EventFmxSetPiecePenaltyCandidatesMove` ve `RemoveEvents → …Remove` taşıyor; "Add Player" listesi
  (`EventFmxSetPiecePenaltyCandidatesAdd`) seçeneklerini `FM.UI.PersonReference` olarak veriyor, köprü adlarını okuyamıyor
  (`Choice` yalnızca `DynamicReference`'ı açıyor). Ayrıca `EventFmxSetPieceSwapPenaltyCandidates` var. Sürükleme olayının
  bağlamında hangi satırın nereye gittiği olmalı; yapılmadı. Kişi referansını ada çevirmek için seçeneği bir düğüme koyup
  (`RawPath.Set`) `Name`'ini bağlamak denenebilir.

## 8.88 Antrenman programını değiştirmek: ilk bakış (10 Ekim)

- Club › Responsibilities › "Training Responsibility": genel ve bireysel antrenmanı menajer yönetiyor ("Leads General
  Training … Faruk Kavlak, Manager"; "Take Control" / "Delegate"). Yani program bizim elimizde.
- Squad › Training › Overview'daki takvim (§8.73) yalnızca özet: seans hücreleri `DynamicItem DISABLED`, tıklanmıyor.
  "This Week's Schedule" kutusu `/press` ile kuyruğa giriyor ama yeni bir şey açmıyor; `/click` ile bir
  `PanelHistoryClose` beliriyor, içeriği ayırt edilemedi.
- Katalogdaki olaylar: `EventFmxConfirmTrainingSession`, `…ConfirmTrainingSessionFromMenu`, `…CreateCustomTrainingSchedule`,
  `…SaveCustomTrainingSchedule`, `…ConfirmTrainingSchedule`, `…CancelTrainingSchedule`, `…DeleteCustomTrainingSchedule`.
  Veri: `CustomTrainingScheduleReference` (`TrainingSessions`, `CurrentTrainingSession`, `TrainingSessionCategoryDropdown`,
  `TrainingScheduleDropdown`, `LoadTrainingScheduleType`), `CalendarWeekReference.CanEditTrainingSchedule`,
  `SessionType1..3`. Sıradaki deneme: takvimi düzenleyen sayfayı bulmak (`screens.txt`'te "Training Schedule" / "Calendar"
  paneli; olası yol günün hücresinin üstündeki gün başlığı ya da Training alt menüsünde gizli bir sekme).

## 8.89 Antrenman programını değiştirmek (10 Ekim akşam)

- Düzenleyen sayfa **Portal › Calendar'ın "Training" sekmesi** (General / Training / Fixtures). Sekme butonları
  (`#navigation-tab-default-default`) olay taşımıyor, `/buttons`'ta yok; `/click?ptr=` ile basılıyor. Sayfa açılır
  açılmaz gelen tıklama boşa gidebiliyor, ızgara gelene kadar yeniden deneniyor. Her sekmenin kendi `MonthDropdown`'ı var:
  ay sekmeye geçtikten sonra seçilmeli.
- Izgara: her haftanın başında bir kart (`#TrainingFocusCard`: programın adı + türü, "Tactical" / "Training Style"),
  sonra günler; her günde üç buton (`FMCalendarEvent #Training_TrainingSessionDynamicItem`, bir `DropTarget #View`
  içinde — seanslar sürüklenebiliyor olmalı, denenmedi). Maç günü ortadaki buton maçın kendisi (içinde lig kısaltması,
  rakip, H/A: birden çok metin). Geçmiş günler, maç ve "Travel" kapalı (`enabledInHierarchy = false`).
- `/form?anchor=Monday&up=18&buttons=1` sekmeleri, ay listesini ve ızgarayı tek okumada, sırasıyla veriyor
  (`buttons=1` yeni: buton da bir öğe; `Text` ilk metni, `Texts` içindeki hepsi). "Go On Holiday" çapası Training
  sekmesinde gizlendiği için işe yaramıyor.
- **Seans penceresi** (bir seansa tıklayınca): adsız bir kategori listesi (General, Match Preparation, Attacking,
  Defending, Technical, Tactical, Goalkeeping, Set Pieces, Physical, Extra-Curricular; `…trainingsessioncategory`'ye
  yazıyor, `fire=1` ile), o kategorinin seansları (`SetDatastoreValue` butonları), seçili seansın anlatımı (Primary /
  Secondary / Tertiary Focus: kime, bir cümle, "Impacts", "Priority" %, "Attributes" adları, etki çiftleri
  "Condition" / "Slightly Reduced"), Cancel ve Confirm (`EventFmxConfirmTrainingSession`). Onaylanınca haftanın adı
  "Tactical (Modified)" oluyor; sayfadan çıkıp dönünce duruyor ✓.
- Kategorideki seanslar güne göre değişiyor (9 Ekim'de Match Preparation yalnızca "Match Tactics"; "Match Practice",
  "Match Focus", "Travel" başka günlerde). CLI kataloğu `~/.config/fm-cli/training-sessions.json`'da tutuyor,
  yazılan ad katalogda yoksa yeniden okuyor. Adlar iç içe ("Rest" ⊂ "Transition - Restrict"): önce tam eşleşme.
  Seçenek butonlarının arasında etiketsiz bir buton da var, atlanıyor.
- **Hafta penceresi** (karta tıklayınca): tür listesi (`…trainingschedulecategory`: "Pre-Season ›" alt menüsü
  Early / Mid / Late — `names=Pre-Season>Early` —, Tactical Style, Scenario, Training Style), türün programları
  (Training Style: Defence, Possession, Attack, Physical, Technical, Set Piece, Tactical, Balanced [Recommended];
  Tactical Style: Route One, Gegenpress, Tiki-Taka, Fluid Counter-Attack, Catenaccio, Wing Play, Control Possession,
  Vertical Tiki-Taka, Direct Counter-Attack, Park the Bus, Clean Slate; Scenario: Boot Camp, Full Week of Rest;
  Pre-Season: Light, Normal, Heavy, Tactical, Technical), başlık ("Tactical - No matches": haftadaki maç sayısına göre
  şablon), bir cümle, etki çubukları (Injury Risk, Condition… — değerleri çizim, okunmadı), "Create New Schedule"
  (denenmedi), Cancel, Confirm (`EventFmxConfirmTrainingSchedule`). Sezon öncesi programının kartı "Light - Early".
- Bir haftaya aynı programı yeniden uygulamak "(Modified)"i kaldırıyor; 6 Ekim haftası birebir eski haline döndü,
  3 Kasım haftasında ise 4 Kasım'ın ilk seansı "Ground Defence" yerine "Defending" geldi (şablon o an bilinen
  fikstüre göre yeniden kuruluyor olabilir).
- İki pencere de etiketi aynı kontroller taşıyor ("Tactical" hem takvimde seans hem pencerede seçenek):
  `/press?text=…&event=SetDatastoreValue` ya da işaretçiyle basılmalı; `/click` ile yanlış "Tactical"a basmak
  takvimdeki seansın penceresini açtı.

## 8.90 Penaltı sırasını değiştirmek: sürüklenen tablo (10 Ekim akşam)

- `StreamedTable` (SI.Bindable) satır sürüklenip bırakılınca kendi **`OnMoveItemInternal(int from, int to)`**
  yöntemini çağırıyor: taşınanı `m_dragKey`'e ("itemmoved") yazıp `DragEvents`'i
  (`EventFmxSetPiecePenaltyCandidatesMove`) gönderiyor. Satır çıkarmak **`OnRemoveItemInternal(int index)`**
  (`m_removeKey` "itemremoved", `RemoveEvents`). Interop'ta ikisi de çağrılabiliyor: `/rowmove?ptr=<tablo>&from=&to=`
  ve `/rowremove?ptr=<tablo>&index=` (0'dan sayılır; `to` taşınanın yeni yeri, aradakiler birer kayıyor).
  `IsDragCellActive` bu tabloda false (ayrı bir sürükleme hücresi yok, satırın kendisi sürükleniyor) — bakılmıyor.
  Tablonun işaretçisi `/buttons?json=1`'de (`Type` "StreamedTable", `Events`'te "PenaltyCandidatesMove").
- "Add Player" listesi (`…playerselected`'a yazıyor, `EventFmxSetPiecePenaltyCandidatesAdd`): seçeneklerin
  `DisplayValue`'su `FM.UI.PersonReference`, `PropertyValue`'su kişinin **`Index`**'i. Köprü böyle bir seçeneği artık
  `#<Index>` diye adlandırıyor; adı `/pull?path=Human.Team.Players&props=Name,Index&list=1` veriyor (bağlandıktan sonra
  satırın `Index` alanı kişininkine dönüyor: Ederson 19301, Kerem 24884). `/select?ptr=…&choice=%23<Index>&fire=1`
  oyuncuyu sona ekliyor; listede yalnızca henüz penaltıcı olmayanlar var.
- Üçü de sayfadan çıkıp dönünce duruyor ✓ (taşı 2→0 ve geri, çıkar, ekle; üç tur). Tablo satır satır yeniden
  çiziliyor: değişiklikten hemen sonra okunan satırda mevki hücresi eksik olabiliyor, CLI bütün hücreler iki okumada
  aynı kalana kadar bekliyor.
- Aynı yöntemler sürüklenen öbür tablolara da uymalı (köşe/serbest vuruş atıcıları, kaptan sırası); denenmedi.
  `OnAddItemInternal(object, int)` de var, kullanılmadı.

## 8.91 Duran top atıcıları (10 Ekim gece)

- Match Day › Set Pieces bir seferde tek türü gösteriyor. Tür listesi (ilk seçeneği "Corners" olan `SIDropdown`):
  Corners, Free Kicks › (Shooting Range, Wide, Deep), Throw-Ins › (Defensive Third, Middle Third, Attacking Third).
  `/select?ptr=…&names=Free Kicks>Wide&fire=1` türü değiştiriyor; sayfadaki "Set Piece Type" yazısının yanındaki metin
  hangi türün açık olduğunu söylüyor.
- Atıcıyı gösteren buton (`taker-button`; taçta `taker-picker-button`) `OpenPanel` ile iki liste açıyor: korner ve
  serbest vuruşta "Right Footers" / "Left Footers", taçta "Right Side" / "Left Side". İkisi de penaltıdaki gibi
  `StreamedTable` (`EventFmxSetPieceTakerMove`, `…TakerRemoveFromList`), yanında "Add Player" listesi
  (`…TakerAddToList`) ve "Set X as the default taker from both sides" anahtarı (`…TakerBothSides`, denenmedi).
  `/buttons`'taki ilk tablo sağ, ikincisi sol. Kolonlar türe göre: korner Tec / Cro / Cor, serbest vuruş Tec / Fre /
  Long Sh. (kanat ve derinde üçüncü kolon başka), taç L Th / Position.
- **Yalnızca hücum bölgesi tacında atıcı seçiliyor**; Defensive Third ve Middle Third sayfalarında buton yok.
- `/rowmove`, `/rowremove` ve "Add Player" seçimi (§8.90) burada da çalışıyor, sayfadan çıkıp dönünce duruyor ✓
  (korner sol: ekle 2. sıraya / zaten var / çıkar, üç tur; korner sağ, serbest vuruş sol ve taç sağ: taşı ve geri al).
  "Add Player" seçeneğinin `PropertyValue`'su burada sayı değil kayıt: `{SetPieceType, SetPieceSide, Index, Player}`;
  köprü içindeki `Index`'ten yine `#<Index>` adını veriyor. Listede yalnızca o ayağı kullananlar var (sol ayaklılarda
  Brown, Fred, Oosterwolde, Defrel).
- ⚠ **Sayfa adım adım çiziliyor.** Sayfa açılır açılmaz yapılan tür seçimi kayboluyor ama liste o türün adını
  göstermeye devam ediyor (sayfa korneri gösterirken liste "Shooting Range" diyor); bir sonraki açılışta da öyle
  kalıyor. Tür değişince de eski türün atıcı butonu bir süre yerinde duruyor ve basılınca eski türün listelerini
  açıyor. CLI her seferinde türü yeniden seçiyor ve listenin, "Set Piece Type" metninin ve butonun iki okumada aynı
  kalmasını bekliyor; açılan tabloların işaretçileri de öncekilerden farklı olmalı (eski panel yerinde kalabiliyor).
- ⚠ `/rowremove` bir kez "VisualElements cannot be marked for dirty repaint under an active visual tree" ile
  döndü (çağrı çizim anına denk geldi, §8.78'deki yarış); satır yine de çıkmıştı. CLI bu hatada önce sayfaya bakıyor,
  değişiklik görünmüyorsa bir kez daha gönderiyor.
- Sayfanın kalanı okunmadı: vuruş tipi (Inswinger / Outswinger, `EventFmxSetPieceRoutineDeliverySelect`), hedef
  ("Aim For": Mixed, Short, Near Post…, `…StrategyConfirmed`), oyuncu görevleri (`…InstrChanged`), Attacking /
  Defending, rutin kaydet / yükle.

## 8.92 Serbest oyuncular, kişi listesine sızan kadro, iki park (10 Ekim gece)

- **Serbest oyuncular** oyuncu veritabanının hazır süzgeci "Contract Expired": satırların durumu `FrA`, kulüp boş
  (bir amatör dışında). `fm players --free` bunu seçiyor.
- ⚠ **`/pull`'un kendi düğümleri kişi listelerine sızıyordu.** `list=1` her girdiyi `fmcli.<liste>.<n>` düğümüne
  koyuyor ve orada bağlı bırakıyor; `/people` ağaçtaki adı ve yaşı (ya da mevkisi) bağlı her `PersonReference`'ı satır
  sayıyor. `fm squad`, `contracts`, `doctor`, `diff` kadroyu yaşla okuduğu için sonraki `fm players --filter …`
  listesinde kendi oyuncularımız da çıkıyordu (serbest oyuncular arasında Ederson). `/people` ve `/squad` artık
  `fmcli.` altındaki düğümleri atlıyor ✓ (squad + contracts sonrası `players --free`: 21 satır, bizden kimse yok;
  `search`, `shortlist`, `squad` aynı).
- **Maç hazırlığı odağını değiştirmek (park).** Karttaki odaklar (Build Up, High Press, Attacking Corners, Defending
  Free Kicks) seans kataloğunda yok (`~/.config/fm-cli/training-sessions.json`, on kategori). Maçtan önceki "Match
  Focus" seansı takvimde kapalı (`enabledInHierarchy = false`), tıklanamıyor. Odak büyük olasılıkla rakibe göre
  kendiliğinden seçiliyor; değiştiren bir kontrol bulunamadı.
- **Gelişim grafiği (park).** `AbilityProgressHistory` `/pull`'da `List<TypedValue>` diye geliyor (liste açılmıyor);
  §8.85'teki dört sayının ölçeği hâlâ belirsiz. Antrenman raporunun kaydında (`TrainingProgressReportReference`)
  `AbilityGraphDataFull` / `AbilityGraphDataRecent` / `AbilityProgression` alanları var; grafik oradan denenebilir.

## 8.93 Duran top rutini: vuruş tipi, hedef, görevler (10 Ekim akşam)

- **Vuruş tipi** iki `SIButton` (`EventFmxSetPieceRoutineDeliverySelect`, veri `const.Zero` / `const.One`); `/press`
  çalışıyor. ⚠ Butonların adı (`…-active-regular` / `…-inactive-regular`) seçimi göstermiyor, çizildiği gibi kalıyor;
  seçim ağaçta `routine.SetPieceDeliveryType` (0 Inswinger, 1 Outswinger). Vuruş tipi değişince atıcı öbür ayağın
  listesinden geliyor (korner: Inswinger → Kerem, Outswinger → Asensio).
- **Hedef** ("Aim For") bir `SIDropdown` (`EventFmxSetPieceStrategyConfirmed`), `/select?choice=…&fire=1`. Korner:
  Mixed · Short · Near Post · Far Post · Edge of Area · Centre; kanat ve derin serbest vuruş: Mixed · Short · Long ·
  Near Post · Cross Centre · Far Post · Best Header; şut mesafesinde hedef ve vuruş tipi yok. **Taçta** aynı event iki
  butonda ("Work Into Box" / "Long Throw"), seçim ağaçta `routine.SetPieceStrategy` (0 / 1). Work Into Box'ta
  oyunculara görev verilemiyor ("Individual instructions can't be set…"), Long Throw'da yerler geliyor.
- **Görevler:** her yer (A1…, B1, C1…, D1…) için bir `SIDropdown` (`EventFmxSetPieceInstrChanged`), sayfadaki
  yerlerle aynı sırada; "Stay Back If Needed" gibi görevlerde hemen ardından ikinci liste
  (`…SecondaryInstrChanged`, "gerekmezse ne yapsın"). Yerin grubu (Aerial Threats, Box Threats, Creators, Recovery
  Defenders) metinlerde "(3/9)" sayacından önceki satır. Grup değiştirmek oyunda sürükleme; denenmedi.
- Önceki türün kontrolleri sayfada kalıyor (taçta boş etiketli vuruş butonları ve eski hedef listesi); CLI bir
  kontrolü yalnızca sayfa onun başlığını ("Delivery Type", "Aim For", "Throw-In Instructions") yazıyorsa alıyor.
- ⚠ **Sayfayı açan basış ara sıra yutuluyor:** `OpenPage` "Match Day › Overview"ı açıp 0,2 sn sonra "Set Pieces"
  sekmesine basıyor; basış başarı dönüyor ama oyun Overview'da kalıyor (10 çağrıda 3). `ShowSetPieceKind` sayfa 10 sn
  gelmezse bir kez yeniden açıyor: 12 ardışık çağrıda 12 başarı ✓ (üçü yeniden açarak, ~15 sn).
- Doğrulama: korner vuruş out → in, hedef Far → Near, A3 değiştir / geri al (sayfadan çıkıp dönünce duruyor), D1
  ikinci seçim; taç Long ↔ Work beş tur. Hepsi eski haline döndü.
- Açık: "Scenario" (Attacking / Defending), rutin ekleme / kaydetme / yükleme menüleri, savunma ve orta saha taçları.

## 8.94 Kulüplere önerme, kilitli görüşme kartı, bire bir konuşma (10 Ekim akşam)

- **Kulüplere önerme = "Offer via TransferRoom".** Oyuncunun sayfasında **değer** butonu transfer aksiyonlarını açıyor
  (Set Transfer Status, Offer via TransferRoom, Hire An Intermediary To Sell Player, Ask Agent About Market Interest,
  Enter Contract Negotiations + transfer / kiralık durumu ve gelen tekliflere otomatik cevap seçenekleri); **maaş**
  butonu sözleşme aksiyonlarını (§8.76). "Offer via TransferRoom" küçük bir teklif ekranı açıyor: "Transfer Offer" /
  "Loan Offer" (`EventFmxTransferOfferSelectOfferType`), "Targets (0)" (kulüp seçme paneli, okunmadı), bonservis
  `FMCombobox`'ı (`/setnumber` çalışıyor: "Unspecified" → "£50K"), ek ücret ve madde listeleri, "Offer Player"
  (`SpeakToAction`). Kiralıkta bonservis alanı yok, yalnızca ek maddeler. `/offer` okuyucusu bu ekranı görmüyor (`{}`).
- "Offer Player" onaysız gönderiyor; gönderilince aksiyon listesinden "Offer via TransferRoom" ve "Hire An
  Intermediary…" kalkıyor (ikinci denemede seçenek yok ✓ negatif test). Transfer dönemi kapalıyken de gönderilebiliyor.
  Denenenler: Defrel £50K (elle), Cengiz Ünder £60M (`fm propose`), Söyüncü kiralık (`fm propose --loan`).
- ⚠ Oyuncu sayfasında aksiyon listesi açık kalınca `status` bunu "Açık bir soru var" sanıyor (liste `SpeakToAction`
  butonları). `propose` işi bitince Portal › Overview'a dönüyor; `contract`'tan "Exit Talks" ile çıkınca aynı şey olur.
- **Kilitli görüşme kartı.** Defrel (34 yaş, sözleşmesinin son yılı) için "Enter Contract Negotiations" kartı açıyor ama
  vaatlerin ikisi de "None Selected", seçenek listeleri boş, bütün kontroller kapalı; `--next` hiçbir şey yapmıyor.
  Müldür'de aynı akış sorunsuz (şartlar → `--sign --yes` → iki gün sonra "Mert Müldür signs new 3-year Fenerbahçe deal"
  haberi; oyuncunun kararı soru olarak gelmiyor). CLI kilitli kartı söylüyor. Kartta "Cancel Negotiations" yok, çıkış
  "Exit Talks" butonu (`contract --cancel` bunu bilmiyor — açık).
- **Bire bir oyuncu konuşması** `fm talk` şablonuyla aynı ✓: sakatlık mesajındaki "Talk to Doğukan About Injury" cevabı
  konuşmayı açtı ("What can I do for you?"), tek cümle seçildi, tepki "Expressionless → Encouraged (+1)", "End Chat".
  Konuşmadan sonra mesaj hâlâ tedavi seçimi bekliyor ("You have discussed the issue…", üç seçenek) ama oyun onu artık
  "cevap bekliyor" diye işaretlemiyor; Continue butonu "Address Injury" diyor. `status` bu durumda gelen kutusuna
  yönlendiriyor.

## 8.95 Soyunma odası: sosyal gruplar, hiyerarşi, kişilik (10 Ekim akşam)

- Squad › Dynamics › **Social Groups** her oyuncuyu grubunun altında veriyor: grup adı ("Core Social Group",
  "Secondary Social Group A / B", "Others"), grubu neyin tuttuğunu söyleyen cümle, "Player · Hierarchy · Personality"
  başlıkları, sonra oyuncu başına ad, "N year-old …", hiyerarşi ("Team Leader", "Highly Influential Player",
  "Influential Player", "Others") ve **kişilik** (Perfectionist, Professional, Iron Willed…). `fm dynamics` bu tek
  sayfayı okuyor; kişilik böylece gizli özniteliklerden okunabilen ilk parça.
- Aynı sekmedeki **Hierarchy** sayfası oyuncuları hiyerarşiye göre gruplayıp oynama süresini de veriyor; bir adın
  önündeki kısa etiketler durum bayrağı (Wnt, Inj, Loa, Slt, Unr — önündeki değil **sonraki** oyuncuya ait).
- `/pull`: `Human.Team.ManagerSupportPlayers{Supportive,Neutral,Opposing}Count` geliyor (0 / 21 / 0);
  `CurrentDressingRoomAtmosphere` ham sayı (15, ölçeği belirsiz); `SocialGroupFeedbackString` liste (açılmıyor).
  Oyuncu başına `HierarchyInfluenceCategoryString` ve `SocialGroupString` boş geliyor. Bilinmeyen bir property
  istenince `/pull` bütün kaydı `{}` döndürüyor.
- **Vaat eklemek** (`EventFmxAddPreContractPromise`): `fm contract --term "Will appoint player as captain"` ekledi
  ("Kaptan yapılacak: evet"), `=kaldır` çıkardı ✓. §8.94'teki kilitli kart bir hafta sonra aynı oyuncuda normal açıldı
  (Playing Time seçenekleri dolu, "Finalise Promises" var); kilit o güne özgüydü, nedeni bilinmiyor. Vaat aşamasında
  "Cancel Negotiations" da "Exit Talks" da yok; `contract --cancel` sayfadan çıkarak kapatıyor ✓.

## 8.96 Sözleşmenin primleri ve maddeleri (10 Ekim akşam)

- `FullContract`'ın her şartı bir `displayvaluewithsort`: `<Alan>.DisplayValue` okunur metni veriyor (`AppearanceBonus`
  "£23.5K", `GoalBonus`, `CleanSheetBonus`, `UnusedSubBonus`, `LoyaltyBonus`, `WageAfterTax` "£93K p/w after tax",
  `ContractType`, `RelegationWageDecrease` "25%", `MinimumFeeReleaseClauseValue`, `SellingOnFeePercentage`…). Olmayan
  şart "-", evet / hayır maddeleri "No". Ekran açmadan, maç sırasında da okunuyor.
- İlk istekte değer boş dönüyor (bağlanması bir kare sürüyor); `fm contracts <oyuncu>` alanları önce bir kez isteyip
  sonra okuyor. `ContractBonuses` / `ContractClauses` (`contractaddonlistreference`) liste olarak açılmıyor;
  `ContractBonusesCount` boş. Kupa / ödül primleri (yeni imzalanan Müldür sözleşmesindeki "UEFA Europa League
  kazanılırsa") bu yüzden görünmüyor.
- Doğrulama: Müldür'ün yeni sözleşmesi imzalanan şartlarla aynı (£31.5K, maç başı £8K, gol yememe £4.7K, yedek £3.9K,
  30/6/2028).

## 8.97 Haber sitesi (10 Ekim akşam)

- Portal › News Site sayfası her haberi bir `NewsItemReference` olarak bağlıyor: `NewsTitleString`,
  `HeadlineSubTitle` (kulüp, turnuva ya da ülke), `NewsItemAgeString` ("1 day 10 hours 15 minutes ago"),
  büyük karolarda `NewsBodyString` (ilk cümle), `MediaNewsCategory`. `fm news` `/tree?depth=40`'tan okuyor.
- Aynı sayfada gelen kutusu mesajları da bağlı (aynı `NewsTitleString`); onların yaşı yok, öyle ayrılıyorlar.
- ⚠ Sayfa karo karo doluyor ve yarım haliyle de bir an sabit duruyor: iki okuma aynı diye kabul edilince 16 başlığın
  8'i geldi. Üç okuma şartıyla dört ardışık çağrıda 16 / 16 ✓ (araya başka sayfa açarak).
- **Haberin tamamı** karonun kendi `#newsBodyText` metninde (çok paragraflı; ağaçtaki `NewsBodyString` yalnızca ilk
  cümle). Karo butonu (`layout-tile-interaction-button`, `RunNewsAction, MarkMediaNewsAsRead`) haberi mesaj
  görünümünde açıyor, orada metin `#BodyText`. `fm news <#>` karoya basıp bu iki addan birini taşıyan metni okuyor ✓
  (dört haber, biri altı paragraf). Yaş "Just Now" da olabiliyor.
- **Kulüplere önerme sonuç verdi:** Defrel 11 Ekim'de £50K ile önerildi (§8.94), 17 Ekim'de gelen kutusuna "Angers SCO
  make Defrel offer" düştü (£50,000, pazarlıksız; Accept / Reject / View Offer).

## 8.98 Gelen teklife mesaj olmadan cevap (10 Ekim akşam)

- Gelen teklifin mesajı ("Angers SCO make Defrel offer") birkaç gün sonra gelen kutusu listesinden düşüyor ama teklif
  bekliyor; süresi dolarken Continue butonu "Review Transfer Offer" oluyor ve `inbox`'ta cevap bekleyen mesaj görünmüyor.
- Teklif **Recruitment › Transfer Activity**'de: "Most Recent Offers" altında oyuncu, kulüp, durum ("Offer Made"),
  bedel, "1 Unresponded Offers (1 Total)". Satırdaki "Offer Made" (`transfer-link-button`) teklif ekranını açıyor;
  `fm offer` onu kart olarak çiziyor ✓ ama o ekranda yalnızca "Accept Offer" var.
- Satırın kendi ikon butonları (etiketsiz): `accept-button` (`EventFmxTransferCentreResponseAccept`), `stall-button`
  (`…ResponseStall`; pazarlıksız teklifte kapalı), `cancel-reject-button` (`…ResponseCancelReject`). Alttaki etiketli
  "Accept / Stall / Reject Offer" üçlüsü (`EventFmxTransferCentreResponse`) satır seçilmeden kapalı.
- `/press` ile `cancel-reject-button` teklifi onaysız reddetti ✓: liste boşaldı, oyun "CanContinue"a döndü.
- **`fm offers`** bunu komuta bağladı (aynı akşam, kayıt 19 Ekim'e dönünce teklif yeniden bekliyordu): liste, `view`
  (teklif kartı), `stall` ✓ (durum "Offer Made (Stalled)", ikinci kez bekletilemiyor — buton kapanıyor ✓), `accept` ✓
  (Defrel: durum "Contract"e geçti, teklif listede kalıyor) / `reject` (`--yes` ister; ret aynı butonla elle doğrulanmıştı). Bir oyuncuya birden çok teklif
  varken satırların sırası görülmedi; kod her teklif için "kulüp, tür, -, durum, bedel" beşlisini ve sıradaki
  `transfer-link-button`'ı eşliyor.

## 8.99 Tercihlerin kalan sekmeleri; bir kilitlenme (10 Ekim akşam)

- "All Preferences" sekmeleri: Your World, Audio & Visual, Accessibility, Match Day, Processing, Third Party, Shortcuts.
  Bildirim ya da "detay seviyesi" diye bir sekme / ayar yok.
- **Processing:** Continue Game Timeout (liste + kutu), Match Scheduling Options, Speed to Process Other Teams' Matches
  (Fast / Normal / Slow), Match Storage Space, Continue Button Legacy Mode ("Don't Continue the game until all messages
  are read"), Automatically Open a Must Respond Message, Disable Fixtures and Results Forced Screen.
  **Your World**'de ayrıca Match Odds, tarih ayracı, para biçimleri, ondalık / basamak simgeleri.
- `fm settings`'e eklendi: oran biçimi, diğer maçların hızı, maç takvimi, okumadan ilerleme yok (hepsi değiştirilip geri
  alındı ✓).
- ⚠ Processing sekmesinde listeler başlıklarından sonra doluyor: seçenekler boş, seçili değer yok okunabiliyordu
  ("'fast' bir seçenek değil. Seçenekler:" / "yapıldı ama sayfa yeniden açılınca ?"). `PreferencesTab` artık listelerin
  dolmasını ve formun iki okumada aynı kalmasını bekliyor; üç turda altı değişiklik temiz ✓. Değişiklikten sonraki
  doğrulama okuması yine de bir kez "okunamadı" dedi (değer tutmuştu).
- ⚠ **Kilitlenme:** art arda 13. ayar değişikliğinde ("Automatically Open a Must Respond Message" → açık) köprü
  "the main thread did not run this request within 30s" dedi ve oyun bir daha cevap vermedi (süreç ayakta, %1 CPU).
  Yeniden başlatmak gerekti. Neden o ayar mı, sayfayı hızlı açıp kapamak mı, bilinmiyor; o ayar CLI'a eklenmedi
  (zaten mesajı kendiliğinden açması CLI akışına uymuyor). Tercih kilitlenmeden önce yazılmıştı, sonradan kapatıldı.

## 8.100 Tablo: hangi turnuvanın? (10 Ekim akşam)

- ⚠ `fm table` Avrupa maçı öncesinde lig yerine Avrupa Ligi tablosunu veriyordu: Portal › Overview'daki karo **sıradaki
  maçın turnuvasının** tablosunu gösteriyor. Üstelik plugin'in `/table`'ı ekranda bağlı bütün tabloların satırlarını
  sıra numarasına göre tek tabloda birleştiriyordu (ilk 18 satır lig, 19-27 Avrupa).
- Plugin: `/table?groups=1` her tabloyu ayrı veriyor; satırlar içinde durdukları listeye göre ayrılıyor (yolun son iki
  adımı atılınca kalan). Eski çağrı (`/table`) aynı.
- CLI: `fm table [turnuva]`. Varsayılan lig (`LeagueName`); karodaki tablo yalnızca `Human.Team.NextFixture.Competition`
  istenen turnuvaysa alınıyor, değilse turnuvanın adı taşıyan `SIButton`'a basılıp o sayfada yeni beliren tablo
  bekleniyor. Altı ardışık çağrı (lig / Avrupa dönüşümlü, araya başka sayfa girerek) tutarlı ✓.
- Avrupa Ligi lig aşaması: 36 takımın 33'ü bağlı (liste sanal, son üç satır ekrana girmeden bağlanmıyor); G / B / M /
  AV sütunları var. Lig, turnuva sayfasından açılınca yalnızca O ve P geliyor. Kupada tablo yok, komut bunu söylüyor.

## 8.101 Milli oyuncular (10 Ekim akşam)

- `Human.Team.Players` listesinde oyuncu başına `/pull` ile geliyor: `Nation.Name`, `InternationalCapsAndGoalsString`
  ("85 caps / 3 goals", hiç yoksa "No Caps"), `InternationalAppearancesThisSeason`, `InternationalGoalsThisSeason`.
  `InternationalReturnDate` paketli tarih (`GameDate { Data1 = … }`), çözülmedi. `fm internationals` bunları basıyor.
- Bilinmeyen property listede yalnızca o alanı düşürüyor (kaydı boşaltmıyor; tek kayıtta `{}` dönüyordu, §8.95).
- Squad › Internationals sayfası milli arada olmayınca neredeyse boş ("Players on international duty: 0", "Upcoming
  event: Euro U19 Ch'ship Quals 12th Nov-31st Mar"); davet durumu milli ara gelince yeniden bakılacak. Tabloya giden
  satırların yolu (`..<panel>…Items.N.binding`) `/pathbind` ile bağlanamıyor (oyun `IndexOutOfRange` atıyor), tablodaki
  form sütunu bu yüzden hâlâ kapalı.

## 8.102 Mentorluk grupları (10 Ekim akşam)

- Squad › Training sekmesinin alt listesinde "Mentoring" (`fm open` adıyla bulmuyor; `SecondaryTabDropdownButton`
  seçimiyle açılıyor). Boşken: "Mentoring Groups", "Create New Mentoring Group" (`EventFmxAddMentoringGroup`, panel
  açıyor — denenmedi) ve konuşma butonu "Ask <yardımcı> to assign mentoring groups" (`SpeakToAction`).
- Yardımcıya sorunca bir grup kuruldu ("Mentoring Group 1", "3/3"), ardından "Acknowledge and Move On".
- Üyeler sayfanın metninde yok; grubun grafiği (`MentoringGroupRelationship1`) her üyeyi `Person.Personality` ve
  `Person.Name` ile, ikili ilişkileri `MentoringGroupRelationshipScore` (0-2) ile bağlıyor, grubun adı arkalarından
  geliyor. `fm mentoring` üyeleri buradan okuyor ✓ (Škriniar, Brown, Mercan); `--auto` yardımcıya kurduruyor ✓.
- Açık: birden çok grup varken blokların sırası görülmedi (kod her `MentoringGroupRelationshipN` bloğunu ayrı grup
  sayıyor); elle grup kurmak / oyuncu eklemek; ilişki puanlarını göstermek.

## 8.103 Kolay çıkmayan iki okuma (10 Ekim akşam)

- **Mutluluğun nedeni.** Veri modelinde oyuncuda `HappinessNegatives` / `HappinessPositives`, `HappinessPlayingTime` …
  (`HappinessCategoryReference`: `Description`, `DetailedString`, `ProsList`, `ConsList`) var, ama `Human.Team.Players`
  satırlarında `/pull` hiçbirini vermiyor; `/pathbind` bağlıyor, `/pathlist` "is not a list ()" diyor. Oyuncunun kendi
  sayfası açıkken denenmedi.
- **Scout odakları.** Recruitment › Recruitment Focuses: seçili odağın adı, durumu ve atananlar metinde ("South Eastern
  Europe · In Progress · 1 scout · 1 analyst · Assignments (4)"), sonuç sekmeleri "Matched (0) / Nearly Matched (12) / In
  Progress (7)" (`EventFmxRecruitmentFocusChangeResultsTable`), konuşma butonları "Edit / Remove / Create a New
  Recruitment Focus". Odak listesi ve sonuç tablosunun satırları metinde de `/people`'da da yok.

## 8.104 Kayıt tarayıcısı: farklı kaydet, sil, belirli kaydı yükle (10 Ekim gece)

**Oyun içinde.** FM menüsü (`dashboard-hud-fm-button`) › "Save Game As" kayıt tarayıcısını açıyor: bir isim kutusu
(`BindableTextEditBox #EditBox`, içinde o an yüklü kaydın adı — yüklü kaydın adını veren tek yer bu; `game.GameName`
boş), "Name / Last saved" tablosu (klasördeki bütün `.fm`'ler), `Delete` (`DeleteSelectedGame`), `Cancel`
(`ClosePanel`), `Save` (`SaveGame`).
- **Farklı kaydet:** kutuya `/type` ile adı yaz, `Save`'e tıkla; `Saving` kalkıp iniyor, dosya 2 sn'de yazılıyor. Ad
  varsa "Replace Existing File?" (Cancel / Confirm) soruyor. Sonraki kayıtlar yeni dosyaya gidiyor.
- **Sil:** satırı seç → `Delete` açılıyor → "Delete File" (No / Yes) → dosya diskten gidiyor. Satır seçmek kutuya o
  satırın adını yazıyor: silmeden sonra `Cancel` ile çıkılmalı, `Save` ile değil.
- **Satır seçmek:** `TableRowNavigatable.TableParent.SetListSelection(row.Index)` — plugin `/rowselect?text=<hücre
  yazısı>[&dry=1]` (`&expand=1|0` satırdaki `ExpandedToggle`'ı çeviriyor; kayıt listesinde bir etkisi görülmedi).
- ⚠ **Devre dışı butona simüle tıklama oyunu kilitledi:** hiçbir satır seçili değilken `Delete`'e `/click` ana thread'i
  dondurdu, oyun yeniden başlatıldı. `/click` artık devre dışı butonu reddediyor (`force=1` ile zorlanır).
- ⚠ Önceki oturumdan cevapsız kalmış bir pencere (mentorluk onayı) açıkken yeni pencere ("Replace Existing File?")
  görünmüyor, `/dialog` eskisini veriyor; eskisi cevaplanınca yenisi geliyor.

**Ana menüde.** Oyun içinde "Load Game" yok: başka kayıt yalnızca ana menüden yükleniyor ("Load an FM26 career…"
döşemesi, `LoadGameTileSelection`). Açılan ekran aynı tablo + `Delete` + `Load` (`LoadSelectedGame, ClosePanel`);
satır seçilince sağda kaydın özeti (boyut, oyun tarihi, ülkeler, menajer / takım) çıkıyor ve `Load` açılıyor.
"Browse" aynı listeyi dosya adlarıyla (`.fm`) veriyor, "Back to last saves" geri döndürüyor.
- Liste sanal: gizli penceredeki ekranda yalnızca **üç satır** çiziliyor (ilk açılışta bazen bir). Kalanlar `/scroll`
  ile geliyor; kaydırma çapası olarak satırın tarihi kullanılıyor, çünkü kaydın adı arkadaki ana menü döşemesinde de
  yazıyor ve `/scroll` ilk bulduğunu alıyor.
- macOS dosya adını ayrık harfle veriyor ("c" + sedil), oyun birleşik "ç" gösteriyor; CLI ICU'suz derlendiği için
  `string.Normalize()` hiçbir şey yapmıyor. Adlar elle birleştiriliyor (`Composed`), yoksa satır bulunmuyor.
- `fm load` bir kayıttayken oyunu kapatıp `fm start --menu` gibi ana menüye açıyor, sonra satırı seçip `Load`'a basıyor:
  ana menüden 14 sn, yeniden başlatmayla ~80 sn.

Canlı doğrulama: farklı kaydet (yeni ad, var olan ad), yüklü kaydı silmeyi reddetme, silme, ana menüden eski yedeği
(`(v02)`) yükleme, kayıttayken yeniden başlatarak yükleme, olmayan / belirsiz ad.

## 8.105 Stages sayfası: kupa turları, eleme eşleşmeleri, her ülkenin ligi (10 Ekim gece)

**Sayfa.** Portal › Stages. Üstünde dört liste: turnuva (tek seçeneği "Competitions ›" alt menüsü olan liste; etiketi
seçili turnuvanın adı), aşama ("First Round … Fourth Round", "League Phase", alt menülü "Main Path ›"; sonda hep
"xG Table"), görünüm ("Round / Tree" kupada; ligde "Overall / Home Record / Away Record / Last 5 Games …") ve sezon.
Sayfa her açılışta **sıradaki maçın turnuvasına** dönüyor.

**Turnuva menüsü dünyanın tamamı:** Competitions › kıta › (ülke ›) Men's / Women's › turnuva (› Under 19s …). Bu kayıtta
195 turnuva. `/menu?ptr=&names=a>b` her dalı veriyor, `/select?ptr=&names=Competitions>Europe>Türkiye>Men's>Turkish Cup`
seçiyor. "Başka ligler"in yolu bu (§8.81'de aranıp bulunamamıştı): başka ülkenin lig tablosu da burada, `/table?groups=1`
son grubu veriyor.
- Turnuva seçilince sayfa yeniden kuruluyor: listelerin pointer'ları değişebiliyor (her zaman değil), o yüzden her adımda
  yeniden bulunuyorlar; seçimin tuttuğu etiket + pointer + aşama seçenekleri değişince anlaşılıyor.
- Sayfanın turnuvaya verdiği ad menüdekinden kısa olabiliyor ("English FA Cup" → "FA Cup").

**Bir eşleşmenin yapısı.** Her eşleşme bir `#MainPart`: `#LeftSide` (ev), `#Versus`, `#RightSide`. Takım adı `SIText #text`;
yanındaki `SIText #SIText` iki maçlı turda **toplam skor**. `#Versus`'ta skor (oynanmadıysa "V"), iki yanında
`#HomeResultTypeVisible` / `#AwayResultTypeVisible`: turu geçen tarafta "p" (penaltılar) ya da "e" (uzatma). Düz metin
akışından bu taraf bilgisi çıkmıyor; plugin `/ties` yapıdan okuyor.
- ⚠ `SIButton` da bir `TextElement` (Unity `Button`), kendi metni boş: metin toplarken "TextElement'se dur" demek
  butonun içindeki asıl yazıyı kaçırıyor (ilk `/ties` yalnızca "p"/"e"leri veriyordu).
- Lig rozeti ("2L", "SL") ve ülke kodu ("CZE") `#MainPart`'ın dışında; okunmuyor.
- Liste sanal: uzun turda 18 eşleşme çiziliyor (Türkiye Kupası 2. tur 39 eşleşme). `/scroll?anchor=<ev takımı>&to=0..1`
  onar adımda gezilip birleştiriliyor.
- Kurası çekilmemiş tur: "This round will be drawn on 31/10/2025", eşleşme yok. Sayfa kupada son aşamada (Final) açılıyor;
  aşama yazılmayınca çekilmiş son tur ikiye bölerek aranıyor.
- "Tree" görünümü aynı eşleşmeleri takım / gol alt alta veriyor; kullanılmadı.

`fm stages [turnuva] [aşama] [--all|--list|--refresh]`; turnuva listesi `~/.config/fm-cli/competitions.json`'da tutuluyor,
ad bulunamazsa bir kez yeniden yürünüyor. Canlı: Türkiye Kupası (oynanmış / çekilmiş / çekilmemiş tur), Avrupa Ligi
playoff (toplam skor, penaltı), FA Cup, Premier League, Bundesliga, Şampiyonlar Ligi; belirsiz ad ("cup").

## 8.106 Elle mentorluk grubu (11 Ekim gece)

- Bir grup tam **üç oyuncu** (`MentoringGroupPlayerOne/Two/Three`). Sayfadaki grup listesi bir `SIRadioGroup` (seçenekler
  grupların adları); çizilen grafik yalnızca **seçili grubun** (`selectedmentoringgroup`). Her grubu okumak için sırayla
  `/select?name=SIRadioGroup&index=N` ve grafiğin yetişmesini bekleme (ad hemen, üyeler biraz sonra değişiyor). §8.102'deki
  "her blok ayrı grup" varsayımı yanlıştı: birden çok grupta tek grup, yanlış adla görünüyordu.
- "Create New Mentoring Group" (`EventFmxAddMentoringGroup` + `OpenPanel`) **grubu hemen yaratıp** düzenleyiciyi açıyor:
  "0/3 Selected", ad kutusu (`EditBox`), tablo (Sel. / Player / Age / Workload / Personality / Hierarchy), `Confirm`
  (`EventFmxEditMentoringGroup`, `…EditMentoringGroupName`, `PanelHistoryBack`), `Auto Select`, `Undo Changes`, `Clear All`.
  Onaylamadan çıkılırsa boş bir grup kalıyor (sayfada yine "3/3" yazıyor); CLI vazgeçince onu siliyor.
- **Satır işaretlemek:** baştaki kutu sıradan Unity `Toggle`'ı, ama değerini yazmak seçimi saydırmıyor (sayaç "0/3" kalıyor).
  Sayan şey listenin kendi seçimi: `row.TableParent.ListElement.SelectionInternal.SetSelected(row.Index, true)` →
  "3/3 Selected", `Confirm` açılıyor. Plugin `/rowselect?text=<ad>&check=1|0`. Dördüncü de işaretlenebiliyor ("4/3").
- Aynı ad arkadaki sayfanın tablosunda da satır (Training'in oyuncu listesi); `check` isteyen çağrı kutusu olan satırı arıyor.
  `Shows` derinliği 12'den 28'e çıktı (ad hücrede 13+ kat içeride).
- Liste sanal (16 satır çiziliyor, kalanı kaydırınca); kaydırma çapası iş yükü hücresi ("Light"), ad değil.
- Silme: grubu seç, `EventFmxDeleteMentoringGroup` taşıyan ikon butona tıkla — **sormadan** siliyor.
- ⚠ Radyo grubu sayfadan bir an sonra geliyor. İlk yazımda "önceki gruplar" listesi boş okundu, vazgeçme temizliği bütün
  grupları yeni sanıp sildi (kayıt edilmemiş oturumda; gruplar yeniden kuruldu). Adlar artık sayfa metni ile radyo grubu
  aynı şeyi söyleyene dek bekleniyor ve temizlik yalnızca "tam bir fazla grup" varken, o tek grubu siliyor.

`fm mentoring new [<o1> <o2> <o3>] [--name]`, `fm mentoring remove "<grup>" --yes`. Canlı: iki grup kurma (adlı / adsız),
çok gruplu okuma, aday listesi (artık grup bırakmıyor), olmayan oyuncu, iki oyuncu, onaysız / onaylı dağıtma.

## 8.107 Taktiği dosyaya kaydetmek ve yüklemek (11 Ekim gece)

- Taktik menüsü (içinde "Clear Slot" olan etiketsiz liste) › **Save**: panel — "Change Preview Image", başlık kutusu,
  açıklama kutusu (ikisi de `EditBox`), "No save created yet.", `Cancel` (`PanelHistoryClose`), `Save` (`SaveAddon`).
  Başlık yazılıp Save'e basılınca `…/Football Manager 26/tactics/<başlık>.fmf` yazılıyor (1 KB) ve **etkin taktiğin adı da
  başlık oluyor**. Save başlık yazıldıktan bir an sonra açılıyor (hemen tıklanınca "disabled"). Aynı ada yeniden kaydetmek
  sorusuz üzerine yazdı.
- **Load...**: "Name / Last saved" tablosu, `Delete` (`EventDeleteAddOn`), `Load` (`EventLoadAddOn, ClosePanel`). Satır
  `/rowselect` ile seçilince Load açılıyor; yüklenen taktik **etkin yuvadakinin yerine** geçiyor (yuva sayısı değişmiyor).
  Liste yeni yazılmış dosyayı ilk açılışta göstermeyebiliyor; kapatıp açınca geliyor.
- Yüklemeden sonra planlayıcı kendiliğinden yeniden bağlanmıyor (`/tactic` boş); sayfa yeniden açılınca ad geliyor.
- ⚠ Sayfa açıldıktan hemen sonra bulunan menü, değiştirilmekte olan eski sayfanın menüsü olabiliyor; seçim boşa gidiyor.
  Panel 3 sn'de açılmazsa menü yeniden bulunup seçim tekrarlanıyor (`TacticPanel`).
- Öbür menüdeki "Load Tactic" (yeni yuvaya) denenmedi; `tactic copy` + `tactic load` aynı işi görüyor.

`fm tactic save "<ad>" [--note] [--yes]`, `fm tactic saved [delete "<ad>" --yes]`, `fm tactic load "<ad>" --yes`. Canlı: iki
kez kaydet / yükle turu, üzerine yazma, uzun ad, Türkçe karakterli ad ("Öbür"), belirsiz ad, silme.

## 8.108 Yönetimden talep (11 Ekim gece)

- Club › Board › "N board member" döşemesi kartı açıyor. Talep türleri dört `GenericDropdown` (etiketi tür: Facilities,
  Finance, Networking, Staff; seçenekleri talepler, bazıları alt menülü — "Negotiate Existing Affiliation › <kulüp>",
  "Coaching Staff Allowed › <takım>") ve iki düz buton (`StandardButton`: Build New Stadium, New Contract); hepsi
  `SpeakToAction`. `/dropdowns?deep=1` alt menüleri `Submenus` altında indeksle veriyor. Bu kayıtta 33 talep.
- `/select?ptr=&choice=<talep>&fire=1` (alt menüde `names=ana>alt`) talebi iletiyor; `/talk`: "» <talep>", "The board will
  get back to you shortly to let you know of their decision", tek cevap "Acknowledge and Move On".
- Yapılmış talep listeden kalkıyor. **Üç talepten sonra kartta hiç talep seçeneği kalmadı** (liste de buton da yok,
  açıklama yok); iki gün sonra kararlar gelen kutusuna düşünce ("Number of scouts increased", "Staff Numbers Update",
  "Board respond to board request - Increase Transfer Budget") seçenekler geri geldi.
- Karttaki "Board Requests" tablosu (Request · Status · Started · Outcome · Completed) bu taleplerle dolmadı; uzun süren
  işler (tesis, stadyum) için olabilir.
- Build New Stadium / New Contract denenmedi (görüşme açmaları beklenir; `talk` ile cevaplanır).

`fm board request ["<talep>" --yes]`. Canlı: liste, belirsiz ad, onaysız, üç gerçek talep (biri alt menüden), talep
alınmayan durum, kararların gelişi.

## 8.109 Scout odakları: okuma (11 Ekim gece)

- Recruitment › Recruitment Focuses bir odağı gösteriyor; odak, seçenekleri yalnızca **durum** olan adsız bir listeden
  seçiliyor (['In Progress' ×4, 'Complete' ×2]). `/select?ptr=&index=N` odağı değiştiriyor; başlık metinde: ad, durum,
  "Assigned to:", "1 scout", "1 analyst", "Assignments (4)", "Matched (a)", "Nearly Matched (b)", "In Progress (c)".
- Sonuç tablosu **metinde var** (§8.103'te "yok" denmişti: o an seçili odağın eşleşeni yoktu ve Matched sekmesi boştu).
  Satır: [durum işareti] ad, ülke, yaş, mevkiler, en iyi rol, kulüp, lig, değer aralığı, not (A–E), tavsiye ("Quality",
  "Very Good", "Practical", "Last Resort", "Superb", "Good"). Durum işareti ("Inj") yalnızca bazı satırlarda, adın önünde —
  hücreler onar onar sayılamıyor; satır başı "ad + üç harfli ülke + yaş" kalıbından bulunuyor.
- Sekmeler `matched-tab` / `nearly-matched-tab` / `in-progress-tab` (`EventFmxRecruitmentFocusChangeResultsTable`):
  `/press` (olayı kuyruğa koymak) tabloyu değiştirmiyor, `/click` değiştiriyor.
- Tablo sanal (18 satır); `/scroll` ile kalanı geliyor.
- **Odak kurma / düzenleme** ("Edit Recruitment Focus", `SpeakToAction`) iki adımlı sihirbaz açıyor. 1/2: saha üstünde mevki
  butonları (`EventFmxSelectPosition`, "Any Position" / "Any Tactic"), topla / topsuz rol ve rol yeterliliği (± butonlar).
  2/2: ad (en çok 25 harf), en düşük şimdiki / potansiyel yetenek, yaş aralığı, tarama bölgeleri ("Choose an area"),
  öncelik (Top / Standard), "Ongoing", scout ve analist atama ("Edit"), "Include results found in other Recruitment
  Focuses"; ayrıca "Further Details" sekmesi. `Cancel` → "Are you sure you want to cancel this Focus?" (Confirm).
  Yazılmadı: çok kontrollü, ayrı bir iş.

`fm scout [#|ad] [--matched|--near|--progress]`. Canlı: altı odak, üç sekme, işaretli satırlar, 21 satırlık liste.

## 8.110 FM menüsünden dünya ve kayıt bilgisi (11 Ekim gece)

FM menüsünün (`dashboard-hud-fm-button`) döşemeleri metin olarak: "Add / Remove Leagues · Selected Nations · 5 · Selected
Leagues · 12 · Approximate player count · 34500 · Game Status · <menajer> · <kulüp> · Playing · 1/32 connections · Detail
Level · …" ve kayıt bloğu "<kayıt adı> · Save Game · Save Game As · Last saved over 31 minutes ago · (2 days ago in-game
time) · Saves automatically every week · 0 Days, 13 Hours, 55 Minutes · Estimated game speed:". Döşemeler sırayla doluyor:
iki okuma aynı olana dek beklenmezse kayıt adının yerinde "Credits" okunuyor. `fm world` bunu veriyor ve menüyü
(`PanelHistoryClose`) kapatıyor. Yüklü kaydın adını veren ikinci yer burası (ilki kayıt tarayıcısının kutusu, §8.104).

## 8.111 Personel sorumlulukları (11 Ekim gece)

- Club › Responsibilities ("Staff Responsibilities"): gruplar halinde uzun, sanal bir liste (2420 px; sekiz grup, 29
  görev). Grup: ad + `TakeControlAll` ("Take Control of All") + `DelegateAll` ("Delegate All to Recommended"). Görev:
  başlık, açıklama, "Take Control" / "Delegate" (`buttons-segmented-button-row-…`), `AssignableStaff` listesi (etiketi
  görev kimdeyse o; seçenekleri adsız `PersonReference`), altında kişinin unvanı. `/form?anchor=&up=12&buttons=1` hepsini
  okuma sırasıyla veriyor.
- ⚠ İkili butonun **adı** hangisinin geçerli olduğunu söylemiyor: "Take Control" hep `…-active-…`, "Delegate" hep
  `…-inactive-…`. Geçerli olan, **basılamayan**: görev menajerdeyken "Take Control" kapalı, "Delegate" açık; devredilmişken
  tersi. İkisi de kapalıysa görev kilitli (U19 takım seçimi → U19 menajeri).
- "Delegate"e tıklamak görevi oyunun önerdiği kişiye veriyor (Devin Özek, futbol direktörü); soru sormuyor. Etiket
  butonlardan bir an sonra güncelleniyor. "Take Control" geri alıyor.
- Belirli bir kişiye vermek (`AssignableStaff` seçenekleri) denenmedi: seçeneklerin adı `/dropdowns`'ta yok.
- Yalnızca personel işleri burada; antrenman, transfer, basın gibi öbür devirler bu sayfada değil.

`fm responsibilities [süzgeç]`, `… delegate "<görev>"`, `… take "<görev>"`. Canlı: tam liste, süzme, belirsiz ad, iki görevde
devret / geri al, zaten o durumda olan.

## 8.112 Altyapı (11 Ekim gece)

- Squad › Youth Setup'ın alt sekmeleri (`SecondaryTabDropdownButton`): Overview, Youth Environment, Youth Performance,
  Youth Candidates, Under 19s Competitions, U19 Fixtures. `fm open "Youth Environment"` adıyla bulmuyor; sekme listesinden
  `fire=1` ile seçiliyor.
- Youth Environment'ta iki metin bloğu: (1) "Overall Youth Rating · Good · Youth Facilities · 2 First Team Candidates ·
  Player / Best Pos / Age / Ability / Potential · <ad, mevki, yaş>… · 2026 Youth Intake · 11th March 2026 · (141 days to
  go)" — yetenek / potansiyel yıldız çizimi, metni yok; (2) "Club Atmosphere" cümleleri, "Academy Coaching · … · Youth
  Recruitment · … · Youth Level · 11 · Youth Stadium · …", "Staff · Coaching Team · 11 / 13 · Recruitment Team · 0 / 0 ·
  Medical Team · 5 / 5".
- Youth Performance ve Youth Candidates sekmelerinin içeriği bulunamadı (çapa olacak metin bilinmiyor; adaylar Mart'taki
  alımdan önce boş olmalı).

`fm youth`.

## 8.113 `/pull` liste değerlerini açıyor; gelişim verisi (11 Ekim gece)

- `/pull`'da değeri `List<TypedValue>` olan özellik artık öğeleri virgülle ayrılmış olarak geliyor (önce yalnızca tipin adı
  geliyordu; §8.85 ve §8.92'de park edilen şey). İlk okuma boş dönebiliyor (bağlanma gecikmesi), ikincide dolu.
- **`AbilityProgressHistory`** (kendi oyuncularımızda): son dört aylık ölçüm, her biri **yetenek × 220** (Ederson 35860 →
  163; Oosterwolde 29700, 29920, 29920, 30360 → 135, 136, 136, 138). Çoğu tam sayı çıkıyor; küsuratlılar (Kahveci 108.3,
  Asensio 143.4) sakatlık / form düşüşü dönemlerine denk geliyor. Yeni gelen oyuncuda üç ölçüm var (Defrel).
  `PerceivedPotentialAbility` ile aynı ölçek (/200).
- Başka kulübün oyuncusunda bu özellik gelmiyor.
- `RecentFormValuesAllComps` oyuncuda yok; takımlarda denenmedi (tablodaki form sütunu için sıradaki adım).

`fm progress` (kadro tablosu, değişime göre sıralı) ve oyuncu kartında "Yetenek" satırı.

## 9. Sıradaki adımlar

Yapılacaklar listesi artık burada tutulmuyor: sıralı iş listesi ve günlük `PLAN.md`'de
("Sıradaki işler" ve "Günlük"). Bu dosya yalnızca bulguları taşıyor.

---

## 10. Kaynaklar

| Kaynak | Neden önemli |
|---|---|
| [oasis1701/footballmanager-accessibility](https://github.com/oasis1701/footballmanager-accessibility) | NVDA ekran okuyucu plugin'i. `FMNavigationManager.CurrentFocus` kullanımının canlı örneği. Erken aşama ama yol gösterici. |
| [DadMych/fm26-player-export-macos](https://github.com/DadMych/fm26-player-export-macos) | arm64 BepInEx bundle + yamalı Il2CppInterop. **Bu proje onsuz M2'de yürümezdi.** Plugin deseni de buradan alındı. |
| [Thunderstore BepInExPack FM26](https://thunderstore.io/c/football-manager-26/p/BepInEx/BepInExPack_FootballManager26/) | Resmi pack (x64) |
| [FM26 AI Dialogue mod](https://www.fmscout.com/a-fm26-mod-ai-dialogue.html) | Plugin içinden localhost'a HTTP atılabildiğinin kanıtı |
| [Perfare/Il2CppDumper](https://github.com/Perfare/Il2CppDumper) | Dump aracı |

**Not:** FM'i dışarıdan bir ajana/CLI'a oynatan mevcut bir proje bulunamadı
(Nexus, ModDB, Thunderstore, FM Scout, sortitoutsi tarandı — 2026-09). Niş boş.
