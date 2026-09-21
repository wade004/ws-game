using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Carriers.Common;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Combat;
using Core.Rules.Common;

namespace Presentation.Ui
{
    /// <summary>
    /// <c>player.*</c> 路径的解答者（见任务书路径小语法："player.power.&lt;powerType&gt;.current|max、
    /// player.stat.&lt;statId&gt;、player.level、player.xp、player.xp_to_next、
    /// player.inventory.count、player.inventory[i].template|count|instance、
    /// player.equipment.&lt;slot&gt;、player.equipment.&lt;slot&gt;.template|instance（ADR-0063 补充，
    /// 见 <see cref="ResolveEquipment"/> 判断记录）、player.quest.&lt;questId&gt;.state|objective[i]、
    /// player.currency.&lt;id&gt;、player.skills[i]、player.skill.&lt;id&gt;.cooldown、
    /// player.casting.skill|remaining|total、player.auras.count、
    /// player.auras[i].def|stacks|remaining|total|name_key"）。全部查询都绕着构造期注入的
    /// <see cref="_playerId"/>（当前玩家单位 id）展开，本 Provider 自身不做任何写操作（铁律 P1）。
    /// <para>
    /// 消费方反馈第 4 条（2026-09-21，ADR-0056）：<c>casting.*</c>/<c>auras.*</c> 两条子路径分别
    /// 委托 <see cref="UnitSubQueries.Casting"/>/<see cref="UnitSubQueries.Auras"/> 共享逻辑（与
    /// <see cref="TargetPathProvider"/> 复用同一份解析代码，唯一差异是 unitId 来源）。
    /// </para>
    /// </summary>
    public sealed class PlayerPathProvider : IUiPathProvider
    {
        private readonly Id _playerId;
        private readonly IStatHost _statHost;
        private readonly IPowerHost _powerHost;
        private readonly IProgressionHost _progression;
        private readonly IInventoryHost _inventory;
        private readonly IEquipmentHost _equipment;
        private readonly IQuestHost _quest;
        private readonly IEconomyHost _economy;
        private readonly ISkillBookQuery _skillBook;
        private readonly IAuraQuery? _auraQuery;
        private readonly IUnitAccess? _unitAccess;
        private readonly AutoAttackHost? _autoAttackHost;

        public PlayerPathProvider(
            Id playerId,
            IStatHost statHost,
            IPowerHost powerHost,
            IProgressionHost progression,
            IInventoryHost inventory,
            IEquipmentHost equipment,
            IQuestHost quest,
            IEconomyHost economy,
            ISkillBookQuery skillBook)
        {
            _playerId = playerId;
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _powerHost = powerHost ?? throw new ArgumentNullException(nameof(powerHost));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _quest = quest ?? throw new ArgumentNullException(nameof(quest));
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            _skillBook = skillBook ?? throw new ArgumentNullException(nameof(skillBook));
        }

        /// <summary>
        /// 消费方反馈第 4 条新增重载（2026-09-21，ADR-0056）：携带 <see cref="_auraQuery"/>，供
        /// <c>player.auras.*</c> 解答。判断记录（新增重载而不是给既有构造函数追加可选参数）：同
        /// <c>TargetPathProvider</c>/<c>SkillDef</c> 多处既有重载判断记录同一套 ABI 兼容惯例——既有
        /// 构造函数追加参数会改变其物理 IL 签名；本重载十个参数全部不带默认值，与既有构造函数（恰好
        /// 九个参数）参数个数不重叠，互不冲突。<c>casting.*</c> 复用既有必填的 <see cref="_skillBook"/>
        /// 字段，不需要新增依赖——沿用既有九参数构造函数即可解答，只有 <c>auras.*</c> 需要本重载。
        /// </summary>
        public PlayerPathProvider(
            Id playerId,
            IStatHost statHost,
            IPowerHost powerHost,
            IProgressionHost progression,
            IInventoryHost inventory,
            IEquipmentHost equipment,
            IQuestHost quest,
            IEconomyHost economy,
            ISkillBookQuery skillBook,
            IAuraQuery auraQuery)
            : this(playerId, statHost, powerHost, progression, inventory, equipment, quest, economy, skillBook)
        {
            _auraQuery = auraQuery ?? throw new ArgumentNullException(nameof(auraQuery));
        }

