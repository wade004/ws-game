# ws-game 1.7.0 工程项目审计发现

基线：`D:\workespace\ws-game-review-8160178`，`HEAD 8160178b76fb51ae704a8f14b428decf228cc33e`，`VERSION 1.7.0`。本文件只记录当前 1.7 工程交付、版本兼容、工具链与文档边界；`audit-85f1f4f`、`followup-2026-09-08e` 是历史材料，不直接当作当前结论。原 `D:\workespace\ws-game` 仅作只读发布产物来源。

## P2：SaveSystem 回滚合同文档漂移，且三处口径不一致

`SaveSystem.Load` 在 `core/foundation/save_system/core/SaveSystem.cs:334-385` 先为已注册段取快照，失败后在 `:690-727` 按逆序调用快照回滚；实现注释明确这是“尽力而为”，快照缺失或回滚再次抛错时只记诊断。Core 代理已对当前 1.7 实现复现出真实回滚残留风险，详见主审汇总的 core 证据；这是存档失败后的跨段一致性问题；实现风险级别由 core 代理证据和主审单独判定，应与旧字段迁移分开处理。

当前接口注释 `core/foundation/save_system/contracts/IPersistable.cs:30-35` 仍写“之前成功 Load 的其它段不会被回滚”，与实现冲突；`core/foundation/save_system/README.md:131-132,206-219` 同时存在“会被回滚/取代不回滚”与后文 best-effort 的强弱不一表述；`CHANGELOG.md:84-85` 又把 best-effort 写成“部分加载中间态消失”，比实现保证更强。`architecture/10_存档与持久化.md:112` 的“尽力恢复”较准确。应统一为：失败结果仍为 `PersistableThrew`；只对有成功快照的已成功段尝试逆序恢复；快照/回滚失败可能残留，必须有诊断和独立测试。不能把 `PersistableThrew`（存档段失败）与公开 API 的 CS1061/CS0618 混称。

## 版本与公开 API 兼容

只读使用原始 `D:\workespace\ws-game\dist\ws-game-1.5.0.zip` 与同名 lock；zip SHA256 为 `4C596D4A93C802C2FD065EB3ADC0F4ABC113B3B2687CC4F901FFA5B3C7E8C76B`，六个 DLL 与 1.5 lock 全部 MATCH，lock 的 `git_commit=3224ca1`。1.7 使用原始 `D:\workespace\ws-game\dist\ws-game-1.7.0.zip` 与 lock；zip SHA256 为 `1B72927AFFBD5E5AAF38FA9AA40A619F8F460F2F021EF0DFD799A597FB3B9A42`，六个 DLL 与 1.7 lock 全部 MATCH，lock 的 `git_commit=8160178`。详细逐 DLL hash 在 `api-compat-hash-verify.log` 与 `api-compat-current17zip-hash.log`。

同一旧调用 probe（`PendingChestLootSnapshot`/`RestorePendingChestLoot`）在隔离工程 `api-compat/old15-only` 对原始 1.5 DLL 构建退出 0、0 警告；对原始 1.7 DLL 构建退出 0、0 错误、2 条预期 CS0618 过时警告。反射结果为 1.5 的旧两个方法，1.7 同时含新名和旧名两个 `[Obsolete]` 转发方法。编译证据以 `consumer-old15-only-rebuild.log`、`consumer-current17-only-rebuild.log` 两份最终日志为准；反射日志只作成员枚举补充，此前同目录参数复用产生的旧日志不采用。该结果证明 API 别名窗口，不证明旧存档字段自动迁移。

## 能力接线与文档边界

- owner/day/vendor 的三个可选参数已在 `GameplayAssembly.cs:272-274`，内部转发在 `:539-540,718`；三处组合根分别在 `GameFoundationBootstrap.cs:330-341`、`FrameworkResidentHost.cs:337-348`、`GameBootstrap.cs:217-236` 透传。默认都为 null，所以分类是“已实现未默认接线”，不应继续使用旧的“构造函数不存在参数”结论。
- `QuestHost.Update` 在 `core/gameplay/quest/core/QuestHost.cs:386`，但 `core`、三处组合根和游戏模板生产路径未找到 `Quest.Update` 调用。日任务自动推进必须由游戏决定固定步、地图切换或其它触发时机。
- `GobjOptions.SimTime` 在 `core/carriers/gobj/contracts/GobjOptions.cs:85-95` 默认为 `() => 0`；三处组合根没有把统一模拟时钟注入 Gobj 选项。`respawn_after_use>0` 的采集节点因此属于已实现但未默认接线，不能写成采集机制完全不存在。
- 能力索引 `落地方案与分阶段计划.md:1215-1263` 已按未实现、已实现未默认接线、明确非目标与已修历史项拆栏；应继续把旧 PR/CR 修复留在历史小节，避免恢复“19 项”或把接线缺口统称框架未实现。索引中 SimTime 行仍保留“两个生产根”的旧措辞，应按当前三处组合根复核；owner/day/vendor 必须保持三根透传与默认 null 的双重描述。
- `time.day_cycle` 恒 0（`RulesExprHostFactory.cs:461-462`）和 ATB 在 `TurnScheduler.cs:79-81` 抛 `NotSupportedException` 是明确非目标；不要把离散 turn/round/is_my_turn 一并判为不可用。
- `SaveSections.KnownOrder` 当前在 `SaveSections.cs:122-145` 登记了 dropped/vendor/difficulty/spawn、`sim.turn_state` 和 RNG；`GobjPendingLootPersistable.cs:53` 的 `world.gobj_pending_loot` 已由装配根注册，但仍未列入该顺序。10 章 `:124-125` 将它写在 7a、sim 前，和当前自定义段排序事实漂移；不能把旧审计结论整体标为已修复，应修正文档顺序，或明确将该自定义 key 纳入 KnownOrder。

## 工具链与交付文档

`toolchain/README.md:359` 写复制/引用 `get_framework.ps1`，但未提醒同步携带 helper，可能被误解为单脚本可独立使用；1.7 `toolchain/get_framework.ps1:102` 已强依赖同目录 `_hash.ps1`。完整发布包含 helper，因此不是当前包缺件；接入说明应改成复制整套 toolchain，或至少同时携带 `_hash.ps1`。这是文档接入风险，不应评级为发布包损坏。

本机环境已确认：Python 3.13.9（`C:\ProgramData\anaconda3\python.exe`，`preferred_encoding=cp936`）、Windows PowerShell 5.1.26100.9168、pwsh 7.6.5；`toolchain/_hash.ps1` 首三字节 `239,187,191`，UTF-8 BOM 存在。`check.ps1 -SkipUnity` 的 pytest 92 passed、2 skipped，哈希 fallback 与 BOM 门禁均通过；这只是本机工具验证，不代表其它代码页、CI runner 或 Unity 环境等价。

## 交付结论边界

当前静态/非 Unity 交付材料足以证明 1.7 版本号、lock、原始包 DLL hash、数据校验、工具链测试、六个 Native 测试程序集与包清单的一致性。本项目验证子任务使用 `-SkipUnity`；整体专项验证见 `../presentation/presentation-findings.md` 与 `../core/core-findings.md`。本子任务未证明 Unity 编译/EditMode/PlayMode、独立版冒烟、IL2CPP、消费方演练、网络 registry/Release 或真实游戏用户验收。全量 .NET `dotnet test` 已包含 Perf 类别，但未执行实际游戏性能/压测。已复现的存档失败恢复问题与上面的“门禁通过”并存，不能以 2460 个 Native tests passed 结论覆盖它。

