using System;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// <see cref="IEffectExtension"/> 的组合帮助类型（阶段 3 整理"事项四"）：按传入顺序依次尝试每
    /// 一份扩展，第一个返回 <c>true</c> 的生效。<c>core/carriers</c> 三个模块各自只处理 06 第 3.2
    /// 节六类扩展效果原语里的一类，互不重叠（<c>item.ItemEffectExtension</c> 只认
    /// <see cref="EffectKind.CreateItem"/>、<c>gobj.GobjEffectExtension</c> 只认
    /// <see cref="EffectKind.OpenLock"/>、<c>summon.SummonEffectExtension</c> 只认
    /// <see cref="EffectKind.Summon"/>），本类型把三者合并成 <see cref="SkillHost"/> 构造期需要的
    /// 单一 <see cref="IEffectExtension"/>（经 <c>RulesAssembly.EffectExtension.Bind</c> 换入，见
    /// <see cref="Core.Rules.Assembly.DeferredEffectExtension"/> 判断记录）。
    /// </summary>
    public sealed class CompositeEffectExtension : IEffectExtension
    {
        private readonly IEffectExtension[] _extensions;

        public CompositeEffectExtension(params IEffectExtension[] extensions)
        {
            if (extensions == null || extensions.Length == 0)
            {
                throw new ArgumentException("至少需要一份 IEffectExtension", nameof(extensions));
            }
            _extensions = extensions;
        }

        public bool TryHandle(EffectContext context, out ResolveResult result)
        {
            for (var i = 0; i < _extensions.Length; i++)
            {
                if (_extensions[i].TryHandle(context, out result))
                {
                    return true;
                }
            }

            result = null!;
            return false;
        }
    }
}
