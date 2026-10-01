using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.Feel
{
    /// <summary>
    /// 调试覆盖层（第 8 层，实验室热调参，仅开发期；手感设计/05 第 3.2 节、06）：全局覆盖与按单位覆盖。
    /// 同一作用域内同一字段只保留一条（再设即替换，避免同层重复写）。任何变化通过 <see cref="Changed"/>
    /// 通知，装配根据此调用解析器失效（全局变化传 null）。
    /// </summary>
    public sealed class FeelDebugOverrides : IFeelDebugProvider
    {
        private readonly FeelFieldSet _fields;
        private readonly List<FeelWrite> _global = new List<FeelWrite>();
        private readonly Dictionary<Id, List<FeelWrite>> _perUnit = new Dictionary<Id, List<FeelWrite>>();

        /// <summary>覆盖变化通知：参数为受影响单位，全局变化为 null。</summary>
        public event Action<Id?>? Changed;

        public FeelDebugOverrides(FeelFieldSet fields)
        {
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        /// <summary>设置全局覆盖（同字段替换）。</summary>
        public void SetGlobal(FeelWrite write)
        {
            Upsert(_global, write);
            Changed?.Invoke(null);
        }

        /// <summary>设置单位覆盖（同字段替换）。</summary>
        public void SetUnit(Id unitId, FeelWrite write)
        {
            if (!_perUnit.TryGetValue(unitId, out var list))
            {
                list = new List<FeelWrite>();
                _perUnit[unitId] = list;
            }
            Upsert(list, write);
            Changed?.Invoke(unitId);
        }

        /// <summary>清除某字段的全局覆盖；无此覆盖返回 false。</summary>
        public bool ClearGlobal(string field)
        {
            var removed = list_Remove(_global, field);
            if (removed) Changed?.Invoke(null);
            return removed;
        }

        /// <summary>清除单位覆盖：<paramref name="field"/> 为空清除该单位全部。</summary>
        public bool ClearUnit(Id unitId, string? field = null)
        {
            if (!_perUnit.TryGetValue(unitId, out var list)) return false;
            var changed = false;
            if (field == null)
            {
                changed = list.Count > 0;
                list.Clear();
            }
            else
            {
                changed = list_Remove(list, field);
            }
            if (list.Count == 0) _perUnit.Remove(unitId);
            if (changed) Changed?.Invoke(unitId);
            return changed;
        }

        /// <summary>清除全部覆盖（全局变化）。</summary>
        public void ClearAll()
        {
            if (_global.Count == 0 && _perUnit.Count == 0) return;
            _global.Clear();
            _perUnit.Clear();
            Changed?.Invoke(null);
        }

        public IReadOnlyList<FeelWrite> GetGlobalOverrides() => _global.ToArray();

        public IReadOnlyList<FeelWrite> GetUnitOverrides(Id unitId) =>
            _perUnit.TryGetValue(unitId, out var list) ? list.ToArray() : Array.Empty<FeelWrite>();

        private void Upsert(List<FeelWrite> list, FeelWrite write)
        {
            if (!_fields.Contains(write.Field)) throw new ArgumentException($"手感字段 \"{write.Field}\" 未登记", nameof(write));
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Field, write.Field, StringComparison.Ordinal))
                {
                    list[i] = write;
                    return;
                }
            }
            list.Add(write);
        }

        private static bool list_Remove(List<FeelWrite> list, string field)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Field, field, StringComparison.Ordinal))
                {
                    list.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }
}
