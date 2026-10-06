#nullable enable
// EquipWardrobeRunner：衣橱在引擎宿主上的完整跑法（手感设计/06 第 3.6 节"逐件穿戴"、08 第 6 节、ADR-0149）。
//
// 一次运行做四件事：
//   ① 由数据列出全部可装备物品并生成衣橱脚本（EquipWardrobe.Plan，物品清单/槽位次序全取数据，不手写）；
//   ② 在引擎宿主上跑这份脚本（逐件穿、再全部卸下；EngineLabStage 照常做图标与待机/攻击图层剪辑的资源核对）；
//   ③ 方向 × 姿势键轮播核对：每件纸娃娃装备 × 五个已制作方向 ×（idle / move.run / attack 三个姿势键，武器族取该件穿上时的姿势族），
//      静态层（layer.<集>__<方向>__<层>）与逐层剪辑（sprite_anim.<物品>__<剪辑>__<方向>__<层>）都经真实资源加载器加载，
//      剪辑的帧数/帧尺寸必须与同剪辑同方向的身体层一致——与导入校验的 equip_layer_static_missing / equip_override_clip_layer_missing
//      逐项一一对应（导入工具读文件头静态地查，这里经引擎真实加载查）；
//   ④ 出本地产物：完整度报告 JSON 与方向 × 装备的静态拼图 PNG（lab/out/wardrobe/，已被忽略规则覆盖，不进 git）。
//
// 判断记录（报告计数由数据算出）：物品数、槽位数、武器/纸娃娃/模型/无外观件数、轮播格数（= 纸娃娃件数 × 方向数 ×（静态层 1 + 姿势键数））
// 都由数据清单与常量算出，用例按同一规则独立重算，不写死裸数。
// 判断记录（拼图是本地 QA 产物）：拼图按每格"身体静态层 + 装备静态层"居中叠放（不做锚点对位，占位美术的画布大小不同），用于人眼快速过一遍
// 每件装备每个方向有没有图、有没有画错，不是渲染正确性的验收（那由逐层剪辑核对承担）；纹理不可读或没有像素访问时只记原因、不阻断。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Ui;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DisplayInfo;
using Lab;
using UnityEngine;

namespace FeelLab.Unity
{
    /// <summary>方向 × 姿势键轮播里的一格核对结果。</summary>
    public sealed class WardrobeCarouselCell
    {
        public string Item { get; }

        /// <summary><c>static</c>（静态层）或姿势状态名（<c>idle</c> / <c>move.run</c> / <c>attack</c>）。</summary>
        public string Pose { get; }

        /// <summary>解析出的姿势集键（静态层为空）。</summary>
        public string PoseKey { get; }

        public string Direction { get; }

        /// <summary>被核对的图层资源（静态层 id 或图层剪辑 id）。</summary>
        public string Resource { get; }

        public int Frames { get; }

        public int Width { get; }

        public int Height { get; }

        public bool Mismatch { get; }

        public WardrobeCarouselCell(string item, string pose, string poseKey, string direction, string resource, int frames, int width, int height, bool mismatch)
        {
            Item = item;
            Pose = pose;
            PoseKey = poseKey;
            Direction = direction;
            Resource = resource;
            Frames = frames;
            Width = width;
            Height = height;
            Mismatch = mismatch;
        }
    }

    public sealed class WardrobeEngineResult
    {
        public InputScript Script { get; }

        public List<WardrobeEntry> Entries { get; }

        public int SlotCount { get; }

        public EngineLabRun Run { get; }

        public WardrobeReport Report { get; }

        public List<WardrobeCarouselCell> Carousel { get; } = new List<WardrobeCarouselCell>();

        /// <summary>方向 × 姿势键轮播的期望格数（由数据与常量算出）。</summary>
        public int ExpectedCarouselCells { get; set; }

        public string? ReportPath { get; set; }

        public string? MontagePath { get; set; }

        /// <summary>拼图没有产出的原因（产出了为空）。</summary>
        public string MontageNote { get; set; } = string.Empty;

