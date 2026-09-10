# ws-game 1.16.2 文档核对 findings

冻结对象为 `D:\workespace\ws-game-artifacts\audit-4faab73-frozen` 的 detached HEAD `4faab73e7081f2984e7addb88fee051b6c0d3d02`（`VERSION=1.16.2`）。本文件只登记能由当前文档原文与当前源码/API 逐条复核的文档问题，并把“未提供、延期、未默认接线、游戏责任”单独列出；不把某个游戏尚未接入的玩法或资源列成框架缺陷。

## 当前需要更新的文档

以下 5 条是独立 P3 文档问题。ABI 表面覆盖范围的文档更新与 ABI-1162-01 共用同一根因和修复，列在表内但不另计一条问题。

| ID | 级别 | 文档原文证据 | 当前代码/权威文档证据 | 实际与预期 | 责任与修复验收 |
|---|---|---|---|---|---|
| DOC-162-01 | P3 文档漂移 | `core/foundation/data_registry/README.md:46-54` 写跨根主键重复“一律阻断”；`core/foundation/data_registry/core/DataRegistry.cs:183-186` 的 `LoadAll(sources)` XML 注释也写“同一主键冲突无论顺序都会报错”。 | `core/foundation/data_registry/core/DataRegistry.cs:623-665` 实际在 `_options.AllowOverride` 打开且后层行 `override:true` 时整行替换，前层 `final:true` 才阻断；`data/README.md:96-113` 已准确规定默认阻断、override/final 条件、单根忽略。 | 两处文档把“默认规则”写成了无条件规则，掩盖已支持的覆盖语义；代码仍能正确阻断默认重复。 | 框架文档责任。把模块 README 与 XML 注释统一改为 `AllowOverride + override/final` 条件，并保留默认重复阻断；验收为文档与 `data/README.md`、代码分支三方一致。该问题合并计 1 条，不重复计数。 |
| DOC-162-02 | P3 文档漂移 | `architecture/落地计划/落地方案与分阶段计划.md:260` 仍写 Release 附件为 zip/lock 加“三个 `.tgz`”。 | 根 `README.md:68-71,213-217` 与最终发布验证 `D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\run5\output\raw\formal-and-indexer.raw.txt`、`D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\run5-console.log` 均按四个包处理：`adapter.unity`、`framework-data`、`toolchain`、`adapter.headless`。 | 计划段落包计数落后于当前四包发布设计；不影响当前四包产物。 | 发布/计划文档责任。改为“四个 `.tgz`”并与附件清单同步；验收为计划、根 README、`build.ps1`/验证输出一致。历史 CHANGELOG 中的旧数字不作为当前问题重复计数。 |
| DOC-162-03 | P3 文档漂移 | `architecture/落地计划/落地方案与分阶段计划.md:1368` 称 nested teleport 元素/引用完整性“未实现，仅是数组”。 | `core/foundation/scene_router/core/WorldMapSchema.cs:60-79` 已定义共享 `PointItemSchema`（`id?`、`position?`，`position.x/y` 必填），` :92-99` 已把该 item 接到 `spawn_points`/`teleport_points`；`core/gameplay/assembly/TeleportTargetResolver.cs:20-22,111,174` 实际解析命名点。`core/foundation/scene_router/schema/README.md:26` 仍准确限制本模块不做跨表引用目标校验。 | 当前已提供嵌套结构的形状登记与校验；`id`/`position` 的可选性及传送点目标存在性不应被扩大为“已承诺的引用完整性”。计划条目把“结构未登记”和“引用目标未承诺”混成一项。 | 计划文档责任。改写为“元素结构已登记；跨表引用目标/业务必填约束按当前责任边界分别处理”；验收为不再声称“仅数组”，且不新增未经 ADR 承诺的引用门禁。 |
| DOC-162-04 | P3 文档分类漂移 | `architecture/落地计划/落地方案与分阶段计划.md:1363` 把 ATB 列为“明确非目标”。 | `architecture/adr/0013-时间模型可替换即时与回合制同一规则层.md` 决策明确本版不展开、保留预留位；`core/foundation/sim_loop/schema/TimeModelSchema.cs:23` 接受 `atb`，`core/foundation/sim_loop/core/TurnScheduler.cs:77-81` 对其抛 `NotSupportedException`。 | ATB 是合法配置值但本版延期实现，属于“预留延期”，与 `day_cycle` 的明确非目标（同计划 `:1362`）不是同一分类。此条只修分类措辞，不指控 ATB 算法缺陷。 | 架构/计划文档责任。将 ATB 分类改为“延期/预留”，保留 `day_cycle` 的非目标分类；验收为 ADR、能力索引、代码行为三方同义。 |
| DOC-162-05 | P3 诊断名不一致 | `architecture/04_数据与内容管线.md:362-363` 将 `Number`/`Int` 非有限值统一承诺为 `field_finite`，并写类型错误优先。 | `core/foundation/data_registry/core/DataRegistry.cs:1004-1012` 的 `Int` 分支先调用 `JsonNumber.TryGetInt64`，失败报告 `field_type`；`:1015-1030` 的 `field_finite` 分支只在 `Number` 的 `double.IsFinite` 失败时进入。V-03 还显示 `2^63` 可能被 `TryGetInt64` 接受为 `long.MinValue` 并通过 Int 校验，因此不能泛称所有 Int 越界都会阻断。 | 文档把 `field_finite` 的适用范围写宽；当前差异包含诊断名不一致，也与 Int 数值边界实现的独立 V-03 风险相邻，但本条不把它扩大成新的校验结论。 | 框架文档责任。明确 `field_finite` 当前针对 Number，Int 解析失败归 `field_type`；V-03 的边界缺陷由根报告单列，不能用此文档修订替代。验收为文档示例与 `ValidationIssue.Check` 实际值一致。 |
| ABI-1162-01-DOC（关联更新，不单独计数） | P3 文档关联 | `toolchain/README.md:555-559` 声称 `abi_surface` 覆盖“全部公开/受保护 API 表面”，并称新增/修改的公开签名均会被覆盖。 | `toolchain/abi_surface/SurfaceDumper.cs:126-139` 的属性签名只写 `prop.Name:PropertyType`，没有编码 `PropertyInfo.GetIndexParameters()`；因此 `public int this[int i]` 改为 `public int this[string i]` 时，旧新 dump 可保持同一属性行。delivery `run5-console.log:30-39` 的 indexer oracle 已复现旧 consumer 换 DLL 抛 `MissingMethodException`，而 surface compare 为 `breaks=0`。 | 正式 1.16.2 包本身的旧 1.12 consumer 核验通过；问题是 ABI surface 门禁与文档声称的覆盖范围不一致，不能把 surface `breaks=0` 当作完整 ABI 证明。 | 与 ABI-1162-01 共用工具修复和验收，不另计 P3。修复验收为 dump 编码索引参数并由 compare/负 oracle 捕获该变更，或将 README 覆盖范围明确收窄；同时保留正式包 consumer 实测结果的独立表述。 |

