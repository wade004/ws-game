#nullable enable
// 诊断契约统一转发机制的生产装配清单（feat/diagnostics-contracts-unification，架构结论见
// architecture/adr/0042-诊断契约统一转发到宿主控制台.md）。
//
// 判断记录（为什么单独拆一个 Composition 静态类，而不是把 Register 调用直接摊在三处装配根里）：
// 三个生产装配入口（GameFoundationBootstrap/FrameworkResidentHost/games/_template.GameBootstrap）
// 装配 GameplayAssembly/PresentationAssembly/EventBus/SaveSystem/SceneRouter 的过程本身各自略有
// 差异（数据集路径、Inspector 配置等），但"装配完成后，把这些实例的诊断出口一一登记进
// DiagnosticsHub"这件事完全一样——同 PresentationAssemblyDiagnosticsForwarder 那五个来源当年被
// 直接摊平在三处（本单不改动那部分，见该类型既有判断记录）不同，本单选择不重复这个模式：本文件把
// "登记哪些来源、用什么来源名"集中写一次，三处装配根各自只需要在 PresentationAssemblyDiagnosticsForwarder
// 构造完成之后紧接着调用本文件的 <see cref="RegisterCoreSources"/> 一行，新增/调整来源只改本文件，
// 不需要同步改三处。
//
// 判断记录（逐个 `is InMemoryXxxDiagnostics` 模式匹配，而不是直接读接口属性）：本单给每个 Host
// 新增的 <c>Diagnostics</c> 只读属性一律按契约接口类型暴露（如 <c>IHookDiagnostics Diagnostics</c>），
// 不是具体的 <c>InMemoryHookDiagnostics</c>——因为构造函数的 diagnostics 参数允许调用方传入自定义
// 实现（非 InMemory 型），属性必须按接口类型暴露才不会在那种情况下类型不匹配；但 Warnings/Errors
// 这两个"消息列表"读出口只存在于默认的 InMemory 实现上，接口本身只承诺 Warn/Error 两个写方法（同
// event_bus.IEventDiagnostics 类型注释"接口不认识具体呈现方式"的既有设计）。这与既有
// PresentationAssemblyDiagnosticsForwarder 构造函数里 `vfxDiagnostics as PresentationDiagnosticsRecorder`
// 的运行期类型转换是同一个惯例：生产装配路径上这些参数全部走默认值（没有任何一处生产代码显式传自定义
// 实现，见普查笔记），转换恒成功；调用方一旦真的换成自定义实现，本方法按"该来源未提供"静默跳过（不
// 阻断其它来源），不是当前范围要处理的场景。
//
// 判断记录（本文件不进入 dotnet test 编译范围）：本文件直接引用 GameplayAssembly/PresentationAssembly/
// 具体的 EventBus/SaveSystem/SceneRouter 类型以及二十个模块各自的 InMemory*Diagnostics 类型——这些
// 类型分布在 Core.Gameplay/Core.Rules/Core.Carriers/Core.Numbers/Presentation.Common 等多个程序集，
// 而 dotnet test 侧的 Adapters.Unity.DiagnosticsForwarding.csproj 刻意只引用 Presentation.Common
// （见该 csproj 判断记录，为了不引入 UnityEngine 依赖同时保持编译范围最小）；把本文件也纳入会需要
// 给该 csproj 追加一长串 ProjectReference，且本文件的职责纯粹是"装配期把哪些实例的哪个属性接给
// Hub"这一层胶水判断（不含任何去重/开关/级别映射逻辑——那些已经在 DiagnosticsHub 本身内被 dotnet
// test 覆盖）。这与既有 PresentationAssemblyDiagnosticsForwarder 在三处装配根里的构造调用本身也
// 从未被 dotnet test 覆盖是同一类型的裁剪（只能在真实 Unity 批处理测试里确认，见任务书硬性规则）。
// 本文件只在 Unity asmdef 的编译范围内（Adapter.Unity，precompiledReferences 已含全部所需程序集）。
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Gameplay.Assembly;
using Presentation.Assembly;

