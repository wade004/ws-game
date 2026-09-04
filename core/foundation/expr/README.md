# L0 基础层 · expr 条件表达式语言

职责：实现全架构唯一的条件表达式语言 Expr 的解析器、静态校验器、求值器（见
`00_架构总则.md` 第 4 节原则 5、`ADR-0005`、`04_数据与内容管线.md` 第 6 节）。触发条件、
优先级表候选条件、掉落条件、任务前置、对话显隐、遭遇阶段切换、成就 criteria、刷新条件
全部用它书写；任何宿主只需实现 `IExprHost.Query(group, key, args)` 一个出口即可接入。

依赖：只依赖 `core/foundation/common`（`Id` 类型）与 .NET 标准库；不依赖任何其他模块、
任何引擎适配层实现。

不负责什么：

- 不知道任何具体游戏系统的字段含义（`self.hp_pct` 的 `hp_pct` 具体怎么算，是宿主的事）。
- 不做数据表加载、schema 迁移、引用完整性校验，那是 `DataRegistry`（本阶段尚未创建）的职责；
  本模块只保证"给定一段 Expr 文本，能否解析、能否通过静态类型校验、给定宿主如何求值"。
- 不引入第二种语法或任何脚本引擎：纯手写词法 + 递归下降语法分析，无反射、无
  `System.Linq.Expressions`、无 Roslyn/CodeDom、无多线程、无系统时钟依赖。

## 目录

```
expr/
  README.md
  contracts/   ExprValue.cs ExprValueKind.cs ExprGroups.cs ExprNode.cs
               IExprHost.cs IExprSchema.cs ExprSchema.cs ExprSignature.cs
               IExprDiagnostics.cs ExprDiagnosticsRecorder.cs
               ExprParseException.cs ExprIssue.cs
  core/        ExprLexer.cs ExprParser.cs ExprValidator.cs ExprEvaluator.cs
  tests/       FakeHost.cs ExprParserTests.cs ExprValidatorTests.cs ExprEvaluatorTests.cs
```

## 类型清单

| 类型 | 说明 |
|---|---|
| `ExprValue` | `readonly struct` 判别联合：`Bool \| Int(long) \| Number(double) \| String \| Id`，静态工厂 `OfBool/OfInt/OfNumber/OfString/OfId` 构造，`Kind` 判别，`AsXxx` 按 Kind 取值（不符抛异常）。`IsNumeric`/`ToDouble()` 承载 Int/Number 互操作规则（04 第 6.3 节） |
| `IExprHost` | 宿主查询出口：`ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)` |
| `IExprSchema` / `ExprSchema` | 静态校验用的引用登记表：`TryGetSignature(group, key, out ExprSignature)`；`ExprSchema` 是可编程实现，`Register(group, key, returnKind, argKinds...)` 逐条登记 |
| `ExprSignature` | `{ ReturnKind: ExprValueKind, ArgKinds: IReadOnlyList<ExprValueKind> }` |
| `IExprDiagnostics` / `ExprDiagnosticsRecorder` | 诊断出口：`Warn(string)`、`Error(string, Exception?)`；内存实现供测试与调用方检查 |
| `ExprNode` 及子类 | 不可变 AST：`ExprOrNode`、`ExprAndNode`、`ExprNotNode`、`ExprCompareNode`、`ExprLiteralNode`、`ExprReferenceNode`；均带结构化 `Equals`/`GetHashCode` 与还原为规范化文本的 `ToString()` |
| `ExprParser` | `Parse(string text) -> ExprNode`；失败抛 `ExprParseException { Position, Message }` |
| `ExprValidator` | `Validate(ExprNode, IExprSchema) -> IReadOnlyList<ExprIssue>`；空列表即通过 |
| `ExprEvaluator` | `Evaluate(ExprNode, IExprHost, IExprDiagnostics) -> ExprValue`；便捷 `EvaluateBool(...)` |
| `ExprParseException` | 解析期错误：语法错误、非法字符、未闭合字符串、缺少 domain/分组前缀等词法/语法层问题 |
| `ExprIssue` / `ExprIssueKind` | 静态校验问题：未知分组/key、参数个数/类型不匹配、比较两侧类型不匹配、非法比较运算符、逻辑操作数非 Bool |

