"""``toolchain/validate_data.py`` 的 ``--data-root``/``--framework-root`` 相对路径按调用方当前
工作目录解析的回归测试（消费方反馈 E9 根治，2026-09-10，见
architecture/落地计划/消费方反馈-2026-09-10-编辑器.md E9）。

背景：相对路径此前一律按 ``find_repo_root()``（本文件所在目录的上一级）解析，不是按调用方实际
运行时的当前工作目录——"从仓库根目录运行本脚本"这一惯例场景下两者恰好相同，看不出区别；但游戏侧
把 ``toolchain/`` 整个目录复制/引用到自己仓库、又不是从自己仓库根目录调用本脚本时（例如从子目录、
或用绝对路径调用本脚本本身），两种基准会给出不同的解析结果。根治：统一按 ``Path.cwd()`` 解析全部
相对路径（含默认根），不再依赖脚本自身的安装位置。

本文件在临时目录里构造一份"游戏侧"数据目录布局，从与脚本所在目录完全无关的另一个临时工作目录
（``os.chdir`` 切换过去）用**相对路径**调用 ``--skip-dotnet`` 模式的 ``validate_data.py``，断言
能找到并校验到该目录——如果解析基准退回到脚本自身位置，会因为找不到这个路径而报参数错误
（exit code 2）。

运行：``python -m pytest toolchain/tests/test_validate_data_relative_root_cwd.py -q`` 或
``python -m pytest toolchain/tests -q``。
"""

from __future__ import annotations

import importlib.util
import json
import os
import shutil
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]


def _load_validate_data_copy(module_dir: Path):
    module_path = module_dir / "validate_data.py"
    spec = importlib.util.spec_from_file_location("validate_data_copy_under_test_cwd", module_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def chdir_guard():
    """保证测试结束后（即便断言失败）恢复原始工作目录，不污染同一 pytest 会话里的其它用例。"""
    original = Path.cwd()
    try:
        yield
    finally:
        os.chdir(original)


def test_relative_data_root_resolved_against_cwd_not_script_location(tmp_path: Path, chdir_guard) -> None:
    # 脚本拷贝放在一个位置（模拟随包分发的 toolchain/ 目录）。
    toolchain_copy = tmp_path / "somewhere" / "toolchain"
    toolchain_copy.mkdir(parents=True)
    shutil.copy(TOOLCHAIN_DIR / "validate_data.py", toolchain_copy / "validate_data.py")
    shutil.copy(TOOLCHAIN_DIR / "_console.py", toolchain_copy / "_console.py")

    # 数据根放在完全不同的另一棵目录树下（模拟"游戏侧自己的仓库"），与脚本拷贝的父目录没有
    # 任何路径关系——若解析基准退回脚本自身位置（旧行为：本文件所在目录的上一级），相对路径
    # "my_game_data" 在那里找不到对应目录。
    game_repo_root = tmp_path / "elsewhere" / "my_game_repo"
    data_dir = game_repo_root / "my_game_data"
    data_dir.mkdir(parents=True)
    (data_dir / "test.thing.json").write_text(
        json.dumps({"table": "test.thing", "schema_version": 1, "rows": []}),
        encoding="utf-8",
    )

    module = _load_validate_data_copy(toolchain_copy)

    os.chdir(game_repo_root)
    # 用相对路径调用（相对"游戏仓库根"，也就是当前工作目录）。
    exit_code = module.main(["--data-root", "my_game_data", "--skip-dotnet"])

    assert exit_code == 0, "相对路径应按当前工作目录解析，找到 my_game_data 并校验通过"


def test_relative_data_root_not_found_from_unrelated_cwd(tmp_path: Path, chdir_guard) -> None:
    """反例：从与数据目录无关的第三个工作目录调用同一个相对路径，应该报"目录不存在"（退出码 2）
    ——证明解析确实跟随当前工作目录变化，而不是恰好命中了某个固定基准。
    """
    toolchain_copy = tmp_path / "somewhere" / "toolchain"
    toolchain_copy.mkdir(parents=True)
    shutil.copy(TOOLCHAIN_DIR / "validate_data.py", toolchain_copy / "validate_data.py")
    shutil.copy(TOOLCHAIN_DIR / "_console.py", toolchain_copy / "_console.py")

    unrelated_cwd = tmp_path / "unrelated_cwd"
    unrelated_cwd.mkdir()

    module = _load_validate_data_copy(toolchain_copy)

    os.chdir(unrelated_cwd)
    exit_code = module.main(["--data-root", "my_game_data", "--skip-dotnet"])

    assert exit_code == 2


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
