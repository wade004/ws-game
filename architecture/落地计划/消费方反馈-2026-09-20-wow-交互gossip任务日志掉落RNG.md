# 消费方反馈处理记录（交互/gossip正文/任务日志进度/NpcFlag/持久化注册/掉落RNG，2026-09-20）

> 本文档允许出现具体技术名（引擎/语言/框架/工具名），不受 `architecture/00～14` 与
> `architecture/adr/` 正文的禁用词约束（见仓库根 `CLAUDE.md`"硬性规则"）。本文档答复消费方（仓库
> `ws-game-wow`）2026-09-20 提交的反馈文档《交互/gossip/任务日志/NpcFlag/持久化注册策略/掉落RNG
> 共七项核实》，正文统一用"消费方"指代对方项目，不出现具体游戏代号。编号沿用消费方原文编号
> （反馈 1、2、3a、3b、4、5、6、7，以及消费方自行排除的"候选 5（原稿）"），不自创编号。
>
> 本轮已落地的四条改动（ADR-0043、ADR-0044、ADR-0045 + 反馈第 2 条根治）已合入 `main`
> （提交 `7791fed4`），将随 `<待发布版本>` 发布，本文档所有字段名/签名/枚举取值均以合入 `main`
> 的实际代码为准核对，不采用反馈文档"期望改动"里的描述。独立核实依据：消费方反馈原文
> `D:\workespace\ws-game-wow\docs\框架反馈\2026-09-20_交互gossip任务日志NpcFlag持久化注册与掉落RNG七项核实.md`
> （只读，未改动该仓库任何文件）与框架侧独立复审报告（`review_wow_9.md`，基于框架 `main` 当时
> HEAD `4f4530c1` 核实，早于本轮四条改动合入）。

**结论先行**：消费方 7 条候选反馈（含 1 条本文档编号"候选 5（原稿）"的已排除项）逐条核实：
7 条成立或部分成立，1 条（候选 5（原稿）：持久化注册策略"两种并存"）经框架侧复核确认确实不成立，
与消费方自查结论一致。已成立的 7 条中，**4 条本轮已落地**（3a+3b 合并处理、反馈 6、反馈 4，均已
出 ADR；反馈 2 是参考 UI 局部修正，不需要 ADR）；**2 条方向已定但本轮未落地**（反馈 1、反馈 7，
均需要单独一份 ADR 才能推进代码实现）；**反馈 5 本轮不改代码**，本文档在"口径不同"一节澄清消费方
表述中不够精确的一点，建议消费方更正自己的登记文档措辞，框架侧后续视需要再评估是否补文档/校验
规则。

---

## A. 逐条答复表

