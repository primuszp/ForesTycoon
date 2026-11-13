# Animációk és hangesemények szerkesztése

Az editor **Játékparaméterek → Animációk és hangok** lapja ugyanazt a
`RuleProject.Presentation` adatot szerkeszti, amelyet a játék megjelenítése használ.
A módosítások visszavonhatók és újra végrehajthatók; projektmentés, tesztvilágra
alkalmazás és játékmentés után is megmaradnak. A 9-es projektverzió előtti
projektek a korábbi modell-, klip- és hangkötéseket kapják alapértékül.

## Animáció

- A GLB útvonala az alkalmazás tartalomkönyvtárához képest relatív.
- A mozgási gyökér a modell csomópontneve; üresen nem rögzítjük a gyökérmozgást.
- A `deer.idle` és `deer.walk` események klipje, lejátszási sebessége és
  engedélyezése külön szerkeszthető. A kikapcsolt állapot alaphelyzetet használ.
- A szöveges klipnév Enterrel rögzíthető; a betöltött modell klipjei listából is
  választhatók. A modell és a gyökér módosítását a külön rögzítőgomb menti.
- A „Klipek és előnézet betöltése” ellenőrzi a modellt és a klipneveket.
  Módosítás után újra kell tölteni az előnézetet. Lejátszás, szünet, időpont és
  nyugalom–járás keverés állítható; ez nem lépteti a tesztvilág szimulációját.
- Az alkalmazás gomb átadja a dokumentumot a tesztvilágnak. A játék következő
  rajzolásakor a világ saját, változtathatatlan profilja érvényesül.

A közös `WildlifeAnimationPlayer` végzi a játék és az editor CPU-pózmintavételét.
A GPU-előnézet kizárólag az Editor projekt része; az Editor.Core és a Simulation
nem kap grafikus vagy audioeszköz-függőséget. Hibás futásidejű modellkötésnél a
renderer megtartja az előző érvényes modellt, és az új revízió hibáját egyszer
naplózza. A teljes modellimport támogatási korlátai továbbra is érvényesek;
morph target animáció és más szereplők új eseményei nem részei ennek a bővítésnek.
Az előnézet kamerája a szállított szarvas méretéhez igazodik.

## Hang

A nyolc esemény: `Wind`, `Rain`, `Spring`, `Summer`, `Autumn`, `Winter`,
`Thunder`, `Deer`. Mindegyikhez engedélyezés, WAV-fájl és 0–1 hangerőszorzó
adható. A mennydörgés és szarvashang minimális késleltetése és véletlen
időtöbblete valós másodpercben szerkeszthető. Ezek a meglévő időjárási és
állatjelenléti feltételeket követik; nem általános szkriptelt eseményrendszer.

A WAV útvonala a `FORESTYCOON_AUDIO_DIR`, illetve ennek hiányában az alkalmazás
`Assets/Licensed/Audio` könyvtárán belül relatív. Enter rögzíti a szöveget.
Üres útvonal a telepített `manifest.json` kötését örökli. Kifejezett fájlkötés
manifest nélkül is használható; a fájlokat külön kell a célgépre telepíteni,
a projekt és a mentés nem csomagolja be a médiát. Az alkalmazás előtt a
kifejezett, engedélyezett WAV-fájlok és az animációk betölthetőségét ellenőrizzük.
A formátumellenőrzés a meglévő PCM WAV-olvasót használja.

Az „Előhallgatás” csak a kiválasztott eseményt indítja, legfeljebb öt másodpercre;
a „Leállítás”, a lejárat és az editor bezárása felszabadítja a hangkimenetet.
A játékban az esemény hangerőszorzója a közös keverési paraméterekkel és a
játékos hangerőbeállításaival együtt érvényesül. Hangkötés-változáskor a
hangcsomag újratöltődik, a játékos hangerő/némítás megmarad. Animációváltozás
nem indítja újra a hangot. Hiányzó opcionális hangcsomag vagy hangeszköz mellett
a szimuláció tovább működik; hibás hangfájlt a betöltő naplóz és kihagy.

## Ellenőrzés

`PresentationBindingTests` ellenőrzi a dokumentum tranzakcióját, Undo/Redo-t,
migrációt, útvonalakat, világok függetlenségét, parancshatárt, checkpointot,
projekt-exportot, közös animációmintavételt és a hangkeverő tényleges viselkedését.

