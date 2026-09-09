# ws-game 1.13.0 文档—代码对照与框架范围审计

## 结论

冻结版本的框架分层、统一装配、schema/coverage 测试和 1.12 旧问题复测基础成立。当前必须单独面对的六项 P2 是：ABI/API 迁移说明与实际兼容范围不一致、Gobj `world_flag.expected` 未在内容报告阶段拒绝、启用开发期热重载时 resident SkillHost 缓存不刷新、删除 game override 不回落、Quest world flag reward 的 union 负例迟到 parser、以及启用装备表现且已有 View 时 SaveLoaded 不清理旧外观。热重载两项只适用于启用 Editor/Development 模板的路径，ExistingView 只适用于启用装备表现且已有 View 的路径；每项的适用条件见 findings，不能泛化成所有游戏的框架阻断。

本轮重点是把框架责任和具体游戏接入解耦。Talent 完整点数管理没有既定通用契约；框架已有被动光环复用、字段和 granter 入口，不能由审计自动生成框架待办。召唤 follow/owner/联动机制由框架提供，Discrete expiration 是当前时间模型的条件支持边界。SampleNewGameStarter 是可替换的最小示例，孤儿检测是 04 的建议，导航跨帧预算和全索引空间查询已被 02 收窄为实现方性能边界；这些不再列为本轮框架 P2。

## 基线、范围与方法

审计目标为恢复仓 `D:\workespace\ws-game-artifacts\audit-c9ff301-resumed` 的 `c9ff30107413083188c597c0b65cf1691c9dfe9b`，`VERSION=1.13.0`。`D:\workespace\ws-game` 按任务只读；本轮只修改本审计根允许的 Markdown、README、索引和归档脚本，未修改产品源码、既有测试、原始 log/XML/probe 或旧 ZIP，也未提交。

核查范围包括 architecture 00—14、ADR-0001—0019、能力索引、editor docs、template 与模块 README、冻结源码、schema/coverage tests、validator/schema-audit、release workflow、正式 ZIP/lock 和真实 consumer ABI。责任权威和证据等级见 [scope-and-evidence.md](docs-project/scope-and-evidence.md)；逐章矩阵见 [doc-code-matrix.md](docs-project/doc-code-matrix.md)；完整文件清单见 [document-inventory.log](docs-project/document-inventory.log)。

本轮是责任解释复核，复用此前已保存的 headless、Core、Unity、ABI 证据，未重新运行测试。旧 review worktree 被外部清理任务删除的事实保留在 validation；它不是本轮执行动作。此前记录的 main `396b1db` 只作为上一轮快照，不能替代冻结基线，也不作为本轮更新后产品状态结论。

## 六项 P2：触发、责任与最小验收

详细 trigger→actual→expected→source→evidence→acceptance 见 [project-findings.md](docs-project/project-findings.md)。责任摘要如下：

| Finding | Owner | 只在何时适用 | 独立 oracle | 最小验收 |
|---|---|---|---|---|
| P2-01 ABI/API | 框架发布/兼容策略 | 旧编译 consumer 或旧 public rule 源码仍被使用 | 1.12 formal DLL 运行 0；替换 1.13 formal DLL 不重编译出现 MissingMethodException | A：保留旧签名/ façade 后旧 binary 替换仍 0；B：按 11 升级版本契约并完成迁移，旧 binary 失败保留为已知破坏；五个删除 public rule 逐项处理 |
| P2-03 Gobj | 框架 schema/业务 validator | 数据使用 `world_flag` lock | 已观测 formal report 缺 `expected` 为 0 error，随后 parser 抛异常；应满足 report blocking error | 缺失/错型报告期拒绝，合法 Bool/Number 继续通过 |
| P2-05 SkillHotReload | 框架 SkillHost/registry 可选热重载契约 | Editor/Development + DataHotReload 开启 + resident host 继续使用 | 已观测 existing host 为 base 7/cooldown 0、fresh 为 99/5；应满足 existing reload 后与 fresh 一致 | cache generation 清理/重绑，或明确强制重建 |
| P2-06 Quest union | 框架 validation assembly/Quest rule | 使用 `rewards.world_flags[].value` | 已观测 `[]` report 0 error 后 parser `FormatException`；应满足 report blocking；Bool/Number/String/`{$id}`通过 | validation 与 `ExprValueJson.Parse` 联合一致 |
| P2-08 ExistingView | 框架可选装备表现 SaveLoaded 路径 | 启用装备表现、已有 View、A→空 B 读档 | 已观测 live equipment 0、socket child 1；应满足同一 View/socket child 0 | 按真实快照先清旧再应用；不重发全局装备业务事件 |
| P2-09 Deleted override | 框架模板 watcher/registry 可选热重载契约 | Editor/Development 双根覆盖删除 game 文件 | 已观测 delete 后 2、manual reload 后 1；应满足删除事件自动回 framework 1 | watcher 处理 Deleted，变更/创建/删除分别验证 |

P2-05 的生产正向 changed-file reload 只证明模板 registry 路径；core resident-host 负例证明缓存边界。P2-08 只证明默认可选装备表现的已有 View 消费路径。P2-09 的诊断 test 通过是因为它捕获了缺陷，不能计成功能通过。

三项 P3 也保留在交付判断中：[project-findings.md](docs-project/project-findings.md) 的 P3-02 记录 ADR0019 runtime-set coverage 已由模块测试承载、generic `schema-audit` 仅做结构审计的文案漂移；P3-04 记录 Periodic `scaling_stat` 的 schema metadata 漂移；P3-07 记录 WorldMap `PointItemSchema` 已有部分元素结构检查但首个出生点/命名引用用途的语义与跨图引用仍是静态边界。它们不与六项 P2 的运行时负例混为一类。

