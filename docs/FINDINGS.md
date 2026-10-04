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

## 9. Sıradaki adımlar

- [x] FM26 kurulumu, IL2CPP doğrulaması
- [x] Il2CppDumper ile tam dump
- [x] BepInEx arm64'te ayağa kaldırma — **çalışıyor**, 2 plugin yüklendi
- [x] Probe plugin yazımı ve kurulumu
- [x] Probe v1 — singleton'lar + Bindings instance'ı bulundu
- [x] Probe v2 — ağaç gezildi, string path çalıştı, canlı veri okundu
- [x] Kayıt yükleme — `LoadSelectedGameEvent` ile CLI'dan kariyer açılıyor
- [x] Kadro listesi bulundu: `GameStateCache.Manager.Club.MainTeam.Players.ListLoader0.<0..1A>`
      (27 oyuncu, her biri `PersonReference`)
- [x] Path adresleme — key hesaplayarak çözüldü (§8.2b), span'a gerek kalmadı
- [x] **Veri modelinin tamamı** — 160 tip / 6571 property, isimli (§8.11)
- [x] **Event kataloğu** — 275 aksiyon tipi, alanlarıyla (§8.11)
- [x] **Canlandırma mekanizması** — event gönder, veri ağaçta belirir (§8.13)
- [x] **Genel event göndericisi** — `/send?event=...` (§8.14)
- [ ] `DynamicReference` sözlüğünü gez — aksiyonların sayısal id'si orada (§8.14)
- [ ] **Oyuncu özelliklerini canlandır.** Ağaç hâlâ tembel: oyuncu düğümünde sadece `UniqueId`
      var. `Bind(key)` var olmayan özelliği **oluşturmuyor**; denenen isimlerin
      (`Name`, `Condition`, `Age`, `Fitness`, `FullName`...) hiçbiri yok.
      Demek ki canlandırmayı yapan şey `BindingBag.Queue(id, element, BindingPath, cb)` —
      bir `IBindableElement` üzerinden. Sıradaki deneme: Squad ekranını event/panel ile açtırmak.
- [ ] Veri katmanı: path → TypedValue sarmalayıcı, referans gezinme, lazy bind
- [x] Plugin içinde HTTP server (v0.2.0: /health /tree /get /exists /props)
- [ ] `fm-cli` komut katmanı
- [ ] MCP server
- [ ] Ses

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
