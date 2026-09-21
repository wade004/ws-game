using System;
using System.Collections.Generic;
using Core.Carriers.Creature;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Combat;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// <c>target.*</c> 路径的解答者（见任务书路径小语法 "target.&lt;...&gt;"）。09_表现层.md 第 7.1
    /// 节把"目标框"列为 UI 组成之一，目标框典型只需要生命/资源条与关键属性——本 Provider 因此支持
    /// <c>target.power.&lt;powerType&gt;.current|max</c>、<c>target.stat.&lt;statId&gt;</c> 两条子路径
    /// （与 <c>player.*</c> 同款语法，见 <see cref="UnitSubQueries"/>），不支持 player 专有的
    /// inventory/equipment/quest/currency/skills 系列（当前目标不是玩家自己的容器）。
    /// <para>
    /// "当前目标是谁"由构造期注入的 <paramref name="targetResolver"/> 委托给出（见任务书
    /// "target.&lt;...&gt;（当前目标经注入的 Func&lt;Id?&gt; targetResolver）"）——本 Provider 不
    /// 自己维护目标状态，目标选择/切换属于玩法层或表现层其它模块（如战斗目标锁定）的职责。
    /// </para>
    /// <para>
    /// 消费方反馈第 3 条续（2026-09-21，沿用 ADR-0048 口径）：新增 <c>target.name</c>/
    /// <c>target.faction</c> 两条叶子路径，补齐目标框展示名字与阵营所需的最小数据（此前只有
    /// <c>target.id</c>，接入方要展示名字/阵营只能自行查表硬编码，见本模块 README"判断记录"）。
    /// 两者延续本仓库一贯的"转发原始标识，不做本地化/展示解析"惯例：
    /// <list type="bullet">
    /// <item><c>target.faction</c> 经新增的 <paramref name="unitAccess"/>（可选构造参数，见新增
    /// 重载）转发 <see cref="IUnitAccess.GetFaction"/>——阵营是 <c>Unit</c> 运行期字段（05 第 1.2 节
    /// <c>Unit.factionId</c>），本就必填，不存在"未登记"这一失败态。</item>
    /// <item><c>target.name</c> 先经 <see cref="IUnitAccess.GetTemplateId"/> 取目标的内容模板 id，
    /// 再经新增的 <paramref name="creatureTemplates"/>（可选构造参数）查
    /// <see cref="CreatureTemplate.NameKey"/>（文本键，不是已本地化文本，同
    /// <c>skill.def.name_key</c>/<c>ActionBarViewModel.NameKey</c> 口径）——模板 id 缺失或未登记
    /// 属于内容配置问题，记一条诊断后返回 <c>null</c>，不回退成占位文案（AGENTS.md §3"运行时路径
    /// 不静默降级"）。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 消费方反馈第 1/4 条（2026-09-21，ADR-0056）：新增 <c>target.casting.skill|remaining|total</c>
    /// （目标读条预警：谁在读条、还剩多久、总共多久）、<c>target.auras.count</c>/
    /// <c>target.auras[i].def|stacks|remaining|total|name_key</c>（目标控制/增益/减益列表）两组子
    /// 路径，均委托 <see cref="UnitSubQueries.Casting"/>/<see cref="UnitSubQueries.Auras"/> 共享逻辑
    /// （与 <see cref="PlayerPathProvider"/> 复用同一份解析代码）。两者均需要新增重载注入的
    /// <see cref="_skillBook"/>/<see cref="_auraQuery"/>，未装配旧重载的调用方两条路径恒返回"无"
    /// （不记诊断，同 <see cref="_unitAccess"/> 既有惯例）。
    /// </para>
    /// </summary>
    public sealed class TargetPathProvider : IUiPathProvider
    {
        private readonly Func<Id?> _targetResolver;
        private readonly IStatHost _statHost;
        private readonly IPowerHost _powerHost;
        private readonly IUnitAccess? _unitAccess;
        private readonly ICreatureTemplateQuery? _creatureTemplates;
        private readonly ISkillBookQuery? _skillBook;
        private readonly IAuraQuery? _auraQuery;
        private readonly AutoAttackHost? _autoAttackHost;

        public TargetPathProvider(Func<Id?> targetResolver, IStatHost statHost, IPowerHost powerHost)
        {
            _targetResolver = targetResolver ?? throw new ArgumentNullException(nameof(targetResolver));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
        }

        /// <summary>
        /// 新增重载（2026-09-21）：携带 <paramref name="unitAccess"/>/<paramref name="creatureTemplates"/>
        /// 才能解答 <c>target.faction</c>/<c>target.name</c>（见类型注释）；纯加法，既有三参数构造
        /// 函数不改一个字节，仍供不需要这两条新路径的既有调用方使用。
        /// </summary>
        public TargetPathProvider(
            Func<Id?> targetResolver,
            IStatHost statHost,
            IPowerHost powerHost,
            IUnitAccess unitAccess,
            ICreatureTemplateQuery creatureTemplates)
            : this(targetResolver, statHost, powerHost)
        {
            _unitAccess = unitAccess ?? throw new ArgumentNullException(nameof(unitAccess));
            _creatureTemplates = creatureTemplates ?? throw new ArgumentNullException(nameof(creatureTemplates));
        }

        /// <summary>
        /// 消费方反馈第 1/4 条新增重载（2026-09-21，ADR-0056）：携带 <paramref name="skillBook"/>/
        /// <paramref name="auraQuery"/> 才能解答 <c>target.casting.*</c>/<c>target.auras.*</c>（目标
        /// 读条预警、目标控制/增益/减益状态，见类型注释）。判断记录（新增重载而不是给上一个五参数
        /// 构造函数追加可选参数）：同类型内既有重载判断记录同一套 ABI 兼容惯例——本重载七个参数全部
        /// 不带默认值，与既有两个构造函数（分别恰好三个、恰好五个参数）参数个数不重叠，互不冲突。
        /// </summary>
        public TargetPathProvider(
            Func<Id?> targetResolver,
            IStatHost statHost,
            IPowerHost powerHost,
            IUnitAccess unitAccess,
            ICreatureTemplateQuery creatureTemplates,
            ISkillBookQuery skillBook,
            IAuraQuery auraQuery)
            : this(targetResolver, statHost, powerHost, unitAccess, creatureTemplates)
        {
            _skillBook = skillBook ?? throw new ArgumentNullException(nameof(skillBook));
            _auraQuery = auraQuery ?? throw new ArgumentNullException(nameof(auraQuery));
        }

        /// <summary>
        /// 消费方反馈第四批第 1 条新增重载（2026-09-21，ADR-0061）：携带 <paramref
        /// name="autoAttackHost"/>，才能解答 <c>target.auto_attack.state</c>（目标自身是否正在普通
        /// 攻击，见类型注释）。判断记录（<c>target.alive</c> 不需要新重载）：存活查询复用本类型五
        /// 参重载已经引入的 <see cref="_unitAccess"/>（见 <see cref="ResolveFaction"/>/<see
        /// cref="ResolveName"/> 既有惯例"未装配 unitAccess 时静默返回无"），本重载只为
        /// <c>auto_attack.*</c> 新增 <see cref="_autoAttackHost"/> 这一个依赖；八个参数全部不带默认
        /// 值，与既有三个构造函数（分别恰好三个/五个/七个参数）参数个数不重叠，互不冲突。
        /// </summary>
        public TargetPathProvider(
            Func<Id?> targetResolver,
            IStatHost statHost,
            IPowerHost powerHost,
            IUnitAccess unitAccess,
            ICreatureTemplateQuery creatureTemplates,
            ISkillBookQuery skillBook,
            IAuraQuery auraQuery,
            AutoAttackHost autoAttackHost)
            : this(targetResolver, statHost, powerHost, unitAccess, creatureTemplates, skillBook, auraQuery)
        {
            _autoAttackHost = autoAttackHost ?? throw new ArgumentNullException(nameof(autoAttackHost));
        }

        public string Root => "target";

        public ExprValue? Resolve(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            var targetId = _targetResolver();
            if (!targetId.HasValue)
            {
                // 当前无目标：合法查询、无值，不是路径错误，不记诊断。
                return null;
            }

            if (remaining.Count == 0)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 在 \"target\" 之后缺少子路径");
                return null;
            }

            switch (remaining[0].Name)
            {
                case "power":
                    return UnitSubQueries.Power(targetId.Value, _powerHost, remaining, fullPath, diagnostics);
                case "stat":
                    return UnitSubQueries.Stat(targetId.Value, _statHost, remaining, fullPath, diagnostics);
                case "id":
                    // 消费方反馈第 3 条（2026-09-20，ADR-0048）：目标身份原始 Id，供 HudViewModel.
                    // TargetId 转发给表现层自行决定如何展示（本仓库惯例是转发原始 Id，不在 presentation/ui
                    // 内新增名称解析服务，见 HudViewModel.TargetId 判断记录）。
                    return Exact(remaining, 1, fullPath, diagnostics) ? ExprValue.OfId(targetId.Value) : (ExprValue?)null;
                case "faction":
                    return Exact(remaining, 1, fullPath, diagnostics) ? ResolveFaction(targetId.Value) : (ExprValue?)null;
                case "name":
                    return Exact(remaining, 1, fullPath, diagnostics) ? ResolveName(targetId.Value, fullPath, diagnostics) : (ExprValue?)null;
                case "casting":
                    return UnitSubQueries.Casting(targetId.Value, _skillBook, remaining, fullPath, diagnostics);
                case "auras":
                    return UnitSubQueries.Auras(targetId.Value, _auraQuery, remaining, fullPath, diagnostics);
                case "alive":
                    return UnitSubQueries.Alive(targetId.Value, _unitAccess, remaining, fullPath, diagnostics);
                case "auto_attack":
                    return UnitSubQueries.AutoAttack(targetId.Value, _autoAttackHost, remaining, fullPath, diagnostics);
                default:
                    diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 target 子路径关键字 \"{remaining[0].Name}\" 未知（只支持 power/stat/id/name/faction/casting/auras/alive/auto_attack）");
                    return null;
            }
        }

        /// <summary>消费方反馈第 3 条续：目标阵营原始 Id（不解析显示文本，同 <c>target.id</c> 口径）。
        /// 未装配 <see cref="_unitAccess"/>（沿用旧三参数构造函数的调用方）或目标当前不在世界模拟中
        /// （<see cref="IUnitAccess.Exists"/> 为 <c>false</c>，理论上不应发生——<paramref name="targetId"/>
        /// 来自 targetResolver 且已通过存在性隐含前提，此处仍防御性判断）时返回"无"，不记诊断：
        /// <c>Unit.factionId</c>（05 第 1.2 节）是必填运行期字段，一旦单位存在就恒有合法值，唯一的
        /// "查不到"只可能是单位本身不存在这一transient 状态，与 <c>power</c>/<c>stat</c> 子路径对
        /// 未注册单位的既有处理（静默返回 null）同一惯例，不是内容配置问题。</summary>
        private ExprValue? ResolveFaction(Id targetId)
        {
            if (_unitAccess == null || !_unitAccess.Exists(targetId))
            {
                return null;
            }

            return ExprValue.OfId(_unitAccess.GetFaction(targetId));
        }

        /// <summary>消费方反馈第 3 条续：目标显示名文本键（<c>creature.template.name_key</c>，同
        /// <c>skill.def.name_key</c> 口径——文本键，不是已本地化文本，本表现层不做本地化）。
        /// 未装配 <see cref="_unitAccess"/>/<see cref="_creatureTemplates"/>（旧三参数构造函数）或
        /// 目标不在世界模拟中时返回"无"，不记诊断（同 <see cref="ResolveFaction"/> 判断记录）；
        /// 目标存在但取不到内容模板（<see cref="IUnitAccess.GetTemplateId"/> 为空——手工放置对象没有
        /// 模板引用）或模板 id 未在 <see cref="_creatureTemplates"/> 登记，属于内容配置问题，记一条
        /// 诊断后返回"无"（AGENTS.md §3"运行时路径不静默降级"），不回退成占位文案。</summary>
        private ExprValue? ResolveName(Id targetId, string fullPath, IUiDiagnostics diagnostics)
        {
            if (_unitAccess == null || _creatureTemplates == null || !_unitAccess.Exists(targetId))
            {
                return null;
            }

            var templateId = _unitAccess.GetTemplateId(targetId);
            if (!templateId.HasValue)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的目标 \"{targetId}\" 没有内容模板引用，无法解析显示名");
                return null;
            }

            try
            {
                var template = _creatureTemplates.Get(templateId.Value);
                return ExprValue.OfId(template.NameKey);
            }
            catch (ArgumentException)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 引用的生物模板 \"{templateId.Value}\" 未登记，无法解析显示名");
                return null;
            }
        }

        private static bool Exact(IReadOnlyList<UiPathSegment> remaining, int count, string fullPath, IUiDiagnostics diagnostics)
        {
            if (remaining.Count == count && !remaining[0].Index.HasValue)
            {
                return true;
            }

            diagnostics.Warn($"UI 路径 \"{fullPath}\" 段数或下标形状不符合预期");
            return false;
        }
    }
}
