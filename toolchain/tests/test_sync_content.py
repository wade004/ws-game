"""``toolchain/sync_content.ps1`` 回归测试（ADR-0040 决策 3 落地，对应消费方反馈第 69 条）。

背景：ADR-0040（``architecture/adr/0040-运行期宿主命令行能力契约.md``）拍板新增一个通用、参数化
的内容同步入口——"任意源内容目录 -> 任意目标目录"，与既有两个各自绑定专属流程的同步入口
（``toolchain/sync_package_content.ps1``、``build.ps1 -SyncContent``）并列，不取代、不包装。本文件
覆盖该入口的对外契约：

  1. 参数校验：缺 ``-SourceDir``/``-TargetDir``、``-SourceDir`` 不存在，均以非 0 退出码结束，
     不写入任何文件。
  2. Additive（默认）策略：只新增/更新有变化的文件，保留目标目录里源目录中不存在的文件。
  3. Mirror 策略：以源为准，目标目录中源里没有的文件会被删除。
  4. 干跑（``-DryRun``）：报告将要发生的改动，但不实际创建/写入/删除任何文件。
  5. 幂等性：源内容不变时重复运行，第二次不产生任何新增/更新/删除（"不变"计数覆盖全部文件）。

运行：

```
python -m pytest toolchain/tests/test_sync_content.py -q
```

或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``。Windows-only（依赖
Windows PowerShell/``pwsh`` 执行 ``.ps1``），非 Windows 环境下全部用例自动跳过，惯例同
``test_get_framework_with_samples.py``/``test_get_framework_path_boundary.py``。
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

import pytest

from _ps_subprocess_env import clean_powershell_env

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "toolchain" / "sync_content.ps1"

pytestmark = pytest.mark.skipif(
    sys.platform != "win32", reason="sync_content.ps1 只在 Windows PowerShell 下运行"
)


def _find_powershell_executable() -> str | None:
    for candidate in ("powershell.exe", "powershell", "pwsh.exe", "pwsh"):
        path = shutil.which(candidate)
        if path:
            return path
    return None


POWERSHELL = _find_powershell_executable() if sys.platform == "win32" else None


def _run(*args: str) -> subprocess.CompletedProcess[str]:
    assert POWERSHELL is not None
    return subprocess.run(
        [
            POWERSHELL,
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(SCRIPT_PATH),
            *args,
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=60,
        env=clean_powershell_env(POWERSHELL),
    )


def _write_tree(root: Path, files: dict[str, str]) -> None:
    for relative, content in files.items():
        path = root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")


def _read_tree(root: Path) -> dict[str, str]:
    if not root.exists():
        return {}
    result: dict[str, str] = {}
    for path in root.rglob("*"):
        if path.is_file():
            result[str(path.relative_to(root)).replace("\\", "/")] = path.read_text(encoding="utf-8")
    return result


@pytest.fixture(autouse=True)
def _skip_without_powershell():
    if sys.platform == "win32" and POWERSHELL is None:
        pytest.skip("找不到 powershell.exe 或 pwsh")


def test_missing_source_dir_arg_fails_without_writing(tmp_path: Path) -> None:
    target = tmp_path / "target"
    result = _run("-TargetDir", str(target))
    assert result.returncode != 0
    assert not target.exists()


def test_missing_target_dir_arg_fails(tmp_path: Path) -> None:
    source = tmp_path / "source"
    source.mkdir()
    result = _run("-SourceDir", str(source))
    assert result.returncode != 0


def test_nonexistent_source_dir_fails_without_writing(tmp_path: Path) -> None:
    source = tmp_path / "does_not_exist"
    target = tmp_path / "target"
    result = _run("-SourceDir", str(source), "-TargetDir", str(target))
    assert result.returncode != 0
    assert not target.exists()


def test_additive_default_preserves_target_only_files(tmp_path: Path) -> None:
    source = tmp_path / "source"
    target = tmp_path / "target"
    _write_tree(source, {"a.txt": "a", "sub/b.txt": "b"})
    target.mkdir()
    _write_tree(target, {"target_only.txt": "keep me"})

    result = _run("-SourceDir", str(source), "-TargetDir", str(target))
    assert result.returncode == 0, result.stdout + result.stderr

    tree = _read_tree(target)
    assert tree["a.txt"] == "a"
    assert tree["sub/b.txt"] == "b"
    assert tree["target_only.txt"] == "keep me", "Additive 策略不应删除目标独有文件"


def test_mirror_policy_removes_target_only_files(tmp_path: Path) -> None:
    source = tmp_path / "source"
    target = tmp_path / "target"
    _write_tree(source, {"a.txt": "a"})
    target.mkdir()
    _write_tree(target, {"target_only.txt": "should be removed"})

    result = _run(
        "-SourceDir", str(source), "-TargetDir", str(target), "-OverridePolicy", "Mirror"
    )
    assert result.returncode == 0, result.stdout + result.stderr

    tree = _read_tree(target)
    assert tree == {"a.txt": "a"}, "Mirror 策略应让目标目录内容与源目录一致，删除目标独有文件"


def test_dry_run_reports_without_writing_any_file(tmp_path: Path) -> None:
    source = tmp_path / "source"
    target = tmp_path / "target"
    _write_tree(source, {"a.txt": "a", "sub/b.txt": "b"})

    result = _run("-SourceDir", str(source), "-TargetDir", str(target), "-DryRun")
    assert result.returncode == 0, result.stdout + result.stderr
    assert "干跑" in result.stdout

    # 干跑不应创建目标目录，也不应写入任何文件。
    assert _read_tree(target) == {}


def test_dry_run_mirror_does_not_delete_existing_files(tmp_path: Path) -> None:
    source = tmp_path / "source"
    target = tmp_path / "target"
    _write_tree(source, {"a.txt": "a"})
    target.mkdir()
    _write_tree(target, {"target_only.txt": "must survive dry run"})

    result = _run(
        "-SourceDir", str(source), "-TargetDir", str(target),
        "-OverridePolicy", "Mirror", "-DryRun",
    )
    assert result.returncode == 0, result.stdout + result.stderr

    tree = _read_tree(target)
    assert tree == {"target_only.txt": "must survive dry run"}, "干跑不应实际删除任何文件"


def test_idempotent_rerun_reports_all_unchanged(tmp_path: Path) -> None:
    source = tmp_path / "source"
    target = tmp_path / "target"
    _write_tree(source, {"a.txt": "a", "sub/b.txt": "b"})

    first = _run("-SourceDir", str(source), "-TargetDir", str(target))
    assert first.returncode == 0, first.stdout + first.stderr

    tree_after_first = _read_tree(target)

    second = _run("-SourceDir", str(source), "-TargetDir", str(target))
    assert second.returncode == 0, second.stdout + second.stderr
    assert "新增 0" in second.stdout
    assert "更新 0" in second.stdout
    assert "不变 2" in second.stdout

    assert _read_tree(target) == tree_after_first, "重复运行不应改变目标目录内容（幂等性承诺）"


def test_invalid_override_policy_rejected(tmp_path: Path) -> None:
    # ValidateSet 校验发生在参数绑定阶段，非法值应直接以非 0 退出（PowerShell 参数绑定错误），
    # 不会执行到脚本正文——用空的临时目录即可，不需要引用仓库自身路径。
    source = tmp_path / "source"
    target = tmp_path / "target"
    source.mkdir()
    result = _run(
        "-SourceDir", str(source), "-TargetDir", str(target), "-OverridePolicy", "Bogus"
    )
    assert result.returncode != 0
