#nullable enable
// CombatStanceAnimFixture：ADR-0111（消费方反馈第六十一批"战斗待机"）两个验收类
// （CombatStanceAnimReproTests / CombatStanceAnimInvariantTests）共用的自造夹具与读数辅助。
//
// 夹具原则（不依赖检出目录里碰巧存在的文件）：每条用例把需要的序列帧资源自己写进一个 Guid 命名的
// 临时目录并经 UnityResourceLoader.RootDirOverrideForTests 指过去；外形是 8 方向、
// paperdoll_layers:["body"]、可选装备一件落在 mainhand 层的武器，display.anim_set 按用例给定的
// "剪辑键 -> resource_ref"声明。期望的资源 id 一律在用例里按命名规则拼出来（LayerResourceId），不写死。
//
// 读数原则：断言"精灵实例实际应用了什么"——逐层剪辑播放时每层 SpriteRenderer.sprite 就是该层逐层
// 剪辑帧集里的某个 Sprite 实例，AppliedResource 反查它属于哪个已加载资源（不看加载器进度、不看
// player 的剪辑 id，那是解析结果不是视觉结果）。
//
// 渲染隔离（AGENTS.md）：夹具根物体整棵挂到专属 Layer，并自带一个 cullingMask 只看该 Layer 的相机；
// 用例不做任何全场景查找，只读自己根物体下的 SpriteRenderer，不依赖套件里其它用例的清理是否完整。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.Tests.Runtime
{
    public abstract class CombatStanceAnimFixtureBase : PlayModeTestBase
    {
        /// <summary>本类专属高位 Layer（项目 TagManager 里 8~31 全部未使用，同 UnityRenderer2DTieBreakTests）。</summary>
        protected const int IsolationLayer = 30;

        /// <summary>8 方向、无 mirror_pairs 时 raw facing=0.0 解析出的裸档位名。</summary>
        protected const string SideDir = "side_r";

        /// <summary>raw facing=PI/2 解析出的裸档位名。</summary>
        protected const string FrontDir = "front";

        protected GameObject RootGo = null!;
        protected UnityResourceLoader Loader = null!;
        protected UnityRenderer2D Renderer = null!;
        private GameObject _cameraGo = null!;
        private string _scratchRoot = null!;
        private readonly List<Id> _writtenResources = new List<Id>();

        [SetUp]
        public void FixtureSetUp()
        {
            RootGo = new GameObject("CombatStanceFixtureRoot");
            Loader = new UnityResourceLoader();
            Renderer = new UnityRenderer2D(RootGo.transform, Loader);

            _cameraGo = new GameObject("CombatStanceFixtureCamera");
            var camera = _cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.cullingMask = 1 << IsolationLayer;
            camera.enabled = false;

            _scratchRoot = Path.Combine(Application.temporaryCachePath, "feedback61_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratchRoot);
            UnityResourceLoader.RootDirOverrideForTests = _scratchRoot;
            _writtenResources.Clear();
        }

        [TearDown]
        public void FixtureTearDown()
        {
            UnityResourceLoader.RootDirOverrideForTests = null;
            UnityEngine.Object.DestroyImmediate(_cameraGo);
            UnityEngine.Object.DestroyImmediate(RootGo);
            try
            {
                if (Directory.Exists(_scratchRoot))
                {
                    Directory.Delete(_scratchRoot, recursive: true);
                }
            }
            catch (Exception)
            {
                // 临时目录清理失败不应该让测试本身失败，每次 SetUp 都用新的 Guid 子目录。
            }
        }

        // ---------------- 资源命名规则（与 UnityViewFactory.BuildLayerCandidates 同一约定，用例里拼，不写死） ----------------

        /// <summary>逐层剪辑资源 id：身体层（<paramref name="meshRefValue"/> 为 null）
        /// <c>sprite_anim.&lt;剪辑名&gt;__&lt;方向&gt;__&lt;层名&gt;</c>；装备层
        /// <c>sprite_anim.&lt;去前缀 mesh&gt;__&lt;剪辑名&gt;__&lt;方向&gt;__&lt;层名&gt;</c>。
        /// <paramref name="clipName"/> 是 anim_set 里 resource_ref 去掉 <c>sprite_anim.</c> 前缀后的名字。</summary>
        protected static Id LayerResourceId(string? meshRefValue, string clipName, string dir, string layerName)
        {
            var meshPart = meshRefValue == null ? string.Empty : StripCategory(meshRefValue) + "__";
            return new Id($"sprite_anim.{meshPart}{clipName}__{dir}__{layerName}");
        }

        protected static string StripCategory(string idValue)
        {
            var dot = idValue.IndexOf('.');
            return dot < 0 ? idValue : idValue.Substring(dot + 1);
        }

        protected static string ResourceRefOf(string clipName) => "sprite_anim." + clipName;

        protected static string DefaultClipIdValue(string displayMapIdValue, string stateKey) =>
            $"anim.default.{displayMapIdValue}.{stateKey}";

        // ---------------- 资源写盘 / 预热 / 冷加载泵 ----------------

        /// <summary>写一份最小两帧序列帧资源（atlas.png + frames.json，fps=4），按 EffectFramesDocument 结构。</summary>
        protected void WriteEffectResource(Id resourceId, Color frame0Color, Color frame1Color)
        {
            var dir = Path.Combine(_scratchRoot, AssetRefConventions.SpriteAnimDir(resourceId).Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);

            var atlas = new Texture2D(8, 4, TextureFormat.RGBA32, false);
            var pixels = new Color[8 * 4];
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    pixels[y * 8 + x] = frame0Color;
                    pixels[y * 8 + x + 4] = frame1Color;
                }
            }
            atlas.SetPixels(pixels);
            atlas.Apply();
            File.WriteAllBytes(Path.Combine(dir, "atlas.png"), UnityEngine.ImageConversion.EncodeToPNG(atlas));
            UnityEngine.Object.DestroyImmediate(atlas);

            const string framesJson = "{\"fps\":4,\"loop\":true,\"frames\":[" +
                "{\"x\":0,\"y\":0,\"w\":4,\"h\":4,\"duration\":0.25}," +
                "{\"x\":4,\"y\":0,\"w\":4,\"h\":4,\"duration\":0.25}]}";
            File.WriteAllText(Path.Combine(dir, "frames.json"), framesJson);
            _writtenResources.Add(resourceId);
        }

        /// <summary>同步把 <paramref name="resourceId"/> 加载进缓存（热路径预热）。</summary>
        protected IEnumerator WarmEffectCache(Id resourceId)
        {
            var done = false;
            Loader.LoadAsync(resourceId, ResourceKind.Effect, (_, success) =>
            {
                Assert.IsTrue(success, $"测试资源 \"{resourceId}\" 应当能被成功加载（先写盘再加载）");
                done = true;
            });

            var deadline = Time.realtimeSinceStartup + 5f;
            while (!done && Time.realtimeSinceStartup < deadline)
            {
                Loader.Tick();
                yield return null;
            }
            Assert.IsTrue(done, $"测试资源 \"{resourceId}\" 预热加载超时");
        }

        /// <summary>推动加载器（夹具用的是独立 UnityResourceLoader 实例，没有宿主每帧 Tick，不调用就不会有
        /// 任何异步加载完成——这正是构造"冷加载尚未完成"状态的手段），直到全部 <paramref name="ids"/> 进缓存。</summary>
        protected IEnumerator PumpUntilCached(params Id[] ids)
        {
            var deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var all = true;
                for (var i = 0; i < ids.Length; i++)
                {
                    if (!Loader.TryGetEffect(ids[i], out _))
                    {
                        all = false;
                    }
                }
                if (all)
                {
                    yield break;
                }
                Loader.Tick();
                yield return null;
            }
            Assert.Fail("冷加载资源应当能在合理时间内异步加载完成：" + string.Join(", ", ids));
        }

        /// <summary>再推动若干帧加载器（让在途的整身占位升级/失败回调等收尾），不依赖具体资源。</summary>
        protected IEnumerator PumpFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Loader.Tick();
                yield return null;
            }
        }

        // ---------------- 夹具装配 ----------------

        protected sealed class Fx
        {
            public IEventBus Bus = null!;
            public UnityViewFactory Factory = null!;
            public UnitySpriteView View = null!;
            public Id EntityId;
            public UnityFrameAnimPlayer Player = null!;
            public string DisplayMapIdValue = null!;
            public Dictionary<Id, EquipVisualDef> EquipCatalog = null!;
            public EquipmentVisualSource EquipSource = null!;

            public Id DefaultClipId(string stateKey) => new Id(DefaultClipIdValue(DisplayMapIdValue, stateKey));
        }

        /// <summary>造一个 8 方向、paperdoll_layers:["body"] 的生物外形，<paramref name="clips"/> 是
        /// anim_set 声明的 "剪辑键 -> resource_ref"；<paramref name="weaponStyleAutoAttack"/> 非空时同时登记
        /// 一条 display.weapon_style（auto_attack_anim）并注入 weaponStyleSource；
        /// <paramref name="configureFactory"/> 在 CreateView 之前调用（供用例在视图创建前设置
        /// 工厂上的开关）。视图创建后朝向 side_r（raw facing=0.0）。</summary>
        protected Fx BuildFixture(
            string suffix, IReadOnlyDictionary<string, string> clips, Id? weaponStyleAutoAttack = null,
            Action<UnityViewFactory>? configureFactory = null)
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var bus = new EventBus(EventCatalog.FromDefinitions(definitions), new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var displayMapIdValue = "display.map.test_0111_" + suffix;
            var animSetIdValue = "display.anim_set.test_0111_" + suffix;
            var weaponStyleIdValue = "display.weapon_style.test_0111_" + suffix;

            var sprite = new SpriteInfo(spriteSetId: "sprite.creature.test_0111_" + suffix, directionCount: 8, paperdollLayers: new[] { "body" });
            var info = new DisplayInfo(
                id: new Id(displayMapIdValue),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.test_0111_" + suffix),
                kind: DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.None, sortOffset: 0.0, weaponStyleRef: null,
                sprite: sprite, model: null);

            var displayInfoRegistry = new FakeDisplayInfoRegistryForAnim();
            displayInfoRegistry.Add(info);

            var dataRegistry = new FakeAnimSetAndWeaponStyleRegistry();
            var clipsJson = new System.Text.StringBuilder();
            foreach (var kv in clips)
            {
                if (clipsJson.Length > 0)
                {
                    clipsJson.Append(',');
                }
                clipsJson.Append('"').Append(kv.Key).Append("\":{\"resource_ref\":\"").Append(kv.Value).Append("\"}");
            }
            var animSetJson = "{\"id\":\"" + animSetIdValue + "\",\"clips\":{" + clipsJson + "}}";
            var animSetSchema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            dataRegistry.Add("display.anim_set", new DataRecord(animSetSchema, animSetIdValue, new Id(animSetIdValue), (JsonObject)JsonReader.Parse(animSetJson)));

            global::Presentation.VfxSfx.Contracts.IWeaponStyleSource? weaponSource = null;
            if (weaponStyleAutoAttack.HasValue)
            {
                var weaponStyleJson = "{\"id\":\"" + weaponStyleIdValue + "\",\"auto_attack_anim\":\"" + weaponStyleAutoAttack.Value.Value + "\"}";
                var weaponStyleSchema = new TableSchema("display.weapon_style", "id", 1, Array.Empty<FieldSchema>());
                dataRegistry.Add("display.weapon_style", new DataRecord(weaponStyleSchema, weaponStyleIdValue, new Id(weaponStyleIdValue), (JsonObject)JsonReader.Parse(weaponStyleJson)));
                weaponSource = new FixedWeaponStyleSource(new Id(weaponStyleIdValue));
            }

            var equipCatalog = new Dictionary<Id, EquipVisualDef>();
            var equipSource = new EquipmentVisualSource(bus, equipCatalog);

            var entityId = new Id("unit.test_0111_" + suffix + "_" + Guid.NewGuid().ToString("N"));
            var factory = new UnityViewFactory(
                Renderer, new RenderConventionHost(), displayInfoRegistry, Loader,
                bus: bus, dataRegistry: dataRegistry,
                weaponStyleSource: weaponSource,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);
            configureFactory?.Invoke(factory);

            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            view.Bind(entityId);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);

            var root = Renderer.GetSpriteRoot(view.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");
            Isolate();

            return new Fx
            {
                Bus = bus, Factory = factory, View = view, EntityId = entityId, Player = player!,
                DisplayMapIdValue = displayMapIdValue, EquipCatalog = equipCatalog, EquipSource = equipSource,
            };
        }

        /// <summary>把夹具根物体整棵挂到专属隔离层（装备/换向会新建子物体，创建后需要再调用一次）。</summary>
        protected void Isolate() => SetLayerRecursively(RootGo.transform, IsolationLayer);

        private static void SetLayerRecursively(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (var i = 0; i < t.childCount; i++)
            {
                SetLayerRecursively(t.GetChild(i), layer);
            }
        }

        /// <summary>登记一件落在 <paramref name="slotValue"/> 槽位、mesh 为 <paramref name="meshRefValue"/> 的装备并穿上。</summary>
        protected void EquipMesh(Fx fx, string tag, string slotValue, string meshRefValue)
        {
            var slotId = new Id(slotValue);
            var itemTemplateId = new Id("item.test_0111_" + tag);
            var itemInstanceId = new Id("item_instance.test_0111_" + tag + "_1");
            fx.EquipCatalog[itemTemplateId] = new EquipVisualDef(
                new Id("display.equip_visual.test_0111_" + tag), itemTemplateId, EquipVisualMode.SlotMesh,
                slotId: slotId, meshRef: new Id(meshRefValue), socketId: null, modelRef: null);

            var equipEvt = new ItemEquippedEvent(fx.EntityId, itemInstanceId, slotId);
            fx.Bus.PublishImmediate(new ItemAddedEvent(fx.EntityId, itemInstanceId, itemTemplateId, count: 1));
            fx.Bus.PublishImmediate(equipEvt);
            fx.View.OnEvent(equipEvt);
            Isolate();
        }

        // ---------------- 读数 ----------------

        private static Transform? FindChild(Transform root, string name)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }
            }
            return null;
        }

        /// <summary>按当前层名列表定位 <c>Layer_&lt;下标&gt;</c> 子物体的 SpriteRenderer；层不存在返回 null。</summary>
        protected SpriteRenderer? FindLayerRenderer(Fx fx, string layerName)
        {
            var layersRoot = Renderer.GetLayersRoot(fx.View.EngineHandle);
            var names = Renderer.GetLayerNames(fx.View.EngineHandle);
            if (layersRoot == null || names == null)
            {
                return null;
            }
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], layerName, StringComparison.Ordinal))
                {
                    var child = FindChild(layersRoot, $"Layer_{i}");
                    return child == null ? null : child.GetComponent<SpriteRenderer>();
                }
            }
            return null;
        }

        /// <summary>该层此刻实际应用的 Sprite 属于哪个已加载资源（"精灵实例实际应用了什么"）：遍历本用例写过盘且
        /// 已进缓存的全部资源，找帧集里含这个 Sprite 实例的那个；找不到（静态层图/未命中）返回
        /// "(static)"，层不存在返回 "(no layer)"。</summary>
        protected string AppliedResource(Fx fx, string layerName)
        {
            var layerRenderer = FindLayerRenderer(fx, layerName);
            if (layerRenderer == null)
            {
                return "(no layer)";
            }

            var applied = layerRenderer.sprite;
            for (var i = 0; i < _writtenResources.Count; i++)
            {
                if (!Loader.TryGetEffect(_writtenResources[i], out var effect))
                {
                    continue;
                }
                for (var f = 0; f < effect.Frames.Length; f++)
                {
                    if (effect.Frames[f].Sprite == applied)
                    {
                        return _writtenResources[i].Value;
                    }
                }
            }
            return "(static)";
        }

        /// <summary>有界等待：<paramref name="condition"/> 成立或超时都返回（不在此处失败），让调用方用
        /// Assert.AreEqual 给出"期望/实际"两个实测值（复现用例红的实测值就是这样读到的）。</summary>
        protected static IEnumerator WaitBounded(Func<bool> condition, float timeoutSeconds = 3f)
        {
            var elapsed = 0f;
            while (!condition() && elapsed < timeoutSeconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }
    }
}
