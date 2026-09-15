using System;
using System.Collections.Generic;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    /// <summary>T-N3-1 验收标准"集合常量单测 1"：<see cref="SettlementEffectKinds.All"/> 与
    /// <see cref="EffectKindNames"/> 登记的全集之间的对应项一一匹配、无未知项、只读不可变。</summary>
    public sealed class SettlementEffectKindsTests
    {
        /// <summary>06 第 3.2 节 2026-09-14 修订段原文集合，按表格行序：school_damage、
        /// weapon_damage_pct、heal、projectile、apply_aura（apply_aura 一项见类型判断记录——按
        /// 效果原语类型这一层无条件计入，不下钻具体引用的光环内容）。</summary>
        [Fact]
        public void All_MatchesExpectedSettlementKindSet_InDocumentedOrder()
        {
            var expected = new[] { "school_damage", "weapon_damage_pct", "heal", "projectile", "apply_aura" };

            Assert.Equal(expected, SettlementEffectKinds.All);
        }

        /// <summary><see cref="SettlementEffectKinds.All"/> 里的每一项都必须能在
        /// <see cref="EffectKindNames"/>（<c>core/carriers/creature/core/CreatureImmunityProvider.cs</c>
        /// 等消费方用到的效果 kind 名字集合，见该类型判断记录）里找到对应项——无未知项，不会出现
        /// 一份"结算类集合"引用了根本不存在的效果原语名字。</summary>
        [Fact]
        public void All_MembersAreAllKnownInEffectKindNames_NoUnknownItems()
        {
            foreach (var name in SettlementEffectKinds.All)
            {
                Assert.True(EffectKindNames.TryParse(name, out var kind), $"未知的 EffectKind 文本：\"{name}\"");
                Assert.Equal(name, EffectKindNames.ToText(kind));
            }
        }

        /// <summary>只读不可变：<see cref="SettlementEffectKinds.All"/> 转型为可写集合接口后调用
        /// 变更方法必须抛出，不存在任何能让调用方修改集合内容的途径。</summary>
        [Fact]
        public void All_IsReadOnly_MutationThrows()
        {
            var mutable = Assert.IsAssignableFrom<IList<string>>(SettlementEffectKinds.All);

            Assert.Throws<NotSupportedException>(() => mutable.Add("effect.made_up"));
            Assert.Throws<NotSupportedException>(() => mutable.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => mutable[0] = "effect.made_up");
        }

        [Theory]
        [InlineData("school_damage")]
        [InlineData("weapon_damage_pct")]
        [InlineData("heal")]
        [InlineData("projectile")]
        [InlineData("apply_aura")]
        public void IsSettlement_KnownSettlementKind_ReturnsTrue(string kind)
        {
            Assert.True(SettlementEffectKinds.IsSettlement(kind));
        }

        [Theory]
        [InlineData("dispel")]
        [InlineData("energize")]
        [InlineData("trigger_spell")]
        [InlineData("modify_cooldown")]
        [InlineData("add_charge")]
        [InlineData("move")]
        [InlineData("summon")]
        [InlineData("interrupt")]
        [InlineData("teleport")]
        [InlineData("open_lock")]
        [InlineData("create_item")]
        [InlineData("learn_skill")]
        [InlineData("set_world_flag")]
        [InlineData("script")]
        [InlineData("not_a_real_effect_kind")]
        public void IsSettlement_NonSettlementOrUnknownKind_ReturnsFalse(string kind)
        {
            Assert.False(SettlementEffectKinds.IsSettlement(kind));
        }

        [Fact]
        public void IsSettlement_Null_ReturnsFalse()
        {
            Assert.False(SettlementEffectKinds.IsSettlement(null));
        }
    }
}
