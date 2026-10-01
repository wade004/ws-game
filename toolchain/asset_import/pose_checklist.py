"""标准姿势清单（手感设计/04 第 3 节）的导入校验侧实现。

清单的权威出处是 C# 侧 ``core/foundation/display_info/contracts/PoseChecklist.cs``（必备/推荐/可选三层、
旧键 ``combat_<state>`` 与 ``<state>.combat`` 等价、选入制）；本模块是同一份清单在 ``import_assets.py check``
里的镜像，由 ``toolchain/tests/test_pose_checklist_parity.py`` 逐键对照两侧的清单与规则，改一侧忘改另一侧会被测试拦下。

判断记录：

- **选入制**：只有 ``pose_standard: true`` 的姿势集，或 id 以 ``display.anim_set.std_`` 开头的框架级姿势集
  （04 第 6.3 节）才按清单检查；其余既有姿势集（只有七个状态键、没有 ``move.walk/run``）完全不受影响。
- **继承**：先沿 ``extends`` 链把键并集合并（子集声明的键覆盖同名键，规范键相同即同名），再按清单检查。
  ``extends`` 指向不存在的记录、成环为错误。
- **回落目标**：缺项的回落键从缺失键逐段去尾求第一个已声明的键；``move.sprint`` 先回落到 ``move.run``
  （C# ``PoseRequest.Chain`` 判断记录：没有冲刺剪辑时冲刺按跑步取姿势）。
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Callable, Iterable, Optional

LEGACY_COMBAT_PREFIX = "combat_"
FRAMEWORK_SET_ID_PREFIX = "display.anim_set.std_"

TIER_REQUIRED = "required"
TIER_RECOMMENDED = "recommended"
TIER_OPTIONAL = "optional"

_KEY_SYNTAX = re.compile(r"^[a-z0-9_]+(\.[a-z0-9_]+)*$")


@dataclass(frozen=True)
class ChecklistEntry:
    key: str
    tier: str
    # 非空：只要任一已声明键（规范拼写）满足该判定，本项视为齐全
    any_of: Optional[Callable[[str], bool]] = None
    # 非空：缺失时先从哪个键起求回落目标（缺省从 key 去尾）
    fallback_from: Optional[str] = None
    when_missing: str = "无可回落的键（该状态不播放，维持当前显示）"


_BLEND = "回退为 start_blend_ms/stop_blend_ms 混合"


def _attack_segment(segment: str) -> Callable[[str], bool]:
    return lambda k: k.startswith("attack.") and k.endswith("." + segment)


ENTRIES: tuple[ChecklistEntry, ...] = (
    # 必备
    ChecklistEntry("idle", TIER_REQUIRED),
    ChecklistEntry("move.walk", TIER_REQUIRED),
    ChecklistEntry("move.run", TIER_REQUIRED),
    ChecklistEntry("attack", TIER_REQUIRED),
    ChecklistEntry("hit", TIER_REQUIRED),
    ChecklistEntry("death", TIER_REQUIRED),
    # 推荐
    ChecklistEntry("idle.combat", TIER_RECOMMENDED),
    ChecklistEntry("move.run.combat", TIER_RECOMMENDED),
    ChecklistEntry("attack.02", TIER_RECOMMENDED, any_of=_attack_segment("02")),
    ChecklistEntry("attack.03", TIER_RECOMMENDED, any_of=_attack_segment("03")),
    ChecklistEntry("hit.heavy", TIER_RECOMMENDED),
    ChecklistEntry("hit.knockback", TIER_RECOMMENDED),
    ChecklistEntry("hit.knockdown", TIER_RECOMMENDED),
    ChecklistEntry("hit.getup", TIER_RECOMMENDED),
    ChecklistEntry("cast", TIER_RECOMMENDED),
    ChecklistEntry("dodge", TIER_RECOMMENDED),
    ChecklistEntry("jump", TIER_RECOMMENDED),
    # 可选
    ChecklistEntry("move.sprint", TIER_OPTIONAL, fallback_from="move.run"),
    ChecklistEntry("move.walk.combat", TIER_OPTIONAL),
    ChecklistEntry("move.start", TIER_OPTIONAL, when_missing=_BLEND),
    ChecklistEntry("move.stop", TIER_OPTIONAL, when_missing=_BLEND),
    ChecklistEntry("move.pivot", TIER_OPTIONAL, when_missing=_BLEND),
    ChecklistEntry("hit.launch", TIER_OPTIONAL),
    ChecklistEntry("stunned", TIER_OPTIONAL),
    ChecklistEntry("block", TIER_OPTIONAL),
    ChecklistEntry("wounded", TIER_OPTIONAL, any_of=lambda k: k.endswith(".wounded")),
)


def canonicalize(key: str) -> str:
    """旧键 ``combat_<state>``（<state> 非空且不含点）恒等于 ``<state>.combat``，其余原样返回。"""
    if len(key) > len(LEGACY_COMBAT_PREFIX) and key.startswith(LEGACY_COMBAT_PREFIX):
        rest = key[len(LEGACY_COMBAT_PREFIX):]
        if "." not in rest:
            return rest + ".combat"
    return key


def is_well_formed(key: str) -> bool:
    return bool(_KEY_SYNTAX.match(key))


def _fallback_target(key: str, has: Callable[[str], bool]) -> Optional[str]:
    current = key
    while True:
        if has(current):
            return current
        if "." not in current:
            return None
        current = current.rsplit(".", 1)[0]


@dataclass(frozen=True)
class Finding:
    entry: ChecklistEntry
    fallback_key: Optional[str]

    def describe(self) -> str:
        tier = {TIER_REQUIRED: "必备", TIER_RECOMMENDED: "推荐"}.get(self.entry.tier, "可选")
        tail = f"运行期回落到 {self.fallback_key}" if self.fallback_key else self.entry.when_missing
        return f"缺{tier}键 {self.entry.key}，{tail}"


def evaluate(declared_keys: Iterable[str]) -> dict[str, list[Finding]]:
    """对一组已声明键求缺项：返回 ``{tier: [Finding...]}``（三个键恒在，按清单声明顺序）。"""
    canonical = {canonicalize(k) for k in declared_keys}
    has = canonical.__contains__
    result: dict[str, list[Finding]] = {TIER_REQUIRED: [], TIER_RECOMMENDED: [], TIER_OPTIONAL: []}
    for entry in ENTRIES:
        satisfied = has(entry.key) or (entry.any_of is not None and any(entry.any_of(k) for k in canonical))
        if satisfied:
            continue
        if entry.fallback_from is not None:
            fallback = _fallback_target(entry.fallback_from, has)
        elif "." in entry.key:
            fallback = _fallback_target(entry.key.rsplit(".", 1)[0], has)
        else:
            fallback = None
        result[entry.tier].append(Finding(entry, fallback))
    return result


def applies_to(record_id: str, pose_standard_flag: bool) -> bool:
    """选入制：显式 ``pose_standard: true``，或 id 以框架级前缀开头。"""
    return bool(pose_standard_flag) or record_id.startswith(FRAMEWORK_SET_ID_PREFIX)


def merged_keys(row: dict, rows_by_id: dict[str, dict]) -> tuple[list[str], Optional[str]]:
    """沿 ``extends`` 链合并后的全部声明键；返回 ``(keys, 继承问题描述或 None)``。

    继承问题：指向不存在的记录、成环（含自己 extends 自己）。有问题时 keys 只含已走到的部分。
    """
    keys: list[str] = []
    seen_keys: set[str] = set()
    visited: list[str] = []
    cursor: Optional[dict] = row
    while cursor is not None:
        cid = cursor.get("id", "?")
        if cid in visited:
            if cid != row.get("id", "?"):
                return keys, None  # 环不经过本记录：由环上的记录自己报告（与 C# AnimSetPoseRule 一致）
            return keys, "姿势集不能 extends 自己" if len(visited) == 1 else "姿势集继承成环：" + " -> ".join(visited + [cid])
        visited.append(cid)
        for k in (cursor.get("clips") or {}):
            if k not in seen_keys:
                seen_keys.add(k)
                keys.append(k)
        parent_id = cursor.get("extends")
        if not parent_id:
            break
        cursor = rows_by_id.get(parent_id)
        if cursor is None:
            return keys, f"继承的姿势集 \"{parent_id}\" 不存在"
    return keys, None
