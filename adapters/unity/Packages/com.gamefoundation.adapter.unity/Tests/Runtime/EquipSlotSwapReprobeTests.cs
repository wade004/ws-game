#nullable enable
// EquipSlotSwapReprobeTests：消费方反馈第五十三批收口验收——同槽位换装（同层名、不同 mesh_ref）
// 必须触发逐层剪辑重探测。
//
// 根因（1.84.0 冻结提交 1292896e 上核对）：UnityViewFactory.TryAttachPerLayerAnimation 挂的
// view.LayersComposed 处理器此前只投影 SpriteComposedLayer.LayerName 与上一次快照 SetEquals，不等
// 才调用 ReprobeForCompositionChange——同槽位换装（如 paperdoll.item.a -> paperdoll.item.b，都落在
// mainhand 层）前后层名集合逐字节相同，被误判为"没有变化"，逐层剪辑按方向缓存的候选/内容不失效，
// 换装那一刻已在场且之后未发生方向切换的方向档位会一直播放旧装备的帧集。
//
// 根治：判据改投影 (LayerName, ResourceId) 二元组（ResourceId 是 SpriteComposedLayer 上的合成资源
// id，装备层的 ResourceId 由 EquipMeshRef 换算得到，同层名换资源必然导致 ResourceId 变化，见
// UnityViewFactory._composedLayerIdentityByEntity 判断记录）。
//
// 判断记录（夹具/辅助方法逐字节复用 PerLayerClipEquipAndOverrideTests 一贯做法）：本文件只新增一个
// 独立测试类（避免与既有 ADR-0100 验收类耦合），复用同一套"最小 IDataRegistryView/IDisplayInfoRegistry
// 手工夹具 + WriteEffectResource 精确构造两帧序列帧"惯例；FakeAnimSetAndWeaponStyleRegistry 定义在
// PerLayerClipEquipAndOverrideTests.cs（同一 internal 类，同命名空间可见，不重复定义）。
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
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:render")]
    public sealed class EquipSlotSwapReprobeTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;
        private string _scratchRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("EquipSlotSwapReprobeTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);

            _scratchRoot = Path.Combine(Application.temporaryCachePath, "feedback53_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratchRoot);
            UnityResourceLoader.RootDirOverrideForTests = _scratchRoot;
        }

        [TearDown]
        public void TearDown()
        {
            UnityResourceLoader.RootDirOverrideForTests = null;
            UnityEngine.Object.DestroyImmediate(_rootGo);
            try
            {
                if (Directory.Exists(_scratchRoot))
                {
                    Directory.Delete(_scratchRoot, recursive: true);
                }
            }
            catch (Exception)
            {
                // 判断记录：同 PerLayerClipEquipAndOverrideTests.TearDown——临时目录清理失败不应该让
                // 测试本身失败，每次 SetUp 都用新的 Guid 子目录，不会互相冲突。
            }
        }

        /// <summary>逐字节拷贝自 PerLayerClipEquipAndOverrideTests.WriteEffectResource：按
        /// EffectFramesDocument 的精确 JSON 结构写出一份最小两帧序列帧资源。</summary>
        private void WriteEffectResource(Id resourceId, Color frame0Color, Color frame1Color)
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
        }

        /// <summary>逐字节拷贝自 PerLayerClipEquipAndOverrideTests.WarmEffectCache：同步把
        /// <paramref name="resourceId"/> 加载进 <see cref="_resourceLoader"/> 缓存（热路径预热）。</summary>
        private IEnumerator WarmEffectCache(Id resourceId)
        {
            var done = false;
            _resourceLoader.LoadAsync(resourceId, Core.Foundation.EngineAdapter.ResourceKind.Effect, (_, success) =>
            {
                Assert.IsTrue(success, $"测试资源 \"{resourceId}\" 应当能被成功加载（先写盘再加载）");
                done = true;
            });

            var deadline = Time.realtimeSinceStartup + 5f;
            while (!done && Time.realtimeSinceStartup < deadline)
            {
                _resourceLoader.Tick();
                yield return null;
            }
            Assert.IsTrue(done, $"测试资源 \"{resourceId}\" 预热加载超时");
            Assert.IsTrue(_resourceLoader.TryGetEffect(resourceId, out _), $"\"{resourceId}\" 预热后应当已在缓存里");
        }

        /// <summary>逐字节拷贝自 PerLayerClipEquipAndOverrideTests.WaitUntilEffectCached：轮询直到
        /// <paramref name="resourceId"/> 出现在缓存里（冷加载场景专用）。</summary>
        private IEnumerator WaitUntilEffectCached(Id resourceId, float timeoutSeconds = 5f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!_resourceLoader.TryGetEffect(resourceId, out _) && Time.realtimeSinceStartup < deadline)
            {
                _resourceLoader.Tick();
                yield return null;
            }
            Assert.IsTrue(_resourceLoader.TryGetEffect(resourceId, out _), $"冷加载资源 \"{resourceId}\" 应当能在合理时间内异步加载完成");
        }

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

        private static IEnumerator WaitUntilOrFail(Func<bool> condition, string failureMessage, float timeoutSeconds = 5f)
        {
            var elapsed = 0f;
            while (!condition())
            {
                if (elapsed >= timeoutSeconds)
                {
                    Assert.Fail(failureMessage);
                }
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }

        /// <summary>逐字节拷贝自 PerLayerClipEquipAndOverrideTests.FindLayerRenderer：按当前层名列表
        /// 定位 <c>Layer_&lt;下标&gt;</c> 子物体的 <see cref="SpriteRenderer"/>。</summary>
        private SpriteRenderer? FindLayerRenderer(SpriteHandle handle, Transform layersRoot, string layerName)
        {
            var names = _renderer.GetLayerNames(handle);
            if (names == null)
            {
                return null;
            }
            var index = -1;
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], layerName, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                return null;
            }
            var child = FindChild(layersRoot, $"Layer_{index}");
            return child == null ? null : child.GetComponent<SpriteRenderer>();
        }

        /// <summary>同 PerLayerClipEquipAndOverrideTests.BuildFixture 一套最小夹具：8 方向、
        /// paperdoll_layers:["body"]，display.anim_set 只声明 move 一个状态，引用
        /// <paramref name="moveResourceRefValue"/>。不需要武器风格/覆盖剪辑，本文件场景只涉及默认
        /// 状态的逐层剪辑。</summary>
        private (IEventBus Bus, UnityViewFactory Factory, UnitySpriteView View, Id EntityId, UnityFrameAnimPlayer Player,
            Id MoveClipId, Dictionary<Id, EquipVisualDef> Catalog, EquipmentVisualSource EquipSource)
            BuildFixture(string suffix, string moveResourceRefValue)
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var displayMapIdValue = "display.map.test_0053_" + suffix;
            var animSetIdValue = "display.anim_set.test_0053_" + suffix;

            var sprite = new SpriteInfo(spriteSetId: "sprite.creature.test_0053_" + suffix, directionCount: 8, paperdollLayers: new[] { "body" });
            var info = new DisplayInfo(
                id: new Id(displayMapIdValue),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.test_0053_" + suffix),
                kind: DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.None, sortOffset: 0.0, weaponStyleRef: null,
                sprite: sprite, model: null);

            var displayInfoRegistry = new FakeDisplayInfoRegistryForAnim();
            displayInfoRegistry.Add(info);

            var dataRegistry = new FakeAnimSetAndWeaponStyleRegistry();

            var animSetJson = "{\"id\":\"" + animSetIdValue + "\",\"clips\":{\"move\":{\"resource_ref\":\"" + moveResourceRefValue + "\"}}}";
            var animSetRaw = (JsonObject)JsonReader.Parse(animSetJson);
            var animSetSchema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            dataRegistry.Add("display.anim_set", new DataRecord(animSetSchema, animSetIdValue, new Id(animSetIdValue), animSetRaw));

            var equipCatalog = new Dictionary<Id, EquipVisualDef>();
            var equipSource = new EquipmentVisualSource(bus, equipCatalog);

            var entityId = new Id("unit.test_0053_" + suffix + "_" + Guid.NewGuid().ToString("N"));
            var factory = new UnityViewFactory(
                _renderer, new RenderConventionHost(), displayInfoRegistry, _resourceLoader,
                bus: bus, dataRegistry: dataRegistry,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            view.Bind(entityId);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");

            var moveClipId = new Id($"anim.default.{displayMapIdValue}.move");

            return (bus, factory, view, entityId, player!, moveClipId, equipCatalog, equipSource);
        }

        private static void Equip(IEventBus bus, UnitySpriteView view, Id entityId, Id itemTemplateId, Id itemInstanceId, Id slotId)
        {
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, slotId);
            bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, itemTemplateId, count: 1));
            bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);
        }

        /// <summary>消费方反馈第五十三批核心复现：装备 A 落在 mainhand 层，Walk 状态下逐层剪辑正在播放
        /// A 的帧集；不换向，直接换装成同槽位的装备 B（同层名 mainhand，不同 mesh_ref）。根治前
        /// （只投影 LayerName）换装前后层名集合逐字节相同，被误判为"没有变化"，mainhand 层贴图会
        /// 一直停在换装那一刻 B 的静态合成图（或 A 的最后一帧，取决于时序），不会追上 B 的逐层剪辑帧，
        /// 直至下一次真正的方向切换才被纠正——本用例断言换装后 mainhand 应当在合理等待后显示 B 的
        /// 逐层剪辑帧，修复前必然红（见下方核心断言）。body 层全程不受影响。</summary>
        [UnityTest]
        public IEnumerator SameSlotEquipSwap_SameLayerName_DifferentMeshRef_ReprobesToNewGearFrames()
        {
            var bodyRef = new Id("sprite_anim.hero53_walk__side_r__body");
            var mainhandARef = new Id("sprite_anim.m53a__hero53_walk__side_r__mainhand");
            var mainhandBRef = new Id("sprite_anim.m53b__hero53_walk__side_r__mainhand");
            WriteEffectResource(bodyRef, Color.red, Color.green);
            WriteEffectResource(mainhandARef, Color.blue, Color.yellow);
            WriteEffectResource(mainhandBRef, Color.black, Color.white);
            yield return WarmEffectCache(bodyRef);
            yield return WarmEffectCache(mainhandARef);
            yield return WarmEffectCache(mainhandBRef);
            _resourceLoader.TryGetEffect(bodyRef, out var bodyEffect);
            _resourceLoader.TryGetEffect(mainhandBRef, out var mainhandBEffect);

            var fx = BuildFixture("repro", "sprite_anim.hero53_walk");

            var slotId = new Id("slot.mainhand");
            var itemATemplateId = new Id("item.test_0053_a");
            var itemAInstanceId = new Id("item_instance.test_0053_a_1");
            fx.Catalog[itemATemplateId] = new EquipVisualDef(
                new Id("display.equip_visual.test_0053_a"), itemATemplateId, EquipVisualMode.SlotMesh,
                slotId: slotId, meshRef: new Id("mesh.m53a"), socketId: null, modelRef: null);

            var itemBTemplateId = new Id("item.test_0053_b");
            var itemBInstanceId = new Id("item_instance.test_0053_b_1");
            fx.Catalog[itemBTemplateId] = new EquipVisualDef(
                new Id("display.equip_visual.test_0053_b"), itemBTemplateId, EquipVisualMode.SlotMesh,
                slotId: slotId, meshRef: new Id("mesh.m53b"), socketId: null, modelRef: null);

            // 先装备 A，播 Walk——确认前置条件：mainhand 命中 A 的逐层剪辑。
            Equip(fx.Bus, fx.View, fx.EntityId, itemATemplateId, itemAInstanceId, slotId);
            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
            yield return null;

            var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
            Assert.IsNotNull(layersRoot, "应当已经建好 LayersRoot 子物体");
            var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
            Assert.IsNotNull(mainhandRenderer, "装备 A 后 mainhand 应当已经作为装备新增层存在");
            _resourceLoader.TryGetEffect(mainhandARef, out var mainhandAEffect);
            Assert.AreEqual(
                mainhandAEffect.Frames[fx.Player.CurrentFrame % mainhandAEffect.Frames.Length].Sprite, mainhandRenderer!.sprite,
                "前置条件：装备 A 后 mainhand 应当显示 A 的逐层剪辑当前帧");

            // 核心：不换向，直接换成同槽位的装备 B（同层名 mainhand，不同 mesh_ref m53b）。
            Equip(fx.Bus, fx.View, fx.EntityId, itemBTemplateId, itemBInstanceId, slotId);

            var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
            Assert.IsNotNull(bodyRenderer, "body 层不应该受同槽位换装影响，应当继续存在");

            yield return WaitUntilOrFail(
                () => mainhandRenderer!.sprite == mainhandBEffect.Frames[fx.Player.CurrentFrame % mainhandBEffect.Frames.Length].Sprite,
                "消费方反馈第五十三批核心断言：同槽位换装（同层名 mainhand、不同 mesh_ref）后，不换向的" +
                "情况下 mainhand 层应当在合理等待内追上并显示装备 B 的逐层剪辑当前帧——根治前判据只看" +
                "层名集合，换装前后层名集合不变，被误判为“没有变化”，不会触发重探测，该层会一直播放" +
                "装备 A 的旧帧集，本断言即为红→绿分界线",
                timeoutSeconds: 3f);

            Assert.AreEqual(
                bodyEffect.Frames[fx.Player.CurrentFrame % bodyEffect.Frames.Length].Sprite, bodyRenderer!.sprite,
                "body 层不参与本次装备变化，应当继续正常播放身体逐层剪辑，不受同槽位换装影响");

            fx.EquipSource.Dispose();
            fx.View.Destroy();
        }

        /// <summary>消费方反馈第五十三批不变量（4 个分支合一，各用独立实体，同既有 ADR-0100 系列
        /// Invariant 用例一贯惯例）：
        /// ① 换装（A -> B）后换向再换回原方向，仍然显示装备 B 的逐层剪辑（不是掉回 A，也不是停在换向
        ///    前的旧帧）——验证换向重探测与换装重探测两条路径叠加后结果仍然正确。
        /// ② 冷加载：换装到 B 那一刻 B 的逐层剪辑帧集资源尚未加载完成，加载完成后当前方向应当立即
        ///    显示 B 的帧（AGENTS.md 冷/热路径一致性不变量）。
        /// ③ 层名集合确实变化（新增 waist 层）的既有路径行为不受影响——新增层参与逐层剪辑，回归既有
        ///    ADR-0100 决策 1 行为（阳性对照）。
        /// ④ 再次换回装备 A（B -> A，同槽位再次换资源）同样触发重探测，显示 A 的逐层剪辑帧。
        /// </summary>
        [UnityTest]
        public IEnumerator Invariant_DirectionRoundTrip_ColdLoad_NewLayerNameChange_SwapBackToOriginal()
        {
            // ---------- ① 换装后换向再换回，仍是装备 B ----------
            {
                var bodyRef = new Id("sprite_anim.hero53a_walk__side_r__body");
                var mainhandARef = new Id("sprite_anim.m53aa__hero53a_walk__side_r__mainhand");
                var mainhandBRef = new Id("sprite_anim.m53ab__hero53a_walk__side_r__mainhand");
                WriteEffectResource(bodyRef, Color.red, Color.green);
                WriteEffectResource(mainhandARef, Color.blue, Color.yellow);
                WriteEffectResource(mainhandBRef, Color.black, Color.white);
                yield return WarmEffectCache(bodyRef);
                yield return WarmEffectCache(mainhandARef);
                yield return WarmEffectCache(mainhandBRef);
                _resourceLoader.TryGetEffect(mainhandBRef, out var mainhandBEffect);

                var fx = BuildFixture("inv_a", "sprite_anim.hero53a_walk");
                var slotId = new Id("slot.mainhand");
                var itemATemplateId = new Id("item.test_0053_inv_a_a");
                var itemAInstanceId = new Id("item_instance.test_0053_inv_a_a_1");
                fx.Catalog[itemATemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0053_inv_a_a"), itemATemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m53aa"), socketId: null, modelRef: null);
                var itemBTemplateId = new Id("item.test_0053_inv_a_b");
                var itemBInstanceId = new Id("item_instance.test_0053_inv_a_b_1");
                fx.Catalog[itemBTemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0053_inv_a_b"), itemBTemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m53ab"), socketId: null, modelRef: null);

                Equip(fx.Bus, fx.View, fx.EntityId, itemATemplateId, itemAInstanceId, slotId);
                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;
                Equip(fx.Bus, fx.View, fx.EntityId, itemBTemplateId, itemBInstanceId, slotId);

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
                yield return WaitUntilOrFail(
                    () => mainhandRenderer!.sprite == mainhandBEffect.Frames[fx.Player.CurrentFrame % mainhandBEffect.Frames.Length].Sprite,
                    "前置条件：换装到 B 后应当显示 B 的逐层剪辑帧", timeoutSeconds: 3f);

                // 换向 front，再换回 side_r（raw facing=0.0）。
                fx.View.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI / 2.0, 8), height: 0.0);
                yield return null;
                fx.View.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);
                yield return null;

                yield return WaitUntilOrFail(
                    () => mainhandRenderer!.sprite == mainhandBEffect.Frames[fx.Player.CurrentFrame % mainhandBEffect.Frames.Length].Sprite,
                    "不变量①核心断言：换向再换回原方向后，mainhand 仍应显示装备 B 的逐层剪辑帧（换向" +
                    "重探测与换装重探测叠加后结果仍然正确，不掉回装备 A、也不停留在换向前的旧帧）",
                    timeoutSeconds: 3f);

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ② 冷加载：换装到 B 那一刻帧集尚未加载完成 ----------
            {
                var bodyRef = new Id("sprite_anim.hero53b_walk__side_r__body");
                var mainhandARef = new Id("sprite_anim.m53ba__hero53b_walk__side_r__mainhand");
                WriteEffectResource(bodyRef, Color.red, Color.green);
                WriteEffectResource(mainhandARef, Color.blue, Color.yellow);
                yield return WarmEffectCache(bodyRef);
                yield return WarmEffectCache(mainhandARef);

                var fx = BuildFixture("inv_b", "sprite_anim.hero53b_walk");
                var slotId = new Id("slot.mainhand");
                var itemATemplateId = new Id("item.test_0053_inv_b_a");
                var itemAInstanceId = new Id("item_instance.test_0053_inv_b_a_1");
                fx.Catalog[itemATemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0053_inv_b_a"), itemATemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m53ba"), socketId: null, modelRef: null);

                Equip(fx.Bus, fx.View, fx.EntityId, itemATemplateId, itemAInstanceId, slotId);
                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;

                // 冷加载核心：装备 B 的逐层剪辑帧集写盘但刻意不预热，换装事件触发的
                // ReprobeForCompositionChange 会自己发起真正的异步 LoadAsync。
                var mainhandBRef = new Id("sprite_anim.m53bb__hero53b_walk__side_r__mainhand");
                WriteEffectResource(mainhandBRef, Color.black, Color.white);

                var itemBTemplateId = new Id("item.test_0053_inv_b_b");
                var itemBInstanceId = new Id("item_instance.test_0053_inv_b_b_1");
                fx.Catalog[itemBTemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0053_inv_b_b"), itemBTemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m53bb"), socketId: null, modelRef: null);
                Equip(fx.Bus, fx.View, fx.EntityId, itemBTemplateId, itemBInstanceId, slotId);

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
                Assert.IsNotNull(mainhandRenderer, "换装后 mainhand 层应当已经存在（尚未完成冷加载，暂时是静态合成图或 A 的旧帧）");

                yield return WaitUntilEffectCached(mainhandBRef);
                _resourceLoader.TryGetEffect(mainhandBRef, out var mainhandBEffect);

                yield return WaitUntilOrFail(
                    () => mainhandRenderer!.sprite == mainhandBEffect.Frames[fx.Player.CurrentFrame % mainhandBEffect.Frames.Length].Sprite,
                    "不变量②核心断言（AGENTS.md 冷/热路径一致性）：装备 B 的逐层剪辑帧集冷加载完成后，" +
                    "当前方向应当立即显示 B 的帧，与预热完成后的热路径最终视觉状态一致",
                    timeoutSeconds: 3f);

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ③ 阳性对照：层名集合真变化（新增 waist 层）既有路径不受影响 ----------
            {
                var bodyRef = new Id("sprite_anim.hero53c_walk__side_r__body");
                var waistRef = new Id("sprite_anim.m53c__hero53c_walk__side_r__waist");
                WriteEffectResource(bodyRef, Color.red, Color.green);
                WriteEffectResource(waistRef, Color.blue, Color.yellow);
                yield return WarmEffectCache(bodyRef);
                yield return WarmEffectCache(waistRef);
                _resourceLoader.TryGetEffect(waistRef, out var waistEffect);

                var fx = BuildFixture("inv_c", "sprite_anim.hero53c_walk");
                var slotId = new Id("slot.waist");
                var itemTemplateId = new Id("item.test_0053_inv_c");
                var itemInstanceId = new Id("item_instance.test_0053_inv_c_1");
                fx.Catalog[itemTemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0053_inv_c"), itemTemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m53c"), socketId: null, modelRef: null);

                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;
                Equip(fx.Bus, fx.View, fx.EntityId, itemTemplateId, itemInstanceId, slotId);

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var waistRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "waist");
                Assert.IsNotNull(waistRenderer, "阳性对照：新增 waist 层应当已经出现在当前层列表");

                yield return WaitUntilOrFail(
                    () => waistRenderer!.sprite == waistEffect.Frames[fx.Player.CurrentFrame % waistEffect.Frames.Length].Sprite,
                    "阳性对照核心断言：新增层名（既有路径，不涉及本次判据变化）应当仍然参与逐层剪辑探测，" +
                    "显示 waist 逐层剪辑当前帧——既有 ADR-0100 决策 1 行为不受本次判据扩展影响",
                    timeoutSeconds: 3f);

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ④ 再次换回装备 A（B -> A） ----------
            {
                var bodyRef = new Id("sprite_anim.hero53d_walk__side_r__body");
                var mainhandARef = new Id("sprite_anim.m53da__hero53d_walk__side_r__mainhand");
                var mainhandBRef = new Id("sprite_anim.m53db__hero53d_walk__side_r__mainhand");
                WriteEffectResource(bodyRef, Color.red, Color.green);
                WriteEffectResource(mainhandARef, Color.blue, Color.yellow);
                WriteEffectResource(mainhandBRef, Color.black, Color.white);
                yield return WarmEffectCache(bodyRef);
                yield return WarmEffectCache(mainhandARef);
                yield return WarmEffectCache(mainhandBRef);
                _resourceLoader.TryGetEffect(mainhandARef, out var mainhandAEffect);

                var fx = BuildFixture("inv_d", "sprite_anim.hero53d_walk");
                var slotId = new Id("slot.mainhand");
                var itemATemplateId = new Id("item.test_0053_inv_d_a");
                var itemAInstanceId = new Id("item_instance.test_0053_inv_d_a_1");
                fx.Catalog[itemATemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0053_inv_d_a"), itemATemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m53da"), socketId: null, modelRef: null);
                var itemBTemplateId = new Id("item.test_0053_inv_d_b");
                var itemBInstanceId = new Id("item_instance.test_0053_inv_d_b_1");
                fx.Catalog[itemBTemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0053_inv_d_b"), itemBTemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m53db"), socketId: null, modelRef: null);

                Equip(fx.Bus, fx.View, fx.EntityId, itemATemplateId, itemAInstanceId, slotId);
                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;
                Equip(fx.Bus, fx.View, fx.EntityId, itemBTemplateId, itemBInstanceId, slotId);
                yield return null;

                // 再换回 A：同槽位第二次换资源，同样应当触发重探测。
                Equip(fx.Bus, fx.View, fx.EntityId, itemATemplateId, itemAInstanceId, slotId);

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
                yield return WaitUntilOrFail(
                    () => mainhandRenderer!.sprite == mainhandAEffect.Frames[fx.Player.CurrentFrame % mainhandAEffect.Frames.Length].Sprite,
                    "不变量④核心断言：B -> A 换回同样应当触发重探测，mainhand 应当显示装备 A 的逐层剪辑帧",
                    timeoutSeconds: 3f);

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }
        }
    }
}
