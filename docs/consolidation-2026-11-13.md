# Megjelenítés és hang: architekturális konszolidáció, 2026-11-13

## Felelősségek

- **Rules:** verziózott, validált szerkesztési adatok; változtathatatlan
  világprofil. Nem ismer OpenAL-t, OpenGL-t vagy editorvezérlőt.
- **Simulation:** gépállapot → szereplőesemény leképezés és teljes tickenként
  közzétett, eszközfüggetlen pozíció-/eseménypillanatkép. Az animáció és a hang
  közös állapotleképezést használ; nincs második munkagép-állapotgép.
- **Editor.Core:** egy dokumentum és a meglévő tranzakció/Undo/Redo-rendszer.
- **Megjelenítési réteg:** közös CPU-animációmintavétel és térhangkeverő.
  A procedurális járműanimáció a meglévő rendererben marad.
- **Natív adapter:** egy `OpenAlAudioOutput` tulajdonos kezeli a környezeti és
  térbeli hangok kontextusát, erőforrásait, szálhoz kötését és takarítását.
  Külön példányok továbbra is izoláltak; nincs párhuzamos kontextuskezelési
  implementáció. Külön hangkeverők indokoltak: a környezeti hangerő/időzítés és
  a térbeli forrásprioritás eltérő feladat.

## Megszüntetett ismétlődések és költségek

1. `AudioContent`: közös gyökérfeloldás, relatív útvonalvalidálás, PCM WAV
   olvasás, opcionális fájlhibák kezelése és mono átalakítás. Az editor
   validálása, előhallgatása és az éles játék ezt használja. A korábbi editor
   az üres környezeti változót másképp kezelte, mint a játék; ez is egységes.
2. A térhangkeverő közvetlenül a Simulation publikált listáját olvassa.
   Megszűnt a párhuzamos forrásrekord és a képkockánkénti teljes listamásolat.
3. A forráskiválasztás előre foglalt, legfeljebb 32 elemű tömböt használ,
   O(N × K) munkával és O(K) saját munkamemóriával. Nem rendez teljes
   járműlistát, nem hoz létre eszközforrást képkockánként. A hangpufferek
   betöltéskor készülnek; a prioritásváltás megtartja a meglévő hang azonosságát.
4. Hangzó gép nélküli világban nincs új hangpillanatkép-lista tickenként.
5. Az editor animáció-előnézete egyszer importálja a GLB-t a kliplista és a
   mintavételező számára. A GL-állapotmentés két munkatömbje újrahasználható.
6. A profilvalidálás eseménykulcsai gyorsítótárazott, belső halmazok;
   validálásonként nem épülnek teljes alapértelmezett kötésgyűjtemények.

7. A játékos hangerő- és némításbeállításának egyetlen tulajdonosa a viewport.
   A hangkimenetek frissítéskor kapják az értékeket; a profilcsere nem másol
   vissza másodlagos UI-állapotot. Az alkalmazás előtti médiaellenőrzésnek
   egyetlen belépési pontja van az editor hostban.

## Ellenőrzési szerződések

- Félkész kooperatív tick nem cserélheti le a publikált géphangpillanatképet.
- A szerkesztés, visszavonás, alkalmazás és checkpoint azonos profilt ad.
- A forwarder anyagátadási animációsebességét a validátor is rögzíti.
- A térhangforrások száma korlátos; szünet és némítás azonnal üríti a készletet.
- A kamera forgása változtatja a térbeli oldalt, a távolság a hangerőt.
- A keverő 1000 bemelegített frissítése 0 menedzselt bájtot foglal.
- Natív UI-bevitel, WAV-előhallgatás, profilból kapcsolt kerékrajzolás,
  OpenAL-forrásazonosság, kontextusváltás és lezárás külön próbát kapott.

A generált eredmények az `artifacts/spatial-actors/` könyvtárba kerülnek.
Ez célzott konszolidáció, nem a teljes, mintegy 90 ezer soros C# kódbázis
ismétlődésmentességének bizonyítása. A nagy HUD, szabálykatalógus-nézet és
logisztikai modul további bontása konkrét felelősséghatárok és regressziós
bizonyíték mellett indokolt. A mért allokáció a CPU-keverőre vonatkozik;
a teljes játékra, az OpenAL-driverre vagy az összes rendererre nem jelent
allokációmentességet vagy FPS-garanciát.
