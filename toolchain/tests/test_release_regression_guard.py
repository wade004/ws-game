"""``toolchain/_release_regression_guard.ps1`` 回归测试（测试覆盖第四批，2026-10-01，复盘 I-13）。

被测规则：``build.ps1 -Release`` 在写回版本号之前要求 ``REGRESSION_LOG.md`` 里有一行"含 Unity 全量
通过"记录，且其对应提交是当前 HEAD，或是 HEAD 的祖先且其后到 HEAD 只改了文档类文件
（``docs/``、``architecture/``、``*.md``）；否则拒绝发布。

做法：每个场景在临时目录里 ``git init`` 一个真实的小仓库，造提交序列，写一份伪造的
``REGRESSION_LOG.md``，然后在 PowerShell 子进程里 dot-source 守卫脚本，调用
``Test-ReleaseRegressionRecord`` 取回 Ok / Reason / RunId / Sha。复现 = 缺记录/记录不含 Unity/记录之后
改了代码 -> 必须拒绝；不变量 = 记录就是 HEAD、记录之后只追加文档 -> 必须放行；另用真实的
``REGRESSION_LOG.md`` 钉住"现有记录行能被解析、含 Unity 与否的分类符合肉眼判断"。

另有静态接线断言：``build.ps1`` 已删除 ``-ReleaseSkipUnity`` 开关、调用了守卫、``-Release`` 第 5 步
不强制传 ``-Il2cpp``（复盘 I-6 于 2026-10-01 回退：构建机缺 VS C++ 工作负载与 Windows SDK）。
"""

from __future__ import annotations

import json
import re
import subprocess
from pathlib import Path

import pytest

from _ps_harness import REPO_ROOT, git_env, ps_quote, run_ps_json

GUARD = REPO_ROOT / "toolchain" / "_release_regression_guard.ps1"
BUILD_SCRIPT = REPO_ROOT / "build.ps1"
REAL_LOG = REPO_ROOT / "REGRESSION_LOG.md"

LOG_HEADER = """# 回归记录

| run_id | 通过/失败 | 对应提交 sha | 日期 |
| --- | --- | --- | --- |
"""

UNITY_FULL = "通过（32/32，含 Unity 全量，PlayMode 362/362）"


def _git(repo: Path, *args: str) -> str:
    proc = subprocess.run(
        ["git", "-C", str(repo), "-c", "user.name=t", "-c", "user.email=t@example.invalid",
         "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", *args],
        capture_output=True, text=True, encoding="utf-8", env=git_env(), timeout=60,
    )
    assert proc.returncode == 0, f"git {args} 失败：{proc.stderr}"
    return proc.stdout.strip()


def _commit(repo: Path, files: dict[str, str], message: str) -> str:
    for rel, content in files.items():
        path = repo / rel
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8", newline="\n")
        _git(repo, "add", "--", rel)
    _git(repo, "commit", "-q", "-m", message)
    return _git(repo, "rev-parse", "HEAD")


def _new_repo(root: Path, name: str) -> Path:
    repo = root / name
    repo.mkdir()
    _git(repo, "init", "-q")
    return repo


def _log(rows: list[tuple[str, str, str]]) -> str:
    lines = [LOG_HEADER.rstrip("\n")]
    for i, (result, sha, date) in enumerate(rows, start=1):
        lines.append(f"| full-20261001-{i:02d} | {result} | {sha} | {date} |")
    return "\n".join(lines) + "\n"


# ----------------------------------------------------------------------------
# 场景构造
# ----------------------------------------------------------------------------

