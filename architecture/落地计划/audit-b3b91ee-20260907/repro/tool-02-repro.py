"""Run the real sfx importer against two distinct legal IDs and distinct WAVs."""
from __future__ import annotations

import argparse
import json
import sys
import tempfile
import wave
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[4]
sys.path.insert(0, str(REPO_ROOT / "toolchain"))

from asset_import import sfx_cmd


def write_wav(path: Path, value: int) -> None:
    with wave.open(str(path), "wb") as out:
        out.setnchannels(1)
        out.setsampwidth(1)
        out.setframerate(8000)
        out.writeframes(bytes([value, value, value]))


def main() -> int:
    log_path = Path(__file__).with_name("tool-02-repro.log")
    with tempfile.TemporaryDirectory(prefix="ws-game-tool02-") as temp:
        root = Path(temp)
        src = root / "src"
        assets = root / "assets"
        data = root / "data"
        src.mkdir()
        first = src / "first.wav"
        second = src / "second.wav"
        write_wav(first, 17)
        write_wav(second, 231)
        outputs = []
        for asset_id, source in (("sfx.fire.hit", first), ("sfx.fire_hit", second)):
            args = argparse.Namespace(
                src=[str(source)], dataset="audit", id=asset_id, layer="sfx",
                priority=0, assets_root=str(assets), data_root=str(data), dry_run=False,
            )
            outputs.append(sfx_cmd.run(args))
        table = data / "audit" / "sfx" / "sfx.def.json"
        rows = json.loads(table.read_text(encoding="utf-8"))["rows"]
        files = sorted((assets / "audit" / "sfx").glob("*.wav"))
        refs = [(row["id"], row["resource_ref"]) for row in rows]
        same_ref = len({ref for _, ref in refs}) == 1
        second_bytes = second.read_bytes()
        shared_is_second = len(files) == 1 and files[0].read_bytes() == second_bytes
        defect = same_ref and shared_is_second and outputs == [0, 0]
        lines = [
            "ids=sfx.fire.hit,sfx.fire_hit",
            f"run_exit_codes={outputs}",
            f"rows={refs}",
            f"audio_file_count={len(files)}",
            f"both_records_share_resource={same_ref}",
            f"shared_file_is_second_source={shared_is_second}",
            f"result={'PASS_FOR_REPRO (distinct IDs collide and second source overwrites first)' if defect else 'NOT_REPRODUCED'}",
        ]
    log_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    print(f"LOG={log_path}")
    return 0 if defect else 1


if __name__ == "__main__":
    raise SystemExit(main())
