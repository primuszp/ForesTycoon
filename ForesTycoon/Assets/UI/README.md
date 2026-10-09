# Erdei ikonok – Transport stílus

## Jelenlegi készlet

A játék a `forest-icons-transport.png` ImageGen-készletet használja mind a 46 ikonhoz.
A B változat finomítása: komolyabb ipari arányok, visszafogottabb formák,
Transport Tycoon jellegű erdészeti gépek. A rönkszállító, a hozzáadás-ikon és a forwarder
rönkjei a gép hossztengelyével párhuzamosan, a fülke mögötti rakfelületen fekszenek.

A `GameIconAtlas` forrásterületeit a PNG alfa-csatornájának összefüggő alakzataiból
mértük, a különálló vezérlővonalakat, napsugarakat és esőcseppeket is beleértve.
Két forráspixel ráhagyás őrzi az élsimítást. Az ikonok saját képarányukat megtartva,
középre igazítva jelennek meg; a szomszédos ikonrészletek nem kerülnek a kivágásba.
Az ikonlap változatlan generált PNG, 1448 × 1086 pixel.

Ellenőrzés: `dotnet run --project ForesTycoon -- --forest-icon-smoke-test`.
Aktuális vizuális katalógus: `artifacts/icon-preview/forest-icons-transport.png`.
Generálási prompt: `transport-imagegen-prompt.txt`.

## Korábbi B változat

A `forest-icons-b.png` a felhasználó által korábban kiválasztott B ikonstílus teljes, 46 elemes készlete,
a beépített ImageGen eszközzel generálva. Átlátszó PNG, 1448 × 1086 pixel; a fordítás az
alkalmazásba ágyazza. A közös `GameIcons.Draw` használja minden gombhoz és ikonhoz.

Paletta: sötét lucfenyő #141A16, moha #5C8045 / #7DA45B, pergamen #EAE4CF,
mézborostyán #E6B35A, kék #94C0E0, fa #AA7642 / #6E4A2C, agyag #E2836B.
Stílus: egyszerű, lekerekített, kétszínű illusztrációk, enyhén izometrikus tárgyakkal.

Az ikonok a `GameIcon` sorrendjét követik, soronként nyolc elemmel. A generált kép
nem pontos geometriai rács: a `GameIconAtlas` külön forrásterületei biztosítják, hogy a
szomszédos ikonok ne látszódjanak. Új kép esetén ezeket is ellenőrizni kell.
A PNG változatlan ImageGen-kimenet; nincs kézi képszerkesztés.

Ellenőrzés: `dotnet run --project ForesTycoon -- --forest-icon-smoke-test`.
A játék megjelenítőjével készített 64 és 28 pixeles katalógus:
`artifacts/icon-preview/forest-icons-b.png`.

## Végső ImageGen prompt

Referencia: az első generált teljes ikonlap, a korábban jóváhagyott B tervből származtatva.

> Correct this game icon atlas. Preserve each object's illustrated style, palette and identity, but fix layout precisely and remove ALL colored speckles, noisy halo pixels and debris around icons. Background must be completely transparent clean alpha. Exactly 8 columns by 6 rows in uniformly sized SQUARE cells on a landscape canvas exactly 4:3. Precisely centered within each cell with 12% empty margin on all sides. No labels or grid lines. Important source row 5 incorrectly has NINE icons; relocate the road-repair icon (road plus wrench) to row 6 column 1. Final row 5 has exactly 8 icons: cloud, rain, storm, thermometer, calendar, timber logs, lightning, speedometer. Final row 6 exactly: road-repair (road plus wrench), gravel road, dirt tracks with tree, dirt tracks with X badge, depot, forestry forwarder, EMPTY, EMPTY. Preserve first four rows, exactly eight evenly spaced icons per row: row1 menu save folder exit new-map pause play fast; row2 fastest magnifier raise lower road road-remove plant harvest; row3 sawmill truck truck-plus fleet forest environment graphics developer; row4 question camera deer spruce birch oak beech sun. No colored noise outside symbols. Simplify tiny surface textures and truck details slightly to match clean flat B style. Every symbol entirely within its own cell. THIS WILL BE READ WITH EXACT 8x6 texture coordinates.
