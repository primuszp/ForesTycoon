# Szabályműhely

Az editor témák szerint rendezi a játék 82 katalogizált szabályát. A meglévő erdei
megjelenés megmaradt; a külön szerkesztők közös munkafolyamatba kerültek.

```powershell
dotnet run --project ForesTycoon.Editor
# Meglévő projekt és saját tesztvilág:
dotnet run --project ForesTycoon.Editor -- --project projekt.json --world mentés.json
```

## Első próbálkozás

1. Bal oldalt válaszd az **Erdő és élővilág → Egyedi fanövekedés** szabályt.
2. A **Megértés** megmutatja, kire vonatkozik, mikor fut, miből számol és melyik
   szabály használja az eredményét. A kapcsolódó szabály nevére kattintva át lehet
   lépni hozzá, az **Előző szabály** gombbal visszatérni. A részletes kapcsolatok
   és forráshivatkozások alapból zárva vannak. Az adatkapcsolat nem futási sorrend.
3. Nyisd meg a **Szerkesztés** lépést. A választó a növekedési tényező képletét és
   a kapcsolódó belső algoritmusgráfokat is eléri. A **Szorzó hozzáadása az
   eredményhez** valódi állandó- és szorzáscsomópontot hoz létre, összeköti őket,
   kijelöli a szorzót. Jobb oldalt módosítsd az értékét: például 1,1 = +10%.
   A kimeneti tartomány és a fizikai készletkorlát továbbra is érvényes.
4. A **Mentés / Ctrl+S** külön munkapéldányt ír. Ettől a játék szabálya nem változik.
5. A **Próba** lépésben válassz tesztvilágot vagy saját világmentést és időtávot.
   Az összehasonlító próba az alap- és a szerkesztett teljes projektet ugyanabból
   a checkpointból futtatja. Az időtáv tickben adott: 30 tick egy szimulációs
   másodperc. A táblázat az erdőév kezdetét és végét is mutatja; havi folyamat
   vizsgálatához megfelelően hosszú időszak kell.
6. Az **Alkalmazás** áttekinti az eltéréseket az editor tesztvilágának szabályaihoz
   képest. Az **Alkalmazás a játékban** ellenőrzi, szükség esetén fordítja, majd
   közzéteszi a teljes projektet. A felső sáv külön jelzi az átvétel visszaigazolását.

A négy lépés között szabadon válthatsz. A próba pillanatfelvételt használ, közben
szerkeszthetsz. Az újabb változtatás után a régi eredmény korábbiként jelenik meg.
A próba megszakítható; eredménye és nyomvonala külön fájlokban megmarad.

## Témák és szerkesztés

- Erdő és élővilág
- Időjárás, talaj és víz
- Kitermelés és szállítás
- Utak és járművek
- Gazdaság és fenntartás
- Világ és idő
- Látvány

A kereső minden témában keres. Egy szabály lapján a közös választó nyitja a
képletet, munkaciklust vagy belső algoritmust; nem kell előre külön szerkesztőfület
választani. Gépi munkaciklus vagy hiányzó képletfelülírás megnyitása önmagában nem
hoz létre új szabályt: erre külön létrehozásgomb van.

Képletgráfnál a csomópontok húzhatók, a jobb egérgomb mozgatja a vásznat, a görgő
nagyít. A pontokra kattintva vagy az inspector bemeneteivel szerkesztheted a
kapcsolatokat. Van visszavonás és újra. A belső algoritmusok **Műveletnézet**
alapnézete elágazásokat, ismétléseket, hívásokat, értékadásokat és elnevezett
bemeneteket mutat. Kattintás kijelöl, dupla kattintás belép a részgráfba. A
számok, nevek és szövegek a megfelelő eredeti értékcsomópontot módosítják;
Enter vagy a mező elhagyása rögzíti az értéket. A számítási / összehasonlítási művelet és az egyszerű adattípus
választóval módosítható, az igaz / hamis érték kapcsolóval szerkeszthető. Számnál
tizedespontot használj. Egy utasításblokkban művelet
közvetlenül beszúrható; osztályban vagy struktúrában saját eljárás is létrehozható.
Az új eljárást híváscsomóponttal kell a működő folyamatba bekötni.

A **Teljes szerkezeti gráf** minden eredeti portot és műveletet elérhetővé tesz.
A két nézet ugyanazokat az ID-kat és kapcsolatokat használja; nincs veszteséges
átalakítás vagy párhuzamos forrás. A nem felismert szerkezet részletes műveletként
látható. A fordító ellenőrzi a szintaxist és a típusokat. Nincs forráskódszerkesztő.

