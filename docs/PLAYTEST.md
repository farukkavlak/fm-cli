# Playtest — 2026-10-22 (gece turu)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 17–31 Ocak 2027 · 3 maç: Blackpool 0-1 (D), Stockport 3-2 (G), Reading 1-4 (M)
`fm save` çağrılmadı; oyunun otomatik kaydı kullanıldı.

## Bulgular

1. **Maç sonrası sonuç ekranında komut rc=0 ile yutuluyor** — 30 Oca 2027 16:45, `fm opponent` rc=0: çıktı "Oyun maç sonrası sonuçlar ekranında; başka sayfa açmadan önce geç: next"; aynı anda `fm table --json --csv` de rc=0 ve tablo yerine aynı cümle. Komut çalışmadığı hâlde rc=0 dönüyor, script hata göremiyor.
2. **Türkçe arayüzde İngilizce seçenek** — 29 Oca 2027 13:59, `fm reply "Erik Ring sakatlandı"` rc=0: seçenekler "1 İğneyle Oynat (650 £) / 2 Leave To Physio (Free)"; aynı seçenek `fm reply "Erik Ring sakatlandı" 2` sonrası "Fizyoterapist Gözetimine Bırak (Masrafsız)" olarak Türkçeleşiyor.
3. **Çelişen biçim bayrakları sessizce kabul ediliyor** — 23 Oca 2027, `fm table --json --csv` rc=0: hata yok, yalnız JSON dizisi bastı ("[{\"ShowTableSplit\": ..."); `--json` ile `--csv` birlikte verilince komut reddetmeli.

---

# Playtest — 2026-10-23 (gece turu)

Kayıt: `Faruk Kavlak - Fenerbahçe (v03)` · Lincoln City · Sky Bet League One
Oyun tarihi: 31 Oca – 25 Şub 2027 · 4 maç: Exeter 1-4 (D), Norwich 3-0 (G, FF Kupası), Wigan 0-3 (D), Wycombe 3-1 (G)
`fm save` çağrılmadı; oyunun otomatik kaydı kullanıldı.

## Bulgular

1. **Maç sonrası sonuç ekranında `fm press` butonu bulamıyor** — 6 Şub 2027 16:45, `fm buttons` "Son Sonuçlar / continue-button / ContinueGame" diyor; aynı ekranda `fm press "Son Sonuçlar"` rc=1: "error: no control labelled 'Son Sonuçlar' on the loaded screens". 31 Oca'da "Devam" ile de aynı. Buton listede duruyor ama basılamıyor; ekran ancak `fm next` ile geçiliyor.
2. **`fm reply <mesaj> 1` onayı uygulamıyor ama rc=0 ile "cevaplandı" diyor** — 6 Şub 2027 15:00, `fm reply "Kadro Seçim Önerisi: Exeter" 1` rc=0: "Cevaplandı: … → Tavsiye Edilen Kadroyla Sahaya Çık" + "Ama oyun mesajı hâlâ cevap bekliyor gösteriyor."; `fm tactic` 11'i değişmiyor (Bradley/Street/Moylan), `fm match --play --no-talk` rc=1 "Oyun bir şey soruyor: Tavsiye Edilen Kadroyla Sahaya Çık". Ancak `fm dialog 2` uyguluyor; Norwich'te aynısı tekrarlandı.
3. **Maç sonu "Puanlarımız" satırı oyuncu puanlarını yanlış basıyor** — 6 Şub 2027, `fm match --play --no-talk` rc=0: "Maçın oyuncusu: Ben House (8,0)"; `fm player "Ben House" --matches` Exeter satırı da 8,00; ama aynı çıktının "Puanlarımız" satırı "Ben House 7,2" yazıyor ve listeyi 7,8'lik Okoro yönetiyor.
