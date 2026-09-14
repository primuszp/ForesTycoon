# ForesTycoon

## Live Preview

![Current state](images/preview/hero_demo.gif)

## Magyar osszefoglalo

A `ForesTycoon` egy kiserleti, csempes alapu jatekmotor-projekt, amely a klasszikus Transport Tycoon jellegu gondolkodast viszi tovabb erdeszeti es erdogazdalkodasi iranyba.

A jelenlegi allapot fokusza:

- 3D izometrikus terepmegjelenites
- node- es tile-alapu terepmodell
- procedurális terepgeneralas
- alloviz, folyok es partvonal-logika
- interaktiv terepszerkesztes
- kamera- es nezetrendszer finomitasa
- determinisztikus erdoszimulacio negy fafajjal (luc, nyir, tolgy, bukk)
- fafajonkent egyedi novekedes, termohely-igeny, arnyektures es faanyag-ertek
- lombkorona-versenges es onritkulas, termeszetes ujulas a magot ado allomanyok korul
- fafajonkent kulon 3D famodell (sziluett, torzs es korona szin)

A projekt meg prototipus fazisban van. A hangsuly most a terepmotoron, a vizmegjelenitesen, a kameraelmenyen es a szerkesztesi workflow-n van.

## English Summary

`ForesTycoon` is an experimental tile-based engine prototype inspired by the Transport Tycoon style of simulation design, but redirected toward forestry and forest management gameplay.

The current prototype focuses on:

- 3D isometric terrain rendering
- node and tile based terrain representation
- procedural terrain generation
- standing water, rivers, and shoreline logic
- interactive terrain editing
- camera and view-control polish
- deterministic forest simulation with four species (spruce, birch, oak, beech)
- per-species growth, site requirements, shade tolerance and timber value
- canopy competition and self-thinning, with regeneration around seeding stands
- a distinct low-poly 3D model per species

The project is still in prototype stage. Right now the emphasis is on terrain technology, water rendering, camera behavior, and the core editing loop.

## Tech Stack

- C#
- .NET 8
- OpenTK 4 cross-platform `GameWindow`
- OpenGL 3.3 core-profile renderer with GLSL, VAO/VBO batching and shader-based ImGui
- ImGui.NET

## Run

Requirements:

- macOS, Windows or Linux with OpenGL 3.3 core support
- .NET 8 or newer SDK

From the repository root:

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj
```

You can also open `ForesTycoon.sln` in Visual Studio.

The test suite is cross-platform:

```sh
dotnet test ForesTycoon.sln
```

To validate native window creation and rendering in CI or on a new machine:

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --smoke-test
```

The smoke test renders 120 frames and exits automatically.

## Controls

- Left-drag in inspect mode: rotate camera
- Right-drag: pan view
- Mouse wheel: zoom
- Left / Right arrow: rotate toward fixed isometric directions
- Up / Down arrow: change tilt angle
- Left click with the raise/lower tool: edit terrain
- Left-drag with the road tools: build or remove a path
- Left click with the planting tool: plant the selected species (spruce, birch, oak or beech)
- Left click with the harvesting tool: cut a stand and add its biomass to the timber stockpile
- Cmd/Ctrl+S: quicksave
- Cmd/Ctrl+L: quickload

Quicksaves are stored under the operating system's local application-data directory in `ForesTycoon/quicksave.json`.

## Project Structure

- [ForesTycoon/App](ForesTycoon/App): window, OpenGL context, frame loop, camera and platform input
- [ForesTycoon/Interaction](ForesTycoon/Interaction): testable user-intent and editing gesture handling
- [ForesTycoon/Simulation](ForesTycoon/Simulation): fixed-step world, forestry, commands, vehicles and effects
- [ForesTycoon/Animation](ForesTycoon/Animation): interpolated animation timelines and playback modes
- [ForesTycoon/Terrain](ForesTycoon/Terrain): terrain data, generation, hydrology, roads and chunk updates
- [ForesTycoon/Rendering](ForesTycoon/Rendering): ordered render pipeline and GPU helpers
- [ForesTycoon/Diagnostics](ForesTycoon/Diagnostics): frame, simulation, allocation and draw-call metrics
- [ForesTycoon/Camera](ForesTycoon/Camera): platform-independent isometric camera state
- [Engine architecture](docs/engine-architecture.md): responsibility boundaries, performance policy and roadmap

## Current Direction

Planned next steps include:

- faster partial terrain and hydrology updates
- improved river bed and water surface rendering
- biome and terrain-type layers
- placeable timber depots, route assignment and delivery contracts
- later transport, roads, and industrial chains

Forest cache and LOD integration check (requires OpenGL):

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --forest-smoke-test
```

This hidden-window test checks cached geometry reuse, detail reduction, and geometry refresh after planting, harvesting and terrain edits.

## Erdő látványminta

A képreferenciához készített, rögzített 16×16 csempés erdőminta külön nézetben indítható:

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --forest-preview
```

Bal egérhúzás: forgatás; görgő: zoom; bal/jobb nyíl: 45° forgatás; fel/le nyíl: kameradöntés; Esc: bezárás. A mintában négy fafaj, összefüggő lombkorona, ritkuló tisztásszegély és enyhe tereplépcső szerepel. A minta nem ír játékmentést.

36 összehasonlítható PNG exportja (4 kamerairány × 3 dőlés × 3 zoom):

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --forest-preview --capture artifacts/forest-preview
```

Az új stilizált koronák, finom kontúrok és talpközeli árnyékok a normál játék erdőrenderelőjében is működnek. A mintaterület egy külön kezdeti állapot; a normál játék kezdőerdő-generátora és mentési formátuma változatlan.

Zoom/forgatás és futó erdőszimuláció terheléses mérése:

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --camera-benchmark
```

## Rönkszállító teherautó

Az új játékok járművei háromtengelyes, forgó kerekű rönkszállítók. A rakomány mennyiségét legfeljebb kilenc látható rönk jelzi; üresen a tartórudazat marad. Az út befagyasztott vezetőfelületét követik, kanyarban negyedíven fordulnak, az első/hátsó tengely környezetéből számolt dőléssel és keresztlejtéssel. Egyszerű gyorsulás, terhelés- és emelkedőfüggő sebesség, kanyar előtti lassítás és végponti fékezés működik, fix szimulációs lépésekben.

Ez útvonalhoz kötött kinematikai modell: még nincs ütközés, forgalomkövetés vagy külön kerékfelfüggesztés. A végponton megállás után a meglévő automatikus irányváltás történik. A korábbi mentések megőrzik az eredeti állandó sebességű szimulációt, hogy a parancsnapló visszajátszásakor a szállítások időzítése ne változzon; az új mentések külön járműfizika-verziót tárolnak.

Grafikus ellenőrzés és három PNG (rakott emelkedő/lejtő, üres síkút), az `artifacts/truck-preview` könyvtárba:

```sh
dotnet run --project ForesTycoon/ForesTycoon.csproj -- --truck-smoke-test
```
