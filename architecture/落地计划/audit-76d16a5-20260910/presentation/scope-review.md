# 1.14.0 表现责任 review

本页按“框架机制、可选启用条件、游戏内容策略”分开记录。依据是冻结 HEAD `76d16a54e54f0f11204d97d7460563c8a0dc8cd8` 的当前源码与本轮独立 Unity 副本；本轮原始 XML/log 未改。

| 主题 | 框架应负责的机制 | 启用条件/默认装配 | 游戏侧责任与本轮边界 |
|---|---|---|---|
| Existing View SaveLoaded | `ViewBinder.OnSaveLoaded` 对同图仍存活 View 做身份保留和装备外观清空+快照重放；`IEquipmentVisualResettable` 规定空快照清零、重复调用幂等 | `ViewBinder` 与 `UnityViewFactory` 收到同一 `EquipmentVisualSource`，具体 View 实现接口；模板 `GameBootstrap.cs:314,337,433-438` 已接线 | 没有装备外观概念的 View 可不实现；具体模型、槽位和外观资源是游戏数据。真实 A→空 B Save/Load 已在 `EquipmentVisualSaveLoadResetTests` 通过 |
| VFX anchor/socket | `architecture/09_表现层.md:31,283-287` 已将 `anchor` 与 socket 降级 world 的持续跟随列为通用表现契约；真实 socket 用 `AttachToSocket` 父子挂接，anchor/降级 socket 用 `IParticleRepositioner` 每帧重定位；目标消失时停止回收 | `presentation/vfx_sfx/core/VfxPlayer.cs:162,388-393,450-465` 按需探测 `IParticleRepositioner`；Unity `adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/EngineAdapter/UnityRenderer2D.cs:50,464-483` 默认提供；未提供的其他引擎允许记录生成时位置的退化 | 游戏决定使用哪种 VFX、资源、类别和参数；不因游戏未选某 VFX 定义而推翻框架跟随职责。真实 Transform 移动与 Stop 后防御已通过 |
| DataHotReload Deleted | `DataHotReload.cs:95-113,126-159,225-256` 在 Editor/Development 下监听 Deleted，去抖后由主线程调用同一 `DataRegistry.Reload`，按仍存在的数据根回落 | `GameBootstrap` 按 `EnableDataHotReload` 挂载；Release/Shipping 编译为空壳（文件头 `:10-12`） | 游戏提供 framework/game 数据根和 override 内容；临时双根 fixture 是通用输入，不引入 ws-game-wow 依赖。删除回落真实 Editor probe 通过 |
| New Game reset | Shell 只提供 `NewGameStarter` 委托契约 `presentation/shell/contracts/ShellHostTypes.cs:25-37` | 模板将 `SampleNewGameStarter.Start` 接到 `GameBootstrap.cs:433` | 起始地图、玩家模板、初始装备、难度/职业分支属于游戏专属策略。`games/_template/Runtime/SampleNewGameStarter.cs:7-22,34-40` 明确可替换；本轮不把模板最小示例当作所有游戏的完整重置保证 |
| TargetPoint / socket 内容 | 框架提供 anchor/socket 查询、模型句柄与挂接机制 | 需要相应 View/renderer 能力和有效 id | 地图点、socket 命名、模型资产属于游戏数据；本轮验证机制和 adapter 生命周期，不要求游戏内容覆盖 |
| Swing / Impact / VFX 选择 | 框架消费事件、动画/特效句柄、播放完成与回收原语 | 需要游戏把技能/武器定义映射到这些表现资源 | 哪个技能触发 Swing/Impact、命中特效和时序是游戏策略；本轮不把未选择内容算作框架缺陷 |
| gather 时钟 | Core/adapter 提供注入的 `IClock`/SimTime 与固定步长语义；消费方应使用注入时钟推进冷却和事件时间 | 由运行时装配把具体时钟实现注入规则/游戏 host | 采集冷却数值与内容仍由游戏策略决定；本轮只审查时钟注入边界，不把策略差异推给表现层 |

## NAV/SPATIAL 收窄

`architecture/02_引擎适配层.md:165-167` 是需要保持的通用几何语义：不可行走端点返回空、成功路径首尾精确接合、路径段与 raycast 使用同一可通行规则且不切角；`02:189` 要求查询只作用于调用方已登记对象，登记生命周期由调用方负责。性能边界在 `02:168` 和 `02:190` 已明确收窄：是否跨帧分摊、是否每种查询都建全索引由实现方决定。因而旧审计中关于窄通道/跨 bucket 的复测只能针对这些几何和登记契约；跨帧预算或全索引建议不能作为本轮违约。

## 覆盖与限制

本轮筛选 Play 3/3、筛选 Deleted Edit 1/1，最终完整 Edit 70/70、Play 272/272。历史 1.13 的 View 正确性失败和 Deleted 诊断 PASS 均在 `presentation-findings.md` 的矩阵中保留原意：前者是旧实现的真实失败，后者是用错误签名断言确认缺陷；1.14 用正确行为 oracle 重新验证两项修复。未运行独立版、IL2CPP、consumer smoke 或正式 package 发布门禁，也未将模板资源策略或未接入的游戏表现内容判为框架缺陷。