        /// <summary>
        /// 消费方反馈第四批第 1/2 条新增重载（2026-09-21，ADR-0061）：携带 <see cref="_unitAccess"/>/
        /// <see cref="_autoAttackHost"/>，才能解答 <c>player.alive</c>/<c>player.auto_attack.state</c>
        /// （见类型注释路径清单）。判断记录（一个重载一次性带上两个新能力，不拆成两个重载）：本批
        /// 两条反馈（存活状态、普通攻击状态）同一任务一并交付，没有"只要其中一个能力"的既有调用方
        /// 需要兼容——同 <c>TargetPathProvider</c> 五参重载一次性带上 <c>unitAccess</c>+
        /// <c>creatureTemplates</c> 两个新依赖的既有先例同一惯例，避免为不会出现的"单独启用一半"
        /// 组合平白多出一个从未被使用的重载。本重载十二个参数全部不带默认值，与既有两个构造函数
        /// （分别恰好九个、十个参数）参数个数不重叠，互不冲突。
        /// </summary>
        public PlayerPathProvider(
            Id playerId,
            IStatHost statHost,
            IPowerHost powerHost,
            IProgressionHost progression,
            IInventoryHost inventory,
            IEquipmentHost equipment,
            IQuestHost quest,
            IEconomyHost economy,
            ISkillBookQuery skillBook,
            IAuraQuery auraQuery,
            IUnitAccess unitAccess,
            AutoAttackHost autoAttackHost)
            : this(playerId, statHost, powerHost, progression, inventory, equipment, quest, economy, skillBook, auraQuery)
        {
            _unitAccess = unitAccess ?? throw new ArgumentNullException(nameof(unitAccess));
            _autoAttackHost = autoAttackHost ?? throw new ArgumentNullException(nameof(autoAttackHost));
        }

        public string Root => "player";

        public ExprValue? Resolve(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            if (remaining.Count == 0)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 在 \"player\" 之后缺少子路径");
                return null;
            }

            var head = remaining[0];
            switch (head.Name)
            {
                case "power":
                    return UnitSubQueries.Power(_playerId, _powerHost, remaining, fullPath, diagnostics);
                case "stat":
                    return UnitSubQueries.Stat(_playerId, _statHost, remaining, fullPath, diagnostics);
                case "level":
                    return Exact(remaining, 1, fullPath, diagnostics) ? ExprValue.OfInt(_progression.GetLevel(_playerId)) : (ExprValue?)null;
                case "xp":
                    return Exact(remaining, 1, fullPath, diagnostics) ? ExprValue.OfInt(_progression.GetXp(_playerId)) : (ExprValue?)null;
                case "xp_to_next":
                    return Exact(remaining, 1, fullPath, diagnostics) ? ExprValue.OfInt(_progression.GetXpToNext(_playerId)) : (ExprValue?)null;
                case "inventory":
                    return ResolveInventory(remaining, fullPath, diagnostics);
                case "equipment":
                    return ResolveEquipment(remaining, fullPath, diagnostics);
                case "quest":
                    return ResolveQuest(remaining, fullPath, diagnostics);
                case "currency":
                    return ResolveCurrency(remaining, fullPath, diagnostics);
                case "skills":
                    return ResolveSkillsIndex(remaining, fullPath, diagnostics);
                case "skill":
                    return ResolveSkillCooldown(remaining, fullPath, diagnostics);
                case "casting":
                    return UnitSubQueries.Casting(_playerId, _skillBook, remaining, fullPath, diagnostics);
                case "auras":
                    return UnitSubQueries.Auras(_playerId, _auraQuery, remaining, fullPath, diagnostics);
                case "alive":
                    return UnitSubQueries.Alive(_playerId, _unitAccess, remaining, fullPath, diagnostics);
                case "auto_attack":
                    return UnitSubQueries.AutoAttack(_playerId, _autoAttackHost, remaining, fullPath, diagnostics);
                default:
                    diagnostics.Warn($"UI 路径 \"{fullPath}\" 的子路径关键字 \"{head.Name}\" 未知");
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

        private ExprValue? ResolveInventory(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            var head = remaining[0];
            if (!head.Index.HasValue)
            {
                if (remaining.Count == 2 && remaining[1].Name == "count" && !remaining[1].Index.HasValue)
                {
                    return ExprValue.OfInt(_inventory.ListItems(_playerId).Count);
                }

                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 inventory 子路径只认识 \"inventory.count\" 或 \"inventory[i].<field>\"");
                return null;
            }

            if (remaining.Count != 2)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 inventory[i] 子路径缺少字段名");
                return null;
            }

