#nullable enable
// 第十一方深度审核修复"第 0 步"新增本文件顶部 #nullable enable（同 GameBootstrap.cs/
// GameFoundationBootstrap.cs/FrameworkResidentHost.cs 既有惯例）：本次新增的
// QuestOwnerResolver/QuestDayProvider/VendorOpenRequested 三个字段是可空委托类型
// （`Func<Id, Id?>?` 等），未声明可空上下文时 `Func<...>?` 语法本身仍能编译，但会触发
// CS8632（"可为空引用类型的注释只能在启用了 '#nullable' 注释上下文的代码中使用"）警告；
// 本文件其余全部字段此前已按各自默认值初始化，开启本指令不会新增 CS8618 一类"非可空字段
// 未初始化"警告。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Rules.Skill;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using UnityEngine;

namespace Game.Template
{
    /// <summary>
    /// 口味配置项（architecture/13_新游戏接入指南.md 第 4 节表格逐行对应，见各字段注释"13 §4 第 N 行"）。
    /// <para>
    /// 纯 C# 类（而不是 ScriptableObject）：字段全部是 Unity 可序列化的基础类型
    /// （bool/int/double/string/enum），供 <see cref="GameBootstrap"/> 以
    /// <c>[SerializeField] private GameOptions _options = new GameOptions();</c> 的形式在
    /// Inspector 里直接编辑，不需要单独的资产创建/引用步骤；新游戏若更偏好"多套配置切换存成资产"
    /// 的用法，可自行把本类型包一层 ScriptableObject。
    /// </para>
    /// <para>
    /// 判断记录（哪些字段接了真代码、哪些只是占位）：本类型只是"配置数据的容器"，真正读取这些字段
    /// 构造框架各层 Options 对象（<c>SkillOptions</c>/<c>CombatOptions</c>/<c>MovementOptions</c>/
    /// <c>LootOptions</c>/<c>Presentation.Render.RenderOptions</c>/<c>PresentationAssemblyOptions</c>/
    /// <c>SaveSystemOptions</c> 等）的代码在 <see cref="GameBootstrap"/>（见该类型
    /// "BuildXxxOptions" 系列私有方法）。13 §4 清单里没有对应框架 Options 字段的口味项
    /// （多为数据层/引擎适配层/UI 布局层面的口味，不是 L0～L5 逻辑层的策略配置项），本类型仍然
    /// 声明对应字段并在注释里如实标注"待游戏层使用"，不假装接了根本不存在的代码。
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class GameOptions
    {
        // -----------------------------------------------------------------
        // 起始状态：新游戏进哪张图、玩家用什么模板/职业/阵营（SampleNewGameStarter 消费）。
        // -----------------------------------------------------------------

        [Header("起始状态（SampleNewGameStarter 消费，见该类型）")]
        /// <summary>新游戏进入的地图 id。默认指向本模板自带的最小示例数据
        /// （<c>data/game/world/world.map.json</c>）；复制为新游戏后应改成自己的地图 id
        /// （替换为本游戏 id，见模板 <c>data/README.md</c>）。</summary>
        public string StartMapId = "world.template_field";

        /// <summary>玩家单位 id（内容表之外的运行期实体 id，只需符合 id 格式，不必在任何数据表登记）。</summary>
        public string PlayerUnitId = "unit.template_player";

        public string PlayerFactionId = "fac.template_player";

        public string PlayerClassId = "arch.class.template_hero";

        /// <summary>玩家外形模板 id：供表现层 <c>display.map</c> 按 <c>logical_id</c> 匹配到具体
        /// 外形（本模板尚未配表现，见 13 §6"配表现"；未配到之前玩家不可见，不影响进图本身）。
        /// 默认值不是 <c>data/_sample</c> 的具体 id（本模板不依赖框架自带示例数据），
        /// 替换为本游戏真正的生物模板 id。</summary>
        public string PlayerTemplateId = "creature.template_hero";

        public int PlayerLevel = 1;

        public string DefaultDifficultyId = "diff.template_normal";

        /// <summary>存档系统的游戏 id（<c>SaveSystemOptions</c> 构造参数，区分不同游戏各自的存档
        /// 目录），复制为新游戏后应改成自己的游戏代号。</summary>
        public string GameId = "game.template";

        public ulong Seed = 20260905UL;

        // -----------------------------------------------------------------
        // 13 §4 第 1 行：主动技能槽数 —— PresentationAssemblyOptions.ActionBarSlotCountFallback。
        // -----------------------------------------------------------------
        [Header("13 §4 口味配置项")]
        public int ActiveSkillSlotCount = 4;

        // 13 §4 第 2 行：公共冷却开关 —— SkillOptions.GcdEnabled / GcdDuration。
        public bool GlobalCooldownEnabled = false;
        public double GlobalCooldownDuration = 1.5;

        // 13 §4 第 3 行：目标链（Targeting）—— GameplayAssembly(targetingOptions:)；具体链的组成
        // 是 target.chain_def 数据行，本字段只控制 TargetingOptions 的通用行为（默认半径）。
        public double TargetingDefaultRadius = 8.0;

        // 13 §4 第 4 行：消耗品开关 —— 无专用框架 Options 字段，待游戏层使用（数据层
        // item.template.item_type == consumable 决定某件物品是否为消耗品，本项只作为立项决策
        // 记录位，不驱动任何代码分支）。
        public bool ConsumablesEnabled = false;

        // 13 §4 第 5 行：跳跃 —— 无专用框架 Options 字段，待游戏层使用（影响碰撞/导航高度语义，
        // 见 05_对象模型与世界.md）。
        public bool JumpEnabled = false;

        // 13 §4 第 6 行：命中表项（招架/偏斜/格挡）—— CombatOptions.HitTableConfigId，指向
        // combat.hit_table_config 数据行；该数据行的字段决定具体开哪些分支，本字段只是指向哪一条。
        public string HitTableConfigId = "combat.hit_table.default";

        // 13 §4 第 7 行：资源类型数量与种类 —— PresentationAssemblyOptions.HudPowerTypes（HUD
        // 展示哪些资源类型）+ arch.power_type 数据本身决定实际有几种资源。默认只展示生命值
        // （WellKnownPowers.Health，见 GameBootstrap.BuildPresentationOptions）。
        public bool MultiplePowerPoolsEnabled = false;

        // 13 §4 第 8 行：控制递减 —— 13 号文档原文"若确有需要可作为游戏层自定义策略实现，不建议
        // 作为通用原语"，无框架 Options 字段，待游戏层使用。
        public bool DiminishingReturnsEnabled = false;

        // 13 §4 第 9 行：方向档位数（仅 sprite 型外形）—— Presentation.Render.RenderOptions.DirectionCount。
        public int DirectionCount = 8;

        // 13 §4 第 10 行：外形类型 —— 无专用框架 Options 字段，待游戏层使用（数据层
        // display.map/display.weapon_style 的 kind 字段区分 sprite/model）。
        public bool UseModelShape = false;

        // 13 §4 第 11 行：场景做法 —— 无专用框架 Options 字段，待游戏层使用（引擎适配层/资产管线
        // 决策，见 02_引擎适配层.md）。
        public bool Use3DScene = false;

        // 13 §4 第 12 行：镜头俯角与缩放范围 —— camera_profile 数据表字段 + ICamera.configure，
        // 无专用框架 Options 字段直接对应"角度/缩放范围"本身；CameraHostOptions 只管相机阶段切换
        // （PhaseProfileSwitch）与场景加载完成后是否重置跟随，作为该口味项在 Options 层唯一能挂的
        // 接入点一并声明。
        public bool CameraResetFollowOnSceneLoadFinished = true;

        // 13 §4 第 13 行：装备外观方式 —— PresentationAssemblyOptions.EquipmentSlotIds（背包/纸
        // 娃娃展示哪些槽位）+ display.equip_visual 数据（model 型外形下的槽位网格替换）。默认空，
        // 待游戏层按自己的槽位登记表填入。
        public string[] EquipmentSlotIds = Array.Empty<string>();

        // W6-B 新增（ADR-0017 决策 e）：主手槽位 id —— Presentation.VfxSfx.Core.
        // EquipmentWeaponStyleSource 的 MainHandWeaponTemplateResolver 用它从
        // EquipmentHost.GetAllEquippedInstances(unitId) 取主手武器的物品模板 id，进而查
        // display.map.weapon_style_ref（见该类型判断记录"未新增 item.template 字段"）。09/04
        // 未定义全局槽位登记表，本字段是该口味配置项在本模板的落点，默认 "item.slot.main_hand"；
        // 找不到该槽位当前装备时解析结果为 null（不影响其它 13 §4 第 13 行既有装备槽位口味项）。
        public string MainHandSlotId = "item.slot.main_hand";

        // W6 收口新增（ADR-0017 决策 d 遗留缺口收口）：命中帧同步开关 —— 与 09/ADR-0017"渲染侧/
        // 反馈绑定侧是同一个口味配置项的两个落点"一致，同一个布尔值驱动
        // Presentation.Render.RenderOptions.HitFrameSync 与
        // Presentation.FeedbackBinder.Contracts.FeedbackOptions.HitFrameSync（见下方
        // BuildRenderOptions/BuildFeedbackOptions）。默认 false（LogicDriven，与改动前行为一致）；
        // 置 true 后（AnimKeyframeDriven）需要 display.map 的 model 型外形声明 anim_set 的
        // hit_frame 事件、feedback.binding 声明 sync: hit_frame 才有实际效果（09 第 4.3/6.1 节），
        // sprite 型外形同样受同一策略驱动（见 SpriteCharacterRig 判断记录），不是 model 型专属。
        public bool HitFrameSyncEnabled = false;

        // 13 §4 第 14 行：影子方式 —— 无专用框架 Options 字段，待游戏层使用。
        public string ShadowMode = "blob";

        // 13 §4 第 15 行：死亡策略 —— CombatOptions.DeathPolicy（RespawnPolicy 枚举）。
        public RespawnPolicy DeathPolicy = RespawnPolicy.RespawnPoint;

        // 13 §4 第 16 行：自动存档点 —— SaveSystemOptions.AutoSave（AutoSavePolicy：
        // OnSavePoint/OnMapSwitch/OnQuestComplete）。
        public bool AutoSaveOnSavePoint = true;
        public bool AutoSaveOnMapSwitch = false;
        public bool AutoSaveOnQuestComplete = true;

        // 13 §4 第 17 行：画布分辨率与拉伸策略 —— 无专用框架 Options 字段，待游戏层使用（Unity UI
        // Canvas Scaler / Adapter.Unity.Ui.UiRoot 的引擎适配层设置，不在 core/presentation 的
        // Options 类范围内）。
        public int ReferenceResolutionWidth = 1920;
        public int ReferenceResolutionHeight = 1080;

        // 13 §4 第 18 行：反馈合并窗口 —— FeedbackOptions.MergeWindow / MergeMode。
        public double FeedbackMergeWindowSeconds = 0.0;

        // 13 §4 第 19 行：光环多来源分别计时 —— SkillOptions.AllowMultiSourceTiming。
        public bool AuraMultiSourceTimingEnabled = false;

        // 13 §4 第 20 行：单位间移动阻挡（unit_block）—— MovementOptions.UnitBlocking（加固任务
        // 已接线，见 BuildMovementOptions()；05 §3.6 碰撞层规划落地）。
        public bool UnitBlockEnabled = false;

        // NF2：寻路失败/阻挡变化时的移动处理策略 —— MovementOptions.PathFailurePolicy / BlockingChangePolicy
        // （W9 导航与移动公共接口补齐新增，见 CHANGELOG [1.10.0]）。默认值即框架默认（KeepOldPath / Replan），
        // 见 BuildMovementOptions()。
        public Core.Carriers.Unit.PathFailurePolicy PathFailurePolicy = Core.Carriers.Unit.PathFailurePolicy.KeepOldPath;
        public Core.Carriers.Unit.BlockingChangePolicy BlockingChangePolicy = Core.Carriers.Unit.BlockingChangePolicy.Replan;

        // 13 §4 第 21 行：地面掉落物是否随存档持久化 —— LootOptions.PersistDropped。
        public bool PersistDroppedLoot = true;

        // 13 §4 第 22～27 行：探索/战斗时间模型、先攻策略、每回合秒数换算、移动预算规则、网格吸附
        // —— 均是 found.time_model 数据表字段（scope=exploration/combat 各一行），不是 C# Options
        // 字段；见模板 data/game/found/found.time_model.json（若已按 13 §6 补齐）与
        // core/foundation/sim_loop/schema/TimeModelSchema.cs。移动预算规则里"距离"档位另有
        // MovementOptions.DiscreteTurnEquivalentSeconds 这个 Options 层接入点，一并声明在此。
        public double DiscreteTurnEquivalentSeconds = 1.0;

        // 13 §4 第 28 行：节奏策略 —— GameplayAssembly(pacingPolicy:)，取
        // Core.Foundation.SimLoop.ImmediatePacingPolicy 或 WaitForPlaybackPacingPolicy 二选一；
        // true = wait_for_playback，false（默认）= immediate。
        public bool PacingWaitForPlayback = false;

        // F3 新增口味配置项：开发期数据热重载（见 Runtime/DataHotReload.cs、
        // architecture/13_新游戏接入指南.md 第 4 节新增行）。默认 true——GameBootstrap 在数据加载
        // 完成后按本开关决定是否挂载 DataHotReload 组件；该组件本身只在 UNITY_EDITOR ||
        // DEVELOPMENT_BUILD 下编译为有效实现（其余情况为空壳，见该文件判断记录"编译条件"），发布
        // 构建下本开关不生效——不需要游戏层在发布构建前额外记得关掉它。
        public bool EnableDataHotReload = true;

        // -----------------------------------------------------------------
        // 第十一方深度审核修复（architecture/落地计划/audit-85f1f4f-20260908，08 号文档"装配扩展点"
        // 一节）：GameplayAssembly 构造函数已支持的 questOwnerResolver/questDayProvider/
        // vendorOpenRequested 三个可选回调，此前三处装配根（含本模板）内部构造 GameplayAssembly 时
        // 都没有转发（恒等价于不传，见 GameBootstrap.Bootstrap() 对应判断记录），游戏层无法在不
        // 绕开本装配根、自行重新拼一遍 GameplayAssembly 的前提下接上"击杀归属判定""每日任务按天数
        // 重置""gossip 打开商店 UI"这三个能力。委托类型不是 Unity 可序列化类型，不会出现在
        // Inspector 里，只能由具体游戏代码在 GameBootstrap.Awake（进而 Bootstrap()）真正读取之前
        // 赋值——典型做法是先把承载本组件的 GameObject 建成未激活状态（<c>SetActive(false)</c>）、
        // <c>AddComponent&lt;GameBootstrap&gt;()</c>（Unity 惯例：未激活物体上新增组件时 Awake 会
        // 推迟到 <c>SetActive(true)</c> 才触发），此刻赋值 <c>Options.QuestDayProvider</c> 等字段，
        // 再激活触发真正的 Awake/Bootstrap()（同 adapters/unity 包
        // SharedBootstrapDiscreteTests.BuildInactiveBootstrapWithDiscreteOverlay 同款测试惯例）。
        // 默认 null，不改变现有行为——GameplayAssembly 对应三个参数的默认值同样是 null。
        // -----------------------------------------------------------------
        public Func<Id, Id?>? QuestOwnerResolver;
        public Func<long>? QuestDayProvider;
        public Core.Gameplay.Dialog.VendorOpenRequestedCallback? VendorOpenRequested;

        /// <summary>
        /// P4 备忘 3：带商人引用（<c>vendor</c> 动作的 <c>ref</c>）的打开商店回调；装配完成后转发给 <c>Gameplay.Dialog.VendorOpenRequestedWithRef</c>，
        /// 与 <see cref="VendorOpenRequested"/> 并存（两者都设置时先调旧的）。是属性而不是字段：不属于 Unity 可序列化类型，避免被
        /// <c>GameOptionsTests</c> 的"值字段 JsonUtility 往返"遍历误收（同 <see cref="FeelOptions"/>）。默认 null。
        /// </summary>
        public Core.Gameplay.Dialog.VendorOpenRequestedWithRefCallback? VendorOpenRequestedWithRef { get; set; }

        /// <summary>
        /// 手感落地 S10：手感系统装配选项（转发给 GameplayAssembly 的 <c>feelOptions</c>）。默认 null 即不启用手感，行为与此前逐位一致。
        /// 与上面三个委托字段同一惯例：不是 Unity 可序列化类型，只能由游戏代码在 Bootstrap() 真正读取之前赋值。是属性而不是字段，
        /// 避免被 <c>GameOptionsTests</c> 的"值字段 JsonUtility 往返"遍历误收。启用时数据根必须含 feel.* 行（如框架的 data/_feel）。
        /// </summary>
        public Core.Carriers.Assembly.CarriersFeelOptions? FeelOptions { get; set; }

        /// <summary>手感框架数据根（框架分发包的 <c>data/_feel</c>：手感档案、预设、缺省标定行、武器原型，随 Adapter.Unity 包同步进内容根）。</summary>
        public const string FeelDatasetRoot = "data/_feel";

        /// <summary>
        /// 内容起步包数据根（框架分发包的 <c>data/_starter_kit</c>：技能/敌人/装备/掉落/任务/对话/地图规则模板、通用界面与系统文案、数值曲线）。
        /// 可选内容，<b>默认不装载</b>；游戏要用就在 <see cref="ExtraFrameworkDatasetRoots"/> 里声明它（见下方注释里的示例），再删掉自己数据里与它重复的通用表
        /// （或在自己的行上写 <c>"override": true</c>）。
        /// </summary>
        public const string StarterKitDatasetRoot = "data/_starter_kit";

        /// <summary>
        /// 手感落地 M1 补缺：额外的框架级数据根（相对内容根，与 <c>GameBootstrap</c> 的 <c>_frameworkDatasetRoot</c> 同一套解析规则），
        /// 与框架根、游戏根一起按顺序合并加载（顺序：框架根 → <see cref="FeelDatasetRoot"/>（仅在 <see cref="FeelOptions"/> 非空时自动加入）→ 本字段各项 → 游戏根）。
        /// 缺省空数组：不开手感、不配本字段时数据来源与此前逐位一致。游戏需要框架之外的共享数据包（如自己的另一套装备资产包）时在这里追加。
        /// </summary>
        // 用内容起步包起步（默认关闭；取消下一行注释即声明起步包，起步包的 l10n.locale/l10n.text/stat/prog 等通用表不要在游戏数据根里重复定义）：
        // public string[] ExtraFrameworkDatasetRoots = new[] { StarterKitDatasetRoot };
        public string[] ExtraFrameworkDatasetRoots = Array.Empty<string>();

        /// <summary>
        /// 按上面的顺序算出本次要加载的额外框架数据根（去重、跳过空串）：开启手感（<see cref="FeelOptions"/> 非空）时第一项是 <see cref="FeelDatasetRoot"/>，
        /// 游戏无需再手工配；不开启手感且没配 <see cref="ExtraFrameworkDatasetRoots"/> 时为空。
        /// </summary>
        internal IReadOnlyList<string> BuildExtraFrameworkDatasetRoots()
        {
            var roots = new List<string>();
            if (FeelOptions != null)
            {
                roots.Add(FeelDatasetRoot);
            }

            if (ExtraFrameworkDatasetRoots != null)
            {
                foreach (var root in ExtraFrameworkDatasetRoots)
                {
                    if (!string.IsNullOrEmpty(root) && !roots.Contains(root))
                    {
                        roots.Add(root);
                    }
                }
            }

            return roots;
        }

        /// <summary>见字段注释：把 <see cref="HitTableConfigId"/> 转成 <see cref="CombatOptions"/>。</summary>
        internal CombatOptions BuildCombatOptions() => new CombatOptions
        {
            HitTableConfigId = new Id(HitTableConfigId),
            DeathPolicy = DeathPolicy,
        };

        internal SkillOptions BuildSkillOptions() => new SkillOptions
        {
            GcdEnabled = GlobalCooldownEnabled,
            GcdDuration = GlobalCooldownDuration,
            AllowMultiSourceTiming = AuraMultiSourceTimingEnabled,
        };

        internal Core.Rules.Targeting.TargetingOptions BuildTargetingOptions() => new Core.Rules.Targeting.TargetingOptions
        {
            DefaultRadius = TargetingDefaultRadius,
        };

        internal Core.Carriers.Unit.MovementOptions BuildMovementOptions() => new Core.Carriers.Unit.MovementOptions
        {
            DiscreteTurnEquivalentSeconds = DiscreteTurnEquivalentSeconds,
            // 13 §4 第 20 行：单位间移动阻挡（unit_block）→ MovementOptions.UnitBlocking（加固任务，
            // 05 §3.6 碰撞层规划落地，见该字段判断记录）。UnitBlockRadius 沿用 MovementOptions 自身
            // 默认值 0.5，本模板暂不额外暴露口味项，游戏层如需调整直接改 GameOptions 或绕过本方法。
            UnitBlocking = UnitBlockEnabled,
            PathFailurePolicy = PathFailurePolicy,
            BlockingChangePolicy = BlockingChangePolicy,
        };

        internal Core.Gameplay.Loot.LootOptions BuildLootOptions() => new Core.Gameplay.Loot.LootOptions
        {
            PersistDropped = PersistDroppedLoot,
        };

        internal RenderOptions BuildRenderOptions() => new RenderOptions
        {
            DirectionCount = DirectionCount,
            HitFrameSync = HitFrameSyncEnabled ? HitFrameSyncStrategy.AnimKeyframeDriven : HitFrameSyncStrategy.LogicDriven,
        };

        internal FeedbackOptions BuildFeedbackOptions() => new FeedbackOptions
        {
            MergeWindow = FeedbackMergeWindowSeconds,
            HitFrameSync = HitFrameSyncEnabled ? HitFrameSyncStrategy.AnimKeyframeDriven : HitFrameSyncStrategy.LogicDriven,
        };

        internal IReadOnlyList<Id> BuildEquipmentSlotIds()
        {
            var result = new Id[EquipmentSlotIds.Length];
            for (var i = 0; i < EquipmentSlotIds.Length; i++)
            {
                result[i] = new Id(EquipmentSlotIds[i]);
            }
            return result;
        }
    }
}
