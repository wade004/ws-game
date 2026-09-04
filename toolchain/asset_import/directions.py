"""方向档位命名与镜像规则，对应 architecture/14_资产规格书模板.md 第 2.1 节（权威）；
镜像规则字段结构（``direction_slot``/``mirror_of``/``flip_x``）与 architecture/09_表现层.md
第 3.2 节镜像规则表一致。

- "canonical"（规范）档位：需要真实美术、不由镜像回填的档位，包含正前方（画面朝下/朝向
  摄像机一侧）与正后方两个位于镜像轴上的档位，以及二者之间"右侧"（原创绘制）的过渡档位；
  数量固定为 ``direction_count // 2 + 1``。
- "mirror"（镜像）档位：由某个 canonical 档位水平翻转回填得到的"左侧"档位，数量固定为
  ``direction_count // 2 - 1``；两者之和等于 ``direction_count``。

8 方向的命名（``front``/``front_side_r``/``front_side_l``/``side_r``/``side_l``/
``back_side_r``/``back_side_l``/``back``）与 ``mirror_of`` 关系（``front_side_l`` 镜像自
``front_side_r``，``side_l`` 镜像自 ``side_r``，``back_side_l`` 镜像自 ``back_side_r``）直接
取自 14 第 2.1 节命名表；4 方向（``front``/``side_r``/``side_l``/``back``，``side_l`` 镜像自
``side_r``）同取自该节；16 方向的具体命名 14 只给出"延伸规则"（在每两个相邻档位族之间插入
过渡档位，命名与镜像方式按同一模式延伸，新增档位 id 由游戏侧登记），本工具按该规则给出一套
具体命名（在 8 方向的 5 个 canonical 档位族之间各插一个过渡档位，右侧过渡档位以 ``_a``/``_b``
后缀区分，见 ``_CANONICAL_NAMES[16]``），属本工具自行约定的扩展（在 ``toolchain/README.md``
追加说明），不改变 04/09/14 已拍板的字段结构。

镜像档位命名规则：把 canonical 档位名中最后一个独立的 ``r`` 分段替换为 ``l``（例如
``front_side_r`` -> ``front_side_l``，``front_side_r_a`` -> ``front_side_l_a``），不再使用
旧版按固定后缀拼接得到镜像档位名的命名方式。
"""

from __future__ import annotations

from .common import AssetImportError

_CANONICAL_NAMES: dict[int, list[str]] = {
    4: ["front", "side_r", "back"],
    8: ["front", "front_side_r", "side_r", "back_side_r", "back"],
    16: [
        "front",
        "front_side_r_a",
        "front_side_r",
        "front_side_r_b",
        "side_r",
        "back_side_r_b",
        "back_side_r",
        "back_side_r_a",
        "back",
    ],
}


def canonical_slot_names(direction_count: int) -> list[str]:
    """返回给定方向档位数量下，需要真实美术的 canonical 档位名列表（含首尾两个镜像轴档位）。"""
    if direction_count not in _CANONICAL_NAMES:
        raise AssetImportError(f"不支持的 --direction-count 取值 {direction_count}（仅支持 4/8/16）")
    return list(_CANONICAL_NAMES[direction_count])


def mirror_slot_name(canonical_name: str) -> str:
    """把 canonical 档位名中最后一个独立的 ``r`` 分段替换为 ``l``，得到对应镜像档位名。"""
    parts = canonical_name.split("_")
    for i in range(len(parts) - 1, -1, -1):
        if parts[i] == "r":
            parts[i] = "l"
            return "_".join(parts)
    raise AssetImportError(f"内部错误：档位名 '{canonical_name}' 不含可镜像的 '_r' 分段")


def mirrorable_slot_names(direction_count: int) -> list[str]:
    """canonical 档位中除首尾（镜像轴上，front/back）外，可被镜像出对侧档位的那些。"""
    canonical = canonical_slot_names(direction_count)
    return canonical[1:-1]


def all_slot_names(direction_count: int) -> list[str]:
    """该方向档位数量下，全部方向档位名（canonical + mirror），长度等于 direction_count。"""
    canonical = canonical_slot_names(direction_count)
    mirrorable = canonical[1:-1]
    mirrors = [mirror_slot_name(n) for n in mirrorable]
    slots = canonical + mirrors
    assert len(slots) == direction_count, (
        f"内部错误：direction_count={direction_count} 但生成了 {len(slots)} 个档位名"
    )
    return slots


def default_direction_slot(direction_count: int) -> str:
    """写入 display.map 的 anchor_points 取用的"默认档位"：canonical 档位表的第一个（front）。"""
    return canonical_slot_names(direction_count)[0]
