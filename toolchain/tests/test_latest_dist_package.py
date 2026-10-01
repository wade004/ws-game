"""``_latest_dist.find_latest_dist_package`` 的单测：真实 dist 产物回归用例不依赖 ``VERSION``。

复现：发布流程先把 ``VERSION`` 写回目标版本再跑门禁，目标版本的包尚不存在，按 ``VERSION`` 找包的
用例会 skip，撞上门禁"skip 上限 0"。不变量：``VERSION`` 指向尚不存在的版本时，仍取 ``dist/`` 里
已存在的最新（按语义化版本，不按字典序）成对包；没有任何成对包才返回 None。
"""

from __future__ import annotations

from pathlib import Path

from _latest_dist import find_latest_dist_package


def _touch(dist: Path, *names: str) -> None:
    dist.mkdir(parents=True, exist_ok=True)
    for n in names:
        (dist / n).write_bytes(b"x")


def test_version_points_to_nonexistent_still_picks_latest_existing(tmp_path: Path) -> None:
    root = tmp_path
    (root / "VERSION").write_text("1.92.0\n", encoding="utf-8")  # 目标版本，包尚不存在
    dist = root / "dist"
    _touch(dist, "ws-game-1.91.0.zip", "ws-game-1.91.0.lock", "ws-game-1.90.1.zip", "ws-game-1.90.1.lock")
    found = find_latest_dist_package(dist)
    assert found is not None
    version, zip_path, lock_path = found
    assert version == "1.91.0"
    assert zip_path == dist / "ws-game-1.91.0.zip"
    assert lock_path == dist / "ws-game-1.91.0.lock"


def test_semver_order_not_lexical_and_ignores_samples_unpaired_and_dryrun(tmp_path: Path) -> None:
    dist = tmp_path / "dist"
    _touch(
        dist,
        "ws-game-1.9.0.zip", "ws-game-1.9.0.lock",
        "ws-game-1.10.0.zip", "ws-game-1.10.0.lock",
        "ws-game-1.11.0-samples.zip",       # 样例包不参与
        "ws-game-1.12.0.zip",               # 缺 lock，不成对
        "ws-game-1.13.0.lock",              # 缺 zip，不成对
    )
    (dist / "1.14.0-dryrun").mkdir()
    version, _, _ = find_latest_dist_package(dist)
    assert version == "1.10.0"


def test_no_pair_returns_none(tmp_path: Path) -> None:
    assert find_latest_dist_package(tmp_path / "missing") is None
    dist = tmp_path / "dist"
    _touch(dist, "ws-game-1.0.0.zip")
    assert find_latest_dist_package(dist) is None
