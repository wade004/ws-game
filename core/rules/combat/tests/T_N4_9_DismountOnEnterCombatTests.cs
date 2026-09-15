using System.Collections.Generic;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// T-N4-9（[ADR-0034](../../../../architecture/adr/0034-单一货币与价格挂物品等级.md) 决策 7；
    /// 数值设计分阶段落地计划拍板 9"'进入战斗时移除坐骑光环'归 CombatOptions"）：
    /// <see cref="Core.Rules.Combat.CombatOptions.DismountOnEnterCombat"/> 策略项——开启时进入战斗
    /// （<c>不在战 -&gt; 在战</c> 的那一次转换）应移除坐骑光环，关闭时不应移除；三项接线（开关本身、
    /// <c>MountAuraDispelType</c>、<c>DismountMountAuras</c> 委托）任一缺失都不产生实际移除效果。
    /// </summary>
    public sealed class T_N4_9_DismountOnEnterCombatTests
    {
        private static readonly Id Hero = new Id("unit.dismount_hero");
        private static readonly Id Dummy = new Id("unit.dismount_dummy");
        private static readonly Id MountDispelType = new Id("dispel.mount");

        [Fact]
        public void DismountOnEnterCombat_True_RemovesMountAurasOnFirstEnterCombat()
        {
            var dismounted = new List<(Id UnitId, Id DispelType)>();
            var fx = CombatTestSupport.Build(o =>
            {
                o.DismountOnEnterCombat = true;
                o.MountAuraDispelType = MountDispelType;
                o.DismountMountAuras = (unitId, dispelType) => dismounted.Add((unitId, dispelType));
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            fx.Host.NotifyCombatEvent(Hero, Dummy);

            var call = Assert.Single(dismounted);
            Assert.Equal(Hero, call.UnitId);
            Assert.Equal(MountDispelType, call.DispelType);
        }

        [Fact]
        public void DismountOnEnterCombat_False_DoesNotRemoveMountAuras()
        {
            var dismounted = new List<(Id UnitId, Id DispelType)>();
            var fx = CombatTestSupport.Build(o =>
            {
                o.DismountOnEnterCombat = false;
                o.MountAuraDispelType = MountDispelType;
                o.DismountMountAuras = (unitId, dispelType) => dismounted.Add((unitId, dispelType));
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            fx.Host.NotifyCombatEvent(Hero, Dummy);

            Assert.Empty(dismounted);
        }

        [Fact]
        public void DismountOnEnterCombat_True_ButMountAuraDispelTypeNotConfigured_DoesNotRemoveAnything()
        {
            var dismounted = new List<(Id UnitId, Id DispelType)>();
            var fx = CombatTestSupport.Build(o =>
            {
                o.DismountOnEnterCombat = true;
                // MountAuraDispelType 保持默认 null——即便开关为真也不知道移除哪些光环。
                o.DismountMountAuras = (unitId, dispelType) => dismounted.Add((unitId, dispelType));
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            fx.Host.NotifyCombatEvent(Hero, Dummy);

            Assert.Empty(dismounted);
        }

        [Fact]
        public void DismountOnEnterCombat_True_ButDelegateNotWired_DoesNotThrow()
        {
            var fx = CombatTestSupport.Build(o =>
            {
                o.DismountOnEnterCombat = true;
                o.MountAuraDispelType = MountDispelType;
                // DismountMountAuras 未接线（默认 null）——同本模块一贯的未接线降级惯例。
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);

            var ex = Record.Exception(() => fx.Host.NotifyCombatEvent(Hero, Dummy));

            Assert.Null(ex);
            Assert.True(fx.Host.IsInCombat(Hero)); // 进战本身不受影响。
        }

        [Fact]
        public void DismountOnEnterCombat_AlreadyInCombat_DoesNotReDismount()
        {
            var dismounted = new List<(Id UnitId, Id DispelType)>();
            var fx = CombatTestSupport.Build(o =>
            {
                o.DismountOnEnterCombat = true;
                o.MountAuraDispelType = MountDispelType;
                o.DismountMountAuras = (unitId, dispelType) => dismounted.Add((unitId, dispelType));
            });
            CombatTestSupport.RegisterUnit(fx, Hero, CombatTestSupport.FactionParty);
            CombatTestSupport.RegisterUnit(fx, Dummy, CombatTestSupport.FactionHorde);
            var thirdParty = new Id("unit.dismount_third");
            CombatTestSupport.RegisterUnit(fx, thirdParty, CombatTestSupport.FactionHorde);

            fx.Host.NotifyCombatEvent(Hero, Dummy);
            fx.Host.NotifyCombatEvent(Hero, thirdParty); // Hero 已在战中，这次调用应是空操作

            Assert.Single(dismounted); // 只在第一次"不在战 -> 在战"的转换上移除过一次。
        }
    }
}