        public WardrobeEngineResult(InputScript script, List<WardrobeEntry> entries, int slotCount, EngineLabRun run, WardrobeReport report)
        {
            Script = script;
            Entries = entries;
            SlotCount = slotCount;
            Run = run;
            Report = report;
        }
    }

    public static class EquipWardrobeRunner
    {
        /// <summary>轮播的姿势（状态 + 可选步态），武器族取该件装备穿上时的姿势族。</summary>
        public static readonly (string Name, string State, string? Gait)[] Poses =
        {
            ("idle", "idle", null),
            ("move.run", "move", "run"),
            ("attack", "attack", null),
        };

        public const string TemplateScript = "equip_cycle";

        /// <summary>
        /// 跑衣橱。<paramref name="cell"/> 取引擎宿主适用的格子（缺省 <c>2d_action</c>）；<paramref name="outDir"/> 缺省 <c>&lt;仓库根&gt;/lab/out/wardrobe</c>；
        /// <paramref name="bodySet"/> 是拼图用的身体静态层精灵集（空 = 只拼装备层）。
        /// </summary>
        public static WardrobeEngineResult Run(EngineLabHost host, string cell = "2d_action", string? outDir = null, string bodySet = "placeholder_hero", bool writeFiles = true)
        {
            InputScript? template = null;
            foreach (var script in host.LoadScripts())
            {
                if (string.Equals(script.Meta.ScriptId, TemplateScript, StringComparison.Ordinal))
                {
                    template = script;
                    break;
                }
            }

            if (template == null)
            {
                throw new InvalidOperationException("夹具里没有模板脚本 " + TemplateScript + "（衣橱沿用它的数据根与姿势集）");
            }

            var wardrobe = EquipWardrobe.Plan(host.Runner, template, out var entries, out var slotCount);
            var run = host.Run(wardrobe, cell);
            var equip = run.Recording.Equip ?? throw new InvalidOperationException("衣橱脚本没有产生换装记录");
            var report = EquipWardrobe.BuildReport(entries, slotCount, equip);
            var result = new WardrobeEngineResult(wardrobe, entries, slotCount, run, report);

            var loader = UnityEngineHost.Ensure().ResourceLoader;
            void Pump() => EngineLabStage.PumpLoader(loader, 5000);

            AuditCarousel(host, template, equip, result, loader, Pump);

            // 资源侧核对合计：舞台跑完时的图标与待机/攻击剪辑核对 + 本类的方向 × 姿势键轮播。
            var total = run.Engine.LayerAudits.Count + result.Carousel.Count;
            var bad = 0;
            foreach (var audit in run.Engine.LayerAudits)
            {
                if (audit.Mismatch)
                {
                    bad++;
                    report.Problems.Add("资源核对不一致：" + audit.Layer + " " + audit.Resource);
                }
            }

            foreach (var c in result.Carousel)
            {
                if (c.Mismatch)
                {
                    bad++;
                    report.Problems.Add("轮播核对不一致：" + c.Item + " " + c.Pose + " " + c.Direction + " " + c.Resource);
                }
            }

            report.AuditTotal = total;
            report.AuditBad = bad;

            if (writeFiles)
            {
                var dir = outDir ?? Path.Combine(host.RepoRoot, "lab", "out", "wardrobe");
                Directory.CreateDirectory(dir);
                result.ReportPath = Path.Combine(dir, "wardrobe_report.json");
                File.WriteAllText(result.ReportPath, report.ToJson());
                WriteMontage(result, loader, Pump, bodySet, Path.Combine(dir, "wardrobe_montage.png"));
            }

            return result;
        }

