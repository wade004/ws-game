# L0 基础层 · feel 手感档案与解析器

职责：手感体系的数据域与解析器（[手感设计/05](../../../architecture/手感设计/05_手感档案与解析.md)、
[ADR-0113](../../../architecture/adr/0113-手感体系纳入框架判定型与呈现型两半拆分.md)、
[ADR-0118](../../../architecture/adr/0118-手感档案分层解析与字段登记.md)）。

- **数据域 `feel`**：八张表——`feel.preset`、`feel.archetype`、`feel.weapon`、`feel.character`、`feel.action`、
  `feel.calibration`、`feel.motion_mode_rules`、`feel.tag_map`（表结构见 `schema/FeelSchemas.cs`；总索引见
  [04](../../../architecture/04_数据与内容管线.md) 第 1.1 节）。
- **字段登记** `FeelFields.Default`：每个手感字段登记名字、类型、范围（限幅依据）、半属（判定型/呈现型）、
  分组、允许的覆盖操作、合成来源、单位、副手可叠加；元数据类型 `FeelFieldMeta` 落在 `data_registry`（见判断记录 1）。
- **L0 解析器** `FeelResolver`：八层覆盖（预设 → 体型原型 → 标签映射 → 武器 → 角色 → 动作 → 临时状态 → 调试），
  层内 `set → multiply → add → remove`，末尾取整与限幅（限幅记录在案），按单位缓存，单调递增的单位版本号，
  每个字段的溯源链，标定换算（相对量 → 绝对量，毫秒 → tick），动作开始快照，调试覆盖 API。
- **两个只读视图**：`JudgingFeelView`（规则层、模拟层读）与 `PresentingFeelView`（表现层、实验室读），
  各自只能读到自己那一半字段，读另一半抛 `FeelHalfViolationException`。
- **静态校验**：`FeelProfileChecker`（九项检查，数据校验规则与装配共用同一份实现）与三条注册表规则
  （`FeelProfileValidationRule`、`FeelHalfIsolationRule`、`FeelCalibrationRule`）。
- **装配**：`FeelAssembly`（没有手感数据则不装配；有数据而无标定则装配失败，见判断记录 4）。

依赖：`core/foundation/common`（`Id`）、`core/foundation/data_registry`（表登记、校验规则、字段元数据）、
`core/foundation/common/json`（解析数据行）与 .NET 标准库。**不依赖任何上层模块**：实体的体型原型、标签、
装备、动作状态、临时状态、调试覆盖全部经依赖倒置的提供者接口读取（`IFeelBodyProvider` 等），由上层装配方实现。
不使用系统时间、随机数、多线程、反射。

不负责什么：

- 不实现输入缓冲、动作时间线、运动仲裁、局部顿帧、受击裁决、反馈绑定——这些消费解析结果，各在自己的模块（`input_map`、`rules/skill`、`carriers/unit`、`rules/combat`、`presentation/feedback_binder`）。
- 不读单位模板（`creature.template`/`item.template`）：提供者的实现由上层装配方写，本模块只定义读什么。
- 不含任何具体游戏的数值：框架自带两个预设与少量原型/武器是**试调起点**（`maturity=experimental`），
  不是已验证值；标定行属于游戏层，框架默认数据与新游戏模板都不带。

## 目录

| 目录 | 内容 |
|---|---|
| `contracts/` | 字段登记（`FeelFieldDef`、`FeelFieldSet`、`FeelFields`、`FeelFieldNames`）、取值与写入（`FeelValue`、`FeelWrite`、`FeelLayer`、`FeelOp`）、标定（`FeelCalibration`）、解析结果与两个视图（`ResolvedFeel`、`JudgingFeelView`、`PresentingFeelView`）、解析器与提供者接口（`IFeelResolver`、`IFeelJudgingSource`、`IFeelPresentingSource`、`IFeelBodyProvider` 等） |
| `core/` | `FeelProfileSet`（档案集合与数据行解析）、`FeelResolver`、`FeelProfileChecker`、`FeelDebugOverrides`、`FeelAssembly` |
| `schema/` | `FeelSchemas`（八张表登记与规则注册）、`FeelValidationRules` |
| `tests/` | 解析器、标定、快照、字段登记、校验、装配、框架数据、半属隔离与性质测试 |

