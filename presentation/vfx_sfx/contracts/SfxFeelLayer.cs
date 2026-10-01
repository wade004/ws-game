using System;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 手感音效层（手感设计/07 第 3 节）。判断记录：07 第 1 节反馈包的层列表写
    /// <c>swing|whiff|impact|sweetener|voice</c>，第 3 节写五个手感层 <c>swing|whiff|impact|sweetener|footstep</c>，
    /// 两处各漏一项；取并集六层（挥动、挥空、命中、增味、语音、脚步），全部是同一套"层 + 强度档 + 可选材质"
    /// 映射规则，没有层特有的行为差异，故不为一处笔误砍掉任何一层。
    /// </summary>
    public enum SfxFeelLayer
    {
        Swing,
        Whiff,
        Impact,
        Sweetener,
        Voice,
        Footstep,
    }

    /// <summary><see cref="SfxFeelLayer"/> 与数据里的小写蛇形名互转。</summary>
    public static class SfxFeelLayers
    {
        public static readonly string[] Names = { "swing", "whiff", "impact", "sweetener", "voice", "footstep" };

        public static string ToName(SfxFeelLayer layer) => Names[(int)layer];

        public static bool TryParse(string? name, out SfxFeelLayer layer)
        {
            for (var i = 0; i < Names.Length; i++)
            {
                if (string.Equals(Names[i], name, StringComparison.Ordinal))
                {
                    layer = (SfxFeelLayer)i;
                    return true;
                }
            }

            layer = default;
            return false;
        }
    }
}
