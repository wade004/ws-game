using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 位移曲线来源的数据表实现（ADR-0147，手感设计/02 第 4 节）：<c>custom:&lt;id&gt;</c> 引用解析到表
    /// <c>skill.motion_curve</c> 的一行（导入期由 <c>bake-motion</c> 从动画剪辑烘焙出的断点表），运行期运动层按固定步求值，
    /// 逻辑层不读剪辑、不读骨骼——这是根运动驱动删除后"带位移的动画"的唯一路径。
    /// <para>
    /// 判断记录：表不存在或行不存在返回 null（<see cref="MotionMath.EvalCurve"/> 据此抛"曲线引用无法解析"，不静默改线性；
    /// 加载期由 <c>skill.def</c> 的 timeline 规则提前报错）。按记录对象引用缓存解析结果，数据热重载后记录对象换新，缓存自然失效。
    /// </para>
    /// </summary>
    public sealed class DataMotionCurveSource : IMotionCurveSource
    {
        /// <summary>曲线表名。</summary>
        public const string TableName = "skill.motion_curve";

        private readonly IDataRegistryView _registry;
        private readonly Dictionary<string, KeyValuePair<DataRecord, PiecewiseCurve>> _cache =
            new Dictionary<string, KeyValuePair<DataRecord, PiecewiseCurve>>(StringComparer.Ordinal);

        public DataMotionCurveSource(IDataRegistryView registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public PiecewiseCurve? GetCurve(string curveId)
        {
            if (string.IsNullOrEmpty(curveId)) return null;
            var record = _registry.Get(TableName, curveId);
            if (record == null) return null;
            if (_cache.TryGetValue(curveId, out var hit) && ReferenceEquals(hit.Key, record)) return hit.Value;
            var curve = CurveSchema.ReadBreakpoints(record, "points");
            _cache[curveId] = new KeyValuePair<DataRecord, PiecewiseCurve>(record, curve);
            return curve;
        }
    }
}
