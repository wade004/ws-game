using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.WorldState;
using Xunit;

namespace Tests.Gameplay.WorldState
{
    /// <summary>
    /// <see cref="WorldExprGroupProvider"/> 的三个键（<c>get</c>/<c>has</c>/<c>get_int</c>）经真实
    /// <see cref="ExprParser"/>/<see cref="ExprEvaluator"/> 求值（任务书验收项）。解析用的
    /// <see cref="IExprSchema"/> 取 <see cref="WorldExprSchemaEntries.BuildStandalone"/>（只精确登记
    /// 本三个键，不含 <c>RulesExprSchema</c> 的"已知分组放行"分支）——见
    /// <see cref="WorldExprSchemaEntries"/> 顶部判断记录："world.get(world.bridge.repaired)" 这类把
    /// <c>world.&lt;路径&gt;</c> 标志键字面量当参数传入的写法，只有在 schema 不放行未登记 <c>world.*</c>
    /// key 时才能被正确解析成 Id 字面量，用 <c>RulesExprSchema</c> 解析会把参数误判成另一个（不存在的）
    /// world 引用。
    /// </summary>
    public class WorldExprGroupProviderTests
    {
        private static readonly Id WriterQuest = new Id("quest.deliver_letter");
        private static readonly IExprSchema Schema = WorldExprSchemaEntries.BuildStandalone();

        private static (Core.Gameplay.WorldState.WorldState state, WorldOnlyExprHost host) NewHost()
        {
            var eventBus = TestSupport.NewEventBus();
            var state = new Core.Gameplay.WorldState.WorldState(eventBus);
            var provider = new WorldExprGroupProvider(state);
            return (state, new WorldOnlyExprHost(provider));
        }

        private static ExprValue Eval(string text, IExprHost host)
        {
            var node = ExprParser.Parse(text, Schema);
            return ExprEvaluator.Evaluate(node, host, new ExprDiagnosticsRecorder());
        }

        [Fact]
        public void WorldGet_ReturnsBoolValue_WhenFlagSet()
        {
            var (state, host) = NewHost();
            state.Set(new Id("world.bridge.repaired"), ExprValue.OfBool(true), WriterQuest);

            var result = Eval("world.get(world.bridge.repaired)", host);

            Assert.Equal(ExprValue.OfBool(true), result);
        }

        [Fact]
        public void WorldGet_ReturnsFalse_WhenFlagMissing()
        {
            var (_, host) = NewHost();

            var result = Eval("world.get(world.bridge.repaired)", host);

            Assert.Equal(ExprValue.OfBool(false), result);
        }

        [Fact]
        public void WorldHas_TrueThenFalse()
        {
            var (state, host) = NewHost();

            Assert.Equal(ExprValue.OfBool(false), Eval("world.has(world.bridge.repaired)", host));

            state.Set(new Id("world.bridge.repaired"), ExprValue.OfInt(1), WriterQuest);

            Assert.Equal(ExprValue.OfBool(true), Eval("world.has(world.bridge.repaired)", host));
        }

        [Fact]
        public void WorldGetInt_ReturnsZero_WhenMissing()
        {
            var (_, host) = NewHost();

            var result = Eval("world.get_int(world.spawn.kill_count)", host);

            Assert.Equal(ExprValue.OfInt(0), result);
        }

        [Fact]
        public void WorldGetInt_ReturnsStoredIntValue()
        {
            var (state, host) = NewHost();
            state.Set(new Id("world.spawn.kill_count"), ExprValue.OfInt(5), WriterQuest);

            var result = Eval("world.get_int(world.spawn.kill_count)", host);

            Assert.Equal(ExprValue.OfInt(5), result);
        }

        [Fact]
        public void WorldGet_ThroughComparisonExpression_EvaluatesAsCondition()
        {
            var (state, host) = NewHost();
            state.Set(new Id("world.spawn.kill_count"), ExprValue.OfInt(10), WriterQuest);

            var result = ExprEvaluator.EvaluateBool(
                ExprParser.Parse("world.get_int(world.spawn.kill_count) >= 10", Schema),
                host,
                new ExprDiagnosticsRecorder());

            Assert.True(result);
        }
    }
}
