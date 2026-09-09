using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary><see cref="GobjEffectExtension"/>：<c>open_lock</c> 效果原语转发到
    /// <see cref="GameObjectHost.TryUnlock"/>（见 06 第 3.2 节 <c>open_lock</c>"对目标 GameObject
    /// 尝试开锁"、07 第 3.6 节"供未直接经 interact 触发的开锁场景（如 open_lock 效果）复用"）。</summary>
    public sealed class GobjEffectExtensionTests
    {
        private static readonly Id MapId = new Id("map.sample");
        private static readonly Id DisplayRef = new Id("display.sample_gobj");
        private static readonly Id Unit = new Id("unit.sample_1");
        private static readonly Id School = new Id("school.sample");
        private static readonly Id SkillId = new Id("skill.sample_open_lock");

        private static Core.Foundation.Common.Json.JsonObject Door(string id, string lockId) => J.O(
            ("id", J.S(id)),
            ("name_key", J.S("l10n." + id.Replace('.', '_') + ".name")),
            ("kind", J.S("door")),
            ("type_data", J.O()),
            ("lock_id", J.S(lockId)),
            ("display_ref", J.S(DisplayRef.Value)));

        [Fact]
        public void TryHandle_OpenLock_SucceedsAndUnlocksWhenRequirementMet()
        {
            const string lockId = "gobj.lock.sample_effect_ok";
            var itemId = new Id("item.sample_effect_key");
            var world = new GobjWorldBuilder()
                .Item(itemId.Value)
                .Lock(J.O(("id", J.S(lockId)), ("requirement", J.O(("kind", J.S("item_key")), ("item_id", J.S(itemId.Value))))))
                .Template(Door("gobj.sample_effect_door_a", lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            world.Inventory.AddItem(Unit, itemId, 1);
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_effect_door_a"), MapId, new Vec2(0, 0));

            var extension = new GobjEffectExtension(world.Host);
            var context = new EffectContext(Unit, gobjId, SkillId, EffectKind.OpenLock, School, 0, 0);

            var handled = extension.TryHandle(context, out var result);
            Assert.True(handled);
            Assert.Equal(HitResult.Hit, result.Hit);
            Assert.True(world.Host.TryUnlock(Unit, gobjId));
        }

        [Fact]
        public void TryHandle_OpenLock_FailsWhenRequirementNotMet()
        {
            const string lockId = "gobj.lock.sample_effect_bad";
            var itemId = new Id("item.sample_effect_key2");
            var world = new GobjWorldBuilder()
                .Item(itemId.Value)
                .Lock(J.O(("id", J.S(lockId)), ("requirement", J.O(("kind", J.S("item_key")), ("item_id", J.S(itemId.Value))))))
                .Template(Door("gobj.sample_effect_door_b", lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_effect_door_b"), MapId, new Vec2(0, 0));

            var extension = new GobjEffectExtension(world.Host);
            var context = new EffectContext(Unit, gobjId, SkillId, EffectKind.OpenLock, School, 0, 0);

            var handled = extension.TryHandle(context, out var result);
            Assert.True(handled);
            Assert.Equal(HitResult.Miss, result.Hit);
        }

        [Fact]
        public void TryHandle_NonOpenLockKind_ReturnsFalse()
        {
            var world = new GobjWorldBuilder().Build();
            var extension = new GobjEffectExtension(world.Host);
            var context = new EffectContext(Unit, new Id("gobj.inst_1"), SkillId, EffectKind.CreateItem, School, 0, 0);

            var handled = extension.TryHandle(context, out _);
            Assert.False(handled);
        }
    }
}
