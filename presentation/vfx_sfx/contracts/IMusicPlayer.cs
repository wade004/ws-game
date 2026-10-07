using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 背景音乐播放器（P4 备忘 2，样板游戏 A 反馈）：与 <see cref="ISfxPlayer"/> 对称的表现层入口。<see cref="Core.Foundation.EngineAdapter.IAudio.PlayMusic"/>
    /// 本身不替调用方加载音频资源（ADR-0016 决策 6：<c>IAudio</c> 实现不隐式加载，首次引用方负责 <c>LoadAsync</c>），资源没加载时静音且没有任何提示；
    /// 本播放器就是"首次引用方"：<see cref="Play"/> 在资源未加载时先发起加载、加载完成后再交给音频后端开播，加载失败记诊断。
    /// </summary>
    public interface IMusicPlayer
    {
        /// <summary>请求播放 <paramref name="trackRef"/>（音频资源 id）。已在播或正在加载同一首时什么也不做；请求了别的曲目则以最新一次为准
        /// （加载途中改了主意，先前那一首加载完成后不会再开播）。</summary>
        void Play(Id trackRef, double fadeInSeconds = 0.0, bool loop = true);

        /// <summary>停止当前音乐并作废在途的播放请求。</summary>
        void Stop(double fadeOutSeconds = 0.0);

        /// <summary>最近一次请求的曲目（含仍在加载中的）；没有或已 <see cref="Stop"/> 为 null。</summary>
        Id? Requested { get; }

        /// <summary>真正交给音频后端开播的曲目（加载完成后才有值）；测试与诊断用。</summary>
        Id? Playing { get; }
    }
}
