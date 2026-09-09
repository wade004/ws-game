# 框架范围、责任与证据口径

这份文件是本轮审计的责任判定入口。它把冻结架构承诺、源码现状、可选模块和具体游戏接入拆开，避免把一个游戏没有启用的能力误报成框架缺陷，也避免把框架层应提供的通用机制推给游戏层。

## 基线与权威顺序

审计基线是恢复仓 `D:\workespace\ws-game-artifacts\audit-c9ff301-resumed` 的 `c9ff30107413083188c597c0b65cf1691c9dfe9b`，版本 `1.13.0`。`D:\workespace\ws-game` 只读；本轮只重写审计文档，不重跑测试，不修改产品、既有测试、原始日志/XML/probe 或旧归档。

责任判断按以下顺序进行：

1. `architecture/00_架构总则.md:18-34` 先定义框架技术无关、游戏无关、表现与时间模型可选；`01_分层与依赖.md:45-55` 再定义各层负责和不负责什么。
2. 具体模块的契约、可选性和验收标准优先于能力索引中的旧摘要。尤其 `02_引擎适配层.md:23-24` 已把导航跨帧预算和完整空间索引化收窄为实现方自行决定的性能边界。
3. `13_新游戏接入指南.md:58-66,70-88` 说明游戏层组装、内容和口味选择；它不能改变框架层已经承诺的通用机制。
4. ADR-0018 `:20-25` 规定编辑器是外部消费项目，框架交付无头适配层和校验装配入口；ADR-0019 `:17-24` 规定复合字段登记和模块集合门禁的方向。

## 分类词典

| 分类 | 判定含义 | 责任归属 | 证据要求 |
|---|---|---|---|
| 框架既有通用契约 | 架构对所有消费方承诺的接口、解析、校验、持久化或通用机制 | 框架实现与发布门禁 | 独立 Core/consumer 或正式装配证据；具体游戏 E2E 不是唯一证明 |
| 可选模块启用后契约 | 只有显式启用某种时间模型、热重载、装备表现、model 路线或可选校验规则时才生效 | 框架提供机制；消费方负责启用并满足输入条件 | 必须记录启用条件，再用最小独立 consumer/sample oracle；未启用不判失败 |
| 游戏接入/内容 | 具体游戏的地图、场景、数据行、资源、输入、UI、装配策略和玩法选择 | 游戏仓库 | 游戏自己的接入记录或 sample/consumer 证据；不作为框架基础能力缺失的替代 |
| 宿主扩展点 | 框架提供窄契约、委托或注册入口，但行为由调用方实现 | 框架负责契约；游戏/编辑器宿主提供实现 | 以默认 `null`/恒值与显式注入两种路径分别说明 |
| 未实现的框架能力 | 架构把通用机制列为本版能力，源码没有对应运行逻辑 | 框架 | 最小通用负例/静态证据，不能用某一游戏未接线证明 |
| 明确非目标 | ADR 或正文明确排除本版范围 | 无本轮交付责任 | 引用明确 ADR/章节，不列为待办 |
| 用户暂缓 | 技术上不构成框架缺口，但用户明确本阶段不落地 | editor 本阶段不形成交付责任 | 只适用于 editor |
| 文档/metadata 漂移 | 代码与机器可读说明不一致，尚未单独证明运行时错误 | 文档/metadata 维护者 | 需要源码行、旧文案和准确替代文本 |

“已实现未默认接线”只表示可选模块的机制或入口已经存在，必须同时写明接线者和启用条件；它不等于所有游戏都必须接线。

## 契约责任矩阵

