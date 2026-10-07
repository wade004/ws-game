# L0 基础层 · input_map 输入映射

职责：把物理输入事件（经引擎适配层 `IInput`）转译为游戏动作，提供绑定解析、重绑定、冲突
检测、绑定存储四项通用能力（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `input_map` 行、
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 7 节、第 9 节 `InputMapHost`
签名）。动作集内容（要映射哪些游戏动作）由游戏层通过数据表 `found.input_action` 声明；本模块
不知道、也不关心任何具体游戏定义了哪些动作。

依赖：`core/foundation/common`（`Id`、`Vec2`、`common/json`）、`core/foundation/event_bus`
（`IEvent`、`IEventBus`）、`core/foundation/engine_adapter`（`IInput`、`InputEvent`，L0 依赖
L-1 契约，见 01 依赖矩阵允许项）、`core/foundation/data_registry`（`DataRecord`、
`TableSchema` 等，仅 `ActionDefinition.FromRecord`/`InputActionSchema` 用到）与 .NET 标准库；
不引用任何引擎适配层具体实现、不使用系统时间、不使用多线程、不使用系统级 `Random`、不使用反射。

## 绑定字符串小语法

绑定字符串描述"一个物理输入信号"，由 `BindingParser.Parse` 解析、`ArgumentException` 报告
格式错误。设备侧名字（键名、鼠标按钮名、手柄按钮名、手柄轴名）本模块不限定取值集合，原样
透传给 `IInput`（`InputEvent.Key`、`IInput.GetGamepadAxis` 同样不限定，具体取值由各引擎适配层
实现约定）。

| 语法 | 含义 | 归类 |
|---|---|---|
| `key:<name>` | 键盘按键 | 按下型 |
| `mouse:<button>` | 鼠标按键 | 按下型 |
| `pad:<button>` | 手柄按键 | 按下型 |
| `pad_axis:<axis>` | 手柄一维轴，`axis` 原样传给 `GetGamepadAxis` | 轴型 |
| `pad_stick:<left\|right>` | 手柄摇杆二维轴，取值只能是字面量 `left`/`right` | 轴型 |
| `composite2d:<up>\|<down>\|<left>\|<right>` | 四个按下型子绑定合成的二维轴 | 轴型 |

`composite2d` 的四个子绑定只能是 `key:`/`mouse:`/`pad:` 三种按下型绑定，不能嵌套
`composite2d`/`pad_axis`/`pad_stick`。示例：`composite2d:key:w|key:s|key:a|key:d`。

## 目录

```
input_map/
  README.md
  contracts/
    ActionKind.cs           ActionKind（Button/Axis1D/Axis2D）
    ActionDefinition.cs      ActionDefinition、FromRecord(DataRecord)
    Binding.cs                BindingKind、ParsedBinding、BindingParser
    Events.cs                  InputMapEventKeys、InputActionTriggeredEvent、InputRebindConflictEvent
    IInputMapDiagnostics.cs    IInputMapDiagnostics
    IInputMapHost.cs           IInputMapHost
    InputActionSchema.cs       found.input_action 的 TableSchema
    InputMapOptions.cs         InputMapOptions（GamepadIndex）
    ActionClass.cs / InputRepeatPolicy.cs / ActionClassDefaults.cs   动作类别、重复策略、类别缺省（优先级/转向/是否缓冲）
    BufferedIntent.cs          BufferedIntent、BufferHoldState、BufferDropReason、InputBufferDroppedEvent 相关
    IInputBufferQuery.cs       缓冲查询/取用接口（动作层在取消窗口里调用）
    IInputEdgeSink.cs          按钮边沿汇点（InputMapHost → 缓冲）
    IBufferedIntentSink.cs     tick 处理器的反向接口（动作层决定"此刻接不接受"）
    IGraceQuery.cs / GraceConditionSchema.cs   宽限窗口查询、条件求值接口、found.grace_condition 表
  core/
    InputMapHost.cs             IInputMapHost 默认实现
    InMemoryInputMapDiagnostics.cs
    InputBufferHost.cs          每行动者输入缓冲（槽、覆盖/优先级、按住/点按、重复策略、过期、清空）
    InputBufferTickHandler.cs   tick 步骤 1 处理器（缓冲推进 + 宽限采样 + 可选的推送式取用）
    GraceTracker.cs             宽限窗口追踪
  schema/
    found.input_action.md       字段说明、登记表判断记录
    found.grace_condition.md    宽限条件登记表
  tests/
    BindingParserTests.cs
    InputMapHostTests.cs
    InputBufferTests.cs / InputBufferIntegrationTests.cs / InputBufferTestSupport.cs
```

## 设计要点与判断记录

1. **`found.input_action` 按登记表处理，主键字段名 `key`**：见
   `schema/found.input_action.md`"判断记录"一节——表名 domain 前缀是 `found`，但记录 id 域名
   是 `input`，与 `found.event_catalog` 同理，用登记表机制（`IsRegistryTable=true`，主键字段
   `key`）绕开"domain 前缀须等于表名首段"的一般内容表规则，`data/README.md`"记录主键"一节
   已同步补充这一行。

