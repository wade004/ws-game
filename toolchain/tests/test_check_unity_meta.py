"""``toolchain/check_unity_meta.py``（Unity ``.meta`` 完整性门禁）的单元测试。

背景：见 ``check_unity_meta.py`` 模块 docstring——本会话内先后两次出现新增 ``.cs`` 漏提交
``.meta``（1.45.0 发版前 ``games/_template/`` 6 个文件、随后 ``Runtime/Diagnostics/`` 2 个
文件），根因是执行 agent 一律不跑 Unity、纯 .NET/Python 门禁查不出这类问题。本文件在**独立的
临时 git 仓库**里构造最小 Unity 工程布局，覆盖判定规则的每一条分支，不依赖、不触碰真实仓库
状态（除最后一个"真实仓库现状回归"用例，见该用例注释）。

运行：``python -m pytest toolchain/tests/test_check_unity_meta.py -q`` 或作为套件一部分
``python -m pytest toolchain/tests -q``。跨平台（纯 git + Python 标准库，不依赖 PowerShell/
Unity）。
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

import check_unity_meta  # noqa: E402

REPO_ROOT = TOOLCHAIN_DIR.parent


def _git(repo: Path, *args: str) -> subprocess.CompletedProcess:
    result = subprocess.run(
        ["git", *args], cwd=str(repo), capture_output=True, text=True, encoding="utf-8", errors="replace"
    )
    assert result.returncode == 0, f"git {args} 失败：{result.stderr}"
    return result


def _init_repo(tmp_path: Path) -> Path:
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "-q")
    _git(repo, "config", "user.email", "test@example.com")
    _git(repo, "config", "user.name", "Test")
    return repo


def _write(repo: Path, rel_path: str, content: str = "// test\n") -> None:
    p = repo / rel_path
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(content, encoding="utf-8")


def _stage(repo: Path) -> None:
    _git(repo, "add", "-A")


def _minimal_unity_project(repo: Path) -> None:
    """搭一个最小但合法的 Unity 工程骨架：Assets/ 下一对齐全的文件+meta，Packages/ 下一个自带
    package.json 的本地包，同样齐全。后续测试在此基础上各自追加要测的场景，不需要每个用例重复
    这些样板文件。"""
    _write(repo, "adapters/unity/Assets/Existing.cs")
    _write(repo, "adapters/unity/Assets/Existing.cs.meta", "guid: aaaa\n")
    _write(repo, "adapters/unity/Packages/com.sample.pkg/package.json", json.dumps({"name": "com.sample.pkg"}))
    _write(repo, "adapters/unity/Packages/com.sample.pkg/package.json.meta", "guid: bbbb\n")
    _write(repo, "adapters/unity/Packages/manifest.json", json.dumps({"dependencies": {}}))
    _write(repo, "adapters/unity/Packages/packages-lock.json", json.dumps({"dependencies": {}}))


def test_baseline_layout_has_no_issues(tmp_path: Path) -> None:
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _stage(repo)

    missing, orphans, roots = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == []
    assert orphans == []
    assert "adapters/unity/Assets" in roots
    assert "adapters/unity/Packages/com.sample.pkg" in roots


def test_missing_meta_is_detected(tmp_path: Path) -> None:
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _write(repo, "adapters/unity/Assets/NewFeature.cs")  # 故意不写对应 .meta
    _stage(repo)

    missing, orphans, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == ["adapters/unity/Assets/NewFeature.cs"]
    assert orphans == []


def test_orphan_meta_is_detected(tmp_path: Path) -> None:
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _write(repo, "adapters/unity/Assets/Deleted.cs.meta", "guid: cccc\n")  # 对应源文件不存在
    _stage(repo)

    missing, orphans, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == []
    assert orphans == ["adapters/unity/Assets/Deleted.cs.meta"]


def test_directory_level_meta_missing_is_detected(tmp_path: Path) -> None:
    """还原真实踩坑形态（2026-09-20）：Unity 不止为文件生成 .meta，也为它导入范围内的每一层
    目录生成——``Runtime/Diagnostics/`` 下两个 .cs 各自补了 .meta 后，Unity 真实导入还额外
    生成了目录级的 ``Runtime/Diagnostics.meta``，第一版实现（只检查文件级）对此完全没有检测
    能力。本用例构造同样的形状：子目录下两个文件各自都有 .meta，但子目录自身没有 .meta。"""
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _write(repo, "adapters/unity/Assets/Diagnostics/A.cs")
    _write(repo, "adapters/unity/Assets/Diagnostics/A.cs.meta", "guid: 1111\n")
    _write(repo, "adapters/unity/Assets/Diagnostics/B.cs")
    _write(repo, "adapters/unity/Assets/Diagnostics/B.cs.meta", "guid: 2222\n")
    _stage(repo)

    missing, orphans, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == ["adapters/unity/Assets/Diagnostics"]
    assert orphans == []

    # 补上目录级 meta 后应变干净（对应真实修复：Unity 真实导入补齐 Runtime/Diagnostics.meta）。
    _write(repo, "adapters/unity/Assets/Diagnostics.meta", "guid: 3333\n")
    _stage(repo)
    missing2, orphans2, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")
    assert missing2 == []
    assert orphans2 == []


def test_nested_directories_each_require_their_own_meta(tmp_path: Path) -> None:
    """多层嵌套目录：每一层都要求各自的 .meta，不是只查最深或最浅一层。"""
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _write(repo, "adapters/unity/Assets/A/B/C.cs")
    _write(repo, "adapters/unity/Assets/A/B/C.cs.meta", "guid: 4444\n")
    _write(repo, "adapters/unity/Assets/A.meta", "guid: 5555\n")
    # 故意不写 adapters/unity/Assets/A/B.meta
    _stage(repo)

    missing, orphans, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == ["adapters/unity/Assets/A/B"]
    assert orphans == []


def test_gitignored_directory_meta_is_not_orphan(tmp_path: Path) -> None:
    """对应 adapters/unity/Packages/.../Runtime/Plugins/Core.meta 的既有约定：目录整体被
    .gitignore 排除（构建期产物，不提交），但目录自身的 .meta 为保留稳定 GUID 仍然提交——
    这种情况不应被判为孤儿。"""
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _write(repo, ".gitignore", "adapters/unity/Assets/BuildOutput/\n")
    _write(repo, "adapters/unity/Assets/BuildOutput.meta", "guid: dddd\n")
    _stage(repo)

    missing, orphans, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == []
    assert orphans == []


def test_packages_root_manifest_files_are_out_of_scope(tmp_path: Path) -> None:
    """manifest.json/packages-lock.json 直接摆在 Packages/ 根下（不在任何包目录里），本就不是
    "被导入的资源"，Unity 不会为它们生成 .meta；_minimal_unity_project 已经落了这两个文件且不带
    .meta，本用例只是显式断言它们不会被判定为缺失（防止未来改动误把 Packages/ 自身也当成导入根）。"""
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _stage(repo)

    missing, _, roots = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert "adapters/unity/Packages/manifest.json" not in missing
    assert "adapters/unity/Packages/packages-lock.json" not in missing
    assert "adapters/unity/Packages" not in roots


def test_files_outside_assets_and_packages_are_out_of_scope(tmp_path: Path) -> None:
    """对应 adapters/unity/DiagnosticsForwarding/ 这类普通 dotnet 工程：不在 Assets/ 或
    Packages/ 下，即使没有 .meta 也不应被本检查判定为缺失。"""
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _write(repo, "adapters/unity/PlainDotnetTool/Program.cs")
    _write(repo, "adapters/unity/PlainDotnetTool/PlainDotnetTool.csproj", "<Project />")
    _stage(repo)

    missing, orphans, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == []
    assert orphans == []


def test_embedded_local_package_via_manifest_file_dependency_is_discovered(tmp_path: Path) -> None:
    """对应仓库真实布局：games/_template、adapters/conformance 通过 Packages/manifest.json 里
    "file:../../../<路径>" 声明为内嵌本地包，物理位置在 Packages/ 之外，仍然属于 Unity 导入范围。"""
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    (repo / "adapters/unity/Packages/manifest.json").write_text(
        json.dumps({"dependencies": {"com.sample.embedded": "file:../../../embedded_pkg"}}),
        encoding="utf-8",
    )
    _write(repo, "embedded_pkg/package.json", json.dumps({"name": "com.sample.embedded"}))
    _write(repo, "embedded_pkg/package.json.meta", "guid: eeee\n")
    _write(repo, "embedded_pkg/Runtime/Thing.cs")  # 故意不写对应 .meta，也不写 Runtime 目录的 meta
    _stage(repo)

    missing, orphans, roots = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert "embedded_pkg" in roots
    # 文件级（Thing.cs.meta）与目录级（Runtime.meta，Thing.cs 所在目录）两处都应被检出。
    assert missing == sorted(["embedded_pkg/Runtime", "embedded_pkg/Runtime/Thing.cs"])
    assert orphans == []


def test_hidden_and_tilde_suffixed_files_do_not_require_meta(tmp_path: Path) -> None:
    """Unity 自身的导入忽略规则：隐藏文件（名字以 . 开头）、以 ~ 结尾的文件不会被导入，天然没有
    .meta，不应误判为缺失。"""
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _write(repo, "adapters/unity/Assets/.hidden_marker")
    _write(repo, "adapters/unity/Assets/Backup.cs~")
    _stage(repo)

    missing, orphans, _ = check_unity_meta.find_meta_issues(repo, "adapters/unity")

    assert missing == []
    assert orphans == []


def test_cli_exit_code_reflects_result(tmp_path: Path) -> None:
    repo = _init_repo(tmp_path)
    _minimal_unity_project(repo)
    _stage(repo)
    ok = check_unity_meta.main(["--repo-root", str(repo), "--unity-project", "adapters/unity"])
    assert ok == 0

    _write(repo, "adapters/unity/Assets/NewFeature.cs")
    _stage(repo)
    failed = check_unity_meta.main(["--repo-root", str(repo), "--unity-project", "adapters/unity"])
    assert failed == 1


@pytest.mark.skipif(not (REPO_ROOT / ".git").exists(), reason="需要在真实 git 仓库/工作树里跑")
def test_real_repo_known_state() -> None:
    """真实仓库现状回归：记录本任务（Unity .meta 门禁落地时）核实到的真实缺口，以及它们如何被
    修复。这条测试是有意为之的"如实反映现状"，不是"放宽标准"：现状退化（又出现新的真实缺口）
    时本用例会失败，提醒同步更新下面的 KNOWN_MISSING 并在上面追加一条历史记录——不要不看内容
    就删掉或放宽这条测试。

    历史记录（不要删，后续若变化按下面注明的时间点续写）：
    - 2026-09-20 首次落地时：``Runtime/Diagnostics/`` 下两个新 .cs 漏提交 .meta。当天由真实
      Unity 导入补齐（提交 90691cc3），一并生成了目录级的 ``Runtime/Diagnostics.meta``——
      促成本文件新增"目录级 meta 也要检查"的能力（见 ``find_meta_issues``/``_ancestor_dirs``
      判断记录）。
    - 补齐目录级检查后当天又发现第三处：``adapters/unity/Assets/Resources`` 目录下有真实
      提交的内容（``Resources/GameFoundation/...`` 下的占位模型/动画），按规则需要
      ``Resources.meta``，但 ``.gitignore`` 把该文件整体忽略（注释称"目录此前不存在，此次由
      PlayMode 性能测试自动创建"——该前提早已过时：子目录 `GameFoundation.meta` 都已跟踪，
      唯独父目录 `Resources.meta` 被忽略，规则本身与仓库现状自相矛盾）。修复方式是**撤销那条
      过期的忽略规则**（`PerformanceTestRun*` 那条运行时文件忽略保留不动），磁盘上早已存在的
      `Resources.meta`（2026-09-06 由 Unity 生成）随之正常纳入跟踪（提交 29d7e5bd）——不是
      手写 meta，也不是放宽本检查。

    三处缺口共同点：全部属于"纯 .NET/Python 门禁完全看不见、只有对照 Unity 实际导入范围才能
    发现"的一类问题（文件级漏提交、目录级漏提交、规则注释与仓库现状脱节导致的该忽略却未忽略/
    该跟踪却被忽略）——本检查落地当次即照单验出这三处真实存在但极易被人眼审查漏过的不一致，
    是这个检查存在价值的直接证明。"""
    missing, orphans, _ = check_unity_meta.find_meta_issues(REPO_ROOT, "adapters/unity")
    assert missing == []
    assert orphans == []
