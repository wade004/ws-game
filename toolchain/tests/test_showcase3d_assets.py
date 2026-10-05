"""3D 演示场景模型包的入库检查（ADR-0158，``assets/_showcase/models`` + ``data/_showcase_3d``）。

不变量（读仓库里真实提交的文件，不 mock）：

1. 规格里声明的每个 FBX、每张贴图都在库里、非空，FBX 是二进制 FBX（文件头 ``Kaydara FBX Binary``）；
2. 四个角色是四个不同的 FBX 与不同的贴图（英雄 / 杂兵 / 精英 / 木桩互不相同）；
3. 数据行 ``display.anim_set.show3d_*`` 每一行的每个剪辑引用都指向规格声明过的剪辑（``anim.<模型>_<剪辑>``），不引用库里没有的剪辑；
4. 展示行与规格由 ``build_data_3d.py`` 同源生成：``--check`` 通过（提交的行没有被手改）；
5. 来源说明 ``SOURCE.md`` 与随包 ``License.txt`` 存在，且声明 CC0；
6. 规格里的已知限制清单非空（包里缺的状态必须登记，不静默）。

运行：``python -m pytest toolchain/tests/test_showcase3d_assets.py -q``。
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MODELS = REPO / "assets" / "_showcase" / "models"
SPEC = MODELS / "showcase3d_models.json"
ROWS = REPO / "data" / "_showcase_3d" / "display"
BUILDER = REPO / "assets" / "_showcase" / "_source" / "build_data_3d.py"


def _spec() -> dict:
    return json.loads(SPEC.read_text(encoding="utf-8"))


def test_every_declared_fbx_and_texture_is_committed_and_nonempty() -> None:
    spec = _spec()
    assert len(spec["models"]) == 4
    for model in spec["models"]:
        fbx = MODELS / model["fbx"]
        assert fbx.is_file() and fbx.stat().st_size > 10_000, fbx
        assert fbx.read_bytes()[:18] == b"Kaydara FBX Binary", f"{fbx} 不是二进制 FBX"
        assert model["materials"], model["id"]
        for material in model["materials"]:
            texture = MODELS / material["texture"]
            assert texture.is_file() and texture.stat().st_size > 1_000, texture
    assert (MODELS / spec["shader"]).is_file()


def test_four_characters_use_distinct_models_and_textures() -> None:
    models = _spec()["models"]
    assert len({m["fbx"] for m in models}) == 4
    textures = [mat["texture"] for m in models for mat in m["materials"]]
    assert len(textures) == len(set(textures))


def test_display_rows_reference_only_declared_clips() -> None:
    spec = _spec()
    declared: set[str] = set()
    for model in spec["models"]:
        for clip in model["clips"]:
            declared.add(f"anim.{clip['id']}")
    anim_rows = json.loads((ROWS / "display.anim_set.json").read_text(encoding="utf-8"))["rows"]
    assert {r["id"] for r in anim_rows} == {f"display.anim_set.{m['id']}" for m in spec["models"]}
    referenced = {c["resource_ref"] for r in anim_rows for c in r["clips"].values()}
    assert referenced, "展示行没有引用任何剪辑"
    missing = referenced - declared
    assert not missing, f"展示行引用了规格里没有的剪辑：{sorted(missing)}"
    weapon = json.loads((ROWS / "display.weapon_style.json").read_text(encoding="utf-8"))["rows"][0]
    for skill, ref in weapon["cast_anim_override"].items():
        assert ref in declared, f"{skill} -> {ref} 不在规格里"


def test_rows_and_spec_are_in_sync_with_generator() -> None:
    result = subprocess.run([sys.executable, str(BUILDER), "--check"], capture_output=True, text=True, encoding="utf-8")
    assert result.returncode == 0, result.stdout + result.stderr


def test_license_and_source_notes_present_and_cc0() -> None:
    source = (MODELS / "SOURCE.md").read_text(encoding="utf-8")
    assert "CC0" in source and "quaternius.com" in source
    license_text = (MODELS / "quaternius_rpg" / "License.txt").read_text(encoding="utf-8", errors="replace")
    assert "CC0" in license_text or "Creative Commons" in license_text or "public domain" in license_text.lower()


def test_pack_limitations_are_declared_not_silent() -> None:
    limitations = _spec()["limitations"]
    assert len(limitations) >= 4
    joined = "\n".join(limitations)
    for word in ("击退", "击倒", "闪避"):
        assert word in joined, f"限制清单没有登记「{word}」"
