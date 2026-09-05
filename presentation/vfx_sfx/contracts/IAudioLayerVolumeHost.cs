using System.Collections.Generic;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 缺口 12：分层音量宿主（见 09_表现层.md 第 5.5 节"音效按 layer 分组，每层可独立调节音量"、
    /// <c>presentation/ui/README.md</c>"已知契约缺口"：此前 <c>SettingsViewModel</c>/<c>UiIntents</c>
    /// 各自接一份构造期注入回调，两侧各说各话，没有统一的"音量从哪来、写到哪去、重启后能不能恢复"
    /// 归口）。本接口把"层清单 + 读音量 + 写音量（含持久化）"收敛到一处，供表现层 UI 消费。
    /// </summary>
    public interface IAudioLayerVolumeHost
    {
        /// <summary>全部可调音量的层名清单：<c>sfx.def.layer</c> 去重后的清单 + <c>"music"</c>
        /// （音乐没有 <c>sfx.def</c> 行，见 <see cref="Presentation.VfxSfx.Core.AudioLayerVolumeHost"/>
        /// 判断记录）。顺序稳定（sfx 层保持传入顺序，<c>"music"</c> 恒在末尾，除非调用方已把
        /// <c>"music"</c> 塞进 sfx 层清单）。</summary>
        IReadOnlyList<string> Layers { get; }

        /// <summary>当前生效音量，范围与语义由具体游戏约定（本接口不做 [0,1] 裁剪，同
        /// <see cref="Core.Foundation.EngineAdapter.IAudio.SetBusVolume"/>/
        /// <see cref="Presentation.VfxSfx.Contracts.ISfxPlayer.SetLayerVolume"/> 惯例）。
        /// <paramref name="layer"/> 不在 <see cref="Layers"/> 里时返回 1.0（默认满音量，不抛异常）。</summary>
        double GetVolume(string layer);

        /// <summary>设置 <paramref name="layer"/> 的音量：<c>"music"</c> 经
        /// <see cref="Core.Foundation.EngineAdapter.IAudio.SetBusVolume"/> 应用，其余层经
        /// <see cref="ISfxPlayer.SetLayerVolume"/> 应用；随后经 <see cref="Core.Foundation.SaveSystem.ISettingsStore"/>
        /// 持久化（键 <c>audio.volume.&lt;layer&gt;</c>），下次构造本接口实现时自动恢复。</summary>
        void SetVolume(string layer, double volume);
    }
}
