# ws-game 1.6.0 项目问题与文档修订清单

基线：85f1f4f / v1.6.0。1.6 已修复的 PR150-03、CR150-04 不作为现存产品缺陷重复上报。

## 本机工具链验证阻塞（不评级）

- **预期**：check.ps1 -SkipUnity 的 pytest 应输出可读结果，路径边界测试应能执行 get_framework.ps1。
- **实际**：原始门禁 13 PASS、1 FAIL、6 SKIP，唯一 FAIL 是 python -m pytest toolchain/tests -q。默认 Python 按 GBK 解码 PowerShell 子进程输出，test_get_framework_path_boundary.py:111-117 的 subprocess.run(..., text=True) 触发 UnicodeDecodeError，随后 result.stdout + result.stderr 因 stdout 为 None 又触发 TypeError，掩盖子进程 stderr。
- **UTF-8 对照**：PYTHONUTF8=1 后两个真实失败均暴露 Windows PowerShell 5.1 找不到 Get-FileHash；toolchain/get_framework.ps1:437 直接调用它。测试选择器 :67-72 只选 powershell/powershell.exe，不会选本机可用的 pwsh。
- **pwsh 对照**：pwsh 7.6.5 执行同一脚本、输入原始 1.6 zip/lock，六 DLL hash 全通过并落地，见 get-framework-pwsh-smoke.raw.log。
- **建议**：子进程明确 encoding=utf-8、errors=replace 或 bytes 后显式解码；运行器明确支持 pwsh，或脚本提供不依赖 Get-FileHash 的 SHA256 fallback。改动后分别验证默认 Python、UTF-8 Python、Windows PowerShell 5.1、pwsh。
- **边界**：本轮未改脚本/测试；只证明失败原因和 pwsh 路径成功。

## P2：1.6 公共 API 改名破坏旧 consumer

- **预期**：minor 升级承诺保留已有公开签名，或给出 breaking-change 迁移。
- **实际**：1.5 原始 zip DLL 公开 PendingChestLootSnapshot/RestorePendingChestLoot；1.6 core/carriers/gobj/core/GameObjectHost.cs:104,118 只有 PendingLootSnapshot/RestorePendingLoot，无 alias。相同 OutputType=Library consumer 对 1.5 编译成功，对 1.6 旧调用 CS1061。
- **建议**：加入带 Obsolete 的旧名转发方法并保留一个 minor 周期，或将改名列为 breaking change 并调整 CHANGELOG/升级指南/版本策略。
- **边界**：这是 CLR API surface 证据；存档 JSON/字段迁移仍需独立 fixture。

## P2：owner/day/vendor 文档给出不存在的 GameplayAssembly 参数

- **实际**：GameplayAssembly 构造签名仅 :223-260，没有 ownerResolver、dayProvider、vendorOpenRequested。内部 :516-520 的 QuestHost 与 :692-695 的 DialogHost 把它们传 null；下层 QuestHost.cs:64-66 才有 owner/day 参数。
- **建议**：改为“下层窄回调存在但装配层未暴露；需新增 GameplayAssemblyOptions/adapter 或构造参数，再由游戏 bootstrap 提供”，并写明 day 恒 0、owner/vendor 不执行。不能指导调用方给现有构造函数传这些参数。
- **边界**：只做静态核对，未实现扩展点。

## P2：能力索引混合当前状态和历史问题

落地方案与分阶段计划.md:1193-1230 以“契约已就位、默认组装不接入或未实现”统称，应拆四类。:1214 的 PR150-03 是 1.6 已收口的历史修复，:1219 的 CR150-04 也应明确历史已修；当前 SimTime 冻结是独立的已实现未默认接线问题。根 README.md:9 同样应按四类引用，避免把框架未实现和已实现未接线混称。

## P2：04 章默认校验覆盖边界未登记完整

- GameplaySchemaCatalog.cs:171-188 仅在 creatureTemplateQuery != null 时登记 SpawnSummonOnlyCreatureRule；默认 null 时 spawn.table.content_ref 没有这条检查。
- RulesSchemaCatalog.cs:154-174 在此 catalog 显式登记 3 条引用规则；不能据此推断整个框架所有 catalog 只有 3 条。ArchSchemas.cs:35-59 的 power_types、skill_book_ref、passive_auras 是普通 Id/IdList，不能声称会被该处跨表整合检查。
- DisplayMapCoverageRule 存在但 PresentationSchemaCatalog.cs:115 明确不在默认注册，必须显式提供 sources。
- **建议**：04 章增加 validator/catalog 覆盖矩阵，逐项说明框架、装配层或游戏数据 pipeline 责任。

