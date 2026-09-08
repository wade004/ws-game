# v1.8.0 项目发现（e070e3f）

本报告只记录本次冻结仓复核得到的现状和需要主审跟进的差异。`D:\workespace\ws-game` 仅作为只读发布快照/基线读取，未写入；没有修改产品源码、既有测试或提交。可选回调为空、编辑器/天赋等明示未实现、游戏内容接线责任和本轮未执行项均不单独算框架缺陷。

## 需要跟进的文档/工程维护与证据项

### 1. 根 README 对 TargetPoint 的责任边界仍会误导（文档更新项）

根 `README.md:9` 将“地面点选”列在框架“未实现”示例中。当前契约 `core/rules/common/contracts/SkillCastRequest.cs:21-33` 已承载可空 `TargetPoint`，`core/rules/skill/core/SkillTickHandler.cs:13-16` 的实现边界是 cast 意图的 point 不由技能 tick 消费，应该由上层 AI/玩家辅助施法层解析为具体目标。落地计划能力索引已经写出这一区分，但根 README 仍把它读成框架能力缺失。建议改为“框架提供 TargetPoint 字段；地面点选到具体目标的消费由上层负责”，并把编辑器、天赋运行时等真实未实现项独立列出。

### 1a. `target_shape_ref` 的直接 Shape 分支与当前 API 登记不一致（文档/API 更新项）

`architecture/06_规则层_属性技能战斗AI.md:126,223` 的现行正文写成 `target_shape_ref` 可直接指向 05 的 `Shape` 并与目标选择链组合；当前 `core/rules/assembly/RulesSchemaCatalog.cs:161-172` 只将该字段登记为 `target.chain_def`，`core/rules/skill/core/CastPipeline.cs:254-255` 也按链 id 调 `TargetHost.Resolve`。建议统一契约为“字段只引用 chain，chain 内使用 Shape”，或补齐并明确实现 direct Shape 分支；当前不能把 direct Shape 当已支持，也不应扩大解释为 `ISpatialQuery`/`FindUnits` 整体不可用。

### 2. ADR-0017 修订记录可做历史文字维护（低优先级）

`architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md:79` 的“决定二”保留了旧算法句；同文件 `:83-85` 已明确这是被 PRES-170-01 收窄的废止方案，任何 anim_set 都不能写共享资产。`architecture/02_引擎适配层.md:142` 正文和 `:22` 勘误已按当前算法表述，Unity 实现/测试也使用任意非空配置隔离。这里仅建议下次触达 ADR 时把旧句显式标为“原决定（已废止）”，不构成现行代码冲突或运行时缺陷。

### 3. `ProgressionRestored` 在 Load 抑制窗口中的重算闭环已由 core 探针复现

`core/foundation/save_system/core/SaveSystem.cs:357-365` 在加载和回滚期间使用 `IEventBus.SuppressDispatch`；`EventBus.Enqueue/PublishImmediate` 在抑制深度非零时丢弃事件。`ProgressionHost.RestoreState` 会在 `core/numbers/progression/core/ProgressionHost.cs:311-327` 发布 `ProgressionRestoredEvent`，而 `core/rules/assembly/RulesAssembly.cs:261-269` 通过该事件重算 rating stats，并通过 `StatChangedEvent` 驱动 power max 重算。当前 core Load 探针已确认真实读档后 rating 为 5（预期 10）、max/health 为 100（预期 200）；详见同审计 `../core/core-findings.md` 与 `../core/logs/followup-core-probe.log`。这应由主审按 core 报告定性，本报告不重复扩展测试。

### 4. 归档 current17-only API 兼容项目不可直接迁移（工程证据问题）

