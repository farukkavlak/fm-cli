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