Az **Új szabály / készlet / áramlás** a választott témában hoz létre saját elemet.
A saját képletet a játék tényleges folyamatához kötöd; a meglévő célkötést
helyettesíti. A készletek és áramlások a témalistában és a közös keresőben is
megjelennek. A besorolás és a saját jegyzet a projektben tárolódik, az AI is
szerkesztheti. Egy készletből közvetlenül készíthető azt olvasó szorzóképlet.
A fogyasztók és a kapcsolódó áramlások látszanak; használt készletet előbb
le kell választani a folyamatokról és képletekről, utána törölhető.

A **Szabálytár és sablonok** összegyűjti az aktív képleteket és munkaciklusokat.
Az ott vagy az inspectorban készített másolat sablon, nem ütköző aktív kötés.
A sablon egy típusszabályhoz vagy támogatott egyedi objektumhoz kapcsolható:
a célkötést helyettesíti, a többi kötés megmarad. A sablonok neve módosítható,
importálhatók és exportálhatók (`forest-rule-template-library`, 1. verzió).
Meglévő `forest-behaviors` dokumentum és szabályprojekt is importálható sablonként.

Az **Indulási segítség** játékmeneti célból vezet a megfelelő munkatérbe.
A közös kereső szabályokat, saját elemeket és a választott világ objektumait is
keresi. A **További eszközök** a teljes kapcsolattérképet, az összes dinamikai
elemet és minden belső algoritmusgráfot eléri.

## Objektumok és projekt

Az **Objektum kiválasztása** a játék által publikált katalógust böngészi. Látszik,
hogy friss, korábbi vagy hiányzó kapcsolatból származik. A tesztvilág forrása
külön választható: hiányzó kapcsolat esetén nincs automatikus forráscsere.
Egyedi felülírást külön gomb hoz létre; az helyettesíti a típusszintű szabályt.
Járműhöz egyedi munkaciklus is készíthető. A **Vissza a típusszabályhoz** vagy
**Vissza a típus munkaciklusához** eltávolítja a helyi kötést.
Az objektumazonosító a saját világára vonatkozik.

A **Projekt** menüben van a projekt neve és útvonala, a munkapéldány és a közzétett
projekt megnyitása, a világmentés betöltése és az objektumforrás kiválasztása.
Világ betöltése nem írja felül a munkapéldányt; a tesztvilág szabályainak átvétele
külön művelet.

Más algoritmusverzióval készült, aktuális és függő parancs nélküli checkpoint
állapota is beolvasható az editor elkülönített futtatójába. A helyi világ ilyenkor
az editor algoritmusait használja; a teljes összehasonlító próba alapforrása az
eredeti mentés lesz, az eredeti lefordított algoritmusverzióval.

A munkapéldány `<projekt>.draft.json`, algoritmusai változatlan pillanatképek a
`<projekt>.draft.algorithms` mappában. Hiányos képlet is menthető javításhoz;
alkalmazáskor a futtató teljes ellenőrzése szükséges. A közzétett projekt külön
fájl. Más belső algoritmusverzióra a játék mentett állapotból új futtatóval vált;
az újvilág-szabályok nem generálják újra a meglévő világot.

A mentés és publikálás észleli, ha közben másik editor vagy AI átírta a célfájlt;
az új fájlt nem írja felül csendben. Megnyithatod az új változatot vagy külön
projektútvonalra mentheted a munkát. A korábbi fájlok a `.history` mappában
megmaradnak és a Projekt menüből visszanyithatók. A visszanyitás önmagában nem
módosítja a játékot. Bezáráskor a mentetlen munkáról külön `.recovery-...draft.json`
mentés készül, amely ugyanott megnyitható. A projektváltás is megőrzi az előző
munkapéldányt. Az **Új, önálló projekt** külön útvonalon indul.

A szerkesztési besorolások, jegyzetek és sablonok az `authoring` mezőben vannak.
Nem játékszabályok: a runtime tartalmi lenyomata ezeket kihagyja, a szerkesztő
módosításjelzése azonban figyelembe veszi őket.

A backend, a pontos egységek, az objektumkötések, a munkaciklusok, a mentési
szerződés és az AI parancssori műveletei a
[rendszerdinamika útmutatóban](system-dynamics.md) találhatók. A további UI-terv:
[editor újratervezése](editor-ux-redesign.md).

## Ellenőrzés

```powershell
dotnet test ForesTycoon.Tests
dotnet run --project ForesTycoon.Editor -- --smoke-test
```

A grafikus ellenőrzés megnyitja az új munkafolyamat lapjait, ellenőrzi, hogy a
böngészés nem változtatja meg az aktív világot, és tényleges összehasonlító
szimulációval igazolja a visszacsatolási készlet változását. Képernyőképek:
`artifacts/rule-editor/workshop-*.png`.
