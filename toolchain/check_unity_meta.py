#!/usr/bin/env python3
"""Unity ``.meta`` 完整性门禁（不依赖 Unity 本体，纯读 git 索引 + 静态规则）。

背景（判断记录）：Unity 会为它自己导入范围（工程下的 ``Assets/`` 与 ``Packages/``，含
manifest.json 里 ``file:`` 开头指向的内嵌本地包）内的每个文件/目录生成一个同名
``<名字>.meta``；仓库约定把 ``.meta`` 与源文件一并提交（漏提交会让消费方克隆后 Unity
重新生成 GUID，破坏既有场景/预制体对这些资源的引用）。本仓库执行 agent 一律被要求不跑
Unity（``check.ps1``/``build.ps1`` 的 Unity 步骤耗时且会触发全量 reimport），而 ``.meta``
恰恰是 Unity 首次导入新文件时才在本机生成的产物——纯 .NET/Python 门禁此前完全没有覆盖这一
类问题，已经在 1.45.0 发版前（``games/_template/`` 下 6 个新 ``.cs``）与之后（``Runtime/
Diagnostics/`` 下 2 个新 ``.cs``）各漏提交一次。本脚本只用 git 元数据判断"该有 .meta 的地方
是否都有"，不需要启动 Unity，可以在 ``check.ps1 -SkipUnity``/``-Quick`` 下正常跑。

**判定范围（Unity 实际会导入、因而需要 .meta 的路径）**：

1. ``<Unity 工程>/Assets/`` 整棵树。
2. ``<Unity 工程>/Packages/<pkg>/`` —— 直接摆在 ``Packages/`` 目录下、自带
   ``package.json`` 的本地包（本仓库目前是
   ``adapters/unity/Packages/com.gamefoundation.adapter.unity/``）。
3. ``<Unity 工程>/Packages/manifest.json`` 里值以 ``"file:"`` 开头的依赖——UPM 内嵌
   本地包（embedded package），物理路径可能落在仓库其它位置（本仓库目前是
   ``games/_template/`` 与 ``adapters/conformance/``，均在各自的 ``manifest.json``
   里以 ``"file:../../../<路径>"`` 形式登记，见该文件）。

**判断记录（目录本身也要有 .meta，2026-09-20 二次踩坑后补齐）**：Unity 不止为文件生成
``.meta``，也为它导入范围内的**每一层目录**生成——本仓库真实样本：
``adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins.meta``（`Plugins`
目录本身的 meta，与 `Plugins/Core.meta` 是两个不同层级的东西）、``games/_template/Runtime.meta``
一类。本脚本第一版只检查了文件级缺失，遗漏了目录级——真实踩坑：`Runtime/Diagnostics/` 下两个
`.cs` 补了 `.meta` 后，Unity 真实导入还额外生成了目录级的 `Runtime/Diagnostics.meta`，
第一版实现对此完全没有检测能力（`git ls-files` 从不返回目录本身的路径，只有文件路径，需要从
"某目录下存在被跟踪的文件"反推"这个目录该有 meta"）。现在的判定：导入根内，只要某个目录下
（任意深度）存在至少一个被跟踪文件，该目录自身就"应当有 .meta"——导入根目录本身除外（``Assets``
自身、每个包/内嵌包根目录自身都不需要 .meta，只用当前仓库实测核对过：既没有
``adapters/unity/Assets.meta``，也没有 ``games/_template.meta``/``adapters/conformance.meta``）。
隐藏目录/以 ``~`` 结尾的目录同文件一样被 Unity 整体跳过（连同其内容一起，不只是目录自身），
见下方 ``_is_in_import_scope`` 判断记录。

``Packages/manifest.json`` 与 ``Packages/packages-lock.json`` 本身是 Package Manager
的清单文件，不是被导入的资源，Unity 不会为它们生成 ``.meta``——两者都直接摆在
``Packages/`` 根下、不在任何一个"导入根"目录*里面*，天然被上面的范围排除，不需要
额外硬编码例外名单（已用当前仓库实测核对：这两个文件确实没有 ``.meta``，且不在
本脚本任何一个导入根内）。

范围之外的典型例子：``adapters/unity/DiagnosticsForwarding/`` 是普通 dotnet 测试工程
（有 ``.csproj``，不在 ``Assets/``/``Packages/`` 下），不需要 ``.meta``；``adapters/unity/
Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core/`` 整个目录被
``.gitignore`` 排除（同步进来的构建期 DLL），目录本身没有任何文件被 git 跟踪，因此也不会
被本脚本要求补 ``.meta``——但该目录**自身**的 ``Core.meta``（保留稳定 GUID 用，见
``.gitignore`` 对应条目上方判断记录）仍然按仓库既有约定提交，本脚本据此不把它当"孤儿"
（见 :func:`find_meta_issues` 里 ``git check-ignore`` 判断分支）。

两类问题：

* **缺失**：某个已被 git 跟踪、落在上述导入范围内的文件，没有对应的 ``<文件>.meta``。
* **孤儿**：某个 ``.meta`` 存在，但它对应的文件/目录已经不再被 git 跟踪——且该路径
  也没有被 ``.gitignore`` 排除（被排除的情形是"目录级 meta 保留稳定 GUID"的既有约定，
  不算孤儿，见上一段）。

退出码：0 = 通过（无缺失、无孤儿）；1 = 发现缺失和/或孤儿；2 = 调用出错（如 git 命令
本身失败）。
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import _console  # noqa: E402  （见 _console.py 判断记录：必须在打印任何中文前调用）

UNITY_PROJECT_DEFAULT = "adapters/unity"


class GitError(RuntimeError):
    """git 子进程本身失败（不是"检查发现问题"，是"检查跑不起来"）。"""


def _run_git(repo_root: Path, args: list[str]) -> str:
    result = subprocess.run(
        ["git", *args],
        cwd=str(repo_root),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    if result.returncode != 0:
        raise GitError(
            f"git {' '.join(args)} 失败（退出码 {result.returncode}）：{result.stderr.strip()}"
        )
    return result.stdout


def _ls_files(repo_root: Path, rel_root: str) -> list[str]:
    """返回 rel_root 下所有已被 git 跟踪的文件路径（相对仓库根，正斜杠，NUL 分隔避免文件名
    含空格/特殊字符被切错）。"""
    out = _run_git(repo_root, ["ls-files", "-z", "--", rel_root])
    return [p for p in out.split("\0") if p]


def _is_ignored(repo_root: Path, rel_path: str) -> bool:
    """判断 rel_path 是否被 .gitignore 排除（不区分文件/目录）。git check-ignore 找不到匹配
    规则时返回码是 1（"未被排除"，不是错误），只有真正的 git 调用失败才是别的返回码——这里
    统一按"非 0 即未被排除"处理，与 git 自身的语义一致，不需要额外分支。

    判断记录：``.gitignore`` 里"仅目录"的规则写成以 ``/`` 结尾（如
    ``.../Runtime/Plugins/Core/``），而 ``git check-ignore`` 判断一个不带尾部 ``/`` 的路径
    是否命中"仅目录"规则时，要靠 stat 该路径确认它确实是目录——本函数的调用场景（孤儿 .meta
    对应的目标路径）目标本身多半已不在本地磁盘存在（构建产物目录、或确已被删除的资源），
    stat 不到就不会命中仅目录规则，实测复现：不带 ``/`` 查询返回"未忽略"，带 ``/`` 查询才
    命中。因此这里同时用两种形式各查一次，任一命中即算被忽略，不依赖目标是否真的在磁盘上。"""
    for candidate in (rel_path, rel_path + "/"):
        result = subprocess.run(
            ["git", "check-ignore", "-q", "--", candidate],
            cwd=str(repo_root),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        if result.returncode == 0:
            return True
    return False


def discover_import_roots(repo_root: Path, unity_project: str = UNITY_PROJECT_DEFAULT) -> list[str]:
    """发现 Unity 会实际导入（因而要求 .meta 一一对应）的目录集合，规则见模块 docstring。"""
    project = Path(unity_project)
    roots: list[str] = []

    assets_dir = repo_root / project / "Assets"
    if assets_dir.is_dir():
        roots.append((project / "Assets").as_posix())

    packages_dir = repo_root / project / "Packages"
    if packages_dir.is_dir():
        for child in sorted(packages_dir.iterdir()):
            if child.is_dir() and (child / "package.json").is_file():
                roots.append((project / "Packages" / child.name).as_posix())

    manifest_path = packages_dir / "manifest.json"
    if manifest_path.is_file():
        try:
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            raise GitError(f"解析 {manifest_path} 失败：{exc}") from exc
        repo_root_resolved = repo_root.resolve()
        for dep_value in manifest.get("dependencies", {}).values():
            if not isinstance(dep_value, str) or not dep_value.startswith("file:"):
                continue
            rel = dep_value[len("file:"):]
            resolved = (packages_dir / rel).resolve()
            try:
                rel_to_repo = resolved.relative_to(repo_root_resolved)
            except ValueError:
                # 内嵌包指向仓库外部路径：超出本仓库门禁范围，不检查（目前仓库内两处
                # file: 依赖都指向仓库内部路径，这条分支是防御性的，未被现状触发）。
                continue
            roots.append(rel_to_repo.as_posix())

    seen: set[str] = set()
    unique_roots: list[str] = []
    for r in roots:
        if r not in seen:
            seen.add(r)
            unique_roots.append(r)
    return unique_roots


def _is_in_import_scope(rel_path: str, root: str) -> bool:
    """判断 rel_path（相对仓库根、落在 root 之内）是否真的处在 Unity 的导入范围内。

    判断记录：Unity 自身的导入忽略规则是"整个子树跳过"——隐藏目录/以 ``~`` 结尾的目录里的
    任何内容都不会被导入，不只是那个隐藏/``~`` 结尾的路径本身。第一版实现只检查叶子文件名，
    漏了"某个中间目录是隐藏目录"这一情形；这里改成检查 root 到 rel_path 之间**每一段**路径
    分量，任一段命中就整条路径都算超出导入范围。本仓库现状没有这类路径，属于面向 Unity 官方
    行为的防御性实现。"""
    root_prefix = root + "/"
    if not rel_path.startswith(root_prefix):
        return False
    for part in rel_path[len(root_prefix):].split("/"):
        if part.startswith(".") or part.endswith("~"):
            return False
    return True


def _ancestor_dirs(rel_path: str, root: str) -> list[str]:
    """返回 rel_path 在 root 内的全部祖先目录（相对仓库根），不含 root 自身、不含 rel_path
    自己。例如 root="a/b"、rel_path="a/b/c/d/e.cs" 返回 ``["a/b/c", "a/b/c/d"]``。"""
    root_prefix = root + "/"
    parts = rel_path[len(root_prefix):].split("/")
    dirs = []
    for i in range(1, len(parts)):
        dirs.append(root + "/" + "/".join(parts[:i]))
    return dirs


def find_meta_issues(
    repo_root: Path, unity_project: str = UNITY_PROJECT_DEFAULT
) -> tuple[list[str], list[str], list[str]]:
    """返回 ``(missing, orphans, roots)``，均为相对仓库根、正斜杠的路径列表（已排序）。

    ``missing``/``orphans`` 里的条目既可能是文件、也可能是目录（见模块 docstring"目录本身也要
    有 .meta"判断记录）——两者格式相同（不带 ``.meta`` 后缀的路径本身），调用方按"这个路径缺
    对应 .meta"统一处理即可，不需要先区分文件/目录。"""
    roots = discover_import_roots(repo_root, unity_project)
    tracked: set[str] = set()
    in_scope: set[str] = set()
    required_dirs: set[str] = set()
    for root in roots:
        for f in _ls_files(repo_root, root):
            tracked.add(f)
            if not _is_in_import_scope(f, root):
                continue
            in_scope.add(f)
            required_dirs.update(_ancestor_dirs(f, root))

    missing = []
    for f in in_scope:
        if f.endswith(".meta"):
            continue
        if f + ".meta" not in tracked:
            missing.append(f)
    for d in required_dirs:
        if d + ".meta" not in tracked:
            missing.append(d)

    orphans = []
    for m in tracked:
        if not m.endswith(".meta"):
            continue
        target = m[: -len(".meta")]
        if target in tracked:
            continue  # 文件级 meta，对应文件仍被跟踪
        if target in required_dirs:
            continue  # 目录级 meta，该目录下仍有被跟踪文件
        if _is_ignored(repo_root, target):
            # 目标路径整体被 .gitignore 排除（如构建产物目录）：目录级 meta 用于保留稳定
            # GUID 是仓库既有约定（见 Runtime/Plugins/Core.meta 判断记录），不算孤儿。
            continue
        orphans.append(m)

    return sorted(missing), sorted(orphans), roots