def _build_scenarios(root: Path) -> list[dict]:
    scenarios: list[dict] = []

    def scenario(name: str, repo: Path, expected_ok: bool, reason_has: str = "") -> None:
        scenarios.append({"name": name, "repo": str(repo), "expected_ok": expected_ok, "reason_has": reason_has})

    # A. 记录就是 HEAD。
    repo = _new_repo(root, "a_head")
    head = _commit(repo, {"core/a.cs": "class A {}", "REGRESSION_LOG.md": LOG_HEADER}, "c1")
    (repo / "REGRESSION_LOG.md").write_text(_log([(UNITY_FULL, head[:8], "2026-10-01")]), encoding="utf-8", newline="\n")
    scenario("a_record_is_head", repo, True, "就是当前 HEAD")

    # B. 记录是祖先，其后只追加回归记录行本身（最典型：记录行随下一个提交落库）。
    repo = _new_repo(root, "b_ancestor_docs_only")
    tested = _commit(repo, {"core/a.cs": "class A {}"}, "tested")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested[:8], "2026-10-01")])}, "log row")
    scenario("b_ancestor_then_log_row_only", repo, True, "只改了文档类文件")

    # C. 记录是祖先，其后混入文档类改动（docs/、architecture/、CHANGELOG、README、任意 .md）。
    repo = _new_repo(root, "c_ancestor_many_docs")
    tested = _commit(repo, {"core/a.cs": "class A {}"}, "tested")
    _commit(repo, {
        "REGRESSION_LOG.md": _log([(UNITY_FULL, tested, "2026-10-01")]),
        "CHANGELOG.md": "# c\n",
        "docs/复盘/x.md": "x\n",
        "docs/data/table.csv": "a,b\n",
        "architecture/adr/0001.md": "adr\n",
        "adapters/unity/Packages/p/README.md": "r\n",
    }, "docs batch")
    scenario("c_ancestor_then_docs_batch", repo, True)

    # D. 记录是祖先，其后改了一个 .cs -> 拒绝，原因点名该文件。
    repo = _new_repo(root, "d_ancestor_code_changed")
    tested = _commit(repo, {"core/a.cs": "class A {}"}, "tested")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested, "2026-10-01")])}, "log row")
    _commit(repo, {"core/a.cs": "class A { int x; }"}, "code change")
    scenario("d_code_changed_after_record", repo, False, "core/a.cs")

    # E. 记录之后既有文档又有一个非文档文件（.json 数据）-> 拒绝。
    repo = _new_repo(root, "e_mixed")
    tested = _commit(repo, {"core/a.cs": "class A {}"}, "tested")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, tested, "2026-10-01")]), "data/x.json": "{}"}, "mixed")
    scenario("e_docs_plus_data_file", repo, False, "data/x.json")

    # F. 记录不含 Unity（-SkipUnity / 未跑 Unity）-> 拒绝。
    repo = _new_repo(root, "f_not_unity")
    head = _commit(repo, {"core/a.cs": "class A {}"}, "c1")
    (repo / "REGRESSION_LOG.md").write_text(_log([
        ("通过（check.ps1 -SkipUnity -Quick 30 步通过；未跑 Unity 步骤）", head, "2026-10-01"),
        ("通过（32/32，-SkipUnity）", head, "2026-10-01"),
        ("通过（G1 全量部分，未跑 Unity）", head, "2026-10-01"),
    ]), encoding="utf-8", newline="\n")
    scenario("f_records_without_unity", repo, False, "没有任何'含 Unity 全量通过'记录")

    # G. 含 Unity 字样但结果是失败 -> 拒绝。
    repo = _new_repo(root, "g_failed_run")
    head = _commit(repo, {"core/a.cs": "class A {}"}, "c1")
    (repo / "REGRESSION_LOG.md").write_text(_log([("失败（含 Unity 全量，PlayMode 340/362）", head, "2026-10-01")]),
                                          encoding="utf-8", newline="\n")
    scenario("g_failed_result_is_not_a_pass", repo, False)

    # H. 没有 REGRESSION_LOG.md / 没有任何记录行。
    repo = _new_repo(root, "h_no_log")
    _commit(repo, {"core/a.cs": "class A {}"}, "c1")
    scenario("h_log_file_missing", repo, False, "找不到回归记录文件")
    repo = _new_repo(root, "h2_empty_table")
    _commit(repo, {"REGRESSION_LOG.md": LOG_HEADER}, "c1")
    scenario("h2_no_rows", repo, False, "没有任何 full-YYYYMMDD-NN 记录行")

    # I. 记录的 sha 在本仓库不存在。
    repo = _new_repo(root, "i_unknown_sha")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, "deadbeef", "2026-10-01")])}, "c1")
    scenario("i_unknown_sha", repo, False, "解析不到提交")

    # J. 记录的提交不是 HEAD 的祖先（在另一条分支上）。
    repo = _new_repo(root, "j_not_ancestor")
    base = _commit(repo, {"core/a.cs": "class A {}"}, "base")
    _git(repo, "checkout", "-q", "-b", "side")
    side = _commit(repo, {"core/side.cs": "class S {}"}, "side")
    _git(repo, "checkout", "-q", "-")
    _commit(repo, {"REGRESSION_LOG.md": _log([(UNITY_FULL, side, "2026-10-01")])}, "main log")
    scenario("j_record_on_other_branch", repo, False, "不是当前 HEAD 的祖先")

    # K. 提交列多个 sha（"889a9736 + 收口提交" 的写法），任一满足即可。
    repo = _new_repo(root, "k_multi_sha")
    head = _commit(repo, {"core/a.cs": "class A {}"}, "c1")
    (repo / "REGRESSION_LOG.md").write_text(_log([(UNITY_FULL, f"1234567 + {head[:10]} + 收口提交", "2026-10-01")]),
                                          encoding="utf-8", newline="\n")
    scenario("k_sha_cell_with_extra_words", repo, True)

    # L. 多条记录：最新的含 Unity 记录不满足，但更早的一条满足（HEAD 之前的一次全量，其后只有文档）-> 放行。
    repo = _new_repo(root, "l_older_row_matches")
    tested = _commit(repo, {"core/a.cs": "class A {}"}, "tested")
    broken = _commit(repo, {"core/b.cs": "class B {}"}, "code after")
    _git(repo, "reset", "-q", "--hard", tested)  # 让 broken 悬空
    _commit(repo, {"REGRESSION_LOG.md": _log([
        (UNITY_FULL, tested, "2026-10-01"),
        (UNITY_FULL, broken, "2026-10-01"),   # 悬空提交：不是祖先
    ])}, "log")
    scenario("l_newest_row_unusable_older_row_ok", repo, True, "full-20261001-01")

    # M. 多条记录里最新的含 Unity 记录被拒绝时，原因里报告的是最新那条。
    repo = _new_repo(root, "m_reason_cites_newest")
    old = _commit(repo, {"core/a.cs": "class A {}"}, "old")
    new = _commit(repo, {"core/a.cs": "class A { int y; }"}, "new")
    _commit(repo, {"core/a.cs": "class A { int z; }", "REGRESSION_LOG.md": _log([
        (UNITY_FULL, old, "2026-10-01"), (UNITY_FULL, new, "2026-10-01")])}, "code+log")
    scenario("m_code_after_every_record", repo, False, "full-20261001-02")

    return scenarios


