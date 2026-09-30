using System;
using Core.Foundation.AppLifecycle;
using Xunit;

namespace Tests.Foundation.AppLifecycle
{
    /// <summary>
    /// ADR-0125 第三批探针缺陷：<see cref="AppStateMachineConfig.FromDefinitions"/> 的 <c>kind: main</c> 行用
    /// <c>Enum.TryParse</c> 解析 <c>from</c>/<c>to</c>，数字串（含未定义的数字）会被当作 <see cref="AppState"/>
    /// 写进转移表。期望：只接受枚举名，其余抛 <see cref="ArgumentException"/>（与"不是合法枚举名"同一口径）。
    /// </summary>
    public sealed class ADR0125_AppStateNameParsingTests
    {
        private static GameStateTransitionDefinition MainRow(string from, string to) =>
            new GameStateTransitionDefinition("found.state.adr0125", from, to, GameStateTransitionKind.Main);

        public static System.Collections.Generic.IEnumerable<object[]> NonNameStates()
        {
            var all = (AppState[])Enum.GetValues(typeof(AppState));
            var max = 0;
            foreach (var v in all) { max = Math.Max(max, (int)v); }
            yield return new object[] { (max + 1).ToString() };   // 未定义的数字
            yield return new object[] { "-1" };
            foreach (var v in all) { yield return new object[] { ((int)v).ToString() }; } // 已定义值的数字串
        }

        [Theory]
        [MemberData(nameof(NonNameStates))]
        public void FromDefinitions_MainRowWithNonNameState_ThrowsArgumentException(string state)
        {
            var valid = Enum.GetNames(typeof(AppState))[0];

            Assert.Throws<ArgumentException>(() => AppStateMachineConfig.FromDefinitions(new[] { MainRow(state, valid) }));
            Assert.Throws<ArgumentException>(() => AppStateMachineConfig.FromDefinitions(new[] { MainRow(valid, state) }));
        }

        [Fact]
        public void FromDefinitions_MainRowWithEveryDefinedName_IsAccepted()
        {
            var names = Enum.GetNames(typeof(AppState));

            var config = AppStateMachineConfig.FromDefinitions(new[] { MainRow(names[0], names[1]) });

            Assert.True(config.IsTransitionAllowed(
                (AppState)Enum.Parse(typeof(AppState), names[0]), (AppState)Enum.Parse(typeof(AppState), names[1])));
        }
    }
}
