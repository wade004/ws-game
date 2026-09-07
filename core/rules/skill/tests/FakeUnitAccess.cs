using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;

namespace Tests.Rules.Skill
{
    /// <summary><see cref="IUnitAccess"/> 的测试假实现：手工登记的单位状态表，不做任何空间索引/
    /// 阵营矩阵之类的真实逻辑。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private readonly HashSet<Id> _units = new HashSet<Id>();
        private readonly Dictionary<Id, Vec2> _positions = new Dictionary<Id, Vec2>();
        private readonly Dictionary<Id, bool> _alive = new Dictionary<Id, bool>();
        private readonly Dictionary<Id, Id> _factions = new Dictionary<Id, Id>();
        private readonly Dictionary<Id, int> _levels = new Dictionary<Id, int>();
        private readonly Dictionary<Id, double> _facings = new Dictionary<Id, double>();
        private readonly Dictionary<Id, List<Id>> _tags = new Dictionary<Id, List<Id>>();

        public FakeUnitAccess Add(Id id, Vec2? position = null, bool alive = true, Id? faction = null, int level = 1)
        {
            _units.Add(id);
            _positions[id] = position ?? Vec2.Zero;
            _alive[id] = alive;
            if (faction.HasValue) _factions[id] = faction.Value;
            _levels[id] = level;
            return this;
        }

        public bool Exists(Id unitId) => _units.Contains(unitId);

        public IReadOnlyList<Id> AllUnits => _units.OrderBy(id => id.Value, StringComparer.Ordinal).ToList();

        public Vec2 GetPosition(Id unitId) => _positions.TryGetValue(unitId, out var p) ? p : Vec2.Zero;

        public void SetPosition(Id unitId, Vec2 position) => _positions[unitId] = position;

        public Id GetFaction(Id unitId) => _factions.TryGetValue(unitId, out var f) ? f : default;

        public int GetLevel(Id unitId) => _levels.TryGetValue(unitId, out var l) ? l : 1;

        public double GetFacing(Id unitId) => _facings.TryGetValue(unitId, out var f) ? f : 0;

        public bool IsAlive(Id unitId) => _alive.TryGetValue(unitId, out var a) && a;

        public void SetAlive(Id unitId, bool alive) => _alive[unitId] = alive;

        public Id? GetTemplateId(Id unitId) => null;

        public IReadOnlyList<Id> GetTags(Id unitId) => _tags.TryGetValue(unitId, out var t) ? t : Array.Empty<Id>();

        /// <summary>RC-03 收边补齐：模拟单位被彻底销毁（<c>entity.destroyed</c> 之后
        /// <see cref="Exists"/> 应当返回 false）——此前本假实现只有 <see cref="Add"/>，没有对应的
        /// 反操作，无法测试"目标/施法者已被销毁（而不只是死亡）"这条区别于"存在但已死亡"的路径
        /// （见 <see cref="Core.Rules.Skill.CastPipeline.FilterDestroyedTargets"/>/
        /// <see cref="Core.Rules.Skill.CastPipeline.IsCasterStillValid"/> 判断记录）。</summary>
        public FakeUnitAccess Remove(Id id)
        {
            _units.Remove(id);
            return this;
        }
    }
}
