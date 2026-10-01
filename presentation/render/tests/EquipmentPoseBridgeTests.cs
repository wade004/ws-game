using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Presentation.Render;
using Xunit;

namespace Tests.PresentationRender
{
    /// <summary>
    /// 换装链的姿势侧（手感设计/08 第 1 节）：<c>feel.weapon_changed</c> 经 <see cref="EquipmentPoseBridge"/> 进入
    /// <see cref="PoseSelector"/>，换装后 idle/attack 的姿势键随武器族变化；空手清除武器族；只认事件携带的族。
    /// </summary>
    public sealed class EquipmentPoseBridgeTests
    {
        private static readonly Id Hero = new Id("unit.bridge_hero");
        private static readonly Id Other = new Id("unit.bridge_other");

        private static IEventBus CreateBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static void Publish(IEventBus bus, Id unit, string? family)
        {
            bus.Enqueue(new FeelWeaponChangedEvent(unit, null, family == null ? null : "feel.weapon.x", null, null, family, 1));
            bus.DispatchPending();
        }

        private static string Key(PoseSelector selector, Id unit, string state, HashSet<string> clips)
        {
            var resolution = PoseResolver.Resolve(selector.GetContext(unit).ToRequest(state, false), k => clips.Contains(k));
            return resolution.Found ? resolution.TableKey : string.Empty;
        }

        [Fact]
        public void WeaponChanged_SetsFamily_AndPoseKeysFollow()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            using var bridge = new EquipmentPoseBridge(bus, selector);
            var clips = new HashSet<string>(StringComparer.Ordinal) { "idle", "idle.1h", "idle.2h", "attack", "attack.1h", "attack.2h" };

            Publish(bus, Hero, "2h");
            Assert.Equal("2h", selector.GetContext(Hero).Family);
            Assert.Equal("idle.2h", Key(selector, Hero, "idle", clips));
            Assert.Equal("attack.2h", Key(selector, Hero, "attack", clips));

            Publish(bus, Hero, "1h");
            Assert.Equal("idle.1h", Key(selector, Hero, "idle", clips));

            // 卸下武器：事件携带 null 族，姿势回落到不带族的基础键。
            Publish(bus, Hero, null);
            Assert.Null(selector.GetContext(Hero).Family);
            Assert.Equal("idle", Key(selector, Hero, "idle", clips));
        }

        [Fact]
        public void WeaponChanged_OnlyAffectsTheEventUnit()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            using var bridge = new EquipmentPoseBridge(bus, selector);

            Publish(bus, Hero, "2h");
            Assert.Equal("2h", selector.GetContext(Hero).Family);
            Assert.Null(selector.GetContext(Other).Family);
        }

        [Fact]
        public void WeaponChanged_RaisesContextChanged_OnlyWhenFamilyActuallyChanges()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var changes = 0;
            selector.ContextChanged += _ => changes++;
            using var bridge = new EquipmentPoseBridge(bus, selector);

            Publish(bus, Hero, "2h");
            Publish(bus, Hero, "2h");
            Publish(bus, Hero, "1h");

            Assert.Equal(2, changes);
        }

        [Fact]
        public void Dispose_StopsListening()
        {
            var bus = CreateBus();
            var selector = new PoseSelector();
            var bridge = new EquipmentPoseBridge(bus, selector);
            bridge.Dispose();

            Publish(bus, Hero, "2h");
            Assert.Null(selector.GetContext(Hero).Family);
        }
    }
}