## 旧项核对结果

旧矩阵 `architecture/落地计划/audit-24a11fe-20260910/docs-project/` 中的 DOC-116-01～04、TOOL-116-01 已在当前冻结文档中有对应修订：architecture/root README 已列 23 条 ADR（`architecture/README.md:24,84-97`）；`FindUnits` 已说明默认注入与 null 降级（`core/rules/skill/README.md:80-90`）；数据 README 已列 `IdList` 引用、`field_range`、`field_finite`、Expr 检查（`core/foundation/data_registry/README.md:65-73`）；ADR-0022 已区分 `validator --list-tables --json` 与 `validate_data.py --json`（`architecture/adr/0022-登记表补充导航与编辑元数据.md:68-81`）；`check.ps1` 已明确 ignored `bin/`/`dist/` 副作用而不改 tracked 文件（根 `README.md:119-131`）。这些是已修复项，不在本文件重复登记。

ADR-0023 当前已闭环：`architecture/adr/0023-光环叠加类别为静态校验分组.md:18-23`、`core/rules/skill/schema/SkillValidationRules.cs:63-74`、`core/rules/skill/core/AuraHost.cs:77-84` 和 `core/rules/skill/tests/ADR0023_StackCategoryStaticGroupingTests.cs` 都把 `stack_category` 限定为加载期静态分组，运行时槽位为 `(target, defId, sourceKey)`。因此“同类别跨 aura 定义仍可并存”是当前契约，不是待修运行时互斥缺陷。

## 未确认存在的完全缺失通用 API

此次覆盖未新增确认“完全缺失且已承诺”的通用 API；已有实现缺陷另见根报告的 V/ABI 条目。容易被误列入此类的条目实际属于以下边界：

- `prog.xp_source.condition` 只登记 `Expr` 类型、不求值；`core/numbers/progression/README.md:45-49,137-141` 与 `schema/README.md:27-41` 明确这是本任务限定。授予前决策由游戏/宿主完成，或未来另立扩展。
- nested teleport 的元素结构已登记，但跨表引用存在性与 `spawn_points` 业务必填约束分别受当前模块边界约束；不能从旧计划文字反推一条未拍板的引用门禁。
- 孤儿记录检测在 `architecture/04_数据与内容管线.md:358` 明确是建议，不是当前门禁承诺。