| 编号 | 成立性判定 | 框架侧结论 | 本轮是否落地 | 对应 ADR / 后续排期 |
|---|---|---|---|---|
| 反馈 1：`interact` 意图无原生 NPC/creature 路径 | 成立 | `InteractIntentTickHandler` 是全仓库对 `"interact"` 意图的唯一消费者，参数形状 `{gobj_instance_id: Id}` 专属 gobj；creature 侧从未规划对应路径，消费方自行实现的绕行链路（`NpcInteractionRegistrar` 等）是消费方项目侧代码，不在框架仓库内，框架侧不核实其运行时状态 | 未落地（方向已定） | 需单独一份 ADR 拍板"走原生 creature interact 路径"，见 D 节 |
| 反馈 2：`QuestLogPanel` 不显示目标进度 | 成立 | 参考 UI 功能未做全，非契约缺口（`QuestProgress.ObjectiveCounts` 数据链路本身是通的） | 已落地 | 不需要 ADR（局部修正，细节非架构结论） |
| 反馈 3a：`dialog.gossip_menu` 缺开场白字段 | 成立 | schema 设计之初确未规划该字段 | 已落地 | [ADR-0043](../adr/0043-gossip菜单新增可选开场白正文字段.md) |
| 反馈 3b：`DialogPanel` gossip 正文硬编码占位 | 成立（3a 下游症状，随 3a 一并处理） | UI 硬编码是因为数据层此前无字段可查，非 UI 独立缺陷 | 已落地（随 3a） | [ADR-0043](../adr/0043-gossip菜单新增可选开场白正文字段.md) |
| 反馈 4：无"任务指示器"聚合概念 | 成立（否定性结论，已复核搜索范围） | 纯能力空白，非数据缺口 | 已落地 | [ADR-0045](../adr/0045-任务给予者指示器只读查询.md) |
| 反馈 5：`NpcFlag` 五值"零消费" | 部分成立——核心论点（无专属业务行为分支）成立，但"零消费"表述不准确 | 六个标志值全部经 `CreatureFactory.Spawn` 写入 `unit.Tags`，可被通用目标过滤/`ContainsTag` Expr 消费，不是字面零消费；见 C-1 | 本轮不改代码 | 无（建议消费方更正登记文档措辞；框架侧文档/校验规则留待后续视需要评估） |
| 反馈 6：`GobjOptions.DialogOpener` 不携带交互者身份 | 成立（框架自己判断记录承认的"契约缺口"） | `DialogOpenerDelegate` 签名过窄，装配根曾用 `dialogRef` 顶替 `npcId` | 已落地 | [ADR-0044](../adr/0044-gobj对话打开回调新增交互者身份透传路径.md) |
| 反馈 7：`LootHost` 掉落 RNG 消耗不对称 | 成立，但不宜表述为"违反确定性契约" | 固定种子下结果完全可复现；真正问题是流位置对无关字段敏感这一脆弱性，见 C-2 | 未落地（仅方向） | 需单独一份 ADR 定契约（每条掉落项按 `(loot_table_id, entry_id)` 派生独立子流），代码实现待评估，见 D 节 |
| 候选 5（原稿，已排除）：持久化注册策略"两种并存" | 不成立（消费方自查已排除，框架侧复核确认） | `QuestPersistable`、`RngStreamsPersistable` 在同一个 `RegisterPersistables` 方法内走完全相同的注册路径，不存在"自动注册 vs 显式调用"两种策略 | 无需处理 | 无 |

---

## B. 本轮已落地（4 条）

### 3a + 3b：`dialog.gossip_menu` 新增可选字段 `greeting_key`

- **实际改法**：`core/gameplay/dialog/core/DialogSchemas.cs` 的 `GossipMenu` 表 schema 新增
  字段 `greeting_key`（`FieldKind.TextKey`，`required: false`）；内存态 `GossipMenuDefinition`
  新增只读属性 `Id? GreetingKey`，并新增一个携带该参数的构造函数重载（原有两参构造函数原样保留）；
  `GossipView`（`core/gameplay/dialog/contracts/IDialogHost.cs`）同样新增 `Id? GreetingKey`
  属性 + 新增构造函数重载；`DialogHost.OpenGossip`/`GetGossipView` 把
  `menu.GreetingKey` 透传到 `GossipView.GreetingKey`。字段命名按本模块既有的"语义名 + `_key`
  后缀"惯例，**不是**消费方反馈原文建议的 `greeting_text_key`（理由见 ADR-0043 决策 2）。
- **参考 UI 改法**：`adapters/unity/.../DialogSettingsPanels.cs`（`DialogPanel` 类）的 gossip
  分支不再硬编码占位字符串"（NPC 对话选项）"；`GreetingKey` 有值时经 `_l10n.Text(...)` 渲染，
  **缺省时隐藏正文标签、不渲染正文区**——不是回落到某句占位/兜底文案（这一取舍是 ADR-0043 明确
  拍板的展示决策，理由是"框架编的话被当成游戏内容"比"干脆不显示"更容易误导内容审查与玩家）。
