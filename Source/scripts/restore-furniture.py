"""Restore the separately downloaded furniture models, keeping Unity GUIDs."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
from zipfile import ZipFile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path, help="LowPolyFurniturePackVol2_v1.0.zip")
    args = parser.parse_args()
    source = Path(__file__).resolve().parents[1]
    expected = json.loads((source / "assets/licenses/FurnitureV2-files.json").read_text(encoding="utf-8"))
    target = source / "NetCafeClient/Assets/CozyLoungeGreybox/CuratedLowPoly/Models/FurnitureV2"

    with ZipFile(args.archive) as archive:
        entries = {}
        for entry in archive.infolist():
            path = PurePosixPath(entry.filename.replace("\\", "/"))
            if path.parent.name == "FBX" and path.name in expected:
                if path.name in entries:
                    parser.error(f"duplicate archive entry: {path.name}")
                entries[path.name] = entry
        models = {}
        for name, digest in expected.items():
            file = target / name
            if name not in entries or not file.with_suffix(".fbx.meta").is_file():
                parser.error(f"missing archive entry or Unity metadata: {name}")
            data = archive.read(entries[name])
            if hashlib.sha256(data).hexdigest() != digest:
                parser.error(f"unexpected archive contents: {name}")
            if file.exists() and file.read_bytes() != data:
                parser.error(f"refusing to overwrite a different model: {file}")
            models[file] = data
    for file, data in models.items():
        if not file.exists():
            file.write_bytes(data)
    print(f"Verified {len(models)} furniture models.")


if __name__ == "__main__":
    main()
