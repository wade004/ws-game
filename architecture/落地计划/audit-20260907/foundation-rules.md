# Foundation / Rules 文档代码对照审查（2026-09-07）

审查基线为 `main` 的 `8d7057c049aa`（工作树 `D:/workespace/ws-game-main-audit`）。本报告只以该基线的当前代码、README、测试和 `architecture/00`～`07`、`10` 正文及相关 ADR 为证据；历史审计仅用于定位线索，未直接当作结论。没有修改生产代码或原有架构文档，也没有运行全套测试。

## 覆盖矩阵

| 文档/模块 | 对照范围 | 结果 |
|---|---|---|
| `architecture/00_架构总则.md` | 分层边界、依赖方向、基础契约 | 已核对；本报告只记有实际调用链证据的问题 |
| `architecture/01_分层与依赖.md` | L0～L4 模块表及依赖 | 已核对；`foundation → numbers → rules → carriers → gameplay` 项目引用保持单向 |
| `architecture/02_引擎适配层.md` | L-1 窄契约、引擎隔离 | 已核对；未发现与本审查主题有关的确定缺口 |
| `architecture/03_运行时骨架.md`、ADR-0013 | 连续/离散驱动、时间字段 | 已核对；将显式可选的时间 provider 视为扩展点，未把未装配离散模式误报为缺失 |
| `architecture/04_数据与内容管线.md`、ADR-0015 | Expr 九组、共享 schema、运行期宿主 | 已核对；发现 L2 targeting/skill 工厂与完整工厂分裂（P2） |
| `architecture/05_对象模型与世界.md` | PlayerUnit、世界与载体状态 | 已核对；`PlayerUnit.ArchetypeId/Talents` 有状态但无对应存档实现 |
| `architecture/06_规则层_属性技能战斗AI.md` | Proc、target chain、AI、Expr 使用场景 | 已核对；Proc condition、target filter 均由实际 `IExprHostFactory` 求值 |
| `architecture/07_载体层_物品生物物件.md` | 物品/单位载体及状态持久化边界 | 已核对；现有 Unit/Inventory/Equipment 等实现不覆盖 progression/archetype |
| `architecture/10_存档与持久化.md`、ADR-0009 | 必填 player 段、固定顺序、RNG、回放 | 已核对；发现三项生产注册缺口及 RNG restore 确定性缺口（P1） |
| `core/foundation`（README、代码、tests） | JSON、SaveSystem、Rng、Replay、SimLoop 契约和实现 | 已核对；RNG 状态恢复及格式解析另记 P1/P3 |
| `core/numbers`（README、代码、tests） | progression、archetype、numbers 基础能力 | 已核对；能力实现存在，未提供 IPersistable |
| `core/rules`（README、代码、tests） | Expr、skill/proc、targeting、AI、assembly | 已核对；工厂引用链如实追到构造点 |
| `core/carriers`（README、代码、tests） | PlayerUnit、Unit/SkillBinding 等载体存档 | 已核对；现有实现不覆盖 progression/archetype |
| `core/gameplay/assembly/GameplayAssembly.cs` | 默认生产组装与 `RegisterPersistables` | 已核对；是必填段漏接的实际入口 |

## 确定问题

### P1-01：默认 Gameplay 存档没有 `player.progression` 实现或注册

**Doc 与 code 证据。** `architecture/10_存档与持久化.md:42-53` 将 `progression` 列为 player 段必填字段，`architecture/10_存档与持久化.md:99-112` 要求持有跨读档状态的模块实现 `Persistable` 并按固定顺序注册，步骤 3 明确包含 `player.progression`。基础段名也已声明在 `core/foundation/save_system/contracts/SaveSections.cs:21-25`、固定顺序位于 `:64-80`。但 `core/gameplay/assembly/GameplayAssembly.cs:773-784` 的装配判断明确说 `ProgressionHost` 未实现 `IPersistable` 并跳过；实际注册 `:787-821` 没有该段。当前 progression 能力本身存在于 `core/numbers/progression/core/ProgressionHost.cs:36`，契约 `core/numbers/progression/contracts/IProgressionHost.cs:12-48` 只提供注册、等级、经验和授予经验操作，没有持久化入口。

