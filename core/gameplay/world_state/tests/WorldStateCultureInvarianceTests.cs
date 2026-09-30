using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Tests.Gameplay.Culture;
using Xunit;

namespace Tests.Gameplay.WorldState
{
    /// <summary>
    /// 测试覆盖梳理 T-H13：<c>WorldState</c> 存档段（<c>Save</c>/<c>Load</c>）在非不变文化（de-DE 小数逗号、
    /// tr-TR、sv-SE 负号 U+2212）下写出的 JSON 文本必须与不变文化下逐字节相同，读回后各 flag 的值与类型
    /// （Int/Number/String/Id/Bool）不变。
    /// </summary>
    public sealed class WorldStateCultureInvarianceTests
    {
        private static readonly Id Writer = new Id("quest.culture_writer");

        private static readonly (Id Key, ExprValue Value)[] Flags =
        {
            (new Id("world.culture.flag_bool"), ExprValue.OfBool(true)),
            (new Id("world.culture.flag_int"), ExprValue.OfInt(-1234567)),
            (new Id("world.culture.flag_big_int"), ExprValue.OfInt(9007199254740993L)),
            (new Id("world.culture.flag_number"), ExprValue.OfNumber(-1234.5)),
            (new Id("world.culture.flag_number_whole"), ExprValue.OfNumber(3.0)),
            (new Id("world.culture.flag_number_tiny"), ExprValue.OfNumber(1e-7)),
            (new Id("world.culture.flag_string"), ExprValue.OfString("Iiİı 0.5")),
            (new Id("world.culture.flag_id"), ExprValue.OfId(new Id("quest.culture_target"))),
        };

        private static Core.Gameplay.WorldState.WorldState BuildState()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            foreach (var (key, value) in Flags)
            {
                state.Set(key, value, Writer);
            }

            return state;
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void Save_Text_IsByteIdenticalUnderNonInvariantCulture(string culture)
        {
            var baseline = CultureScope.Run("", () => JsonWriter.Write(BuildState().Save()));
            var actual = CultureScope.Run(culture, () => JsonWriter.Write(BuildState().Save()));

            Assert.Equal(baseline, actual);
            Assert.DoesNotContain("-1234,5", actual);
            Assert.Contains("-1234.5", baseline);
        }

        [Theory]
        [MemberData(nameof(CultureScope.NonInvariantCultures), MemberType = typeof(CultureScope))]
        public void SaveLoadRoundTrip_RestoresEveryFlagExactlyUnderNonInvariantCulture(string culture)
        {
            var baselineText = CultureScope.Run("", () => JsonWriter.Write(BuildState().Save()));

            using (CultureScope.Enter(culture))
            {
                var restored = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
                restored.Load(JsonReader.Parse(baselineText));

                Assert.Equal(Flags.Length, restored.Count);
                foreach (var (key, value) in Flags)
                {
                    var got = restored.Get(key);
                    Assert.Equal(value, got);
                    Assert.Equal(value.Kind, got.Kind);
                }

                // 读回后再存档是不动点
                Assert.Equal(baselineText, JsonWriter.Write(restored.Save()));
            }
        }
    }
}
