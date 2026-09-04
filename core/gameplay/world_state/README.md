# L4 玩法层 · world_state（世界状态标志字典）

职责：落地 05_对象模型与世界.md 第 8 节 WorldState、ADR-0008"世界状态标志替代 Phasing"、
00_架构总则.md 第 4 节设计原则 8："世界状态是一个带命名空间的标志字典，用来表达'这段剧情是否
发生过''这个区域当前处于什么阶段'等世界级状态；刷新策略、物件显隐、任务可见性、对话分支都
通过条件语言引用它来判断；存档在持久化层面等价于'世界状态字典 + 玩家数据'"。对应 01 第 L4
模块表 `world_state` 行（契约 `WorldState`、数据表 `world.flag_schema`、事件 `world.flag_changed`、
策略配置项"命名空间划分方式"）。

依赖：`Core.Carriers`（`core/gameplay` 唯一直接依赖，见 `core/gameplay/Core.Gameplay.csproj`），
经其传递引用 `Core.Rules`（`IExprGroupProvider`、`IExprReadableEvent`）、`Core.Numbers`、
`Core.Foundation`（`Common`/`Common.Json`/`Expr`/`EventBus`/`SaveSystem`/`DataRegistry`）。
本模块本身不直接持有 `Core.Rules`/`Core.Foundation` 的 `ProjectReference`（沿用 `Core.Gameplay.csproj`
既有依赖声明，不新增），只使用它们经 `Core.Carriers` 传递可见的公开类型。不使用
`UnityEngine`/`System.Threading`/`DateTime`/`System.Random`/`System.Reflection`（同 01 第 6 节、
11 第 2～4 节工程规范）。

## 目录

```
world_state/
  README.md
  contracts/
    IWorldState.cs            IWorldState 接口 + FlagChangedCallback 委托
    WorldStateOptions.cs      DispatchMode/EnforceSchema/SchemaEntries 策略配置 + WorldFlagSchemaEntry
    IWorldStateDiagnostics.cs 诊断出口（OnChanged 回调异常隔离用）
    Events.cs                 WorldStateEventKeys + WorldFlagChangedEvent（IEvent + IExprReadableEvent）
    WorldExprSchemaEntries.cs world.get/has/get_int 的精确 IExprSchema 登记 + 判断记录（含契约缺口）
    CompositeExprSchema.cs    通用 IExprSchema 合并帮助类型
  core/
    WorldState.cs              IWorldState + IWorldFlags + IPersistable 的唯一实现
    WorldExprGroupProvider.cs  IExprGroupProvider：world.get/has/get_int
    InMemoryWorldStateDiagnostics.cs
  schema/
    WorldStateSchemas.cs       world.flag_schema 的 TableSchema（仅文档化，不参与运行期加载）
  tests/
    TestSupport.cs             EventBus 构造帮助 + WorldOnlyExprHost
    WorldStateTests.cs          WorldState 读写/事件/OnChanged/校验/持久化用例
    WorldExprGroupProviderTests.cs  world.get/has/get_int 经真实 ExprParser/ExprEvaluator 求值
```

## 谁能写、谁能读

05 第 8.2 节原文："谁能写：任何持有该模块契约引用的系统都可以写（技能效果 `set_world_flag`、
对话动作、任务奖励、脚本钩子、遭遇结算），但每次写入必须带 `writerId`……架构不做写权限的强
约束，约束靠内容评审与 ADR 流程"——`WorldState`/`IWorldState` 忠实照抄这条：`Set`/`Remove` 对调用方
身份不做任何检查，只强制 `writerId` 是一个合法构造的 `Id`（用于排查与日志，见
`WorldFlagChangedEvent.WriterId`），不区分谁"有权"写某个命名空间。

建议命名空间划分（01 第 L4 模块表本行"策略配置项：命名空间划分方式"——本模块不强制，仅作约定）：

| 前缀 | 典型写入方 | 说明 |
|---|---|---|
| `world.gobj.<实例id>.<字段名>` | `core/carriers/gobj` 经 `IWorldFlags` | 07 第 3.4 节：`GameObject.state` 落地为标志，如 `chest.open_state` |
| `world.quest.<...>` | `core/gameplay/quest` | 任务前置/进度相关的世界级标志（与 `player.quest_state` 的"玩家自己的任务进度"不同，见 04 第 6.2 节 `quest` 分组另有专门引用） |
| `world.story.<...>` | `core/gameplay/dialog` | 剧情节点是否发生过、对话分支门槛 |
| `world.spawn.<...>` | `core/gameplay/spawn` | 一次性刷新点是否已消耗、区域刷新阶段 |

## 判断记录