namespace Adapter.Unity.Diagnostics
{
    /// <summary>三个生产装配入口共享的诊断来源登记清单——普查全清单见
    /// architecture/adr/0042-诊断契约统一转发到宿主控制台.md 正文引用的普查表。</summary>
    public static class DiagnosticsHubComposition
    {
        /// <summary>登记本轮普查覆盖到的全部核心层诊断来源（presentation/vfx_sfx.IPresentationDiagnostics
        /// 那五条既有链路走 <see cref="Adapter.Unity.Presentation.PresentationAssemblyDiagnosticsForwarder"/>，
        /// 不经本方法，见该类型既有注释；本方法只登记本单新增覆盖的其余来源）。</summary>
        public static void RegisterCoreSources(
            DiagnosticsHub hub,
            GameplayAssembly gameplay,
            PresentationAssembly presentation,
            EventBus bus,
            SaveSystem saveSystem,
            SceneRouter sceneRouter)
        {
            // --- L0 基础模块 ---
            if (bus.Diagnostics is InMemoryEventDiagnostics busDiag)
            {
                hub.Register("Core.Foundation.EventBus", busDiag.Warnings,
                    new ProjectedReadOnlyList<EventDiagnosticsErrorRecord>(busDiag.Errors, e => e.Message));
            }

            if (gameplay.HooksDiagnostics is Core.Foundation.HookRegistry.InMemoryHookDiagnostics hooksDiag)
            {
                hub.Register("Core.Foundation.HookRegistry", hooksDiag.Warnings,
                    new ProjectedReadOnlyList<Core.Foundation.HookRegistry.HookDiagnosticsErrorRecord>(hooksDiag.Errors, e => e.Message));
            }

            if (gameplay.AppStateDiagnostics is Core.Foundation.AppLifecycle.InMemoryAppLifecycleDiagnostics appStateDiag)
            {
                hub.Register("Core.Foundation.AppLifecycle", appStateDiag.Warnings,
                    new ProjectedReadOnlyList<Core.Foundation.AppLifecycle.AppLifecycleDiagnosticsErrorRecord>(appStateDiag.Errors, e => e.Message));
            }

            if (saveSystem.Diagnostics is InMemorySaveDiagnostics saveDiag)
            {
                hub.Register("Core.Foundation.SaveSystem", saveDiag.Warnings,
                    new ProjectedReadOnlyList<SaveDiagnosticsErrorRecord>(saveDiag.Errors, e => e.Message));
            }

            if (sceneRouter.Diagnostics is InMemorySceneDiagnostics sceneDiag)
            {
                hub.Register("Core.Foundation.SceneRouter", sceneDiag.Warnings,
                    new ProjectedReadOnlyList<SceneDiagnosticsErrorRecord>(sceneDiag.Errors, e => e.Message));
            }

            if (presentation.InputMapDiagnostics is Core.Foundation.InputMap.InMemoryInputMapDiagnostics inputMapDiag)
            {
                hub.Register("Core.Foundation.InputMap", inputMapDiag.Warnings);
            }

            if (presentation.L10nDiagnostics is Core.Foundation.Localization.InMemoryL10nDiagnostics l10nDiag)
            {
                hub.Register("Core.Foundation.Localization", l10nDiag.Warnings);
            }

            // --- L3 载体层（core/carriers，经 gameplay.Carriers 可达） ---
            if (gameplay.Carriers.GameObjectInteractions.Diagnostics is Core.Carriers.Gobj.InMemoryGobjDiagnostics gobjDiag)
            {
                hub.Register("Core.Carriers.Gobj", gobjDiag.Warnings, gobjDiag.Errors);
            }

            // ADR-0051：生物原生交互宿主，与上一条 Core.Carriers.Gobj 并列（同一层面两个 interact
            // 意图消费者，见 CarriersAssembly.CreatureInteractions 判断记录）。
            if (gameplay.Carriers.CreatureInteractions.Diagnostics is Core.Carriers.Creature.InMemoryCreatureDiagnostics creatureInteractDiag)
            {
                hub.Register("Core.Carriers.Creature", creatureInteractDiag.Warnings, creatureInteractDiag.Errors);
            }

            if (gameplay.Carriers.Equipment.Diagnostics is Core.Carriers.Item.InMemoryItemDiagnostics itemDiag)
            {
                hub.Register("Core.Carriers.Item", itemDiag.Warnings);
            }

            if (gameplay.Carriers.Projectiles.Diagnostics is Core.Carriers.Projectile.InMemoryProjectileDiagnostics projectileDiag)
            {
                hub.Register("Core.Carriers.Projectile", projectileDiag.Warnings, projectileDiag.Errors);
            }

            // --- L2 规则层（core/rules，经 gameplay.Carriers.Rules 可达） ---
            if (gameplay.Carriers.Rules.PowerDiagnostics is Core.Numbers.PowerSet.InMemoryPowerDiagnostics powerDiag)
            {
                hub.Register("Core.Numbers.PowerSet", powerDiag.Warnings);
            }

            if (gameplay.Carriers.Rules.Progression.Diagnostics is Core.Numbers.Progression.InMemoryProgressionDiagnostics progressionDiag)
            {
                hub.Register("Core.Numbers.Progression", progressionDiag.Warnings);
            }

            if (gameplay.Carriers.Rules.Combat.Diagnostics is Core.Rules.Combat.InMemoryCombatDiagnostics combatDiag)
            {
                hub.Register("Core.Rules.Combat", combatDiag.Warnings);
            }

            if (gameplay.Carriers.Rules.Skill.Diagnostics is Core.Rules.Skill.InMemorySkillDiagnostics skillDiag)
            {
                hub.Register("Core.Rules.Skill", skillDiag.Warnings, skillDiag.Errors);
            }

            // --- L4 玩法宿主（core/gameplay，直接挂在 GameplayAssembly 上） ---
            if (gameplay.AreaTrigger.Diagnostics is Core.Gameplay.AreaTrigger.InMemoryAreaTriggerDiagnostics areaTriggerDiag)
            {
                hub.Register("Core.Gameplay.AreaTrigger", areaTriggerDiag.Warnings, areaTriggerDiag.Errors);
            }

            if (gameplay.Reward.Diagnostics is Core.Gameplay.Common.InMemoryRewardDiagnostics rewardDiag)
            {
                hub.Register("Core.Gameplay.Common.Reward", rewardDiag.Warnings);
            }

            if (gameplay.Death.Diagnostics is Core.Gameplay.Death.InMemoryDeathPolicyDiagnostics deathDiag)
            {
                hub.Register("Core.Gameplay.Death", deathDiag.Warnings, deathDiag.Errors);
            }

            if (gameplay.Dialog.Diagnostics is Core.Gameplay.Dialog.InMemoryDialogDiagnostics dialogDiag)
            {
                hub.Register("Core.Gameplay.Dialog", dialogDiag.Warnings);
            }

            if (gameplay.Spawn.Diagnostics is Core.Gameplay.Spawn.InMemorySpawnDiagnostics spawnDiag)
            {
                hub.Register("Core.Gameplay.Spawn", spawnDiag.Warnings, spawnDiag.Errors);
            }

            if (gameplay.WorldState.Diagnostics is Core.Gameplay.WorldState.InMemoryWorldStateDiagnostics worldStateDiag)
            {
                hub.Register("Core.Gameplay.WorldState", worldStateDiag.Warnings);
            }

            // 消费方反馈第 5/6 条根治（feat/silent-degradation-diagnostics，2026-09-20）：progression_bridge
            // 两个监听器（CreatureDeathXpListener/AreaTriggerDiscoveryXpListener）此前从未持有任何诊断
            // 契约实例，本轮补上——GameplayAssembly 不对外暴露这两个监听器实例本身（构造后即弃元），
            // 改为直接转发两者共用的诊断实例，见 GameplayAssembly.ProgressionBridgeDiagnostics 判断记录。
            if (gameplay.ProgressionBridgeDiagnostics is Core.Gameplay.ProgressionBridge.InMemoryProgressionBridgeDiagnostics progressionBridgeDiag)
            {
                hub.Register("Core.Gameplay.ProgressionBridge", progressionBridgeDiag.Warnings);
            }

            // 消费方反馈同构问题第三处根治（2026-09-20）：core/gameplay/loot 的 CreatureDeathLootListener
            // 此前从未持有任何诊断契约实例，本轮补上——GameplayAssembly 不对外暴露这个监听器实例本身
            // （构造后即弃元），改为直接转发它的诊断实例，见 GameplayAssembly.LootDiagnostics 判断记录。
            if (gameplay.LootDiagnostics is Core.Gameplay.Loot.InMemoryLootDiagnostics lootDiag)
            {
                hub.Register("Core.Gameplay.Loot", lootDiag.Warnings);
            }

            // --- presentation/ui 独立契约（不实现 IPresentationDiagnostics，同 ISkillDiagnostics 惯例） ---
            // 判断记录（CS0234 修正，2026-09-20）：此前写作 `Presentation.Ui.InMemoryUiDiagnostics`
            // 编译报 "The type or namespace name 'Ui' does not exist in the namespace
            // 'Adapter.Unity.Presentation'"——本文件所在命名空间 Adapter.Unity.Diagnostics 与本程序集
            // 内已存在的兄弟命名空间 Adapter.Unity.Presentation（见 Runtime/Presentation/ 下
            // AnimClipResolver 等文件）同名前缀冲突：编译器按"由内向外逐级匹配外层命名空间"解析未限定
            // 的 `Presentation` 标识符，会先命中同程序集内的 Adapter.Unity.Presentation（存在这个命名
            // 空间），再在其下找 `Ui` 子命名空间失败即报错终止，不会继续回退到全局的 Presentation.Ui
            // （核心层 presentation/ui 模块的命名空间）。这不是"UiDiagnostics 出口未接线"（该出口已由
            // presentation/assembly.PresentationAssembly.UiDiagnostics 属性提供，属性类型正是
            // Presentation.Ui.IUiDiagnostics，只是引用点未限定作用域），修法是加 `global::` 前缀强制从
            // 全局命名空间解析，不是补属性/补装配——本程序集内 GameFoundationBootstrap.cs/
            // FrameworkResidentHost.cs 已有同名冲突场景下用 `global::Presentation.Xxx`（VfxSfx/Render/
            // FeedbackBinder/Shell 等多处）的既有先例，此处沿用同一惯例。
            if (presentation.UiDiagnostics is global::Presentation.Ui.InMemoryUiDiagnostics uiDiag)
            {
                hub.Register("Presentation.Ui", uiDiag.Warnings);
            }

            // 判断记录（IExprDiagnostics 结构性排除，不在本方法登记）：见
            // architecture/adr/0042-诊断契约统一转发到宿主控制台.md 正文"结构性排除"一节——该契约
            // 全仓 20+ 个持有点里相当一部分是方法调用期临时构造、调用结束即丢弃的实例（如
            // CastPipeline.EvaluateUseCondition、SkillHost 的技能可用性判定、ProcHost/TargetHost 的
            // 条件求值、AreaTriggerHost/SpawnHost 的动态解析各一处、DataRegistry.RecordExprHost 一处），
            // 轮询式转发要求"消息列表随时间只增不减、可跨帧持续读取同一个引用"，这些临时实例活不过
            // 一次方法调用，物理上无法被 Pump 追踪；即便对少数确实按字段持久持有的来源（AiHost/
            // RotationEvaluator/FeedbackBinder/RulesExprHostFactory/QuestHost/DialogHost/EconomyHost/
            // EncounterHost/LootHost/AchievementHost/SummonTickHandler/MovementTickHandler 十余处）
            // 单独接入，也无法覆盖同一契约的另一半临时用法，覆盖面本身就是残缺的、容易给人"这个契约
            // 已经接好了"的错觉。本单结论：IExprDiagnostics 需要一套不同于轮询的机制（如按调用点
            // 在异常/非预期分支处直接同步转发一次，而不是攒起来轮询），留待后续单独立项，不在本单
            // 内牵强凑数。
        }
    }
}
