using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;

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

        /// <summary>ADR-0074 新增：混合模式，未声明时为 <see cref="VfxBlendMode.Alpha"/>（与改动前
        /// 逐字一致的既有行为）。直接透传给 <see cref="IRenderer2D"/> 新增的 <c>EmitParticle</c> 重载，
        /// 见 <see cref="Presentation.VfxSfx.Core.VfxPlayer.Spawn"/>。</summary>
        public VfxBlendMode BlendMode { get; }

        public VfxDef(Id id, string category, VfxAttachMode attachMode, double? lifetime, Id resourceRef)
            : this(id, category, attachMode, lifetime, resourceRef, VfxBlendMode.Alpha)
        {
        }

        /// <summary>ADR-0074 新增重载：携带 <see cref="BlendMode"/>。既有 5 参构造函数保留、签名不变
        /// （ABI 只加法），转发到本重载并固定传 <see cref="VfxBlendMode.Alpha"/>。</summary>
        public VfxDef(Id id, string category, VfxAttachMode attachMode, double? lifetime, Id resourceRef, VfxBlendMode blendMode)
        {
            Id = id;
            Category = category ?? throw new System.ArgumentNullException(nameof(category));
            AttachMode = attachMode;
            Lifetime = lifetime;
            ResourceRef = resourceRef;
            BlendMode = blendMode;
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
            var blendMode = record.TryGetString("blend_mode", out var blendModeVal) ? ParseBlendMode(record, blendModeVal) : VfxBlendMode.Alpha;
            return new VfxDef(id, category, attachMode, lifetime, resourceRef, blendMode);
        }

        private static VfxAttachMode ParseAttachMode(DataRecord record, string value) => value switch
        {
            "world" => VfxAttachMode.World,
            "anchor" => VfxAttachMode.Anchor,
            "socket" => VfxAttachMode.Socket,
            "screen" => VfxAttachMode.Screen,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "attach_mode", $"未知的 attach_mode 取值：\"{value}\""),
        };

        private static VfxBlendMode ParseBlendMode(DataRecord record, string value) => value switch
        {
            "alpha" => VfxBlendMode.Alpha,
            "additive" => VfxBlendMode.Additive,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "blend_mode", $"未知的 blend_mode 取值：\"{value}\""),
        };
    }
}
