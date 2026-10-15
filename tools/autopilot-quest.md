# Otopilot görev defteri — turlar arası hafıza

Her tur başında oku, sonunda güncelle. Taze bir oturum ilerlemeyi buradan alır; aynı maddeyi
iki kez yapmaz, park edileni tekrar denemez.

## Şu an
- Madde: (yok — son tur D "önbellekler hangi kayda/dile ait" maddesini bitirdi, sıradaki tur seçecek)
- Durum: bitti

## Ölçülen kanıt (tur tur)

### Tur 2 — D "`~/.config/fm-cli/` önbellekleri hangi kayda/dile ait" (§8.190, 15 Ekim gece)
Ne: `diff`in baktığı hâl (`Diff.cs`) üst klasörde yalnız takım adıyla tutuluyordu; oyuna ait kelimeler/dil
taşıdığı için `CacheFile`e (dil + yönetici–kulüp) taşındı. Dosya: `cli/fm/Diff.cs` (+ `docs/BAGIMLILIKLAR.md`,
`docs/FINDINGS.md`). Paylaşılan koda (Bridge/CacheFile tanımı) dokunulmadı → sweep gerekmedi.
- Sorun (önce): TR'de `fm diff` ile bakış kaydedilip `fm settings dil English` sonrası `fm diff` → her oyuncunun
  sözleşme tarihi değişmiş sayıldı; ölçüm çıktısı: `"Contracts":[{"Name":"Jamie Pardington","Was":"N/A","Is":"Yok"}]`.
  Dosya: `~/.config/fm-cli/diff-Lincoln.json` (üst klasör, takım adıyla).
- Durumlar + diller (sonra):
  - EN ilk: `fm diff --json` → `{"First":true,...}`; dosya `cache/english/Faruk Kavlak - Lincoln/diff-Lincoln.json`.
  - EN ikinci: `fm diff --json` → `"Contracts":[]` (yanlış değişiklik yok).
  - TR ilk: `fm settings Language "Türkçe"` → `fm diff --json` → yeniden `{"First":true,...}` (EN dosyası ve
    eski üst dosya okunmadı); TR ikinci → `"Contracts":[]`; dosya `cache/turkish/.../diff-Lincoln.json`.
  - Üstteki eski `diff-Lincoln.json` dokunulmadı (mtime 23:53 sabit kaldı).
- Kayıt test kaydı; TR'ye dönüldü.

### Tur 1 — B "Mutluluk seviyesi" (§8.189, 15 Ekim gece)
Ne: `happiness` sıra+rengi seviyenin yazısına değil oyunun puanına bağlandı; moral/konum/seviye yazıları
oyunun diline çevrildi. Dosya: `cli/fm/Happiness.cs` (+ `docs/BAGIMLIKLAR.md`, `docs/FINDINGS.md`,
`docs/manual/KILAVUZ.md`). Paylaşılan koda dokunulmadı → sweep gerekmedi.
- Sorun (önce): `Array.IndexOf(HappinessLevels, p.Level)` "Biraz Mutlu"/"Biraz Mutsuz" için -1 →
  bu seviyeler listenin başına düşüyor, rengi kırmızı; `--why` moral/konum İngilizce ("Poor").
  Kanıt: `/tmp/happ.json` (önce) Ben House "Very Unhappy", listenin başı "Biraz Mutlu".
- Veri yolu (ölçüldü, `/pull`): `Human.Team.Players` › `HierarchyOverallHappinessLevel` — 27/27 oyuncu.
  Değerler: 80 Delighted, 60 Very Happy, 40 Happy, 20 Slightly Happy, -14/-20 Content, -40 Slightly Unhappy,
  -65 Very Unhappy. Bant sınırları boşluklarda: `<-30` kırmızı, `<30` sarı, üstü yeşil.
- Komutlar/kanıt (sonra):
  - TR: `fm open Messages` → `fm happiness --json`; `fm happiness --json` (açık); `fm open Messages` →
    `fm happiness --json` → üçü de `md5 2d06d6c224a52fe0286684100b10c3d9`, sıra artan (-65…80 doğrulandı).
  - TR renk (pty): `script -q /dev/null fm happiness` → `38;5;9` Çok Mutsuz/Biraz Mutsuz (kırmızı),
    `38;5;11` Memnun/Biraz Mutlu (sarı), `38;5;2` Mutlu+ (yeşil).
  - TR `--why`: `fm happiness "Ben House"` → "Çok Mutsuz · moral Kötü · Yüksek Nüfuzlu Oyuncu";
    `fm happiness --why` 27/27, 11 sn, rc=0.
  - EN: `fm settings dil English` → `fm open Messages` → `fm happiness --json`; `fm happiness --json`
    (açık) → ikisi aynı; renk Very Unhappy/Slightly Unhappy kırmızı, Content/Slightly Happy sarı.
  - Dönüş: `fm settings Language "Türkçe"` → "English → Turkish"; `fm happiness --json` önceki TR
    çıktısıyla birebir aynı.
- Önce/sonra (aynı oyuncu, TR): `fm happiness "Ben House"` önce "moral Poor", sonra "moral Kötü".

## Park edilenler ([?] + Faruk'a soru)
- (yok)

## Sabitler
- Kapsam: **A** (zamanlama) + **B** (dil/kimlik) + **C** (ayarlar), mümkünse **D**.
- Girmeyeceğiz: §14 UX/görsel, **G** (platform), **H** (yayın).
- Model: opencode-go/deepseek-v4.1-flash · sürücü: tools/autopilot.sh
- Commit: main'e, push yok. Conventional Commits, son satır `Co-Authored-By: OpenCode`.
