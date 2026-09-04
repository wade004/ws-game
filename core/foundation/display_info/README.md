# L0 基础层 · display_info 外形注册表

职责：把逻辑 id 映射到表现资源引用（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `display_info` 行、
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 9 节 `DisplayInfoRegistry` 签名、
[04_数据与内容管线.md](../../../architecture/04_数据与内容管线.md) 第 7.1、7.1.1、7.1.2 节）。
本模块拥有三张表的字段说明与 schema 登记：`display.map`（外形映射，sprite/model 两种
`kind`）、`display.anim_set`（动画集，供 model 型驱动骨骼动画）、`display.equip_visual`
（装备外观，供 model 型换装）；提供两条校验规则（`DisplayKindFieldGroupRule`"外形类型字段组
完整"、`DisplayMapCoverageRule`"外形映射存在"，均对应 04 第 5 节校验器检查项清单）；提供
`IDisplayInfoRegistry` 默认实现 `DisplayInfoRegistry`，按 `logical_id` 建索引，供
`ViewBinder`（L5）等按逻辑 id 或类别查询外形信息。

依赖：`core/foundation/common`（`Id`、`Vec2`、`Common.Json` 用于解析
`display.map` 的嵌套结构字段）、`core/foundation/data_registry`（`TableSchema`、
`FieldSchema`、`DataRecord`、`IValidationRule`、`IDataRegistryView`）、
`core/foundation/event_bus`（`IEvent`、`IEventBus`，`Reload` 发出
`display_info.reloaded`）与 .NET 标准库；不引用任何引擎适配层实现、不使用系统时间、
不使用多线程、不使用反射、不使用系统级 `Random`。

不负责什么：

- 不做数据文件的实际读取/JSON 解析——那是 `IDataRegistry`（T1-4）的职责，本模块只登记
  `TableSchema` 供其加载，自身经 `IDataRegistryView` 只读查询已加载的记录。
- 不知道"哪些内容表的哪些字段属于 display 域引用集合"——`DisplayMapCoverageRule` 的
  "要检查覆盖哪些表"由调用方经构造函数注入，本模块不预设 `skill.def`/`creature.template`
  一类具体内容表。
- 不做 `display.equip_visual` 的 `mode` 条件必填校验（`mode: slot_mesh` 时 `slot_id`/
  `mesh_ref` 必填、`mode: socket_attach` 时 `socket_id`/`model_ref` 必填）——任务书只点名
  `DisplayKindFieldGroupRule`/`DisplayMapCoverageRule` 两条规则，`equip_visual` 的条件必填
  留给后续任务按同一模式（独立 `IValidationRule`）扩展。
- 不做 `direction_count` 取值必须是 4/8/16 的范围校验——04 第 7.1 节把它列在 sprite 型
  字段表里但未列入第 5 节校验器检查项清单，本模块只做"字段组完整性"（必填/留空），不做
  具体取值范围校验。
- 不做资源实际存在性校验（`sprite_set_id`/`model_ref` 等指向的资源文件是否真的存在）——
  那是资产导入工具（11 第 2.1 节）的职责。

## 目录

```
display_info/
  README.md
  contracts/
    Enums.cs                    DisplayKind、DisplayCategory、ShadowMode
    SpriteInfo.cs                SpriteInfo、MirrorPair
    ModelInfo.cs                 ModelInfo
    DisplayInfo.cs                DisplayInfo（FromRecord）
    IDisplayInfoRegistry.cs      IDisplayInfoRegistry
    Events.cs                    DisplayInfoEventKeys、DisplayInfoReloadedEvent
  core/
    DisplaySchemas.cs            Map/AnimSet/EquipVisual 三张 TableSchema
    DisplayKindFieldGroupRule.cs  "外形类型字段组完整"校验规则
    DisplayMapCoverageRule.cs     "外形映射存在"校验规则
    DisplayInfoRegistry.cs        IDisplayInfoRegistry 默认实现
  schema/
    README.md                    三张表字段说明（摘自 04 第 7.1、7.1.1、7.1.2 节）
  tests/
    DisplayInfoFromRecordTests.cs
    DisplayInfoRegistryTests.cs
    DisplayKindFieldGroupRuleTests.cs
    DisplayMapCoverageRuleTests.cs
```

