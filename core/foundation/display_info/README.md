# L0 基础层 · display_info 外形注册表

职责：把逻辑 id 映射到表现资源引用（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `display_info` 行、
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 9 节 `DisplayInfoRegistry` 签名、
[04_数据与内容管线.md](../../../architecture/04_数据与内容管线.md) 第 7.1、7.1.1、7.1.2 节）。
本模块拥有三张表的字段说明与 schema 登记：`display.map`（外形映射，sprite/model 两种
`kind`）、`display.anim_set`（动画集，供 model 型驱动骨骼动画）、`display.equip_visual`
（装备外观，供 model 型换装）；提供五条校验规则（`DisplayKindFieldGroupRule`"外形类型字段组
完整"、`DisplayMapCoverageRule`"外形映射存在"、`AnimSetEventsShapeRule`"动画剪辑关键帧事件形状
合法"、`EquipVisualModeFieldGroupRule`"装备呈现字段组条件必填"，均对应 04 第 5 节校验器检查项
清单；`AnimSetPoseRule`"姿势集符合姿势契约"对应手感设计/04 第 3、7、8 节）；姿势维度（手感设计/04，
ADR-0119）的键语法 `PoseKeys`、回落链解析 `PoseResolver`、标准姿势清单 `PoseChecklist` 与 `AnimSetDef`
的 `extends` 继承合并也在本模块；提供 `IDisplayInfoRegistry` 默认实现 `DisplayInfoRegistry`，按 `logical_id` 建索引，供
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
- 不做 `display.equip_visual` 的 `mode` 条件必填"另一组字段必须留空"校验——`mode: slot_mesh`
  时 `slot_id`/`mesh_ref` 必填、`mode: socket_attach` 时 `socket_id`/`model_ref` 必填由
  `EquipVisualModeFieldGroupRule` 校验（消费方反馈第 63 条），但该规则只检查"必填"，不检查
  "另一模式的字段组是否同时出现"——04 第 7.1.2 节与本模块 `schema/README.md` 均未写"另一模式下
  必须留空"，按取舍原则不发明未明确写出的语义，见该规则类型注释判断记录。
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
    AnimSetDef.cs                AnimSetDef（clips/extends 解析与继承合并）、AnimClipDef
    PoseKeys.cs                  姿势键语法（旧键 combat_<state> 等价）、PoseRequest（维度请求与回落链）
    PoseChecklist.cs             标准姿势清单（必备/推荐/可选）与完整性报告
  core/
    DisplaySchemas.cs            Map/AnimSet/EquipVisual 三张 TableSchema
    DisplayKindFieldGroupRule.cs  "外形类型字段组完整"校验规则
    DisplayMapCoverageRule.cs     "外形映射存在"校验规则
    AnimSetEventsShapeRule.cs     "动画剪辑关键帧事件形状合法"校验规则
    EquipVisualModeFieldGroupRule.cs  "装备呈现字段组条件必填"校验规则
    PoseResolver.cs               姿势回落链解析（纯函数）
    AnimSetPoseRule.cs            "姿势集符合姿势契约"校验规则（键语法/extends 无环/标准清单，选入制）
    DisplayInfoRegistry.cs        IDisplayInfoRegistry 默认实现
  schema/
    README.md                    三张表字段说明（摘自 04 第 7.1、7.1.1、7.1.2 节）
  tests/
    DisplayInfoFromRecordTests.cs
    DisplayInfoRegistryTests.cs
    DisplayKindFieldGroupRuleTests.cs
    DisplayMapCoverageRuleTests.cs
    AnimSetEventsShapeRuleTests.cs
    EquipVisualModeFieldGroupRuleTests.cs
    PoseDimensionTests.cs         姿势维度：回落链、旧键等价、extends、标准清单、框架假人姿势集零问题
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