## 契约（本切片新增，均为纯加法）

| 类型/成员 | 位置 | 说明 |
|---|---|---|
| `FeelFieldMeta`、`FeelHalf`、`FeelGroup`、`FeelOpSet`、`FeelComposition`、`FeelUnit`；`FieldSchema.Feel`/`WithFeel` | `data_registry/contracts` | 登记表的手感元数据（只设一次） |
| 本模块 `contracts/` 全部公开类型 | `feel/contracts` | 见上表 |
| `ActionClass`、`BufferedIntent`、`BufferHoldState`、`BufferDropReason`、`IInputBufferQuery`、`InputBufferDroppedEvent` | `input_map/contracts` | 输入缓冲只读契约（实现随输入缓冲切片） |
| `IActorActionClockQuery`、`IActorActionClockControl` | `sim_loop/contracts` | 行动者动作时钟契约（暂停取大、到期解除、无条件释放；实现随局部顿帧切片） |
| `ActionState`、`ActionPhase`、`ActionCancelReason`、`HitReaction`、`IActionStateQuery` | `rules/common/contracts` | 动作状态只读查询（含无敌窗口） |
| `HitResult.Invulnerable`（追加到枚举末尾） | `rules/common/contracts` | 无敌窗口前置回避，既有成员数值不变 |
| `ActionStartedEvent` 等十个事件类型与 `RulesEventKeys` 十个 key | `rules/common/contracts` | 已登记在 `found.event_catalog`，`EventKeys.g.cs` 由 `toolchain/gen_event_constants.py` 重新生成 |
| 可选新增数据字段 `creature.template.feel_archetype_ref`/`feel_ref`、`item.template.feel_weapon_ref`、`skill.aura_def.feel_modifiers` | 各表 schema | 均为可选，各表 schema 版本不升，既有数据零改动合法 |

## 判断记录

1. **元数据类型放 `data_registry`，字段登记集合放本模块。** `FieldSchema` 属于 `data_registry`，要持有手感元数据；
   `feel` 依赖 `data_registry`（表登记、规则），反过来依赖会成环。所以纯元数据类型落在 `data_registry/contracts`，
   字段登记集合、解析器、校验规则落在本模块。
2. **镜头组字段加 `camera_` 前缀。** 07 的镜头档案 `dead_zone`（呈现型）与 01 输入组的 `dead_zone`（判定型）同名，
   而登记表里字段名必须全局唯一（一个字段只属于一边），所以镜头一侧改名，输入组沿用设计文档的名字。
3. **合成来源的归类**：05 第 3.4 节点名的字段按点名归类；点名之外的字段按"描述行动者自身的操控/承受属性归角色为主，
   描述这把武器打出去的东西归武器为主"归类。`hit_stun_ms` 按设计文档点名归**角色为主**（受击方体型决定硬直），
   所以设计文档第 9 节"单手剑 150 ms / 巨剑 230 ms"的硬直数值**不能**写成武器层写入，放进 `feel.weapon.timeline_reference`
   的 `inflicted_hit_stun_ms`（不分层、不参与解析的参考数值）。`timeline_reference` 这个结构与 `impact_vfx_scale`
   字段是本切片为容纳设计文档数值而补的（设计文档只给了数字没给落点），待设计侧确认。
4. **装配规则**（对应 05 第 7 节）：数据里**没有任何** `feel.*` 行 → 手感系统不装配（`FeelAssemblyResult.IsAssembled`
   为假，不创建解析器、不订阅事件），既有行为逐位不变；**有**手感数据而标定缺项（零行、多行未指定 `CalibrationId`、
   行字段不完整、基础预设不存在）→ 装配失败，抛 `FeelAssemblyException`（`Code` 为 `feel_calibration_missing`）并给出明确诊断，
   不用隐式缺省；档案校验有错误 → 同样失败（`feel_assembly_profile_invalid`，附全部问题）。框架默认数据根与新游戏模板
   都不带标定行，所以框架自带的预设数据放在独立目录 `data/_feel/`（见 5），由需要手感的游戏显式合入，
   不会让没有手感的游戏被迫写标定。
