# ws-game 1.16.1 文档—代码—验证矩阵

审计输入是冻结副本 `D:\workespace\ws-game-artifacts\audit-24a11fe-frozen`，HEAD 为 `24a11fe28f9647cd532c41f56f7ab18c00fb8516`。本矩阵只记录当前源码和当前验证证据；没有把缺少 Unity/消费方运行证据写成能力缺失。

| 文档/主题 | 当前代码与合同锚点 | 当前证据 | 结论/责任 |
|---|---|---|---|
| 00 总则、01 分层 | `architecture/00_架构总则.md`、`01_分层与依赖.md`；Core 与 Presentation 合同 | `check-skipunity-transcript.txt` 的 .NET build/test | 框架与具体游戏解耦边界保持；未发现游戏内容耦合 finding |
| 02 引擎适配层 | `architecture/02_引擎适配层.md`、`core/foundation/engine_adapter` | 静态阅读；Unity 相关步骤按 `-SkipUnity` 跳过 | 本轮未测性能优化路径，不据此判定能力缺失 |
| 03 运行时骨架 | `architecture/03_运行时骨架.md`、`core/foundation/sim_loop`、`scene_router` | 静态阅读；无 Unity 重跑 | ATB 是预留延期项，本版不执行，不展开为能力缺陷 |
| 04 数据与内容管线 | `architecture/04_数据与内容管线.md`、`core/foundation/data_registry` | 合并根、`data/_framework`、schema audit 均有 check 证据；另有 precompiled validator JSON probe | 校验链当前可用；孤儿检查/业务覆盖仍按文档责任矩阵解释 |
| 05 对象模型与世界 | `architecture/05_对象模型与世界.md`、`CarriersAssembly.cs:89-103`、`core/carriers` | 代码静态证据；未声称 Runtime | 默认索引不登记 `gobj`；自定义共用索引时由宿主过滤，属于已知边界 |
| 06 规则层（属性/技能/战斗/AI） | `architecture/06_规则层_属性技能战斗AI.md`、`SkillHost.cs:247-365`、`RulesAssembly.cs:359-360` | `FindUnits` 代码及默认 `Spatial` 注入；ABI strict 1.12 probe | `FindUnits` 已实现并默认接线；旧 README 的“恒空”是文档漂移 |
| 07 载体层（物品/生物/物件） | `architecture/07_载体层_物品生物物件.md`、`core/carriers` | .NET/静态证据 | 天赋完整点数管理是游戏责任；连续/离散召唤等按现有责任表解释 |
| 08 玩法层（掉落/任务/对话/关卡） | `architecture/08_玩法层_掉落任务对话关卡.md`、`core/gameplay/quest` | .NET/静态证据 | Quest 状态机/框架机制属于本层审计对象；escort、轨迹碰撞等具体策略/内容由游戏决定 |
| 09 Presentation | `architecture/09_表现层.md`、`ShellHostTypes.cs:25-37`、`IShellHost.cs:36` | Presentation .NET tests；Unity/消费方未在本任务重跑 | `NewGameStarter` 是游戏/宿主注入责任；未注入后抛错按合同处理，不是框架缺陷 |
| 10 存档与持久化 | `architecture/10_存档与持久化.md`、`core/foundation/save_system` | .NET tests；未声称跨进程 Runtime | 仅以已有测试证据为准，不扩大为消费方证明 |
| 11 工程规范与门禁 | `architecture/11_工程规范与测试.md`、`check.ps1` | 唯一一次 `check.ps1 -SkipUnity`：24 steps，17 PASS/7 SKIP | 这是受限门禁，不是完整 release certification；ABI 默认基线缺失为 SKIP |
| 12 变更流程 | `architecture/12_扩展与变更流程.md` | ADR/index 静态核对 | 需补 ADR 20–22 索引说明 |
| 13 新游戏接入 | `architecture/13_新游戏接入指南.md`、`games/_template` | 静态代码/合同；消费方演练按 `-SkipUnity` 跳过 | 起始状态、口味配置、具体内容由游戏实现 |
| 14 资产规格 | `architecture/14_资产规格书模板.md` | import/check 与静态证据 | 资产内容不属于框架能力缺口 |
| ADR-0020 | `architecture/adr/0020-表达式词法器纳入公开契约.md`、`core/foundation/expr/core/ExprLexer.cs`、`ExprParser.cs` | `schema/raw/schema-consumer-rerun.log` 记录 token span、parser 复用及 F-03 溢出路径 | 普通 token/span 与错误位置符合观察合同；超范围整数在正式 `LoadAll` 外抛，见 F-03 |
| ADR-0021 | `architecture/adr/0021-字段登记表纳入数值范围约束.md`、`FieldRange.cs`、`DataRegistry.cs` | schema/data checks；真实框架 `skill.aura_def` Infinity probe | 合法有限范围/导出路径通过；F-01 发现 JSON `1e309` 进入 Infinity，F-02 发现公开非有限边界防御观察 |
| ADR-0022 | `architecture/adr/0022-登记表补充导航与编辑元数据.md` | direct `validator --list-tables --json` 与 wrapper JSON probe | validator 直接导出 `tables_list/field_meta`；wrapper 仅透传 `--json`，需澄清文案 |
| Schema/Expr 当前验证 | `schema/schema-findings.md`、`schema/raw/presentation-consumer-rerun.log`、`schema/raw/schema-consumer-rerun.log` | .NET 8 独立 consumer + 真实 `ContentValidationAssembly` | F-01 非有限 Number 放行、F-03 Expr 整数溢出外抛为 P2；F-02 非有限 Range 边界为 P3 API 观察 |

矩阵按本轮变更与历史基线复核；未变化模块复用既有静态结论与当前回归证据，不宣称穷尽每行。`architecture/README.md` 的清单仍在 ADR-0019 截止，根 `README.md` 仍写“19 条 ADR”，见 `project-findings.md`。
