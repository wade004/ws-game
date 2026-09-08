using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// CR150-02 根治（architecture/落地计划/audit-3224ca1-20260908，P2）收边：<see
    /// cref="GameObjectFactory.Spawn"/> 未显式传入 <c>originKey</c> 时自动合成的"地图 + 位置 +
    /// 模板"稳定键（<see cref="GameObjectEntity.OriginKey"/>）必须落在 <see cref="Id"/> 允许的字符
    /// 集内（<c>^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$</c>，不含负号、大写字母）——坐标编码若直接用往返
    /// 精度格式化（<c>"G17"</c>/<c>"R"</c>），负坐标带的负号、极端量级触发的大写科学计数法
    /// <c>E</c> 都会让合成出的字符串在 <see cref="Id"/> 构造期直接抛 <c>ArgumentException</c>，
    /// 而现有的两个自动化验收场景恰好都只用了 <c>(0, 0)</c>/正坐标，不会触发这个缺口。本文件专门
    /// 覆盖负坐标、含小数的坐标，并核对同一摆放位置两次生成得到相同的 <c>OriginKey</c>（是"稳定
    /// 键"这一整个根治的前提）、不同摆放位置得到不同的 <c>OriginKey</c>。
    /// </summary>
    public sealed class GameObjectFactoryOriginKeyTests
    {
        private static readonly Id MapId = new Id("map.gobj_origin_key");
        private static readonly Id TemplateId = new Id("gobj.gobj_origin_key_template");

        private static GameObjectFactory BuildFactory(out WorldSim world)
        {
            var bus = GobjWorldBuilder.CreateBus();
            world = new WorldSim(bus);
            return new GameObjectFactory(world);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-5.5, 3)]
        [InlineData(12.25, -0.001)]
        [InlineData(-1000000.123456, -999999.654321)]
        [InlineData(1e12, -1e-9)]
        public void Spawn_AtAnyFinitePosition_DoesNotThrow_AndOriginKeyIsValidId(double x, double y)
        {
            var factory = BuildFactory(out var world);

            var entityId = factory.Spawn(TemplateId, MapId, new Vec2(x, y), 0);

            var entity = Assert.IsType<GameObjectEntity>(world.GetEntity(entityId));
            Assert.True(entity.OriginKey.HasValue);
            // OriginKey 本身已经是一个合法构造出来的 Id（构造期校验过格式），这里额外用
            // Id.IsValidFormat 复核一遍字符串本身，确保没有绕过校验的中间状态。
            Assert.True(Id.IsValidFormat(entity.OriginKey!.Value.Value));
        }

        [Fact]
        public void Spawn_SamePositionTwice_ProducesSameOriginKey_DifferentPositionProducesDifferentKey()
        {
            var factory = BuildFactory(out var world);

            var first = factory.Spawn(TemplateId, MapId, new Vec2(-3.5, 7), 0);
            var second = factory.Spawn(TemplateId, MapId, new Vec2(-3.5, 7), 0);
            var third = factory.Spawn(TemplateId, MapId, new Vec2(-3.5, 7.0001), 0);

            var firstEntity = Assert.IsType<GameObjectEntity>(world.GetEntity(first));
            var secondEntity = Assert.IsType<GameObjectEntity>(world.GetEntity(second));
            var thirdEntity = Assert.IsType<GameObjectEntity>(world.GetEntity(third));

            Assert.NotEqual(first, second); // 两次 Spawn 拿到不同的运行期实体 id（本身不是本测试重点）。
            Assert.Equal(firstEntity.OriginKey, secondEntity.OriginKey); // 但稳定键相同——同一摆放位置。
            Assert.NotEqual(firstEntity.OriginKey, thirdEntity.OriginKey); // 位置不同，稳定键也不同。
        }
    }
}
