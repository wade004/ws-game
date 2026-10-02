"""发版打包必须带 data/_feel（手感落地 S1，收 S0 遗留 (b)）。

build.ps1 -Release 的真实打包要跑数分钟、且需要 Unity 参与，不适合放进 pytest；这里对"打包脚本里
data/_feel 与 data/_framework 同路径对待"做静态断言（复现：S0 时 data/_feel 没有任何打包语句，
消费方的 dist 里拿不到手感档案），产物层的断言在门禁 pkg_manifest 步骤
（toolchain/_gate_unity_verdicts.ps1 的 Get-PackageManifestProblems，对 framework-data 包要求
Data~/data/_feel/feel/*.json 在清单里，见 test_gate_unity_verdicts.py）。
"""

from __future__ import annotations

import re

from _ps_harness import REPO_ROOT

BUILD = (REPO_ROOT / "build.ps1").read_text(encoding="utf-8")
SYNC = (REPO_ROOT / "toolchain" / "sync_package_content.ps1").read_text(encoding="utf-8")


def test_dist_copies_the_feel_dataset_like_the_framework_dataset() -> None:
    assert re.search(r'Copy-DistDir\s+-SourceRelative\s+"data\\_framework"\s+-DestName\s+"data\\_framework"', BUILD)
    assert re.search(r'Copy-DistDir\s+-SourceRelative\s+"data\\_feel"\s+-DestName\s+"data\\_feel"', BUILD)


def test_framework_data_package_carries_the_feel_dataset() -> None:
    assert re.search(r'Join-Path \$DistRoot "data\\_feel"\)\s+-Destination \(Join-Path \$pkgDataDataTilde "data\\_feel"\)', BUILD)


def test_manifest_lists_feel_file_count_and_table_versions() -> None:
    assert '"data/_feel: $dataFeelFileCount files"' in BUILD
    assert '@("_framework", "_feel")' in BUILD  # data_schemas 遍历两个框架级数据根


def test_workbench_and_consumer_sync_mirror_the_feel_dataset_into_streaming_assets() -> None:
    assert re.search(r'Sync-ContentTree .*data\\_feel', BUILD)
    assert 'Sync-Tree -SourceDir (Join-Path $dataDir "data\\_feel")' in SYNC


# ---------------------------------------------------------------------------
# 手感实验室随发布产物分发（M2-D）。原先这里有一条"data\\_lab 不分发"的断言，随分发决定一并作废。
# 产物层的断言在门禁 pkg_manifest 步骤（Get-PackageManifestProblems 对 toolchain 包要求 Tools~/feellab/ 下的
# 预编译命令行与自包含实验室根，见 test_gate_unity_verdicts.py）；运行层在 consumer_smoke.ps1 的 feellab suite 步骤。
# ---------------------------------------------------------------------------

FEELLAB_CSPROJ = (REPO_ROOT / "toolchain" / "feellab" / "FeelLab.csproj").read_text(encoding="utf-8")
CONSUMER_SMOKE = (REPO_ROOT / "toolchain" / "consumer_smoke.ps1").read_text(encoding="utf-8")


def test_dist_copies_the_lab_dataset_action_dataset_and_fixtures_at_repo_relative_paths() -> None:
    # 与仓库同路径：dist 根本身就是一个实验室根（脚本里的 extraDataRoots、默认夹具目录都按工作目录相对解析）。
    for source, dest in (("data\\_lab", "data\\_lab"), ("data\\_lab_action", "data\\_lab_action"),
                          ("data\\_equip", "data\\_equip"), ("lab\\fixtures", "lab\\fixtures")):
        pattern = r'Copy-DistDir\s+-SourceRelative\s+"' + re.escape(source) + r'"\s+-DestName\s+"' + re.escape(dest) + '"'
        assert re.search(pattern, BUILD), source
    for line in ('"data/_lab: $dataLabFileCount files"', '"data/_lab_action: $dataLabActionFileCount files"',
                 '"data/_equip: $dataEquipFileCount files"', '"lab/fixtures: $labFixturesFileCount files"'):
        assert line in BUILD, line


def test_lab_data_is_not_mirrored_into_streaming_assets_or_the_framework_data_package() -> None:
    # 实验室数据是验收设施用的，不是运行期框架数据：不进游戏的 StreamingAssets、不进 framework-data 包的 Data~/。
    assert not re.search(r"Sync-ContentTree .*data\\_lab", BUILD)
    assert not re.search(r'\$pkgDataDataTilde "data\\_lab', BUILD)


def test_feellab_prebuilt_output_and_lib_are_packaged_like_simrunner() -> None:
    assert 'Join-Path $DistRoot "toolchain\\feellab\\bin"' in BUILD
    assert 'Join-Path $DistRoot "toolchain\\feellab\\lib"' in BUILD
    assert 'Join-Path $DistRoot "toolchain\\feellab\\Directory.Build.props"' in BUILD
    assert '"[feellab]"' in BUILD
    # 打包顺序：必须排在"打四个 npm 包"（toolchain 包内容取自 dist\\<ver>\\toolchain\\）之前。
    assert BUILD.index('toolchain\\feellab\\bin"') < BUILD.index('$pkgToolDir = Join-Path $PackagesRoot "com.gamefoundation.toolchain"')


def test_toolchain_package_carries_a_self_contained_lab_root() -> None:
    assert 'Join-Path $pkgToolDir "Tools~\\feellab\\labroot"' in BUILD
    for part in ("data\\_framework", "data\\_feel", "data\\_lab", "data\\_lab_action", "data\\_equip", "lab\\fixtures"):
        assert f'"{part}"' in BUILD.split("$pkgFeelLabRoot = ")[1].split("Write-Host")[0], part


def test_feellab_csproj_lib_fallback_lists_exactly_the_dlls_build_ps1_ships() -> None:
    # 不变量：csproj 的 lib/ 回退分支引用的 DLL 集合 == build.ps1 拷进 dist\\toolchain\\feellab\\lib\\ 的集合
    # （Lab.Kernel + Core.Sim + Adapters.Stub + $CoreAssemblies 全部六个）。少一个，独立发行包里自行编译/运行就缺依赖。
    referenced = set(re.findall(r"<HintPath>lib\\([A-Za-z.]+)\.dll</HintPath>", FEELLAB_CSPROJ))
    core_block = BUILD.split("$CoreAssemblies = @(")[1].split(")")[0]
    core_names = set(re.findall(r'Name = "([A-Za-z.]+)"', core_block))
    assert len(core_names) == 6
    assert referenced == core_names | {"Lab.Kernel", "Core.Sim", "Adapters.Stub"}
    # 源码树存在时走 ProjectReference，不存在时才走 lib/（同 SimRunner 的 Exists 条件分支）。
    assert "Condition=\"Exists('..\\..\\lab\\Lab.Kernel.csproj')\"" in FEELLAB_CSPROJ
    assert "Condition=\"!Exists('..\\..\\lab\\Lab.Kernel.csproj')\"" in FEELLAB_CSPROJ


def test_consumer_smoke_runs_the_dist_feellab_suite() -> None:
    assert "feellab\\bin\\FeelLab.dll" in CONSUMER_SMOKE
    assert re.search(r"Invoke-Step\s+\"[^\"]*feellab", CONSUMER_SMOKE)