1. **`IWorldState.Get` 缺失返回 `ExprValue.OfBool(false)`，与 `IWorldFlags.Get`（返回 `ExprValue?`，
   缺失返回 `null`）刻意保持不同语义**：04 第 6.3 节"求值期若引用的具体对象缺失……`query` 返回该
   分组约定的默认值（`Bool` 默认 `false`）"是 Expr 求值层的规则，`IWorldState.Get` 是这条规则在
   `world` 分组的落地（供 `WorldExprGroupProvider.Query("get", ...)` 直接透传）；而
   `core/carriers/common/contracts/IWorldFlags.cs` 是该模块自己拍板的"未设置过返回 null"语义（该
   接口注释原文），二者是两个不同层面的契约，本类型都要满足，用显式接口实现区分（`ExprValue Get`
   满足 `IWorldState`，`ExprValue? IWorldFlags.Get` 满足 `IWorldFlags`）——CLR 按方法名+参数判定
   签名、不看返回类型，两个同名同参但返回类型不同的方法无法用同一个隐式成员同时满足两个接口。

2. **`Remove`/`Keys`/`KeysUnder`/`Count` 是 05 第 8.2 节接口原文之外的补充**：05 原文只给出
   `get`/`set`/`has`/`onChanged` 四个成员。任务书拍板补充这四个成员，理由：`Remove` 供内容脚本
   撤销误写的标志（05 未禁止，`set` 一个"缺省值"也能模拟但语义不如显式移除清晰，且会在
   `Has()` 上产生"到底是真设置成默认值还是从未设置"的歧义）；`Keys`/`KeysUnder`/`Count` 供存档/
   调试工具与内容校验按命名空间批量浏览（07 第 3.4 节 `world.gobj.<实例id>.*` 这类命名空间下的
   查询场景没有其它入口）。四者均不改变 05 原有四个成员的语义。

3. **`Set`"同值不发事件"，但 `Remove` 永远发事件（只要标志原先存在）**：05 原文未规定"同值是否
   发事件"，任务书拍板"值未变化不发事件"——避免脚本反复 `set` 同一个值时刷屏
   `world.flag_changed` 与 `OnChanged` 通知。`Remove` 不适用这条规则：它改变的是"该标志是否存在"
   这个状态本身（`Has()` 从 `true` 变 `false`），即便存储值恰好等于"缺失"的默认值
   （`ExprValue.OfBool(false)`）也仍视为一次真实变化。

4. **`OnChanged` 不维护独立于事件总线的回调登记表，而是构造期订阅自己发出的 `world.flag_changed`
   一次，按 `flagKey` 过滤后调用用户回调**：这样"回调在事件发布之后调用"（05 第 8.2 节
   `onChanged` 定位为"同一事件的按 key 过滤订阅便利接口"）与"`DispatchMode.Enqueue` 时事件延后到
   `DispatchPending`"两条要求由同一处派发时机自动满足，不需要为 `OnChanged` 单独维护一套"立即 vs
   延后"的分支逻辑。回调异常在这次内部订阅里被捕获、记入 `IWorldStateDiagnostics`，不影响其它
   订阅者（惯例同 `EventBus.DispatchOne` 对自己订阅者列表的异常隔离，但用的是本模块自己的诊断
   出口，不是 `IEventDiagnostics`——`OnChanged` 回调是本模块自己的 API 面，异常应该出现在本模块
   的诊断记录里，而不是淹没在事件总线的通用诊断流水账中）。

5. **`WorldStateOptions.DispatchMode` 默认 `Immediate`**：世界标志的写入多来自对话/任务/脚本等
   非 tick 上下文（不像 `combat.damage_dealt` 那样密集产生、适合 tick 末批处理），且 07 第 3.4 节
   要求 `gobj.state_changed` 与对应的一次 `WorldState.set` 保持顺序关系——立即派发保证
   `world.flag_changed` 在触发它的调用返回前就已经被观察者感知到，不会因为进入队列而与随后
   同步发生的其它事件（如 `gobj.state_changed`）产生顺序倒挂。`Enqueue` 作为可选项保留给需要与
   tick 内其它批处理事件统一时机的调用方。

6. **`world.flag_schema` 只声明 `TableSchema`，不注册 `IValidationRule`，不接入 `IDataRegistry`
   运行期加载**：04 第 1.1 节表清单原文该行"非运行态数据，仅作文档化 schema"——它的作用是给内容
   作者一份"这个命名空间下应该是什么类型"的说明文档，不是运行期强制校验的依据。`WorldStateOptions`
   提供的 `EnforceSchema`/`SchemaEntries` 是任务书额外拍板的"运行期可选校验"（默认关闭），把
   `world.flag_schema` 的登记内容（经游戏组装根从 `IDataRegistry` 查询后转换）喂给它，可以选择性
   地在 `Set` 时做"前缀已登记 + 类型匹配"的防呆校验；本模块不负责把 `IDataRegistry` 的查询结果
   转换成 `WorldFlagSchemaEntry` 列表——那是组装期的事，避免 `world_state` 对 `data_registry`
   产生"必须先加载完数据才能构造"的运行期耦合。

7. **`IWorldState.Set` 值类型放宽到 Expr 全部五种标量（`Bool|Int|Number|String|Id`），10 第 2.3 节
   `world_state_flags` 字段表原文只写 `Bool｜Int`**：任务书拍板"放宽到 Expr 标量集合并在 README
   注明超出 10 的部分是实现期补录，存档格式向后兼容"。`WorldState.Save()`/`Load()` 按 5 种类型
   分别落地 JSON 形状（见下条），旧存档里只出现 `Bool`/`Int` 两种形状时行为与 10 文档原文完全一致，
   `Number`/`String`/`Id` 是本次实现按 04 第 6.3 节"类型集合与接口记法一致：`Bool`/`Int`/`Number`/
   `String`/`Id`"（Expr 语言本身没有其它类型）补齐的向后兼容扩展，不破坏旧存档的可读性。

