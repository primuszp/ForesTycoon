# ForesTycoon

`ForesTycoon` egy kísérleti játékprojekt, amely egy `Transport Tycoon` jellegű, csempés gazdasági-szimulációs motor alapjaira épül, de erdészeti és erdőgazdálkodási fókuszú irányba megy tovább.

Jelenlegi állapotban a projekt főleg a 3D terepnézetre és a csempés terepmodellre koncentrál:

- 3D izometrikus nézet OpenGL-lel
- csempés, node-alapú terepmodell
- procedurális terepgenerálás
- partvonalhoz vágott állóvíz
- folyó- és nedvességalapú tereplogika
- egyszerű faelhelyezés a terepviszonyok alapján
- interaktív terepszerkesztés csúcspontokon

## Technológia

- C#
- .NET 8
- Windows Forms
- OpenTK 3.3.3

## Indítás

Elvárások:

- Windows
- .NET 8 SDK

Futtatás a projektmappából:

```powershell
dotnet run --project .\ForesTycoon\ForesTycoon.csproj
```

Vagy Visual Studio alatt a `ForesTycoon.sln` megnyitásával.

## Kezelés

- Bal egér: kamera forgatása
- Jobb egér: nézet eltolása
- Görgő: zoom
- Bal / Jobb nyíl: fix izometrikus nézetek közti forgatás
- Fel / Le nyíl: dőlésszög váltása
- Bal kattintás node-on: terep emelése
- Jobb kattintás node-on: terep süllyesztése

## Jelenlegi fókusz

A projekt még motor-prototípus fázisban van. A hangsúly jelenleg ezeken van:

- jó olvashatóságú 3D csempés terep
- procedurálisan generált hegy-völgy-partvonal rendszer
- állóvíz és folyók vizuális minősége
- kamera és szerkesztési UX

## Következő lépések

- chunkolt mesh és gyorsabb részleges újragenerálás
- pontosabb folyómeder és vízvizualizáció
- tereptípusok és biome-réteg
- erdőnövekedés és fakitermelési játékrendszer
- utak, szállítás, ipari láncok

## Projektstruktúra

- [ForesTycoon/Terrain.cs](ForesTycoon/Terrain.cs): terepgenerálás, víz, fák, renderelt tereplogika
- [ForesTycoon/Viewport.cs](ForesTycoon/Viewport.cs): OpenGL viewport, kamera és input
- [ForesTycoon/Tile.cs](ForesTycoon/Tile.cs): csempe reprezentáció
- [ForesTycoon/Node.cs](ForesTycoon/Node.cs): rácspont reprezentáció
- [ForesTycoon/VertexBuffer.cs](ForesTycoon/VertexBuffer.cs): egyszerű bufferkezelés OpenGL-hez

## Állapot

Ez a repository jelenleg prototípus jellegű. A kód elsődlegesen motor- és renderelési kísérletezésre szolgál, nem kész játék.
