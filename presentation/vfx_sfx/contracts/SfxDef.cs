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

        /// <summary>ADR-0089 新增：循环播放（区域触发进入/持续状态施加一类"进入态起播、离开态
        /// 停播"的音效，见 <see cref="Presentation.VfxSfx.Core.SfxPlayer.PlayAttached"/>）；未声明
        /// （数据行没有 <c>loop</c> 字段）时为 <c>false</c>，与本字段新增前的既有一次性播放行为
        /// 逐字一致（见 <see cref="FromRecord"/>）。</summary>
        public bool Loop { get; }

        /// <summary>ABI 兼容 façade：不带 <see cref="Loop"/> 的旧构造签名，物理 IL 签名与新增该字段
        /// 之前完全一致，供旧编译产物不重新编译即可继续加载；<see cref="Loop"/> 恒为 <c>false</c>。</summary>
        public SfxDef(Id id, string layer, int? priority, IReadOnlyList<Id>? variants, Id resourceRef)
            : this(id, layer, priority, variants, resourceRef, loop: false)
        {
        }

        /// <summary>ADR-0089 新增重载：九个参数均不带默认值，避免与上面五参数构造在调用点产生
        /// 二义性（同 <see cref="Core.Rules.Common.CombatDamageDealtEvent"/> 新增重载判断记录同一套
        /// 惯例）。</summary>
        public SfxDef(Id id, string layer, int? priority, IReadOnlyList<Id>? variants, Id resourceRef, bool loop)
        {
            Id = id;
            Layer = layer ?? throw new System.ArgumentNullException(nameof(layer));
            Priority = priority;
            Variants = variants;
            ResourceRef = resourceRef;
            Loop = loop;
        }

        /// <summary>从一条已加载的 <c>sfx.def</c> <see cref="DataRecord"/> 构造（同 <see cref="VfxDef.FromRecord"/> 惯例）。
        /// ADR-0089：<c>loop</c> 为新增可选字段，未提供时按 <see cref="Loop"/> 判断记录缺省 <c>false</c>。</summary>
        public static SfxDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var layer = record.GetString("layer");
            var priority = record.TryGetInt("priority", out var priorityVal) ? (int?)priorityVal : null;
            var variants = record.TryGetIdList("variants", out var variantsVal) ? variantsVal : null;
            var resourceRef = record.GetId("resource_ref");
            var loop = record.TryGetBool("loop", out var loopVal) && loopVal;
            return new SfxDef(id, layer, priority, variants, resourceRef, loop);
        }
    }
}