## 旧问题复测与 Runtime 证据

| 项目 | 已保存结果 | 责任解释 |
|---|---|---|
| CORE-111-01 | Core 状态 oracle 恢复全部注册资源池与 InCombat，rollback 通过 | 框架 SaveSystem/persistable 通用契约；游戏定义自己的 persistable 内容 |
| UI-111-01 | 同图 Inventory VM 与 live B 直接查询一致 | VM 通用修复通过；ExistingView 是另一条可选装备表现契约 |
| TP-111-01 | 连续 Loading 请求后 router/player/WorldSim 一致 | 框架 teleport 原子性通过，不要求游戏采用同样 UI 流程 |
| NAV-111-01 | EditMode 2 个真实 oracle 通过：窄通道和对角切角 | 证明导航语义；不证明跨帧预算（02:168 已非强制） |
| SPATIAL-111-01 | EditMode 4 个真实 oracle 通过：跨 bucket、更新、卸载 | 证明结果语义；不证明所有查询必须索引化 |
| Unity 全轮 | EditMode 70/70（69 基线 + 1 Deleted 诊断），PlayMode 270/270 | 外部 template/sample 路径证据；具体游戏 E2E 不等于框架证明 |
| ExistingView 隔离 probe | 0/1，Load B 后 equipment=0、view/socket 仍在 | 正确负例，确认 P2-08，独立于全轮通过数 |

旧五项和全轮数字来自已保存日志/XML，本轮未重复运行。基线通过数与新增语义负例分开保存。

## 框架与游戏边界

框架对所有消费方负责 schema、统一装配、规则/载体通用机制、SaveSystem、adapter 窄契约及发布一致性。可选模块在启用后才形成附加契约：DataHotReload 仅 Editor/Development；model、装备 View、武器风格、命中帧按游戏口味选择；可选 validator 需要调用方传入查询/表源。

游戏负责数据行、地图/出生点用途、场景和输入、资源、表现选择、时间/节奏口味及装配策略。owner/day/vendor 是已有扩展点：框架提供签名和 GameplayAssembly 透传，默认未提供业务回调或使用固定默认值，需按所选语义注入；`dayProvider=null` 的恒定 day、`GobjOptions.SimTime=()=>0` 的固定时钟不能被改写成整个 Quest/采集能力关闭。

框架提供召唤 follow/owner/联动和天赋被动光环复用，但没有完整 talent 点数管理契约；具体游戏要不要点数学习需另行设计。`FindUnits` 是尚未实现的独立框架便利 API，不能归因于游戏未接线；`TargetHost` 的 shape 查询是不同的既有路径。escort 有 `UpdateProgress`/显式 Fail API，通用 route provider/自动路线属于游戏集成边界。VFX anchor/socket 的 09 契约写明跟随，当前 `VfxPlayer` 仅在 Spawn 解析、Update 只做回收/pending，列为可选表现契约的静态差距，不新增本轮 Runtime P2。

`day_cycle` 和 ATB 是明确非目标；editor UI 是 ADR-0018 规定的外部游戏项目，且本阶段用户暂缓。位移原语只保证最终逻辑落点（`EffectDispatcher.cs:337-341`），没有本轮证明的通用轨迹碰撞承诺，具体游戏的冲锋需求不能反推默认框架缺陷。

## 门禁与正式发行物

[validation.md](docs-project/validation.md) 保存一次历史 `check.ps1 -SkipUnity` 的完整事实：**15 PASS / 6 SKIP / 0 FAIL**，六个 Core 测试程序集 `2774 passed / 0 failed / 0 skipped`，数据/schema/toolchain/package 检查通过但 warning/override 单列。11 工程规范仍把 consumer 演练列入完整门禁；13:150 的 consumer smoke 是独立最小模板工程，不是某个具体游戏的技能/战斗/任务 E2E。IL2CPP/发布形态是可选快照门槛。本轮不把 SkipUnity 数字扩大为完整发布认证。

原仓正式 `dist/ws-game-1.13.0.zip` SHA-256 为 `cbe6261b35c1a6f4b418c28a4809dc45f283f25021f92df64a86cc5fb4f3469f`，lock SHA-256 为 `82381043d3c8644ca1f4fbf5128b3e11362d36ed35e853281f89682d58c69ddb`，lock release commit `609cec4`。ZIP 1053 entries 中七个 DLL 精确流 hash 和 lock 全部匹配，四个 package manifest 版本均为 1.13.0。重建 check-artifacts 的 DLL 不是正式 ZIP 运行证明；freeze c9ff301 与 release lock 609cec4 的来源差异单独保留。

ABI probe 证明旧七参数 `FieldSchema` consumer 只换正式 1.13 DLL 会 `MissingMethodException`；五个 public rule 类型也被删除。源码重编译可利用可选参数，但不能修复已编译 consumer，也不能自动恢复删除的 public 类型。

## 旧结论与交付索引

旧能力索引中“仅数组类型”“强制跨帧/全索引”等过期说法，以及“未实现/未默认接线”混列导致的责任不清，已按最新 02、04、07、13 和模板 README 重分类。完整变更映射在 [scope-and-evidence.md](docs-project/scope-and-evidence.md)。

旧 `ws-game-1.13.0-c9ff301-deep-audit.zip`/candidate 和源码镜像 README 是历史输入；`framework-scope-review.zip` 是本轮范围复核包。纳入/排除规则与 raw 证据继承关系见 [evidence-index.md](evidence-index.md)。
