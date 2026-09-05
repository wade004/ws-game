using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>缺口 4：<c>player.skill_bindings</c> 段（<see cref="SkillBindingPersistable"/>）的
    /// SectionKey/存读档往返。</summary>
    public sealed class SkillBindingPersistableTests
    {
        private static readonly Id MapId = new Id("map.town_square");
        private static readonly Id FactionId = new Id("fac.player");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id SkillFireball = new Id("skill.fireball");
        private static readonly Id SkillHeal = new Id("skill.heal");

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static PlayerUnit NewPlayer() => new PlayerUnit(new Id("unit.hero"), MapId, FactionId, ArchetypeId);

        [Fact]
        public void SectionKey_MatchesSaveSections()
        {
            var host = new SkillBindingHost(NewBus(), (_, __) => true);
            var persistable = SkillBindingPersistable.For(host, NewPlayer());

            Assert.Equal(SaveSections.PlayerSkillBindings, persistable.SectionKey);
        }

        [Fact]
        public void Save_Empty_ProducesEmptyObject()
        {
            var host = new SkillBindingHost(NewBus(), (_, __) => true);
            var player = NewPlayer();
            var persistable = SkillBindingPersistable.For(host, player);

            var saved = persistable.Save();

            var obj = Assert.IsType<JsonObject>(saved);
            Assert.Empty(obj);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsAllSlots_IntoFreshHost()
        {
            var savingHost = new SkillBindingHost(NewBus(), (_, __) => true);
            var player = NewPlayer();
            savingHost.Bind(player.EntityId, "slot_0", SkillFireball);
            savingHost.Bind(player.EntityId, "slot_1", SkillHeal);

            var saved = SkillBindingPersistable.For(savingHost, player).Save();

            // 读档一侧：全新 host，已知技能查询恒返回 false（模拟"读档时刻已知技能集合为空"，见
            // SkillBindingPersistable 判断记录——Load 不经 Bind 校验，理应不受影响）。
            var loadingHost = new SkillBindingHost(NewBus(), (_, __) => false);
            SkillBindingPersistable.For(loadingHost, player).Load(saved);

            var restored = loadingHost.GetBindings(player.EntityId);
            Assert.Equal(2, restored.Count);
            Assert.Equal(SkillFireball, restored["slot_0"]);
            Assert.Equal(SkillHeal, restored["slot_1"]);
        }

        [Fact]
        public void Load_NullData_LeavesEmpty()
        {
            var host = new SkillBindingHost(NewBus(), (_, __) => true);
            var player = NewPlayer();

            SkillBindingPersistable.For(host, player).Load(JsonNull.Instance);

            Assert.Empty(host.GetBindings(player.EntityId));
        }

        [Fact]
        public void Load_RejectsWrongShape()
        {
            var host = new SkillBindingHost(NewBus(), (_, __) => true);
            var persistable = SkillBindingPersistable.For(host, NewPlayer());

            Assert.Throws<System.FormatException>(() => persistable.Load(new JsonString("not-an-object")));
        }

        [Fact]
        public void Load_RejectsNonIdSkillValue()
        {
            var host = new SkillBindingHost(NewBus(), (_, __) => true);
            var persistable = SkillBindingPersistable.For(host, NewPlayer());
            var badData = new JsonObjectBuilder().Add("slot_0", new JsonString("not an id")).Build();

            Assert.Throws<System.FormatException>(() => persistable.Load(badData));
        }
    }
}