@pytest.fixture(scope="module")
def verdicts(tmp_path_factory: pytest.TempPathFactory) -> dict[str, dict]:
    tmp = tmp_path_factory.mktemp("release_guard")
    scenarios = _build_scenarios(tmp)
    manifest = tmp / "scenarios.json"
    manifest.write_text(json.dumps(scenarios, ensure_ascii=False), encoding="utf-8")
    body = f"""
. {ps_quote(GUARD)}
$items = Get-Content -LiteralPath {ps_quote(manifest)} -Raw -Encoding UTF8 | ConvertFrom-Json
$out = [ordered]@{{}}
foreach ($s in @($items)) {{
    $r = Test-ReleaseRegressionRecord -RepoRoot $s.repo
    $out[$s.name] = [ordered]@{{ ok = [bool]$r.Ok; reason = [string]$r.Reason; runId = [string]$r.RunId; sha = [string]$r.Sha; head = [string]$r.Head }}
}}
$out | ConvertTo-Json -Depth 4 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    result = run_ps_json(tmp, body, name="guard", timeout=300)
    result["__scenarios__"] = {s["name"]: s for s in scenarios}
    return result


@pytest.mark.parametrize("name", [
    "a_record_is_head", "b_ancestor_then_log_row_only", "c_ancestor_then_docs_batch",
    "d_code_changed_after_record", "e_docs_plus_data_file", "f_records_without_unity",
    "g_failed_result_is_not_a_pass", "h_log_file_missing", "h2_no_rows", "i_unknown_sha",
    "j_record_on_other_branch", "k_sha_cell_with_extra_words", "l_newest_row_unusable_older_row_ok",
    "m_code_after_every_record",
])
def test_scenario_verdict_matches_expectation(verdicts: dict, name: str) -> None:
    scenario = verdicts["__scenarios__"][name]
    row = verdicts[name]
    assert row["ok"] is scenario["expected_ok"], row["reason"]
    if scenario["reason_has"]:
        assert scenario["reason_has"] in row["reason"], row["reason"]
    if scenario["expected_ok"]:
        assert row["runId"].startswith("full-"), "放行时必须报告采纳的是哪条记录"
        assert re.fullmatch(r"[0-9a-f]{40}", row["sha"])
    else:
        assert row["runId"] == "" and row["sha"] == ""


def test_rejection_reason_tells_the_operator_what_to_do(verdicts: dict) -> None:
    reason = verdicts["d_code_changed_after_record"]["reason"]
    assert "REGRESSION_LOG.md" in reason
    assert "check.ps1" in reason
    assert verdicts["d_code_changed_after_record"]["head"][:8] in reason


# ----------------------------------------------------------------------------
# 记录行解析与分类：纯函数 + 真实 REGRESSION_LOG.md
# ----------------------------------------------------------------------------

@pytest.fixture(scope="module")
def classification(tmp_path_factory: pytest.TempPathFactory) -> dict:
    tmp = tmp_path_factory.mktemp("release_guard_classify")
    texts = {
        "unity_word": "通过（check.ps1 全量含 Unity：33 步）",
        "playmode_count": "通过（32/32，1.91.0 发布门禁，PlayMode 362/362，一次通过）",
        "skipunity_with_playmode_word": "通过（-SkipUnity 30 步；PlayMode 362/362 取自上一轮）",
        "not_run_unity": "通过（G1 全量部分；未跑 Unity 步骤）",
        "no_unity_at_all": "通过（31/31，488s）",
        "failed": "失败（含 Unity 全量）",
        "empty": "",
    }
    payload = tmp / "texts.json"
    payload.write_text(json.dumps(texts, ensure_ascii=False), encoding="utf-8")
    body = f"""
