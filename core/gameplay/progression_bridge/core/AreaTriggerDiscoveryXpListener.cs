using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.WorldState;
using Core.Numbers.Progression;
using Core.Rules.Common;

namespace Core.Gameplay.ProgressionBridge
{
    /// <summary>
    /// 某个 <c>area.trigger_def</c>（<c>trigger_type=quest_explore</c>，见 05 第 7 节）是否配置为
    /// "探索奖励区域"、以及该区域的等级——由调用方（游戏内容/组合根）提供，框架本身不知道具体游戏
    /// 有哪些区域（<paramref name="triggerId"/> 未配置为探索奖励区域时返回 <c>null</c>，本类据此
    /// 跳过发放，见 <see cref="AreaTriggerDiscoveryXpListener"/> 类型注释"契约疑点上报"）。
    /// </summary>
    public delegate int? AreaDiscoveryLevelResolver(Id triggerId);

    /// <summary>
    /// 探索经验监听器（分阶段落地计划 T-N4-3；[ADR-0033]
    /// (../../../../architecture/adr/0033-等级经验模块正文与当量来源.md) 决策 3"探索 = 一只怪当量 ×
    /// 击杀基数(区域等级)，首次进入由 <c>WorldState</c> 标志保证"；06 第 2.5 节）：订阅
    /// <see cref="AreaTriggerEnteredEvent"/>（见 <c>area_trigger</c> 模块 <c>quest_explore</c> 触发器
    /// 类型，05 第 7 节"quest_explore 无需额外字段"），玩家单位首次进入某个已配置为"探索奖励区域"的
    /// 触发器范围时按 <c>prog.xp_source(kind=discovery)</c> 经 <see cref="IProgressionHost.GrantXp"/>
    /// 发放经验，来源等级取该区域的等级；一次性标志经 <see cref="IWorldState"/> 保证，标志一经写入
    /// 随存档持久化（<see cref="IWorldState"/> 已由装配根注册为 <c>ISaveSystem.Persistable</c>，见
    /// <c>core/gameplay/assembly.GameplayAssembly</c> 判断记录，本类不需要另外处理存读档）。
    /// <para>
    /// 契约疑点上报/临时判断（"区域"由哪个概念承载、"区域等级"从哪里来）：05/06/ADR-0033 均未给出
    /// "探索经验的区域等级具体从哪张表/哪个字段读取"的字面结论——<c>area.trigger_def</c> 的
    /// <c>quest_explore</c> 类型按 05 第 7 节"无需额外字段"定死不带任何 <c>params</c>，本模块允许
    /// 改动的目录也不包含"新增一种区域/等级表"这类新契约面。临时判断：本类把
    /// <see cref="AreaTriggerEnteredEvent.TriggerId"/> 本身当作"区域 id"（每个 <c>quest_explore</c>
    /// 触发器天然唯一，见 <c>AreaTriggerDef.Id</c>），区域等级经
    /// <see cref="AreaDiscoveryLevelResolver"/> 委托由调用方（游戏内容/组合根）按需提供——同
    /// <c>CreatureDeathLootListener</c> 的 <c>Func&lt;double&gt; lootMultiplierProvider</c>"难度模块
    /// 提供；默认……"一类扩展点写法：框架本身是"技术无关、游戏无关"的基础设施（本仓库 CLAUDE.md"面向
    /// 后续所有游戏"），不预设任何具体区域列表；委托未注入或对某个触发器 id 返回 <c>null</c> 时按
    /// "这个触发器不是探索奖励区域"处理，不发放、不记一次性标志，与"没有这个功能"之前完全一致的
    /// 零成本退化路径。若后续设计层认为区域等级应该落在数据表而不是委托（例如给
    /// <c>quest_explore</c> 补一个可选字段，或新增一张"区域"表），需要新任务重新拆分——本任务不擅自
    /// 改 05 的"quest_explore 无需额外字段"结论（架构文档改结论须先出 ADR，不在本任务允许范围）。
    /// </para>
    /// <para>
    /// 判断记录（一次性标志键：<c>world.&lt;once_key 前缀&gt;.&lt;triggerId&gt;</c>）：
    /// <c>prog.xp_source.once_key</c>（ADR-0033 决策 3"探索类一次性标志键前缀"）登记的是"前缀"本身，
    /// 不是完整标志键——多个区域共用同一条全局 <c>discovery</c> 来源 id（见
    /// <see cref="ProgressionOptions.DiscoveryXpSourceId"/> 判断记录），因此需要按
    /// <see cref="AreaTriggerEnteredEvent.TriggerId"/> 再分叉，否则任意一个区域首次进入就会把全部
    /// 区域的标志一并"点亮"。取前缀的优先级：该来源记录自身登记的 <c>once_key</c> 字段（存在且非空）
    /// 优先于 <see cref="ProgressionOptions.DefaultOnceKeyPrefix"/>（ADR-0033 决策 3 字段定义"探索类
    /// 一次性标志键前缀"，<see cref="ProgressionOptions.DefaultOnceKeyPrefix"/> 判断记录"某条来源缺省
    /// 未填 once_key 时……回退到本前缀"）。<see cref="IWorldState.Set"/>/<see cref="IWorldState.Has"/>
    /// 要求 <c>flagKey</c> 必须以 <c>"world."</c> 开头（见该接口 <c>Set</c> 文档），前缀本身若未带这个
    /// 命名空间头，本类负责补上——内容作者不需要在 <c>once_key</c> 里重复写 <c>"world."</c>。
    /// </para>
    /// </summary>
    public sealed class AreaTriggerDiscoveryXpListener
    {
        /// <summary>未显式配置 <see cref="ProgressionOptions.DiscoveryXpSourceId"/> 时使用的约定 id
        /// （见该字段判断记录"契约疑点上报"）。</summary>
        public static readonly Id DefaultDiscoveryXpSourceId = new Id("prog.xp_source.discovery");