2. **`IInputMapHost` 在 03 第 9 节签名之外补充 `Update`/`ResetBindings`/`ExportBindings`/
   `ImportBindings`/`GetBindings` 五个方法**：03 第 9 节只给出
   `declareActionSet`/`rebind`/`getConflicts`/`isActionActive`/`getActionAxis` 五个方法签名，
   完全没有规定"谁来把 `IInput.pollEvents()` 转译为动作状态"（`isActionActive`/`getActionAxis`
   显然要读到一份"当前状态"，但该状态从哪里来、什么时候刷新，03 没有给出方法）、也没有规定
   "存储（重绑定结果持久化到设置）"具体是哪个方法。任务书显式拍板这五个方法的签名与语义，
   与 hook_registry 补充 `CallbackCount`、sim_loop 补充 `dt` 字段是同一类"接口签名未逐项排他
   性限定 ⇒ 允许按需要补充"处理，已在此记录供设计层复核。

3. **`IsActionActive` 只适用于 `Button` 动作、`GetActionAxis` 只适用于 `Axis1D`/`Axis2D`
   动作，用错抛 `InvalidOperationException`**：03 第 9 节两个方法的签名本身没有排除"用
   `isActionActive` 查一个轴动作"这种误用；选择让这类误用在调用期立即报错，而不是返回一个
   默认值悄悄放过——这是"契约误用应尽快暴露"的同一类考虑（对照 hook_registry `Register`/
   `Invoke` 判断记录 3）。`Axis1D` 动作的值统一放进 `Vec2.X`、`Y` 恒为 0（03 第 9 节
   `getActionAxis` 签名统一返回 `Vec2`，未按动作维度拆分两个方法，本模块按此约定复用同一
   返回类型）。

4. **手柄索引固定为 `InputMapOptions.GamepadIndex`（默认 0），绑定语法不编码手柄索引**：
   `pad:`/`pad_axis:`/`pad_stick:` 均不携带手柄编号。03/04 均未提及多手柄/多玩家分配规则，
   按"单本地玩家"场景简化为整份动作集固定用同一个手柄，多手柄场景留待后续按需扩展绑定语法
   （如 `pad0:`/`pad1:`）。

5. **`pad_stick:<left|right>` 的 x/y 轴名约定为 `"{left|right}x"`/`"{left|right}y"`**（如
   `pad_stick:left` 经 `GetGamepadAxis(index, "leftx")`/`GetGamepadAxis(index, "lefty")` 取值）：
   02_引擎适配层.md 第 1.5 节与本仓库 `IInput.cs` 均未规定手柄轴名字取值集合，本模块选定这一
   具体约定，供设计层复核是否需要与具体引擎适配层实现的轴命名对齐。

6. **`composite2d` 对角向量归一化到长度 1（任务书拍板）**：单方向按下时原始向量长度已是 1，
   对角同时按下两个正交方向时原始长度是 `sqrt(2)`；统一做"长度非零则归一化到 1"处理，单方向
   情形不受影响、对角情形被压到 1，与拍板结论一致。

7. **动作名（`ActionDefinition.ActionId.Value`）必须跨全部已声明动作集唯一**：03 第 9 节
   `declareActionSet` 只约定"重复声明同一 `actionSetId`"抛异常，未提及"两个不同动作集里出现
   同名动作"是否允许；本模块选择禁止（同一动作名在 `IsActionActive`/`GetActionAxis`/
   `Rebind` 等查询方法里必须无歧义地定位到唯一一个动作状态），声明期直接抛
   `InvalidOperationException` 暴露这类接线错误。

8. **`Rebind` 冲突判定只比较"同一 `RebindGroup`"内的绑定字符串是否逐字相等**：不做设备/
   按键语义层面的等价折叠（例如不认为 `key:a` 与 `key:A`是同一个绑定），保持解析结果与原始
   字符串一致，避免引入本模块无法验证的大小写/别名约定。跨组不冲突（`GetConflicts` 不分组，
   返回值供上层按需自行按组过滤或展示）。

9. **`ExportBindings` 只导出"当前绑定 ≠ 声明时默认绑定"的动作**（任务书拍板"只导出被改过的
   绑定"）：`ActionState.IsDirty` 在 `Rebind`/`ImportBindings` 后重新比较得出，`ResetBindings`
   清除该标记；`ImportBindings` 不做冲突检测——设置文件被视为"此前已通过检测才落盘"的可信
   状态，格式错误（不是数组/元素非字符串/绑定字符串非法/引用未声明动作）仍然抛异常。