## 语法（BNF，原文照抄自 04 第 6.1 节）

```
<expr>        ::= <or_expr>
<or_expr>     ::= <and_expr> ( "or" <and_expr> )*
<and_expr>    ::= <unary_expr> ( "and" <unary_expr> )*
<unary_expr>  ::= "not" <unary_expr> | <compare_expr>
<compare_expr>::= <term> ( <cmp_op> <term> )?
<cmp_op>      ::= "==" | "!=" | ">" | ">=" | "<" | "<="
<term>        ::= <literal> | <reference> | "(" <expr> ")"
<literal>     ::= <number> | <string> | "true" | "false"
<reference>   ::= <group> "." <key> ( "(" <arg_list> ")" )?
<group>       ::= "self" | "target" | "event" | "world" | "quest" | "player" | "combat" | "enemies" | "time"
<arg_list>    ::= <term> ( "," <term> )*
```

优先级（低到高）：`or` < `and` < `not` < 比较 < 括号/字面量/引用。括号只改变优先级，不支持函数定义。

## 语法细节（BNF 之外必须定的点，本模块拍板落地）

BNF 没有规定词法细节与"点分标识符到底是 reference 还是 Id"的判定规则，以下是本模块的
具体实现约定：

1. **词法**：空白（空格/制表符/回车/换行）分隔，不敏感于换行；关键字 `and`/`or`/`not`/`true`/`false`
   固定小写。
2. **数字**：`[0-9]+` 无小数点 → `Int`；`[0-9]+.[0-9]+` 有小数点 → `Number`；负号前缀 `-`
   只在紧跟数字时被词法器当作数字字面量的一部分（例如 `-5`、`-0.3`），因此负号**只能**
   出现在数字字面量前，不是通用一元运算符——`-self.hp_pct` 或 `- (a and b)` 不是合法语法。
3. **字符串**：双引号包裹，支持 `\"` 与 `\\` 两种转义，其余反斜杠后缀（如 `\n`）视为非法转义，
   解析期报错；未闭合字符串（缺右引号或转义符位于文本末尾）解析期报错。
4. **标识符**：单段满足 `[a-z][a-z0-9_]*`，多段用 `.` 连接（如 `self.hp_pct`、`skill.aura.burning`）；
   每一段（含第一段）都必须以小写字母开头——这比 `common/Id` 的格式正则（后续段允许数字/下划线开头）
   更严格，是本模块在 Expr 词法层面做出的收紧，详见下方"判断记录"。
5. **点分标识符归类（BNF 未定，本模块拍板）**：整个点分标识符先按上一条规则整体词法为一个
   token；解析期看它的第一段：
   - 是 04 第 6.2 节九个分组（`self/target/event/world/quest/player/combat/enemies/time`）之一
     → 整体按 `<reference>` 解析：`group` 取第一段，`key` 取"第一个点之后的全部剩余部分"
     （允许 key 本身多段，如 `quest.objective_progress`），紧跟 `(` 则解析参数列表。
   - 否则 → 整体作为 `Id` 字面量：构造 `Core.Foundation.Common.Id`（复用该类型，不重新定义
     一套 Id 校验逻辑）。因为 Expr 标识符段规则（每段必须 `[a-z]` 开头）严格蕴含
     `Common.Id` 的格式正则，这里的 `new Id(...)` 构造不会因为格式问题失败。
   - 这条规则**统一应用于任何位置**（顶层、括号内、引用的参数列表内），不因"是不是在参数
     位置"而改变判定方式——具体取舍见下方"判断记录"第 2 条。
