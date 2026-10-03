"""Convert embedded glTF textures to RGBA PNG; preserve geometry and animation.

Usage: python tools/import_scene_glb.py input.glb output.glb
Requires Pillow. Source files are never modified.
"""
import io
import json
import struct
import sys
from pathlib import Path
from PIL import Image


def convert(source, destination):
    blob = Path(source).read_bytes()
    magic, version, total = struct.unpack_from("<III", blob)
    if (magic, version, total) != (0x46546C67, 2, len(blob)):
        raise ValueError("Expected a GLB v2 file")
    size, chunk_type = struct.unpack_from("<II", blob, 12)
    if chunk_type != 0x4E4F534A:
        raise ValueError("Missing JSON chunk")
    document = json.loads(blob[20:20 + size])
    binary = bytearray(blob[28 + size:])
    for image in document.get("images", []):
        view = document["bufferViews"][image["bufferView"]]
        offset = view.get("byteOffset", 0)
        texture = Image.open(io.BytesIO(binary[offset:offset + view["byteLength"]])).convert("RGBA")
        output = io.BytesIO()
        texture.save(output, format="PNG")
        pixels = output.getvalue()
        binary.extend(b"\0" * (-len(binary) % 4))
        image["bufferView"] = len(document["bufferViews"])
        document["bufferViews"].append({"buffer": 0, "byteOffset": len(binary), "byteLength": len(pixels)})
        binary.extend(pixels)
        image["mimeType"] = "image/png"
    binary.extend(b"\0" * (-len(binary) % 4))
    document["buffers"][0]["byteLength"] = len(binary)
    metadata = json.dumps(document, separators=(",", ":")).encode()
    metadata += b" " * (-len(metadata) % 4)
    output = struct.pack("<III", magic, version, 28 + len(metadata) + len(binary))
    output += struct.pack("<II", len(metadata), 0x4E4F534A) + metadata
    output += struct.pack("<II", len(binary), 0x004E4942) + binary
    target = Path(destination)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(output)


if __name__ == "__main__":
    convert(sys.argv[1], sys.argv[2])
