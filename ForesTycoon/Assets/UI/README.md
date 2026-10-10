# Erdei ikonok – Transport stílus

A játékba a `forest-icons-transport.png` van beágyazva: 46 alapikon,
1448 × 1086 pixeles, átlátszó ImageGen-ikonlap. A rönkszállító és a forwarder
rakománya a gép hossztengelyével párhuzamosan, a fülke mögött helyezkedik el.
A 47. művelet, a rönkeltávolítás, a rönkikonra rajzolt törlésjelzést használja.

A [GameIconAtlas](../../UI/GameIconAtlas.cs) mért forrásterületekkel választja ki
az ikonokat; a generált kép nem pontos geometriai rács. Két forráspixel ráhagyás
őrzi az élsimítást. A [GameIcons](../../UI/GameIcons.cs) közös rajzolási felületet
és atlasz nélküli vektoros helyettesítést biztosít.

Ellenőrzés a repó gyökeréből:

```sh
dotnet run --project ForesTycoon -c Release -- --forest-icon-smoke-test
```

A vizuális katalógus generált kimenet az `artifacts/icon-preview/` alatt;
takarítás után újra előállítható. A jelenlegi generálási leírás:
[transport-imagegen-prompt.txt](transport-imagegen-prompt.txt).

A `forest-icons-b.png` korábbi referencia, nincs beágyazva a jelenlegi buildbe.
Új ikonlapnál a kis méretű olvashatóságot és az atlasz kivágásait is ellenőrizni kell.
