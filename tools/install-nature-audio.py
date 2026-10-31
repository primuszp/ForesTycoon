#!/usr/bin/env python3
"""Install selected, privately purchased WAVs without copying the purchased archive into git."""
import argparse
import json
from pathlib import Path
import shutil
import wave

CUES = {"Wind", "Rain", "Spring", "Summer", "Autumn", "Winter", "Thunder", "Deer"}
DEFAULT_DESTINATION = Path(__file__).resolve().parents[1] / "ForesTycoon/Assets/Licensed/Audio"


def install(source, mapping, destination):
    source = source.resolve(strict=True)
    entries = json.loads(mapping.read_text(encoding="utf-8"))
    if not isinstance(entries, dict) or not entries or not set(entries) <= CUES:
        raise ValueError("Mapping must contain supported cue names; see docs/audio/manifest.example.json")
    selected = []
    for cue, relative in entries.items():
        if not isinstance(relative, str):
            raise ValueError(f"{cue}: expected relative WAV path")
        path = (source / relative).resolve(strict=True)
        if not path.is_relative_to(source) or path.suffix.lower() != ".wav":
            raise ValueError(f"{cue}: file must be a WAV inside the source directory")
        if path.stat().st_size > 32 * 1024 * 1024:
            raise ValueError(f"{cue}: shorten the clip to at most 32 MiB")
        with wave.open(str(path), "rb") as clip:
            if clip.getnchannels() not in (1, 2) or clip.getsampwidth() not in (2, 3):
                raise ValueError(f"{cue}: use mono/stereo, 16/24-bit PCM WAV")
            if not 8000 <= clip.getframerate() <= 192000 or clip.getnframes() == 0:
                raise ValueError(f"{cue}: unsupported sample rate or empty clip")
            expected = clip.getnframes() * clip.getnchannels() * clip.getsampwidth()
            if len(clip.readframes(clip.getnframes())) != expected:
                raise ValueError(f"{cue}: truncated PCM data")
        selected.append((cue, path))
    # Validate every selection before writing anything. Destination must remain private.
    destination.mkdir(parents=True, exist_ok=True)
    manifest = {}
    for cue, path in selected:
        target = destination / f"{cue}.wav"
        if target.resolve() != path:
            shutil.copyfile(path, target)
        manifest[cue] = target.name
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(f"Installed {len(manifest)} cues in {destination}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True, help="Privately extracted Download Pack")
    parser.add_argument("--mapping", type=Path, required=True, help="Private JSON mapping cue -> actual WAV path")
    parser.add_argument("--destination", type=Path, default=DEFAULT_DESTINATION)
    args = parser.parse_args()
    try:
        install(args.source, args.mapping, args.destination)
    except (OSError, ValueError, wave.Error) as error:
        parser.exit(1, f"Audio installation failed: {error}\n")


if __name__ == "__main__":
    main()
