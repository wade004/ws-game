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

namespace Presentation.Ui
{
    /// <summary>
    /// <c>player.*</c> 路径的解答者（见任务书路径小语法："player.power.&lt;powerType&gt;.current|max、
    /// player.stat.&lt;statId&gt;、player.level、player.xp、player.xp_to_next、
    /// player.inventory.count、player.inventory[i].template|count|instance、
    /// player.equipment.&lt;slot&gt;、player.quest.&lt;questId&gt;.state|objective[i]、
    /// player.currency.&lt;id&gt;、player.skills[i]、player.skill.&lt;id&gt;.cooldown"）。全部查询都
    /// 绕着构造期注入的 <see cref="_playerId"/>（当前玩家单位 id）展开，本 Provider 自身不做任何
    /// 写操作（铁律 P1）。
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

        private ExprValue? ResolveEquipment(IReadOnlyList<UiPathSegment> remaining, string fullPath, IUiDiagnostics diagnostics)
        {
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