- **消费方需要怎么改用法**：给 `dialog.gossip_menu` 记录加一个可选的 `greeting_key` 字段（值是
  本地化文本引用键）即可获得开场白展示；不加此字段的既有记录行为不变（正文区不渲染，只显示选项，
  与改动前效果一致）。
- **迁移成本**：零。字段可选，旧数据不需要做任何改动即保持合法；两处新增的构造函数重载都是"加"
  不是"改"，任何已按旧签名构造 `GossipMenuDefinition`/`GossipView` 的既有代码（含消费方自己的
  测试代码，如有）不受影响。

### DialogOpener 身份缺口：新增 `DialogOpenerWithSourceDelegate`/`GobjOptions.DialogOpenerWithSource`

- **实际改法**：`core/carriers/gobj/contracts/GobjOptions.cs` 新增委托类型
  `public delegate void DialogOpenerWithSourceDelegate(Id unitId, Id gobjInstanceId, Id dialogRef);`
  （比既有 `DialogOpenerDelegate(Id unitId, Id dialogRef)` 多一个 `gobjInstanceId` 参数），并
  新增可选属性 `GobjOptions.DialogOpenerWithSource`；既有 `DialogOpener` 属性/委托类型原样保留，
  签名未改一字。`GameObjectHost.Interact` 分发 `on_use: dialog` 时，**新回调设置了就只调用新回调
  （不重复调用旧回调）**，只设了旧回调时调用旧回调，两者都未设置时行为与改动前一致（记诊断、判定
  为无动作）。生产装配根 `core/gameplay/assembly/GameplayAssembly.cs` 改接新回调：
  `resolvedGobjOptions.DialogOpenerWithSource ??= (unitId, gobjInstanceId, dialogRef) =>
  Dialog.OpenGossip(unitId, gobjInstanceId, dialogRef);`——`OpenGossip` 的 `npcId` 参数现在传
  的是真正的 gobj 实例 id，不再用 `dialogRef`（菜单 id）顶替。装配根内此前明确写着"契约缺口"的
  判断记录已更新为指向 ADR-0044，标记缺口已根治。
- **消费方需要怎么改用法**：本轮改动只涉及框架仓库自己的装配根与 `core/carriers/gobj` 契约层，
  **不需要消费方做任何改动就能受益**——如果消费方未来选择走"把 NPC 包装成 gobj"这条绕行路线
  （反馈 1 提到的选项 (a)），装配根拿到的"当前交互对象"从此就是正确的物件实例 id，vendor 等
  "按当前交互对象自身推断"的动作能拿到正确身份。**消费方目前走的是自己实现的 creature 直连路径
  （`NpcInteractionRegistrar`/`GameInteractionHost`），本来就不经过 gobj 的 `DialogOpener` 委托，
  不受本条改动直接影响**——本条修的是"把 NPC 包装成 gobj"这条绕行方案本身的可用性，不是消费方
  当前实际在用的那条路径。
- **迁移成本**：零。ABI 只新增，旧委托/旧回调仍是合法契约，未升级到新回调的既有集成点行为完全不变。

### 任务日志目标进度：`IQuestHost.GetObjectiveRequiredCounts` 默认接口成员

- **实际改法**：`core/gameplay/quest/contracts/IQuestHost.cs` 新增
  `IReadOnlyList<int> GetObjectiveRequiredCounts(Id questId) => System.Array.Empty<int>();`——
  **默认接口成员**，未登记/已删除的任务定义降级返回空列表、不抛异常。`QuestHost`（生产实现）给出
  真实实现（读取该任务定义每条目标的需求计数）；`presentation/ui/core/ViewModels/
  QuestLogViewModel` 新增同名转发方法；`adapters/unity` 的 `QuestLogPanel.RefreshUi` 据此拼出
  "当前/需求"文案，覆盖空任务日志、需求数量缺失、计数超过需求、任务已完成四种边界情形，数字格式化
  固定 `InvariantCulture`。
