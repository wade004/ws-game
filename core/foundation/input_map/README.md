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
  core/
    InputMapHost.cs             IInputMapHost 默认实现
    InMemoryInputMapDiagnostics.cs
  schema/
    found.input_action.md       字段说明、登记表判断记录
  tests/
    BindingParserTests.cs
    InputMapHostTests.cs
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

## 诊断

`IInputMapDiagnostics`（默认实现 `InMemoryInputMapDiagnostics`）记录：`Rebind` 检测到同组
冲突时的警告文本。不依赖任何引擎适配层接口。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 绑定解析、重绑定、冲突检测、导出/导入的实现机制 | 是 | 具体动作集内容与默认绑定（`found.input_action` 数据行） |
| `found.input_action` 表字段定义与 `FromRecord` 加载入口 | 是 | 具体游戏的动作数据 |
| `input.action_triggered`/`input.rebind_conflict` 事件 | 是 | 订阅这些事件做具体呈现（如设置界面提示冲突） |
