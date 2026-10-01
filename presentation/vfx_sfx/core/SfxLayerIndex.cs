using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>一次手感音效层解析的结果：命中的 <c>sfx.def</c> 行与实际落到的档/材质（供测试与诊断看回落路径）。</summary>
    public readonly struct SfxLayerResolution
    {
        public Id SfxId { get; }

        /// <summary>实际命中行的强度档（可能低于请求档）。</summary>
        public int Tier { get; }

        /// <summary>实际命中行的材质（<see cref="SfxLayerIndex.GenericMaterial"/> 表示落到通用行）。</summary>
        public string Material { get; }

        /// <summary>是否发生了回落（请求的 <c>(层, 档, 材质)</c> 没有精确命中行）。</summary>
        public bool FellBack { get; }

        public SfxLayerResolution(Id sfxId, int tier, string material, bool fellBack)
        {
            SfxId = sfxId;
            Tier = tier;
            Material = material;
            FellBack = fellBack;
        }
    }

    /// <summary>
    /// 手感音效分层索引（手感设计/07 第 3 节）：把 <c>sfx.def</c> 里声明了 <c>feel_layer</c> 的行按
    /// <c>(层, 档, 材质)</c> 建索引，反馈包只引用"层 + 档"，经本索引映射到具体 sfx 行。
    /// <para>
    /// 回落顺序（缺行不报错、不静音到无声前先尽量找近似）：对请求档 t 从高到低（t、t-1、…、1），每一档先找材质精确行、
    /// 再找该档通用（<see cref="GenericMaterial"/>）行；第一个命中的返回。全部落空返回 null 并记一条去重诊断
    /// （同一 <c>(层, 档, 材质)</c> 只记一次，不刷屏）；请求档 &lt;= 0 表示该层关闭，返回 null 且不记诊断。
    /// 判断记录：先降材质、后降档——材质只是音色差异，档位才决定"这一下打得多重"，听感上"同档通用声"比"低一档的
    /// 精确材质声"更接近设计意图；不向高档回落，避免资产缺失时反而放大动静。
    /// </para>
    /// <para>
    /// 同一 <c>(层, 档, 材质)</c> 出现多行时按 id 序取第一行并记一条诊断（数据歧义，不抛异常）。
    /// </para>
    /// </summary>
    public sealed class SfxLayerIndex
    {
        /// <summary>未声明材质的行/请求视为的材质标签（同手感字段 <c>sfx_material</c> 的缺省 generic）。</summary>
        public const string GenericMaterial = "generic";

        private readonly Dictionary<(SfxFeelLayer Layer, int Tier, string Material), Id> _rows =
            new Dictionary<(SfxFeelLayer, int, string), Id>();

        private readonly HashSet<(SfxFeelLayer, int, string)> _reportedMisses = new HashSet<(SfxFeelLayer, int, string)>();
        private readonly IPresentationDiagnostics _diagnostics;

        public SfxLayerIndex(IEnumerable<SfxDef> defs, IPresentationDiagnostics? diagnostics = null)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();

            var ordered = new List<SfxDef>();
            foreach (var def in defs)
            {
                if (def.FeelLayer.HasValue)
                {
                    ordered.Add(def);
                }
            }
            ordered.Sort((a, b) => string.CompareOrdinal(a.Id.Value, b.Id.Value));

            foreach (var def in ordered)
            {
                var tier = def.FeelTier ?? 1;
                var material = string.IsNullOrEmpty(def.FeelMaterial) ? GenericMaterial : def.FeelMaterial!;
                var key = (def.FeelLayer!.Value, tier, material);
                if (_rows.TryGetValue(key, out var existing))
                {
                    _diagnostics.Warn(
                        $"sfx 手感分层：({SfxFeelLayers.ToName(key.Item1)}, 档 {tier}, 材质 {material}) 有多行登记，" +
                        $"取 id 序第一行 \"{existing}\"，忽略 \"{def.Id}\"");
                    continue;
                }
                _rows[key] = def.Id;
            }
        }

        /// <summary>索引里的行数（去重后）。</summary>
        public int Count => _rows.Count;

        /// <summary>解析 <c>(层, 档, 材质)</c>；回落规则见类型注释。</summary>
        public bool TryResolve(SfxFeelLayer layer, int tier, string? material, out SfxLayerResolution resolution)
        {
            resolution = default;
            if (tier <= 0)
            {
                return false;
            }

            var wanted = string.IsNullOrEmpty(material) ? GenericMaterial : material!;
            for (var t = tier; t >= 1; t--)
            {
                if (_rows.TryGetValue((layer, t, wanted), out var exact))
                {
                    resolution = new SfxLayerResolution(exact, t, wanted, fellBack: t != tier);
                    return true;
                }

                if (wanted != GenericMaterial && _rows.TryGetValue((layer, t, GenericMaterial), out var generic))
                {
                    resolution = new SfxLayerResolution(generic, t, GenericMaterial, fellBack: true);
                    return true;
                }
            }

            if (_reportedMisses.Add((layer, tier, wanted)))
            {
                _diagnostics.Warn(
                    $"sfx 手感分层：({SfxFeelLayers.ToName(layer)}, 档 {tier}, 材质 {wanted}) 及其全部回落行都没有登记，本次不发声");
            }
            return false;
        }
    }
}
