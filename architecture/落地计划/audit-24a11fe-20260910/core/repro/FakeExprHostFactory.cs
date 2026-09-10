using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Tests.Rules.Skill
{
    /// <summary><see cref="IExprHost"/> 的测试假实现：<see cref="Query"/> 全权委托给可编程的
    /// <see cref="QueryFunc"/>。</summary>
    internal sealed class FakeExprHost : IExprHost
    {
        public Func<string, string, IReadOnlyList<ExprValue>, ExprValue> QueryFunc { get; set; } =
            (group, key, args) => ExprValue.OfBool(true);

        public ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args) => QueryFunc(group, key, args);
    }

    /// <summary><see cref="IExprHostFactory"/> 的测试假实现：每次 <see cref="CreateFor"/> 都产出一个
    /// 绑定同一个 <see cref="DefaultQuery"/> 规则的 <see cref="FakeExprHost"/>，可整体替换以模拟不同
    /// 的条件判定结果。</summary>
    internal sealed class FakeExprHostFactory : IExprHostFactory
    {
        public Func<string, string, IReadOnlyList<ExprValue>, ExprValue> DefaultQuery { get; set; } =
            (group, key, args) => ExprValue.OfBool(true);

        public Id? LastSelfId { get; private set; }
        public Id? LastTargetId { get; private set; }
        public IEvent? LastEvent { get; private set; }

        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent)
        {
            LastSelfId = selfId;
            LastTargetId = targetId;
            LastEvent = triggeringEvent;
            return new FakeExprHost { QueryFunc = DefaultQuery };
        }
    }
}
