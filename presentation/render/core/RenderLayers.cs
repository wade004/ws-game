namespace Presentation.Render
{
    /// <summary>
    /// 场景层结构的整数层号常量（见 09_表现层.md 第 3.1 节层结构表：地面/装饰/单位/前景遮挡/特效/UI
    /// 六层），供 <c>IRenderer2D.SetTransform</c>/<c>emitParticle</c> 的 <c>layer</c> 参数使用。数值
    /// 只保证"从下到上"的相对顺序，具体是否连续、是否预留中间层号供游戏层插入自定义层，是可配置
    /// 项（见 09"基础架构提供/游戏层提供"表"具体分辨率/拉伸策略等口味数值"一类留白，本模块只固定
    /// 六层的相对顺序这一机制）。
    /// </summary>
    public static class RenderLayers
    {
        /// <summary>地面层：2D 地面图，固定在最底层，不参与 sortY 排序。</summary>
        public const int Ground = 0;

        /// <summary>装饰层：2D 地面装饰物，固定在地面层之上、单位层之下。</summary>
        public const int Decoration = 1;

        /// <summary>单位层：玩家/生物/物件等可见对象，按统一 sortY 排序键合成。</summary>
        public const int Units = 2;

        /// <summary>前景遮挡层：2D 前景遮挡物，固定在单位层之上。</summary>
        public const int Foreground = 3;

        /// <summary>特效层：VFX，按挂点或世界坐标随所属对象参与 sortY 排序（屏幕空间特效另行处理）。</summary>
        public const int Vfx = 4;

        /// <summary>UI 层：固定叠加在最上层，不参与 sortY 排序。</summary>
        public const int Ui = 5;
    }
}