10. **FND-08 收口（外部审核 `code-review.md`）：`Button` 动作的"是否触发一次
    `InputActionTriggeredEvent`"按本批次内是否发生过真正的按下边沿判定，不是按"批次首尾两次
    持有状态快照的差值"判定。** 此前 `Update` 先把 `PollEvents()` 整批事件全部应用到
    `_keysDown`/`_mouseDown`/`_padDown` 三个"当前持有"集合，再统一对每个动作算一次
    `active && !CurrentActive`；同一批次内同一个键先 `KeyDown` 后 `KeyUp`（点击类交互在一次
    `Update` 调用内就完成按下与释放时很常见，见外部审核 FND-08、`validation-repros.txt`/
    `validation-boundaries.md` FND08 复现）净效果是"没有变化"，这次真实发生过的按下会被完全
    丢弃，`InputActionTriggeredEvent` 不触发。现改为逐事件维护三个"本批次按下边沿"集合
    （`HashSet<string>.Add` 的返回值就是"这次调用是否发生了 0→1 转变"，天然处理了"同批次内
    先释放后再次按下"应计两次边沿、"同批次内重复 Down 事件（引擎按键连发）"不重复计入两种
    情形），批次末 `CurrentActive` 仍按持有集合的最终状态计算（"当前是否按住"与"是否应该触发
    一次事件"是两个独立问题，不能用同一个布尔值回答）。

11. **ADR-0019 F1c 子结构登记**：`default_bindings` 按 `ActionDefinition.FromRecord` 权威解析
    登记为 `Item`（`FieldKind.String`，元素非字符串抛异常）；"数组至少一项"这条业务判断当时登记层
    表达不了，留在 `ActionDefinition` 构造函数（构造期直接抛异常，不经 `IValidationRule`）。

12. **消费方反馈第 60 条（2026-09-18）：`default_bindings` 最小长度改用 `WithItemCount` 登记**——上
    一条判断记录的前提已被推翻：`InputActionSchema.Table` 的 `default_bindings` 字段新增
    `FieldSchema.WithItemCount(min: 1)` 登记，`DataRegistry` 通用字段校验的 `field_item_count`
    检查项据此在加载期报告"元素数不足"，编辑器/内容工具不必再等到 `ActionDefinition.FromRecord`
    真正构造才发现空数组。`ActionDefinition` 构造函数里的防御性检查予以保留（覆盖绕过
    `DataRegistry` 加载校验、直接调用 `FromRecord` 构造的调用路径，AGENTS.md"运行时路径不静默
    降级"），不是重复诊断——两者分属"加载期批量校验"与"运行时最后一道防线"两个不同阶段。

13. **`ImportBindings` 全量解析、校验通过后再落地，任一条非法整批拒绝**（[ADR-0121](../../../architecture/adr/0121-测试覆盖梳理第一批十条行为语义拍板.md)
    第 8 条，覆盖梳理 D8）：先逐条检查动作存在、值为字符串数组、每条绑定格式合法，全部通过后才统一写入
    `CurrentBindings`/`ParsedBindings`/脏标记；此前逐条边解析边落地，非法项之前的合法项已经生效，留下
    “前半应用”的状态。非法输入抛出的异常类型不变（未知动作 `InvalidOperationException`，形状/格式非法
    `ArgumentException`），区别只是抛出时所有动作的绑定与导入前逐项相等。

## 诊断

`IInputMapDiagnostics`（默认实现 `InMemoryInputMapDiagnostics`）记录：`Rebind` 检测到同组
冲突时的警告文本。不依赖任何引擎适配层接口。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 绑定解析、重绑定、冲突检测、导出/导入的实现机制 | 是 | 具体动作集内容与默认绑定（`found.input_action` 数据行） |
| `found.input_action` 表字段定义与 `FromRecord` 加载入口 | 是 | 具体游戏的动作数据 |
| `input.action_triggered`/`input.rebind_conflict` 事件 | 是 | 订阅这些事件做具体呈现（如设置界面提示冲突） |

## 手感落地 S1：输入缓冲与宽限窗口（手感设计/01 第 2 节，ADR-0115，2026-10-02）

**数据**：`found.input_action` 新增 7 个可选加法字段（`class`/`buffer_ms`/`priority`/`hold_threshold_ms`/`repeat_policy`/
`face_on_accept`/`grace_conditions`，见 `schema/found.input_action.md`）与登记表 `found.grace_condition`。**缺省 = 不进缓冲**：
旧行不写 `class` 时 `ActionDefinition.IsBuffered` 为假，`InputMapHost` 行为与此前逐项相同（有边沿汇点才多做一次逐事件求值）。
`ActionDefinition` 旧 5 参构造保留，新增 12 参构造（ABI 只新增）。

**运行时**（`core/InputBufferHost.cs`，实现 `IInputBufferQuery` 与 `IInputEdgeSink`）：
- 来源：`InputMapHost.SetEdgeSink(buffer)` 后，每个 `Button` 动作的按下/抬起边沿按到达顺序递交（同批次内"按下又抬起"不丢，仍是一次点按）；
  `BindLocalInput(map, actorId, moveActionName)` 把本地输入绑到某个行动者并在按下瞬间截取移动轴方向快照。网络/AI 来源直接 `Press/Release/Submit`。
