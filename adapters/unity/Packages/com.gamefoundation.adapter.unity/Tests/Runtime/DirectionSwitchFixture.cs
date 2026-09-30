#nullable enable
// DirectionSwitchFixture：ADR-0112（消费方反馈第六十三批"方向切换原子化 + 按实体预热全部方向"）四个验收类
// （DirectionSwitchAtomicReproTests / DirectionSwitchAtomicInvariantTests / DirectionPrewarmReproTests /
// DirectionPrewarmInvariantTests）共用的夹具扩展：静态层图写盘、逐帧读数采样、方向解析。
//
// 读数原则（同 CombatStanceAnimFixtureBase）：断言"精灵实例实际应用了什么"——逐帧遍历夹具根物体下全部
// 可见渲染器（每个纸娃娃层的 SpriteRenderer + 整身兜底渲染器），反查它们此刻的 Sprite 实例属于哪个已写盘、
// 已加载的资源（序列帧剪辑帧或静态层图），方向从资源名按命名规则解析。不属于任何已加载资源的 Sprite 是占位
// 方块（冷加载未完成时渲染器上的临时内容），单独计数。不看加载器进度、不看内部回调标志。
//
// 冷加载构造：夹具用独立 UnityResourceLoader 实例、没有宿主每帧 Tick，用例自己决定每帧是否 Tick——不 Tick
// 的帧里没有任何异步加载完成。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Common;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    public abstract class DirectionSwitchFixtureBase : CombatStanceAnimFixtureBase
    {
        protected const string BackDir = "back";
        protected const string FrontSideDir = "front_side_r";
        protected const string BackSideDir = "back_side_r";

        private static readonly string[] KnownDirs = { "front", "front_side_r", "side_r", "back_side_r", "back" };

        /// <summary>8 方向、镜像对去重后的五个方向档位（预热的目标档位集合）。</summary>
        protected static readonly string[] AllDirs = { SideDir, FrontSideDir, FrontDir, BackSideDir, BackDir };

        private readonly List<Id> _writtenImages = new List<Id>();

        [SetUp]
        public void DirectionSwitchSetUp()
        {
            _writtenImages.Clear();
        }

        // ---------------- 朝向 ----------------

        /// <summary>某裸档位名对应的原始朝向（8 方向、无 mirror_pairs 登记；量化表见 DirectionSlots.FromQuantized 的
        /// 判断记录）。side_r 取 PI（index 4，无镜像）——夹具初始朝向 0.0 落在镜像档位 side_l（显示 side_r 的资源、
        /// 水平翻转），两者同档位不同翻转。</summary>
        protected static Direction Face(string dir)
        {
            switch (dir)
            {
                case "side_r": return Direction.FromQuantized(Math.PI, 8);
                case "front": return Direction.FromQuantized(Math.PI / 2, 8);
                case "front_side_r": return Direction.FromQuantized(3 * Math.PI / 4, 8);
                case "back_side_r": return Direction.FromQuantized(5 * Math.PI / 4, 8);
                case "back": return Direction.FromQuantized(3 * Math.PI / 2, 8);
                default: throw new ArgumentException("未知方向 " + dir);
            }
        }

        /// <summary>夹具视图创建后的初始朝向（raw facing 0.0，镜像档位 side_l -> 资源方向 side_r + 翻转）。</summary>
        protected static Direction InitialFacing => Direction.FromQuantized(0.0, 8);

        // ---------------- 静态层图 ----------------

        /// <summary>静态层图资源 id：身体层 <c>layer.&lt;精灵集去前缀&gt;__&lt;方向&gt;__&lt;层名&gt;</c>（精灵集 id 由
        /// BuildFixture 按 <c>sprite.creature.test_0111_&lt;suffix&gt;</c> 构造）；装备层用装备 mesh 去前缀作资源集名。</summary>
        protected static Id StaticLayerId(string suffix, string? meshRefValue, string dir, string layerName)
        {
            var setName = (meshRefValue == null ? "creature.test_0111_" + suffix : StripCategory(meshRefValue)).Replace('.', '_');
            return new Id($"layer.{setName}__{dir}__{layerName}");
        }

        /// <summary>把一张纯色静态层图写到加载器解析的位置（<see cref="UnityResourceLoader.ResolvePath"/>），
        /// <paramref name="size"/> 是正方形边长（预热让路用例用大图拉长在途窗口）。</summary>
        protected void WriteStaticImage(Id imageId, Color color, int size = 4)
        {
            var path = UnityResourceLoader.ResolvePath(imageId, ResourceKind.Image);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex));
            UnityEngine.Object.DestroyImmediate(tex);
            _writtenImages.Add(imageId);
        }

        /// <summary>同步把静态层图加载进缓存（热路径预热），直到 TryGetSprite 命中。</summary>
        protected IEnumerator WarmImage(Id imageId)
        {
            Loader.LoadAsync(imageId, ResourceKind.Image, (_, __) => { });
            var deadline = Time.realtimeSinceStartup + 5f;
            while (!Loader.TryGetSprite(imageId, out _) && Time.realtimeSinceStartup < deadline)
            {
                Loader.Tick();
                yield return null;
            }
            Assert.IsTrue(Loader.TryGetSprite(imageId, out _), $"静态层图 \"{imageId}\" 预热加载超时");
        }

        /// <summary>写一个方向下一层的全套美术：逐层剪辑（可选）+ 静态层图（可选）。</summary>
        protected void WriteLayerArt(
            string suffix, string? meshRefValue, string clipName, string dir, string layerName,
            bool clip = true, bool image = true, int imageSize = 4)
        {
            if (clip)
            {
                WriteEffectResource(LayerResourceId(meshRefValue, clipName, dir, layerName), Color.red, Color.green);
            }
            if (image)
            {
                WriteStaticImage(StaticLayerId(suffix, meshRefValue, dir, layerName), Color.gray, imageSize);
            }
        }

        // ---------------- 读数 ----------------

        protected sealed class Reading
        {
            public string Renderer = "";
            public string Resource = "";
            public string? Dir;
            public bool Placeholder;

            public override string ToString() => $"{Renderer}={Short(Resource)}";
        }

        protected sealed class FrameReading
        {
            public int Frame;
            public List<Reading> Readings = new List<Reading>();
            public string Displayed = "";
            public string Desired = "";

            public HashSet<string> Dirs()
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in Readings)
                {
                    if (r.Dir != null)
                    {
                        set.Add(r.Dir);
                    }
                }
                return set;
            }

            public bool AnyPlaceholder()
            {
                foreach (var r in Readings)
                {
                    if (r.Placeholder)
                    {
                        return true;
                    }
                }
                return false;
            }

            public Reading? Find(string renderer)
            {
                foreach (var r in Readings)
                {
                    if (r.Renderer == renderer)
                    {
                        return r;
                    }
                }
                return null;
            }

            public override string ToString() =>
                $"f{Frame:D3} [shown={Displayed} want={Desired}] " + string.Join(" | ", Readings);
        }

        protected static string Short(string resource) =>
            resource.StartsWith("sprite_anim.", StringComparison.Ordinal) ? resource.Substring("sprite_anim.".Length) : resource;

        /// <summary>资源 id 里的方向段（按命名规则 <c>__&lt;方向&gt;__</c> 或末段）；方向无关的资源返回 null。</summary>
        protected static string? ParseDirection(string resourceId)
        {
            var parts = resourceId.Split(new[] { "__" }, StringSplitOptions.None);
            for (var i = 0; i < parts.Length; i++)
            {
                if (Array.IndexOf(KnownDirs, parts[i]) >= 0)
                {
                    return parts[i];
                }
            }
            return null;
        }

        private string IdentifySprite(Sprite? sprite)
        {
            if (sprite == null)
            {
                return "(none)";
            }
            for (var i = 0; i < WrittenEffectResources.Count; i++)
            {
                if (!Loader.TryGetEffect(WrittenEffectResources[i], out var effect))
                {
                    continue;
                }
                for (var f = 0; f < effect.Frames.Length; f++)
                {
                    if (effect.Frames[f].Sprite == sprite)
                    {
                        return WrittenEffectResources[i].Value;
                    }
                }
            }
            for (var i = 0; i < _writtenImages.Count; i++)
            {
                if (Loader.TryGetSprite(_writtenImages[i], out var image) && image == sprite)
                {
                    return _writtenImages[i].Value;
                }
            }
            return "(placeholder)";
        }

        private Reading? ReadRenderer(string name, SpriteRenderer? sr)
        {
            if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy || sr.sprite == null)
            {
                return null;
            }
            var resource = IdentifySprite(sr.sprite);
            return new Reading
            {
                Renderer = name,
                Resource = resource,
                Dir = ParseDirection(resource),
                Placeholder = resource == "(placeholder)",
            };
        }

        /// <summary>夹具根物体下全部可见渲染器此刻的读数：每个纸娃娃层（按层名）+ 整身兜底渲染器（名 "(fallback)"）。</summary>
        protected List<Reading> SampleVisible(Fx fx)
        {
            var result = new List<Reading>();
            var layersRoot = Renderer.GetLayersRoot(fx.View.EngineHandle);
            var names = Renderer.GetLayerNames(fx.View.EngineHandle);
            if (layersRoot != null && names != null)
            {
                for (var i = 0; i < names.Count; i++)
                {
                    Transform? child = null;
                    for (var c = 0; c < layersRoot.childCount; c++)
                    {
                        if (layersRoot.GetChild(c).name == $"Layer_{i}")
                        {
                            child = layersRoot.GetChild(c);
                            break;
                        }
                    }
                    var reading = ReadRenderer(names[i], child == null ? null : child.GetComponent<SpriteRenderer>());
                    if (reading != null)
                    {
                        result.Add(reading);
                    }
                }
            }
            var fallback = ReadRenderer("(fallback)", fx.Player.SpriteRenderer);
            if (fallback != null)
            {
                result.Add(fallback);
            }
            return result;
        }

        protected FrameReading Snapshot(Fx fx, int frame)
        {
            var slotName = new Func<(Id SlotId, bool FlipX), string>(t =>
                DirectionSlots.StripPrefix(t.SlotId) + (t.FlipX ? "~" : ""));
            return new FrameReading
            {
                Frame = frame,
                Readings = SampleVisible(fx),
                Displayed = slotName(fx.View.DisplayedDirection),
                Desired = slotName(fx.View.DesiredDirection),
            };
        }

        /// <summary>逐帧推进：每帧（可选）Tick 加载器 -> SyncPose（生产里视图绑定器每帧调用）-> yield -> 采样。
        /// <paramref name="tickThisFrame"/> 为 null 表示每帧都 Tick；<paramref name="beforeFrame"/> 在本帧的
        /// Tick/SyncPose 之前调用（用例在指定帧发事件/换装）。</summary>
        protected IEnumerator RunFrames(
            Fx fx, Direction facing, int frames, List<FrameReading> log,
            Func<int, bool>? tickThisFrame = null, Action<int>? beforeFrame = null)
        {
            var start = log.Count;
            for (var i = 0; i < frames; i++)
            {
                beforeFrame?.Invoke(i);
                if (tickThisFrame == null || tickThisFrame(i))
                {
                    Loader.Tick();
                }
                fx.View.SyncPose(Vec2.Zero, facing, 0.0);
                yield return null;
                log.Add(Snapshot(fx, start + i));
            }
        }

        /// <summary>有界推进直到 <paramref name="done"/> 成立（每帧 Tick + SyncPose）。</summary>
        protected IEnumerator RunUntil(
            Fx fx, Direction facing, Func<bool> done, int maxFrames, List<FrameReading> log)
        {
            for (var i = 0; i < maxFrames && !done(); i++)
            {
                yield return RunFrames(fx, facing, 1, log);
            }
        }

        protected static string Dump(List<FrameReading> log, int from = 0, int to = int.MaxValue)
        {
            var sb = new StringBuilder();
            for (var i = Math.Max(0, from); i < log.Count && i <= to; i++)
            {
                sb.AppendLine(log[i].ToString());
            }
            return sb.ToString();
        }

        /// <summary>方向不变量：每一帧全部可见渲染器至多一个方向（方向无关的内容不算）；<paramref name="allowPlaceholder"/>
        /// 为 false 时还要求没有任何占位方块帧。失败消息带完整逐帧读数。</summary>
        protected static void AssertNoMixedDirection(List<FrameReading> log, string context, bool allowPlaceholder = false)
        {
            for (var i = 0; i < log.Count; i++)
            {
                var dirs = log[i].Dirs();
                if (dirs.Count > 1)
                {
                    Assert.Fail($"{context}：第 {i} 帧同屏出现多个方向 [{string.Join(",", dirs)}]（混合帧）\n{Dump(log)}");
                }
                if (!allowPlaceholder && log[i].AnyPlaceholder())
                {
                    Assert.Fail($"{context}：第 {i} 帧出现占位方块\n{Dump(log)}");
                }
            }
        }

        /// <summary>第一个"全部可见渲染器都是 <paramref name="dir"/>、且至少 <paramref name="minVisible"/> 个可见"的帧下标，
        /// 没有返回 -1。</summary>
        protected static int FirstFrameAllIn(List<FrameReading> log, string dir, int minVisible = 1)
        {
            for (var i = 0; i < log.Count; i++)
            {
                var readings = log[i].Readings;
                if (readings.Count < minVisible)
                {
                    continue;
                }
                var all = true;
                foreach (var r in readings)
                {
                    if (r.Dir != dir)
                    {
                        all = false;
                    }
                }
                if (all)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>第一个出现任何 <paramref name="dir"/> 读数的帧下标，没有返回 -1。</summary>
        protected static int FirstFrameAnyIn(List<FrameReading> log, string dir)
        {
            for (var i = 0; i < log.Count; i++)
            {
                if (log[i].Dirs().Contains(dir))
                {
                    return i;
                }
            }
            return -1;
        }

        protected sealed class Box<T>
        {
            public T Value = default!;
        }

        protected static string ClipName(string suffix, string key) => $"h63{suffix}_{key}";

        /// <summary>写一个方向下的整套美术：每个状态键的身体层剪辑、（有装备时）装备层剪辑，加身体/装备静态层图。</summary>
        protected void WriteArtSet(
            string suffix, string? mesh, string[] keys, string dir,
            bool bodyClips = true, bool equipClips = true, bool statics = true)
        {
            foreach (var key in keys)
            {
                WriteLayerArt(suffix, null, ClipName(suffix, key), dir, "body", clip: bodyClips, image: false);
                if (mesh != null)
                {
                    WriteLayerArt(suffix, mesh, ClipName(suffix, key), dir, "mainhand", clip: equipClips, image: false);
                }
            }
            if (statics)
            {
                WriteStaticImage(StaticLayerId(suffix, null, dir, "body"), Color.gray);
                if (mesh != null)
                {
                    WriteStaticImage(StaticLayerId(suffix, mesh, dir, "mainhand"), Color.gray);
                }
            }
        }

        protected IEnumerator WarmArtSet(string suffix, string? mesh, string[] keys, string dir)
        {
            foreach (var key in keys)
            {
                yield return WarmEffectCache(LayerResourceId(null, ClipName(suffix, key), dir, "body"));
                if (mesh != null)
                {
                    yield return WarmEffectCache(LayerResourceId(mesh, ClipName(suffix, key), dir, "mainhand"));
                }
            }
            yield return WarmImage(StaticLayerId(suffix, null, dir, "body"));
            if (mesh != null)
            {
                yield return WarmImage(StaticLayerId(suffix, mesh, dir, "mainhand"));
            }
        }

        /// <summary>造夹具（anim_set = 每个键一条 <c>h63&lt;后缀&gt;_&lt;键&gt;</c> 剪辑）、穿装备、站着、按起始朝向跑 6 帧稳定。</summary>
        protected IEnumerator Build(
            Box<Fx> box, string suffix, string? mesh, string[] keys, Action<UnityViewFactory>? configure = null)
        {
            var clips = new Dictionary<string, string>();
            foreach (var key in keys)
            {
                clips[key] = ResourceRefOf(ClipName(suffix, key));
            }
            var fx = BuildFixture(suffix, clips, configureFactory: configure);
            if (mesh != null)
            {
                EquipMesh(fx, suffix + "_w", "slot.mainhand", mesh);
            }
            StandIdle(fx);
            var settle = new List<FrameReading>();
            yield return RunFrames(fx, InitialFacing, 6, settle);
            box.Value = fx;
        }

        protected static void Finish(Fx fx)
        {
            fx.EquipSource.Dispose();
            fx.View.Destroy();
        }

        /// <summary>读数上的剪辑名段（身体层 <c>&lt;剪辑&gt;__&lt;方向&gt;__&lt;层&gt;</c>；装备层前面多一段 mesh）。</summary>
        protected static string ClipOf(Reading r)
        {
            var parts = Short(r.Resource).Split(new[] { "__" }, StringSplitOptions.None);
            return parts.Length >= 3 ? parts[parts.Length - 3] : r.Resource;
        }

        /// <summary>用例常用：站着（Walk -> Idle）让 idle 逐层剪辑在播。</summary>
        protected static void StandIdle(Fx fx)
        {
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Walk", "Idle"));
        }
    }
}