**触发场景与影响。** 走模板/Unity 的正常 bootstrap，调用 `GameplayAssembly.RegisterPersistables` 后保存一个已有等级或经验的玩家。SaveSystem 只写已注册段（`core/foundation/save_system/README.md:91-95`），因此存档没有 `player.progression`；重新创建玩家后等级/当前等级经验无法由该存档恢复，表现为静默丢进度。

**建议与验收。** 补一个拥有 `SaveSections.PlayerProgression` 的持久化实现，定义等级、曲线引用、当前等级经验等恢复语义；在 `GameplayAssembly.RegisterPersistables` 按 `SaveSections.KnownOrder` 接线。验收：经验跨级后 Save JSON 含该段；新实例 Load 后 `GetLevel/GetXp/GetXpToNext` 与保存点一致；旧档缺段按既定兼容策略有明确诊断/默认值；至少有一个 Save → Load → 继续加经验的回归测试。此项是**补代码并接线**，不是改文档，因为文档将其定义为必填。

### P1-02：默认 Gameplay 存档没有 `player.archetype` 实现或注册

**Doc 与 code 证据。** `architecture/10_存档与持久化.md:46-50` 将 `archetype_id` 列为必填，固定顺序 `:99-112` 要求在 inventory/equipment 之前恢复。段名已在 `core/foundation/save_system/contracts/SaveSections.cs:24-25` 声明。`core/carriers/unit/contracts/PlayerUnit.cs:28-35` 持有可变 `ArchetypeId` 和 `Talents`；但 `core/numbers/archetype/contracts/IArchetypeRegistry.cs:14-37` 只有模板查询和 `ApplyTo`，`core/numbers/archetype/core/ArchetypeRegistry.cs:28` 也没有 `IPersistable`。`GameplayAssembly.RegisterPersistables` 的完整实际列表为 `core/gameplay/assembly/GameplayAssembly.cs:792-820`，没有 `player.archetype`。

**触发场景与影响。** 玩家通过职业/种族模板初始化后改变职业引用或天赋，再从正常 Gameplay 存档读档。由于没有 archetype 段，读档目标只能由外部默认构造值填充，不能证明与保存点相同；装备恢复还可能在职业规则尚未确定时采用错误槽位/合法性规则。该缺口是必填段静默缺失，不是 `IArchetypeRegistry` 作为只读模板库本身的设计错误。

**建议与验收。** 在载体/玩法持久化边界补 `PlayerArchetypePersistable`，明确至少保存 `archetype_id` 和已选天赋（若天赋属于该字段语义）；在固定顺序中先于 inventory/equipment 注册和加载。验收：职业与天赋变更后 Save JSON 含段；新 PlayerUnit Load 后 `ArchetypeId/Talents` 相同；随后加载 equipment 时使用恢复后的职业规则；缺失旧段遵循迁移/兼容策略。此项是**补代码并接线**。

### P1-03：`RngStreamsPersistable` 已实现但没有默认生产注册

**Doc 与 code 证据。** `architecture/10_存档与持久化.md:99-112` 把 `rng.stream_states` 作为固定顺序第 8 段；`core/foundation/save_system/contracts/SaveSections.cs:79-80` 也有该段。实现存在于 `core/foundation/save_system/core/RngStreamsPersistable.cs:15-65`，测试在 `core/foundation/save_system/tests/SaveSystemTests.cs:136-180` 里手工注册。但生产入口 `core/gameplay/assembly/GameplayAssembly.cs:792-820` 只注册世界、玩家载体、任务、掉落、难度和可选 TurnScheduler，没有 RNG；对 `core`、`presentation`、`adapters`、`games` 的源码检索显示 `new RngStreamsPersistable` 只出现在 SaveSystem/Migration 测试，没有生产构造点。

**触发场景与影响。** 模板或 Unity bootstrap 运行并保存时，`RngHost` 已经消耗过 loot/combat 等分流随机流，但 `rng.stream_states` 不在已注册段，SaveSystem 按 `core/foundation/save_system/README.md:91-95` 不写它。读档后流从新的 master seed 懒创建，掉落、命中或 proc 的后续序列不再是保存点的后续序列，破坏 `architecture/10_存档与持久化.md:160-163` 的确定性/回放前提。

