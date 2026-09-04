using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary><c>vfx.def</c> 一条记录的不可变运行期视图（见 09_表现层.md 第 5.1 节字段表）。</summary>
    public sealed class VfxDef
    {
        public Id Id { get; }

        /// <summary>分类（施法/命中/环境/UI 等），供对象池分组与音画分层参考；自由文本，见
        /// <see cref="Presentation.VfxSfx.Core.VfxPool"/> 按本字段分池。</summary>
        public string Category { get; }

        public VfxAttachMode AttachMode { get; }

        /// <summary>预期存活时长，用于对象池回收兜底；未声明时为 null（不设兜底回收时限）。</summary>
        public double? Lifetime { get; }

        /// <summary>指向具体引擎资源的间接引用，由适配层解释。</summary>
        public Id ResourceRef { get; }

        public VfxDef(Id id, string category, VfxAttachMode attachMode, double? lifetime, Id resourceRef)
        {
            Id = id;
            Category = category ?? throw new System.ArgumentNullException(nameof(category));
            AttachMode = attachMode;
            Lifetime = lifetime;
            ResourceRef = resourceRef;
        }

        /// <summary>从一条已加载的 <c>vfx.def</c> <see cref="DataRecord"/> 构造（与
        /// <c>Core.Foundation.DisplayInfo.DisplayInfo.FromRecord</c> 同一惯例：假设记录已通过
        /// 校验，缺失必填字段按 <see cref="DataRecord"/> 惯例抛 <see cref="DataFieldException"/>）。</summary>
        public static VfxDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var category = record.GetString("category");
            var attachMode = ParseAttachMode(record, record.GetString("attach_mode"));
            var lifetime = record.TryGetNumber("lifetime", out var lifetimeVal) ? (double?)lifetimeVal : null;
            var resourceRef = record.GetId("resource_ref");
            return new VfxDef(id, category, attachMode, lifetime, resourceRef);
        }

        private static VfxAttachMode ParseAttachMode(DataRecord record, string value) => value switch
        {
            "world" => VfxAttachMode.World,
            "anchor" => VfxAttachMode.Anchor,
            "socket" => VfxAttachMode.Socket,
            "screen" => VfxAttachMode.Screen,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "attach_mode", $"未知的 attach_mode 取值：\"{value}\""),
        };
    }
}