5. **ADR-0019 F1c 子结构登记**：`mirror_pairs`（`[{direction_slot:Id, mirror_of:Id, flip_x:Bool}]`，
   前两者必填、`flip_x` 缺省 `false`）、`paperdoll_layers`（`[String]`）按 `DisplayInfo.
   ParseSpriteInfo` 权威解析登记为 `Item`。`anchor_points`/`default_slot_meshes`/
   `material_params` 三个字段是 `Map<动态键, ...>`（分别为锚点名/槽位 id/参数名）——ADR-0024
   第二批登记（04 第 3.3 节"映射登记"）改为 `MapSchema.FreeKeyed`（三者的键均不指向任何已登记
   表，理由分别见各字段登记处注释）：`anchor_points` 值登记为
   `{parent_layer:String, offset:Vec2, offset_by_direction?:Map<Id,Vec2>}`（与 `DisplayInfo.
   ParseAnchorDef` 逐字段核对一致）；`default_slot_meshes` 值登记为 `FieldKind.Id`；
   `material_params` 值登记为 `FieldKind.Number`。

6. **`EquipVisualModeFieldGroupRule`（消费方反馈第 63 条）只检查"必填"，不检查"互斥留空"**：
   `display.equip_visual.mode` 决定 `slot_id`/`mesh_ref`（`slot_mesh`）或 `socket_id`/
   `model_ref`（`socket_attach`）两组字段中哪一组必填，写法、诊断字段（`Severity`/`Table`/
   `Check`/消息/`RecordKey`/`Field`）与严重级别（`Error`）均沿用 `DisplayKindFieldGroupRule`
   同一模式。取舍与 `DisplayKindFieldGroupRule` 不同的地方：04 第 7.1.2 节与本模块
   `schema/README.md` 对这四个字段只写了"某 `mode` 下必填"，未写"另一 `mode` 下必须留空"
   （不同于 `display.map` 的 sprite/model 字段组，04 第 7.1 节原文明确写了"另一组字段必须
   留空"）；消费方反馈第 63 条本身也只要求补"条件必填"。按"文档未明确写的语义不自行发明"的
   取舍原则，本规则不检查两组字段是否同时出现，`EquipVisualModeFieldGroupRuleTests` 有专项
   用例锁定这一行为（`SlotMeshRecord_WithSocketAttachFieldsAlsoPresent_NoFieldGroupIssues`）。
   若后续要收紧为"互斥留空"，需先在 04/`schema/README.md` 补明确措辞，再扩展本规则或另开
   检查项，不在本次改动范围内。

7. **ADR-0111（消费方反馈第六十一批）：`display.anim_set.clips` 的战斗姿态变体键 `combat_<状态键>` 不新增 schema 字段、不设键白名单**：`clips` 在 schema 里登记为自由键表（`FreeKeyed`），一直没有键白名单，未知键当前的处理是"原样进入 `AnimSetDef.Clips`、不报错"，声明了 `combat_*` 键即生效。`AnimSetDef` 新增常量 `CombatClipKeyPrefix`（`"combat_"`）与静态方法 `CombatClipKey(baseKey)`，前缀全仓库只在这一处定义。`AnimSetEventsShapeRule` 对变体键与基础键一视同仁地检查 `events` 形状。不加白名单的理由：白名单会让任何游戏自己扩展的状态键（如 `jump`）都被拒绝，且变体键拼错（如 `combat_idel`）在运行期表现为"没有变体、回落基础键"，属于美术资源缺失同一口径，交给资源校验而不是 schema。契约面纯加法。

