using System;
using Core.Carriers.Common;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Numbers.Progression;
using Core.Rules.Common;

namespace Core.Gameplay.ProgressionBridge
{
    /// <summary>
    /// 击杀经验监听器（分阶段落地计划 T-N4-3；[ADR-0033]
    /// (../../../../architecture/adr/0033-等级经验模块正文与当量来源.md) 决策 3"击杀 = 击杀基数
    /// (怪物等级) × 分档经验倍率 × 难度经验倍率 × 等级差表.经验系数(Δ)……召唤物击杀归主人"；06 第
    /// 2.5 节）：订阅 <c>unit.died</c>（见 <see cref="RulesEventKeys.UnitDied"/>），击杀者是玩家单位
    /// （或召唤物且其主人是玩家单位）时按 <c>prog.xp_source(kind=kill)</c> 经
    /// <see cref="IProgressionHost.GrantXp"/> 发放经验，来源等级取死亡单位当前等级。
    /// <para>
    /// 判断记录（本类只订阅事件，不读取掉落结果）：任务书硬性规则"禁止在监听器里读掉落结果"——本类
    /// 与 <see cref="Core.Gameplay.Loot.CreatureDeathLootListener"/> 各自独立订阅同一个
    /// <c>unit.died</c>，互不持有对方引用、不读取 <c>LootRolledEvent</c>/掉落结果，只共用"死亡单位
    /// 的模板/等级"这类与掉落无关的公共只读信息（经 <see cref="IUnitAccess"/>/
    /// <see cref="ICreatureTemplateQuery"/>，两个监听器各自持有一份，不是同一份共享状态）。
    /// </para>
    /// <para>
    /// 判断记录（订阅顺序"先掉落后经验"）：本类构造时机由装配根（
    /// <c>core/gameplay/assembly.GameplayAssembly</c>）决定——需要接在
    /// <see cref="Core.Gameplay.Loot.CreatureDeathLootListener"/> 构造之后（见该处判断记录、本模块
    /// README"接线顺序"）；<see cref="IEventBus"/> 按订阅顺序同步派发给同一事件的多个订阅者，
    /// 因此"先构造先收到"，本类不自行决定顺序。
    /// </para>
    /// <para>
    /// 判断记录（未登记的 <see cref="IProgressionHost.GrantXp"/> 来源 id 不阻断死亡结算）：T-N4-4
    /// 附带任务（设计层裁定）起改为先调用 <see cref="IProgressionHost.HasXpSource"/> 显式查询，
    /// 查到才调用 <see cref="IProgressionHost.GrantXp"/>——取代此前"直接调用、
    /// <c>try/catch (ArgumentException)</c> 兜未登记来源"的写法（<see cref="IProgressionHost.GrantXp"/>
    /// 对未登记的 <c>sourceId</c> 仍然抛 <see cref="ArgumentException"/>，本类只是不再依赖这条异常
    /// 路径）：同 <see cref="Core.Gameplay.Loot.CreatureDeathLootListener.OnUnitDied"/> 对未登记模板
    /// id 的处理惯例（"不是本监听器的职责范围，静默跳过，不阻断死亡结算流程"）——某个游戏/测试夹具
    /// 尚未配置 <see cref="ProgressionOptions.KillXpSourceId"/> 指向的 <c>prog.xp_source</c> 记录
    /// 时，击杀经验退化为"不发放"，不应该因此让整条死亡结算/事件派发链路崩溃。
    /// </para>
    /// </summary>
    public sealed class CreatureDeathXpListener
    {
        /// <summary>未显式配置 <see cref="ProgressionOptions.KillXpSourceId"/> 时使用的约定 id
        /// （见该字段判断记录"契约疑点上报"）。</summary>
        public static readonly Id DefaultKillXpSourceId = new Id("prog.xp_source.kill");

        private readonly IProgressionHost _progression;
        private readonly IUnitAccess _units;
        private readonly ICreatureTemplateQuery _templates;
        private readonly ISummonHost? _summons;
        private readonly Id _killXpSourceId;
        private readonly IProgressionBridgeDiagnostics _diagnostics;

        /// <summary>诊断出口只读暴露（ABI 只新增只读属性，见
        /// architecture/adr/0042-诊断契约统一转发到宿主控制台.md）：供 adapters/unity 侧统一诊断
        /// 转发机制轮询本实例累积的 Warnings，不改变本类型任何既有公开签名。</summary>
        public IProgressionBridgeDiagnostics Diagnostics => _diagnostics;

        public CreatureDeathXpListener(
            IEventBus bus,
            IProgressionHost progression,
            IUnitAccess units,
            ICreatureTemplateQuery templates,
            ISummonHost? summons = null,
            ProgressionOptions? options = null)
            : this(bus, progression, units, templates, summons, options, diagnostics: null)
        {
        }

        /// <summary>
        /// 消费方反馈第 6 条根治新增构造重载：追加 <paramref name="diagnostics"/>（ABI 门禁"公开
        /// API 只能新增"——既有六参构造函数已发布，直接追加参数会改变其物理 IL 签名，对已编译好的
        /// 外部消费方二进制是破坏性变更；本重载七个参数全部不带默认值，与既有构造函数在参数个数上
        /// 不重叠（6 对 7，惯例同 <see cref="Core.Rules.Combat.Resolver"/> 十四→十六参重载），互不
        /// 冲突，也不产生调用点重载二义性）。未提供时缺省 <see cref="InMemoryProgressionBridgeDiagnostics"/>
        /// （惯例同本仓库其余全部 Host 的 diagnostics 可选参数）。
        /// </summary>
        public CreatureDeathXpListener(
            IEventBus bus,
            IProgressionHost progression,
            IUnitAccess units,
            ICreatureTemplateQuery templates,
            ISummonHost? summons,
            ProgressionOptions? options,
            IProgressionBridgeDiagnostics? diagnostics)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _units = units ?? throw new ArgumentNullException(nameof(units));
            _templates = templates ?? throw new ArgumentNullException(nameof(templates));
            _summons = summons;
            _killXpSourceId = (options ?? new ProgressionOptions()).KillXpSourceId ?? DefaultKillXpSourceId;
            _diagnostics = diagnostics ?? new InMemoryProgressionBridgeDiagnostics();

