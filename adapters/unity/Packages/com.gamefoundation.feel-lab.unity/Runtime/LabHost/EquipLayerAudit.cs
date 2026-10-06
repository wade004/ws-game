#nullable enable
// EquipLayerAudit：换装场景在引擎宿主上的资源核对（手感设计/06 第 3.6 节 + 第 4 节）。
//
// 无头宿主只能核对"外形数据的引用字段"，证明不了美术资源真的加载出来、尺寸与帧数对得上；本核对在换装脚本跑完之后，对每一次穿戴步骤：
//   图标   ：经适配器的资源加载器按资源引用约定（icon 类别路径，AssetRefConventions.IconFile）加载，宽高必须大于 0 且与本次核对里其余图标的众数尺寸一致；
//   逐层剪辑：该装备图层（纸娃娃层，如 hand_main）在当前姿势族的待机/攻击剪辑下，每个朝向都经真实资源加载器加载，帧数与帧尺寸必须与同剪辑同朝向的身体层一致。
// 判断记录（数据对账不在这里）：静态导入校验报告与运行期核对两侧用 item_facts 对账是数据侧的事；本类只负责渲染侧——"实际加载出来的是什么"。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EngineAdapter;
using Lab;
using UnityEngine;

namespace FeelLab.Unity
{
    internal static class EquipLayerAudit
    {
        private static readonly string[] Directions = { "front", "back", "side_r", "front_side_r", "back_side_r" };

        public static void Run(EquipRecording equip, LabHostContext ctx, EngineRecording rec, UnityResourceLoader loader, Action pump)
        {
            AuditIcons(equip, rec, loader, pump);
            AuditLayerClips(equip, ctx, rec, loader, pump);
        }

        private static void AuditIcons(EquipRecording equip, EngineRecording rec, UnityResourceLoader loader, Action pump)
        {
            var found = new List<(string Item, string Icon, int W, int H)>();
            var missing = new List<(string Item, string Icon)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in equip.Steps)
            {
                if (!string.Equals(step.Op, "equip", StringComparison.Ordinal) || step.Icon.Length == 0 || !seen.Add(step.Icon))
                {
                    continue;
                }

                // 图标经适配器自己的资源加载器加载（UnityResourceLoader 的 icon 类别路径，M4-W4），与界面真实走的是同一条路径：
                // 宿主不再自己找文件、自己解码。形状不合约定的 id（IconFile 抛 FormatException）与加载失败都记为缺失。
                var id = new Id(step.Icon);
                var ok = false;
                try
                {
                    loader.LoadAsync(id, ResourceKind.Image, (rid, success) => ok = success);
                    pump();
                }
                catch (FormatException)
                {
                    ok = false;
                }

                if (!ok || !loader.TryGetSprite(id, out var sprite) || sprite == null)
                {
                    missing.Add((step.Arg, step.Icon));
                    continue;
                }

                found.Add((step.Arg, step.Icon, Mathf.RoundToInt(sprite.rect.width), Mathf.RoundToInt(sprite.rect.height)));
            }

            var modal = ModalSize(found);
            foreach (var item in found)
            {
                var bad = item.W <= 0 || item.H <= 0 || item.W != modal.W || item.H != modal.H;
                rec.LayerAudits.Add(new LayerAuditSample("player", "icon:" + item.Item, item.Icon, 1, item.W, item.H, bad));
            }

            foreach (var item in missing)
            {
                rec.LayerAudits.Add(new LayerAuditSample("player", "icon:" + item.Item, item.Icon, 0, 0, 0, true));
            }
        }

        private static (int W, int H) ModalSize(List<(string Item, string Icon, int W, int H)> items)
        {
            var counts = new Dictionary<(int, int), int>();
            var best = (0, 0);
            var bestCount = 0;
            foreach (var item in items)
            {
                var key = (item.W, item.H);
                counts.TryGetValue(key, out var n);
                counts[key] = ++n;
                if (n > bestCount)
                {
                    bestCount = n;
                    best = key;
                }
            }

            return best;
        }

