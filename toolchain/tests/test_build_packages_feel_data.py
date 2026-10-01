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


def test_the_lab_dataset_is_still_not_distributed() -> None:
    assert "data\\_lab" not in BUILD
