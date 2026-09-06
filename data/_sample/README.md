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
| `display.map` | `display/display.map.json` | 8 | `sample_hero`/`sample_beast`/`sample_blade`/`sample_chest`/`sample_door`/`sample_save_point`/`sample_bolt`/`sample_loot_pile`（见下"与占位资产的对应关系"） |
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

`assets/_sample/` 下的精灵集/图标/特效/音效均由 `toolchain/import_sample_assets.py` 驱动
`toolchain/import_assets.py` 的 `sprite`/`icon`/`vfx`/`sfx`/`map` 子命令，从 `assets/_placeholder/`
的占位素材（`sample_save_point` 例外，无占位源图，由该脚本用 Pillow 确定性生成）导入而来（见
`toolchain/README.md`"`data/_sample` 的资产来源（import_sample_assets.py）"一节，含完整来源
对应表）；本节只记录 `data/_sample` 表里最终落地的引用字段取值：

| `display.map` 行 | `logical_id` | `sprite_set_id` | `icon_id` | 对应资产目录 |
|---|---|---|---|---|
| `display.map.sample_hero` | `creature.sample_hero` | `sprite.creature.sample_hero` | `icon.creature.sample_hero` | `assets/_sample/sprites/creature_sample_hero/`（8 方向、`body`/`hand_main`/`head` 三层） |
| `display.map.sample_beast` | `creature.sample_beast` | `sprite.creature.sample_beast` | `icon.creature.sample_beast` | `assets/_sample/sprites/creature_sample_beast/`（8 方向，单层 `body`） |
| `display.map.sample_blade` | `item.sample_blade` | `sprite.item.sample_blade` | 无 | `assets/_sample/sprites/item_sample_blade/`（4 方向，单层，源图是一张图标复制成 3 个方向档位） |
| `display.map.sample_bolt` | `projectile.sample_bolt` | `sprite.item.sample_blade`（与 `sample_blade` 共用精灵集） | 无 | 同上 |
| `display.map.sample_chest` | `gobj.sample_chest` | `sprite.gobj.sample_chest` | `icon.gobj.sample_chest` | `assets/_sample/sprites/gobj_sample_chest/`（4 方向，单层，源图 `placeholder_chest/closed.png`） |
| `display.map.sample_loot_pile` | `loot.generic_pile` | `sprite.gobj.sample_chest`（与 `sample_chest` 共用精灵集） | `icon.gobj.sample_chest` | 同上 |
| `display.map.sample_door` | `gobj.sample_door` | `sprite.gobj.sample_door` | `icon.gobj.sample_door` | `assets/_sample/sprites/gobj_sample_door/`（4 方向，单层，源图 `placeholder_door/closed.png`） |
| `display.map.sample_save_point` | `gobj.sample_save_point` | `sprite.gobj.sample_save_point` | `icon.gobj.sample_save_point` | `assets/_sample/sprites/gobj_sample_save_point/`（4 方向，单层，脚本 Pillow 生成的占位立柱图，无占位源） |

判断记录（箱子/门/存档点/剑/弩矢的 `direction_count`）：`display.map` schema 的 sprite 型必填
字段组含 `direction_count`（04 第 7.1 节、`DisplayKindFieldGroupRule`），但这几个物件的占位素材
只有单张原图（箱子/门是"开合两态"的 `closed.png`，剑是一张图标，存档点是脚本生成图），不是天然
的多方向美术——`toolchain/import_sample_assets.py` 把同一张源图复制成 `direction_count=4` 下的
3 个 canonical 档位（`front`/`side_r`/`back`），再经 `sprite` 子命令按 4 方向镜像回填出第 4 个
档位（`side_l`，`--mirror auto` 默认行为，记入 `mirror_pairs`），凑齐最小合法档位数，不代表这些
物件真的有 4 个方向的独立美术；开合状态切换是另一套机制（`gobj.template.type_data`/
`GameObjectHost` 状态机），不在 `display.map` 方向档位字段的表达范围内，这是占位资产本身的设计
取舍，如实记录，不代为修正。

`vfx.def`/`sfx.def` 的 `resource_ref`/`variants` 由 `vfx`/`sfx` 子命令按各自 `--id` 派生（`vfx.
<去掉 domain 前缀并把点号换成下划线>`；`sfx.<同上>_v<N>`，`N` 从 0 开始按源文件传入顺序编号）：

| `resource_ref`/`variants` | 对应资产路径 |
|---|---|
| `vfx.sample_cast_circle` | `assets/_sample/vfx/sample_cast_circle/`（`atlas.png` + `frames.json`，源图 `assets/_placeholder/vfx/cast_circle/frame_00.png`…`frame_07.png`） |
| `vfx.sample_hit_spark` | `assets/_sample/vfx/sample_hit_spark/`（源图 `assets/_placeholder/vfx/hit_spark/`） |
| `vfx.sample_burn` | `assets/_sample/vfx/sample_burn/`（源图 `assets/_placeholder/vfx/burn/`；`--loop`，不写 `lifetime`） |
| `sfx.sample_hit_v0`/`sfx.sample_hit_v1` | `assets/_sample/sfx/sample_hit_v0.wav`/`sample_hit_v1.wav`（`sfx.def.sample_hit` 的 `variants`，源文件 `assets/_placeholder/sfx/hit_01.wav`/`hit_02.wav`） |
| `sfx.sample_cast_v0` | `assets/_sample/sfx/sample_cast_v0.wav`（源文件 `assets/_placeholder/sfx/cast_01.wav`） |
| `sfx.sample_ui_click_v0` | `assets/_sample/sfx/sample_ui_click_v0.wav`（源文件 `assets/_placeholder/sfx/ui_click_01.wav`） |

判断记录（占位资产实际文件组织与 14 第 1.2 节命名模板的差异，如实记录不代为修正）：14 第 1.2 节的
文件名模板是"`<资源引用id去掉类别前缀>[__方向][__层][__帧号].<扩展名>`"（单一扁平文件名）；
`toolchain/gen_placeholder_assets.py`（本任务未改动）实际生成的是"目录按方向/资源名分层、帧序号
用 `frame_NN.png` 命名"的组织方式（如 `sprites/placeholder_hero/front/body.png`、
`vfx/hit_spark/frame_00.png`），两者不是同一套具体文件名拼接规则；本 README 记录的
`resource_ref → 资产路径`对应关系按"资源引用 id 找到对应目录/文件"的语义连接两者，不代表
`toolchain/gen_placeholder_assets.py` 已经实现了 14 第 1.2 节文件名模板本身——具体游戏接入阶段
落地真正的资产导入工具（14 第 11 节）时需要按 14 原文模板重新约定文件名规则，或者更新本条判断
记录改为"占位包也已按 14 模板生成"。`assets/_sample/` 已经是这样一个"真正的资产导入工具"落地的
结果，但沿用的仍是与 `assets/_placeholder` 相同的目录分层组织，按
`<category>_<sprite_set_name>/<direction_slot>/<layer>.png`（`sprite`）、
`<name>/{atlas.png,frames.json}`（`vfx`）、扁平 `<name>_v<N>.wav`（`sfx`）落地，不是 14 原文的单一
扁平文件名模板——`adapters/unity` 侧 `UnityResourceLoader`/`SpriteViewBase.
ResolveLayerResourceId` 对 `layer.<sprite_set_id 去掉 "sprite." 前缀、点号换下划线>__<方向>__<层>`
这个具体资源 id 形态做了专门解析（"加载器 layer 特例"），弥合了两者的差异；`assets/_placeholder`
包本身未变，仍是 `toolchain/gen_placeholder_assets.py` 的原始产出。
