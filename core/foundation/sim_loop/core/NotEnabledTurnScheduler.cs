using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// <see cref="ITurnScheduler"/> 的占位实现：离散时间模型（回合制）本项目暂不启用
    /// （见 ADR-0013、落地方案与分阶段计划.md T1-5 禁止事项）。全部方法一律抛
    /// <see cref="NotSupportedException"/>，仅用于让依赖 <see cref="ITurnScheduler"/> 的
    /// 调用点在编译期有类型可用；真正的回合调度逻辑留待未来需要回合制时再实现。
    /// </summary>
    public sealed class NotEnabledTurnScheduler : ITurnScheduler
    {
        private const string NotEnabledMessage = "离散时间模型本项目暂不启用，见 ADR-0013 与落地计划 T1-5";

        public void Configure(InitiativePolicy policy, IReadOnlyDictionary<string, object> parameters) =>
            throw new NotSupportedException(NotEnabledMessage);

        public void BeginCombat(IReadOnlyList<Id> participants) =>
            throw new NotSupportedException(NotEnabledMessage);

        public void EndCombat() => throw new NotSupportedException(NotEnabledMessage);

        public SimStep? NextStep() => throw new NotSupportedException(NotEnabledMessage);

        public void SubmitIntent(Id actorId, object intent) =>
            throw new NotSupportedException(NotEnabledMessage);

        public void EndTurn(Id actorId) => throw new NotSupportedException(NotEnabledMessage);

        public IReadOnlyList<Id> GetOrder() => throw new NotSupportedException(NotEnabledMessage);

        public Id? GetCurrentActor() => throw new NotSupportedException(NotEnabledMessage);
    }
}
