# fm-cli

Gerçek **Football Manager 26**'yı terminalden oynat.

```
$ fm squad --fitness
$ fm results "Süper Lig"
$ fm transfers list
$ fm next
```

Oyunu sıfırdan yazmıyoruz — gerçek FM26'nın içine bir BepInEx plugin'i enjekte edip
oyunun kendi veri katmanını (`SI.Bindable.Bindings`) ve navigasyon sistemini CLI'a açıyoruz.
Oyun arka planda açık kalır; ekran, fare, ekran görüntüsü yok.

**Durum:** keşif tamamlandı, BepInEx arm64'te çalışıyor, probe plugin canlı test bekliyor.

📄 Bütün teknik bulgular, API'ler, kısıtlar ve kurulum adımları: **[docs/FINDINGS.md](docs/FINDINGS.md)**

## Yapı

```
plugin/FmCliProbe/    FM26 içinde çalışan keşif plugin'i (C#, BepInEx 6 IL2CPP)
docs/FINDINGS.md      araştırma notları — buradan başla
```

## Gereksinimler

- macOS, Apple Silicon
- Football Manager 26 (Steam)
- BepInEx 6 IL2CPP arm64 — kurulum `docs/FINDINGS.md` §3'te
- .NET SDK

## Plugin'i derle ve kur

```bash
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll \
   "$HOME/fm26_bep/BepInEx/plugins/FmCliProbe/"
```
