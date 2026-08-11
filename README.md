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
- determinisztikus erdoszimulacio harom fafajjal, novekedessel es termeszetes ujulassal

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
- deterministic forest simulation with three species, growth, health and natural regeneration

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
- planting, harvesting and forestry management tools on top of the forest simulation
- later transport, roads, and industrial chains
