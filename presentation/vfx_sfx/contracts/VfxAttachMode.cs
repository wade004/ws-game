namespace Presentation.VfxSfx.Contracts
{
    /// <summary>特效挂接模式（见 09_表现层.md 第 5.1 节 <c>vfx.def.attach_mode</c>）：世界坐标播放 /
    /// 挂接到 sprite 型锚点跟随 / 挂接到 model 型挂点跟随 / 屏幕空间播放。</summary>
    public enum VfxAttachMode
    {
        World,
        Anchor,
        Socket,
        Screen,
    }
}