5. **框架手感数据的位置**：`data/_feel/feel/` 下四张表（预设、原型、武器、运动模式规则），不放进 `data/_framework`
   （放进去会让所有游戏的框架数据根含 `feel.*` 行而被迫提供标定）。**校验方式**：`data/_feel` 自带一行缺省标定
   `feel.calibration.framework_default`（试调起点，真实游戏写自己的标定行并在装配时用 `CalibrationId` 指定），所以可以单独校验：
   `python toolchain/validate_data.py --strict --data-root data/_feel`（零错误零警告，门禁步骤 `validate_feel_data`）；
   本模块的测试用 `core/foundation/feel/tests/data` 的单行测试标定行覆盖"有手感数据而标定缺项/多行"等路径。该目录随分发包
   `dist/<version>/data/_feel/` 一起发出，与 `data/_framework` 并列作为框架根，游戏要用把它当作一个数据根合入即可。
6. **相对量单位与标定**：身高倍数 × 参考身高；基础移速倍数与秒级基础移速 × 基础移速；画面高度比例 × 参考镜头高度；
   毫秒按模拟步长换算 tick（四舍五入、远离零取整，非零至少 1，零保持 0）。步长来自装配参数而不是标定表（标定是游戏层
   常量，步长是模拟的固定步长，二者来源不同）。毫秒换算先做 9 位小数预舍入，避免 25 ms ÷ 16.667 ms 这类恰在 .5 边界上的
   浮点误差改变结果。
7. **违规写入的处理**：校验器是第一道门；解析器是第二道，对违规写入跳过并记入 `ResolvedFeel.Diagnostics`，
   不抛异常也不静默生效。
8. **同层重复写的粒度**：按"行"判定——同一行 `writes` 列表对同一字段写两次（不论操作）即错误（`feel_duplicate_write`），
   标签映射与它引用的体型原型同处第 3 层，合并后重复也报错。**不同来源**（主手与副手、多个标签映射、多个光环）在同层写
   同一字段是叠加场景，不算错，按"操作序 → 来源顺序"确定性应用（后应用的 `set` 胜出）。覆盖写入用列表形状正是为了让重复可检测
   （JSON 对象无法承载重复键）。
9. **相对值范围按操作语义**：`set` 的值落在登记范围内；`multiply` 倍数在 0～10；`add` 增量绝对值不超过范围跨度。
   登记范围是限幅依据，不是"唯一合法取值"，多层叠加后越界不是数据错误，由解析器限幅并记录。
10. **"属性已承载"字段**：移动速度倍率（`walk_speed_ratio`、`sprint_speed_ratio`）与三个相位倍率（`phase_scale.*`）标注
    `AttributeBackedReason`，`skill.aura_def.feel_modifiers` 写这些字段报 `feel_modifier_attribute_duplicate`
    （光环改速度走属性，不经手感修饰）。这份名单是本切片的判断，待设计侧确认。
11. **只对已加载数据的表检查半属隔离**：`FeelHalfIsolationRule` 经数据视图遍历表，只扫已加载（有数据）的 `display.*`/`feedback.*`
    表；已登记但没有任何数据行的表不会被扫到（数据视图不暴露全部登记表名）。
12. **提供者倒置与缓存失效**：解析器只读 `IFeelBodyProvider` 等提供者；上层在实体状态变化（换装、加减标签、光环增删、
    调试覆盖变化）时调用 `Invalidate(unit, reason)` 失效该单位缓存，版本号单调递增；`Reload` 换档案集合时清缓存，
    但进行中动作的快照（`BeginAction` 返回的独立结果，以施法实例 id 为键）不变，下一次动作才看到新数据。
13. **第 7 层临时状态精确恢复**：临时状态每次从基础层重新计算，不做"反向乘除"，所以光环加上再移除后每个字段与此前**逐位相等**
    （性质由测试覆盖）。
14. **输入动作表的字段扩展不在本模块**：手感设计/01 第 2.1 节的 `class`/`buffer_ms` 等字段属于 `found.input_action`，
    本模块只落地 `ActionClass` 枚举与缓冲记录契约；字段与缓冲实现见 `core/foundation/input_map/README.md`。