. {ps_quote(GUARD)}
$texts = Get-Content -LiteralPath {ps_quote(payload)} -Raw -Encoding UTF8 | ConvertFrom-Json
$out = [ordered]@{{}}
foreach ($p in $texts.PSObject.Properties) {{
    $out["cls:" + $p.Name] = [bool](Test-RegressionRowIsUnityFull -ResultText ([string]$p.Value))
}}
$rows = @(Get-RegressionLogRows -LogPath {ps_quote(REAL_LOG)})
$out["real_rows"] = @($rows | ForEach-Object {{
    [ordered]@{{ runId = $_.RunId; shas = @($_.Shas); unity = [bool](Test-RegressionRowIsUnityFull -ResultText $_.Result) }}
}})
$out["docs_only"] = [ordered]@{{}}
foreach ($path in @("docs/a.txt", "architecture/x/y.json", "README.md", "adapters/unity/Packages/p/README.md", "adapters/x/README.md.meta", "CHANGELOG.md", "REGRESSION_LOG.md", "docs\\win\\path.png")) {{
    $out.docs_only[$path] = [bool](Test-DocsOnlyPath -Path $path)
}}
foreach ($path in @("core/a.cs", "data/x.json", "toolchain/x.py", "build.ps1", "check.ps1", "documents/a.txt", "architecture_notes.txt", "adapters/unity/Assets/Editor/X.cs.meta")) {{
    $out.docs_only[$path] = [bool](Test-DocsOnlyPath -Path $path)
}}
$out | ConvertTo-Json -Depth 5 | Out-File -LiteralPath $ResultPath -Encoding utf8
"""
    return run_ps_json(tmp, body, name="classify")


@pytest.mark.parametrize("name,expected", [
    ("unity_word", True),
    ("playmode_count", True),
    ("skipunity_with_playmode_word", False),
    ("not_run_unity", False),
    ("no_unity_at_all", False),
    ("failed", False),
    ("empty", False),
])
def test_row_classification(classification: dict, name: str, expected: bool) -> None:
    assert classification[f"cls:{name}"] is expected


def test_real_regression_log_rows_parse_and_known_rows_classify_correctly(classification: dict) -> None:
    text = REAL_LOG.read_text(encoding="utf-8")
    expected_ids = re.findall(r"^\|\s*(full-\d{8}-\d+)\s*\|", text, flags=re.MULTILINE)
    rows = classification["real_rows"]
    if isinstance(rows, dict):
        rows = [rows]
    assert [r["runId"] for r in rows] == expected_ids, "解析出的记录行必须与文件里的 full-* 行一一对应、顺序一致"
    assert expected_ids, "REGRESSION_LOG.md 应当有记录行"
    by_id = {r["runId"]: r for r in rows}
    # 已知的两条：1.91.0 发布门禁（含 PlayMode 计数）算含 Unity 全量；只跑 G1 子集 + -SkipUnity -Quick 的那条不算。
    assert by_id["full-20260930-03"]["unity"] is True
    assert by_id["full-20261001-02"]["unity"] is False
    assert by_id["full-20261001-03"]["unity"] is True
    for row in rows:
        shas = row["shas"] if isinstance(row["shas"], list) else [row["shas"]]
        assert shas and all(re.fullmatch(r"[0-9a-f]{7,40}", s) for s in shas), row


@pytest.mark.parametrize("path,expected", [
    ("docs/a.txt", True),
    ("architecture/x/y.json", True),
    ("README.md", True),
    ("adapters/unity/Packages/p/README.md", True),
    ("adapters/x/README.md.meta", True),
    ("CHANGELOG.md", True),
    ("REGRESSION_LOG.md", True),
    ("docs\\win\\path.png", True),
    ("core/a.cs", False),
    ("data/x.json", False),
    ("toolchain/x.py", False),
    ("build.ps1", False),
    ("check.ps1", False),
    ("documents/a.txt", False),
    ("architecture_notes.txt", False),
    ("adapters/unity/Assets/Editor/X.cs.meta", False),
])
def test_docs_only_path_classification(classification: dict, path: str, expected: bool) -> None:
    table = classification["docs_only"]
    key = path
    assert table[key] is expected


# ----------------------------------------------------------------------------
# build.ps1 接线（静态）
# ----------------------------------------------------------------------------

def test_build_script_wiring_for_release_regression_gate() -> None:
    text = BUILD_SCRIPT.read_text(encoding="utf-8-sig")
    assert "ReleaseSkipUnity" not in text, "-ReleaseSkipUnity 开关及其引用必须已删除（复盘 I-13）"
    assert "_release_regression_guard.ps1" in text
    assert "Test-ReleaseRegressionRecord" in text
    step5 = text.split("第 5 步：全量门禁")[1].split("check.ps1 未通过")[0]
    assert '$checkArgs = @("-AbiStrict", "-FailFast", "-NoTiming")' in text, "发布门禁固定只传 -AbiStrict -FailFast -NoTiming（-NoTiming：门禁不得写 timing/ 弄脏工作树，否则打包自检看到 -dirty）"
    assert "-Il2cpp" not in step5.split("$checkArgs =")[1].split("\n")[0], \
        "发布门禁不得强制传 -Il2cpp（复盘 I-6 已回退：构建机缺 VS C++ 工作负载与 Windows SDK）"
    assert "-SkipUnity" not in text.split("第 5 步：全量门禁")[1].split("check.ps1 未通过")[0], \
        "发布门禁不得再有任何跳过 Unity 的传参路径"
    # 检查必须在写回版本号之前（拒绝时不污染工作树）。
    assert text.index("Test-ReleaseRegressionRecord -RepoRoot") < text.index("写回版本号 $Release")


def test_release_flow_docs_no_longer_mention_the_removed_switch() -> None:
    for rel in ("README.md", "build.ps1", "check.ps1", ".githooks/pre-commit", ".github/workflows/ci.yml",
                ".github/workflows/release.yml"):
        path = REPO_ROOT / rel
        if path.exists():
            assert "ReleaseSkipUnity" not in path.read_text(encoding="utf-8-sig"), rel