- 入槽规则（01 第 2.2 节）：同动作在槽内未过期 ⇒ 按 `repeat_policy` 刷新或忽略；同类别互相覆盖；槽满时高优先级替换最低者（`Replaced`），否则新意图被丢（`Full`）。
- 取用：`TryPeek`/`TryConsume(actor, accepts, out intent)`——动作层在取消窗口里传入"当前能否接受"谓词，按优先级降序、同级按先后顺序取第一条可接受记录。
- 过期：行动者动作时钟（`IActorActionClockQuery`，顿帧期间不流逝；没有时钟时退化为模拟 tick）。`ExpiresAt = 入槽读数 + buffer_ms 换算的 tick`，读数 `>` 它即过期，
  发 `InputBufferDroppedEvent`（`Expired`）。`buffer_ms = 0` 只在按下当 tick 有效。`HoldPending`（按下未抬起）不过期，窗口从抬起算。
- 丢弃原因（`BufferDropReason`）：`Replaced`/`Full`/`Expired`/`Cleared`/`Rejected`；`ReportRejected(actor, action, code, timeSolvable)` 由施法管线报告：
  "时间可解"（`ACTION_LOCKED`/`GCD_ACTIVE`/`ON_COOLDOWN`）保留记录继续等，其它原因丢弃并发 `Rejected`（带原因码）。
- 接线：`InputBufferTickHandler.Register(world, buffer, sink, grace)` 挂在步骤 1（输入采集阶段，先于步骤 3 施法管线）：推进缓冲、采样宽限，若给了
  `IBufferedIntentSink`，对每个行动者的待消费记录问"能接受吗"，接受则 `AppendCurrentIntent` 并消费。离散步（`Discrete`）只清空缓冲，不做别的。
  动作时间线切片不用推送式，直接在取消窗口里调 `IInputBufferQuery.TryConsume`。

**宽限窗口**（`core/GraceTracker.cs`，`IGraceQuery`）：每个模拟 tick 对登记的（行动者, 条件）求值并记最近为真的 tick；条件当前为真，或最近为真距今 `<= grace_ms`（手感档案输入组，
换算为 tick）即满足。机制本身不预置条件（框架内置的三条施法瞄点条件见 M4-G 节）；求值经 `IGraceConditionEvaluator`（宿主在行动者上下文里求 `found.grace_condition.expr`）。

### 判断记录

1. **缓冲过期用行动者动作时钟，宽限用模拟 tick**：缓冲描述"玩家这次按键还有效多久"，顿帧中玩家的意图不该白白流逝（00 第 5 节）；宽限描述世界状态（目标是否在射程）的新鲜度，世界不因某个行动者顿帧而停。
2. **验收逻辑留给上层，L0 只提供"拉取"接口**：能否接受依赖动作状态/取消窗口/技能映射（规则层知识），L0 不能向上依赖，所以 `IInputBufferQuery` 暴露带谓词的 `TryConsume`，推送式 tick 处理器经反向接口 `IBufferedIntentSink` 让规则层决定。
3. **`class` 缺省为空 ⇒ 不缓冲**：保证不写新字段的既有数据、既有游戏输入行为完全不变；打开缓冲是数据层逐动作的显式选择。
4. **点按/按住判定的 tick 精度**：阈值换算为 tick 后比较"按下到抬起的动作时钟差"；同批次内按下又抬起视为 0 tick 点按。

### 范围与边界（设计决定，M4 清扫由"已知限制"改写）

- 缓冲与宽限的 tick 处理器只在**连续步**工作；离散步清空缓冲。理由：缓冲窗口以毫秒计、依赖动作时钟的连续推进，手感设计/01 第 3 节的清空规则明文规定"切到离散模式"缓冲整体清空；回合制没有"提前输入"的概念，输入在该回合被取走。
- `GraceTracker` 不内置 Expr 求值：`IGraceConditionEvaluator` 由上层提供，生产装配缺省用 `ExprGraceConditionEvaluator`（`core/carriers/assembly/`，M3-B），游戏可经 `CarriersFeelOptions.GraceEvaluator` 覆盖。理由：L0 不能依赖表达式层（层次约束）。
- `face_on_accept`（转向）只在记录里携带并经 `BufferedIntent.FaceOnAccept` 暴露，实际转向由取用方（动作层）执行：缓冲出口 `BufferedActionIntentSink` 与时间线在取消窗口里的拉取（M4 清扫补齐）都在接受那一刻对齐朝向。理由：转向要写单位朝向，是规则/载体层的职责，L0 不持有单位。
- 缓冲不单独做网络同步与回滚：框架不含网络层；回放走"记录输入再重放"（实验室的输入记录与回放），缓冲状态由重放的输入序列重新算出，不作为独立状态同步。联机游戏的预测与回滚由游戏自己的网络层在输入层之上处理。

## 判断记录（诊断契约统一转发机制，2026-09-19，architecture/adr/0042-诊断契约统一转发到宿主控制台.md）