**建议与验收。** 在 Gameplay 默认组装中以当前同一 `RngHost` 注册 `new RngStreamsPersistable(rng)`，并确认注册发生在保存系统正式可用前、段顺序由 `KnownOrder` 管理。验收：默认 bootstrap 的 Save JSON 必有 `rng.stream_states`；保存后继续消耗每条已创建流，与新实例 Load 后继续消耗逐值相同；测试不能只覆盖单条流，需覆盖多个流、不同构造 master seed 和未注册时的诊断。

### P1-04：RNG 读档只覆盖保存流，未清理残留流，也未保存 master seed

**Doc 与 code 证据。** `core/foundation/rng/README.md:41-56` 明确新流由 `masterSeed + stream.Value` 派生，`Streams` 是已创建流集合，`Reset` 会清空流并更换 master seed。`core/foundation/rng/core/RngHost.cs:14-26` 保存 `_masterSeed` 并在构造时设置，`:64-82` 的 `SetStreamState` 只更新/新增指定流，而真正清理所有流和替换主种子的唯一方法是 `Reset`。然而 `core/foundation/save_system/core/RngStreamsPersistable.cs:38-65` 的 `Load` 对 JSON 每一项直接 `SetStreamState`，没有 `Reset`，也没有任何 master seed 字段。回放路径同样在 `core/foundation/save_system/core/ReplayPlayer.cs:53-65`、`:80-113` 用 `factory(0UL, bus)` 后逐条 `SetStreamState`；契约注释 `core/foundation/save_system/contracts/Replay.cs:487-498` 反而声称工厂主种子“不重要”。

**可重复证据。** 可提交附件为 [Program.cs](repro-rng/Program.cs) 与 [RngRestoreRepro.csproj](repro-rng/RngRestoreRepro.csproj)，其 `ProjectReference` 使用相对路径指向 `core/foundation`；编译输出写入附件目录下的 `bin/obj`（均为忽略目录）。从工作树根执行：`dotnet run --project "architecture/落地计划/audit-20260907/repro-rng/RngRestoreRepro.csproj"`。输入设计是：保存只创建过 A；实验 1 用同 master seed，在首次 Load 后、再次旧档 Load 前创建并推进 B，再与干净 Host 比较；实验 2 用不同 master seed，比较已保存 A 与保存后首次创建 B。程序同时用 `JsonWriter.Write` 打印保存段，并打印 live 旧档 Load 后集合和 clean 创建 B 前集合。

实际输出关键行：

```text
saved={ "rng.audit.a": "89f5dd5e03b2a2b9-45acb443e957d3b7-aae948b7069e4970-3751042a1a922f66" }
case1_same_master_seed_stale_stream
live_streams_after_reload=rng.audit.a,rng.audit.b
clean_streams_before_B=rng.audit.a
live_B_after_reload=0.7722367902808148,0.6078030699829997
clean_B_after_reload=0.1527649558409887,0.4655863791651631
equal=False
case2_different_master_seed_new_stream
A_equal_after_restore=True
B_different_after_restore=True
short_state_accepted=True
```

实验 1 的两个 Host 都是 `1234`，差异只来自旧档 Load 没清除已创建并推进的 B；实验 2 的已保存 A 恢复一致，但保存时不存在、首次使用于 Load 后的 B 随 master seed 不同而不同。`ReplayPlayer` 的 `factory(0UL)` 路径具有相同边界：录制起点后第一次使用的新流没有状态条目时，结果依赖工厂的主种子，而契约却允许忽略传入 seed。

**建议与验收。** 存档格式要么保存 master seed，要么将“所有可能在未来首次访问的流”的派生根纳入可恢复状态；读档前至少执行等价于 `Reset(savedMasterSeed)`，再按 JSON 恢复流。Replay factory 也应收到保存的 master seed，或明确禁止录制起点后出现未记录流并在运行期拒绝。验收：同种子残留流、异种子新流、`Load` 与 `LoadDiscrete` 三组测试均能证明保存后后续输出一致；旧档迁移策略和格式版本明确。此项是**补代码并调整 replay/save 契约**，不是只改注释。