- **消费方需要怎么改用法**：这是纯粹的参考 UI 增强，**消费方任何既有 `IQuestHost` 实现不需要改
  一行代码即可继续编译通过**（默认接口成员本身就是为了这一点存在）——若消费方自己有 `IQuestHost`
  的假实现/测试替身且希望它也返回真实的需求计数（而不是默认的空列表），需要显式重写这个方法；若
  不重写，调用方拿到的是空列表，`QuestLogPanel` 会按"需求数量缺失"这一边界情形处理（不抛异常，
  只是不显示"/需求"部分）。若消费方直接复用框架参考 `QuestLogPanel`，升级后即可自动看到目标进度，
  不需要自己改面板代码。
- **迁移成本**：零到极小。不重写默认接口成员则零成本；若消费方项目里有自定义的 `IQuestHost` 实现
  想要精确的进度展示，需要补一个方法实现（工作量与 `QuestHost.GetObjectiveRequiredCounts` 本身
  相当，读取任务定义目标计数即可）。

### 任务指示器：新增 `QuestGiverIndicatorQuery.Evaluate` + 四态枚举

- **实际改法**：新增文件 `core/gameplay/quest/contracts/QuestGiverIndicator.cs`：枚举
  `QuestGiverIndicatorState { None, Available, InProgress, Completable }`；静态类
  `QuestGiverIndicatorQuery` 的
  `public static QuestGiverIndicatorState Evaluate(IQuestHost questHost, Id unitId,
  IReadOnlyCollection<Id> giverQuestIds)`——遍历调用方传入的 `giverQuestIds`，对每条任务调用
  既有 `questHost.GetState(unitId, questId)`，按固定优先级 **`Completable` > `Available` >
  `InProgress` > `None`** 裁决出一个结果（同一给予者同时命中多态时只返回优先级最高的一个）。
  `questHost`/`giverQuestIds` 为 `null` 均抛 `ArgumentNullException`；`giverQuestIds` 为空集合
  恒返回 `None`；允许重复 id（不影响结果）。**框架不为"给予者"另建 id 体系**——"这个 NPC 关联哪些
  任务"完全由调用方（消费方）按自己的内容编排给出，框架只负责聚合裁决这些任务在当前玩家状态下的
  结果。是纯新增独立静态类，**不是** `IQuestHost` 的新接口成员（`IQuestHost` 有多处独立假实现，
  加接口成员会破坏它们的 ABI，理由与既有的 `QuestPrerequisitePreview` 判断记录一致）。
- **消费方需要怎么改用法**：消费方自己维护"某个 NPC 关联哪些 `quest.def` id"这份映射（gossip
  菜单里 `accept_quest`/`turn_in_quest` 动作引用的任务集合，或消费方自己另外的编排来源），拿到
  某个 NPC 单位 id 与其关联任务集合后调用 `QuestGiverIndicatorQuery.Evaluate(questHost, playerId,
  giverQuestIds)`，按返回的四态枚举自行决定头顶图标的形状/颜色/动画——**框架只给状态判定，具体
  呈现（含消费方之前设计参考里提到的头顶 `!`/`?` 视觉效果）一律由消费方自己实现，框架不提供任何
  默认渲染**。
- **有无迁移成本**：无迁移成本（纯新增能力，不改动任何既有类型）。**明确边界**：本查询不区分
  "完全不可接"与"只差一点点（如等级差 1 级）"这类中间态（均落入 `None`）——如果消费方的设计参考
  （魔兽世界灰色感叹号）需要这类中间态预告，需要消费方自己解读 `prerequisite` 表达式，框架本轮
  不提供、也未承诺后续会提供（ADR-0045 决策 4 已写明这是明确排除的范围，不是遗漏）。

---

## C. 三处框架侧与消费方口径不同的地方

### C-1：`npc_flags` 不是"五个零消费"

