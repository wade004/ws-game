using System.Globalization;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Tests.Carriers.Culture;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary>
    /// 测试覆盖梳理 T-H13：<c>GameObjectFactory</c> 未显式传入 <c>originKey</c> 时合成的"地图 + 位置 + 模板"
    /// 稳定键（<c>BuildFallbackOriginKey</c>/<c>EncodeCoordinate</c>）是存档对账用的稳定标识，必须在任何
    /// 当前文化下逐字节相同——de-DE 的小数逗号会在 <see cref="Id"/> 构造期直接抛异常，sv-SE 的 U+2212
    /// 负号同理。姊妹文件 <see cref="GameObjectFactoryOriginKeyTests"/> 覆盖不变文化下的格式与稳定性。
    /// </summary>
    public sealed class GameObjectFactoryOriginKeyCultureInvarianceTests
    {
        private static readonly Id MapId = new Id("map.gobj_origin_key_culture");
        private static readonly Id TemplateId = new Id("gobj.gobj_origin_key_culture_template");

        // 规则：符号前缀 n/p + 绝对值 F6（不变文化）且小数点换成下划线，见 GameObjectFactory.EncodeCoordinate 注释。
        private static string ExpectedCoordinate(double value) =>
            (value < 0 ? "n" : "p") + System.Math.Abs(value).ToString("F6", CultureInfo.InvariantCulture).Replace('.', '_');

        private static string ExpectedKey(Vec2 p) =>
            $"gobj.origin.{MapId.Value}.{TemplateId.Value}.{ExpectedCoordinate(p.X)}_{ExpectedCoordinate(p.Y)}";

        private static string SpawnAndReadOriginKey(Vec2 position)
        {
            var bus = GobjWorldBuilder.CreateBus();
            var world = new WorldSim(bus);
            var factory = new GameObjectFactory(world);

            var entityId = factory.Spawn(TemplateId, MapId, position, 0);

            var entity = Assert.IsType<GameObjectEntity>(world.GetEntity(entityId));
            Assert.True(entity.OriginKey.HasValue);
            return entity.OriginKey!.Value.Value;
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void Spawn_FallbackOriginKey_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var positions = new[]
            {
                new Vec2(0, 0),
                new Vec2(-5.5, 3),
                new Vec2(12.25, -0.001),
                new Vec2(-1000000.123456, -999999.654321),
                new Vec2(1e12, -1e-9),
            };

            foreach (var position in positions)
            {
                var baseline = CultureScope.Run("", () => SpawnAndReadOriginKey(position));
                var actual = CultureScope.Run(culture, () => SpawnAndReadOriginKey(position));

                Assert.Equal(ExpectedKey(position), baseline);
                Assert.Equal(baseline, actual);
            }
        }
    }
}
