#nullable enable
// TextureSamplingOptions：[ADR-0096](../../../../../../../architecture/adr/0096-运行时解码贴图带多级渐远链.md)
// 新增，供 UnityResourceLoader.TextureSampling 公开可写属性使用——运行时解码贴图（图像/逐帧动画/
// 地图分层图三条路径）是否生成 mip 链、采样的过滤模式、地图图层的各向异性等级，均由本类型的一个
// 实例集中持有，见 UnityResourceLoader 类型顶部该 ADR 的判断记录段落。
//
// 判断记录（默认开启、不新增 ResourceLoadHints 项）：源资产密度高于屏幕实际显示密度时（远景缩小
// 观察角色等常见场景）未生成 mip 链会导致纹理欠采样锯齿与相机/物体移动时的贴图闪烁，属于普适的
// 图形正确性问题，不是某个具体资源的可选特性，因此默认对三条路径全部开启，不要求调用方逐资源
// 显式声明；仍保留公开开关，供极端场景（内存吃紧的低端设备等）按引擎实现整体关闭。只影响此后经
// UnityResourceLoader 解码的资源，不回溯已经解码并缓存的贴图（已知限制，见 UnityResourceLoader
// 类型顶部判断记录）。
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    /// <summary>
    /// [ADR-0096](../../../../../../../architecture/adr/0096-运行时解码贴图带多级渐远链.md) 新增：
    /// <see cref="UnityResourceLoader"/> 运行时解码贴图的采样参数集中配置。可写属性，调用方可在
    /// 加载器构造后随时修改；只影响此后新发起的解码，不回溯已缓存的贴图（见类型顶部判断记录）。
    /// </summary>
    public sealed class TextureSamplingOptions
    {
        /// <summary>默认 true：<c>ResourceKind.Image</c>（静态图像，含纸娃娃层/精灵集图层）解码时
        /// 是否生成 mip 链。</summary>
        public bool MipChainForImages { get; set; } = true;

        /// <summary>默认 true：<c>ResourceKind.Effect</c>（逐帧动画/特效序列帧图集）解码时是否生成
        /// mip 链。开启时逐帧动画每帧改为切成独立纹理（见 <see cref="UnityResourceLoader"/>
        /// 判断记录），关闭时保持改动前"共用同一张图集纹理、无 mip"的行为。</summary>
        public bool MipChainForEffects { get; set; } = true;

        /// <summary>默认 true：<c>ResourceKind.MapLayers</c>（地图分层图 ground/overlay/decal）解码时
        /// 是否生成 mip 链。</summary>
        public bool MipChainForMapLayers { get; set; } = true;

        /// <summary>默认 <see cref="UnityEngine.FilterMode.Trilinear"/>：三条路径生成 mip 链时采用的
        /// 过滤模式。某条路径本次解码未开启对应的 <c>MipChainFor*</c> 开关时，若本属性仍是默认值
        /// <see cref="UnityEngine.FilterMode.Trilinear"/>，自动降级为
        /// <see cref="UnityEngine.FilterMode.Bilinear"/>（Trilinear 需要 mip 级间插值，没有 mip 链
        /// 时等价于 Bilinear，直接采用可避免引擎在没有 mip 数据时的隐式降级行为不可预期）；显式配置
        /// 为其它取值（如 <see cref="UnityEngine.FilterMode.Point"/>）时原样保留，不强行覆盖，见
        /// <see cref="UnityResourceLoader"/> 判断记录。</summary>
        public FilterMode FilterMode { get; set; } = FilterMode.Trilinear;

        private int _mapLayerAnisoLevel = 4;

        /// <summary>默认 4，取值范围 1..16（超出范围自动夹紧）：地图分层图开启 mip 链时的各向异性
        /// 过滤等级。只对 <c>ResourceKind.MapLayers</c> 生效——地图分层图按世界矩形整体拉伸摆放，
        /// 观察角度导致的贴图倾斜比角色精灵更常见，各向异性过滤收益更明显；其余两条路径（图像/逐帧
        /// 动画）不声明各向异性等级，保持引擎默认。</summary>
        public int MapLayerAnisoLevel
        {
            get => _mapLayerAnisoLevel;
            set => _mapLayerAnisoLevel = Mathf.Clamp(value, 1, 16);
        }
    }
}
