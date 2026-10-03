# Rönkszállító

Forrás: a felhasználó által átadott trucks_collection.glb. A teljes gyűjtemény öt járműváltozatot tartalmaz. A kiválasztott rönkszállító a RootNode 127–169 közötti alkatrészeiből áll: Truck_body.001, Truck_Trailler1, Prop2 és a hozzájuk tartozó alváz, ajtók, kerekek, tartályok, lámpák.

A log-truck.glb kizárólag ezt a járművet tartalmazza: 33 rész, 8877 háromszög, hat külön rönk. A rakomány alulról épül fel. A kerekek külön forgathatók. Normalizálás: +X előre, +Y balra, +Z felfelé; 3,3 világegység hossz; talajszint a kerekek alján.

A gyűjtemény kisméretű JPEG színpalettáját a kivágó script lineáris COLOR_0 csúcsszínekké alakítja. A játék ezért az eredeti színalapú módban is megtartja a modell színeit. Ez az import nem általános textúrás/PBR glTF renderelő: a palettás anyagok csúcsszínként jelennek meg, az üvegek sötét, fedő felületek.

Újragenerálás a repo gyökeréből: python tools/inspect_trucks.py D:/Personal/Downloads/trucks_collection.glb
A scripthez numpy, Pillow és matplotlib szükséges. A játék futásához Python nem szükséges.
