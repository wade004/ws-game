"""``toolchain/prune_dist.ps1``（dist 瘦身）回归测试（2026-10-02）。

不变量：在一个临时假仓库根里造 ``VERSION`` + ``dist/``（三个版本、每版六种形态 + dryrun 形态 + 未识别条目），

1. 不传 ``-Apply`` 什么都不删（文件系统快照前后完全一致）；
2. 传 ``-Apply`` 后留下/删掉的集合与规则推出的期望一致——期望集合在用例里由规则算出来，不写死清单：
   dryrun 形态一律删；非 dryrun 的 ``<ver>/``、主 zip、samples zip 只留"当前版本 + 紧邻其下的若干正式版本"
   （合计 ``-KeepVersions`` 个）以及高于当前版本的；lock / release-notes 一律留；认不出的不动；
3. ``toolchain/abi_probe_baseline.txt`` 记录的 ABI 基线版本的主 zip 额外保留（只留 zip），否则 ``build.ps1
   -Release`` 的 ``-AbiStrict`` 门禁会因基线缺失而失败；
4. ``dist`` 本身是 junction、或待删条目内部含 junction 时拒绝（退出码 1）且不删任何东西。

运行：``python -m pytest toolchain/tests/test_prune_dist.py -q``（Windows-only，无 PowerShell 宿主时跳过）。
"""

from __future__ import annotations

import os
import re
import subprocess
from pathlib import Path

import pytest

from _ps_harness import find_powershell
from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "toolchain" / "prune_dist.ps1"

_DRY = "-dryrun"


class _Result:
    """子进程结果：stdout/stderr 同时按 UTF-8 与系统 ANSI 代码页各解一遍拼接（Windows PowerShell 5.1 向管道
    输出的是控制台代码页字节，pwsh 可能是 UTF-8，测试不依赖宿主，断言里的中文在其中一份解码里必现）。"""

    def __init__(self, proc: subprocess.CompletedProcess[bytes]) -> None:
        self.returncode = proc.returncode
        raw = proc.stdout + b"\n" + proc.stderr
        self.stdout = raw.decode("utf-8", errors="replace") + "\n" + raw.decode("mbcs", errors="replace")
        self.stderr = ""


def _run_prune(repo_root: Path, *extra: str) -> _Result:
    exe = find_powershell()
    cmd = [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT_PATH),
           "-RepoRoot", str(repo_root), *extra]
    return _Result(subprocess.run(cmd, capture_output=True, timeout=120, env=clean_powershell_env(exe)))


def _vkey(version: str) -> tuple[int, ...]:
    return tuple(int(x) for x in version.split("."))


def _make_version_entries(dist: Path, version: str, suffix: str = "") -> list[str]:
    """造一个版本的六种形态（suffix 为 ``-dryrun`` 时造 dryrun 形态），返回顶层条目名。"""
    v = version + suffix
    d = dist / v
    (d / "packages" / "nested").mkdir(parents=True)
    (d / "packages" / "nested" / "payload.bin").write_bytes(b"payload-" + v.encode())
    (d / "MANIFEST.txt").write_text("manifest " + v, encoding="utf-8")
    files = [f"ws-game-{v}.zip", f"ws-game-{v}-samples.zip", f"ws-game-{v}.lock", f"release-notes-{v}.txt"]
    for name in files:
        (dist / name).write_bytes(b"x" + name.encode())
    return [v] + files


def _snapshot(root: Path) -> set[str]:
    return {str(p.relative_to(root)) for p in root.rglob("*")}