6. **引用参数**：`(term, term, ...)` 中每个 `term` 只能是字面量、Id、另一个引用，或括号包
   起来的完整表达式；空参数列表 `()` 不合法（BNF 的 `arg_list` 至少一个 `term`），因此零参
   引用一律不写括号（`self.hp_pct`，不是 `self.hp_pct()`）。
7. **比较**：`Int` 与 `Number` 互相可比（提升为 `double`）；`Bool`/`String`/`Id` 三者只能
   `==`/`!=`，`String` 走序数（ordinal）比较；其余类型组合（如 `Bool` 与 `Int`）视为类型
   不匹配——静态校验期在 `ExprValidator` 报 `CompareTypeMismatch`，运行期在
   `ExprEvaluator` 记一条 `Error` 并让整个表达式判定为 `false`（不抛异常）。
8. **`compare_expr` 无比较符时**：`term` 直接作为该子表达式的值——可以是 `Bool`（供
   `and`/`or`/顶层判定使用），也可以是任意类型（供"取某个数值再比较"或"顶层直接返回一个
   非 Bool 值"的场景，如 `DataRegistry.query` 按字段取值）。本模块的 AST 不为这种情况生成
   多余的包装节点：`ParseCompare` 没读到 `cmp_op` 时直接返回 `term` 对应的节点。
9. **短路**：`and`/`or` 求值时严格从左到右，一旦结果已确定（`and` 遇到 `false`、`or` 遇到
   `true`）立即返回，后续操作数（含其中任何 `Query` 调用）不再求值；测试用 `FakeHost` 的调
   用计数验证。
10. **运行期宿主异常**：`IExprHost.Query` 抛出的任何异常，在 `ExprEvaluator` 内部被捕获，
    记一条 `IExprDiagnostics.Error`，并让**整个顶层表达式**（不只是触发异常的那个子引用）
    的求值结果收敛为 `Bool(false)`，不向调用方抛出（04 第 6.4 节"求值期"表）。实现上用一个
    仅内部可见的控制流信号异常把嵌套调用栈直接unwind回 `Evaluate` 顶层，避免异常返回值
    继续参与后续比较/逻辑运算产生第二条误导性错误。

## 判断记录（文档歧义处，按优先级从高到低排列）

1. **负号是否是通用一元运算符**：BNF 没有 `unary_minus` 产生式，`<literal> ::= <number> | ...`
   里 `<number>` 本身要不要含负号也没写。取"负号只是数字字面量词法的一部分"这一更保守的
   解释（原文：任务说明"允许负号前缀作为一元负（仅字面量）"），不允许 `-self.hp_pct`
   `-(a and b)` 这类对任意 term 取负——因为 Expr 语言本身没有算术运算，"取负"唯一合理的
   使用场景就是写一个负数字面量（如 `event.damage_amount > -5`），没有必要为不存在的算术
   引入通用一元负号。
2. **点分标识符归类规则应用于参数位置时与文档示例的冲突**：04 第 6.2 节表格自身的示例
   `world.get(world.bridge.repaired)`、`quest.is_active(quest.deliver_letter)` 里，
   传给引用的参数（`world.bridge.repaired`、`quest.deliver_letter`）第一段恰好和分组名
   `world`/`quest` 同名——04 第 2.2 节域名清单里 `world`、`quest`、`target`、`combat`
   同时也是内容 id 的合法 `domain`。按任务给出的判定规则"第一段是九个分组之一 → reference；
   否则 → Id"逐字实现、且不为参数位置单独开特例，上述两个例子里的参数会被解析成
   **零参引用**（`Reference(group=world, key="bridge.repaired")` /
   `Reference(group=quest, key="deliver_letter")`），而不是文档原意的 Id 字面量。
   本模块的取舍是：严格按任务给出的规则实现（唯一规则、位置无关，简单可预测，不引入
   "参数位置特例"这种隐藏状态），并在此如实记录这一已知限制——**当某个内容 domain 与九个
   分组之一同名（`world`/`quest`/`target`/`combat`）时，该 domain 下的 Id 无法作为 Expr
   参数按字面量书写**，实际使用中要么给这四个分组建立对应的零参 `query`（如
   `quest.deliver_letter` 作为 `quest` 分组下一个返回 `Id` 的零参引用，效果等价但语义
   上是"查询"而非"字面量"），要么这是后续 ADR 需要修订 BNF（比如给 Id 字面量加统一前缀
   区分于分组引用）来彻底消除的歧义。本模块测试套件里凡是需要传"和分组同名的 Id"场景，
   一律登记为对应分组下的一个零参引用（如 `quest.a`），不使用会触发此歧义的写法。