        private static readonly Id WriterId = new Id("progression_bridge.discovery_xp_listener");

        private readonly IProgressionHost _progression;
        private readonly IUnitAccess _units;
        private readonly IWorldState _worldState;
        private readonly IDataRegistryView _registry;
        private readonly AreaDiscoveryLevelResolver _levelResolver;
        private readonly Id _discoveryXpSourceId;
        private readonly string _defaultOnceKeyPrefix;

        public AreaTriggerDiscoveryXpListener(
            IEventBus bus,
            IProgressionHost progression,
            IUnitAccess units,
            IWorldState worldState,
            IDataRegistryView registry,
            AreaDiscoveryLevelResolver? levelResolver = null,
            ProgressionOptions? options = null)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _levelResolver = levelResolver ?? (_ => null);

            var opts = options ?? new ProgressionOptions();
            _discoveryXpSourceId = opts.DiscoveryXpSourceId ?? DefaultDiscoveryXpSourceId;
            _defaultOnceKeyPrefix = opts.DefaultOnceKeyPrefix;

            bus.Subscribe<AreaTriggerEnteredEvent>(AreaTriggerEventKeys.TriggerEntered, OnTriggerEntered);
        }

        private void OnTriggerEntered(AreaTriggerEnteredEvent evt)
        {
            if (_units.GetSourceKind(evt.UnitId) != SourceKind.Player)
            {
                // 判断记录（不做召唤物→主人的探索归属）：ADR-0033 决策 3 只字面给出"召唤物击杀归
                // 主人"，未提及探索场景；本类按最小范围落地，只服务玩家单位本人直接进入的场景，见
                // 类型注释"契约疑点上报"同一处审慎原则（未声明的场景不擅自扩展）。
                return;
            }

            var regionLevel = _levelResolver(evt.TriggerId);
            if (!regionLevel.HasValue)
            {
                // 该触发器未配置为探索奖励区域，见 AreaDiscoveryLevelResolver 判断记录。
                return;
            }

            var onceKey = BuildOnceKey(evt.TriggerId);
            if (_worldState.Has(onceKey))
            {
                // 已经发放过（含读档恢复的标志——IWorldState 随存档持久化，见类型注释）。
                return;
            }

            // T-N4-4 附带任务（设计层裁定）：改为显式查询 IProgressionHost.HasXpSource，取代此前
            // try/catch(ArgumentException)——判断记录同 CreatureDeathXpListener。未登记的来源 id
            // 不阻断触发器进入的其余处理，但一次性标志仍然写入（见下方，避免来源 id 配置修好之后
            // 同一个区域被"追发"一次，语义上"这次进入已经处理过"比"这次进入到底发出了多少经验"更
            // 贴近"一次性"这个约定本身——同 GetXpToNext 满级归零"不发事件也算处理过一次"同一惯例）。
            if (_progression.HasXpSource(_discoveryXpSourceId))
            {
                _progression.GrantXp(evt.UnitId, _discoveryXpSourceId, new XpContext(regionLevel.Value));
            }

            _worldState.Set(onceKey, ExprValue.OfBool(true), WriterId);
        }

        private Id BuildOnceKey(Id triggerId)
        {
            var prefix = _defaultOnceKeyPrefix;
            var record = _registry.Get("prog.xp_source", _discoveryXpSourceId);
            if (record != null && record.TryGetString("once_key", out var configured) && !string.IsNullOrEmpty(configured))
            {
                prefix = configured;
            }

            var withNamespace = prefix.StartsWith("world.", StringComparison.Ordinal) ? prefix : $"world.{prefix}";
            return new Id($"{withNamespace}.{triggerId.Value}");
        }
    }
}