def _build_fake_repo(tmp_path: Path, current: str, versions: list[str], dryrun_versions: list[str],
                     higher: list[str], baseline: str | None = None) -> tuple[Path, Path, dict[str, tuple[str, str, bool]]]:
    """返回 (repo_root, dist, meta)；meta: 顶层条目名 -> (kind, version, dryrun)，kind 为 dir/zip/samples/lock/notes/unknown。"""
    repo = tmp_path / "fake_repo"
    dist = repo / "dist"
    dist.mkdir(parents=True)
    (repo / "VERSION").write_text(current + "\n", encoding="utf-8")
    if baseline is not None:
        (repo / "toolchain").mkdir()
        (repo / "toolchain" / "abi_probe_baseline.txt").write_text(baseline, encoding="utf-8")
    meta: dict[str, tuple[str, str, bool]] = {}
    for ver in versions + higher:
        _make_version_entries(dist, ver)
        meta[ver] = ("dir", ver, False)
        meta[f"ws-game-{ver}.zip"] = ("zip", ver, False)
        meta[f"ws-game-{ver}-samples.zip"] = ("samples", ver, False)
        meta[f"ws-game-{ver}.lock"] = ("lock", ver, False)
        meta[f"release-notes-{ver}.txt"] = ("notes", ver, False)
    for ver in dryrun_versions:
        _make_version_entries(dist, ver, _DRY)
        meta[ver + _DRY] = ("dir", ver, True)
        meta[f"ws-game-{ver}{_DRY}.zip"] = ("zip", ver, True)
        meta[f"ws-game-{ver}{_DRY}-samples.zip"] = ("samples", ver, True)
        meta[f"ws-game-{ver}{_DRY}.lock"] = ("lock", ver, True)
        meta[f"release-notes-{ver}{_DRY}.txt"] = ("notes", ver, True)
    # 认不出形态的条目
    (dist / f"tag-message-{current}.txt").write_text("leftover", encoding="utf-8")
    (dist / "scratch").mkdir()
    (dist / "scratch" / "keep.txt").write_text("keep", encoding="utf-8")
    meta[f"tag-message-{current}.txt"] = ("unknown", "", False)
    meta["scratch"] = ("unknown", "", False)
    return repo, dist, meta


def _expected_deleted(meta: dict[str, tuple[str, str, bool]], current: str, keep_versions: int,
                      baseline: str | None) -> set[str]:
    """按任务规则从条目形态推出应删集合（与脚本实现相互独立地再写一遍规则）。"""
    formal = sorted(
        {ver for (kind, ver, dry) in meta.values() if kind in ("dir", "zip") and not dry},
        key=_vkey,
    )
    lower = [v for v in formal if _vkey(v) < _vkey(current)]
    keep = {current, *lower[::-1][: keep_versions - 1]}
    deleted: set[str] = set()
    for name, (kind, ver, dry) in meta.items():
        if kind == "unknown":
            continue
        if dry:
            deleted.add(name)
        elif kind in ("lock", "notes"):
            continue
        elif _vkey(ver) > _vkey(current):
            continue
        elif ver in keep:
            continue
        elif kind == "zip" and baseline is not None and ver == baseline:
            continue
        else:
            deleted.add(name)
    return deleted


def _entry_bytes(path: Path) -> int:
    if path.is_dir():
        return sum(f.stat().st_size for f in path.rglob("*") if f.is_file())
    return path.stat().st_size


def _top_level(dist: Path) -> set[str]:
    return {p.name for p in dist.iterdir()}