            var items = _inventory.ListItems(_playerId);
            var idx = head.Index.Value;
            if (idx < 0 || idx >= items.Count)
            {
                return null;
            }

            var item = items[idx];
            switch (remaining[1].Name)
            {
                case "template":
                    return ExprValue.OfId(item.TemplateId);
                case "count":
                    return ExprValue.OfInt(item.Count);
                case "instance":
                    return ExprValue.OfId(item.InstanceId);
                default:
                    diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 inventory[i] 字段名 \"{remaining[1].Name}\" 未知");
                    return null;
            }
        }

        /// <summary>
        /// 消费方反馈第五批第 2 条（2026-09-21，ADR-0063）新增 <c>.template</c>/<c>.instance</c> 子
        /// 路径：装备面板要显示"槽位名 + 已装备物品名"，需要已装备物品的模板 id——此前
        /// <c>player.equipment.&lt;slot&gt;</c> 裸路径只返回实例 id（<see cref="IEquipmentHost.GetEquipped"/>
        /// 语义），拿不到模板 id。
        /// <para>
        /// 判断记录（末段保留关键字，惯例同 <see cref="ResolveQuest"/>/<see cref="ResolveSkillCooldown"/>）：
        /// 装备槽 id 允许多段（namespaced，如 <c>equip.main_hand</c>，见
        /// <c>Tests.PresentationUi.UiDataSourceTests.Query_equipment_slot</c>），与 <c>inventory[i].&lt;field&gt;</c>
        /// 的按下标定位不同，无法用"最后一段是字段名"以外的方式消歧。沿用 quest/skill 两条既有子路径
        /// 同一惯例：<c>remaining.Count &gt;= 3</c> 且末段不带下标、字面量恰为 <c>"template"</c>/
        /// <c>"instance"</c> 时按子路径解析（其余段拼装槽位 id）；否则落到既有裸查询分支，行为与本次
        /// 改动之前逐字节一致——<c>player.equipment.&lt;slot&gt;</c>（裸，返回实例 id）的既有行为不变。
        /// </para>
        /// <para>
        /// 判断记录（不给 <c>.count</c>/<c>.quality</c>/<c>.name_key</c>）：装备类物品
        /// <c>stack_size == 1</c>（见 07 校验），<c>.count</c> 恒为 1 没有信息量，不给；<c>quality</c>/
        /// 名称需要查 <c>item.template</c>/词缀表，背包侧 <c>inventory[i].*</c> 同样不给（名字由游戏拿
        /// 模板 id 自己查表），两侧口径保持一致，详见 ADR-0063"备选方案"一节。
        /// </para>
        /// </summary>
        private ExprValue? ResolveEquipment(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            if (remaining.Count >= 3)
            {
                var suffix = remaining[remaining.Count - 1];
                if (!suffix.Index.HasValue && (suffix.Name == "template" || suffix.Name == "instance"))
                {
                    var idSegs = remaining.Skip(1).Take(remaining.Count - 2);
                    if (!UnitSubQueries.TryBuildId(idSegs, out var subSlot))
                    {
                        diagnostics.Warn($"UI 路径 \"{fullPath}\" 的装备槽片段不是合法 Id");
                        return null;
                    }

                    if (suffix.Name == "template")
                    {
                        var templateId = _equipment.GetEquippedTemplateId(_playerId, subSlot);
                        return templateId.HasValue ? ExprValue.OfId(templateId.Value) : (ExprValue?)null;
                    }

                    var equippedSub = _equipment.GetEquipped(_playerId, subSlot);
                    return equippedSub.HasValue ? ExprValue.OfId(equippedSub.Value.InstanceId) : (ExprValue?)null;
                }
            }

            if (!UnitSubQueries.TryBuildId(remaining.Skip(1), out var slot))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的装备槽片段不是合法 Id");
                return null;
            }

            var equipped = _equipment.GetEquipped(_playerId, slot);
            return equipped.HasValue ? ExprValue.OfId(equipped.Value.InstanceId) : (ExprValue?)null;
        }

        private ExprValue? ResolveQuest(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            if (remaining.Count < 3)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 quest 子路径段数不足（需要 quest.<id...>.state|objective[i]）");
                return null;
            }

            var suffix = remaining[remaining.Count - 1];
            var idSegs = remaining.Skip(1).Take(remaining.Count - 2);
            if (!UnitSubQueries.TryBuildId(idSegs, out var questId))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的任务片段不是合法 Id");
                return null;
            }

            if (suffix.Name == "state" && !suffix.Index.HasValue)
            {
                return ExprValue.OfString(_quest.GetState(_playerId, questId).ToString());
            }

            if (suffix.Name == "objective" && suffix.Index.HasValue)
            {
                var entry = _quest.GetLog(_playerId).FirstOrDefault(p => p.QuestId.Equals(questId));
                if (entry == null)
                {
                    return null;
                }

                var idx = suffix.Index.Value;
                if (idx < 0 || idx >= entry.ObjectiveCounts.Count)
                {
                    return null;
                }

                return ExprValue.OfInt(entry.ObjectiveCounts[idx]);
            }

            diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 quest 子路径末段必须是 state 或 objective[i]，实际 \"{suffix}\"");
            return null;
        }

        private ExprValue? ResolveCurrency(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            if (!UnitSubQueries.TryBuildId(remaining.Skip(1), out var currencyId))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的货币片段不是合法 Id");
                return null;
            }

            return ExprValue.OfInt(_economy.GetBalance(_playerId, currencyId));
        }

        private ExprValue? ResolveSkillsIndex(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            var head = remaining[0];
            if (remaining.Count != 1 || !head.Index.HasValue)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 skills 子路径必须形如 \"skills[i]\"");
                return null;
            }

            var known = _skillBook.GetKnownSkills(_playerId);
            var idx = head.Index.Value;
            return idx >= 0 && idx < known.Count ? ExprValue.OfId(known[idx]) : (ExprValue?)null;
        }

        private ExprValue? ResolveSkillCooldown(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
            if (remaining.Count < 3)
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 skill 子路径段数不足（需要 skill.<id...>.cooldown）");
                return null;
            }

            var suffix = remaining[remaining.Count - 1];
            if (suffix.Index.HasValue || suffix.Name != "cooldown")
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的 skill 子路径末段必须是 cooldown，实际 \"{suffix}\"");
                return null;
            }

            var idSegs = remaining.Skip(1).Take(remaining.Count - 2);
            if (!UnitSubQueries.TryBuildId(idSegs, out var skillId))
            {
                diagnostics.Warn($"UI 路径 \"{fullPath}\" 的技能片段不是合法 Id");
                return null;
            }

            return ExprValue.OfNumber(_skillBook.GetCooldown(_playerId, skillId));
        }
    }
}