8. **`Save()`/`Load()` 的 JSON 形状：`Bool`/`String` 用原生 JSON 布尔/字符串；`Id` 用
   `{"$id": "..."}` 包装以区别于 `String`（否则二者在 JSON 里都是字符串，读档时无法判定该恢复
   成哪种 `ExprValueKind`）；`Int` 写成不带小数点的数字文本（如 `"42"`），`Number` 写成必带小数点
   的数字文本（如 `"5.0"`，复用 `ExprValue.ToString()` 对 Number 的既有格式化规则：无小数点/无
   指数时补 `.0`）——`JsonNumber.RawNumberText` 是否含 `.`/`e`/`E` 就此成为 `Int`/`Number` 的
   判别依据，读档时按这条规则分派，二者共享同一个 JSON `number` 语法但不再互相混淆。这一点
   （`Int`/`Number` 的 JSON 层区分）是任务书拍板原句"Bool/Int/Number/String/Id 各自的 JSON
   形式；Id 用 `{"$id": "..."}` 包装以区分 String"未明确规定、本模块按 `ExprValue.ToString()`
   已有的格式化惯例自行补全的判断记录：若调用方绕开 `WorldState.Save()` 自己拼装 `JsonNumber`
   （不经过本模块，如手工构造测试数据）且不带原始文本，`Load()` 按"能否解析为 `Int64`"退化
   判定（无小数部分即视为 `Int`）——这是唯一没有办法从 JSON 本身还原、只能取一个合理默认行为
   的边角情况。

9. **`WorldExprGroupProvider` 与 `RulesExprSchema` 的已知不兼容，见 `contracts/WorldExprSchemaEntries.cs`
   顶部完整记录**：04 第 6.2 节官方示例 `world.get(world.bridge.repaired)` 中的参数
   `world.bridge.repaired` 应解析为 `Id` 字面量，但如果解析时使用的 `IExprSchema` 是
   `core/rules/expr_host.RulesExprSchema.Instance`，会因为该类型"已知分组（含 `world`）下未登记的
   key 一律放行为合法引用"的策略，把 `world.bridge.repaired` 误判成另一个（不存在的）
   `world.bridge.repaired` 引用而不是字面量，运行期最终对 `world.get`/`has`/`get_int` 的调用抛
   `ArgumentException`（参数类型不是 `Id`）。本模块自己的测试改用
   `WorldExprSchemaEntries.BuildStandalone()`（只精确登记 `get`/`has`/`get_int` 三个键、不做已知
   分组放行）绕开这个问题；但这属于 `core/rules/expr_host`（其他任务的目录）的既有设计，本任务
   允许改动的范围不包含它，只能记录、不能修复，详见该文件顶部完整分析与"结论"。游戏组装根接入
   `world` 分组时需要注意：不能简单地把 `WorldExprSchemaEntries` 与 `RulesExprSchema` 合并后当作
   `world` 分组的解析依据，否则任何把 `world.<路径>` 标志键字面量当参数传入的 Expr 文本都会解析
   错误。

10. **`WorldExprGroupProvider.get_int` 缺失返回 `Int(0)`，不是 04 第 6.3 节泛指的 `Bool(false)`**：
    04 第 6.3 节"`Bool` 默认 `false`，数值默认 `0`"本就区分了两类默认值，`get_int` 精确登记的
    `ReturnKind` 是 `Int`，缺失时理应退化到"数值默认 `0`"这一支而不是 `Bool` 默认值；若标志存在但
    实际类型既不是 `Int` 也不是 `Number`，同样按 `Int(0)` 处理并记一条诊断警告（任务书未规定这一
    分支，按"类型不符 = 视同缺失"的最保守取舍处理，不抛异常——`Query` 内部异常会被
    `ExprEvaluator` 收敛为整个表达式判 `false`，代价比返回一个警告过的默认值更大）。

## 不负责什么

- 不实现 `world.flag_schema` 的运行期加载或校验规则——只声明 `TableSchema`（见判断记录 6），
  真正把数据表行接入 `IDataRegistry`/写 `IValidationRule` 不在本任务范围。
- 不提供游戏组装根把 `IDataRegistry` 查询结果转换成 `WorldStateOptions.SchemaEntries` 的转换代码——
  那是组装期的事（同 `RulesExprHostFactory` 的 `extraGroups` 由组装根注入的惯例）。
- 不修复 `core/rules/expr_host.RulesExprSchema` 与本模块的已知不兼容（见判断记录 9）——只记录、
  提供绕过方案（`WorldExprSchemaEntries.BuildStandalone()`）。
- 不做任何写权限检查——谁都能写，靠内容评审与 ADR 流程约束（05 第 8.2 节原文，见"谁能写"一节）。
