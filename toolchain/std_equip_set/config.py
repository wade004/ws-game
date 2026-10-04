"""占位装备集的物品清单与命名（手感设计/06 第 2 节"占位装备集"、08 第 2 节装备资产包）。

六件装备（单手剑 / 双手巨剑 / 匕首 / 弓 / 法杖 / 胸甲），每件一个完整的 08 资产包：图标 + 纸娃娃层（静态层图
+ 逐层剪辑）+ 外观映射 + 武器表现档案 + ``feel.weapon`` + 音效材质行。数据根 ``data/_equip``，资产落
``assets/_placeholder``。全部几何/像素由代码生成，不使用任何 AI 生图。
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Optional

DATA_DIRNAME = "data/_equip"
ASSETS_DIRNAME = "assets/_placeholder"
SPEC_FILE = "std_equip_set.json"

SKIN_NAME = "default"
SKIN_REF = "skin." + SKIN_NAME

ANIM_SET_ID = "display.anim_set.std_dummy_biped"
DIRECTION_COUNT = 8

LAYER_WEAPON = "hand_main"
LAYER_CHEST = "chest"

ICON_SIZE = 64


@dataclass(frozen=True)
class ItemDef:
    name: str                      # 物品名（不含 item. 前缀），如 std_sword_1h
    kind: str                      # 几何/图标种类：sword_1h / greatsword / dagger / bow / staff / chestplate
    slot: str                      # 槽位名（item.slot.<slot>）
    layer: str                     # 纸娃娃层名（display.equip_visual.slot_id 最后一段）
    family: Optional[str]          # 姿势武器族（姿势集键里的 1h/2h 段）；护甲 None
    weapon_id: Optional[str]       # feel.weapon / display.weapon_style 的同 id 后缀；护甲 None
    quality: str                   # 品质名（item.quality.<quality>）
    material: Optional[str]        # feel.weapon.sfx_material；护甲 None
    zh_name: str
    item_level: int = 1
    preview_direction: Optional[str] = None   # 装备面板预览区方向档（裸档位名）
    behind_directions: tuple = ()             # 逐方向层序：这些方向档（裸档位名）上该层画在身体后面（display.equip_visual.behind_directions）

    @property
    def item_id(self) -> str:
        return "item." + self.name

    @property
    def mesh_ref(self) -> str:
        return "paperdoll.item." + self.name

    @property
    def mesh_stem(self) -> str:
        return "item_" + self.name

    @property
    def icon_id(self) -> str:
        return "icon.item." + self.name

    @property
    def icon_file(self) -> str:
        return f"icons/item/{self.name}.png"

    @property
    def display_map_id(self) -> str:
        return "display.map." + self.name

    @property
    def is_weapon(self) -> bool:
        return self.weapon_id is not None


#: 背面两个方向档（镜像侧由运行期镜像解析到它们）：武器在这些方向上画在身体后面（手在身体另一侧、被身体挡住）。
BACK_DIRECTIONS = ("back_side_r", "back")

ITEMS: tuple[ItemDef, ...] = (
    ItemDef("std_sword_1h", "sword_1h", "std_main_hand", LAYER_WEAPON, "1h", "sword_1h", "std_common", "metal_light",
            "占位单手剑", preview_direction="front_side_r", behind_directions=BACK_DIRECTIONS),
    ItemDef("std_greatsword", "greatsword", "std_main_hand", LAYER_WEAPON, "2h", "greatsword", "std_rare", "metal_heavy",
            "占位双手巨剑", item_level=5, behind_directions=BACK_DIRECTIONS),
    ItemDef("std_dagger", "dagger", "std_main_hand", LAYER_WEAPON, "1h", "dagger", "std_common", "metal_light",
            "占位匕首", behind_directions=BACK_DIRECTIONS),
    ItemDef("std_bow", "bow", "std_main_hand", LAYER_WEAPON, "2h", "bow", "std_common", "wood", "占位弓", behind_directions=BACK_DIRECTIONS),
    ItemDef("std_staff", "staff", "std_main_hand", LAYER_WEAPON, "2h", "staff", "std_common", "wood", "占位法杖", behind_directions=BACK_DIRECTIONS),
    ItemDef("std_chestplate", "chestplate", "std_chest", LAYER_CHEST, None, None, "std_rare", None, "占位胸甲",
            item_level=5),
)

SLOTS = (
    # (槽位名, 中文名, is_weapon, sort_weight, has_armor)
    ("std_main_hand", "主手", True, 1, False),
    ("std_chest", "胸部", False, 11, True),
)
QUALITIES = (
    # (品质名, 中文名, sort_weight, 预算倍率, 词缀数)
    ("std_common", "普通", 1, 1.0, 0),
    ("std_rare", "稀有", 2, 1.5, 1),
)

#: 数据里需要有 sfx.def 行的材质（swing/impact 两层各一行）与它们复用的占位音频（assets/_placeholder/sfx/）。
MATERIALS = (
    # (材质, swing 资源引用, impact 资源引用)
    ("generic", "sfx.swing_01", "sfx.hit_01"),
    ("metal_light", "sfx.swing_01", "sfx.hit_01"),
    ("metal_heavy", "sfx.swing_01", "sfx.hit_02"),
    ("wood", "sfx.swing_01", "sfx.hit_02"),
)
EQUIP_SFX_ID = "sfx.std_equip"
EQUIP_SFX_RESOURCE = "sfx.pickup_01"

#: 装备图标色（填充, 描边）。
PALETTE = {
    "metal": (200, 204, 214, 255),
    "metal_dark": (150, 156, 170, 255),
    "wood": (140, 98, 58, 255),
    "wood_dark": (96, 66, 38, 255),
    "leather": (110, 80, 55, 255),
    "gold": (220, 180, 70, 255),
    "gem": (90, 190, 230, 255),
    "armor": (120, 132, 156, 255),
    "outline": (24, 24, 24, 255),
}


def item_by_name(name: str) -> ItemDef:
    for it in ITEMS:
        if it.name == name:
            return it
    raise KeyError(name)