3. **`Id` 段格式比 `common/Id` 更严格**：`common/Id.cs` 的格式正则
   `^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$` 允许第二段及以后以数字/下划线开头（如 `a.1b`）；
   本模块的标识符词法要求"每段 `[a-z][a-z0-9_]*`"（每段都必须字母开头）。两者不冲突
   （本模块词法能产出的字符串永远是 `common/Id` 合法格式的严格子集，`new Id(...)`
   构造不会因为本模块放行的写法而失败），但意味着 Expr 文本里写不出 `skill.1st_rank`
   这种（罕见）以数字开头的段；这是词法简单性与既有 `Id` 类型宽松格式之间的取舍，
   记录在此供后续如需支持再补 ADR。
4. **未知分组在解析期还是校验期报错**：解析器（`ExprParser`）本身不可能产出
   `group` 不在九个分组之内的 `ExprReferenceNode`（判定规则本身保证了这一点），
   所以"未知分组"错误只能出现在——`ExprValidator.Validate` 收到一棵不是由本模块
   `ExprParser` 产出、而是调用方手工构造（或来自未来其它产出 AST 的路径）的
   `ExprNode` 树时。`ExprValidator` 仍然独立检查 `group` 是否属于九个分组
   （不假设输入的 AST 一定来自 `ExprParser`），以保证"未知分组"这条 04 第 5 节
   校验项在任意 AST 输入下都成立。

## 宿主引用分组（04 第 6.2 节，原样列出）

| 分组 | 含义 |
|---|---|
| `self` | 表达式所属主体自身状态 |
| `target` | 当前目标状态 |
| `event` | 触发此次求值的事件携带的数据 |
| `world` | 世界状态标志 |
| `quest` | 玩家任务状态 |
| `player` | 玩家角色数据 |
| `combat` | 当前战斗/施法上下文 |
| `enemies` | 周边敌对单位聚合信息 |
| `time` | 时间与计时器 |

## 错误处理（04 第 6.4 节，原样列出）

| 阶段 | 错误情形 | 处理方式 |
|---|---|---|
| 解析期（内容提交时） | 语法错误、未知分组、未知 key、类型不匹配 | 校验器报错，内容不可合入——本模块对应 `ExprParser.Parse` 抛 `ExprParseException`（语法层）与 `ExprValidator.Validate` 返回非空 `ExprIssue` 列表（语义层） |
| 求值期（运行时） | 引用对象暂缺（如无目标） | 按分组默认值返回，记录警告，不抛异常——这是宿主实现的责任，`IExprHost.Query` 自行决定缺失时返回什么默认值，本模块不干预 |
| 求值期（运行时） | 宿主 `query` 内部异常 | 视为该表达式求值为 `false`，记录错误日志，不影响其余系统 tick——见上"语法细节"第 10 条 |

## 做不了的事 / 明确排除

- 不提供"从 `ExprNode` 反向生成 `IExprSchema`"之类的辅助工具；`ExprSchema` 的注册内容由
  各宿主系统自己维护（如战斗系统注册 `combat.*`，任务系统注册 `quest.*`）。
- 不做表达式的性能缓存/编译（如把 AST 编译成委托）；`ExprEvaluator.Evaluate` 每次都是对
  AST 的直接树形遍历解释执行，足够满足条件判定场景的性能要求，也避免引入
  `System.Linq.Expressions`/反射这类被明确禁止的依赖。
