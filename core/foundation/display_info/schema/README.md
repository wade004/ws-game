# `display.map` / `display.anim_set` / `display.equip_visual` 字段表

对应 [01_分层与依赖.md](../../../../architecture/01_分层与依赖.md) L0 模块表 `display_info` 行、
[04_数据与内容管线.md](../../../../architecture/04_数据与内容管线.md) 第 7.1、7.1.1、7.1.2 节。
字段说明摘自 04，具体 `TableSchema` 登记见 `core/DisplaySchemas.cs`。

## `display.map`

公共字段（`sprite`/`model` 两种 `kind` 均适用）：

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `display.<逻辑id去掉domain前缀>` |
| `category` | `skill\|aura\|item\|creature\|gobj\|projectile` | 是 | 逻辑对象类别 |
| `logical_id` | Id | 是 | 指向的逻辑记录 id（技能/光环/物品/生物/物件）；本模块登记为 `FieldKind.Id`，不做跨表引用完整性检查（见模块 README 判断记录 1） |
| `kind` | `sprite\|model` | 是 | 外形类型，决定下方哪组类型专属字段生效 |
| `icon_id` | String | 否 | UI 图标资源引用 |
| `vfx_id` | Optional\<Id\> | 否 | 指向 `vfx.def`；本模块登记为 `FieldKind.Id`（见判断记录 1） |
| `sfx_id` | Optional\<Id\> | 否 | 指向 `sfx.def`；本模块登记为 `FieldKind.Id`（见判断记录 1） |
| `scale` | Number | 否 | 默认缩放，默认 1.0 |
| `shadow` | `none\|blob\|projected` | 否 | 阴影呈现模式，默认 `blob` |
| `sort_offset` | Number | 否 | 同一 sortY 下的排序微调偏移，默认 0 |
| `weapon_style_ref` | Optional\<Id\> | 否 | 指向 `display.weapon_style`（本任务未定义该表）；登记为 `FieldKind.Id`（见判断记录 1） |

`kind: sprite` 型专属字段（schema 层登记为非必填，`kind=sprite` 时必填由
`DisplayKindFieldGroupRule` 校验，见模块 README 判断记录 2）：

| 字段 | 类型 | 必填（业务语义） | 说明 |
|---|---|---|---|
| `sprite_set_id` | String | 是（sprite 型） | 精灵集资源引用（不含路径） |
| `direction_count` | `4\|8\|16` | 是（sprite 型） | 方向量化档位；本模块登记为 `FieldKind.Int`，不做 4/8/16 取值范围校验（见模块 README"不负责什么"） |
| `mirror_pairs` | Optional\<List\<{direction_slot: Id, mirror_of: Id, flip_x: Bool}\>\> | 否 | 方向镜像规则 |
| `paperdoll_layers` | List\<String\> | 否 | 纸娃娃分层引用列表，仅 item/creature 使用 |
| `anchor_points` | Map\<String, Vec2\> | 否 | 挂点定义 |

`kind: model` 型专属字段（schema 层登记为非必填，`kind=model` 时必填由
`DisplayKindFieldGroupRule` 校验）：

| 字段 | 类型 | 必填（业务语义） | 说明 |
|---|---|---|---|
| `model_ref` | Id | 是（model 型） | 三维模型资源引用（不含路径） |
| `anim_set_ref` | Id | 是（model 型） | 指向 `display.anim_set`；本模块自己拥有该表，登记为 `FieldKind.Reference` |
| `sockets` | List\<Id\> | 否 | 该模型声明的挂点 id 列表 |
| `slots` | List\<Id\> | 否 | 该模型声明的可换装槽位 id 列表 |
| `default_slot_meshes` | Map\<Id, Id\> | 否 | 槽位 id 到默认网格资源引用的映射 |
| `material_params` | Map\<String, Number\> | 否 | 材质参数默认值 |

