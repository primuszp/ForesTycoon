# Fenyőmodell – felhasználói GLB

A játék az eredeti `pine_tree.glb` változatlan másolatát használja (`Assets/Forest/pine-tree-original.glb`). Az eredeti 12 mesh, összes tűlevél, ág és anyagszín megmarad. A könnyített változatot a felhasználó elutasította; nincs bekapcsolt ritkítás vagy egyszerűsítés.

Az `ImportedPineAsset` egyetlen megosztott CPU-modellt, pózt és GPU-meshkészletet használ az adott terep fenyőihez. Az egyedek külön elhelyezési mátrixot kapnak. A teljes GLB-t nem másoljuk chunkonként vagy faegyedenként. A geometria árnyékpassban is ugyanaz. A GLB lineáris anyagszínei külön shaderkapcsolóval jelennek meg; az állatok és épületek meglévő színezési útja megmarad.

A rajzolás a gyökérponthoz illeszti, Z-felfelé forgatja és az egyed aktuális magasságára egységesen skálázza az eredeti formát. A növekedés így képkockánként látszik, új mesh feltöltése nélkül. Az eredeti arányokat megtartó út még nem jeleníti meg önállóan az átmérő- és koronaráta eltéréseit; az egyedi szimuláció és a faanyag elszámolása változatlanul fizikai méreteket használ. A tönk továbbra is a fizikai faállapot alapján készül. A modellhez kalibrált törzs- és tönkillesztés további feladat.

## Ellenőrzés

```powershell
dotnet test ForesTycoon.sln --no-restore
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --spruce-preview
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --tree-growth-smoke-test
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --forest-smoke-test
dotnet run --project ForesTycoon/ForesTycoon.csproj --no-build -- --wildlife-smoke-test
```

A közeli kép és három eltérő nézet az `artifacts/spruce-redesign` mappában található. A tesztek SHA-256-tal ellenőrzik az eredeti állomány megtartását, valamint a teljes geometria és anyagfaktorok betöltését.

## Teljesítmény

Az eredeti fa több mint 430 ezer háromszögből áll. Bár a GPU-bufferek megosztottak, a geometriai rajzolási költség minden látható fával nő. A 33-as erdőpróba 318 millió beadott indexet/csúcsot számolt képkockánként. Ez látványellenőrzéshez használható, nagy erdőhöz még nem optimalizált. Az eredeti modell jóváhagyása után külön, összehasonlító képekkel ellenőrzött LOD/instancing szükséges; automatikus minőségrontás jelenleg nincs.

Szerző és licenc: [asset attribution](../ForesTycoon/Assets/Forest/ATTRIBUTION.md).
