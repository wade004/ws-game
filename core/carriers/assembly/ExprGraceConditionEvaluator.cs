using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Foundation.InputMap;
using Core.Rules.Common;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// 缺省的基于 Expr 的宽限条件求值（手感设计/01 第 2.4 节，手感落地 M3-B）：<c>found.grace_condition</c> 每行的 <c>expr</c> 在行动者上下文里求值
    /// （经 <see cref="IExprHostFactory.CreateFor"/>，<c>self</c> 分组即行动者本身），结果为真即"条件成立"。游戏只在数据里声明条件名与表达式，不写代码。
    /// <para>
    /// 判断记录（求值上下文）：<c>target</c> 分组绑定到 <see cref="CarriersFeelOptions.GraceTargetResolver"/> 给出的"行动者当前目标"
    /// （缺省取自动攻击的当前目标 <c>AutoAttackHost.GetTarget</c>，没有则不绑定目标）。没有目标时 <c>self.distance_to_target</c> 一类引用按表达式宿主的缺省值处理
    /// （数值 0、布尔 false，并记宿主诊断）——"目标在射程内"一类条件在没有目标时应写成 <c>enemies.nearest_distance &lt;= N</c>，或让游戏提供目标解析委托。
    /// </para>
    /// <para>
    /// 判断记录（解析与热加载）：构造时把每行 <c>expr</c> 按 <c>schema</c> 解析成语法树缓存（数据校验已把无法解析的 Expr 拦在加载阶段，这里再遇到解析异常
    /// 视为数据与装配不一致，抛 <see cref="InvalidOperationException"/> 指名条件，不静默当作假）；数据热加载后调用 <see cref="Reload"/> 重建，
    /// 失败（例如新表达式写坏）时保留旧条件并把原因记入 <see cref="LastReloadError"/>，与手感档案的热加载同一"拒绝并保持"口径。
    /// </para>
    /// <para>
    /// 判断记录（未声明的条件名与求值错误）：动作引用了表里没有的条件名（数据校验按 <c>found.input_action.grace_conditions</c> 的引用表约束会报错）运行期求值恒为假，
    /// 并记入 <see cref="Diagnostics"/>；表达式求值错误（类型不匹配、宿主查询抛异常）按 Expr 求值器的口径判为假，诊断同样记入 <see cref="Diagnostics"/>
    /// （只留最近 <see cref="MaxDiagnostics"/> 条，避免每 tick 采样把诊断撑爆）。
    /// </para>
    /// </summary>
    public sealed class ExprGraceConditionEvaluator : IGraceConditionEvaluator
    {
        /// <summary>诊断列表最多保留的条数（最新的）。</summary>
        public const int MaxDiagnostics = 32;

        public const string TableName = "found.grace_condition";

        private readonly IExprHostFactory _hosts;
        private readonly IExprSchema _schema;
        private readonly Func<Id, Id?>? _targetResolver;
        private readonly BoundedDiagnostics _diagnostics = new BoundedDiagnostics(MaxDiagnostics);
        private Dictionary<Id, ExprNode> _conditions = new Dictionary<Id, ExprNode>();

        /// <param name="registry">数据视图（读 <c>found.grace_condition</c>；表不存在时没有任何条件）。</param>
        /// <param name="hosts">表达式宿主工厂（行动者上下文）。</param>
        /// <param name="schema">解析 <c>expr</c> 用的登记表（与数据校验用的同一份）。</param>
        /// <param name="targetResolver">行动者当前目标解析（可空；非空时 <c>target</c> 分组绑定它给出的目标）。</param>
        public ExprGraceConditionEvaluator(
            IDataRegistryView registry, IExprHostFactory hosts, IExprSchema schema, Func<Id, Id?>? targetResolver = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _hosts = hosts ?? throw new ArgumentNullException(nameof(hosts));
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _targetResolver = targetResolver;
            _conditions = Parse(registry);
        }

        /// <summary>当前缓存的条件数量。</summary>
        public int ConditionCount => _conditions.Count;

        /// <summary>条件是否在表里声明过。</summary>
        public bool HasCondition(Id conditionId) => _conditions.ContainsKey(conditionId);

        /// <summary>求值期诊断（最近 <see cref="MaxDiagnostics"/> 条）。</summary>
        public IReadOnlyList<string> Diagnostics => _diagnostics.Messages;

        /// <summary>最近一次 <see cref="Reload"/> 被拒绝的原因；从未拒绝（或最近一次成功）为 null。</summary>
        public string? LastReloadError { get; private set; }

        public bool Evaluate(Id actorId, Id conditionId)
        {
            if (!_conditions.TryGetValue(conditionId, out var node))
            {
                _diagnostics.Warn($"宽限条件 \"{conditionId}\" 没有在 {TableName} 里声明，按不成立处理");
                return false;
            }

            var target = _targetResolver?.Invoke(actorId);
            var host = _hosts.CreateFor(actorId, target, null);
            return ExprEvaluator.EvaluateBool(node, host, _diagnostics);
        }

        /// <summary>
        /// 数据热加载：重读 <c>found.grace_condition</c> 并换入。新表达式有解析错误时拒绝、保持当前条件，返回 false 并把原因写进 <see cref="LastReloadError"/>。
        /// </summary>
        public bool Reload(IDataRegistryView registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            try
            {
                _conditions = Parse(registry);
                LastReloadError = null;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                LastReloadError = ex.Message;
                return false;
            }
        }

        private Dictionary<Id, ExprNode> Parse(IDataRegistryView registry)
        {
            var result = new Dictionary<Id, ExprNode>();
            var tables = registry.Tables;
            var present = false;
            for (var i = 0; i < tables.Count; i++)
            {
                if (tables[i] == TableName)
                {
                    present = true;
                    break;
                }
            }

            if (!present) return result;

            foreach (var record in registry.GetAll(TableName))
            {
                var definition = GraceConditionDefinition.FromRecord(record);
                try
                {
                    result[definition.ConditionId] = ExprParser.Parse(definition.Expr, _schema);
                }
                catch (ExprParseException ex)
                {
                    throw new InvalidOperationException(
                        $"宽限条件 \"{definition.ConditionId}\" 的表达式无法解析：{ex.Message}（位置 {ex.Position}）", ex);
                }
            }

            return result;
        }

        private sealed class BoundedDiagnostics : IExprDiagnostics
        {
            private readonly int _capacity;
            private readonly List<string> _messages = new List<string>();

            public BoundedDiagnostics(int capacity)
            {
                _capacity = capacity;
            }

            public IReadOnlyList<string> Messages => _messages;

            public void Warn(string message) => Add(message);

            public void Error(string message, Exception? exception = null) => Add(message);

            private void Add(string message)
            {
                if (_messages.Count >= _capacity) _messages.RemoveAt(0);
                _messages.Add(message);
            }
        }
    }
}
