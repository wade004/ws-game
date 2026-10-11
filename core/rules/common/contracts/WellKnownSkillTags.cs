using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// 框架保留的技能标签 id（ADR-0178）。技能标签（<c>skill.def.tags</c>）本是内容作者自由声明的分类标签，
    /// 这里登记的少数几个由结算管线按约定识别，其余标签框架不解释。
    /// </summary>
    public static class WellKnownSkillTags
    {
        /// <summary>
        /// 不可闪避标签：<c>skill.tag.unavoidable</c>。带此标签的技能，其全部效果的结算跳过命中表的回避分支
        /// （未命中、闪避、招架、偏斜、格挡），等同于 <see cref="EffectContext.CanMiss"/> 为假；暴击不受影响。
        /// 无敌窗口（翻滚无敌帧）是命中表之前的独立步骤，不受此标签影响。
        /// </summary>
        public static readonly Id Unavoidable = new Id("skill.tag.unavoidable");
    }
}