        private static void AuditCarousel(
            EngineLabHost host, InputScript template, EquipRecording equip, WardrobeEngineResult result,
            UnityResourceLoader loader, Action pump)
        {
            var clipKeys = new HashSet<string>(StringComparer.Ordinal);
            JsonObject? clips = null;
            var poseSet = template.Meta.PoseSet;
            if (poseSet.Length > 0 && !string.Equals(poseSet, "none", StringComparison.Ordinal))
            {
                var probe = Lab.LabHost.BuildProbe(host.Runner.DatasetFor(template).HostOptions);
                var set = probe.Registry.Get("display.anim_set", poseSet);
                if (set != null && set.TryGetObject("clips", out var c))
                {
                    clips = c;
                    for (var i = 0; i < c.Count; i++)
                    {
                        clipKeys.Add(c[i].Key);
                    }
                }
            }

            var paperdoll = 0;
            foreach (var entry in result.Entries)
            {
                if (entry.IsPaperdoll)
                {
                    paperdoll++;
                }
            }

            result.ExpectedCarouselCells = paperdoll * PaperdollPreview.Directions.Length * (1 + (clips != null ? Poses.Length : 0));

            {
                foreach (var entry in result.Entries)
                {
                    if (!entry.IsPaperdoll)
                    {
                        continue;
                    }

                    var visual = FindVisual(equip, entry.ItemId, out var family);
                    if (visual == null)
                    {
                        continue;
                    }

                    var parts = visual.Split('|');
                    var layer = parts[1].StartsWith("slot.", StringComparison.Ordinal) ? parts[1].Substring(5) : parts[1];
                    var itemSet = UiVisuals.SetName(parts[2]);
                    foreach (var direction in PaperdollPreview.Directions)
                    {
                        // 静态层（对应导入校验 equip_layer_static_missing）。
                        var staticId = UiVisuals.LayerId(itemSet, direction, layer);
                        var sprite = LoadSprite(loader, pump, staticId);
                        result.Carousel.Add(new WardrobeCarouselCell(
                            entry.ItemId, "static", string.Empty, direction, staticId.Value, sprite != null ? 1 : 0,
                            sprite != null ? Mathf.RoundToInt(sprite.rect.width) : 0, sprite != null ? Mathf.RoundToInt(sprite.rect.height) : 0, sprite == null));

                        if (clips == null)
                        {
                            continue;
                        }

                        // 逐层剪辑（对应导入校验 equip_override_clip_layer_missing）：姿势键按武器族回落链解析。
                        foreach (var pose in Poses)
                        {
                            var resolution = PoseResolver.Resolve(new PoseRequest(pose.State, pose.Gait, family: string.IsNullOrEmpty(family) ? null : family), k => clipKeys.Contains(k));
                            var key = resolution.Found ? resolution.TableKey : string.Empty;
                            var clipRef = key.Length > 0 ? ClipRef(clips, key) : null;
                            if (clipRef == null || !clipRef.StartsWith("sprite_anim.", StringComparison.Ordinal))
                            {
                                result.Carousel.Add(new WardrobeCarouselCell(entry.ItemId, pose.Name, key, direction, string.Empty, 0, 0, 0, true));
                                continue;
                            }

                            var clipName = clipRef.Substring("sprite_anim.".Length);
                            var layerRef = "sprite_anim." + itemSet + "__" + clipName + "__" + direction + "__" + layer;
                            var bodyRef = clipRef + "__" + direction + "__body";
                            var sample = EquipLayerAudit.CompareClips(loader, pump, "player", pose.Name + ":" + direction, layerRef, bodyRef);
                            result.Carousel.Add(new WardrobeCarouselCell(
                                entry.ItemId, pose.Name, key, direction, layerRef, sample.FrameCount, sample.Width, sample.Height, sample.Mismatch));
                        }
                    }
                }
            }
        }

