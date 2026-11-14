# Editor: karbantarthatósági teljesítménymérés

2026-11-06. A mérés CPU-s, nem hoz létre ablakot vagy GPU-előnézetet.
A fixture-előállítás és a szabályok fordítási ellenőrzése a mért szakaszon kívül
fut. A program tényleges szimulációs mentésből rögzíti a világ kiindulóállapotát.
A végső eredmény a `maintenance-final.json`; a korábbi, 1024 szabályos feltáró
mérések piszkozatokat vizsgáltak a futtató 256 szabályos korlátja felett, ezért
nem ezek igazolják a támogatott dokumentumok teljesítményét.

## Megismétlés

```powershell
dotnet build ForesTycoon.sln -c Release --artifacts-path artifacts/editor-next/final -warnaserror
dotnet artifacts/editor-next/final/bin/ForesTycoon.Editor/release/ForesTycoon.Editor.dll --editor-maintenance-benchmark artifacts/editor-next/maintenance-final.json
```

A mérést érdemes a tesztek és a natív próbák befejezése után, önállóan futtatni.
A helyi referenciafutás .NET 8.0.33 alatt, Windowson készült. Bemelegítés után
öt mintából a mediánt közöljük. A memóriaadat a hívószál foglalása; a
`Func<object>` által visszaadott értéktípusok dobozolása is beleszámít (a két
24 B-os sorban ez a mérőkeret költsége). A rövid műveletek időadatai csak
nagyságrendi jelzések. A GPU, háttérfeladatok és felületrajzolás költsége nem része
a mért adatoknak, ezért a számok nem jelentenek képkockasebesség-garanciát.

## Dokumentumok

| Szabályok | Pálya | Festett csempék | Kiinduló mentés karakterei | Projekt JSON, bájt |
| ---: | --- | ---: | ---: | ---: |
| 32 | 16×16 | 256 | 58982 | 1122669 |
| 256 | 128×128 | 16384 | 150078 | 3566851 |

A nagy minta a jelenleg engedélyezett maximális szabályszámot használja.
A két másolást igénylő referenciaút ugyanabban a folyamatban, ugyanazon a
munkadokumentumon fut; a szabályszöveg formázóját mindkét út újra felhasználja.
A referencia a korábbi másolatigényt modellezi, nem egy teljes korábbi build
minden működését. A szövegegyezést és a dokumentumazonosító egyezését külön
regressziós tesztek ellenőrzik.

## Eredmények a nagy dokumentumon

| Művelet | Referencia, ms | Új út, ms | Referencia, B | Új út, B |
| --- | ---: | ---: | ---: | ---: |
| Revízió újraszámítása | 87.865 | 45.134 | 54036872 | 32350680 |
| Kijelölt szabály szövegfrissítése | 0.172 | 0.080 | 92855 | 60693 |

A revíziószámítás foglalása 40%-kal, a szövegfrissítésé 35%-kal csökkent.
A teljes dokumentumról továbbra is független másolat készül mentéshez,
alkalmazáshoz és háttérmunkához; ez szándékos költség.

| További művelet | µs/művelet | B/művelet |
| --- | ---: | ---: |
| Document capture | 38274.90 | 21686739 |
| Cached revision | 0.01 | 0 |
| Readonly frame queries (32 rows) | 3.01 | 24 |
| Cached field analysis | 0.01 | 0 |
| Catalog search (unchanged) | 0.01 | 0 |
| No-change history checkpoint | 0.03 | 24 |
| History transaction | 15445.23 | 6769992 |
| Undo/redo pair | 101268.53 | 47314776 |

Az előzménytranzakció és az Undo/Redo még teljes, szerializált állapotokat kezel.
Ezek költsége a dokumentummal nő; a mostani változtatás a redundáns kezdő
pillanatképet és a változatlan állapotok különbségmagyarázatát távolítja el.
További, részleges állapotokra épülő előzménykezelést új mérés és külön
viselkedési követelmény alapján érdemes bevezetni.

A gépi riport generált, Git által figyelmen kívül hagyott fájl az
`artifacts/editor-next` könyvtárban; ez a dokumentum őrzi az összefoglaló mérési
adatokat és a módszert. A mérőparancs opcionális argumentuma más riportútvonalat
is elfogad.