        private static void AuditLayerClips(
            EquipRecording equip, LabHostContext ctx, EngineRecording rec, UnityResourceLoader loader, Action pump)
        {
            var poseSet = ctx.Script.Meta.PoseSet;
            if (poseSet.Length == 0 || string.Equals(poseSet, "none", StringComparison.Ordinal))
            {
                return;
            }

            var set = ctx.World.Registry.Get("display.anim_set", poseSet);
            if (set == null || !set.TryGetObject("clips", out var clips))
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in equip.Steps)
            {
                if (!string.Equals(step.Op, "equip", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = step.Visual.Split('|');
                if (parts.Length != 3 || !string.Equals(parts[0], "slot_mesh", StringComparison.Ordinal))
                {
                    continue;
                }

                var layer = parts[1].StartsWith("slot.", StringComparison.Ordinal) ? parts[1].Substring(5) : parts[1];
                var item = parts[2].StartsWith("paperdoll.", StringComparison.Ordinal)
                    ? parts[2].Substring("paperdoll.".Length).Replace('.', '_')
                    : parts[2].Replace('.', '_');
                foreach (var key in new[] { step.IdleKey, step.AttackKey })
                {
                    if (key.Length == 0 || !seen.Add(item + "|" + layer + "|" + key))
                    {
                        continue;
                    }

                    var clipRef = ClipRef(clips, key);
                    if (clipRef == null || !clipRef.StartsWith("sprite_anim.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var clipName = clipRef.Substring("sprite_anim.".Length);
                    foreach (var direction in Directions)
                    {
                        var layerRef = "sprite_anim." + item + "__" + clipName + "__" + direction + "__" + layer;
                        var bodyRef = clipRef + "__" + direction + "__body";
                        rec.LayerAudits.Add(CompareClips(loader, pump, "player", layer + ":" + key + ":" + direction, layerRef, bodyRef));
                    }
                }
            }
        }

        /// <summary>
        /// 核对一个图层剪辑：经真实资源加载器加载图层剪辑与参照（身体层）剪辑，帧数与帧尺寸必须都大于 0 且二者一致；
        /// 图层剪辑加载不出来、帧数/尺寸为 0、与参照不一致都记为不一致。参照剪辑加载不出来时只按图层自身核对（参照缺失由参照自己的核对暴露）。
        /// </summary>
        internal static LayerAuditSample CompareClips(
            UnityResourceLoader loader, Action pump, string unit, string label, string layerRef, string bodyRef)
        {
            var layerAsset = Load(loader, pump, layerRef);
            var bodyAsset = Load(loader, pump, bodyRef);
            var (frames, w, h) = Measure(layerAsset);
            var (bodyFrames, bw, bh) = Measure(bodyAsset);
            var bad = layerAsset == null || frames <= 0 || w <= 0 || h <= 0
                || (bodyAsset != null && (frames != bodyFrames || w != bw || h != bh));
            return new LayerAuditSample(unit, label, layerRef, frames, w, h, bad);
        }

        private static string? ClipRef(JsonObject clips, string key)
        {
            if (clips.TryGetValue(key, out var value) && value is JsonObject clip
                && clip.TryGetValue("resource_ref", out var reference) && reference is JsonString text)
            {
                return text.Value;
            }

            return null;
        }

        private static UnityResourceLoader.EffectAsset? Load(UnityResourceLoader loader, Action pump, string reference)
        {
            var id = new Id(reference);
            if (!loader.TryGetEffect(id, out var asset))
            {
                loader.LoadAsync(id, ResourceKind.Effect, (rid, ok) => { });
                pump();
                if (!loader.TryGetEffect(id, out asset))
                {
                    return null;
                }
            }

            return asset;
        }

        private static (int Frames, int W, int H) Measure(UnityResourceLoader.EffectAsset? asset)
        {
            if (asset == null || asset.Frames.Length == 0)
            {
                return (0, 0, 0);
            }

            var rect = asset.Frames[0].Sprite.rect;
            return (asset.Frames.Length, (int)rect.width, (int)rect.height);
        }
    }
}
