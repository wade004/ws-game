"""通用工具：仓库根解析、id 规范、表信封读写与合并写入。

格式约定同 ``data/README.md``：UTF-8 无 BOM、缩进 2 空格、行尾 LF；合并写入已存在的表
文件时按 ``id``（或调用方指定的主键字段）排序，已存在同主键的行整体替换，不存在则新增，
其余行原样保留（见 04 第 2.1 节 id 规范、``data/README.md`` "文件顶层信封"一节）。
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path
from typing import Any

# toolchain/_console.py 是 toolchain/ 顶层模块（不属于本包），供 toolchain/validate_data.py、
# toolchain/gen_event_constants.py 等顶层入口脚本与本包共用同一份 UTF-8 控制台设置逻辑，
# 避免各处各写一份、也避免顶层脚本反过来 import 本包造成循环导入（见该文件头注释）。
# import_assets.py 已把 toolchain/ 目录本身放进 sys.path（"允许直接以 python
# toolchain/import_assets.py 方式运行"），这里兜底再插一次，保证本模块被其他方式导入
# （如未来新增的脚本、单测直接 import asset_import.common）时同样能找到 _console。
_TOOLCHAIN_DIR = Path(__file__).resolve().parent.parent
if str(_TOOLCHAIN_DIR) not in sys.path:
    sys.path.insert(0, str(_TOOLCHAIN_DIR))

from _console import ensure_utf8_stdio  # noqa: E402

# id 格式：见 architecture/04_数据与内容管线.md 第 2.1 节，与
# toolchain/validate_data.py 的 ID_RE 保持一致（唯一权威定义在架构文档，这里复用同一正则
# 字符串常量，不重新发明规则）。
ID_RE = re.compile(r"^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$")

# 方向档位 Id 前缀：见 architecture/14_资产规格书模板.md 第 2.1 节末尾"Id 前缀"勘误结论、
# presentation/common/contracts/DirectionSlots.cs（唯一权威实现，IdPrefix 字段）——运行期方向
# 档位 Id 固定为 "dir.<裸档位名>" 形式（裸名字本身不含点号，不满足 Id 格式，见 ID_RE），
# 文件名/toolchain/asset_import/directions.py/assets/_placeholder/sprites/*/anchors.json 标注
# 文件一律使用不带前缀的裸档位名，前缀只在裸名字流入 Id 类型字段（如
# display.map.mirror_pairs 的 direction_slot/mirror_of）时补上，见 to_direction_slot_id。
DIRECTION_SLOT_ID_PREFIX = "dir."


def to_direction_slot_id(bare_direction_slot_name: str) -> str:
    """把 directions.py 产出的裸方向档位名包装成合法 Id（见 DIRECTION_SLOT_ID_PREFIX 判断记录）；
    只用于写入 display.map 等 Id 类型字段，文件名/内部查表仍使用裸名字，不要在那些场景调用本函数。
    """
    return DIRECTION_SLOT_ID_PREFIX + bare_direction_slot_name


class AssetImportError(Exception):
    """本工具内可预期的用户可见错误（数据/参数问题），main() 捕获后打印并返回码 1。"""


def find_repo_root() -> Path:
    """仓库根 = 本文件所在目录（toolchain/asset_import/）的上两级。"""
    return Path(__file__).resolve().parents[2]


def setup_utf8_streams() -> None:
    """把标准输出/标准错误 reconfigure 成 UTF-8。

    实现已收敛到 ``toolchain/_console.py`` 的 ``ensure_utf8_stdio()``（与
    ``toolchain/validate_data.py``、``toolchain/gen_event_constants.py``、
    ``toolchain/gen_placeholder_assets.py`` 共用同一份逻辑，不再各自维护一份）；本函数保留
    只是不改调用方（``toolchain/asset_import/cli.py``）的调用点。
    """
    ensure_utf8_stdio()


def resolve_root(value: str | None, repo_root: Path, default_name: str) -> Path:
    """把 ``--assets-root``/``--data-root`` 参数解析为绝对路径。

    省略时默认 ``<repo_root>/<default_name>``；传相对路径按仓库根解析；传绝对路径原样使用。
    """
    if value is None:
        return repo_root / default_name
    p = Path(value)
    return p if p.is_absolute() else (repo_root / p)


def validate_id(value: str, domain_prefix: str, field_name: str) -> None:
    """校验 id 字符串格式与 domain 前缀，不合法抛 AssetImportError。"""
    if not isinstance(value, str) or not ID_RE.match(value):
        raise AssetImportError(
            f"字段 {field_name} 取值 '{value}' 不符合 id 格式 "
            f"(^[a-z][a-z0-9_]*(\\.[a-z0-9_]+)+$)，见架构文档 04 第 2.1 节"
        )
    first_segment = value.split(".", 1)[0]
    if first_segment != domain_prefix:
        raise AssetImportError(
            f"字段 {field_name} 取值 '{value}' 的 domain 前缀 '{first_segment}' 应等于 '{domain_prefix}'"
        )


def strip_domain(logical_id: str) -> str:
    """去掉逻辑 id 的 domain 前缀，返回剩余部分（用于拼 display.<...> 等派生 id）。"""
    if "." not in logical_id:
        raise AssetImportError(f"逻辑 id '{logical_id}' 不含 domain 前缀")
    return logical_id.split(".", 1)[1]


def flatten_id_segment(bare_name: str) -> str:
    """把已去掉 domain 前缀的 id 剩余部分编码为扁平文件名/资源引用片段。

    判断记录（TOOL-02）：旧实现直接 ``.replace(".", "_")``，会让"点分段"与"本就带下划线"
    的不同合法 id 归一到同一个物理文件名——例如 ``sfx.fire.hit``（strip_domain 后
    ``fire.hit``）与合法的 ``sfx.fire_hit``（strip_domain 后 ``fire_hit``）都会得到
    ``fire_hit``，第二次导入会静默覆盖第一次写出的音频/图集文件（且 sfx.def/vfx.def 两行
    最终指向同一份物理资源）。

    改法：把点号（段分隔符）替换成双下划线，单个下划线原样保留——与
    presentation/展示层已经在用的"结构分隔用双下划线"口径一致（见 14 §1.2 纸娃娃扁平
    文件名模板 ``<...>__<direction_slot>__<layer_id>``），且不影响绝大多数本就只含单下划线、
    不含点号的既有 id 的落盘文件名。仍有极端情况（id 本身就显式写了双下划线，恰好撞上某个
    点分 id 编码后的结果）不能仅靠编码规则排除，由调用方在写入前另做
    ``check_no_resource_collision`` 兜底，碰撞时报错而不是覆盖。
    """
    return bare_name.replace(".", "__")


def check_no_resource_collision(
    table_path: Path,
    new_id: str,
    new_resource_refs: list[str],
) -> None:
    """写入 sfx.def/vfx.def 前的碰撞防护：不同 id 的记录不得共用同一条物理 resource_ref
    （含 variants）。

    判断记录（TOOL-02）：``flatten_id_segment`` 已让归一化本身不再产生碰撞，但这里作为
    运行期兜底防线保留显式校验——碰撞时报错而不是静默覆盖（旧行为：``merge_write_row``
    按 id 合并，不同 id 各自成行，但两行若指向同一物理文件，后写入的源文件会覆盖先写入的，
    早前写的资源就此损坏且不可见）。同一 id 的重复导入（更新/重跑同一资源）不算碰撞。
    """
    if not table_path.exists():
        return
    existing = read_json(table_path)
    if not isinstance(existing, dict):
        return
    new_refs = set(new_resource_refs)
    for row in existing.get("rows", []):
        row_id = row.get("id")
        if row_id == new_id:
            continue
        row_refs = set(row.get("variants") or [])
        if row.get("resource_ref"):
            row_refs.add(row["resource_ref"])
        collided = row_refs & new_refs
        if collided:
            raise AssetImportError(
                f"resource_ref 碰撞：id '{new_id}' 与既有记录 '{row_id}' 共用同一物理资源引用 "
                f"{sorted(collided)}，请检查 id 是否重复或存在编码冲突后重试（不会覆盖既有资源）"
            )


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def _row_json(row: dict) -> str:
    """把单条记录序列化为紧凑的单行 JSON（与 data/_sample 现有样例的紧凑风格接近）。"""
    return json.dumps(row, ensure_ascii=False, separators=(", ", ": "))


def write_envelope(path: Path, table: str, schema_version: int, rows: list[dict]) -> None:
    """整体写出一张表文件（信封 + rows），2 空格缩进、LF、UTF-8 无 BOM。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    lines = ["{", f'  "table": "{table}",', f'  "schema_version": {schema_version},']
    if not rows:
        lines.append('  "rows": []')
    else:
        lines.append('  "rows": [')
        row_lines = [f"    {_row_json(row)}" for row in rows]
        lines.append(",\n".join(row_lines))
        lines.append("  ]")
    lines.append("}")
    text = "\n".join(lines) + "\n"
    with path.open("w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def merge_write_row(
    path: Path,
    table: str,
    row: dict,
    *,
    key_field: str = "id",
    schema_version: int = 1,
    dry_run: bool = False,
) -> dict:
    """把一行记录合并写入表文件：已存在同主键则替换，否则新增；按主键排序后整体重写。

    文件不存在时以 ``schema_version`` 创建；已存在时沿用文件里记录的 ``schema_version``
    （不擅自升级表级版本号）。``dry_run`` 时不写文件，只返回将要写入的完整行集合信息
    （供调用方打印计划）。
    """
    key = row[key_field]
    if path.exists():
        existing = read_json(path)
        if not isinstance(existing, dict) or "rows" not in existing:
            raise AssetImportError(f"{path} 不是合法的表信封文件（缺少 rows）")
        existing_table = existing.get("table")
        if existing_table != table:
            raise AssetImportError(f"{path} 的 table 字段 '{existing_table}' 与期望的 '{table}' 不一致")
        schema_version = existing.get("schema_version", schema_version)
        rows_by_key = {r[key_field]: r for r in existing.get("rows", []) if key_field in r}
    else:
        rows_by_key = {}

    rows_by_key[key] = row
    sorted_rows = [rows_by_key[k] for k in sorted(rows_by_key)]

    if not dry_run:
        write_envelope(path, table, schema_version, sorted_rows)

    return {"path": path, "table": table, "schema_version": schema_version, "rows": sorted_rows}