### P2-01：RulesAssembly 为 Targeting/Skill 保留了不完整的 ExprHostFactory

**Doc 与 code 证据。** `architecture/04_数据与内容管线.md:268-282` 将 `self`、`target`、`combat`、`time` 等列为宿主分组，`:287-299` 要求内容校验和运行期使用同一登记/求值语义。对本问题有确定证据的是 `self.is_casting`/`combat.is_casting` 与 `time.turn_index`/`time.round_index`/`time.is_my_turn`：它们属于 L2 基础 schema 和 `RulesExprHostFactory` 内置查询；`architecture/06_规则层_属性技能战斗AI.md:165-174` 允许 `skill.proc_def.condition` 使用宿主分组，`:318-338` 的 `TargetChainDef.filters` 是数据驱动的 Expr 过滤。

实际构造链为：`core/rules/assembly/RulesAssembly.cs:198-220` 首先创建 `RulesExprHostFactory`，传入 `extraGroups: null, skillHost: null`，然后把**同一实例**注入 `TargetHost` 和 `SkillHost`；`SkillHost` 在 `core/rules/skill/core/SkillHost.cs:85-101` 把该工厂传入 `ProcHost`，而 `core/rules/skill/core/ProcHost.cs:105-110` 在每次 proc condition 求值时使用它；`core/rules/targeting/core/TargetHost.cs:225-230` 在过滤 Expr 求值时也使用它。随后 `RulesAssembly.cs:229-248` 新建第二个工厂（带 `skillHost: Skill`），但只给 `AiHost`。第三个完整工厂在 `core/gameplay/assembly/GameplayAssembly.cs:338-362` 才注入 `world/quest/player`、Skill 和离散时间 providers，使用方是 L4（如 `GameplayAssembly.cs:376-429`）。

`core/rules/expr_host/RulesExprHostFactory.cs:81-90` 明确 null `skillHost` 时 `is_casting` 返回 false 并警告一次，`:453-472` 明确未注入离散 providers 时 `turn_index/round_index/is_my_turn` 返回 `0/0/false`；`core/rules/expr_host/README.md:31-49` 又把这些键列为宿主最小集合，并在 `:51-60` 说明 `is_casting` 是运行期语义。

**触发场景与影响。** 内容注册一条通过基础 schema 的 `skill.proc_def.condition` 或 target chain filter，引用 `self.is_casting`、`combat.is_casting` 或 `time.round_index`。表达式运行时由第 5 步旧工厂求值，`is_casting` 恒 false，离散时间恒默认，proc 可能永不触发，过滤器可能错误排除候选。`world/quest/player` 另有游戏层 schema/provider 扩展约束：部分引用会先因未 Compose schema 被内容校验阻断，不能把它们与本 P2 的确定运行期缺口统称为“全部能通过”；这里只记录它们是另一条接线边界，并保留为建议验收项。没有声称已完成 Proc 动态复现，本条的确定性来自静态构造链和 `RulesExprHostFactory` 默认分支。

**建议与验收。** 解开 Skill/Targeting/Expr factory 的构造循环，或让工厂的 skill/time/extra provider 可在 SkillHost 完成后绑定；同时保证内容校验使用的 schema 与最终运行期工厂一致。若确实要限制 L2 Targeting/Proc 的可用键，则应在 04/06、schema 和校验器同步写成显式限制并拒绝其它引用。验收：用真实 `SkillHost.IsCasting=true`、非零 `TurnScheduler.RoundIndex`、一条 extra group provider 构造 proc/filter，断言求值读到真实值；用未装配 provider 的极简场景仍保留已有默认/诊断语义。

## P3 确定问题

### P3-01：`RngStreamState.TryParse` 接受非规定的短段文本

`core/foundation/rng/README.md:51-53` 与 `core/foundation/rng/contracts/RngStreamState.cs:30-45` 都规定四段、每段 16 位、小写、定长补零的稳定文本；但实现 `:52-85` 只检查分段数，再用 `ulong.TryParse`，没有检查每段长度、定长或规范字符。可运行复现输出 `short_state_accepted=True`，说明 `"0-0-0-0"` 被接受。现有测试 `core/foundation/rng/tests/RngHostTests.cs:116-123` 只测空串、段数和非法十六进制，未覆盖短段。