`InputMapHost` 新增只读属性 `Diagnostics`（返回 `IInputMapDiagnostics`，ABI 只新增只读属性，不改动任何既有公开签名）：
全仓普查发现本模块的诊断契约同仓库另外 20 余个 `I*Diagnostics` 契约一样，此前只记内存
（`InputMapHost` 构造函数未注入自定义实现时默认 `new InMemoryInputMapDiagnostics()`），从不外发到引擎控制台——真实
游戏里出现对应告警时控制台一行输出都没有。本轮由 `adapters/unity` 侧新增的
`Adapter.Unity.Diagnostics.DiagnosticsHub`（注册制轮询集线器，见其类型注释）通过本属性拿到默认
实例引用，登记进 `DiagnosticsHubComposition.RegisterCoreSources`，三个生产装配入口
（`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.GameBootstrap`）每帧轮询转发
一次（恒映射为控制台 Warning，不产生 Error，硬约束见该 ADR）。本模块自身逻辑不变，只是多了一个
对外只读出口。

## 判断记录（新增默认接口成员 `IInputMapHost.GetDeclaredActionNames`，2026-10-01，测试覆盖第二批缺陷 1）

接口此前没有"枚举已声明动作"的入口，上层面板（`presentation/ui` 的 `SettingsViewModel`）只能对未声明的动作名
盲调 `GetBindings` 并撞上 `InvalidOperationException`。新增 C# 8 默认接口成员 `GetDeclaredActionNames()`
（ABI 只新增）：`InputMapHost` 覆盖为声明顺序的新数组快照（永不为 `null`）；默认实现返回 `null`，语义是
"本实现不支持枚举"而不是"没有任何动作"，调用方据此退化。未新增变化事件（声明/重绑定成功不发事件的既有
契约不变），调用方要感知变化就重新调用。用例：`presentation/ui/tests`（`FakeInputMapHost` 覆盖）与
`presentation/assembly/tests/PresentationAssemblyOptionsWiringTests.cs`（真实 `InputMapHost`）。

## 手感落地 S10：`found.input_action.skill_slot`（2026-10-02）

`found.input_action` 新增可选字段 `skill_slot`（String），`ActionDefinition.SkillSlot`/新增 13 参数构造重载（12、5 参数旧重载转发并传 `skillSlot: null`）。生产装配（`BufferedActionIntentSink`/`ActionSlotSkillBinding`，见 `core/carriers/assembly/README.md`）据此把"被缓冲接受的输入动作"翻成对应技能绑定槽位里的技能施放；L0 只提供字段与解析，不读技能绑定（不向上依赖）。

本节追加的判断记录（S10 本节编号）：

1. **加法字段而不是另起映射表**：动作 → 技能映射本来就是输入动作的属性，且玩家换技能绑定时"槽位"是稳定的间接层；新表要多一个装配步骤和一份一致性校验。字段缺省不写即不映射，既有数据零改动。
2. **只存槽位名，不校验槽位是否存在**：槽位是运行时由 `SkillBindingHost.Bind` 建立的，数据层无法知道；未绑定时映射返回 false，记录留在缓冲里直到过期。

## 手感落地 M2-B：宽限条件名并集与本地行动者（2026-10-02）

`InputBufferHost` 新增只读 `GraceConditionNames`（所有已声明缓冲动作的 `grace_conditions` 并集，序数序、去重，`DeclareActions` 后重建）与 `LocalActorId`（`BindLocalInput` 绑定的本地行动者）；`InputBufferTickHandler` 据此在步骤 1 对本地行动者与全部有缓冲的行动者登记并采样宽限条件（此前 `GraceTracker` 在生产里从未登记行动者，宽限查询恒为"没有记录"）。判断记录：**本地行动者在第一次按键之前就开始采样**，否则"条件刚失效"的第一次按键没有历史；非本地行动者经 `RegisterActor`（M3-B，见下节）登记后同样从登记起采样。

## 手感落地 M3-B：行动者登记与宽限剩余量（2026-10-02）

`InputBufferHost.RegisterActor(actorId)`（幂等，建立空缓冲，不产生任何缓冲记录）与 `IsActorRegistered`：非本地行动者不必等到第一次按键才进入宽限采样，生产装配对世界里的 `player`/`creature` 实体自动调用（`CarriersFeelOptions.AutoRegisterGraceActors`，缺省 true），"条件刚失效"的第一次按键同样有历史。`IGraceQuery.RemainingGraceTicks(actorId, conditionId)`（接口缺省成员）：条件当前为真返回 `int.MaxValue`，已失效但在窗口内返回剩余 tick 数（&gt;= 0），不满足返回 -1；`IsSatisfied` 恒等于"剩余量 &gt;= 0"，`IsInGrace` 恒等于"0 &lt;= 剩余量 &lt; `int.MaxValue`"；缺省实现只依赖旧成员（窗口内保守返回 0），旧的第三方查询对象不必改。施法管线用它给排队中的施法记宽限快照（见 `core/rules/skill/README.md` M3-B）。

## 手感落地 M4-G：施法瞄点、内置宽限条件与惰性缓冲（2026-10-03）