            bus.Subscribe<UnitDiedEvent>(RulesEventKeys.UnitDied, OnUnitDied);
        }

        private void OnUnitDied(UnitDiedEvent evt)
        {
            if (!evt.KillerId.HasValue)
            {
                // 06 判断记录（环境死亡无明确攻击者，见 UnitDiedEvent.KillerId 类型注释）：没有击杀者
                // 就没有"归属"，直接跳过，不发放。
                return;
            }

            var creditUnitId = ResolveCreditUnit(evt.KillerId.Value);
            if (_units.GetSourceKind(creditUnitId) != SourceKind.Player)
            {
                // ADR-0033 决策 3 隐含前提"三种来源全部以同级怪当量计"面向玩家成长——击杀者不是玩家、
                // 也不归属任何玩家的召唤物（生物杀生物）时不发放经验（任务书验收标准"击杀者非玩家
                // 不发放"）。
                return;
            }

            var diedLevel = _units.GetLevel(evt.UnitId);
            var tierId = ResolveTierId(evt.UnitId);

            // T-N4-4 附带任务（设计层裁定）：改为显式查询 IProgressionHost.HasXpSource 后再发放，
            // 取代此前的 try/catch(ArgumentException)——"来源未登记"是正常场景（游戏/测试夹具尚未
            // 配置），不该靠异常控制流表达，见该接口成员判断记录。行为不变：未登记时静默跳过，不
            // 阻断死亡结算（类型注释"未登记的来源 id 不阻断死亡结算"）。
            if (_progression.HasXpSource(_killXpSourceId))
            {
                _progression.GrantXp(creditUnitId, _killXpSourceId, new XpContext(diedLevel, tierId: tierId));
            }
            else
            {
                // 消费方反馈第 6 条根治（运行时路径不静默降级，见 AGENTS.md §3）：此前"来源未登记
                // 则跳过"这一分支完全没有任何可观察信号，表现为"杀怪一直 0 经验且无任何线索"。
                // 行为不变（仍然跳过、不阻断死亡结算），只是显式标记——消息给到具体表名 + 具体
                // 缺失的来源 id，供内容作者直接去 prog.xp_source 表核对。
                _diagnostics.Warn(
                    $"CreatureDeathXpListener: prog.xp_source 未登记来源 \"{_killXpSourceId}\"，" +
                    $"跳过本次击杀经验发放（creditUnitId={creditUnitId}, diedUnitId={evt.UnitId}）");
            }
        }

        /// <summary>ADR-0033 决策 3"召唤物击杀归主人"：<paramref name="killerId"/> 是某个已登记召唤物
        /// 时改记其主人；否则原样返回 <paramref name="killerId"/> 本身（包括"是玩家本人"与"是普通
        /// 生物，非玩家、非召唤物"两种情形，留给调用方按 <see cref="IUnitAccess.GetSourceKind"/>
        /// 继续判断）。
        /// <para>
        /// 判断记录（2026-09-16 深度复审 D-M1 抽取）：本方法与
        /// <see cref="Core.Gameplay.Loot.CreatureDeathLootListener"/> 货币入账分支需要的"击杀者→记账
        /// 单位"解析逐字同构，两处一直各自维护一份容易漂移——改为共同调用
        /// <see cref="Core.Gameplay.Common.SummonCreditResolver.ResolveCreditUnit"/>，本方法保留原
        /// 名称/签名不变（只是内部转发），不影响任何既有调用点。
        /// </para>
        /// </summary>
        private Id ResolveCreditUnit(Id killerId) =>
            Core.Gameplay.Common.SummonCreditResolver.ResolveCreditUnit(_summons, killerId);

        /// <summary>死亡单位所属生物模板的分档 id（<c>creature.tier_definition</c>），供
        /// <see cref="XpContext.TierId"/> 登记——本类不消费该字段本身（T-N4-4"分档与难度经验倍率
        /// 注入"才读取，见 <see cref="XpContext"/> 类型注释"契约疑点上报"），只负责按 T-N4-4 任务表
        /// "涉及文件"预先把它从生物模板取出来传下去，避免 T-N4-4 还要反过来改本类的构造签名。未登记
        /// 模板 id、或该单位没有模板引用（手工放置对象）时留 <c>null</c>，不阻断经验发放本身——分档
        /// 倍率在 <see cref="TierId"/> 缺失时按 T-N4-4 落地的语义处理（当前恒等价于"无分档倍率"，见
        /// <see cref="ProgressionOptions.ExtraXpMultiplierProvider"/> 判断记录），不是本类的职责。</summary>
        private Id? ResolveTierId(Id diedUnitId)
        {
            var templateId = _units.GetTemplateId(diedUnitId);
            if (!templateId.HasValue)
            {
                return null;
            }

            try
            {
                return _templates.Get(templateId.Value).TierId;
            }
            catch (ArgumentException)
            {
                // 未登记的模板 id：同 CreatureDeathLootListener.OnUnitDied 判断记录，不是本方法的
                // 职责范围，静默退化为"无分档信息"。
                return null;
            }
        }
    }
}
