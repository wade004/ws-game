# `data/_sample/` 表现层（L5）示例数据说明

本文件只补充表现层（`presentation/assembly/PresentationSchemaCatalog` 新登记的表）示例数据与
`assets/_placeholder/` 占位资产的对应关系；文件组织、信封、主键等通用约定见
[`../README.md`](../README.md)，字段定义见
[`../../architecture/09_表现层.md`](../../architecture/09_表现层.md)、
[`../../architecture/04_数据与内容管线.md`](../../architecture/04_数据与内容管线.md) 第 7 节、
[`../../architecture/14_资产规格书模板.md`](../../architecture/14_资产规格书模板.md)。

## 表现层示例表清单

| 表 | 文件 | 行数 | 说明 |
|---|---|---|---|
| `display.map` | `display/display.map.json` | 5 | `sample_hero`/`sample_beast`/`sample_chest`/`sample_door` 四个逻辑对象 + 既有 `sample_blade`（见下"与占位资产的对应关系"） |
| `vfx.def` | `vfx/vfx.def.json` | 3 | `sample_cast_circle`/`sample_hit_spark`/`sample_burn`，`resource_ref` 对应 `assets/_placeholder/vfx/{cast_circle,hit_spark,burn}/` |
| `sfx.def` | `sfx/sfx.def.json` | 3 | `sample_hit`（含 `hit_01`/`hit_02` 变体）/`sample_cast`/`sample_ui_click` |
| `display.weapon_style` | `display/display.weapon_style.json` | 2 | `sample_sword`/`sample_staff`，`swing_vfx`/`impact_vfx_override` 引用本目录 `vfx.def` 样例行 |
| `feedback.binding` | `feedback/feedback.binding.json` | 3 | 按 `event.is_crit` 分流的暴击/普通伤害飘字规则（09 第 6.1 节示例）+ 一条 `aura.applied` 特效规则 |
| `feedback.floating_text_style` | `feedback/feedback.floating_text_style.json` | 2 | `sample_crit`/`sample_normal` |
| `camera_profile` | `camera/camera_profile.json` | 2 | `sample_default`/`sample_boss_fight` |
| `ui_layout_definition` | `ui/ui_layout_definition.json` | 2 | `sample_hud`/`sample_action_bar`（8 槽位） |
| `shell_menu_definition` | `shell/shell_menu_definition.json` | 1 | `sample_main`，四个菜单项（新游戏/读档/设置/退出） |

`l10n.locale`/`l10n.text` 已在 `l10n/` 目录既有；本次改动补充了 `l10n.creature.sample_hero.name`
一条文本键，供新增的 `creature.sample_hero` 内容行使用（`presentation/assembly/PresentationSchemaCatalog`
把这两张表的 schema 一并登记，见该类型判断记录——此前未被任何 catalog 登记过）。

## 与占位资产的对应关系

`assets/_placeholder/sprites/` 下现成的四套方向精灵集直接供 `display.map` 引用：

| `display.map` 行 | `logical_id` | `sprite_set_id` | 对应资产目录 |
|---|---|---|---|
| `display.map.sample_hero` | `creature.sample_hero`（本次新增，见 `creature/creature.template.json`） | `sprite.placeholder_hero` | `assets/_placeholder/sprites/placeholder_hero/`（8 方向、`body`/`hand_main`/`head` 三层，`mirror_pairs`/`anchor_points` 取自该目录 `anchors.json`） |
| `display.map.sample_beast` | `creature.sample_beast`（既有） | `sprite.placeholder_beast` | `assets/_placeholder/sprites/placeholder_beast/`（8 方向全部原创绘制，`anchors.json` 的 `mirror_pairs` 为空） |
| `display.map.sample_chest` | `gobj.sample_chest`（既有） | `sprite.placeholder_chest` | `assets/_placeholder/sprites/placeholder_chest/`（`closed.png`/`open.png` 开合两态，非方向态） |
| `display.map.sample_door` | `gobj.sample_door`（既有） | `sprite.placeholder_door` | `assets/_placeholder/sprites/placeholder_door/`（同上，开合两态） |

判断记录（箱子/门的 `direction_count`）：`display.map` schema 的 sprite 型必填字段组含
`direction_count`（04 第 7.1 节、`DisplayKindFieldGroupRule`），但箱子/门的占位资产是"开合两态"
而非方向态（见上表，只有 `closed.png`/`open.png` 两张图，没有按方向拆分的子目录）——本次示例数据
按最小合法档位填 `4`，不代表这两个物件真的有 4 个方向的独立美术；开合状态切换是另一套机制
（`gobj.template.type_data`/`GameObjectHost` 状态机），不在 `display.map` 方向档位字段的表达范围内，
这是占位资产包生成脚本（`toolchain/gen_placeholder_assets.py`，本任务未改动）本身的设计取舍，如实
记录，不代为修正。

`vfx.def`/`sfx.def` 的 `resource_ref` 按 14 第 1.2 节"资源引用 id"规则由资产目录/文件名推导（去掉
扩展名、点分格式、类别前缀）：

| `resource_ref` | 对应资产路径 |
|---|---|
| `vfx.cast_circle` | `assets/_placeholder/vfx/cast_circle/`（`frame_00.png`…`frame_07.png` + `atlas.png`/`frames.json`） |
| `vfx.hit_spark` | `assets/_placeholder/vfx/hit_spark/` |
| `vfx.burn` | `assets/_placeholder/vfx/burn/` |
| `sfx.hit_01`/`sfx.hit_02` | `assets/_placeholder/sfx/hit_01.wav`/`hit_02.wav`（`sfx.def.sample_hit` 的 `variants`，14 第 8.1 节"两位数字后缀"变体命名） |
| `sfx.cast_01` | `assets/_placeholder/sfx/cast_01.wav` |
| `sfx.ui_click_01` | `assets/_placeholder/sfx/ui_click_01.wav` |

判断记录（占位资产实际文件组织与 14 第 1.2 节命名模板的差异，如实记录不代为修正）：14 第 1.2 节的
文件名模板是"`<资源引用id去掉类别前缀>[__方向][__层][__帧号].<扩展名>`"（单一扁平文件名）；
`toolchain/gen_placeholder_assets.py`（本任务未改动）实际生成的是"目录按方向/资源名分层、帧序号
用 `frame_NN.png` 命名"的组织方式（如 `sprites/placeholder_hero/front/body.png`、
`vfx/hit_spark/frame_00.png`），两者不是同一套具体文件名拼接规则；本 README 记录的
`resource_ref → 资产路径`对应关系按"资源引用 id 找到对应目录/文件"的语义连接两者，不代表
`toolchain/gen_placeholder_assets.py` 已经实现了 14 第 1.2 节文件名模板本身——具体游戏接入阶段
落地真正的资产导入工具（14 第 11 节）时需要按 14 原文模板重新约定文件名规则，或者更新本条判断
记录改为"占位包也已按 14 模板生成"。
