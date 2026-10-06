"""``toolchain/sync_package_content.ps1`` 回归测试（测试覆盖第四批 I-9，2026-10-01）。

该脚本是私服交付通道的"包内容 -> 游戏工程"同步入口，此前没有任何测试。本文件起 PowerShell 子进程
跑真脚本（临时目录里搭一个只含 ``Library/PackageCache/<包>@<版本>/Data~`` 的伪游戏工程），
断言的都是运行后磁盘上可观测的结果：

1. 参数/前置条件校验：缺 ``-UnityProjectPath``、工程不存在、没有 ``Library/PackageCache``、包没解析到、
   ``Data~`` 缺失，均非 0 退出且不写任何文件。
2. 包解析：``-ResolveOnly`` 只打印路径不拷贝；同名包出现多个目录时打印警告并取修改时间最新的。
3. 分流：toolchain 包不做同步；framework-data 包同步 ``data/_framework``、``assets/_placeholder``、
   按 ``resource_layout_map.json`` 扁平化的 sprites/audio/vfx 等目标目录、TextMesh Pro 资源、占位字体。
4. 清单制镜像删除：框架侧删掉的文件在下一次同步被清理（连同 ``.meta``），消费者自己放进同一目录
   （含 Assets/TextMesh Pro 这种公共目录）的文件永不被删；首次运行（无清单）不清理任何东西；
   损坏的清单按空清单处理并警告。
5. 幂等：内容不变时第二次运行全部"跳过"、没有拷贝。

运行：``python -m pytest toolchain/tests/test_sync_package_content.py -q``。依赖 PowerShell 宿主，
非 Windows 或找不到宿主时按 ``_ps_harness.find_powershell`` 的惯例跳过（受 ``WS_GAME_PS_HOST`` 矩阵开关控制）。
"""

from __future__ import annotations

import json
import os
import subprocess
import time
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, find_powershell
from _ps_subprocess_env import clean_powershell_env

SCRIPT = REPO_ROOT / "toolchain" / "sync_package_content.ps1"
LAYOUT_MAP = REPO_ROOT / "toolchain" / "resource_layout_map.json"
FRAMEWORK_DATA = "com.gamefoundation.framework-data"
TOOLCHAIN_PKG = "com.gamefoundation.toolchain"
MANIFEST_NAME = ".gamefoundation-sync-manifest.json"


def _layout_mappings() -> list[dict]:
    return json.loads(LAYOUT_MAP.read_text(encoding="utf-8-sig"))["mappings"]


def _decode(raw: bytes) -> str:
    """脚本用 Write-Host 输出，控制台重定向后的编码是系统 OEM 代码页（中文 Windows 为 GBK），不是 UTF-8；
    先按 UTF-8 严格解，失败再按 OEM 代码页解。"""
    try:
        return raw.decode("utf-8")
    except UnicodeDecodeError:
        import ctypes
        return raw.decode(f"cp{ctypes.windll.kernel32.GetOEMCP()}", errors="replace")


def _run(*args: str, timeout: int = 180) -> subprocess.CompletedProcess[str]:
    exe = find_powershell()
    env = clean_powershell_env(exe)
    if env is None:
        env = dict(os.environ)
    proc = subprocess.run(
        [exe, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(SCRIPT), *args],
        capture_output=True, timeout=timeout, env=env,
    )
    return subprocess.CompletedProcess(proc.args, proc.returncode, _decode(proc.stdout), _decode(proc.stderr))


def _write(path: Path, content: str | bytes = "x") -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(content, bytes):
        path.write_bytes(content)
    else:
        path.write_text(content, encoding="utf-8", newline="\n")
    return path


def _make_package(project: Path, name: str, version: str = "1.0.0") -> Path:
    pkg = project / "Library" / "PackageCache" / f"{name}@{version}"
    pkg.mkdir(parents=True, exist_ok=True)
    return pkg


def _fill_data(pkg: Path) -> dict[str, str]:
    """铺一份最小的 Data~ 内容；返回 {相对包 Data~ 的路径: 内容}。"""
    data = pkg / "Data~"
    files = {
        "data/_framework/tables/a.json": '{"a":1}',
        "data/_framework/tables/b.json": '{"b":2}',
        "assets/_placeholder/sprites/hero.png": "png-bytes-hero",
        "assets/_placeholder/sfx/click.wav": "wav-bytes-click",
        "assets/_placeholder/vfx/spark.json": '{"vfx":1}',
        "assets/_placeholder/fonts/Placeholder.ttf": "ttf-bytes",
        "assets/_placeholder/fonts/readme.txt": "not a font",
        "assets/textmesh_pro_essentials/Resources/TMP Settings.asset": "tmp-settings",
        "assets/textmesh_pro_essentials/Fonts/LiberationSans.ttf": "tmp-font",
    }
    for rel, content in files.items():
        _write(data / rel, content)
    return files


