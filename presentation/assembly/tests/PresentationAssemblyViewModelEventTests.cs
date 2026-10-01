using System;
using System.Linq;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.SaveSystem;
using Core.Rules.Common;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 测试覆盖剩余项 T-M38（装配级补充）：UI 视图模型<b>脱离 Fake 宿主</b>、由真实宿主（真实事件总线、真实
    /// <c>PowerHost</c>/<c>InventoryHost</c>/<c>EquipmentHost</c>/<c>StatHost</c>/<c>AppStateHost</c>/<c>SaveSystem</c>）
    /// 驱动的事件刷新——全程不手动调用 <c>Refresh()</c>，只推进总线派发。期望值一律由宿主自己的查询结果算出。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private static readonly Id ViewModelEventSource = new Id("unit.sample_vm_event_source");

        [Fact]
        public void RealHost_PowerChange_RefreshesHudPowerBar_WithoutManualRefresh()
        {
            var presentation = Build(out var gameplay, out _, out _, out var bus);
            var playerId = presentation.Hud.PlayerId;
            var powers = gameplay.Carriers.Rules.Powers;
            var before = presentation.Hud.PowerBars[WellKnownPowers.Health];
            Assert.Equal(powers.GetPower(playerId, WellKnownPowers.Health), before.Current);
            var delta = -Math.Max(1.0, Math.Floor(before.Current / 2)); // 规则：扣掉当前值的一半（至少 1）

            powers.ModifyPower(playerId, WellKnownPowers.Health, delta, ViewModelEventSource);
            bus.DispatchPending();

            var after = presentation.Hud.PowerBars[WellKnownPowers.Health];
            Assert.Equal(powers.GetPower(playerId, WellKnownPowers.Health), after.Current);
            Assert.NotEqual(before.Current, after.Current); // 区分度：确实变了
            Assert.Equal(powers.GetPowerMax(playerId, WellKnownPowers.Health), after.Max);
        }

        [Fact]
        public void RealHost_ItemAdded_RefreshesInventorySlots_WithoutManualRefresh()
        {
            var presentation = Build(out var gameplay, out _, out _, out var bus);
            var playerId = presentation.Hud.PlayerId;
            gameplay.Carriers.Inventory.RegisterUnit(playerId);
            Assert.Empty(presentation.Inventory.Slots);

            Assert.True(gameplay.Carriers.Inventory.AddItem(
                playerId, new Id("item.sample_sword"), 1, new Id("item.quality.sample_common"), null));
            bus.DispatchPending();

            var hostItems = gameplay.Carriers.Inventory.ListItems(playerId);
            Assert.Equal(hostItems.Count, presentation.Inventory.Slots.Count);
            Assert.Equal(hostItems[0].InstanceId, presentation.Inventory.Slots[0].InstanceId);
            Assert.Equal(hostItems[0].TemplateId, presentation.Inventory.Slots[0].TemplateId);
            Assert.Equal(hostItems[0].Count, presentation.Inventory.Slots[0].Count);
        }

        [Fact]
        public void RealHost_ItemRemoved_ShrinksInventorySlots_WithoutManualRefresh()
        {
            var presentation = Build(out var gameplay, out _, out _, out var bus);
            var playerId = presentation.Hud.PlayerId;
            gameplay.Carriers.Inventory.RegisterUnit(playerId);
            Assert.True(gameplay.Carriers.Inventory.AddItem(
                playerId, new Id("item.sample_sword"), 1, new Id("item.quality.sample_common"), null));
            bus.DispatchPending();
            Assert.NotEmpty(presentation.Inventory.Slots);
            var instanceId = gameplay.Carriers.Inventory.ListItems(playerId)[0].InstanceId;

            Assert.True(gameplay.Carriers.Inventory.RemoveItem(playerId, instanceId, 1));
            bus.DispatchPending();

            Assert.Equal(gameplay.Carriers.Inventory.ListItems(playerId).Count, presentation.Inventory.Slots.Count);
            Assert.Empty(presentation.Inventory.Slots);
        }

        [Fact]
        public void RealHost_EquipAndUnequip_RefreshEquippedSlots_WithoutManualRefresh()
        {
            var options = new PresentationAssemblyOptions { EquipmentSlotIds = new[] { OptionsMainHandSlot } };
            var presentation = Build(out var gameplay, out _, out _, out var bus, options);
            var playerId = presentation.Hud.PlayerId;

            var instanceId = EquipSampleSwordOnPlayer(gameplay);
            bus.DispatchPending();

            Assert.Equal(instanceId, presentation.Inventory.EquippedSlots[OptionsMainHandSlot]);
            Assert.Equal(new Id("item.sample_sword"), presentation.Inventory.EquippedSlotIdentities[OptionsMainHandSlot].TemplateId);
            Assert.Equal(instanceId, presentation.Inventory.EquippedSlotIdentities[OptionsMainHandSlot].InstanceId);

            Assert.NotNull(gameplay.Carriers.Equipment.Unequip(playerId, OptionsMainHandSlot));
            bus.DispatchPending();

            Assert.Empty(presentation.Inventory.EquippedSlots);
            Assert.Empty(presentation.Inventory.EquippedSlotIdentities);
        }

        [Fact]
        public void RealHost_StatChange_RefreshesCharacterStatEntries_WithoutManualRefresh()
        {
            var statId = new Id("stat.max_health");
            var options = new PresentationAssemblyOptions
            {
                CharacterStatConfig = new[] { (statId, new Id("l10n.stat.sample_max_health.name")) },
            };
            var presentation = Build(out var gameplay, out _, out _, out var bus, options);
            var playerId = presentation.CharacterStats.PlayerId;
            var stats = gameplay.Carriers.Rules.Stats;
            var before = presentation.CharacterStats.Entries[0].Value;
            Assert.Equal(stats.GetStat(playerId, statId), before);

            stats.SetBase(playerId, statId, stats.GetBase(playerId, statId) + 25);
            bus.DispatchPending();

            Assert.NotEqual(before, presentation.CharacterStats.Entries[0].Value);
            Assert.Equal(stats.GetStat(playerId, statId), presentation.CharacterStats.Entries[0].Value);
        }

        [Fact]
        public void RealHost_AppStateTransition_RefreshesPauseMenuState_WithoutManualRefresh()
        {
            var presentation = Build(out var gameplay, out _, out _, out var bus);
            var appState = gameplay.AppState;
            Assert.Equal(appState.GetState(), presentation.PauseMenu.CurrentState);

            // 沿合法转移链走到 InWorld，再暂停：每一步 PauseMenu 都应跟上宿主的当前状态。
            foreach (var next in new[] { AppState.MainMenu, AppState.Loading, AppState.InWorld, AppState.Pause })
            {
                if (appState.GetState() == next)
                {
                    continue;
                }

                Assert.True(appState.RequestTransition(next), $"转移到 {next} 被拒");
                bus.DispatchPending();
                Assert.Equal(appState.GetState(), presentation.PauseMenu.CurrentState);
            }

            Assert.True(presentation.PauseMenu.IsPaused);
        }

        [Fact]
        public void RealHost_SaveCompleted_RefreshesSaveSlots_WithoutManualRefresh()
        {
            var presentation = Build(out _, out _, out _, out var bus);
            var slot = new Id("save.slot_vm_event");
            Assert.DoesNotContain(presentation.SaveSlots.Slots, s => s.SlotId.Equals(slot));

            presentation.SaveSystem.Save(new SaveRequest(slot, "2026-10-01T00:00:00Z"));
            bus.DispatchPending();

            Assert.Contains(presentation.SaveSlots.Slots, s => s.SlotId.Equals(slot));
            Assert.Equal(
                presentation.SaveSystem.ListSlots().Select(s => s.SlotId.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray(),
                presentation.SaveSlots.Slots.Select(s => s.SlotId.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray());
        }
    }
}
