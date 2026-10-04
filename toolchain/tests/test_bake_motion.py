"""``import_assets.py bake-motion``（手感设计/02 第 4 节，ADR-0147）：剪辑根位移采样 -> ``skill.motion_curve`` 行。

复现：一条已知形状的位移采样（先快后慢）烘焙出的断点表，在每个采样时刻的求值与归一化采样一致；
不变量：端点恒为 (0,0)/(1,1)；x 严格递增；y 只增不减且落在 [0,1]；断点数不超过上限；行里记来源剪辑与总位移；重复烘焙同一输入逐字节一致。
"""

from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "toolchain"))

from asset_import import cli, motion_cmd  # noqa: E402
from asset_import.common import AssetImportError  # noqa: E402


def _ease_out_samples(n: int = 41, duration_ms: float = 400.0, distance: float = 3.0) -> list[dict]:
    """先快后慢：d(t) = D * (1 - (1 - t/T)^2)。"""
    return [{"t_ms": duration_ms * i / (n - 1), "distance": distance * (1.0 - (1.0 - i / (n - 1)) ** 2)} for i in range(n)]


def _eval(points: list[dict], x: float) -> float:
    for a, b in zip(points, points[1:]):
        if a["x"] <= x <= b["x"]:
            return a["y"] + (b["y"] - a["y"]) * (x - a["x"]) / (b["x"] - a["x"])
    return points[-1]["y"]


def test_bake_matches_the_normalised_samples_and_keeps_the_invariants():
    samples = _ease_out_samples()
    points, total = motion_cmd.bake_points(samples, max_points=16)

    assert total == pytest.approx(3.0)
    assert len(points) <= 16
    assert (points[0]["x"], points[0]["y"]) == (0, 0) and (points[-1]["x"], points[-1]["y"]) == (1, 1)
    assert all(b["x"] > a["x"] for a, b in zip(points, points[1:]))
    assert all(b["y"] >= a["y"] for a, b in zip(points, points[1:]))
    assert all(0.0 <= p["y"] <= 1.0 for p in points)
    for i in range(0, len(samples), 4):                                      # 期望值由采样的归一化公式算出
        x = samples[i]["t_ms"] / 400.0
        expected = samples[i]["distance"] / 3.0
        assert _eval(points, x) == pytest.approx(expected, abs=0.01)


def test_small_inputs_are_kept_as_is_and_noise_going_backwards_is_flattened():
    samples = [{"t_ms": 0, "distance": 0.0}, {"t_ms": 100, "distance": 1.0}, {"t_ms": 200, "distance": 0.8}, {"t_ms": 300, "distance": 2.0}]
    points, total = motion_cmd.bake_points(samples)
    assert total == pytest.approx(2.0)
    assert [p["y"] for p in points] == [0, 0.5, 0.5, 1]                       # 倒退的 0.8 被抹平成当时的最大值 1.0 -> 0.5


def test_invalid_inputs_fail_loudly():
    with pytest.raises(AssetImportError):
        motion_cmd.bake_points([{"t_ms": 0, "distance": 0.0}])               # 采样太少
    with pytest.raises(AssetImportError):
        motion_cmd.bake_points([{"t_ms": 0, "distance": 0.0}, {"t_ms": 100, "distance": 0.0}])   # 没有位移
    with pytest.raises(AssetImportError):
        motion_cmd.bake_points([{"t_ms": 0, "distance": 0.0}, {"t_ms": 0, "distance": 1.0}])      # 时间跨度为 0
    with pytest.raises(AssetImportError):
        motion_cmd.bake_points([{"t_ms": 0}, {"t_ms": 1}])                    # 形状错误
    with pytest.raises(AssetImportError):
        motion_cmd.build_row("skill.lunge", {"samples": _ease_out_samples()})  # id 前缀不对


def test_cli_writes_a_merged_row_and_is_byte_stable(tmp_path):
    src = tmp_path / "lunge.json"
    src.write_text(json.dumps({"clip": "anim.sample_lunge", "samples": _ease_out_samples()}), encoding="utf-8")
    data_root = tmp_path / "data"
    argv = ["bake-motion", str(src), "--id", "skill.motion_curve.lunge", "--dataset", "_t", "--data-root", str(data_root)]
    assert cli.main(argv) == 0
    table = data_root / "_t" / "skill" / "skill.motion_curve.json"
    first = table.read_bytes()
    document = json.loads(first)
    assert document["table"] == "skill.motion_curve"
    (row,) = document["rows"]
    assert row["id"] == "skill.motion_curve.lunge" and row["source_clip"] == "anim.sample_lunge"
    assert row["source_distance"] == pytest.approx(3.0)

    assert cli.main(argv) == 0                                                  # 同一输入重复烘焙：逐字节一致、不重复成行
    assert table.read_bytes() == first

    src.write_text(json.dumps({"samples": _ease_out_samples(distance=5.0)}), encoding="utf-8")
    other = ["bake-motion", str(src), "--id", "skill.motion_curve.other", "--dataset", "_t", "--data-root", str(data_root)]
    assert cli.main(other) == 0
    ids = [r["id"] for r in json.loads(table.read_text(encoding="utf-8"))["rows"]]
    assert ids == ["skill.motion_curve.lunge", "skill.motion_curve.other"]
