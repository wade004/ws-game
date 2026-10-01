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
换算为 tick）即满足。机制不预置条件；求值经 `IGraceConditionEvaluator`（宿主在行动者上下文里求 `found.grace_condition.expr`）。

### 判断记录

1. **缓冲过期用行动者动作时钟，宽限用模拟 tick**：缓冲描述"玩家这次按键还有效多久"，顿帧中玩家的意图不该白白流逝（00 第 5 节）；宽限描述世界状态（目标是否在射程）的新鲜度，世界不因某个行动者顿帧而停。
2. **验收逻辑留给上层，L0 只提供"拉取"接口**：能否接受依赖动作状态/取消窗口/技能映射（规则层知识），L0 不能向上依赖，所以 `IInputBufferQuery` 暴露带谓词的 `TryConsume`，推送式 tick 处理器经反向接口 `IBufferedIntentSink` 让规则层决定。
3. **`class` 缺省为空 ⇒ 不缓冲**：保证不写新字段的既有数据、既有游戏输入行为完全不变；打开缓冲是数据层逐动作的显式选择。
4. **点按/按住判定的 tick 精度**：阈值换算为 tick 后比较"按下到抬起的动作时钟差"；同批次内按下又抬起视为 0 tick 点按。

### 已知限制（逐条交代给设计层）

- 缓冲与宽限的 tick 处理器只在**连续步**工作；离散步清空缓冲（离散模式的"输入缓冲"语义留给后续切片，01 第 6 节未定义）。
- `GraceTracker` 不内置 Expr 求值：`IGraceConditionEvaluator` 的具体实现（行动者上下文里求值）由规则层/宿主接线，本切片只给机制与接口。
- `face_on_accept`（转向）只在记录里携带并经 `BufferedIntent.FaceOnAccept` 暴露，实际转向由取用方（动作层）执行。
- 网络/回放来源的缓冲同步与回滚不在本切片范围。

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