8. **姿势维度（手感设计/04，ADR-0119，手感落地第 1 波 S5）**——`AnimSetDef`/`PoseKeys`/`PoseResolver`/`PoseChecklist`/`AnimSetPoseRule`：
   - **键语法单处定义**：`<state>[.<gait>][.<stance>][.<family>][.<variant>]`；旧键 `combat_<state>` 恒等于 `<state>.combat`（`PoseKeys.Canonicalize`/`TryGetLegacyAlias`），表里两种写法并存时规范写法优先。`peace` 是缺省姿态，键里省略；步态段只在 `move` 状态生成，其它状态请求里带的步态被忽略（因此"请求 attack、run、combat、greatsword、heavy"的回落链是 `attack.combat.greatsword.heavy → …greatsword → attack.combat → attack`）。武器族与变体是自由集合，单看一个键分不出 `idle.2h` 的 `2h` 是族还是变体，所以只提供"结构化请求 → 键"，不提供"键 → 维度"。
   - **`sprint` 先按 `run` 重走一遍再去步态**（设计补漏）：04 把 `move.sprint` 列为可选并说缺项"静默回落"，而必备键只有 `move.walk/move.run`、没有无步态的 `move`——严格按"去步态 → 基础键"，缺冲刺剪辑的姿势集（含框架假人集）冲刺时解析不到任何剪辑。`PoseRequest.Chain` 对 `sprint` 请求在落到基础键前先以 `run` 代替 `sprint` 把同一串候选走一遍；其它步态与旧数据不受影响。
   - **`extends`（加法字段）**：`Reference` 到 `display.anim_set`，`clips` 仍必填（只改个别键的子集写要覆盖的键，可为空对象）；`AnimSetDef.FromRecord(record, registry)` 沿链合并（子覆盖父，按规范键判同名，新旧写法不并存），成环/父集不存在抛 `DataFieldException`（不静默降级）；无 `extends` 的记录结果与单记录 `FromRecord` 逐项一致。校验：自引用与成环为错误（`AnimSetPoseRule`，成环由环上每条记录各报一次），父集不存在由字段引用完整性检查报告。
   - **标准清单是选入制**：新增可选 `pose_standard: Bool`（缺省 false）；`pose_standard: true` 或 id 以 `display.anim_set.std_` 开头（框架级姿势集，04 第 6.3 节）才按清单检查——沿继承链合并后，必备键缺失为错误、推荐键缺失为警告（消息写明运行期回落到哪个键，`move.sprint` 回落到 `move.run`）、可选键静默。理由：既有姿势集（只有 7 个状态键）不声明就完全不受影响，"缺省行为不变"，且默认数据根与模板数据根要求零警告；默认严格级别下警告也会阻断加载，所以键语法警告也只对选入的集生效。
   - **清单项的判定口径**：`attack`（每个武器族一段）按基础键 `attack` 判定，不逐族检查（族是自由集合）；`attack` 二三段按"任一键形如 `attack[.<族>].02/.03`"判定（重武器两段即止）；`wounded` 变体按"任一键以 `.wounded` 结尾"；启停过渡缺失的回落是混合（`start_blend_ms/stop_blend_ms`），不是另一个键。`toolchain/asset_import/pose_checklist.py` 是同一清单在 `import_assets.py check` 里的镜像，`toolchain/tests/test_pose_checklist.py` 对照本侧 `PoseChecklist.cs` 逐项核对。
   - **04 第 8 节其余检查项本版不做**：标记齐全（攻击类 `active_start/active_end/hit`、走/跑 `footstep`、闪避无敌窗口）、循环连续、与 `skill.def.timeline` 一致、每循环位移容差、`sprite` 型原点/方向档数——这些依赖剪辑内容与 `skill.def.timeline` 的抄写工具，属后续切片；本规则只覆盖"键齐全与继承合法"。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| `display.map`/`display.anim_set`/`display.equip_visual` 字段结构与解析 | 是 | 具体外形数据行 |
| "外形类型字段组完整"校验机制 | 是 | 无（规则本身不可配置） |
| "外形映射存在"校验机制 | 是 | 要检查覆盖哪些内容表（构造 `DisplayMapCoverageRule` 时注入） |
| "装备呈现字段组条件必填"校验机制 | 是 | 无（规则本身不可配置） |
| 按 `logical_id`/`category` 查询外形信息 | 是 | 具体使用查询结果的表现层实现（`ViewBinder` 等，L5） |
| `display_info.reloaded` 热重载事件 | 是 | 是否开发期调用 `Reload`、订阅该事件做什么 |

## 空中姿势键 `AirPoseRequest`（2026-10-03，M4-V，ADR-0130 追加决定）

与 `PoseRequest` 并列的结构化请求，键与回落链是固定约定：`jump.rise|jump.fall → jump → idle`、`jump.land → idle`、`hit.air → hit.launch → hit`、`attack.air.<family> → attack.air → attack.<family> → attack`（无武器族时 `attack.air → attack`）。空中键不带姿态/步态/变体维度；链末端（`idle`/`hit`/`attack`）恒作为兜底，不咨询可用性探针。`PoseResolver` 新增 `Resolve(AirPoseRequest, …)`/`TryResolve<T>(AirPoseRequest, …)`，内部与既有解析共用同一个 `ResolveChain`（既有请求的结果逐位不变）。测试：`tests/AirPoseKeyTests.cs`（键存在直接取到、缺失沿链回落、各链末端兜底、武器族与无族、非法阶段）。
