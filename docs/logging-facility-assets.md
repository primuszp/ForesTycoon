# Logging Facility modellcsomag – mit használhatunk fel?

Forrás: megvásárolt csomag, `D:\Personal\Downloads\Logging_Facility_glb` (387 fájl, 408 MB; Blender glTF-export,
beágyazott szerzői/licencadat nélkül). Áttekintés: 2026-10-08. Áttekintő kép a motor saját renderelőjével:
`artifacts/logging-facility/contact-sheet.png` (`--tree-asset-preview <glb>` modellenként).

## Technikai állapot

- Mind a 20 kipróbált modell hibátlanul betölt a meglévő `AnimatedGlbModel`-lel (textúra, skin), a mostani
  low-poly dioráma stílusával egyező, atlasztextúrás megjelenéssel.
- Méter-skála, Y-fel tengely (a betöltő már Z-felre fordít). Teherautó 6,75 m, forwarder 12,1 m, harvester 7,3 m.
- A járművek riggeltek, de animáció nélkül: a csontok nevesítettek, így kódból mozgathatók.
  - `forestry_vehicle` (forwarder): `knee_1..5` daru, `claw.L/R` markoló, `wheel_1..4.L/R`, `trailer` csukló.
  - `harvester_vehicle`: `knee_1..4` daru, `claw.L/R`, `disk.L/R` (vágófej), 4 kerék.
  - `tractor` (darus traktor), `truck` (6 kerék), `truck_trailer` (6 kerék, rakoncás).
- Szinte minden fájlba be van ágyazva ugyanaz a 250 KB-os atlasz (`Textures1`), a fákba és rönkökbe egy 1,6 MB-os
  `tree_1` textúra is: a felhasznált ~25 modellt közös atlaszú csomagba kell összefűzni (a `tools/import_scene_glb.py`
  mintájára), így a játékba kerülő méret néhány MB.

## Mit, hová (a játéklogika igéi szerint)

| Ige / rendszer | Modellek | Felhasználás |
|---|---|---|
| **Termel** – kitermelés | `harvester_vehicle`, `forestry_vehicle` (forwarder), `tractor` | A kitermelési területen dolgozó gépek: a harvester dönt és darabol (daru + vágófej kódból animálva), a forwarder a rönköket az út menti depóba hordja. A mostani „rakodás közben fogyó fák” logikára ráilleszthető. |
| **Termel** – rönkdepó | `logs_001..016`, `log_001..009` | Az út menti depó (`ForestTreeStore.Patch.Depot`) rakásai a térfogat szerint 3–4 méretlépcsőben; egyedi rönkök a pótkocsin. |
| **Szállít** | `truck` + `truck_trailer_004` | Az új rönkszállító: külön vontató és rakoncás pótkocsi (csuklós követés, kerékforgatás csontokkal), rakomány a `log_*` modellekből. Kiváltja a mostani `log-truck.glb`-t. |
| **Szállít** – később | `locomotive`, `gondola_car`, `cargo_car`, `railway_001..004`, `train_platform` | Transport Tycoon-örökség: iparvasút a távoli malmokhoz, nagy tételű szállítás. |
| **Épít** – fűrészüzem | `building_009` (kéményes csarnok), `building_005/004`, `hangar_001`, `loading_belt`, `crusher`, `circular_saw`, `sawdust_001/002`, `wood_group_*`, `stand_wood_001`, `forklift` | A malom bővíthető telephelyként: csarnok + rönktér + szalag + fűrészpor-halom + fűrészáru-rakat. A rakatok mérete a készletet mutatja (a látvány maga a mérleg). |
| **Épít** – erdészház | `cabin_001..005`, `fence_wood_*`, `barrier_*`, `road_cone` | Az erdőbirtok központja (a HUD birtokkártyájának „helye” a világban), sorompó az erdei út elején. |
| **Gondoz** / erdő | `deadwood_009..031`, `stone_*`, `bush_*`, `grass_*`, `reed_*`, `water_lily_*` | Holtfa a pusztuló állományokba (`ForestDeadTree`) és viharkárhoz; kövek, nád és tavirózsa a pangóvizes/vizes élőhelyekhez; cserjék és fű a tisztásokra. |
| Kellékek | `chainsaw`, `axe`, `barrel_*`, `box_*`, `pallet`, `generator`, `liquid_storage_*` | Díszítés a telephelyeken; nem játékmenet-elem. |
| Nem kell | `tree_*`, `pine_tree_*`, `fir_*` | A saját eljárásos fáink jobban illeszkednek a szimulációhoz (kor, egészség, koronatér). Legfeljebb távoli háttérerdőnek. |
| Nem illik | `car_*`, `excavator`, `bulldozer`, `dump_truck`, `wheeldozer`, `building_013/014` lakóépületek | Városi/bányászati elemek; esetleg későbbi útépítés-animációhoz (`bulldozer`). |

## Javasolt sorrend

1. **Rönkszállító csere** (truck + trailer + log): azonnal látható, a meglévő szállítási logikára épül.
2. **Rönkdepók** a kitermelési helyeken a `Depot` térfogata szerint.
3. **Harvester és forwarder** a kitermelési területen, egyszerű kódolt mozgással (daru lengetés, kerékforgás, ingajárat a depóig).
4. **Fűrészüzem-telephely** a mostani `sawmill.glb` helyett, készletet mutató rakatokkal.
5. **Holtfa és vizes élőhely kellékek** a pusztulás és a pangóvíz megjelenítéséhez (a kezelőnézet diagnózisával összhangban).
6. Később: iparvasút.

## Licenc – a repóba kerülés előtt tisztázandó

A GitHub-repó **nyilvános**. A csomagban nincs licencfájl; a legtöbb megvásárolt modellcsomag licence megengedi a
felhasználást egy kiadott játékban, de **a forrásfájlok nyilvános továbbterjesztését nem**. Amíg ez nincs
tisztázva, a modelleket nem tesszük a repóba: a játék a helyi könyvtárból (vagy egy nem verziókezelt
`Assets/Licensed/` mappából) töltheti be őket, hiányuk esetén a mostani modellekre esik vissza.