        private static string? FindVisual(EquipRecording equip, string item, out string family)
        {
            family = string.Empty;
            foreach (var step in equip.Steps)
            {
                if (string.Equals(step.Op, "equip", StringComparison.Ordinal) && string.Equals(step.Arg, item, StringComparison.Ordinal))
                {
                    family = step.PoseFamily;
                    return step.Visual.Split('|').Length == 3 ? step.Visual : null;
                }
            }

            return null;
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

        private static Sprite? LoadSprite(UnityResourceLoader loader, Action pump, Id id)
        {
            if (!loader.TryGetSprite(id, out var sprite))
            {
                try
                {
                    loader.LoadAsync(id, Core.Foundation.EngineAdapter.ResourceKind.Image, (rid, ok) => { });
                    pump();
                }
                catch (FormatException)
                {
                    return null;
                }

                loader.TryGetSprite(id, out sprite);
            }

            return sprite;
        }

        // ───────── 拼图 ─────────

        private static void WriteMontage(WardrobeEngineResult result, UnityResourceLoader loader, Action pump, string bodySet, string path)
        {
            var rows = new List<string>();
            foreach (var cell in result.Carousel)
            {
                if (string.Equals(cell.Pose, "static", StringComparison.Ordinal) && !rows.Contains(cell.Item))
                {
                    rows.Add(cell.Item);
                }
            }

            if (rows.Count == 0)
            {
                result.MontageNote = "没有纸娃娃装备，不出拼图";
                return;
            }

            var dirs = PaperdollPreview.Directions;
            var tiles = new Dictionary<(string, string), List<Sprite>>();
            var tileW = 1;
            var tileH = 1;
            foreach (var cell in result.Carousel)
            {
                if (!string.Equals(cell.Pose, "static", StringComparison.Ordinal))
                {
                    continue;
                }

                var list = new List<Sprite>();
                if (bodySet.Length > 0)
                {
                    var body = LoadSprite(loader, pump, UiVisuals.LayerId(bodySet, cell.Direction, "body"));
                    if (body != null)
                    {
                        list.Add(body);
                    }
                }

                var layer = LoadSprite(loader, pump, new Id(cell.Resource));
                if (layer != null)
                {
                    list.Add(layer);
                }

                tiles[(cell.Item, cell.Direction)] = list;
                foreach (var s in list)
                {
                    tileW = Mathf.Max(tileW, Mathf.RoundToInt(s.rect.width));
                    tileH = Mathf.Max(tileH, Mathf.RoundToInt(s.rect.height));
                }
            }

            try
            {
                var texture = new Texture2D(tileW * dirs.Length, tileH * rows.Count, TextureFormat.RGBA32, false);
                var clear = new Color32[texture.width * texture.height];
                for (var i = 0; i < clear.Length; i++)
                {
                    clear[i] = new Color32(32, 34, 42, 255);
                }

                texture.SetPixels32(clear);
                for (var r = 0; r < rows.Count; r++)
                {
                    for (var d = 0; d < dirs.Length; d++)
                    {
                        if (!tiles.TryGetValue((rows[r], dirs[d]), out var list))
                        {
                            continue;
                        }

                        // 第 0 行在图片最上面：Unity 纹理原点在左下，所以行倒过来排。
                        var originX = d * tileW;
                        var originY = (rows.Count - 1 - r) * tileH;
                        foreach (var sprite in list)
                        {
                            BlendSprite(texture, sprite, originX + (tileW - Mathf.RoundToInt(sprite.rect.width)) / 2, originY + (tileH - Mathf.RoundToInt(sprite.rect.height)) / 2);
                        }
                    }
                }

                texture.Apply();
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(texture));
                UnityEngine.Object.Destroy(texture);
                result.MontagePath = path;
            }
            catch (Exception ex)
            {
                result.MontageNote = "拼图失败：" + ex.Message;
            }
        }

        private static void BlendSprite(Texture2D target, Sprite sprite, int x0, int y0)
        {
            var rect = sprite.rect;
            var w = Mathf.RoundToInt(rect.width);
            var h = Mathf.RoundToInt(rect.height);
            var src = sprite.texture.GetPixels32();
            var tw = sprite.texture.width;
            var dst = target.GetPixels32();
            for (var y = 0; y < h; y++)
            {
                var ty = y0 + y;
                if (ty < 0 || ty >= target.height)
                {
                    continue;
                }

                for (var x = 0; x < w; x++)
                {
                    var tx = x0 + x;
                    if (tx < 0 || tx >= target.width)
                    {
                        continue;
                    }

                    var s = src[(Mathf.RoundToInt(rect.y) + y) * tw + Mathf.RoundToInt(rect.x) + x];
                    if (s.a == 0)
                    {
                        continue;
                    }

                    var d = dst[ty * target.width + tx];
                    var a = s.a / 255f;
                    dst[ty * target.width + tx] = new Color32(
                        (byte)(s.r * a + d.r * (1f - a)), (byte)(s.g * a + d.g * (1f - a)), (byte)(s.b * a + d.b * (1f - a)), 255);
                }
            }

            target.SetPixels32(dst);
        }
    }
}
