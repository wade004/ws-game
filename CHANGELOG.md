# 变更日志

本文件记录 ws-game（游戏技术基础架构框架仓库）各构建产物版本号之间的变更，格式遵循
[Keep a Changelog](https://keepachangelog.com/) 惯例；版本号遵循语义化版本
（[SemVer](https://semver.org/)）：`MAJOR.MINOR.PATCH`——MAJOR 表示不兼容变更（走 ADR 审批的
契约签名变化、存档格式不兼容、数据表字段删改）；MINOR 表示向后兼容的新增能力；PATCH 表示缺陷
修复与文档勘误。单一版本源见仓库根 `VERSION` 文件；版本号与发布流程见根 `README.md`"版本与发布"
一节。

## [Unreleased]

（尚未发布的变更累积在此，随下一次 `build.ps1 -Release` 归档为对应版本号的条目。）

## [1.0.0] - 2026-09-07

首个正式基线版本。此前 `0.1.0`/`0.2.0` 均为落地过程中的里程碑快照（供消费方演练与打包流程自测
使用，未作为正式对外发布版本），`1.0.0` 是阶段 0～5 全部完成、经三轮内部审计与一轮外部深度审核
修复收口后的第一个"可供真实游戏接入"的稳定基线。

### 新增

- **阶段 0～5 全部完成**：环境与仓库骨架、L0 基础层（12 模块）、L1+L2 数值与规则层、L3+L4 载体
  与玩法层、Unity 适配层 + 表现层 + UI 套件、美术管线与资产规格；`Core.sln` 六个测试工程合计
  1550+ 例单测全过，Unity EditMode/PlayMode 测试全绿，独立版无人值守冒烟（连续/离散两种时间
  模型）通过，消费方演练（从零搭建独立于框架源码树的最小 Unity 工程，只以分发包为输入）通过。
- **离散时间模型**：与连续时间模型并列的第二套时间驱动方式（`TurnScheduler`、先攻策略、行动点
  移动预算、回合 HUD 等），玩家意图经 `WorldSim` 路由到调度器，回合结束统一推进计时器。
- **框架级数据目录分层**：`data/_framework/`（事件词汇登记表、输入动作声明等，随分发包交付）与
  `data/_sample/`（框架自测数据，不随分发包交付）分离；`DataRegistry` 支持多根合并加载（主键/
  schema 冲突阻断）。
- **新游戏模板** `games/_template/`：可运行的最小闭环骨架（`GameBootstrap`/`GameOptions`/
  `Editor/GameSceneBuilder`/`data/game/`/`validate.ps1`/PlayMode 冒烟测试），对照 13 号文档口味
  配置项清单逐行落地。
- **资产管线**：`toolchain/import_assets.py` 资产导入工具、`assets/_placeholder/` 通用占位资产
  包、方向档位/纸娃娃分层/序列帧图集等资产契约（14 号文档）。
- **一键门禁** `check.ps1`（22 步）与提交前钩子 `.githooks/pre-commit`（快速子集）、持续集成
  `.github/workflows/ci.yml`（非 Unity 门禁子集）。
- **版本管理方案**：语义化版本、`CHANGELOG.md`、`build.ps1 -Release`/`-DryRun`/`-Publish`、
  维护分支流程（`release/X.Y.x`）、发布工作流 `.github/workflows/release.yml`、游戏侧引用工具
  `toolchain/get_framework.ps1` 与锁文件 `ws-game.lock`。

### 修复

- **三轮内部文档代码一致性审计**（2026-09-05～2026-09-07）：逐轮核对 00～14 号架构文档与实现的
  一致性，修复审计发现的代码缺失（行动点、`SpellModDimension.Charges`、离散 GCD 接线、死亡复活
  三策略执行主体、种族被动光环应用、回合状态占位值等）与文档勘误，详见
  `architecture/落地计划/文档代码一致性审计_2026-09-05.md`、`_2026-09-06.md`、`_2026-09-07.md`。
- **外部深度审核 33 条发现根治**（分支 `codex/deep-review-b3b91ee-20260907`，报告见
  `architecture/落地计划/audit-b3b91ee-20260907/`）：FND-01～10、GP-01～10、RC-01～11、
  TOOL-01/02 共 33 条经三波并行核实全部成立并根治；排障过程中额外发现并修复 PlayMode 全量套件
  `VerticalSliceTests` 因跨夹具存档槽配额累积导致的隐性失败。
- **缺口收敛 G1/G2/G3**（2026-09-05）：16 条已知契约缺口中 13 条落地解决（`AnchorResolver`、
  `SpawnRequester`、`TeleportResolverDelegate`、`SaveRequesterDelegate`、回合状态显示等），3 条
  设计层判断维持"保留"（非拍板内容或本就只需单点承担的既定设计）。

### 兼容性说明

- 存档格式：`save_version`（存档信封层字段，迁移链唯一依据）当前为初始版本，尚无历史存档需要
  迁移；后续存档结构不兼容变更须递增 `save_version` 并登记迁移函数（见
  `architecture/10_存档与持久化.md` 第 5 节）。
- 数据表：各表独立的 `schema_version`（见 `architecture/04_数据与内容管线.md`）随本版本一次性
  确定，表结构不兼容变更（字段删改）须递增 `schema_version` 并提供迁移路径。
- 构建产物公开契约：六个核心 DLL（`Core.Foundation`、`Core.Numbers`、`Core.Rules`、
  `Core.Carriers`、`Core.Gameplay`、`Presentation.Common`）随分发包 `dist/1.0.0/` 交付；对外公开
  的 L-1 接口签名（引擎适配层契约）、事件 key、数据表结构、存档结构变更均属 MAJOR 级变更范畴。
- 与 `0.2.0` 的差异：`1.0.0` 不改变任何公开契约或数据结构，只是把此前若干里程碑快照正式确立为
  第一个语义化版本基线，并新增本文件描述的版本管理方案本身（`build.ps1`/`check.ps1`/工作流/
  文档新增的发布相关能力）。

## [0.2.0]

里程碑快照（供内部打包流程与消费方演练自测使用）。收录工程收尾 K、加固波 J（契约一致性测试套件、
消费方演练脚本、PlayMode 隔离）、第三轮审计修复波（W1～W4）、第四方深度审核修复三波、离散时间
模型引擎侧接线、`found.time_model` 归属勘误、框架级数据目录与新游戏模板等一系列提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。

## [0.1.0]

首个里程碑快照。收录阶段 0～5 全部完成、缺口收敛 G1/G2/G3、框架收官（离散时间模型初版落地、
文档/数据/门禁收尾）、收边波 I/J1 等提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。
