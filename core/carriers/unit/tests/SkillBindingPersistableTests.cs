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

            // 读档一侧：全新 host，已知技能查询恒返回 true（模拟 player.known_skills 段已先于本段
            // 还原完毕，见 SkillBindingPersistable 判断记录——Load 现改回经 Bind 的已知技能校验，
            // KnownSkillsPersistable 在 SaveSections.KnownOrder 里排在本段之前保证这一前提成立）。
            var loadingHost = new SkillBindingHost(NewBus(), (_, __) => true);
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

        /// <summary>
        /// AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：修复前
        /// <c>Load</c> 对 <c>JsonNull</c> 直接 no-op 返回，运行期已有的绑定原样保留（同一宿主先后
        /// 加载两个存档槽时，缺本段的旧档不会清掉前一个槽留下的绑定）。与上面
        /// <see cref="Load_NullData_LeavesEmpty"/>（本就是空的，无法区分"清空"与"no-op"）互补：
        /// 先绑定一个槽位，再 <c>Load(JsonNull)</c>，断言真正被解绑。
        /// </summary>
        [Fact]
        public void Load_NullData_ClearsPreExistingBindings()
        {
            var host = new SkillBindingHost(NewBus(), (_, __) => true);
            var player = NewPlayer();
            host.Bind(player.EntityId, "slot_0", SkillFireball);
            Assert.NotEmpty(host.GetBindings(player.EntityId));

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

        [Fact]
        public void Load_SkillNoLongerKnown_SkipsSlot_DoesNotThrow()
        {
            var savingHost = new SkillBindingHost(NewBus(), (_, __) => true);
            var player = NewPlayer();
            savingHost.Bind(player.EntityId, "slot_0", SkillFireball);
            var saved = SkillBindingPersistable.For(savingHost, player).Save();

            // 判断记录（G1 遗留恢复）：player.known_skills 段还原后不含 SkillFireball（例如内容更新
            // 移除了该技能）——Load 改回经 Bind 校验后，该槽位被静默拒绝、不写入，不抛异常中断读档。
            var loadingHost = new SkillBindingHost(NewBus(), (_, __) => false);
            SkillBindingPersistable.For(loadingHost, player).Load(saved);

            Assert.Empty(loadingHost.GetBindings(player.EntityId));
        }
    }
}
