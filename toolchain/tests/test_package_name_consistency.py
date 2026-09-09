"""四个私服包名在 `registry.json`/`build.ps1`/`check.ps1`/`get_framework.ps1` 四处一致的静态回归。

背景（ADR-0018 决策第 3 条，无头适配层交付落地，2026-09-09）：私服交付通道的包名清单此前分散
维护在四个地方——`toolchain/registry/registry.json`（`packages` 数组，私服本身承认的包清单）、
`build.ps1`（5.15 节组装并 `npm pack` 出对应目录）、`check.ps1`（"包清单一致性"步骤核对
`npm pack --dry-run` 产物）、`toolchain/get_framework.ps1`（`-FromRegistry` 分支写入游戏工程
`Packages/manifest.json` 与 `ws-game.lock`）——新增第四个包 `com.gamefoundation.adapter.headless`
时若漏改其中一处，四处包名集合会悄悄产生分歧且没有任何自动化门禁能发现，只能靠人工审计逐条比对。
本文件用纯文本解析（不需要真的跑 `build.ps1`/`check.ps1`，跨平台可跑）把四处各自声明的包名清单
抽出来，断言四个集合完全相等，防止再漏一处。

判断记录：`get_framework.ps1` 的 `$ThreePackageNames`（写入 `Packages/manifest.json`
`dependencies` 的三个 Unity 依赖）与新增的 `$OptionalPackageNames`（不写入 `manifest.json`、只登记
进 `ws-game.lock` 的 `source.optional_packages`，见该脚本判断记录"为什么本包不写入 manifest.json"）
两者并集才是"该脚本认识的全部包名"，与另外三处的四包清单比较。

运行：

```
python -m pytest toolchain/tests/test_package_name_consistency.py -q
```
或作为 `toolchain` 套件的一部分：`python -m pytest toolchain/tests -q`（`check.ps1` 已在跑，不需要
改 `check.ps1` 本身来运行本文件）。跨平台（纯文本解析，不依赖 PowerShell 宿主）。
"""

from __future__ import annotations

import json
import re
from pathlib import Path

TOOLCHAIN_DIR = Path(__file__).resolve().parents[1]
REPO_ROOT = TOOLCHAIN_DIR.parent

EXPECTED_PACKAGE_NAMES = frozenset(
    {
        "com.gamefoundation.adapter.unity",
        "com.gamefoundation.framework-data",
        "com.gamefoundation.toolchain",
        "com.gamefoundation.adapter.headless",
    }
)

# 四个包名共同的前缀，用来在自由文本（PowerShell 数组字面量、JSON 数组）里粗筛出候选 token，
# 避免把无关的 "com.gamefoundation.*"（如 scope 本身、keywords 里的 "gamefoundation"）误收进来——
# 每个包名都形如 "com.gamefoundation.<segment>(.<segment>)*"，用引号包裹的字符串字面量匹配即可。
_PACKAGE_NAME_LITERAL_RE = re.compile(r'"(com\.gamefoundation\.[a-z][a-z0-9_.-]*)"')


def _read_text(relative_path: str) -> str:
    path = REPO_ROOT / relative_path
    assert path.is_file(), f"找不到文件：{path}"
    return path.read_text(encoding="utf-8")


def _extract_package_literals(text: str) -> set[str]:
    """从任意文本里抽出全部被双引号包裹、形如 "com.gamefoundation.xxx" 的字符串字面量。"""
    return set(_PACKAGE_NAME_LITERAL_RE.findall(text))


def test_registry_json_packages_match_expected_four() -> None:
    registry_json = json.loads(_read_text("toolchain/registry/registry.json"))
    packages = set(registry_json["packages"])
    assert packages == EXPECTED_PACKAGE_NAMES, (
        f"toolchain/registry/registry.json 的 packages 与预期四个包名不一致："
        f"多余={packages - EXPECTED_PACKAGE_NAMES}，缺失={EXPECTED_PACKAGE_NAMES - packages}"
    )
    # registry.json 里每个包名只应出现这一处（数组元素），不应该有重复条目。
    assert len(registry_json["packages"]) == len(EXPECTED_PACKAGE_NAMES), (
        "toolchain/registry/registry.json 的 packages 数组存在重复条目"
    )


