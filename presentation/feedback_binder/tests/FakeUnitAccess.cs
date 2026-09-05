using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>最小 <see cref="IUnitAccess"/> 假实现：只支持
    /// <c>Presentation.FeedbackBinder.Core.FeedbackBinder.ResolveEntityLogicalId</c> 路径需要的
    /// <see cref="GetTemplateId"/>（P4-2 起 <c>from_display: source|target</c> 默认经本接口取模板
    /// id，见该方法判断记录），其余成员按"不需要即抛异常"处理，不为本模块用不到的能力搭建完整假
    /// 实现。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private readonly Dictionary<Id, Id> _templateIds;

        public FakeUnitAccess(Dictionary<Id, Id> templateIds)
        {
            _templateIds = templateIds;
        }

        public Id? GetTemplateId(Id unitId) => _templateIds.TryGetValue(unitId, out var t) ? (Id?)t : null;

        public bool Exists(Id unitId) => _templateIds.ContainsKey(unitId);

        public IReadOnlyList<Id> AllUnits => throw new NotImplementedException();

        public Vec2 GetPosition(Id unitId) => throw new NotImplementedException();

        public void SetPosition(Id unitId, Vec2 position) => throw new NotImplementedException();

        public Id GetFaction(Id unitId) => throw new NotImplementedException();

        public int GetLevel(Id unitId) => throw new NotImplementedException();

        public double GetFacing(Id unitId) => throw new NotImplementedException();

        public bool IsAlive(Id unitId) => throw new NotImplementedException();

        public void SetAlive(Id unitId, bool alive) => throw new NotImplementedException();

        public IReadOnlyList<Id> GetTags(Id unitId) => Array.Empty<Id>();
    }
}
