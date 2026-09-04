using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary><c>sfx.def</c> 一条记录的不可变运行期视图（见 09_表现层.md 第 5.2 节字段表）。</summary>
    public sealed class SfxDef
    {
        public Id Id { get; }

        /// <summary>分层音效轨道（如 战斗/环境/UI/语音），供混音分组、<see cref="Presentation.VfxSfx.Contracts.ISfxPlayer.SetLayerVolume"/>
        /// 按层调节使用；自由文本。</summary>
        public string Layer { get; }

        /// <summary>同轨道抢占优先级；未声明时为 null（视为最低优先级，见
        /// <see cref="Presentation.VfxSfx.Core.SfxPlayer"/> 抢占规则）。</summary>
        public int? Priority { get; }

        /// <summary>多个随机变体资源引用，播放时随机挑一个；未声明或为空表示只有
        /// <see cref="ResourceRef"/> 一种资源、不做变体随机。</summary>
        public IReadOnlyList<Id>? Variants { get; }

        public Id ResourceRef { get; }

        public SfxDef(Id id, string layer, int? priority, IReadOnlyList<Id>? variants, Id resourceRef)
        {
            Id = id;
            Layer = layer ?? throw new System.ArgumentNullException(nameof(layer));
            Priority = priority;
            Variants = variants;
            ResourceRef = resourceRef;
        }

        /// <summary>从一条已加载的 <c>sfx.def</c> <see cref="DataRecord"/> 构造（同 <see cref="VfxDef.FromRecord"/> 惯例）。</summary>
        public static SfxDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var layer = record.GetString("layer");
            var priority = record.TryGetInt("priority", out var priorityVal) ? (int?)priorityVal : null;
            var variants = record.TryGetIdList("variants", out var variantsVal) ? variantsVal : null;
            var resourceRef = record.GetId("resource_ref");
            return new SfxDef(id, layer, priority, variants, resourceRef);
        }
    }
}
