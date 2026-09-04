using System;
using System.Collections.Generic;
using Core.Foundation.Expr;

namespace Tests.Foundation.Expr
{
    /// <summary>
    /// 可编程测试宿主：按 "group.key" 登记固定返回值或委托，并记录每个 key 被调用的次数，
    /// 供短路求值测试断言"跳过的子表达式没有触发 Query"。
    /// </summary>
    internal sealed class FakeHost : IExprHost
    {
        private readonly Dictionary<string, Func<IReadOnlyList<ExprValue>, ExprValue>> _handlers =
            new Dictionary<string, Func<IReadOnlyList<ExprValue>, ExprValue>>(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _callCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        public FakeHost Set(string group, string key, ExprValue value)
        {
            _handlers[MakeKey(group, key)] = _ => value;
            return this;
        }

        public FakeHost Set(string group, string key, Func<IReadOnlyList<ExprValue>, ExprValue> handler)
        {
            _handlers[MakeKey(group, key)] = handler;
            return this;
        }

        public FakeHost Throws(string group, string key, Exception exception)
        {
            _handlers[MakeKey(group, key)] = _ => throw exception;
            return this;
        }

        public int CallCount(string group, string key)
        {
            return _callCounts.TryGetValue(MakeKey(group, key), out var n) ? n : 0;
        }

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args)
        {
            var mapKey = MakeKey(group, key);
            _callCounts[mapKey] = (_callCounts.TryGetValue(mapKey, out var n) ? n : 0) + 1;

            if (_handlers.TryGetValue(mapKey, out var handler))
            {
                return handler(args);
            }

            throw new InvalidOperationException($"FakeHost 未登记处理器：{group}.{key}");
        }

        private static string MakeKey(string group, string key) => group + "#" + key;
    }
}