- **`GraceAim` / `IGraceAimSink`**（`contracts/GraceAim.cs`）：一次施法请求自己携带的瞄点（单位目标或地面落点，加该技能射程）。`IGraceConditionEvaluator` 新增三个接口缺省成员：三参 `Evaluate(actor, condition, aim)`（缺省转调两参，旧第三方实现不必改）、`UsesAim(condition)`（缺省 false）、`DefaultAimTarget(actor)`（缺省 null）。`GraceTracker` 同时实现 `IGraceAimSink`，由施法管线在带宽限条件的请求进入时 `NoteAim`。
- **`InputBufferHost`** 新增 `ActorBuffersAllocated`（已分配的行动者缓冲数）、`GraceConditionsDeclared`（条件名集合由空变非空时触发的事件）、`ActionsWithGraceCondition(actorId)`（引用宽限条件的动作 id，升序）。

判断记录（本节编号）：

1. **瞄点只作用于依赖目标的条件**：`UsesAim` 为真的条件，历史按瞄点归属——瞄点换成另一个单位/另一个落点，该条件的历史清零并以新瞄点即刻探测一次；与瞄点无关的条件（如 `enemies.nearest_distance`）历史不受影响。理由：同一份"最近为真"历史混用不同目标会让"目标 A 刚在射程内"放行对目标 B 的施法，这是与设计相悖的误放行。
2. **瞄点等于缺省目标（自动攻击目标）时不算换瞄点**：沿用此前以缺省目标采样积累的历史，缺省行为不变。瞄点的寿命取 `max(1, grace_ticks)` 个 tick，过期回落到缺省目标，所以历史只在窗口内有意义。
3. **缓冲惰性分配**：生产装配只在已有动作声明宽限条件时才对世界里的单位登记，没有声明时一个单位都不登记（单位数 N -> 登记 0、缓冲 0）；之后条件被声明（热加载）时再补登记已有单位。显式 `RegisterActor` 的惰性语义见下方 M4-W3 节第 3 条。

用例：`core/foundation/input_map/tests/GraceAimTests.cs`（瞄点优先于缺省目标、历史归属瞄点、窗口边界随 `grace_ms`、旧求值器不受影响）、`core/gameplay/assembly/tests/FeelGraceBuiltinTests.cs`（内置条件、缺省目标回落、缓冲分配数）。

## 手感落地 M4-W3：宽限瞄点与登记的收口（2026-10-03）

M4-G 留下的四条限制逐条收口，没有一条留作"已知限制"：

1. **链式解析出来的目标也记为瞄点（实现）**：`CastPipeline` 在目标链解析出目标之后（请求没带目标、步骤 7 之前）按同一个记录出口 `NoteGraceAim` 报告 `resolvedTargets[0]`——与带显式目标的请求同一套归属规则（换了瞄点依赖目标的条件历史作废，等于缺省目标或上一个瞄点则历史保留）。此前连锁技能/自动选敌的施法只能用缺省目标的历史。复现与不变量：`FeelGraceBuiltinTests.AChainResolvedTarget_IsRecordedAsTheAim_AndGivesTheWindowForThatTarget`（窗口边界 1..grace_ticks 由 `grace_ms` 换算规则算出）、`AChainResolvedTargetThatChanges_DropsTheOldTargetsHistory`。
2. **瞄点只对携带宽限条件的请求记录（设计决定，不是遗漏）**：瞄点是"这次施法请求自己的目标"，只被宽限条件的求值读取；不携带宽限条件的请求没有任何读取方，记录它只会让一次无关的普通施法（例如对另一个单位的普攻）改写行动者的瞄点、作废依赖目标的条件历史，让"没有声明宽限的游戏"的行为与引入宽限前不再逐位一致。理由与"没有声明就不采样"同一条原则：宽限是声明式的，不声明就不产生任何副作用。不变量：`FeelGraceBuiltinTests.ACastWithoutGraceConditions_NeverTouchesTheAimOrTheHistory`（对 A 的带条件施法之后，对 B 的无条件施法前后瞄点与最近为真 tick 都不变）。
3. **显式 `RegisterActor` 同样惰性分配（实现）**：`RegisterActor` 只把行动者 id 记入"已登记"集合（`IsActorRegistered`、`ActorIds` 的口径不变，顺序仍是登记顺序），缓冲对象等到该行动者第一次真有边沿（`Press`/`Release`/`Submit`）才建；`BeginTick` 对没有缓冲的已登记行动者跳过。"显式登记即可采样"语义不变（宽限追踪按已登记行动者采样，不依赖缓冲槽）。因此显式登记 N 个单位 `ActorBuffersAllocated` 仍为 0。`RemoveActor` 同时清登记与缓冲。用例：`GraceRemainingAndRegistrationTests` 的两条惰性登记用例、`FeelGraceBuiltinTests` 的分配数用例（重写为新语义：登记的 id 都在记录里、缓冲只在首个边沿分配）。
4. **无头世界视线（实现，可选）**：见 `core/carriers/assembly/README.md` M4-W3 节（`HeadlessWorldOptions.NavigationLineOfSight`）。

## 手感落地 M4-W4：相机相对控制空间（2026-10-03）

