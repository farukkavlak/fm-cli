# Playtest — 2026-10-22 (gece turu)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 17–31 Ocak 2027 · 3 maç: Blackpool 0-1 (D), Stockport 3-2 (G), Reading 1-4 (M)
`fm save` çağrılmadı; oyunun otomatik kaydı kullanıldı.

## Bulgular

1. **Maç sonrası sonuç ekranında komut rc=0 ile yutuluyor** — 30 Oca 2027 16:45, `fm opponent` rc=0: çıktı "Oyun maç sonrası sonuçlar ekranında; başka sayfa açmadan önce geç: next"; aynı anda `fm table --json --csv` de rc=0 ve tablo yerine aynı cümle. Komut çalışmadığı hâlde rc=0 dönüyor, script hata göremiyor.
2. **Türkçe arayüzde İngilizce seçenek** — 29 Oca 2027 13:59, `fm reply "Erik Ring sakatlandı"` rc=0: seçenekler "1 İğneyle Oynat (650 £) / 2 Leave To Physio (Free)"; aynı seçenek `fm reply "Erik Ring sakatlandı" 2` sonrası "Fizyoterapist Gözetimine Bırak (Masrafsız)" olarak Türkçeleşiyor.
3. **Çelişen biçim bayrakları sessizce kabul ediliyor** — 23 Oca 2027, `fm table --json --csv` rc=0: hata yok, yalnız JSON dizisi bastı ("[{\"ShowTableSplit\": ..."); `--json` ile `--csv` birlikte verilince komut reddetmeli.