def test_dry_run_deletes_nothing_then_apply_follows_rules(tmp_path: Path) -> None:
    current = "3.0.0"
    repo, dist, meta = _build_fake_repo(
        tmp_path, current, versions=["1.0.0", "2.0.0", "3.0.0"], dryrun_versions=["2.0.0", "3.0.0"], higher=["4.0.0"],
    )
    before = _snapshot(dist)
    all_top = _top_level(dist)
    expected_deleted = _expected_deleted(meta, current, keep_versions=2, baseline=None)
    expected_bytes = sum(_entry_bytes(dist / name) for name in expected_deleted)
    assert expected_deleted, "用例自检：应当有待删条目"
    # 用例自检：期望里 1.0.0 的大产物被删、2.0.0（紧邻其下）与 4.0.0（高于当前）保留
    assert "1.0.0" in expected_deleted and "2.0.0" not in expected_deleted and "4.0.0" not in expected_deleted

    plan = _run_prune(repo)
    assert plan.returncode == 0, plan.stdout + plan.stderr
    assert _snapshot(dist) == before, "不传 -Apply 不得删除/改动任何内容"
    out = plan.stdout
    assert "未识别，已保留" in out and f"tag-message-{current}.txt" in out and "scratch" in out
    for name in expected_deleted:
        assert name in out, f"清单里应列出待删条目 {name}"

    applied = _run_prune(repo, "-Apply")
    assert applied.returncode == 0, applied.stdout + applied.stderr
    assert _top_level(dist) == all_top - expected_deleted
    # 保留的目录内容完整，被删的目录整棵消失
    assert (dist / "2.0.0" / "packages" / "nested" / "payload.bin").is_file()
    assert (dist / "scratch" / "keep.txt").is_file()
    assert not (dist / "1.0.0").exists()
    # 输出里的释放量等于被删条目删除前的实测字节数之和
    m = re.search(r"已删除 (\d+) / (\d+) 项，释放 .*?（(\d+) 字节", applied.stdout)
    assert m, applied.stdout
    assert int(m.group(1)) == len(expected_deleted) == int(m.group(2))
    assert int(m.group(3)) == expected_bytes

    # 幂等：再跑一次没有可删的了
    again = _run_prune(repo, "-Apply")
    assert again.returncode == 0, again.stdout + again.stderr
    assert "已删除 0 / 0 项" in again.stdout or "删除清单：0 项" in again.stdout


def test_keep_versions_parameter_and_abi_baseline_zip_survive(tmp_path: Path) -> None:
    """ABI 基线版本（本例 1.0.0，早于所有保留版本）只留主 zip，不留目录与 samples；另验 -KeepVersions 3 多留一个版本。"""
    current = "3.0.0"
    repo, dist, meta = _build_fake_repo(
        tmp_path, current, versions=["1.0.0", "2.0.0", "3.0.0"], dryrun_versions=[], higher=[], baseline="1.0.0",
    )
    all_top = _top_level(dist)
    expected_deleted = _expected_deleted(meta, current, keep_versions=2, baseline="1.0.0")
    assert "ws-game-1.0.0.zip" not in expected_deleted
    assert "1.0.0" in expected_deleted and "ws-game-1.0.0-samples.zip" in expected_deleted

    result = _run_prune(repo, "-Apply")
    assert result.returncode == 0, result.stdout + result.stderr
    assert _top_level(dist) == all_top - expected_deleted
    assert (dist / "ws-game-1.0.0.zip").is_file()
    assert (dist / "ws-game-1.0.0.lock").is_file() and (dist / "release-notes-1.0.0.txt").is_file()

    # -KeepVersions 3：三个版本的大产物都留，只有（本例没有的）dryrun 形态会被删
    repo3, dist3, meta3 = _build_fake_repo(tmp_path / "k3", current, ["1.0.0", "2.0.0", "3.0.0"], ["3.0.0"], [])
    top3 = _top_level(dist3)
    exp3 = _expected_deleted(meta3, current, keep_versions=3, baseline=None)
    assert not any(n in exp3 for n in ("1.0.0", "ws-game-1.0.0.zip")), "用例自检：KeepVersions=3 时 1.0.0 保留"
    r3 = _run_prune(repo3, "-KeepVersions", "3", "-Apply")
    assert r3.returncode == 0, r3.stdout + r3.stderr
    assert _top_level(dist3) == top3 - exp3


def _mklink_junction(link: Path, target: Path) -> None:
    proc = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)], capture_output=True, text=True)
    if proc.returncode != 0:
        pytest.skip("本机无法创建 junction：" + proc.stdout + proc.stderr)


