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

### 8.2 `ReadOnlySpan<char>` köprüsü ✅

Il2CppInterop `string → ReadOnlySpan<char>` dönüşümü vermiyor, ama `Il2CppSystem.ReadOnlySpan<char>`'ın
il2cpp dizisinden kurucusu var:

```csharp
var chars = new Il2CppStructArray<char>(s.Length);
for (var i = 0; i < s.Length; i++) chars[i] = s[i];
var span = new Il2CppSystem.ReadOnlySpan<char>(chars);
bindings.Get(span);           // by-value overload
bindings.Exists(ref span);    // ref overload
```

Kod: `plugin/FmCliProbe/src/Interop.cs`. Canlı testte çalıştı:

```
Exists("Game") = true   Get -> (GameReference) FM.UI.GameReference
```

**Namespace notu:** interop assembly'lerinde `Il2Cpp` öneki **yok** — `SI.Bindable.Bindings`,
`FM.UI.TeamReference` düz yazılıyor. Sadece BCL tipleri `Il2CppSystem.*`.

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

## 9. Sıradaki adımlar

- [x] FM26 kurulumu, IL2CPP doğrulaması
- [x] Il2CppDumper ile tam dump
- [x] BepInEx arm64'te ayağa kaldırma — **çalışıyor**, 2 plugin yüklendi
- [x] Probe plugin yazımı ve kurulumu
- [x] Probe v1 — singleton'lar + Bindings instance'ı bulundu
- [x] Probe v2 — ağaç gezildi, string path çalıştı, canlı veri okundu
- [ ] **Probe v3** — `game.Human.Team`'e `GetProperties` ile şemasını sor; kadro listesini `Bind` edip
      oyuncu başına `Condition` oku. Bu çalışırsa `fm squad --fitness` fiilen var demektir.
- [ ] Veri katmanı: path → TypedValue sarmalayıcı, referans gezinme, lazy bind
- [ ] Plugin içinde HTTP server
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