def main(argv: list[str] | None = None) -> int:
    _console.ensure_utf8_stdio()
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo-root", default=".", help="仓库根目录，默认当前工作目录")
    parser.add_argument(
        "--unity-project",
        default=UNITY_PROJECT_DEFAULT,
        help=f"Unity 工程相对仓库根的路径，默认 {UNITY_PROJECT_DEFAULT}",
    )
    args = parser.parse_args(argv)

    repo_root = Path(args.repo_root).resolve()
    try:
        missing, orphans, roots = find_meta_issues(repo_root, args.unity_project)
    except GitError as exc:
        print(f"[check_unity_meta] {exc}", file=sys.stderr)
        return 2

    print("[check_unity_meta] 扫描的 Unity 导入根：")
    for r in roots:
        print(f"  - {r}")

    ok = True
    if missing:
        ok = False
        print(f"\n[check_unity_meta] 缺失 .meta（{len(missing)} 处没有对应 .meta，含文件与目录）：")
        for f in missing:
            kind = "目录" if (repo_root / f).is_dir() else "文件"
            print(f"  - [{kind}] {f}  --> 缺 {f}.meta")
        print(
            "  修法：用 Unity 打开工程完成一次真实导入，让 Unity 生成对应 .meta 后与源文件"
            "一并提交；不要手写 .meta（GUID 必须由 Unity 分配，手写会破坏既有引用）。"
        )
    if orphans:
        ok = False
        print(f"\n[check_unity_meta] 孤儿 .meta（{len(orphans)} 个 .meta 对应的文件/目录已不存在）：")
        for m in orphans:
            print(f"  - {m}  --> 缺 {m[: -len('.meta')]}")
        print(
            "  修法：确认对应资源确已删除/改名后，把这些孤儿 .meta 一并从 git 中移除并提交；"
            "如果该路径是刻意改成 .gitignore 排除的构建产物目录、需要保留目录级 .meta 稳定"
            "GUID，请参考 Runtime/Plugins/Core.meta 的既有判断记录在 .gitignore 里写明理由。"
        )

    if ok:
        print("\n[check_unity_meta] 通过：Unity 导入范围内 .meta 与源文件一一对应，无缺失、无孤儿。")
        return 0
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