| 契约 | 框架提供 | 启用/消费条件 | 游戏或宿主责任 | 本轮证据与限制 |
|---|---|---|---|---|
| schema、统一装配、四包发布 | `FieldSchema`、递归登记、`ContentValidationAssembly`、headless 包 | 消费方使用 1.13 数据与发布包 | 内容行和具体 editor/consumer 选择表 | `check -SkipUnity`、正式 ZIP/lock、独立 Gobj/Quest probe；schema 命令本身不替代模块 coverage |
| 规则与载体机制 | Skill/Item/Creature/Gobj/Summon/Quest 等通用类型与事件 | 使用相应模块和数据表 | 提供数据、参数与扩展回调 | Core 测试和 probes；不把某游戏未配置的机制当缺陷 |
| Talent | 被动光环复用机制、模板字段和奖励分派入口 | 游戏声明 talent 数据并注入 granter 时 | 具体树、光环内容和是否需要点数管理；当前没有完整分配/激活/撤销/持久化契约，不能把它自动列为本轮框架待办 | `TalentPointGranter` 默认 null，不能称奖励已到账；若未来要承诺完整管理，先立通用契约和 owner |
| Summon | `SummonHost`、owner/follow/联动机制 | 使用召唤模块及相应时间模型 | 跟随距离、联动策略和玩家控制口味；escort/路线类行为由游戏集成 | 连续机制有源码/测试；Discrete tick 跳过由 `SummonTickHandler` 明示，属于条件支持边界，需同步文案，不作新增 P2 |
| DataHotReload | 模板 watcher、去抖、registry reload、错误事件 | 仅 Editor/Development 且 `EnableDataHotReload=true`；authoring 数据先同步到 content root | 复制/同步数据、选择开启、消费方若有缓存须遵循刷新契约 | PlayMode changed-file 正向；SkillHost stale cache 与 Deleted override 负例分别成立 |
| 装备 View | ViewBinder、装备外观 replay 与可选 model/sprite 表现机制 | 游戏选择装备表现且存在同一 View 的 SaveLoaded 路径 | 提供 DisplayInfo/资源和装配选项 | ExistingView 负例只约束该可选消费路径，不外推为所有游戏 |
| owner/day/vendor | 回调签名与 GameplayAssembly 透传点 | 游戏需要归属、日任务或 vendor UI 时 | 提供 resolver/provider/callback | 默认未提供业务回调或使用固定默认值，需按所选语义注入；不判框架缺陷 |
| Editor | headless adapter、校验装配和契约 | 外部 editor 项目按 ADR0018 消费 | editor UI/游戏专属面板 | 本阶段用户暂缓；不把框架仓缺 editor UI 列漏实现 |

## 六项 P2 的最小通用审计卡

| Finding | 所有者 | 仅在何时启用 | 正确行为判定（预期，非已通过） | 最小通用修复验收 |
|---|---|---|---|---|
| ABI/API | 框架发布与兼容策略 | 已编译旧 consumer 或源码注册旧 public rule | A（继续 MINOR 兼容）：换正式 1.13 DLL 不重编译仍运行 0；B（按 11 接受破坏性变更）：迁移后的 consumer 通过，旧 binary 失败保留为已知破坏 | A 保留旧 ctor/rule façade；B 按版本/迁移契约发布并完成五个 public 类型迁移，不能只改 changelog。详见 [project-findings.md](project-findings.md) |
| Gobj `expected` | 框架 schema/业务 validation | 使用 `gobj.lock.requirement=world_flag` | formal assembly 对缺字段返回 blocking/error；合法 Bool/Number 通过 | 缺失/错型在报告阶段拒绝，不能到 `LockDef` 才抛异常 |
| SkillHost cache | 框架 SkillHost/registry 热重载契约 | Editor/Development 热重载已开启且 resident host 继续使用 | 同一 host reload 后 BaseValue 99、cooldown 5，与 fresh host 一致 | cache 按 reload generation 清理/重绑，或明确强制重建并让契约与模板一致 |
| Deleted override | 框架模板 watcher/registry 热重载契约 | Editor/Development 双根 framework/game 覆盖且删除 game 文件 | 删除 game override 后自动回到 framework 值 1，无手工 Reload | watcher 处理 Deleted；变更/创建/删除三类事件和错误状态分别覆盖 |
| Quest union | 框架 validation assembly/Quest 业务 rule | 使用 `rewards.world_flags[].value` | `[]` 在 formal report 阶段 blocking/error；Bool/Number/String/`{$id}` 合法值通过 | 校验与 `ExprValueJson.Parse` 联合一致；不能接受运行时 `FormatException` 作为门禁 |
| ExistingView | 框架可选装备表现的 SaveLoaded 消费路径 | 启用装备外观且场景已有 View，A→空装备 B 读档 | live equipment=0 且既有 View/socket child=0；View identity 可保留 | SaveLoaded 先按真实快照清旧再应用新快照，覆盖空装备；不重发全局业务装备事件 |