A natív `--presentation-bindings-smoke-test` valódi ImGui-bevitellel módosít
jelölőnégyzetet, klipnevet és WAV-útvonalat, majd OpenAL-előhallgatást,
alkalmazást és mentés–betöltést ellenőriz. A GL-előnézet két időpontban eltérő,
szüneteltetve megismételhető és nem üres képet kell adjon.
A teszt saját halk WAV-ot generál; vásárolt hangcsomagot nem igényel.

## Térbeli géphangok és további szereplők (projektverzió 10)

Ugyanezen a lapon két új panel érhető el:

- **Gépek animációeseményei:** teherautó-, harvester- és forwarder-kerékmozgás,
  harvester-döntés és -ürítés, forwarder-felrakodás és -lerakodás. Mindegyik
  kikapcsolható; a kerékmozgás és a harvester munkamozgásának sebessége is
  állítható. A forwarder rakodási sebessége rögzített: a megfogás és elengedés
  a szimuláció tényleges anyagátadási fázisához kötött.
- **Térbeli géphangok:** kilenc ismétlődő WAV-esemény a három járműtípus
  vezetési és munkaállapotaihoz. Fájlnév, engedélyezés, hangerő,
  referencia-távolság, hallhatósági határ és az egyidejű hangok száma állítható.
  A „Térhangpróba” legfeljebb öt másodpercig szól; a próba távolsága és iránya
  közben is változtatható.

A gépek procedurális animációját tesszük szerkeszthetővé; ehhez nem vezetünk be
párhuzamos GLB-klipvezérlőt vagy második szimulációt. Licencelt modell nélkül
a meglévő helyettesítő modell marad; a statikus helyettesítő daru nem kap új
klipanimációt. A kerék engedélyezése a forgást szabályozza, nem a kerék
láthatóságát. Az állapotok pontos kulcsai az editorban és a szállított
`default.project.json` dokumentumban láthatók.

A géphangok alapértelmezett fájlkötése üres (néma). Saját, jogtisztán használható
WAV-okat kell megadni az audio tartalomkönyvtárában. Nem szállítunk új
kereskedelmi hangcsomagot. A térbeli lejátszás a sztereó fájlt a két csatorna
átlagával mono jellé alakítja, mert a térbeli irányt a hangforrás helye határozza
meg. Az előhallgatás és a játék ugyanazt a betöltőt, keverőt és natív kimenetet
használja. A mentés a kötéseket tárolja, a hangfájlokat nem.

A játékban a hallgató a kép közepének egy terepmagasság-mintával közelített vetülete; a bal/jobb irány
a kamera forgását követi. A távolság méterben értendő. A csillapítás a
referencia-távolságig állandó, utána fordított távolságú; a hallhatósági határ
előtti utolsó referencia-távolságnyi szakaszon nullára fut. Ha a két
távolsághatár közel van, a kifutás csak a referencia-távolságnál kezdődik. A leghangosabb
hallható források jutnak a legfeljebb 32 (alapértéken 16) lejátszóhelyre.
Azonos hangerőnél stabil szereplőazonosító dönt. A prioritássorrend változása
nem indítja újra a már szóló hurkot. Némítás, fókuszvesztés, szünet vagy
világbetöltés alatt a géphangok leállnak; eltűnő szereplő/állapot forrása a
következő publikált pillanatképpel felszabadul. Doppler, akadályok miatti
árnyékolás és visszhang nincs modellezve.

A projekt v8/v9 migrációja megtartja a korábbi szarvas- és környezeti
hangkötéseket, és hozzáadja az új alapértékeket. A fej nélküli Simulation
csak lezárt tickenként publikál hangforrás-pillanatképet; nem nyit audioeszközt.
A hangkeverő és a megjelenítés nem módosítja a gép fizikai állapotát,
anyagkészletét vagy véletlenszám-generátorát.

A térbeli alapokhoz használt forrás: [OpenAL 1.1 specifikáció, 3.3–3.4 és
forrástulajdonságok](https://www.openal.org/documentation/openal-1.1-specification.pdf).
A távolsági csillapítást a közös CPU-keverő számítja a forrásprioritáshoz is;
a natív kimeneten ezért nincs második csillapítás.
