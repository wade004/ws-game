"""``toolchain/version_label.py``（版本标签推导与分支名规范，ADR-0127）的测试。

两类用例（全局约定：测试 = 复现 + 不变量各至少一条）：

- **复现用例**：feature、bugfix、main、release/X.Y.x、游离 HEAD 各一条，期望标签写成字面量（对着 `AGENTS.md` §1b
  用户规定的格式，不是把实现输出抄回来）；另用临时仓库真实跑一遍命令行入口。
- **不变量用例**：任意分支名下标签都以 VERSION 内容开头且其后紧跟 ``_``；不合规范的 feature/bugfix 名必然被判 FAIL，
  合规名必然 PASS；非 feature/bugfix 分支不判定。
"""

from __future__ import annotations

import random
import re
import subprocess
import sys
from pathlib import Path

import pytest

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent
if str(TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(TOOLCHAIN_DIR))

import version_label as vl  # noqa: E402

SCRIPT = TOOLCHAIN_DIR / "version_label.py"


# ---------------------------------------------------------------------------
# 复现用例
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "branch, sha, expected",
    [
        ("feature/gate-timing-autolog_20261001", "", "1.92.0_gate-timing-autolog_20261001"),
        ("bugfix/cold-load-attach-key_20261001", "", "1.92.0_cold-load-attach-key_20261001"),
        ("main", "", "1.92.0_release"),
        ("release/1.90.x", "", "1.92.0_release-1.90.x"),
        (None, "abc1234", "1.92.0_detached-abc1234"),
    ],
)
def test_repro_label_per_branch_kind(branch: str | None, sha: str, expected: str) -> None:
    assert vl.compute_label("1.92.0", branch, sha) == expected


def test_repro_label_follows_version_bump_on_main() -> None:
    # 发布后 VERSION 升级，main 上的标签随之变成新版本号加 _release，不需要改任何手写字段
    assert vl.compute_label("1.93.0", "main") == "1.93.0_release"


def test_repro_nested_slash_in_suffix_becomes_hyphen() -> None:
    assert vl.compute_label("1.92.0", "feature/a/b_20261001") == "1.92.0_a-b_20261001"


def _git(repo: Path, *args: str) -> str:
    return subprocess.run(
        ["git", "-C", str(repo), *args], capture_output=True, text=True, check=True, encoding="utf-8"
    ).stdout.strip()


@pytest.fixture()
def tmp_repo(tmp_path: Path) -> Path:
    repo = tmp_path / "repo"
    repo.mkdir()
    _git(repo, "init", "-q", "-b", "main")
    _git(repo, "config", "user.email", "t@example.invalid")
    _git(repo, "config", "user.name", "t")
    _git(repo, "config", "commit.gpgsign", "false")
    (repo / "VERSION").write_text("1.92.0\n", encoding="utf-8")
    _git(repo, "add", "VERSION")
    _git(repo, "commit", "-q", "-m", "init")
    return repo


def _cli(repo: Path, *extra: str) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, str(SCRIPT), "--repo-root", str(repo), *extra],
        capture_output=True,
        text=True,
        encoding="utf-8",
    )


def test_repro_cli_real_repo_main_feature_bugfix_release_detached(tmp_repo: Path) -> None:
    assert _cli(tmp_repo).stdout.strip() == "1.92.0_release"

    _git(tmp_repo, "checkout", "-q", "-b", "feature/targeted-gate_20261001")
    assert _cli(tmp_repo).stdout.strip() == "1.92.0_targeted-gate_20261001"
    assert _cli(tmp_repo, "--check-branch-name").returncode == 0

    _git(tmp_repo, "checkout", "-q", "-b", "bugfix/cold-load_20261001", "main")
    assert _cli(tmp_repo).stdout.strip() == "1.92.0_cold-load_20261001"

    _git(tmp_repo, "checkout", "-q", "-b", "release/1.90.x", "main")
    assert _cli(tmp_repo).stdout.strip() == "1.92.0_release-1.90.x"
    assert _cli(tmp_repo, "--check-branch-name").returncode == 0  # 维护分支不判定

    sha = _git(tmp_repo, "rev-parse", "--short", "HEAD")
    _git(tmp_repo, "checkout", "-q", "--detach", "HEAD")
    assert _cli(tmp_repo).stdout.strip() == f"1.92.0_detached-{sha}"
    assert _cli(tmp_repo, "--check-branch-name").returncode == 0


def test_repro_cli_noncompliant_feature_branch_fails(tmp_repo: Path) -> None:
    _git(tmp_repo, "checkout", "-q", "-b", "feature/测试")
    label = _cli(tmp_repo)
    assert label.returncode == 0 and label.stdout.strip() == "1.92.0_测试"  # 标签仍照推导
    check = _cli(tmp_repo, "--check-branch-name")
    assert check.returncode == 1
    assert check.stdout.startswith("FAIL 分支名不合规范")


# ---------------------------------------------------------------------------
# 不变量用例
# ---------------------------------------------------------------------------

_ALPHABET = list("abcXYZ019-_/. 测试") + ["feature/", "bugfix/", "release/", "main"]


