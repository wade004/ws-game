# Core findings — 1.14.0 / 76d16a5

审计对象为冻结仓 `76d16a54e54f0f11204d97d7460563c8a0dc8cd8`（VERSION=1.14.0）。本轮使用独立 .NET probe 与隔离源码镜像；未修改产品、既有测试、原始 raw log 或 ZIP。`dotnet exec` exit 0 只表示探针完成输出，不能证明 oracle 正确。

## 已复现 P2

### CORE114-01 QuestHost reload 不迁移运行中任务的目标数组

- **源码/触发**：`core/gameplay/quest/core/QuestHost.cs:134-145` 的 `Reload` 替换定义而保留 `_progress`；`226-237` 的 `UpdateProgress` 按当前 definition 通过后直接访问 `rt.ObjectiveCounts[objectiveIndex]`。
- **合法场景**：真实 `QuestDefinition` 组装；旧定义一条 kill 目标，玩家接取并取得 1/5；reload 同一 quest id 为两条 kill 目标；随后更新新目标 index 1。
- **实际 vs oracle**：`CoreBoundaryProbe.current-v5.log` 输出 `QUEST-OBJECTIVE-RELOAD;exception=IndexOutOfRangeException`。正确 oracle 是迁移/调整旧计数数组，或原子拒绝不兼容 reload；后续合法更新不可因历史进度数组越界。
- **影响/建议**：开发期热重载可让活跃任务更新路径崩溃。P2；验收应覆盖目标增加、减少、重排，确保进度明确迁移且状态判定与新定义一致。

### CORE114-02 Economy none→timer 转换遗留 null 计时器

- **源码/触发**：`core/gameplay/economy/core/EconomyHost.cs:103-141` 对既有 `(vendor,item)` 保留 `StockState`；`555-580` 的 `Update` 遇 `TimerRemaining=null` 直接 continue。
- **合法场景**：初始合法 vendor item 使用缺省 none、库存由 2 手工降至 0；reload 同一 item 为 `restock_policy=timer, restock_timer=1`；发布 `DataLoadCompleted` 后 `Update(2)`。
- **实际 vs oracle**：日志输出 `ECON-NONE-TO-TIMER;before_update=0;after_update=0;timer_before=;timer_after=;fresh_before=0;fresh_after=2`。新定义下旧运行态应获得可用计时器并在周期到期补回当前 `stock_limit=2`；fresh host 仅作当前定义控制，不替代 resident host 证明。
- **影响/建议**：内容热更后限量库存可能永久不补货。P2；定义转换时初始化/重采样计时器，并明确 none/on-map-enter/timer 相互转换与已消费库存守恒验收。

### CORE114-03 StatHost 查询缓存与 reload 定义不一致

- **源码/触发**：`core/numbers/stat_block/core/StatHost.cs:73-82` 的 `ReloadFromRegistry` 只重建 definitions；`264-276` 命中 `UnitStats.Cache` 直接返回。`GetBase` 在 `217-240` 走当前 definition，故可与 `GetStat` 分叉。
- **合法场景**：两个相同、未显式 `SetBase` 的已注册 unit；仅先查询 A 得到 default 0；reload `stat.definition.default_base` 为 77；发布 `DataLoadCompleted`，再访问 A/B。
- **实际 vs oracle**：日志输出 `beforeA=0;afterA=0;afterB=77;baseA=77;baseB=77`。正确 oracle 是两个未显式覆盖的单位下一次访问都按新 definition 得 77；显式 runtime base 才是应保留的不同状态。
- **影响/建议**：同一时刻同一规则下结果依访问顺序变化，违反当前 reload 后下一访问见新定义的承诺。P2；reload 时失效 derived cache，或记录 explicit base 与 derived cache 的区别并提供一致重算验收。

### CORE114-04 ExprValueJson.IsValid 对非法 Id 抛异常

- **源码/触发**：`core/gameplay/common/contracts/ExprValueJson.cs:16-31,46-57`；`IsValid` 只捕获 `FormatException`，`{$id:"BAD"}` 在 `new Id` 抛 `ArgumentException`。Quest validator 调用点为 `core/gameplay/quest/core/QuestContentValidationRule.cs:207-215`，同族 Dialog 为 `core/gameplay/dialog/core/DialogContentValidationRule.cs:140-154`。
- **合法场景**：正式 `ContentValidationAssembly.Run` 默认装配，quest.def reward world flag value 为 `{"$id":"BAD"}`。
- **实际 vs oracle**：`QuestWorldFlagValueBoundaryProbe.current-v3.log` 输出 `invalid_id_formal=throws;type=ArgumentException`；该结果同时证明正式 Quest 校验链未将异常转换为 report。正确 oracle 是 formal report 给出字段错误并保持 validator 异常安全；Parse 可以抛，但 IsValid/正式校验不应把构造异常冒泡。
- **影响/建议**：恶意/错误内容可令校验入口异常终止而非形成可定位报告。P2；让 `IsValid` 捕获 `ArgumentException`（或先 `Id.TryParse`），并以 Quest 与 Dialog 两条正式路径覆盖非法 Id、非法数组和合法 Bool/Number/Int/String/Id。