触发场景是外部/损坏存档带短文本；当前 Load 会在 `RngStreamsPersistable.cs:58-63` 接受并写入状态。数值上它可能代表同一状态，但绕过了文档承诺的规范格式，导致格式校验、迁移、签名/比较工具对同一状态出现多种文本表示。建议收紧 parser（每段恰好 16 个 ASCII 十六进制字符；是否接受大写需明确），并补短段、长段、大小写和边界值测试。验收为非规范文本返回 false/Load 抛 `FormatException`，`ToString` 输出仍可往返。

## 明确未判为问题的范围

- `world/quest/player` 是 `IExprGroupProvider` 扩展点，`core/rules/expr_host/README.md:104-110` 说明 L2 基础 schema 不可能穷举游戏层 key；L4 `GameplayAssembly` 已有完整 provider 接线。因此本报告只记 Targeting/Skill 已实际拿到旧工厂时的语义不一致。
- `time.turn_index/round_index/is_my_turn` 在未装配离散模式时返回占位值是代码与 README 明确约定的可选行为；问题仅在文档允许的键进入了不带 provider 的已注入 Targeting/Skill 工厂。
- 现有单流 SaveSystem 测试 `core/foundation/save_system/tests/SaveSystemTests.cs:136-180` 验证了“已存在且已保存的流”续接，不能覆盖 P1-04 的残留流/未来新流边界；因此没有把该测试当成通过证据。
- 本报告没有把历史审计中的旧结论直接迁入，也没有把父审查负责的 Snapshot digest（只含 event keys 与实体 id/pos/depth，不含 HP/伤害/背包）重复定级；那属于总报告另一证据边界。

## 代码修复后的文档同步清单

以下是实现修复完成后必须一起更新的文档/契约，避免存档字段和初始化边界再次漂移：

- `architecture/10_存档与持久化.md`：补齐 progression/archetype/rng 的实现状态、字段结构、固定加载顺序、master seed 保存与未记录新流的边界。
- `architecture/04_数据与内容管线.md`、`architecture/06_规则层_属性技能战斗AI.md`：明确 Targeting/Proc 可用的 Expr 键、校验 schema 与运行期 factory 的共享边界，以及离散时间 provider 未装配时的行为。
- `core/foundation/rng/README.md`、`core/foundation/rng/contracts/RngStreamState.cs` 相关契约注释：同步 master seed/Reset/未来流派生规则与规范状态文本校验。
- `core/foundation/save_system/README.md`、`core/foundation/save_system/contracts/Replay.cs`：同步 `RngStreamsPersistable.Load` 和 `ReplayPlayer` 的初始化要求，不能继续声称任意 factory master seed 都无关。
- `core/rules/expr_host/README.md`、`core/rules/assembly/README.md` 及相关 Expr schema/契约注释：记录 Skill/Targeting/Ai 各宿主实际共享的 provider，修复后删除过时的“旧工厂不影响正确性”说明。
- `core/gameplay/assembly/GameplayAssembly.cs` 注册清单注释及 progression/archetype 持久化实现 README：实现接线后同步段名与职责，确保代码注释不再记录跳过必填段。

## 证据边界

本次结论来自当前 main 基线的静态逐行对照、源码检索、已有聚焦测试阅读，以及上述最小 .NET 复现。复现验证的是 `RngHost`/`RngStreamsPersistable` 的确定性边界，不等价于 Unity 全链路运行证明；没有运行全套测试、Unity 场景、跨平台比较或生产 Save/Load 实例。P1-01～P1-03 的“默认路径”依据 `GameplayAssembly.RegisterPersistables` 和模板/Unity 调用该入口的现有代码链，最终合入前仍应由主审运行针对性的默认 bootstrap 回归。P2 的触发条件证明了合法 Expr 键在旧工厂中会落到默认值，但未主张所有现存数据当前都引用这些键；P3 是输入格式边界问题。