def _random_branches(seed: int, count: int = 300) -> list[str | None]:
    rng = random.Random(seed)
    out: list[str | None] = [None, "main", "HEAD", "", "feature/", "bugfix/", "release/1.90.x"]
    for _ in range(count):
        out.append("".join(rng.choice(_ALPHABET) for _ in range(rng.randint(1, 8))))
    return out


@pytest.mark.parametrize("version", ["1.92.0", "0.0.1", "10.20.30"])
def test_invariant_label_starts_with_version_then_underscore(version: str) -> None:
    for branch in _random_branches(seed=7):
        label = vl.compute_label(version, branch, "abc1234")
        assert label.startswith(version + "_"), (branch, label)


def test_invariant_noncompliant_feature_bugfix_names_always_fail() -> None:
    compliant = re.compile(r"^(feature|bugfix)/[a-z0-9]+(-[a-z0-9]+)*_\d{8}$")
    judged = 0
    for prefix in ("feature/", "bugfix/"):
        for tail in _random_branches(seed=11, count=400):
            if tail is None:
                continue
            branch = prefix + tail
            ok, message = vl.check_branch_name(branch)
            judged += 1
            if compliant.match(branch):
                continue  # 随机串恰好合规的不在本不变量内（极少，由下一条覆盖）
            assert not ok, branch
            assert "分支名不合规范" in message
    assert judged > 0


@pytest.mark.parametrize(
    "branch",
    [
        "feature/测试",  # 中文
        "feature/Targeted-Gate_20261001",  # 大写
        "feature/targeted gate_20261001",  # 空格
        "feature/targeted-gate",  # 缺日期
        "feature/targeted-gate_2026100",  # 日期位数不足
        "feature/targeted_gate_20261001",  # 名称里用了下划线
        "feature/-targeted_20261001",  # 连字符开头
        "bugfix/x_20261301",  # 不存在的月份
        "bugfix/",  # 空名称
    ],
)
def test_invariant_known_bad_names_fail(branch: str) -> None:
    ok, _ = vl.check_branch_name(branch)
    assert not ok


@pytest.mark.parametrize(
    "branch",
    ["feature/targeted-gate_20261001", "bugfix/cold-load-attach-key_20261001", "feature/a1_20260101"],
)
def test_invariant_compliant_names_pass(branch: str) -> None:
    ok, _ = vl.check_branch_name(branch)
    assert ok


@pytest.mark.parametrize("branch", [None, "main", "release/1.90.x", "hotfix/Anything Goes", "Feature/Upper"])
def test_invariant_other_branches_not_judged(branch: str | None) -> None:
    ok, message = vl.check_branch_name(branch)
    assert ok and "不判定" in message


# ---------------------------------------------------------------------------
# 接线用例：门禁与构建只读推导结果、不写 VERSION
# ---------------------------------------------------------------------------


def test_wiring_check_ps1_prints_label_and_has_branch_name_step() -> None:
    src = (REPO_ROOT / "check.ps1").read_text(encoding="utf-8-sig")
    assert "版本标签：" in src
    assert '-Id "branch_name"' in src
    assert "version_label.py" in src


def test_wiring_directory_build_props_sets_informational_version_only() -> None:
    props = (REPO_ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    assert "WsGameVersionLabel" in props
    assert "<InformationalVersion" in props
    # ABI 不变：不动程序集版本与文件版本
    assert "<AssemblyVersion" not in props
    assert "<FileVersion" not in props


def test_wiring_version_file_stays_plain_semver() -> None:
    assert re.fullmatch(r"\d+\.\d+\.\d+", (REPO_ROOT / "VERSION").read_text(encoding="utf-8").strip())


# ---------------------------------------------------------------------------
# 发布说明首行（build.ps1 -Release 第 6 步用同一个函数）
# ---------------------------------------------------------------------------


def test_repro_release_notes_first_line_is_release_label(tmp_path: Path) -> None:
    from _ps_harness import REPO_ROOT as HARNESS_ROOT, ps_quote, run_ps_json

    section = "## [1.93.0] - 2026-10-02\n\n### 新增\n\n- 条目"
    body = (
        f". {ps_quote(HARNESS_ROOT / 'toolchain' / '_release_notes.ps1')}\n"
        f"$text = New-ReleaseNotesText -Version '1.93.0' -ChangelogSection {ps_quote(section)}\n"
        "@{ Text = $text } | ConvertTo-Json | Set-Content -LiteralPath $ResultPath -Encoding UTF8\n"
    )
    text = run_ps_json(tmp_path, body)["Text"]
    lines = text.split("\n")
    assert lines[0] == "1.93.0_release"
    assert lines[1] == ""
    assert "\n".join(lines[2:]) == section  # 正文原样保留


def test_wiring_build_ps1_uses_release_notes_helper_and_label_env() -> None:
    src = (REPO_ROOT / "build.ps1").read_text(encoding="utf-8-sig")
    assert "New-ReleaseNotesText" in src
    assert "WsGameVersionLabel" in src
    # 版本校验逻辑不变：仍只接受纯 X.Y.Z
    assert r"$VersionFormatPattern = '^\d+\.\d+\.\d+$'" in src