## 设计要点与判断记录

1. **`logical_id`/`vfx_id`/`sfx_id`/`weapon_style_ref`/`equip_visual.item_id` 用
   `FieldKind.Id` 而非 `FieldKind.Reference`**：这几个字段指向的表（技能/光环/物品/生物/物件
   模板表、`vfx.def`、`sfx.def`、`display.weapon_style`、`item.template`）本任务均未定义，
   也不在本模块独立跑校验时的典型数据集里加载；若声明为 `Reference`，`display_info` 模块
   单独跑数据校验会因为这些表未加载而报出虚假的"引用完整性"错误。任务书就 `logical_id`/
   `weapon_style_ref` 显式拍板用 `Id`；`vfx_id`/`sfx_id`/`item_id` 按"同理"套用同一判断，
   已在 `DisplaySchemas` 类型注释记录。`anim_set_ref` 指向的 `display.anim_set` 是本模块
   自己拥有并登记的表，因此按 04 原文用 `Reference`。

2. **sprite/model 专属字段在 `TableSchema` 里一律登记为 `Required: false`**：`kind` 决定
   哪组字段必填是"条件必填"，`FieldSchema.Required` 只能表达无条件必填/可选，因此专属字段
   组的完整性检查完全交给 `DisplayKindFieldGroupRule`（`IValidationRule` 扩展点），schema
   层只声明类型，不声明是否必填（详见 `DisplaySchemas.Map` 类型注释）。

3. **`DisplayInfo`/`SpriteInfo`/`ModelInfo` 不可变、`FromRecord` 假设记录已通过校验**：
   `FromRecord` 只做"按 `kind` 读取对应字段组"的解析，不重复做字段组完整性校验；调用方应
   先经 `DataRegistry.LoadAll`/`Validate`（含 `DisplayKindFieldGroupRule`）确认数据合法，
   再调用 `FromRecord`（或经 `DisplayInfoRegistry` 间接调用）。对已通过校验的 `model` 型
   记录调用 `FromRecord` 时若仍缺失 `model_ref`/`anim_set_ref`，会按 `DataRecord` 惯例抛
   `DataFieldException`——这是"数据没有真的通过校验就被使用"的编程错误信号，不是本模块
   需要静默兜底的正常分支。

4. **`DisplayInfoRegistry` 索引键是 `logical_id`，不是 `display.map` 行自身的 `id`**：
   `Lookup(logicalId)` 的语义是"某个逻辑对象（技能/生物/物品等）应该长什么样"，查询轴是
   它所指向的逻辑记录 id，与 `display.map` 行自己的 id（`display.<name>`）是两个不同的轴。
   同一 `logical_id` 出现在多条 `display.map` 记录里视为装配期错误，构造/`Reload` 时直接
   抛 `InvalidOperationException`（与 `hook_registry.Register` 对"接线错误尽快暴露"同一
   惯例），不是静默取后一条覆盖前一条。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| `display.map`/`display.anim_set`/`display.equip_visual` 字段结构与解析 | 是 | 具体外形数据行 |
| "外形类型字段组完整"校验机制 | 是 | 无（规则本身不可配置） |
| "外形映射存在"校验机制 | 是 | 要检查覆盖哪些内容表（构造 `DisplayMapCoverageRule` 时注入） |
| 按 `logical_id`/`category` 查询外形信息 | 是 | 具体使用查询结果的表现层实现（`ViewBinder` 等，L5） |
| `display_info.reloaded` 热重载事件 | 是 | 是否开发期调用 `Reload`、订阅该事件做什么 |