def write_json_pretty(path: Path, data: Any, *, dry_run: bool = False) -> None:
    """写辅助 JSON（atlas.json/anchors.json 等）：2 空格缩进、LF、UTF-8 无 BOM。"""
    if dry_run:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
    with path.open("w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def parse_vec2_fraction(spec: str) -> tuple[str, float, float]:
    """解析 ``name=fx,fy`` 形式的默认锚点参数，返回 (name, fx, fy)。"""
    if "=" not in spec:
        raise AssetImportError(f"--anchor-default 取值 '{spec}' 格式应为 name=fx,fy")
    name, coords = spec.split("=", 1)
    parts = coords.split(",")
    if len(parts) != 2:
        raise AssetImportError(f"--anchor-default 取值 '{spec}' 格式应为 name=fx,fy")
    try:
        fx, fy = float(parts[0]), float(parts[1])
    except ValueError as exc:
        raise AssetImportError(f"--anchor-default 取值 '{spec}' 坐标不是合法数字") from exc
    return name.strip(), fx, fy


def parse_hex_color(hex_str: str) -> tuple[int, int, int]:
    """解析 ``#RRGGBB`` 或 ``RRGGBB`` 十六进制颜色为 (r, g, b)。"""
    s = hex_str.strip().lstrip("#")
    if len(s) != 6:
        raise AssetImportError(f"颜色值 '{hex_str}' 应为 #RRGGBB 形式")
    try:
        r = int(s[0:2], 16)
        g = int(s[2:4], 16)
        b = int(s[4:6], 16)
    except ValueError as exc:
        raise AssetImportError(f"颜色值 '{hex_str}' 不是合法十六进制颜色") from exc
    return r, g, b


def log(message: str, *, dry_run: bool = False) -> None:
    prefix = "[dry-run] " if dry_run else ""
    print(f"{prefix}{message}")