消费方反馈 5 的核实结论已经自行更正过一次（从"没有任何消费者"改为"没有专属业务分支消费者"），
框架侧复核确认这次更正基本准确，但仍有一处可以说得更精确：**六个标志值（不只 `SummonOnly`）全部
经过一条写入路径被消费，且这条写入产物是可以被通用规则复用的**，不宜再笼统地说"五个零消费"。

- `core/carriers/creature/core/CreatureFactory.cs:208-211`（`SpawnCore` 内）：
  `foreach (var flag in template.NpcFlags) { unit.NpcFlags.Add(NpcFlagIds.ToId(flag));
  unit.Tags.Add(NpcFlagIds.ToTag(flag)); }`——对全部六个标志值一视同仁执行，把每个标志转成一个
  通用单位标签 `tag.npc.<name>` 写入 `unit.Tags`，不区分具体是哪个标志。
- 这份标签**不是死数据**，两处生产代码可以通用消费任意 `tag.npc.*`：
  `core/rules/targeting/core/TargetHost.cs:319`（`_units.GetTags(candidateId).Contains(tagId)`，
  目标过滤规则可以用它筛选/排除带某个 NPC 标志的单位）与
  `core/rules/expr_host/RulesExprHostFactory.cs:299`（`ContainsTag` Expr 函数，内容表达式可以
  按标签条件判断）。
- 消费方核实时用的 `git grep "NpcFlag\."`（带点号的窄正则）确实会漏掉这条路径——`CreatureFactory`
  内部访问的是 `template.NpcFlags`（属性名不带点号前缀这种写法）与 `NpcFlagIds.ToTag(flag)`
  （形参名 `flag`），不含字面 `NpcFlag.` 子串，这是搜索模式本身的局限，不是消费方观察有误。

**消费方核心论点仍然成立**：没有任何模块按标志语义分支去触发具体业务行为（框架不会因为标了
`Questgiver` 就自动接任务、标了 `Vendor` 就自动开店）——这一点无需更正。需要更正的只是"零消费"
这个字面表述；准确的表述是"没有专属业务逻辑消费者，但全部六值都会被写入可通用消费的单位标签"。

### C-2：掉落 RNG 消耗不对称，不宜表述为"违反确定性契约"

消费方反馈 7 的现象核实（`chance_each` 恒定消耗一次随机数、`RollCount` 在 `min==max` 时完全
不消耗）逐字核实无误，`core/gameplay/loot/core/LootHost.cs:308`/`:364` 两处位置也准确。但框架侧
认为这一现象**不应该被表述成"违反确定性契约"**，理由：

- `AGENTS.md` §3 的确定性条款字面列举的范围是"不依赖字典枚举顺序、`GetHashCode`、系统时间、当前
  文化"——本项不属于这个字面清单里的任何一条。给定固定种子/固定输入序列，`LootHost` 的输出仍然
  是**完全可复现**的（同一份数据、同一颗种子，跑多少次结果都一样），不是非确定性 bug。
- 真正的问题是**契约脆弱性**：`RngStream` 上后续消费者的取值序列，会因为"改一个逻辑上无关的字段"
  （如某条掉落项的 `count_range` 从 `{min:1,max:1}` 改成 `{min:1,max:2}`）而整体错位，且这种
  错位不会在任何测试断言或 diff 里显式报警。这是"可复现性推理"层面的脆弱点，不是确定性契约本身
  被违反。
- **消费方原文的措辞"影响随机数可复现性推理"其实是准确的**，框架侧建议消费方在自己的登记文档里
  保留这个措辞，不需要改成更重的"违反确定性契约"这类表述——本文档特意指出这一点，是为了避免这个
  问题在后续转述中被无意放大成一个它本来不是的性质。

### C-3：候选 5（原稿）持久化注册策略"两种并存"——消费方自查已排除，框架侧复核确认