@pytest.fixture
def project(tmp_path: Path) -> Path:
    root = tmp_path / "GameProject"
    root.mkdir()
    return root


@pytest.fixture
def synced_project(project: Path) -> Path:
    """已经装好 framework-data 包并完成一次成功同步的工程。"""
    pkg = _make_package(project, FRAMEWORK_DATA)
    _fill_data(pkg)
    proc = _run("-UnityProjectPath", str(project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    return project


def _dest(project: Path) -> Path:
    return project / "Assets" / "StreamingAssets" / "GameFoundation"


def _managed(target_dir: Path) -> list[str]:
    manifest = json.loads((target_dir / MANIFEST_NAME).read_text(encoding="utf-8-sig"))
    managed = manifest["managed_files"]
    return [managed] if isinstance(managed, str) else list(managed)


def _tree(root: Path) -> set[str]:
    if not root.exists():
        return set()
    return {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file()}


# ----------------------------------------------------------------------------
# 1. 参数与前置条件
# ----------------------------------------------------------------------------

def test_missing_project_argument_fails(tmp_path: Path) -> None:
    proc = _run()
    assert proc.returncode == 1
    assert "-UnityProjectPath 必填" in proc.stdout + proc.stderr


def test_nonexistent_project_fails(tmp_path: Path) -> None:
    proc = _run("-UnityProjectPath", str(tmp_path / "nope"))
    assert proc.returncode == 1
    assert "找不到 -UnityProjectPath" in proc.stdout + proc.stderr


def test_project_without_package_cache_fails(project: Path) -> None:
    proc = _run("-UnityProjectPath", str(project))
    assert proc.returncode == 1
    assert "Library" in proc.stdout and "PackageCache" in proc.stdout
    assert not (project / "Assets").exists()


def test_package_not_resolved_fails(project: Path) -> None:
    _make_package(project, "com.other.package")
    proc = _run("-UnityProjectPath", str(project))
    assert proc.returncode == 1
    assert f"找不到匹配 {FRAMEWORK_DATA}@*" in proc.stdout + proc.stderr
    assert not (project / "Assets").exists()


def test_missing_data_dir_fails_without_writing(project: Path) -> None:
    _make_package(project, FRAMEWORK_DATA)  # 没有 Data~
    proc = _run("-UnityProjectPath", str(project))
    assert proc.returncode == 1
    assert "Data~" in proc.stdout + proc.stderr
    assert not (project / "Assets").exists()


# ----------------------------------------------------------------------------
# 2. 包解析
# ----------------------------------------------------------------------------

def test_resolve_only_prints_path_and_copies_nothing(project: Path) -> None:
    pkg = _make_package(project, FRAMEWORK_DATA, "2.3.4")
    _fill_data(pkg)
    proc = _run("-UnityProjectPath", str(project), "-ResolveOnly")
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert str(pkg) in proc.stdout
    assert "-ResolveOnly" in proc.stdout
    assert not (project / "Assets").exists()


def test_multiple_matches_warn_and_pick_the_newest_directory(project: Path) -> None:
    old = _make_package(project, FRAMEWORK_DATA, "1.0.0")
    new = _make_package(project, FRAMEWORK_DATA, "1.1.0")
    now = time.time()
    os.utime(old, (now - 3600, now - 3600))
    os.utime(new, (now, now))
    proc = _run("-UnityProjectPath", str(project), "-ResolveOnly")
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert "匹配到 2 个目录" in proc.stdout
    resolved_line = [ln for ln in proc.stdout.splitlines() if ln.strip().startswith("已解析")]
    assert resolved_line and str(new) in resolved_line[0]


# ----------------------------------------------------------------------------
# 3. 分流与同步结果
# ----------------------------------------------------------------------------

def test_toolchain_package_resolves_but_does_not_sync(project: Path) -> None:
    pkg = _make_package(project, TOOLCHAIN_PKG)
    _write(pkg / "Tools~" / "x.py", "print(1)")
    proc = _run("-UnityProjectPath", str(project), "-PackageName", TOOLCHAIN_PKG)
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert "不需要同步动作" in proc.stdout
    assert str(pkg) in proc.stdout
    assert not (project / "Assets").exists()


def test_framework_data_sync_produces_expected_tree(synced_project: Path) -> None:
    dest = _dest(synced_project)
    assert _tree(dest / "data" / "_framework") == {
        "tables/a.json", "tables/b.json", MANIFEST_NAME,
    }
    assert (dest / "data" / "_framework" / "tables" / "a.json").read_text(encoding="utf-8") == '{"a":1}'
    # assets/_placeholder 原样整体镜像
    assert "sprites/hero.png" in _tree(dest / "assets" / "_placeholder")
    assert "sfx/click.wav" in _tree(dest / "assets" / "_placeholder")


def test_resource_layout_map_flattens_placeholder_subdirs(synced_project: Path) -> None:
    dest = _dest(synced_project)
    by_source = {m["source"]: m["target"] for m in _layout_mappings()}
    # 数据里铺了 sprites / sfx / vfx 三类：各自落到映射表指定的扁平目标目录。
    assert (dest / by_source["sprites"] / "hero.png").read_text(encoding="utf-8") == "png-bytes-hero"
    assert (dest / by_source["sfx"] / "click.wav").read_text(encoding="utf-8") == "wav-bytes-click"
    assert (dest / by_source["vfx"] / "spark.json").read_text(encoding="utf-8") == '{"vfx":1}'
    assert by_source["sfx"] == "audio", "映射表本身的关键约定（sfx -> audio）"


def test_textmesh_pro_and_placeholder_fonts_go_to_their_exception_targets(synced_project: Path) -> None:
    tmp = synced_project / "Assets" / "TextMesh Pro"
    assert (tmp / "Resources" / "TMP Settings.asset").read_text(encoding="utf-8") == "tmp-settings"
    assert (tmp / "Fonts" / "LiberationSans.ttf").exists()
    fonts = synced_project / "Assets" / "Framework" / "Resources" / "Fonts"
    assert _tree(fonts) == {"Placeholder.ttf"}, "只拷 .otf/.ttf，readme.txt 不进字体目录"
    # 这两处不属于 StreamingAssets
    assert not (_dest(synced_project) / "TextMesh Pro").exists()


def test_custom_dest_dir_is_honored(project: Path, tmp_path: Path) -> None:
    pkg = _make_package(project, FRAMEWORK_DATA)
    _fill_data(pkg)
    custom = tmp_path / "custom_dest"
    proc = _run("-UnityProjectPath", str(project), "-DestDir", str(custom))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert (custom / "data" / "_framework" / "tables" / "a.json").exists()
    assert not (project / "Assets" / "StreamingAssets").exists()


def test_manifest_lists_exactly_the_files_written(synced_project: Path) -> None:
    target = _dest(synced_project) / "data" / "_framework"
    assert sorted(_managed(target)) == ["tables\\a.json", "tables\\b.json"] or sorted(_managed(target)) == [
        "tables/a.json", "tables/b.json",
    ]


# ----------------------------------------------------------------------------
# 4. 清单制镜像删除
# ----------------------------------------------------------------------------

def test_second_run_is_idempotent_and_copies_nothing(synced_project: Path) -> None:
    before = {p: p.stat().st_mtime_ns for p in (_dest(synced_project)).rglob("*") if p.is_file()
              and p.name != MANIFEST_NAME}
    proc = _run("-UnityProjectPath", str(synced_project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert "拷贝 0" in proc.stdout
    assert "拷贝 1" not in proc.stdout and "拷贝 2" not in proc.stdout
    after = {p: p.stat().st_mtime_ns for p in before}
    assert after == before, "内容不变时不得重写任何目标文件"


def test_changed_source_file_is_updated(synced_project: Path) -> None:
    pkg = next((synced_project / "Library" / "PackageCache").glob(FRAMEWORK_DATA + "@*"))
    _write(pkg / "Data~" / "data" / "_framework" / "tables" / "a.json", '{"a":999}')
    proc = _run("-UnityProjectPath", str(synced_project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    got = (_dest(synced_project) / "data" / "_framework" / "tables" / "a.json").read_text(encoding="utf-8")
    assert got == '{"a":999}'


def test_stale_framework_file_is_removed_with_meta_but_consumer_files_survive(synced_project: Path) -> None:
    target = _dest(synced_project) / "data" / "_framework"
    consumer = _write(target / "tables" / "consumer_owned.json", "mine")
    stale_meta = _write(target / "tables" / "b.json.meta", "guid: 1")
    pkg = next((synced_project / "Library" / "PackageCache").glob(FRAMEWORK_DATA + "@*"))
    (pkg / "Data~" / "data" / "_framework" / "tables" / "b.json").unlink()

    proc = _run("-UnityProjectPath", str(synced_project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert not (target / "tables" / "b.json").exists(), "框架侧已删除的文件应被清理"
    assert not stale_meta.exists(), "伴生 .meta 一并清理"
    assert consumer.read_text(encoding="utf-8") == "mine", "从未进过清单的消费者文件永不删除"
    assert (target / "tables" / "a.json").exists()
    assert "清理 stale 1" in proc.stdout


def test_consumer_files_in_shared_textmesh_pro_dir_are_never_deleted(synced_project: Path) -> None:
    tmp = synced_project / "Assets" / "TextMesh Pro"
    mine = _write(tmp / "Fonts" / "MyGameFont SDF.asset", "game-owned")
    pkg = next((synced_project / "Library" / "PackageCache").glob(FRAMEWORK_DATA + "@*"))
    (pkg / "Data~" / "assets" / "textmesh_pro_essentials" / "Fonts" / "LiberationSans.ttf").unlink()
    proc = _run("-UnityProjectPath", str(synced_project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert mine.read_text(encoding="utf-8") == "game-owned"
    assert not (tmp / "Fonts" / "LiberationSans.ttf").exists()


def test_first_run_without_manifest_does_not_clean_preexisting_files(project: Path) -> None:
    pkg = _make_package(project, FRAMEWORK_DATA)
    _fill_data(pkg)
    legacy = _write(_dest(project) / "data" / "_framework" / "legacy_from_old_script.json", "old")
    proc = _run("-UnityProjectPath", str(project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert legacy.exists(), "尚无清单的首次运行保守地不清理任何东西"
    assert "legacy_from_old_script.json" not in _managed(legacy.parent)


def test_corrupt_manifest_is_treated_as_empty_with_warning(synced_project: Path) -> None:
    target = _dest(synced_project) / "data" / "_framework"
    (target / MANIFEST_NAME).write_text("{ this is not json", encoding="utf-8")
    pkg = next((synced_project / "Library" / "PackageCache").glob(FRAMEWORK_DATA + "@*"))
    (pkg / "Data~" / "data" / "_framework" / "tables" / "b.json").unlink()
    proc = _run("-UnityProjectPath", str(synced_project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    assert "解析既有同步清单失败" in proc.stdout
    assert (target / "tables" / "b.json").exists(), "清单损坏时不清理 stale（保守）"
    # 本次运行后清单被重写为合法 JSON，并且不再包含已从源里消失的 b.json
    assert not any(p.endswith("b.json") for p in _managed(target))


# ----------------------------------------------------------------------------
# 6. 默认手感模板数据根（消费方反馈 P2 缺口 3）
# ----------------------------------------------------------------------------

def test_feel_and_feel_templates_data_roots_are_mirrored(project: Path) -> None:
    """不变量：包 Data~/data 下分发的每一棵框架级数据根（_framework、_feel、_feel_templates）都同步进 StreamingAssets。
    复现：此前 _feel_templates 不落地，按 ADR-0142 把它作为额外框架根加载的游戏启动即数据校验失败。"""
    pkg = _make_package(project, FRAMEWORK_DATA)
    _fill_data(pkg)
    _write(pkg / "Data~" / "data" / "_feel" / "feel" / "feel.preset.json", '{"table":"feel.preset"}')
    _write(pkg / "Data~" / "data" / "_feel_templates" / "feel" / "feel.preset.json", '{"table":"feel.preset","tpl":1}')
    proc = _run("-UnityProjectPath", str(project))
    assert proc.returncode == 0, proc.stdout + proc.stderr
    dest = _dest(project)
    for root, expected in (("_feel", '{"table":"feel.preset"}'), ("_feel_templates", '{"table":"feel.preset","tpl":1}')):
        synced = dest / "data" / root / "feel" / "feel.preset.json"
        assert synced.exists(), f"data/{root} 没有同步进 StreamingAssets"
        assert synced.read_text(encoding="utf-8") == expected
