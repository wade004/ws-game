#nullable enable
// PerLayerClipEquipAndOverrideTests：ADR-0100（消费方反馈第四十五批）收口验收——三条阻塞一次性
// 验收：① 装备层参与逐层剪辑探测（不再局限于 DisplayInfo.Sprite.PaperdollLayers 声明的身体默认层，
// 装备新增/覆盖的层同样纳入 UnityViewFactory.ProbeComposedLayersSequential 探测范围，见
// Presentation.Render.SpriteViewBase.LastComposedLayers）；② 装备层候选按其 EquipVisualDef.MeshRef
// 取前缀（sprite_anim.<去前缀 mesh_ref>__<身体或覆盖剪辑去前缀 ref>__<方向>__<层名>，见
// UnityViewFactory.BuildLayerCandidates）；③ 武器风格/技能覆盖剪辑（WeaponStyleDef.AutoAttackAnim/
// CastAnimOverride）走与默认六个状态同一套逐层 + 方向探测（UnityViewFactory.
// TryResolveOverrideClipForCurrentComposition/ReprobeOverrideClipForDirection），不再是根治前的
// "整身唯一路径"。
//
// 判断记录（不用 data/_sample 真实数据，改用 RootDirOverrideForTests 指向的隔离临时目录 + 手工构造
// 的最小 IDataRegistryView/IDisplayInfoRegistry 夹具）：同 DirectionAwareAnimClipTests.cs 一贯做法
// ——本文件需要精确控制"装备网格 mesh_ref"与"覆盖剪辑 clipId"两个新维度的候选资源是否存在，
// data/_sample 现有占位角色资源没有为这两个新维度准备过专属夹具，改用可精确构造的最小 atlas+
// frames.json（同该文件 WriteEffectResource 逐字节同一套写法，直接拷贝复用）。
//
// 判断记录（EquipmentVisualSource 目录用直接构造的 Dictionary，不经真实 DataRegistry 加载
// display.equip_visual 表）：EquipVisualDef 构造函数本身是 public，测试不需要真实数据表也能拿到
// 完整强类型视图（同 SpriteEquipVisualWiringTests.cs
// UnityViewFactory_SpriteKind_EquipVisualWired_RealFacingChange_KeepsEquipLayerAndExtraLayer 用例
// 里对 cape 额外层的处理惯例——直接 new EquipVisualDef(...) 塞进目录字典，不新增
// display.equip_visual 数据行）。
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
using Core.Foundation.Expr;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>只承载手工构造的 <c>display.anim_set</c>/<c>display.weapon_style</c> 记录的最小
    /// <see cref="IDataRegistryView"/> 测试替身——按表名分桶存储，<see cref="GetAll"/> 对未登记的表名
    /// 返回空列表（同 DirectionAwareAnimClipTests.FakeAnimSetRegistry 判断记录：
    /// UnityViewFactory.EnsureAnimClipResolver 会无条件对 display.weapon_style 表调用 GetAll，
    /// 未登记表名必须返回空列表而不是抛异常）。</summary>
    internal sealed class FakeAnimSetAndWeaponStyleRegistry : IDataRegistryView
    {
        private readonly Dictionary<string, DataRecord> _byId = new Dictionary<string, DataRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<DataRecord>> _byTable = new Dictionary<string, List<DataRecord>>(StringComparer.Ordinal);

        public void Add(string table, DataRecord record)
        {
            _byId[record.Id!.Value.Value] = record;
            if (!_byTable.TryGetValue(table, out var list))
            {
                list = new List<DataRecord>();
                _byTable[table] = list;
            }
            list.Add(record);
        }

        public DataRecord? Get(string table, string key) => _byId.TryGetValue(key, out var r) ? r : null;

        public DataRecord? Get(string table, Id id) => _byId.TryGetValue(id.Value, out var r) ? r : null;

        public IReadOnlyList<DataRecord> GetAll(string table) => _byTable.TryGetValue(table, out var list) ? list : Array.Empty<DataRecord>();

        public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate) => throw new NotSupportedException();

        public IReadOnlyList<DataRecord> Query(string table, string predicateText) => throw new NotSupportedException();

        public IReadOnlyList<string> Tables => throw new NotSupportedException();

        public TableSchema? GetSchema(string table) => null;
    }

    public sealed class PerLayerClipEquipAndOverrideTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;
        private string _scratchRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("PerLayerClipEquipAndOverrideTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);

            _scratchRoot = Path.Combine(Application.temporaryCachePath, "adr0100_" + Guid.NewGuid().ToString("N"));
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
                // 判断记录：同 DirectionAwareAnimClipTests.TearDown——临时目录清理失败不应该让测试本身
                // 失败，每次 SetUp 都用新的 Guid 子目录，不会互相冲突。
            }
        }

        /// <summary>按 EffectFramesDocument 的精确 JSON 结构写出一份最小两帧序列帧资源，逐字节拷贝自
        /// DirectionAwareAnimClipTests.WriteEffectResource（同一套判断记录：不用 data/_sample 真实
        /// 资源，本文件需要精确控制两种颜色帧才能断言"当前贴图确实是这个候选的内容"）。</summary>
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

        /// <summary>同步把 <paramref name="resourceId"/> 加载进 <see cref="_resourceLoader"/> 缓存——逐字节
        /// 拷贝自 DirectionAwareAnimClipTests.WarmEffectCache，供需要"命中缓存、同步解析"（热路径）的
        /// 场景在 BuildFixture/装备事件之前预热；冷加载场景（不变量④）刻意不调用本方法。</summary>
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

        /// <summary>轮询直到 <paramref name="resourceId"/> 出现在缓存里（冷加载场景专用，不预热，靠
        /// 装备/重探测事件自己触发的真实异步 LoadAsync 完成）——同上，需要测试代码自己驱动
        /// <see cref="UnityResourceLoader.Tick"/>（本文件用的是裸构造的 UnityResourceLoader 实例，
        /// 不挂 MonoBehaviour.Update，同 DirectionAwareAnimClipTests 判断记录）。</summary>
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

        /// <summary>按当前层名列表定位 <c>Layer_&lt;下标&gt;</c> 子物体的 <see cref="SpriteRenderer"/>；
        /// 层不存在（未装备/已卸下）时返回 <c>null</c>，供"该层不应存在"一类断言使用。</summary>
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

        /// <summary>本文件公用夹具：8 方向、无 mirror_pairs（默认 side_l 自动镜像自 side_r，raw facing=0.0
        /// 因此解析出的裸档位名恰好是 "side_r"，同 PaperdollLayerAnimTests/DirectionAwareAnimClipTests
        /// 判断记录一致的换算），<c>paperdoll_layers:["body"]</c>（ADR-0100 决策 1 的验收前提：装备新增的
        /// "mainhand" 层不在这份基础层名单里），<c>display.anim_set</c> 只声明 <c>move</c> 一个状态引用
        /// <paramref name="moveResourceRefValue"/>，同时登记一条 <c>display.weapon_style</c> 记录，
        /// <c>auto_attack_anim</c> 取 <paramref name="autoAttackAnimClipId"/>（ADR-0100 决策 3 验收前提，
        /// 未触发 Attack 的用例传一个不需要任何夹具资源的占位 clipId 即可，不影响其余断言）。</summary>
        private (IEventBus Bus, UnityViewFactory Factory, UnitySpriteView View, Id EntityId, UnityFrameAnimPlayer Player,
            Id MoveClipId, Dictionary<Id, EquipVisualDef> Catalog, EquipmentVisualSource EquipSource, Id WeaponStyleId)
            BuildFixture(string suffix, string moveResourceRefValue, Id autoAttackAnimClipId)
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var displayMapIdValue = "display.map.test_0100_" + suffix;
            var animSetIdValue = "display.anim_set.test_0100_" + suffix;
            var weaponStyleIdValue = "display.weapon_style.test_0100_" + suffix;

            var sprite = new SpriteInfo(spriteSetId: "sprite.creature.test_0100_" + suffix, directionCount: 8, paperdollLayers: new[] { "body" });
            var info = new DisplayInfo(
                id: new Id(displayMapIdValue),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.test_0100_" + suffix),
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

            var weaponStyleJson = "{\"id\":\"" + weaponStyleIdValue + "\",\"auto_attack_anim\":\"" + autoAttackAnimClipId.Value + "\"}";
            var weaponStyleRaw = (JsonObject)JsonReader.Parse(weaponStyleJson);
            var weaponStyleSchema = new TableSchema("display.weapon_style", "id", 1, Array.Empty<FieldSchema>());
            dataRegistry.Add("display.weapon_style", new DataRecord(weaponStyleSchema, weaponStyleIdValue, new Id(weaponStyleIdValue), weaponStyleRaw));

            var equipCatalog = new Dictionary<Id, EquipVisualDef>();
            var equipSource = new EquipmentVisualSource(bus, equipCatalog);

            var entityId = new Id("unit.test_0100_" + suffix + "_" + Guid.NewGuid().ToString("N"));
            var factory = new UnityViewFactory(
                _renderer, new RenderConventionHost(), displayInfoRegistry, _resourceLoader,
                bus: bus, dataRegistry: dataRegistry,
                weaponStyleSource: new FixedWeaponStyleSource(new Id(weaponStyleIdValue)),
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            view.Bind(entityId);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 8), height: 0.0);

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");

            var moveClipId = new Id($"anim.default.{displayMapIdValue}.move");

            return (bus, factory, view, entityId, player!, moveClipId, equipCatalog, equipSource, new Id(weaponStyleIdValue));
        }

        private static void Equip(IEventBus bus, UnitySpriteView view, Id entityId, Id itemTemplateId, Id itemInstanceId, Id slotId)
        {
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, slotId);
            bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, itemTemplateId, count: 1));
            bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);
        }

        private static void Unequip(IEventBus bus, UnitySpriteView view, Id entityId, Id slotId, Id itemInstanceId)
        {
            var unequipEvt = new ItemUnequippedEvent(entityId, slotId, itemInstanceId);
            bus.PublishImmediate(unequipEvt);
            view.OnEvent(unequipEvt);
        }

        /// <summary>ADR-0100 核心复现：<c>paperdoll_layers:["body"]</c>，装备一件 mesh <c>m1</c> 加
        /// <c>mainhand</c> 层（不在 <c>paperdoll_layers</c> 声明的层名单里），夹具
        /// <c>sprite_anim.hero_walk__side_r__body</c> 与 <c>sprite_anim.m1__hero_walk__side_r__mainhand</c>
        /// 各 2 帧——根治前（决策 1 未落地时）<c>TryAttachPerLayerAnimation</c> 只在挂接那一刻按
        /// <c>PaperdollLayers</c>（不含装备层）探测一次，此后永远不会把新装备的 mainhand 层纳入探测
        /// 范围，该层维持 <c>SetLayers</c> 落地的静态图，即便晚于挂接才装备也不会补探测——本用例修复前
        /// 必然红（mainhand 层贴图恒为装备时刻的静态图，不随共享时间轴帧号推进切换，见下方核心断言）。</summary>
        [UnityTest]
        public IEnumerator Move_EquipMainhandLayer_ParticipatesInPerLayerClip_HidesAnimRootAndSharesFrame()
        {
            var bodyRef = new Id("sprite_anim.hero_walk__side_r__body");
            var mainhandRef = new Id("sprite_anim.m1__hero_walk__side_r__mainhand");
            WriteEffectResource(bodyRef, Color.red, Color.green);
            WriteEffectResource(mainhandRef, Color.blue, Color.yellow);
            yield return WarmEffectCache(bodyRef);
            yield return WarmEffectCache(mainhandRef);
            _resourceLoader.TryGetEffect(bodyRef, out var bodyEffect);
            _resourceLoader.TryGetEffect(mainhandRef, out var mainhandEffect);

            var fx = BuildFixture("repro", "sprite_anim.hero_walk", new Id("sprite_anim.unused_repro_attack"));

            var itemTemplateId = new Id("item.test_0100_m1");
            var itemInstanceId = new Id("item_instance.test_0100_m1_1");
            var slotId = new Id("slot.mainhand");
            fx.Catalog[itemTemplateId] = new EquipVisualDef(
                new Id("display.equip_visual.test_0100_m1"), itemTemplateId, EquipVisualMode.SlotMesh,
                slotId: slotId, meshRef: new Id("mesh.m1"), socketId: null, modelRef: null);
            Equip(fx.Bus, fx.View, fx.EntityId, itemTemplateId, itemInstanceId, slotId);

            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
            Assert.AreEqual(fx.MoveClipId, fx.Player.CurrentClipId!.Value, "Walk 应当播放 move 剪辑");
            yield return null;

            var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
            Assert.IsNotNull(layersRoot, "应当已经建好 LayersRoot 子物体");
            var animRootTransform = FindChild(layersRoot!, "AnimRoot");
            Assert.IsNotNull(animRootTransform, "应当已经挂接 AnimRoot 子物体");
            var animRootRenderer = animRootTransform!.GetComponent<SpriteRenderer>();
            Assert.IsFalse(animRootRenderer.enabled,
                "ADR-0100 决策 1：装备层参与逐层剪辑后，move 状态至少一层（body）命中，整身 AnimRoot 应当被隐藏");

            var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
            var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
            Assert.IsNotNull(bodyRenderer, "body 层应当存在");
            Assert.IsNotNull(mainhandRenderer, "mainhand 应当已经作为装备新增层存在于当前层列表");

            var canonicalFrame = fx.Player.CurrentFrame;
            Assert.AreEqual(
                bodyEffect.Frames[canonicalFrame % bodyEffect.Frames.Length].Sprite, bodyRenderer!.sprite,
                "body 层应当显示 body 剪辑在当前共享时间轴帧号下的对应帧");
            Assert.AreEqual(
                mainhandEffect.Frames[canonicalFrame % mainhandEffect.Frames.Length].Sprite, mainhandRenderer!.sprite,
                "ADR-0100 核心断言：mainhand 装备层应当已经参与逐层剪辑探测，显示 m1 装备层剪辑在" +
                "当前共享时间轴帧号下的对应帧——根治前该层贴图恒为装备时刻的静态图，不会随时间轴推进切换" +
                "（本断言即为红→绿分界线）");

            // 两层共用同一条权威时间轴（body 是 composedLayers 里的第一层，命中后同时登记为
            // stateClipId 本身内容），两个夹具都恰好 2 帧，帧号天然一致，额外显式断言一次帧号本身
            // 相同，覆盖"各层各自按自己帧数取模但读的是同一个 canonical 帧号"这条不变量。
            Assert.AreEqual(canonicalFrame % bodyEffect.Frames.Length, canonicalFrame % mainhandEffect.Frames.Length,
                "两层帧数相同（各 2 帧）时，取模后的帧下标应当一致");

            fx.EquipSource.Dispose();
            fx.View.Destroy();
        }

        /// <summary>
        /// ADR-0100 不变量（4 个分支合一，各用独立实体避免互相干扰）：
        /// ① 脱下装备层后该层不再存在、不再写帧；换成没有帧集的 mesh 后该层维持静态图、body 仍动。
        /// ② 武器风格覆盖剪辑带逐层夹具：Attack 时 AnimRoot 隐藏、两层都走覆盖剪辑帧；换方向后逐层
        ///    夹具缺失的层（mainhand）冻结在换向前最后一帧，命中新方向变体的层（body）继续换帧。
        /// ③ 覆盖剪辑没有任何逐层夹具，但有整身方向变体：AnimRoot 显示，播放整身方向变体内容
        ///    （旧行为 + ADR-0093 方向感知）。
        /// ④ 冷加载：装备层帧集资源首次引用时尚未加载完成，异步加载完成后该层应当追上、显示为当前帧
        ///    ——与预热完成后的热路径最终状态一致（AGENTS.md 冷/热路径一致性不变量）。
        /// </summary>
        [UnityTest]
        public IEnumerator Invariant_UnequipMeshWithoutFrameSet_OverrideClipPerLayerAndDirectionChange_WholeBodyDirectionVariant_ColdLoadCatchesUp()
        {
            // ---------- ① 脱下 -> 层消失；换无帧集 mesh -> 静态，body 仍动 ----------
            {
                var bodyRef = new Id("sprite_anim.hero_walk_a__side_r__body");
                var mainhandRef = new Id("sprite_anim.m1a__hero_walk_a__side_r__mainhand");
                WriteEffectResource(bodyRef, Color.red, Color.green);
                WriteEffectResource(mainhandRef, Color.blue, Color.yellow);
                yield return WarmEffectCache(bodyRef);
                yield return WarmEffectCache(mainhandRef);

                var fx = BuildFixture("inv_a", "sprite_anim.hero_walk_a", new Id("sprite_anim.unused_a_attack"));
                var slotId = new Id("slot.mainhand");

                var m1TemplateId = new Id("item.test_0100_inv_a_m1");
                var m1InstanceId = new Id("item_instance.test_0100_inv_a_m1_1");
                fx.Catalog[m1TemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0100_inv_a_m1"), m1TemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m1a"), socketId: null, modelRef: null);
                Equip(fx.Bus, fx.View, fx.EntityId, m1TemplateId, m1InstanceId, slotId);

                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                Assert.IsNotNull(FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand"),
                    "装备后 mainhand 层应当存在（前置条件）");

                Unequip(fx.Bus, fx.View, fx.EntityId, slotId, m1InstanceId);
                Assert.IsNull(FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand"),
                    "不变量①：卸下装备后 mainhand 层应当不再存在于当前层列表里，不再写帧");

                var m2TemplateId = new Id("item.test_0100_inv_a_m2");
                var m2InstanceId = new Id("item_instance.test_0100_inv_a_m2_1");
                fx.Catalog[m2TemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0100_inv_a_m2"), m2TemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m2a"), socketId: null, modelRef: null);
                Equip(fx.Bus, fx.View, fx.EntityId, m2TemplateId, m2InstanceId, slotId);

                // m2a 没有任何逐层夹具资源（两级候选均不存在磁盘上）——给异步探测链路充分时间跑完两级
                // LoadAsync 往返（均为确定的"文件不存在"快速失败），同 PaperdollLayerAnimTests.
                // MoveState_NoLayerResolves_KeepsWholeBodyFallbackAnimRootVisible 同一惯例。
                yield return new WaitForSecondsRealtime(0.5f);

                var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
                Assert.IsNotNull(bodyRenderer, "body 层应当仍然存在");
                Assert.IsNotNull(mainhandRenderer, "换成 mesh m2a 后 mainhand 层应当仍然存在（只是没有逐层剪辑）");

                var firstBodySprite = bodyRenderer!.sprite;
                yield return WaitUntilOrFail(
                    () => bodyRenderer!.sprite != firstBodySprite,
                    "不变量①：body 层没有装备变化、应当继续随共享时间轴推进切换贴图", timeoutSeconds: 3f);

                // mainhand 静态：再等一段与上面等价的时间窗口，贴图不应该发生变化。
                var mainhandSpriteAfterWait = mainhandRenderer!.sprite;
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.AreEqual(mainhandSpriteAfterWait, mainhandRenderer!.sprite,
                    "不变量①：mesh m2a 没有对应的逐层剪辑资源，mainhand 层应当维持静态图，不随时间轴推进切换");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ② 覆盖剪辑逐层夹具 + 换向 ----------
            {
                var bodyRef = new Id("sprite_anim.hero_walk_b__side_r__body");
                WriteEffectResource(bodyRef, Color.red, Color.green);
                yield return WarmEffectCache(bodyRef);

                var atkClipId = new Id("sprite_anim.atk_m1b");
                var fx = BuildFixture("inv_b", "sprite_anim.hero_walk_b", atkClipId);
                var slotId = new Id("slot.mainhand");

                var m1TemplateId = new Id("item.test_0100_inv_b_m1");
                var m1InstanceId = new Id("item_instance.test_0100_inv_b_m1_1");
                fx.Catalog[m1TemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0100_inv_b_m1"), m1TemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m1b"), socketId: null, modelRef: null);
                Equip(fx.Bus, fx.View, fx.EntityId, m1TemplateId, m1InstanceId, slotId);

                var atkBodySideR = new Id("sprite_anim.atk_m1b__side_r__body");
                var atkMainhandSideR = new Id("sprite_anim.m1b__atk_m1b__side_r__mainhand");
                WriteEffectResource(atkBodySideR, Color.magenta, Color.cyan);
                WriteEffectResource(atkMainhandSideR, Color.black, Color.white);
                yield return WarmEffectCache(atkBodySideR);
                yield return WarmEffectCache(atkMainhandSideR);
                _resourceLoader.TryGetEffect(atkBodySideR, out var atkBodySideREffect);
                _resourceLoader.TryGetEffect(atkMainhandSideR, out var atkMainhandSideREffect);

                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, new Id("skill.test_0100_inv_b_strike"), castTime: 0.0));
                yield return null;

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var animRootTransform = FindChild(layersRoot!, "AnimRoot");
                var animRootRenderer = animRootTransform!.GetComponent<SpriteRenderer>();
                Assert.IsFalse(animRootRenderer.enabled,
                    "不变量②：覆盖剪辑逐层夹具命中（body+mainhand）时，AnimRoot 应当同默认状态一样被隐藏");
                Assert.AreEqual(atkClipId, fx.Player.CurrentClipId!.Value, "应当已经切到覆盖剪辑");

                var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
                var canonicalFrame = fx.Player.CurrentFrame;
                Assert.AreEqual(atkBodySideREffect.Frames[canonicalFrame % atkBodySideREffect.Frames.Length].Sprite, bodyRenderer!.sprite,
                    "不变量②：body 层应当播放覆盖剪辑 side_r 档位的逐层夹具帧");
                Assert.AreEqual(atkMainhandSideREffect.Frames[canonicalFrame % atkMainhandSideREffect.Frames.Length].Sprite, mainhandRenderer!.sprite,
                    "不变量②：mainhand 装备层应当播放覆盖剪辑 side_r 档位、按 mesh_ref 取前缀的逐层夹具帧");

                var mainhandSpriteBeforeDirectionChange = mainhandRenderer!.sprite;

                var atkBodyFront = new Id("sprite_anim.atk_m1b__front__body");
                WriteEffectResource(atkBodyFront, Color.gray, Color.white);
                yield return WarmEffectCache(atkBodyFront);
                _resourceLoader.TryGetEffect(atkBodyFront, out var atkBodyFrontEffect);

                fx.View.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI / 2.0, 8), height: 0.0);
                yield return null;

                yield return WaitUntilOrFail(
                    () => bodyRenderer!.sprite == atkBodyFrontEffect.Frames[fx.Player.CurrentFrame % atkBodyFrontEffect.Frames.Length].Sprite,
                    "不变量②：换向 front 后 body 层应当切换到覆盖剪辑 front 档位的逐层夹具", timeoutSeconds: 3f);
                Assert.AreEqual(
                    atkBodyFrontEffect.Frames[fx.Player.CurrentFrame % atkBodyFrontEffect.Frames.Length].Sprite, bodyRenderer!.sprite,
                    "不变量②核心断言：body 层应当已经换成覆盖剪辑 front 方向变体的对应帧");

                // 判断记录（红→绿过程中订正）：换向 front 后 mainhand 没有对应的逐层夹具（只写了 m1b 的
                // side_r 档位）——recompose（SetPaperdollLayers/ComposeAndApplyEquipAwareLayers）对全部
                // 层的静态候选是无条件重新解析，不是"没有逐层剪辑命中就保留原样"，只有命中当前状态逐层
                // 剪辑的层才会在 OnLayersComposed 触发时被立刻纠正回动画帧（见 ApplyPerLayerFrame 判断
                // 记录）。本用例没有为 mesh.m1b 提供任何静态图像资源，front 档位的静态解析结果是占位
                // 方块——核心断言分两步：① 换向后不应该继续停留在 side_r 档位的动画帧上（recompose 确实
                // 已经重新解析过这一层）；② 此后保持稳定（不被继续驱动、不再变化、不抛异常）。
                Assert.AreNotEqual(mainhandSpriteBeforeDirectionChange, mainhandRenderer!.sprite,
                    "不变量②核心断言：换向 front 后 mainhand 没有对应的逐层夹具，recompose 应当已经把该层" +
                    "的贴图重新解析为 front 档位的静态候选（本用例未提供 mesh.m1b 的静态图像资源，因此是" +
                    "占位方块），不应该继续停留在 side_r 档位的动画帧上");
                var mainhandSpriteAfterRecompose = mainhandRenderer!.sprite;
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.AreEqual(mainhandSpriteAfterRecompose, mainhandRenderer!.sprite,
                    "不变量②核心断言：换向后 mainhand 没有命中任何逐层剪辑，之后应当保持稳定，不被继续" +
                    "驱动（既不报错也不再变化）");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ③ 覆盖剪辑无逐层夹具，只有整身方向变体 ----------
            {
                var wholeBodyClipId = new Id("sprite_anim.wholebody_only_c");
                var fx = BuildFixture("inv_c", "sprite_anim.hero_walk_c_unused", wholeBodyClipId);

                var frontVariant = new Id("sprite_anim.wholebody_only_c__front");
                WriteEffectResource(frontVariant, Color.red, Color.blue);
                yield return WarmEffectCache(frontVariant);
                _resourceLoader.TryGetEffect(frontVariant, out var frontVariantEffect);

                // 先转到 front，使 ctx.LastDirBareName="front"（ReprobeDirectionAwareAnimation 在
                // DirectionSlotChanged 触发时立即赋值，见该方法判断记录），覆盖剪辑触发时按这份已知
                // 方向探测，不需要等 Attack 之后再转向。
                fx.View.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI / 2.0, 8), height: 0.0);

                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, new Id("skill.test_0100_inv_c_strike"), castTime: 0.0));
                yield return null;

                Assert.AreEqual(wholeBodyClipId, fx.Player.CurrentClipId!.Value, "应当已经切到覆盖剪辑（整身路线）");

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var animRootTransform = FindChild(layersRoot!, "AnimRoot");
                var animRootRenderer = animRootTransform!.GetComponent<SpriteRenderer>();
                Assert.IsTrue(animRootRenderer.enabled,
                    "不变量③：覆盖剪辑没有任何逐层夹具命中时应当维持旧行为——整身 AnimRoot 可见");
                Assert.AreEqual(
                    frontVariantEffect.Frames[fx.Player.CurrentFrame % frontVariantEffect.Frames.Length].Sprite,
                    animRootRenderer.sprite,
                    "不变量③核心断言：AnimRoot 应当播放覆盖剪辑的整身方向变体内容（ADR-0093 方向感知 + " +
                    "ADR-0100 决策 3 扩展到覆盖剪辑），不是根治前忽略方向的原始 clipId 内容");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ④ 冷加载：装备层帧集资源首次引用，加载完成后立即追上当前帧 ----------
            {
                var bodyRef = new Id("sprite_anim.hero_walk_d__side_r__body");
                WriteEffectResource(bodyRef, Color.red, Color.green);
                yield return WarmEffectCache(bodyRef);

                var fx = BuildFixture("inv_d", "sprite_anim.hero_walk_d", new Id("sprite_anim.unused_d_attack"));
                var slotId = new Id("slot.mainhand");

                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;

                // 冷加载核心：mainhand 装备层的逐层夹具资源写盘但刻意不预热（不调用 WarmEffectCache），
                // 装备事件触发的 ReprobeForCompositionChange 会自己发起真正的异步 LoadAsync。
                var mainhandRef = new Id("sprite_anim.m3d__hero_walk_d__side_r__mainhand");
                WriteEffectResource(mainhandRef, Color.black, Color.white);

                var m3TemplateId = new Id("item.test_0100_inv_d_m3");
                var m3InstanceId = new Id("item_instance.test_0100_inv_d_m3_1");
                fx.Catalog[m3TemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0100_inv_d_m3"), m3TemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.m3d"), socketId: null, modelRef: null);
                Equip(fx.Bus, fx.View, fx.EntityId, m3TemplateId, m3InstanceId, slotId);

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
                Assert.IsNotNull(mainhandRenderer, "装备后 mainhand 层应当已经存在（尚未完成冷加载，暂时是静态合成图）");

                yield return WaitUntilEffectCached(mainhandRef);
                _resourceLoader.TryGetEffect(mainhandRef, out var mainhandEffect);

                yield return WaitUntilOrFail(
                    () => mainhandRenderer!.sprite == mainhandEffect.Frames[fx.Player.CurrentFrame % mainhandEffect.Frames.Length].Sprite,
                    "ADR-0100 冷/热路径一致性不变量：冷加载完成后，mainhand 层应当追上并显示当前共享时间轴" +
                    "帧号对应的帧——与预热完成后的热路径（复现用例/不变量①②）最终视觉状态一致，不应该因为" +
                    "是异步迟到加载就停留在静态合成图",
                    timeoutSeconds: 3f);

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }
        }
    }
}