def test_build_ps1_assembles_all_four_packages() -> None:
    text = _read_text("build.ps1")
    # 5.15 节：四个包各自的 $PackagesRoot 子目录赋值行，形如
    # `$pkgXxxDir = Join-Path $PackagesRoot "com.gamefoundation.xxx"`；直接从整份文本抽取候选包名
    # 字面量，比逐行匹配变量名更不容易因为重命名局部变量而漏判。
    found = _extract_package_literals(text)
    assert found >= EXPECTED_PACKAGE_NAMES, (
        f"build.ps1 未提及全部四个包名，缺失={EXPECTED_PACKAGE_NAMES - found}（见 5.15 节四个包各自的组装小节）"
    )

    # 4th 包的组装目录变量、npm pack 循环、-PublishRegistry 循环三处都应把它纳入——用变量名
    # $pkgHeadlessDir 做锚点粗查，防止只在字符串字面量里提了一嘴、实际没接入两个 foreach 循环。
    assert "$pkgHeadlessDir" in text, "build.ps1 缺少 $pkgHeadlessDir 变量（第四个包目录未组装）"
    npm_pack_loop = re.search(r"foreach\s*\(\s*\$pkgDirForPack\s+in\s+@\(([^)]*)\)\s*\)", text)
    assert npm_pack_loop is not None, "build.ps1 找不到 npm pack 的 foreach 循环（四个包 .tgz 生成步骤）"
    assert "$pkgHeadlessDir" in npm_pack_loop.group(1), (
        "build.ps1 的 npm pack foreach 循环未包含 $pkgHeadlessDir，第四个包不会生成 .tgz"
    )


def test_check_ps1_package_names_match_expected_four() -> None:
    text = _read_text("check.ps1")
    # "包清单一致性"步骤里的 $packageNames = @(...) 数组字面量。
    match = re.search(r"\$packageNames\s*=\s*@\(([^)]*)\)", text, re.DOTALL)
    assert match is not None, "check.ps1 找不到 $packageNames 数组（\"包清单一致性\"步骤）"
    found = set(_extract_package_literals(match.group(1)))
    assert found == EXPECTED_PACKAGE_NAMES, (
        f"check.ps1 的 $packageNames 与预期四个包名不一致："
        f"多余={found - EXPECTED_PACKAGE_NAMES}，缺失={EXPECTED_PACKAGE_NAMES - found}"
    )


def test_get_framework_ps1_recognizes_all_four_package_names() -> None:
    text = _read_text("toolchain/get_framework.ps1")
    # $ThreePackageNames（写入 manifest.json 的三个 Unity 依赖）+ $OptionalPackageNames（不写入
    # manifest.json、只登记进 ws-game.lock 的可选包，见该脚本判断记录）两者并集才是全部四个包名。
    three_match = re.search(r"\$ThreePackageNames\s*=\s*@\(([^)]*)\)", text, re.DOTALL)
    optional_match = re.search(r"\$OptionalPackageNames\s*=\s*@\(([^)]*)\)", text, re.DOTALL)
    assert three_match is not None, "get_framework.ps1 找不到 $ThreePackageNames 数组"
    assert optional_match is not None, (
        "get_framework.ps1 找不到 $OptionalPackageNames 数组（ADR-0018 决策 3 新增的第四个可选包）"
    )

    three_names = set(_extract_package_literals(three_match.group(1)))
    optional_names = set(_extract_package_literals(optional_match.group(1)))

    assert not (three_names & optional_names), (
        f"get_framework.ps1 的 $ThreePackageNames 与 $OptionalPackageNames 存在重叠：{three_names & optional_names}"
    )
    combined = three_names | optional_names
    assert combined == EXPECTED_PACKAGE_NAMES, (
        f"get_framework.ps1 认识的包名（$ThreePackageNames ∪ $OptionalPackageNames）与预期四个包名不一致："
        f"多余={combined - EXPECTED_PACKAGE_NAMES}，缺失={EXPECTED_PACKAGE_NAMES - combined}"
    )
    # 第四个包不是 Unity 依赖，不应该混进 $ThreePackageNames（否则会被写进 manifest.json 的
    # dependencies，与"不是 Unity 依赖、按需 npm install"的判断记录矛盾）。
    assert "com.gamefoundation.adapter.headless" not in three_names, (
        "com.gamefoundation.adapter.headless 不应出现在 $ThreePackageNames"
        "（会被写入游戏工程 Packages/manifest.json 的 dependencies，违反该包判断记录）"
    )


