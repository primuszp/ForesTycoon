# Nature Sound FX – környezeti hangok

Ellenőrzés: 2026-10-31. Kiinduló commit: `e49e1f142d3c05ca8bf48cca77260c5fecf45352`.

## Formátum és felhasználási jog

A [gyártó termékoldala](https://ithappystudios.com/sound-fx/nature-sound-fx/) 248 hangot, WAV formátumot, 48 kHz mintavételt, 24 bites felbontást, mono és sztereó felvételeket nevez meg. A csomag loopokat és egyszer lejátszható effekteket is tartalmaz. Az erdő, időjárás, madarak, rovarok és állatok kategóriák illenek a játékhoz. A Unity-letöltés csomagolási lehetőség; a C#/OpenTK játékhoz a **Download Pack** nyers WAV-jai szükségesek, Unity nem kell.

A termékhez közvetlenül kapcsolt [Audio License Certificate](https://media.ithappystudios.com/legal/Audio_License.pdf) megengedi a személyes és kereskedelmi játékban történő felhasználást, a vágást, keverést és feldolgozást. Nem kötelező a névfeltüntetés. Tilos az önálló hangfájlok újraeladása, továbbengedélyezése, továbbadása és más hangtárba csomagolása; a hang csak nagyobb alkotás részeként használható. A jogok a gyártónál maradnak.

Az [általános vásárlási feltételek](https://ithappystudios.com/one-time-purchase-licence-agreement/) örökös, nem átruházható jogot és vásárlásonként öt fejlesztői helyet írnak le; a termékoldal is öt helyet jelez. A megrendelés visszaigazolása szerint a letöltés nem jár le. A bizonylatot és az aktuális licencmásolatokat privát helyen őrizd meg. A hangcsomag licence nem rendezi a játék egyéb függőségeinek jogállását: lásd [LICENSING.md](../LICENSING.md).

**Ellenőrzési korlát:** a megvásárolt Download Pack hivatkozását itt 403-as válasz blokkolta. A fenti formátumadatok gyártói adatok; az archívum tényleges fájlneveit, fejléceit és loopvégeit nem sikerült ellenőrizni. A példában szereplő fájlnevek helykitöltők, nem igazolt csomagtartalom.

## Telepítés

1. A rendelési e-mail **Download Pack** gombjával töltsd le a csomagot; bontsd ki a repozitóriumon kívül. A ZIP-et, a Unity-csomagot és a bizonylatot ne másold a forrásfába.
2. Másold a `docs/audio/manifest.example.json` fájlt privát helyre, és írd át az értékeket a csomag tényleges, a kibontási könyvtárhoz viszonyított WAV-útvonalaira. A nem használt bejegyzéseket törölheted. A Wind, Rain és a négy évszak **loopként** alkalmas fájlt kapjon; a Thunder és Deer egyszeri effekt legyen. Szarvashoz szarvashangot válassz, más állatot ne helyettesíts be.
3. Telepítsd a kiválasztott fájlokat Python 3.12+ segítségével, a repozitórium gyökeréből:

```sh
python tools/install-nature-audio.py --source "/private/NatureSoundFX" --mapping "/private/nature-mapping.json"
dotnet restore ForesTycoon.sln
dotnet test ForesTycoon.Tests/ForesTycoon.Tests.csproj --filter FullyQualifiedName~AmbientAudioTests
dotnet run --project ForesTycoon/ForesTycoon.csproj
```

Windows PowerShellben ugyanígy működik, a `/private/...` útvonalak helyett saját teljes útvonalat adj meg. Az alapértelmezett cél a már git-ignorált `ForesTycoon/Assets/Licensed/Audio/`. A build/publish csak a gyökérben lévő kiválasztott WAV-okat és a manifestet másolja a kimenetbe.

Külső privát könyvtárhoz:

```sh
python tools/install-nature-audio.py --source "/private/NatureSoundFX" --mapping "/private/nature-mapping.json" --destination "/private/ForesTycoonAudio"
FORESTYCOON_AUDIO_DIR=/private/ForesTycoonAudio dotnet run --project ForesTycoon/ForesTycoon.csproj
```

PowerShellben: `$env:FORESTYCOON_AUDIO_DIR = 'D:\Private\ForesTycoonAudio'`, majd `dotnet run --project ForesTycoon/ForesTycoon.csproj`. Hangok nélkül a játék némán működik. Telepítés után indítsd újra: nincs futás közbeni újratöltés.

### Natív futtatókörnyezet

A kezelt függőség `OpenTK.Audio.OpenAL` 4.9.4. A célgépen megfelelő architektúrájú OpenAL implementáció is kell: Windows alatt `OpenAL32.dll`, Linuxon `libopenal.so.1`, macOS-en az OpenTK által megtalálható OpenAL framework/könyvtár. Az OpenTK NuGet-csomag önmagában nem telepít minden platformhoz natív hangmeghajtót. Ubuntu/Debian: `sudo apt install libopenal1`. Windowsos kiadásnál az OpenAL Soft megfelelő DLL-jét a játék mellé helyezd, és mellékeld a hozzá tartozó licencet. A kiválasztott natív buildet a célplatformon is teszteld.

## Viselkedés és határok

| Réteg | Szimulációs bemenet | Lejátszás |
| --- | --- | --- |
| Szél | `Environment.WindSpeed`, m/s | Loop; 15 m/s-nál teljes rétegerősség |
| Eső | `Environment.LiquidRainRate`, mm/h | Loop; 12 mm/h-nál teljes rétegerősség; havazáskor nulla |
| Évszak | `Time / ForestYearSeconds` | Négy erdei loop; az évszak utolsó 20%-ában egyenlő teljesítményű átúsztatás |
| Vihar | `Preset == Storm` | Mennydörgés 8–26 valós másodpercenként; erdei háttér halkul |
| Vadállat | Élő `WildlifeSystem.Deer`, csapadék, vihar | Szarvashang 20–65 valós másodpercenként; viharban és teljes esőben nincs új hívás |

Az első változat globális sztereó hangképet ad; a szarvas jelenléte a teljes világra vonatkozik. Még nincs kamera szerinti távolságcsillapítás, mozgáshoz vagy konkrét állati viselkedési eseményhez kötött hang, villámmal szinkronizált dörgés, illetve vízfolyáshang. A madarak és rovarok az évszakos loopok részei. A hangréteg nem módosítja a világot és nem használja a szimuláció véletlenszám-generátorát.

A hangerő 1,5 másodperces exponenciális időállandóval követi a célértéket. Az évszakváltás ugyanígy simított. A maximális összesített célerősség 0,9; ez hagy némi tartalékot a keverésben. Szünetben a környezeti loopok tovább szólnak, új egyszeri események nem indulnak. Fókuszvesztés és némítás azonnal lenémítja a már elindult effekteket is. Visszatéréskor az új események időzítője folytatódik, nincs felhalmozott lejátszási sor. A hangmagasság és az időzítés nem függ a játéksebességtől. A Környezet ablakban fő-, környezeti és vadállat-hangerő állítható; ezek egyelőre csak az aktuális munkamenetre érvényesek.

A dekóder 16/24 bites integer PCM RIFF/WAV-ot fogad, mono/sztereó és PCM WAVE_FORMAT_EXTENSIBLE támogatással. A 24 bites mintákat 16 bitre alakítja az alap OpenAL formátumokhoz, a mintavételt megtartja. MP3, OGG, float WAV és surround még nincs. Fájlonként 32 MiB a korlát; legfeljebb nyolc kiválasztott klip tölthető memóriába. A beolvasás egyszer, az első kész játékkeretben fut, hosszú fájlok rövid indítási megakadást okozhatnak. Nagy hangtárhoz később háttérbetöltés és streamelés kell.

A fájl loop-jelölése nem garantál kattanásmentes véget: hallgasd meg, és szükség esetén hangvágóban készíts átúsztatott loopot. A kód a különböző rétegek között úsztat; a fájl végének illesztését a kiválasztott klip biztosítja.

## Ellenőrzés és kiadás

```sh
dotnet test ForesTycoon.Tests/ForesTycoon.Tests.csproj
# Natív hívások ellenőrzése szintetikus hanggal, grafikus ablak nélkül:
ALSOFT_DRIVERS=null dotnet run --project ForesTycoon/ForesTycoon.csproj -- --audio-smoke-test
# Valódi hangeszközön: hagyd el az ALSOFT_DRIVERS=null beállítást.
git ls-files -- ForesTycoon/Assets/Licensed/Audio
```

Az utolsó parancs kimenetének üresnek kell lennie. A `.gitignore` nem véd a `git add -f` ellen. Nyilvános CI-be ne tölts fel vásárlási linket vagy hangarchívumot. A kész, hangokat tartalmazó játékot saját kiadási folyamatban csomagold; a nyers WAV-okat ne önálló hangtárként vagy nyilvános forráscsomagként add közre. Kézi próba: napsütés → eső → vihar → hó; évszakváltás 256× sebességgel; szünet, fókuszvesztés, némítás és ablakbezárás.

### Ebben a fejlesztési környezetben elvégzett ellenőrzés

- A játék és a tesztprojekt sikeresen fordult .NET SDK 8.0.427-tel.
- A hangkezelő és az architektúra 42 xUnit-tesztesete közvetlen metódusfuttatással hibamentesen lefutott. A környezet folyamatinformációs korlátja miatt a VSTest tesztgazdája hibával leállt; ez nem teljes, szabványos tesztcsomag-futtatás.
- Az `--audio-smoke-test` sikeres volt az OpenAL Soft `null` meghajtójával: nyolc hang, két kontextus, egyszeri effekt, loop, hangerő, némítás és erőforrás-felszabadítás.
- A Python-telepítő 24 bites sztereó tesztfájllal, manifestkészítéssel és hibás útvonal elutasításával ellenőrizve.
- A vásárolt hangokkal történő meghallgatás, illetve a Windows/macOS célgépes próba még szükséges.