15. **摇杆处理三字段从档案输入组删除（ADR-0143，ADR-0039 授权的破坏性变更）**：`dead_zone`、`response_curve`、`smoothing_ms` 此前在输入组登记却没有任何读取点（消费方是输入映射，输入映射读不到档案）；现登记在 `found.input_action`，归设备与玩家设置。登记集合与 `feel.preset` 里的这三个键一并删除；`FeelFieldNames.DeadZone`/`ResponseCurve`/`SmoothingMs` 常量保留并标 `[Obsolete]`（ABI 只增不删）；仍携带这些键的旧数据表照常加载（容错，不报错，值被忽略）。第 2 条里"镜头一侧改名以避免同名"的理由随之消失，`camera_` 前缀保留（已发布字段名不改）。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 字段登记、八层解析、两个视图、校验、装配、调试覆盖 | 是 | 需要新增手感字段时走变更流程（改字段登记，字段集合是框架契约） |
| 预设、体型原型、武器原型、运动模式规则（试调起点） | 是（`data/_feel/`，实验性） | 自己的预设/原型/武器/角色/动作/标签映射行，以及试玩后把 `maturity` 升为 `validated` |
| 标定行（参考身高、基础移速、基础预设、镜头参考高度等） | 否（结构与校验由框架给出） | 是，每款游戏一行；缺失则装配失败 |
| 提供者接口的实现（体型原型、标签、装备、动作状态、临时状态） | 否（只定义接口） | 上层装配方按自己的实体与装备模型实现 |

## 手感落地 M2-B：热加载入口与层数叠加（2026-10-02）

- **`FeelSystem.TryReload(IDataRegistryView)` -> `FeelReloadResult`（新增）**：重读 `feel.*` 表，要求新数据仍有 `feel.*` 行、本系统的标定行与其基础预设仍在、`FeelProfileChecker.Check` 无问题，才 `FeelResolver.Reload` 换入（`Profiles` 随之指向新集合）；否则拒绝并保持当前档案，原因与校验问题写在结果里（不抛异常、不换入半份数据）。**标定同样热换**（M3-B）：新数据里本系统标定行取值变化时，档案与新标定一并换入（`FeelResolver.Reload(profiles, calibration)`，`FeelSystem.Calibration` 随之指向新标定），结果标 `CalibrationChanged` 但 `Applied` 为真、不再要求重启：此后每次解析与下一个动作的开始快照按新标定换算，进行中的动作沿用其开始时的快照；新标定指定的基础预设不存在时整次拒绝。标定只参与相对量到绝对量的换算与基础预设选择，毫秒到 tick 只取决于模拟步长，所以标定变化不改变任何 tick 数。
- **`FeelWeaponCatalog.Reload()`（新增）**：整体重读 `feel.weapon` 行；已被持有的 `FeelWeaponInfo` 是不可变快照。
- **`FeelTemporaryEntry.Stacks`**：第 7 层数值 `multiply`/`add` 按层数逐次叠加，`set` 与列表操作幂等（见 `core/rules/assembly/README.md` M2-B 节）。
- **空中受击与击飞叠加字段（M4-V，2026-10-03）**：`air_hit_reaction`（可选枚举 `same|none|flinch|stagger_light|stagger|knockback|knockdown`）、`launch_stack`（可选枚举 `restart|add`）、`launch_stack_cap`（可选数值，体型倍数 0..20）注册在 `FeelFields`，缺省均无值（行为与改动前一致）；`air_hit_reaction` 攻击方与受击方档案都可声明（攻击方优先，见 `core/rules/combat/README.md`），`launch_stack`/`launch_stack_cap` 为攻击方判定型字段；语义见 `core/rules/combat/README.md`。
- **空战二期字段（M4-W1b，2026-10-03）**：`air_reaction_cap`（可选枚举，受击方判定型，取值同 `reaction_cap`）、`launch_height_cap`（可选数值 0..20，体型倍数，攻击方/受击方都可声明、取较小者）、`launch_body_scale`（可选数值 0..10，比例，受击方）、`air_stun_until_land`（可选布尔，受击方）、`land_hold_ms`（可选数值 0..2000，毫秒，呈现型）注册在 `FeelFields`，缺省无值（行为与改动前一致）；语义见 `core/rules/combat/README.md` 空战二期节与 `presentation/render/README.md`。测试：`tests/AirAndLaunchStackFieldTests.cs`。测试：`tests/AirAndLaunchStackFieldTests.cs`。