## 发布/包流程证据边界（不评级）

get_framework.ps1:437 在 hash 校验后才落地；pwsh smoke 使用原始 1.6 zip/lock 成功，说明离线 zip 通道可执行。本轮没有执行 Release、网络发布、registry 启动或真实 dist 覆盖；workflow 只静态阅读，不能宣称远端 Release 已核实。原始 1.5 目录 MANIFEST 与 lock 曾指向不同 commit，故不作为 API 基线；API 对照使用 zip 内与 lock 同源 DLL。

## 四类能力状态

**未实现**：天赋点激活、撤销与持久化；`ISkillHost.FindUnits`；位移轨迹碰撞；VFX anchor 持续跟随；新局全状态重置（模板仅重置有限玩家项，完整流程须由游戏负责）；编辑器实现（editor/README.md:5 明确实现尚未开始，当前仅产品 md/html）。

**已实现但未默认接线**：Gobj `SimTime`、`Quest.Update` 生产驱动、Replay 生产入口、下层 owner/day/vendor 回调（下层存在，`GameplayAssembly` 未暴露参数）、`DisplayMapCoverageRule`/`FeedbackRuleValidator`、武器 style 消费。`TargetPoint` 是已有参数但当前 `SkillHost` 不消费，须由游戏上层处理；这是边界责任，不等同完整机制未实现。

**已实现且默认接线**：`target.chain`、连续/基础离散调度、model/sprite 视图装备重放、命中帧事件（配置可关闭）。已有 43 个 Unity 相关回归，但 slot_mesh 新缺陷仍在；这些证据不等于完整游戏通过。

**明确非目标**：`day_cycle`（RulesExprHostFactory.cs:461-462 恒 0）、ATB（TurnScheduler.cs:77-81 抛 `NotSupportedException`）。

## 文档 owner 更新清单

- CHANGELOG.md:100-102 声称 1.5 非空 pending 存档旧字段会被安全忽略、不报错且不影响其它段；实际依据旧 serializer 重建的等价存档经 SaveSystem.Load 返回 PersistableThrew，且 inventory 前段已经变更。需修订旧档兼容说明并落实安全跳过或明确迁移；这是 AUD-01，独立于 AUD-04 的公开 API 编译破坏。

- README 与能力索引：PR150-03、CR150-04 改成历史已修；四类状态分栏，避免“契约已就位”总括。
- owner/day/vendor：GameplayAssembly:223-260 没有三个参数；先补装配层扩展点再写游戏接线。vendorOpenRequested 仍为 null，dayProvider 的恒 0 也不是 vendor 时钟。
- CHANGELOG：旧 PendingChestLoot 两个公开方法在 1.6 没 alias，旧 consumer 编译即 CS1061；安全跳过旧存档字段迁移断言不能覆盖 API 改名。
- 10 章：10_存档与持久化.md:119 步骤 7a 含 gobj_pending_loot；SaveSections.KnownOrder:112-132 没有该段，实际自定义排序在 rng 之后，需修正文档顺序与段登记。
- 09/14 章：slot_mesh 当前缺陷见 ../presentation/presentation-findings.md 的 PRES-85-01：UnityRenderer3D.cs:941-966 按 Mesh 读取，而 sample model_ref 是 prefab；不能把 model 整体写成已收口。VFX anchor 的机制/跟随边界见 VfxPlayer.cs:315-373。
- 07 章：补离散掉落清理/召唤处理的边界；不要把已修 PR150-03 或 CR150-04 再列为现存缺陷。
- 09 章：武器 style resolver 仅有机制，未找到生产消费者，和 model 外观默认路径分栏。

门禁说明：PowerShell 5.1 缺少 Get-FileHash 的实际环境原因已复现，但未独立确认 PSModulePath 等根因；本项只列本机工具链验证阻塞，不评级产品逻辑或 CI 缺陷。