`architecture/落地计划/audit-8160178-20260908/docs-project/api-compat/current17-only/ApiCompatCurrent17Only.csproj` 的四个 `HintPath` 仍指向不存在的旧 worktree `D:\workespace\ws-game-review-8160178\...\current17zip\*.dll`。本次以新的中间目录运行 restore 成功、build 失败（MSB3245/CS0246），完整输出见 `archive-current17-only-absolute-path-restore-build.log`；这证明归档项目本身不可移植，并不证明当前 API 不兼容。作为交叉验证，将同归档的 `ApiCompatLibrary.csproj` 的 `FrameworkRoot` 重新指向本轮冻结仓 `check.ps1` 重建的 `dist/1.8.0` DLL（并在命令行屏蔽预期的 CS0618 过时警告）后构建成功，见 `api-compat-oldapi-portable-build.log`；该结果只证明本轮重建 DLL 的兼容性，不冒充原始发布 ZIP。当前 `GameObjectHost.cs:146-152` 仍提供 `PendingChestLootSnapshot`/`RestorePendingChestLoot` 到新名称的兼容别名，因此旧 API 兼容性当前成立；建议把归档 HintPath 改成相对/参数化路径，但不应把该工程证据问题升级为 P2 框架 bug。

### 5. 同 commit 的发布快照与本地重建产物并非字节一致（发布可复现性）

只读原仓 `dist/1.8.0/MANIFEST.txt` 为 `git_commit: e070e3f`、adapter 234 files，六个 Core DLL hash 为正式 lock 中的值；冻结仓 `check.ps1 -SkipUnity` 产生的 `dist/1.8.0/MANIFEST.txt` 为 `git_commit: e070e3f-dirty`、adapter 228 files，少了六个 Core DLL `.meta` sidecar，且六个 Core DLL hash 分别为：Foundation `a1d355...`、Numbers `666b1b...`、Rules `3f25c9...`、Carriers `f5d842...`、Gameplay `34b7b6...`、Presentation `c544ba...`。原发布快照的 lock/hash 记录见 `D:\workespace\ws-game\dist\ws-game-1.8.0.lock`，重建 transcript 见 `check-skipunity.log`。

差异来自构建路径进入字节、`-SkipUnity` 不生成 Unity 侧六个 `.meta`、以及本轮审计 untracked 状态导致 `e070e3f-dirty`；不能据此把本地检查目录当作正式发布包，也不能在本报告中定为发布缺陷。只读原始 1.8.0 ZIP/lock 的同源核对见 `validation.md`；本轮将此作为验证边界记录，不新增完整 Release/Zip 流程。

## 已核实、无需误报的现状

- `AuraHandleLedger` 已位于 `core/rules/common/contracts/AuraHandleLedger.cs`，由 RulesAssembly 持有并经 CarriersAssembly 注入 EquipmentHost；`Register`/`Release`/`Forget` 和 `InstanceReplaced` 句柄迁移逻辑都在当前源码中。它解决跨装备、套装、种族来源的共享 aura 引用计数，但不替代各来源的私有簿记。
- 等级权威仍是 `ProgressionHost` 的状态/`LevelSync` 写回路径；`WorldUnitAccess.SetLevel` 对未知 unit 是 no-op。等级/评级结论应以当前 Load 探针和实体重建证据为准。
- `GobjOptions.SimTime`、Quest owner/day/vendor 回调、ReplayPlayer、FeedbackRuleValidator 等属于已有机制但需消费方注入/登记；null 或未登记本身符合可选依赖设计。
- 编辑器、天赋分配/激活/持久化、位移轨迹碰撞、VFX 锚点持续跟随属能力索引明确的未实现项；ATB/day_cycle 属当前明确非目标；召唤/掉落 handler 对离散步跳过是局部处理器缺口，不能外推为整个离散模型非目标；新局完整 reset 是游戏/模板接入责任，不应和本轮验证缺失混为一类。
- 孤儿记录检测在 04 中仍只是建议，当前 validator/规则注册没有实现；它是独立的内容质量能力，不应与 `ISkillHost.FindUnits` 或空间查询实现合并判断。
