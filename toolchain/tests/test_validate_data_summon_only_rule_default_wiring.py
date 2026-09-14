"""``SpawnSummonOnlyCreatureRule`` 默认接线端到端回归测试（消费方反馈第 44 条根治，2026-09-14，
比照消费方反馈第 34 条 ``DisplayMapCoverageRule`` 先例，见
``architecture/落地计划/消费方反馈-2026-09-14-编辑器-第43-44条.md``）。

背景：此前 ``toolchain/validator``（一次性命令行进程）从不为
``Presentation.Assembly.ContentValidationOptions.CreatureTemplateQuery`` 传参，
``SpawnSummonOnlyCreatureRule`` 因此在这条命令行路径下恒不注册——``spawn.table`` 即便真的引用一条
``summon_only`` 生物模板，``python toolchain/validate_data.py --strict`` 也永远不会报错，是示例数据
门禁的一处真实缺口。根治：``ContentValidationAssembly.CreateRegistryCore`` 未提供
``CreatureTemplateQuery`` 时默认改用 ``Core.Carriers.Creature.RegistryCreatureTemplateQuery``（直接
从已构造的 registry 现读现解析 ``creature.template`` 记录），该规则现默认启用，本工具不需要任何改动
——``toolchain/validator/Program.cs`` 从未显式设置过该选项，走 ``ContentValidationAssembly`` 的新默认
值即可。

本文件真实调用 ``dotnet``（经 ``validate_data.py`` 拉起 ``toolchain/validator``）验证：

1. 未改动的 ``data/_sample``（含消费方反馈第 44 条新增的 ``creature.sample_summon_totem``，本身不被
   ``spawn.table`` 引用）仍是 0 error 0 warning——规则真的在跑，但示例数据集本身干净。
2. 把示例数据集拷到临时目录、往 ``spawn.table.json`` 追加一条引用 ``creature.sample_summon_totem``
   的行后，``validate_data.py --strict`` 退出码非 0，输出含 ``spawn_summon_only_creature``。

不 mock ``subprocess``——同目录 ``test_validate_data_json_output.py`` 等文件测的是 ``validate_data.py``
自身的参数透传/降级逻辑，不需要真实 dotnet；本文件测的正是"真实数据 + 真实 dotnet 编译运行
toolchain/validator 之后，这条规则是否真的生效"这件事本身，mock 掉 dotnet 就测不出来。

运行：``python -m pytest toolchain/tests/test_validate_data_summon_only_rule_default_wiring.py -q``
或作为 ``toolchain`` 套件的一部分：``python -m pytest toolchain/tests -q``。无 ``dotnet`` 可执行文件
的环境下自动跳过（同 ``test_get_framework_with_samples.py`` 等文件的既有约定）。
"""

from __future__ import annotations

import json
import shutil
import subprocess
import sys
from pathlib import Path

import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]

pytestmark = pytest.mark.skipif(
    shutil.which("dotnet") is None, reason="本用例需要真实调用 dotnet 运行 toolchain/validator"
)


def _copy_sample_data(tmp_path: Path) -> Path:
    dest = tmp_path / "_sample"
    shutil.copytree(REPO_ROOT / "data" / "_sample", dest)
    return dest


def _run_validate_data(sample_root: Path) -> subprocess.CompletedProcess[str]:
    # 判断记录：相对路径 "data/_framework" 按调用方当前工作目录解析（见 validate_data.py
    # --data-root 参数帮助文本判断记录），cwd=REPO_ROOT 保证它指向本仓库真实的框架数据根；
    # sample_root 传绝对路径，指向本用例临时拷贝出的示例数据副本，不touch 仓库内真实文件。
    return subprocess.run(
        [
            sys.executable,
            str(REPO_ROOT / "toolchain" / "validate_data.py"),
            "--strict",
            "--data-root", "data/_framework",
            "--data-root", str(sample_root),
        ],
        cwd=REPO_ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=180,
    )


def test_unmodified_sample_data_still_zero_errors_zero_warnings(tmp_path):
    """规则现在真的在跑（不再是 disabled），但示例数据集本身仍然干净——
    ``creature.sample_summon_totem`` 未被任何 ``spawn.table`` 行引用（见消费方反馈第 44 条处理，
    data/README.md "creature 示例数据" 一节判断记录）。"""
    sample_root = _copy_sample_data(tmp_path)

    result = _run_validate_data(sample_root)

    assert result.returncode == 0, result.stdout + result.stderr
    assert "errors 0, warnings 0" in result.stdout
    # 如实确认规则确实已接线（不是恰好因为其它原因侥幸 0 错误）。
    assert "optional rules disabled: none" in result.stdout


def test_spawn_table_referencing_summon_only_creature_fails_gate(tmp_path):
    """往临时拷贝的 spawn.table.json 追加一条违规行（不改动仓库内真实示例数据），断言门禁真的
    会拦下——这正是此前"toolchain/validator 命令行路径下 SpawnSummonOnlyCreatureRule 恒不生效"
    这一缺口的直接回归验证。"""
    sample_root = _copy_sample_data(tmp_path)
    spawn_table_path = sample_root / "spawn" / "spawn.table.json"
    envelope = json.loads(spawn_table_path.read_text(encoding="utf-8"))
    envelope["rows"].append({
        "id": "spawn.sample_totem_violation",
        "map_id": "world.sample_field",
        "content_ref": "creature.sample_summon_totem",
        "position": {"x": 1, "y": 1},
        "facing": 0,
        "respawn_policy": "never",
    })
    spawn_table_path.write_text(json.dumps(envelope, ensure_ascii=False, indent=2), encoding="utf-8")

    result = _run_validate_data(sample_root)

    assert result.returncode != 0, result.stdout + result.stderr
    assert "spawn_summon_only_creature" in result.stdout


if __name__ == "__main__":
    sys.exit(pytest.main([__file__, "-q"]))