- **`InputControlSpace`**（`contracts/InputControlSpace.cs`）：`World`（缺省）与 `CameraRelative` 两个取值常量与 `IsValid`。`ActionDefinition` 新增可选字段 `ControlSpace`（`found.input_action` 字段 `control_space`，缺省 `world`；新增 14 参构造与 `WithControlSpace`，旧构造保留），只对 `Axis2D` 动作有意义。
- **`InputMapOptions.CameraOrientation`**（`ICameraOrientation`，`core/foundation/engine_adapter/contracts`）：可选相机朝向查询，只有 `YawRadians`（逆时针为正，0 = 屏幕上方是世界 +Y）。声明了 `camera_relative` 动作却没配朝向查询，`DeclareActionSet` 抛 `InvalidOperationException`（不静默当成偏航 0）。
- **换算**：每次 `Update`，`camera_relative` 的 `Axis2D` 动作的轴值 `(x, y)` 旋转成世界方向 `x·右 + y·上`（右 = (cos, sin)，上 = (−sin, cos)），模长不变；偏航恰为 0 时逐位恒等。不声明控制空间的动作、没有配朝向查询的宿主与引入前逐位一致。
- **判断记录**：①朝向走独立的可选接口，不往必选的 `ICamera` 加成员（同 `ICameraImpulse`）；`StubCamera`、`UnityCamera` 都实现它，`PresentationAssembly` 只在相机实现了该接口时给输入映射配朝向。②相机相对输入只依赖偏航：相机俯仰绕右轴转，右轴恒在世界平面上，"屏幕上方"的世界平面投影恒为 (−sin, cos)，所以不需要俯仰。③第三人称游戏的俯仰角、透视由相机适配器自己提供（`UnityCamera` 的 `ApplyPitch`/`Perspective`），输入映射不感知。
- **用例**：`core/foundation/input_map/tests/CameraRelativeControlSpaceTests.cs`（复现：按偏航旋转与期望公式一致；不变量：缺省 world 逐位不变、偏航 0 逐位恒等、没配朝向查询声明期报错、每次更新取样偏航而非声明时取样、取值非法与非 Axis2D 动作声明相机相对被拒绝、`WithControlSpace` 复制其余全部字段、登记表字段往返）。

## 判断记录（永远接不了的记录不挡路，2026-10-03，M4 清扫）

1. **契约（只加不改）**：`IBufferedIntentSink.CanHandle(actorId, record)`（默认接口成员，缺省真）、`IInputBufferQuery.TryPeek(actorId, skip, out)` 与 `TryConsume(actorId, accepts, skip, out)`（默认实现忽略 `skip`，退回无 `skip` 版本）；`InputBufferHost` 覆盖：候选排序前先把 `skip` 判真的记录剔除，`accepts` 只检查剔除后排在最前的那一条。
2. **口径**：优先级语义不变（取最前一条、此刻不能接受就等到过期）；只有出口声明"永远接不了"（`CanHandle` 为假，例如动作没有映射到任何技能）的记录被剔出候选，它们不被消费也不被丢弃，留在缓冲里直到自己的窗口过期，供游戏自己的消费者用无 `skip` 的 `TryPeek`/`TryConsume` 取用。`InputBufferTickHandler` 对每个行动者把 `CanHandle` 当 `skip` 谓词传入。
3. **理由**：此前一条未映射的高优先级记录会在过期前挡住优先级更低的可接受记录（例如绑了闪避键却没配闪避技能，攻击被挡住）；"此刻不能接受"（冷却、动作锁）与"永远接不了"是两回事，前者按设计等待、后者不该占着队首。测试：`tests/InputBufferUnhandledSkipTests.cs`。

## 手感落地 M5-S1：输入层补全（2026-10-04，[ADR-0143](../../../architecture/adr/0143-输入层补全.md)）

