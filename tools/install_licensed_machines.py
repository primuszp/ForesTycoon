"""Copies the licensed harvester and forwarder into ForesTycoon/Assets/Licensed (git-ignored).

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
for original, name in [('harvester_vehicle_001.glb', 'harvester.glb'), ('forestry_vehicle_001.glb', 'forwarder.glb')]:
    shutil.copyfile(source / original, dest / name)
    print(f'{original} -> {dest / name}')