## 未提供、未默认接线、延期、非目标与游戏接入责任

| 分类 | 项目 | 证据与实际责任 |
|---|---|---|
| 未提供 | 编辑器产品 | ADR-0018 只承诺 validator/无头宿主等框架交付物；编辑器产品随具体游戏走。 |
| 游戏责任 | talent 完整 allocator | 当前没有既定通用分配器契约；技能树/点数分配策略由具体游戏实现。 |
| 条件支持/上层消费 | TargetPoint | 已提供字段，cast 意图可携带落点；`SkillHost.CastSkill` 不消费，游戏/AI/辅助施法适配层按自身语义转换为目标。与 `world.map.teleport_points` 是不同功能。 |
| 条件支持/上层消费 | `world.map.teleport_points` | 元素结构已登记；命名点由 `TeleportTargetResolver` 解析；引用目标完整性不在该 schema 保证内。 |
| 条件支持 | 离散召唤/loot | `SummonTickHandler` 在 Discrete 直接跳过、`duration` 不推进；`LootExpiryTickHandler` 跳过清理但底层绝对时钟仍推进。扩大行为需另立 ADR，不是仅补注入即可自动支持。 |
| 未默认接线 | weaponVfx | Resolver 已构造并暴露，`ResolveSwingVfx`/`ResolveImpactVfxOverride` 无默认生产调用；调用方负责接线。 |
| 条件支持/调用方责任 | gatherClock | `GobjOptions.SimTime` 默认恒 0，需调用方注入模拟时钟；刷新机制已存在。 |
| 未默认接线 | Replay、`Quest.Update`、owner/day/vendor | API/实现存在，三入口只透传可选配置，默认不驱动/`null`；游戏负责选择入口与时机。 |
| 未默认接线 | `DisplayMapCoverageRule`、`FeedbackRuleValidator`、`SpawnSummonOnlyCreatureRule` | 规则本体存在，但需要显式登记、调用或查询注入；本轮 delivery 日志也记录 optional rules disabled。 |
| 延期 | ATB | 合法枚举值与预留扩展位存在，`TurnScheduler.Configure` 明确不支持；ADR-0013 本版不展开。 |
| 明确非目标 | `day_cycle` | 由 `RulesExprHostFactory` 的恒定值及计划文档明确限定；不构成框架待办。 |
| 条件支持/实现方边界 | 导航跨帧预算、空间完整索引化 | 02 已将性能要求收窄为实现方边界；具体适配器实现不代表 Core 契约承诺。 |
| 未提供 | 跨 aura 定义共享槽位 | ADR-0023 规定静态分组；运行时按定义分槽。若游戏需要共享槽位，必须另立 ADR。 |
| 游戏责任 | escort 自动路线、轨迹碰撞、完整新局重置、具体资源/动画/UI/地图 | 框架提供窄 API、最终落点和可替换 starter；自动路线、物理细节、状态初始化和内容由游戏实现。 |

## 证据边界

- 覆盖目录与计数见同目录 `doc-code-matrix.md`：architecture 00–14 为 15 个文件/5076 行，ADR 0001–0023 为 23 个文件/1046 行，指定模块 README 共 98 个/13589 行；另有真实生产装配根、合同、核心实现和工具链定点复核。
- 本轮 delivery 原始证据：`D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\output\raw\check.log`（六工程 .NET 3045 passed，Python 176 passed/4 skipped，`-SkipUnity` 17 PASS/7 SKIP）；正式 1.16.2 zip/lock、四包与 DLL/manifest 哈希及旧 consumer/indexer 结果以 `D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\run5\output\raw\formal-and-indexer.raw.txt` 与 `D:\workespace\ws-game-artifacts\audit-4faab73-20260910\delivery\run5-console.log` 为准。早期 `output/raw/formal-and-indexer.console.log` 仅作早期 formal 记录，不作为最终 indexer 证据。Unity 专项由主报告汇总，不以 `-SkipUnity` 结果代替。
- 冻结根的审计范围是静态风险导向深入抽查与既有证据增量核对，不是逐行全仓审计；未读范围包括非目标模块的全部实现细节、构建缓存/Unity `Library`/二进制内部、外部消费方仓库源码。未据此声称全发布认证。
- `check.ps1 -SkipUnity` 对 tracked 文件保持不改，但写入 `.gitignore` 覆盖的 `bin/`、`dist/` 中间物；这是验证过程副作用记录，不是产品文件变更。
