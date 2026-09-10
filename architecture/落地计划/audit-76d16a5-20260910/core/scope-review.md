# Core responsibility and contract review — 1.14.0

冻结对象为 `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`（VERSION 1.14.0）。本文件把可复现的框架契约证据与消费方内容、默认装配和 Unity 入口分开。内容字段、游戏规则、可选功能未接线不能单独升级为框架缺陷；只有公开的通用契约与其启用条件明确，且有独立 oracle 的结果，才进入动态 P2。

## 责任边界矩阵

| 范围 | 已承诺的通用契约与责任模块 | 启用/依赖前提 | 实际 oracle 证明 | 不证明的内容与结论 |
|---|---|---|---|---|
| Gobj lock requirement | `GobjSchemas` / formal validation / `LockDef` 对 `world_flag` requirement 要求合法 `expected`；由 Gobj schema 与 carriers validation 负责 | 正式表注册、`FailOnUnknownTable=true`、字段符合 `Bool/Number` 语义 | `GobjLockBoundaryProbe.current-v2.log`：缺 `expected` 被 report/block；`expected=true` 通过并由 `LockDef` 解析 | 不证明具体游戏锁配置或 Unity 内容管线；当前为框架验证通过 |
| Quest reward world flag | Quest formal rule 应拒绝不支持的数组形状并允许合法 ExprValue；QuestDefinition 负责读取已通过数据 | 正式 formal assembly；默认正式装配含 `FailOnUnknownTable=true`，WarningsAllowed 不等于 warnings block | `QuestWorldFlagValueBoundaryProbe.current-v3.log`：`value=[]` report/block，`true` 真实 `QuestDefinition` 加载且 count=1 | 不证明全部 quest 内容正确；非法 Id 异常属于 `CORE114-04` 的 validator 安全性问题 |
| Skill cache hot reload | 已声明的 template 开发工具热重载在 `DataLoadCompleted` 后，既有 `SkillHost` 的下一次新施法应使用新定义 | .NET registry 的 `Reload` + `PublishImmediate`；适用于声明支持的 template 开发工具路径，不等同 Unity `FileSystemWatcher` 入口 | `SkillHotReloadBoundaryProbe.current-v2.log`：resident host 第二次施法与 fresh host 均 damage=99/cooldown=5 | 不证明所有游戏热更、watcher 初始化、已进行中的冷却如何重写；本次没有把 watcher 当 probe 前提 |
| CORE114-01 QuestHost | `QuestHost` reload 保留运行状态时，后续合法 progress 不应数组越界；责任在 QuestHost 的状态迁移/拒绝策略 | 活跃任务、同 id definition reload、objective cardinality 变化、随后更新新目标 | `CoreBoundaryProbe.current-v5.log`：真实 host 抛 `IndexOutOfRangeException` | 不要求框架替游戏决定目标迁移语义；验收需明确迁移或原子拒绝，并保持状态可读 |
| CORE114-02 EconomyHost | vendor/item 运行库存与新 restock definition 交互应保持可补货且不丢失已消费库存；责任在 EconomyHost reload/update | 同 `(vendor,item)`、none→timer、stock=0、timer 到期 | `CoreBoundaryProbe.current-v5.log`：resident 0→0/timer null，fresh control 0→2 | 不证明经济内容平衡、下架策略或地图进入策略；只证明当前通用状态转换不满足补货 oracle |
| CORE114-03 StatHost | reload 后未显式覆盖的派生查询应看到新 definition；显式 runtime base 可按设计保留；责任在 StatHost cache/reload | 两个同定义且未 SetBase 的 unit，先查 A，再 reload default，发布事件后查 A/B | 同日志：A=0、B=77，而 `GetBase` A/B=77 | 不把显式 base 保留误报为 bug；本项是查询顺序造成的 derived cache 不一致 |
| CORE114-04 ExprValueJson | `ExprValueJson.IsValid` 是不抛异常的校验入口；Parse 的构造异常应被上层校验转换为 report；Quest/Dialog validator 负责异常安全 | formal validation 经过 Quest 或 Dialog rule；支持的值语义包含 Bool、Number/Int、String 与合法 `{$id:...}` | direct probe 与 formal Quest 日志证明非法 Id 使 `IsValid`/正式验证抛 `ArgumentException` | 不证明合法 String/Id 内容业务可接受；不把 Parse 单独抛异常等同缺陷，缺陷在 IsValid/validator 链未隔离异常 |
| Save/UI/TP regression | SaveSystem rollback、InventoryVM 刷新、Loading Dialog teleport 的既有当前契约分别由 SaveSystem、presentation VM、SceneRouter/WorldSim 负责 | 使用对应 framework assembly 与 probe doubles；TP 需 Loading 下连续公共 Dialog、post-load 重新 AddEntity | `FollowupCoreProbe.current-v3.log`、`TeleportLoadingBoundaryProbe.current-v2.log` 均达到当前 oracle | 不证明 Unity 发布 Runtime、动画/VFX、真实 FileSystemWatcher 或完整 `check` |

## 其他模块的边界审阅

- **Talent**：本次未找到完整通用 allocator 的公开承诺。07 文档的“天赋/种族复用被动光环机制”对应已有 Aura/SpellMod 能力，不等同 activate、refund、persist、reapply 的完整管理 API。若另行批准把 allocator 提升为通用框架能力，届时再定义 owner 与契约；当前不把缺少该管理系统列为框架 P2。
- **FindUnits**：`ISkillHost.FindUnits` 的公开责任是委托 `ISpatialQuery`。默认 `CarriersAssembly.DefaultSpatialSyncKinds` 仅登记 Creature、Player 与 trigger-only AreaTrigger；自定义空间索引若注入其他类别属于输入约定边界。当前没有证据证明默认装配会把 Gobj、Loot 或 Projectile 当 unit 返回，不能列动态缺陷。
- **Summon**：连续 duration 与跟随路径存在。`SummonTickHandler` 对 discrete tick 的跳过有明确设计注释，00 时间模型/07 契约没有在本轮证明必须推进离散召唤；扩展离散支持需独立契约决策，不自动排期。不能把整个召唤生命周期写成未实现。
- **Escort**：现有 `IQuestHost.UpdateProgress` 提供通用进度更新路径；未找到框架自动把所有 escort 内容接入导航、事件或 AI 的公开承诺。自动 escort 行为属于消费方接线与内容责任，除非另行批准通用契约。
- **Equipment set**：`EquipmentHost` 对新 bonuses 的应用/旧句柄清理只有静态风险线索。本轮没有完成合法 template 引用保持有效的 equip→reload→unequip resident oracle，因此只列候选，不升级 P2，也不把 reload 立刻重写已应用效果当作通用承诺。
- **Power/Archetype/Inventory/Loot/Dialog**：源码有各自 reload 与运行态保留设计。Power 的 current/max 保留且 `RecomputeMax` 对 Fixed 类型跳过是已声明边界；Archetype、Inventory、Loot、Dialog 的派生缓存或业务副作用没有本轮独立失败 oracle，不列动态缺陷。

## 证据范围

本轮新增责任复核没有重写旧 raw log，也没有把探针 exit 0 当成功。探针日志是诊断输出，语义由每项的实际 oracle 人工判定。旧 CORE/UI/TP 场景沿用当前 1.14 语义并以 `FollowupCoreProbe.current-v3.log`、`TeleportLoadingBoundaryProbe.current-v2.log` 为当前证据。框架源码镜像哈希见 `core/evidence/source-copy-hashes.txt`（1066 files, mismatch=0, missing=0）；探针与日志哈希见 `core/evidence/probe-hashes.txt`。