def test_refuses_reparse_points_and_deletes_nothing(tmp_path: Path) -> None:
    if os.name != "nt":
        pytest.skip("junction 用例只在 Windows 下运行")
    current = "3.0.0"
    # 场景 1：待删目录内部含 junction -> 拒绝，且 junction 指向的目标原样保留
    repo, dist, _ = _build_fake_repo(tmp_path / "inner", current, ["1.0.0", "2.0.0", "3.0.0"], [], [])
    outside = tmp_path / "outside_target"
    outside.mkdir()
    sentinel = outside / "sentinel.txt"
    sentinel.write_text("must-survive", encoding="utf-8")
    link = dist / "1.0.0" / "packages" / "link_to_outside"
    _mklink_junction(link, outside)
    before = _snapshot(dist)
    try:
        result = _run_prune(repo, "-Apply")
        assert result.returncode == 1, result.stdout + result.stderr
        assert "重解析点" in result.stdout + result.stderr
        assert _snapshot(dist) == before, "拒绝时不得删除任何条目"
        assert sentinel.read_text(encoding="utf-8") == "must-survive"
    finally:
        os.rmdir(link)

    # 场景 2：dist 本身是 junction -> 拒绝
    real_repo, real_dist, _ = _build_fake_repo(tmp_path / "real", current, ["1.0.0", "2.0.0", "3.0.0"], [], [])
    fake_repo = tmp_path / "linked_repo"
    fake_repo.mkdir()
    (fake_repo / "VERSION").write_text(current + "\n", encoding="utf-8")
    dist_link = fake_repo / "dist"
    _mklink_junction(dist_link, real_dist)
    real_before = _snapshot(real_dist)
    try:
        result2 = _run_prune(fake_repo, "-Apply")
        assert result2.returncode == 1, result2.stdout + result2.stderr
        assert "重解析点" in result2.stdout + result2.stderr
        assert _snapshot(real_dist) == real_before
    finally:
        os.rmdir(dist_link)
    del real_repo


def test_tag_message_leftover_deleted_only_when_tag_exists(tmp_path: Path) -> None:
    """打标签失败残留的 ``tag-message-<ver>.txt``：标签 v<ver> 已存在 -> 删；标签不存在 -> 按未识别保留。

    期望由规则推出：有标签的版本集合里的残留文件被删，无标签的留下，其余条目不受影响。
    """
    from _git_env import init_temp_repo, run_git

    current = "3.0.0"
    repo, dist, _meta = _build_fake_repo(
        tmp_path, current, versions=["2.0.0", "3.0.0"], dryrun_versions=[], higher=[],
    )
    init_temp_repo(repo)
    run_git(repo, "add", "VERSION")
    run_git(repo, "commit", "-q", "-m", "init")
    tagged = ["2.0.0", current]  # current 的残留文件 _build_fake_repo 已造
    untagged = ["9.9.9"]
    for ver in tagged:
        run_git(repo, "tag", "-a", f"v{ver}", "-m", f"v{ver}")
        (dist / f"tag-message-{ver}.txt").write_text("leftover", encoding="utf-8")
    for ver in untagged:
        (dist / f"tag-message-{ver}.txt").write_text("leftover", encoding="utf-8")

    plan = _run_prune(repo)
    assert plan.returncode == 0, plan.stdout
    for ver in tagged:
        assert re.search(rf"删除\s+tag-message-{re.escape(ver)}\.txt", plan.stdout), "待删清单里应列出残留文件"
    assert (dist / "tag-message-2.0.0.txt").exists(), "不传 -Apply 不得删除"

    applied = _run_prune(repo, "-Apply")
    assert applied.returncode == 0, applied.stdout
    for ver in tagged:
        assert not (dist / f"tag-message-{ver}.txt").exists(), f"标签 v{ver} 存在，残留应被删"
    for ver in untagged:
        assert (dist / f"tag-message-{ver}.txt").exists(), f"标签 v{ver} 不存在，残留应保留"
    assert "未识别，已保留" in applied.stdout and "tag-message-9.9.9.txt" in applied.stdout