## 已复验为当前正确

- **Gobj world_flag expected**：`GobjLockBoundaryProbe.current-v2.log`；正式 assembly 与 `CarriersSchemaCatalog` 均对缺 `expected` 报 1 error、阻断 registry 读取；`expected=true` 为 0 error 且 `LockDef` 成功解析 `WorldFlag`。源码 `core/carriers/gobj/schema/GobjValidationRules.cs:102-140`。这是修复确认，不是 P2。
- **Quest world_flags.value Array**：`QuestWorldFlagValueBoundaryProbe.current-v3.log`；正式默认装配（`WarningsAllowed`、`FailOnUnknownTable=true`）报告 `reward_world_flag_value_shape` 并阻断；合法 `true` 通过且 `QuestDefinition` 实际解析 1 条 flag。源码 `QuestContentValidationRule.cs:207-215`。本报告只引用当前 v1.14 重跑日志；历史 raw 不作为解释依据。
- **Skill cache hot reload**：`SkillHotReloadBoundaryProbe.current-v2.log`；registry 新值 `damage=99,cooldown=5`，同一 resident host 在第二次新施法后得到 `damage=99,cooldown=5`，fresh host 同值。FakeCombatHost 的 damage 是 `EffectContext.BaseValue`，不是 HP。源码 `core/rules/skill/core/SkillHost.cs:166-176`、`SkillDefCache.cs:42-48`。
- **CORE/UI/TP 回归**：`FollowupCoreProbe.current-v3.log` 与 `TeleportLoadingBoundaryProbe.current-v2.log`。失败读档 rollback 的 Mana/InCombat、Equipment/Progression 顺序、同图 race/class rebuild、Inventory VM 与 live direct query、连续公共 Dialog teleport（真实 WorldSim entity 在 post-load 重新 AddEntity）均达到各自当前 oracle。所有场景是 framework assembly + test doubles，未声称 Unity 发布 Runtime。

## 静态边界与未列 P2

- **Equipment set cache**：`EquipmentHost.cs:857-896` 只遍历新 bonuses 并按 threshold 记 `_appliedSetBonuses`；定义删除/重排 threshold 后旧句柄释放路径需要继续审查。本轮未完成合法 item.template 引用保持有效的 resident equip→reload→unequip 独立复现，故不升级 P2，也不把“reload 立刻重写已应用效果”当通用承诺。
- **Power/Archetype/Inventory/Loot/Dialog**：当前源码有 reload 订阅；Power 明确保留 current/max，且 `RecomputeMax` 对 Fixed 类型按设计跳过；Archetype 已应用效果与 Inventory/Loot runtime state 的保留均需由消费方选择重算策略。本轮没有独立失败 oracle，不列动态缺陷。
- **Talent**：未找到完整通用 allocator 的公开承诺；07 文档所述是复用被动 Aura/SpellMod 机制，不等同 activate/refund/persist/reapply 管理 API。若另行批准提升为通用框架能力，才定义 owner 与契约。
- **Summon/escort/FindUnits**：连续 duration 与跟随路径存在；离散 Summon tick 明确是当前设计边界，扩展离散支持需独立契约决策，不自动排期。`ISkillHost.FindUnits` 委托 `ISpatialQuery`; 默认空间同步只登记 Creature/Player/trigger，消费方自定义索引责任不能推成当前通用 bug。本轮未将这些静态边界列 P2。

## 证据与复现

- 独立源码：`core/repro/`；build 镜像：`build/core/source/`；独立 DLL 输出：`build/core/out/<probe>/<probe>.dll`。
- 本轮证据日志：`core/logs/*current-v2.log`、`*current-v3.log`、`CoreBoundaryProbe.current-v5.log`；可移植 runner 完整执行记录为 `core/logs/runner-current.log`，对应 build/run exit 文件同目录。旧首轮 raw 保留但不作为当前解释。
- `core/evidence/source-copy-hashes.txt`：冻结仓 tracked core/presentation/adapters-stub 与 build 镜像 `FILES=1066 MATCH=1066 MISMATCH=0 MISSING=0`，排除 `bin,obj`。




## 文件定位与可复现入口

- 审计根：`D:\workespace\ws-game-artifacts\audit-76d16a5-20260910`；冻结源码：`D:\workespace\ws-game-artifacts\audit-76d16a5-frozen`。
- [本报告](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\core-findings.md)
- [范围复核](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\scope-review.md)
- [可移植 runner](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\run-probes.ps1)
- [当前核心探针日志](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\CoreBoundaryProbe.current-v5.log)
- [当前验证日志](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\QuestWorldFlagValueBoundaryProbe.current-v3.log)
- [Skill 热重载日志](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\logs\SkillHotReloadBoundaryProbe.current-v2.log)
- [源码镜像哈希](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\evidence\source-copy-hashes.txt)
- [探针与日志哈希](D:\workespace\ws-game-artifacts\audit-76d16a5-20260910\core\evidence\probe-hashes.txt)

`core/repro/obj` 与 `core/repro/bin` 若由历史构建留下，仅视为本地 scratch；最终证据包不纳入这些目录。runner 的框架中间产物与独立 DLL 均位于 `build/core/`。