1. **摇杆处理有了消费方（评审 A1）**：`dead_zone`、`response_curve`、`smoothing_ms` 登记在 `found.input_action`，由 `InputMapHost` 消费（`AxisProcessing`）。径向死区把超出部分重标度到 0～1；`expo` 是幅值平方，`custom:<id>` 经 `InputMapOptions.CurveResolver` 取分段线性曲线（解析不到在声明动作集时抛出）；平滑只作用于**下降沿**（线性，每 tick 回落 `步长 / 平滑时间`），上升与方向变化即时，所以一次按下至少一个 tick 满幅；只作用于模拟绑定（`pad_stick`/`pad_axis`），键盘 `composite2d` 是数字输入不处理。未声明时输出与原始值逐位一致。玩家覆盖：`SetAxisProcessing`/`GetAxisProcessing`/`ExportAxisSettings`/`ImportAxisSettings`，整批校验、任一项非法整批拒绝并保留原覆盖；`ShellHost` 在设置文件里读写键 `input_axis_settings`（无覆盖不写出）。破坏性变更（ADR-0039 授权）：手感档案输入组的同名三字段删除，见 `core/foundation/feel/README.md` 判断记录 15。
2. **`jump` 输入类别（评审 B2）**：`ActionClass.Jump`（追加在枚举末尾），缺省优先级 35，无按住阈值。接受不是施法：装配层的缓冲出口对 `jump` 动作直接驱动竖直轴；起不了跳的记录留在缓冲里到过期，这就是跳跃缓冲。`ActionDefinition` 新增 17 参数构造重载（旧重载保留，新增 `axisProcessing`、`holdSkillSlot`、`jumpCutRatio`），`jump_cut_ratio` 只对 `jump` 类合法。
3. **蓄力补全（评审 A4、B5）**：新增 `IChargeRuleSource`/`ChargeRule(MinTicks, MaxTicks, CancelBelowMin)`，`InputBufferHost.ChargeRules` 为空时一切与改动前逐位一致。`HoldPending` 记录按行动者动作时钟存续满 `MaxTicks` 就自动转为 `HoldReleased`（`HeldTicks = MaxTicks`）并发 `input.charge_ready{actorId, actionId, heldTicks}`；释放时按住超过上限同样夹在上限；`CancelBelowMin` 为真时不足下限的释放以 `BufferDropReason.ChargeBelowMin`（追加在枚举末尾）丢弃。设计里"`charge_ready` 是时间线标记"与"蓄力期间没有动作实例"自相矛盾，按后者定稿：它是缓冲侧事件。
4. **按住状态可查与释放通知**：`IInputBufferQuery.IsHeld(actorId, actionId)`（默认接口成员，缺省假）；`InputBufferHost` 在抬起时通知 `ActionReleased`（装配层据此做可变跳高与维持动作的松键判定）。`Clear` 会重置按住状态：被清缓冲的行动者此后的松键不再通知。
5. **`BufferedIntent` 新增 `SkillOverride`/`ExtraArgs`**：合成动作（AI 经缓冲）与点按/按住变体借此携带技能与附加参数，不改既有字段；`IBufferedIntentSink.ProducesIntent`（默认真）让起跳这类不产生施法意图的出口声明自己，缓冲据此不对它做"一个 tick 至多一条动作类意图"的占位。
6. **`BufferedActionIntentSink` 的点按/按住变体**：`HoldReleased` 记录用 `hold_skill_slot`，`Tap` 用 `skill_slot`；按住记录不被"武器的攻击技能"覆盖（`WeaponPreferredActionBinding` 对覆盖与按住槽位旁路）。分界只有 `hold_threshold_ms` 一个。
7. **设计边界（不是遗漏）**：平滑不做上升沿加速度；`custom:<id>` 曲线的输入输出都是 0～1 幅值，方向不变；轴处理不覆盖键盘绑定。
8. **复现与不变量**：`tests/AxisProcessingTests.cs`（恒等逐位一致、死区重标度、曲线、平滑下降沿与"不吞单 tick 按下"、玩家覆盖往返、整批拒绝）、`tests/InputBufferChargeAndHeldTests.cs`（自动释放 tick = 按下 tick + `MaxTicks`、`charge_ready` 恰好一次、夹上限、`below_min` 取消、无规则源时逐位不变、`Clear` 后状态）。

## 判断记录（本地输入绑定不随实体销毁解除；二维轴多绑定求值，2026-10-06，消费方反馈 P2 缺口 6、4）

1. **本地绑定保留**：`BindLocalInput` 是"这套输入属于哪个行动者 id"的角色声明，不是对一次实体生命周期的引用。地图切换会清场（`entity.destroyed` → `RemoveActor`），游戏随后用同一个 id 把玩家加回世界；此前 `RemoveActor` 顺手把 `_localActor` 置空，重新加回的玩家再也收不到按钮边沿。现保留绑定，缓冲对象照旧释放。用例见 `InputBufferIntegrationTests.BindLocalInput_SurvivesActorDestroyAndRecreate_EdgesStillReachTheBuffer`。
2. **二维轴多绑定**：`EvaluateAxis2D` 按列出顺序逐条求值，第一条求值非零的 `composite2d`/`pad_stick` 绑定胜出，全零返回零；摇杆绑定先按本动作的死区/曲线/平滑处理再判断非零。只有一条轴型绑定时与此前逐位相同；同一动作两条摇杆绑定时，平滑状态只随被求值到的那条推进。用例 `AxisDeviceMergeTests`。

## 判断记录（输入上下文栈，2026-10-07，消费方反馈样板游戏 A 备忘 8）

模态界面期间挡掉移动与战斗的通路：`IInputMapHost.PushInputContext(contextId, allowedActions)` / `PopInputContext(contextId)` / `ActiveInputContext`（C# 8 默认接口成员，ABI 只新增；`InputMapHost` 覆盖）。
按**动作**挡而不是按物理键挡：栈顶上下文放行表之外的动作视为未激活（按钮不激活、不触发 `InputActionTriggeredEvent`、不产生边沿；轴输出零）；栈顶独占，弹出后下一层恢复；
同 id 重复压入只更新放行表；允许乱序弹出。物理按键状态照常跟踪：压入时按住的键给边沿接收端补"抬起"；弹出时仍按住的键只恢复"持有"、不补"按下"（关界面不会凭空打出一刀）。
放行表里没声明过的动作名记诊断并忽略。用例：`tests/InputContextStackTests.cs`（7 条）。界面侧用法见 `presentation/ui` 的 `UiModalInputContext`。