## 证据用途与限制

| 证据 | 能证明 | 不能证明 |
|---|---|---|
| 源码/架构行号 | 当前责任、调用关系、默认值、是否存在逻辑 | 真实引擎运行、所有游戏接线、性能上限 |
| `check.ps1 -SkipUnity` | headless build、Core tests、数据/schema/toolchain/package 检查及其 PASS/SKIP/WARN 计数 | Unity、具体游戏 E2E、standalone/IL2CPP；本轮不重新执行 |
| 独立 Core consumer/probe | 框架契约的最小可复现语义和负例 | Unity 文件 watcher、真实渲染、任意游戏装配 |
| Unity full/filtered XML | 外部副本中指定模板/sample 路径的 Runtime 行为 | 框架对所有游戏的保证；诊断 test 的“通过”不等于功能正确 |
| 正式 ZIP/lock 流 hash | 发行 entry、manifest 版本和 lock 一致 | 冻结源码重建 DLL 的正式发行身份；两者 hash 不同不自动表示损坏 |
| ABI runner | 已编译 consumer 在正式 DLL 替换后的兼容结果 | 迁移后所有外部 API 的完整兼容性 |

本轮责任复核复用已保存的历史 headless、Core、Unity 和 ABI 结果，未重新运行测试。所有运行结果均保留原始日志/XML；报告只重写解释和链接。

## 旧结论变更映射

| 原表述/可能造成的误读 | 本轮准确口径 | 依据 |
|---|---|---|
| 原矩阵把 talent、召唤生命周期、FindUnits 混列在未实现/未接线，责任不清 | 07:303-304 只承诺天赋被动光环复用，未形成完整 talent 管理契约，不能自动生成框架待办。召唤 follow/owner 机制由框架提供，游戏只选参数和接入；Discrete expiration 是条件支持边界；`FindUnits` 仍是单独的框架便利 API 现状 | 07:294-304；`SkillHost.cs:169-175`；`SummonTickHandler.cs:53-62` |
| SampleNewGameStarter 未清全状态是框架缺陷 | 这是 `NewGameStarter` 可替换的最小模板示例；新局策略由游戏提供 | `SampleNewGameStarter.cs:8-22`；13 接入指南 |
| 孤儿检测、跨帧寻路、全索引空间查询是发布风险 | 孤儿检测在 04:310 为建议；02:23-24 已收窄性能边界；只记录实现现状与可选需求，不列框架 P2 | 04:310；02:23-24 |
| 热重载要证明所有 host/所有游戏 | 只对启用 Editor/Development 模板且继续使用 resident host 的消费路径提出缓存契约；authoring→content root 同步是接入步骤 | `games/_template/README.md:159-176`；`GameBootstrap.cs:202-221` |
| ExistingView 是所有表现/游戏的通用缺陷 | 它是启用默认可选装备表现、已有 View 的 SaveLoaded 消费契约；新建 View replay 已有 | 09 表现层；`ViewBinder.cs:125,234,248` |
| `-SkipUnity` 或具体游戏测试代表完整发布门禁 | 本轮只复用历史结果；11 变更记录/§8 与 13:150 保留独立 consumer smoke 门禁边界，具体游戏 E2E 不等于框架证明 | `validation.md`；11；13:150 |
| 旧 portable ZIP 是本轮权威交付 | 旧 ZIP/candidate 是历史输入；`framework-scope-review.zip` 由本矩阵和本索引定义内容 | `evidence-index.md` |
