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

        // 13 §4 第 20 行：单位间移动阻挡（unit_block）—— 无专用框架 Options 字段，待游戏层使用。
        public bool UnitBlockEnabled = false;

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
        };

        internal Core.Gameplay.Loot.LootOptions BuildLootOptions() => new Core.Gameplay.Loot.LootOptions
        {
            PersistDropped = PersistDroppedLoot,
        };

        internal RenderOptions BuildRenderOptions() => new RenderOptions
        {
            DirectionCount = DirectionCount,
        };

        internal FeedbackOptions BuildFeedbackOptions() => new FeedbackOptions
        {
            MergeWindow = FeedbackMergeWindowSeconds,
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