def test_registry_manifests_directory_has_four_entries() -> None:
    manifests_dir = REPO_ROOT / "toolchain" / "registry" / "manifests"
    assert manifests_dir.is_dir(), f"找不到目录：{manifests_dir}"
    subdirs = {p.name for p in manifests_dir.iterdir() if p.is_dir()}
    # adapters/unity 包的清单目录历史上就没有独立的 manifests/ 子目录（内容直接取自
    # adapters/unity/Packages/com.gamefoundation.adapter.unity/，见 build.ps1 5.15 节"包 1"注释），
    # 因此本目录只承载另外三个包各自的 package.json/README.md 源文件——与 registry.json 四个包
    # 相比恰好少一个，是既有约定，不是遗漏。
    expected_subdirs = {"framework-data", "toolchain", "adapter-headless"}
    assert subdirs == expected_subdirs, (
        f"toolchain/registry/manifests/ 子目录与预期不一致：多余={subdirs - expected_subdirs}，"
        f"缺失={expected_subdirs - subdirs}"
    )


# F3 新增：release.yml 四包修正（见该文件头判断记录"附件存在性检查改为核对完整附件集合"一节）——
# F2 落地时该工作流仍按"三个 .tgz/五个附件"编写（已知限制），本文件第五处静态解析核对
# release.yml 的 $requiredNames/$requiredPaths 是否已同步四个包名，与另外四处（registry.json/
# build.ps1/check.ps1/get_framework.ps1）保持一致，防止发布工作流单独漏改。

_TGZ_LITERAL_RE = re.compile(r'"(com\.gamefoundation\.[a-z][a-z0-9_.-]*)-\$version\.tgz"')


def test_release_yml_required_assets_match_expected_four_packages() -> None:
    text = _read_text(".github/workflows/release.yml")

    names_match = re.search(r"\$requiredNames\s*=\s*@\(([^)]*)\)", text, re.DOTALL)
    assert names_match is not None, "release.yml 找不到 $requiredNames 数组（\"Check for existing release assets\" 步骤）"

    tgz_package_names = set(_TGZ_LITERAL_RE.findall(names_match.group(1)))
    assert tgz_package_names == EXPECTED_PACKAGE_NAMES, (
        f"release.yml 的 $requiredNames 里 *.tgz 对应的包名与预期四个包名不一致："
        f"多余={tgz_package_names - EXPECTED_PACKAGE_NAMES}，缺失={EXPECTED_PACKAGE_NAMES - tgz_package_names}"
    )

    # $requiredNames 应恰好六项：zip、lock、四个 .tgz（"四包修正"——F2 遗留的"三个 .tgz/五个附件"
    # 已收口）。逐条数引号字符串字面量，防止漏加/多加条目却恰好包名集合仍然相等的边界情况（如
    # 重复了某个包名，集合去重后仍然是四个，但列表本身条目数不对）。
    entry_literals = re.findall(r'"\$?[^"]*\$version\.[a-z]+"', names_match.group(1))
    assert len(entry_literals) == 6, (
        f"release.yml 的 $requiredNames 应恰好六项（zip、lock、四个 .tgz），实际={len(entry_literals)}：{entry_literals}"
    )

    paths_match = re.search(r"\$requiredPaths\s*=\s*@\{([^}]*)\}", text, re.DOTALL)
    assert paths_match is not None, "release.yml 找不到 $requiredPaths 哈希表"
    paths_tgz_names = set(_TGZ_LITERAL_RE.findall(paths_match.group(1)))
    assert paths_tgz_names == EXPECTED_PACKAGE_NAMES, (
        f"release.yml 的 $requiredPaths 里 *.tgz 对应的包名与预期四个包名不一致："
        f"多余={paths_tgz_names - EXPECTED_PACKAGE_NAMES}，缺失={EXPECTED_PACKAGE_NAMES - paths_tgz_names}"
    )

    # 判断记录（不再断言正文不出现"三个 .tgz/五个附件"字样）：文件头判断记录段落如实保留了
    # "本工作流此前按'三个 .tgz/五个附件'编写"这句历史描述（说明改动缘由），不是当前生效的代码，
    # 断言其完全消失反而会强迫删除有价值的判断记录；本测试改为只断言真正生效的
    # $requiredNames/$requiredPaths 两处代码结构已经是四包/六项，这才是"没有漏改"的真正证据。
