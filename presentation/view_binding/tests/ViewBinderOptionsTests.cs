using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.PresentationViewBinding
{
    /// <summary>
    /// T-L16（测试覆盖剩余项第四批，view_binding 部分）：<see cref="ViewBinderOptions"/> 默认值与
    /// <see cref="ViewBinderOptions.DefaultForwardedEventKeys"/> 内容固定。期望来自类型注释的规则：
    /// 默认覆盖 skill / combat / aura / unit.state_changed / item / gobj 域；
    /// 不含 entity.created、entity.destroyed、unit.moved、sim.*。
    /// </summary>
    public class ViewBinderOptionsTests
    {
        private static readonly Id[] MustContain =
        {
            // skill
            RulesEventKeys.SkillCastStart, RulesEventKeys.SkillCastSuccess,
            RulesEventKeys.SkillCastFailed, RulesEventKeys.SkillCastInterrupted,
            // combat
            RulesEventKeys.CombatDamageDealt, RulesEventKeys.CombatHealDone, RulesEventKeys.CombatThreatChanged,
            RulesEventKeys.CombatEntered, RulesEventKeys.CombatLeft, RulesEventKeys.UnitDied, RulesEventKeys.UnitRespawned,
            // aura
            RulesEventKeys.AuraApplied, RulesEventKeys.AuraRemoved, RulesEventKeys.AuraStackChanged, RulesEventKeys.ProcTriggered,
            // unit
            CarriersEventKeys.UnitStateChanged,
            // item
            CarriersEventKeys.ItemAdded, CarriersEventKeys.ItemRemoved, CarriersEventKeys.ItemEquipped, CarriersEventKeys.ItemUnequipped,
            // gobj
            CarriersEventKeys.GobjInteracted, CarriersEventKeys.GobjStateChanged,
        };

        [Fact]
        public void DefaultForwardedEventKeys_ContainsExactlyTheDocumentedDomains()
        {
            var keys = new HashSet<Id>(ViewBinderOptions.DefaultForwardedEventKeys);

            foreach (var expected in MustContain)
            {
                Assert.Contains(expected, keys);
            }
            Assert.Equal(MustContain.Length, keys.Count); // 没有多余项
        }

        [Fact]
        public void DefaultForwardedEventKeys_HasNoDuplicates()
        {
            var list = ViewBinderOptions.DefaultForwardedEventKeys;

            Assert.Equal(list.Count, new HashSet<Id>(list).Count);
        }

        [Fact]
        public void DefaultForwardedEventKeys_ExcludesLifecycleMovementAndSimEvents()
        {
            var keys = new HashSet<Id>(ViewBinderOptions.DefaultForwardedEventKeys);

            Assert.DoesNotContain(SimEventKeys.EntityCreated, keys);   // 由 ViewBinder 内部专门处理
            Assert.DoesNotContain(SimEventKeys.EntityDestroyed, keys);
            Assert.DoesNotContain(CarriersEventKeys.UnitMoved, keys);  // 位置同步走 tick 快照插值
            Assert.DoesNotContain(SimEventKeys.TickFinished, keys);
            foreach (var key in keys)
            {
                Assert.False(key.Value.StartsWith("sim.", StringComparison.Ordinal), "不含 sim.* 内部节奏事件：" + key.Value);
            }
        }

        [Fact]
        public void DefaultForwardedEventKeys_IsAStableSingleInstance()
        {
            Assert.Same(ViewBinderOptions.DefaultForwardedEventKeys, ViewBinderOptions.DefaultForwardedEventKeys);
        }

        [Fact]
        public void Options_Defaults_UseDefaultKeysAndEightDirections()
        {
            var options = new ViewBinderOptions();

            Assert.Same(ViewBinderOptions.DefaultForwardedEventKeys, options.ForwardedEventKeys);
            Assert.Equal(8, options.DefaultDirectionCount);
        }

        [Fact]
        public void Options_CustomValues_AreKeptAsGiven()
        {
            var keys = new[] { RulesEventKeys.CombatDamageDealt };

            var options = new ViewBinderOptions(keys, defaultDirectionCount: 16);

            Assert.Same(keys, options.ForwardedEventKeys);
            Assert.Equal(16, options.DefaultDirectionCount);
        }

        [Fact]
        public void Options_EmptyKeyList_IsKept_NotReplacedByDefaults()
        {
            var empty = new List<Id>();

            var options = new ViewBinderOptions(empty);

            Assert.Same(empty, options.ForwardedEventKeys);
            Assert.Empty(options.ForwardedEventKeys);
        }
    }
}
