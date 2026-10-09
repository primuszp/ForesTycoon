"""Copies the licensed harvester (processor), forwarder and log stacks (sarangok) and the depot hangar into ForesTycoon/Assets/Licensed (git-ignored).

The purchased models must never be committed (their licence forbids redistribution). Without them the game draws
simple stand-in machines.

Usage (repo root): python tools/install_licensed_machines.py <Logging_Facility_glb/Separate_assets_glb>
"""
import shutil
import sys
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit(__doc__)
source = Path(sys.argv[1])
dest = Path('ForesTycoon/Assets/Licensed')
dest.mkdir(parents=True, exist_ok=True)
# Stacks smallest to largest: the game picks one by the stacked timber volume.
files = [('harvester_vehicle_001.glb', 'harvester.glb'), ('forestry_vehicle_001.glb', 'forwarder.glb'),
         ('logs_012.glb', 'stack-1.glb'), ('logs_002.glb', 'stack-2.glb'), ('logs_001.glb', 'stack-3.glb'),
         ('logs_004.glb', 'stack-4.glb'), ('logs_005.glb', 'stack-5.glb'), ('hangar_001.glb', 'depot.glb')]
for original, name in files:
    shutil.copyfile(source / original, dest / name)
    print(f'{original} -> {dest / name}')