校验规则：`kind` 决定哪组类型专属字段必填、另一组必须留空（"外形类型字段组完整"检查项，
本模块实现为 `DisplayKindFieldGroupRule`）；每个出现在 `display` 域引用集合中的逻辑 id
必须在 `display.map` 中有对应行（"外形映射存在"检查项，本模块实现为
`DisplayMapCoverageRule`，具体覆盖哪些表由调用方注入）。

## `display.anim_set`（04 第 7.1.1 节）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `display.anim_set.<名字>` |
| `clips` | Map\<Id, {resource_ref: Id, events: List\<{name: String, time_pct: Number}\>, blend_ms?: Number}\> | 是 | 剪辑 id 到资源引用与关键帧事件列表的映射；`time_pct` 为剪辑内时间百分比（0~1），`name` 为事件名，经 `IRenderer3D.onAnimEvent` 回调。本模块登记为 `FieldKind.Object`，不逐层展开嵌套结构做类型校验，只检查"存在且是对象" |
| `extends` | Id | 否 | 父姿势集（手感设计/04 第 7 节）；登记为 `FieldKind.Reference` 指向本表，成环由 `AnimSetPoseRule` 报错 |
| `pose_standard` | Bool | 否 | 缺省 false；为 true 或 id 以 `display.anim_set.std_` 开头时按标准姿势清单校验（必备键缺失为错误、推荐键缺失为警告） |
| `clips.<键>.blend_ms` | Number（0..2000） | 否 | 切入本剪辑的交叉淡入时长（毫秒）；缺省 = 调用方默认（`ModelCharacterRig` 取 0.15 秒），显式 0 = 硬切；只对 `model` 型骨骼剪辑生效；有 `extends` 时子集覆盖同名键未声明则沿用被覆盖键的值（模块 README 判断记录 9） |
| `blends` | List\<{from: String, to: String, blend_ms: Number（0..2000）}\> | 否 | 每对键（`from` -> `to`，均为本行合并后 `clips` 里的剪辑键）的切入混合时长，优先级高于逐键 `blend_ms`；沿 `extends` 合并，子集同一对覆盖父集；悬空键与重复对由 `AnimSetBlendRule` 报警告 |

## `display.equip_visual`（04 第 7.1.2 节）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `display.equip_visual.<名字>` |
| `item_id` | Id | 是 | 指向 `item.template`（本任务未定义该表）；登记为 `FieldKind.Id`（见模块 README 判断记录 1） |
| `mode` | `slot_mesh\|socket_attach` | 是 | 呈现方式 |
| `slot_id` | Optional\<Id\> | `mode: slot_mesh` 时必填 | 目标槽位 id，对应 `display.map` 的 `slots` |
| `mesh_ref` | Optional\<Id\> | `mode: slot_mesh` 时必填 | 替换用网格资源引用 |
| `socket_id` | Optional\<Id\> | `mode: socket_attach` 时必填 | 目标挂点 id，对应 `display.map` 的 `sockets` |
| `model_ref` | Optional\<Id\> | `mode: socket_attach` 时必填 | 挂接的独立模型资源引用 |
| `preview_direction` | Optional\<Id\> | 否 | 装备面板预览区使用的方向档（方向槽位 id，`dir.<裸档位名>`，与 `mirror_pairs.direction_slot` 同一套局部 id 空间，不指向已登记表）；缺省取姿势集正面档。纯新增可选字段，不升 schema 版本（手感设计/08 第 4 节，ADR-0123）；装备完整性校验（`import_assets.py equip`）核对它在该游戏声明的方向档里 |

校验规则：`mode` 决定哪组字段必填（"装备呈现字段组条件必填"检查项，本模块实现为
`EquipVisualModeFieldGroupRule`，消费方反馈第 63 条）——该规则只检查"必填"，不检查"另一
`mode` 的字段组是否必须留空"，见模块 README 判断记录 6。

## 本模块不做什么

见 `../README.md`"不负责什么"一节。