消费方在反馈文档"已排除"一节已经自行核实并撤回了这条候选（原怀疑 `QuestPersistable` 走自动注册、
`RngStreamsPersistable` 走显式调用，两种策略并存）。框架侧独立复核 `core/gameplay/assembly/
GameplayAssembly.cs` 的 `RegisterPersistables(ISaveSystem, PlayerUnit)` 方法，确认
`saveSystem.RegisterPersistable(new QuestPersistable(...))` 与
`saveSystem.RegisterPersistable(new RngStreamsPersistable(...))` 在同一个方法内、走完全相同的
注册路径，该方法本身在类内未被自动调用一次，必须由外部（消费方的 `WowGameBootstrap.cs`）显式
调用——**确认这条候选确实不成立**，与消费方自查结论一致。感谢消费方在提交前自行发现并撤回，这也
是本仓库此前已经栽过的同一个坑（`docs/接入记录/已知缺口登记.md` 的 `GAP-RNG-PERSIST-SL-FARM-
DESIGN-TENSION` 条目），消费方文档里也已提到这是同一误判的第三次复发，框架侧不需要额外处理。

---

## D. 本轮未落地的两条及方向

### 反馈 1：`interact` 意图无原生 NPC/creature 路径

**设计层方向**：走**原生 creature interact 路径**，不是永久把 NPC 包装成 gobj。理由：`interact`
属于输入层意图（"玩家对准某个对象按下交互键"这一层通用语义），不应该被长期绑定到 gobj 这一个具体
的场景实体类型上——creature 应该有自己的原生消费入口，而不是要求内容作者/消费方为了让 NPC 可交互
而把它伪装成一个非生物物件。

**依赖关系**：本条依赖反馈 6（`DialogOpenerWithSource`）先落地——本轮 ADR-0044 已经完成，为
"把 NPC 包装成 gobj"这条现有绕行方案补齐了身份透传，使其在原生路径推出前保持可用。

**排期**：排到下一轮，需要单独出一份 ADR（新增一等能力：creature/NPC 版的 `interact` tick
处理器 + 对应的 `InteractResult`/`GossipView` 落地路径，工作量较大，涉及是否要在
`core/carriers/creature` 或新模块定义交互结果类型，这是需要设计层先拍板的架构决策）。

**在此之前怎么办**：消费方当前走的是自己实现的 creature 直连绕行方案（不经过 gobj），本来就不受
这条 ADR 排期影响；若消费方未来评估切换到"NPC 包装成 gobj"这条路线，配合本轮已落地的身份透传
（ADR-0044）已经可用，不需要等待原生路径 ADR。

### 反馈 7：掉落 RNG 消耗不对称

**方向**：每条掉落项按 `(loot_table_id, entry_id)` 派生独立子流，取代当前"同一宿主内所有掉落项
共享一条顺序流"的实现，使内容作者改动某条目录目跟数量骰无关的字段（或反过来）不再移动其它掉落项
的流位置。

**排期**：先出一份 ADR 定契约（子流派生规则、`LootOptions` 是否需要新增灰度开关），**代码实现
待评估**——需要评估对既有基线的影响（本仓库生产唯一一行掉落表当前配置下不受影响，但改变消耗
规则本身是一次可观察的随机数序列行为变更，`simrunner` 基线是否需要重新烘焙、是否需要一个开关做
新旧行为切换，都需要在 ADR 里给出结论，不能只是改代码）。**本条目前只是方向，不是已承诺的排期**
——是否进入下一轮、以什么优先级推进，需要设计层在 ADR 起草时一并确认。

---

## 涉及文件

**本文档**：
- `architecture/落地计划/消费方反馈-2026-09-20-wow-交互gossip任务日志掉落RNG.md`（新增，本文件）

**本文档引用、本轮已落地的既有产物（均已在 `main` `7791fed4`，不在本次提交范围内改动）**：
- `architecture/adr/0043-gossip菜单新增可选开场白正文字段.md`
- `architecture/adr/0044-gobj对话打开回调新增交互者身份透传路径.md`
- `architecture/adr/0045-任务给予者指示器只读查询.md`
- `CHANGELOG.md`（`[Unreleased]` 段对应四条条目）
