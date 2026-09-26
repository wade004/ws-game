#nullable enable
// PaperdollRecomposeOnDirectionJitterTests：ADR-0099（消费方反馈第四十四批，db451b82 32/32 段闪回逐帧
// CSV 实测复核）验收——同档位内朝向末位抖动（核心侧 MovementTickHandler 沿直线段逐 tick 现算 Atan2
// 的浮点舍入误差，同批已在核心侧根治，见 core/carriers/unit/tests/MovementFacingStabilityTests.cs）
// 不应该在表现层被放大成一次纸娃娃层重合成：
//   决策 1：UnitySpriteView.SyncPose 判断"朝向是否变化"的口径此前是 Direction.Equals（连
//     RawRadians 一起比），改成只看 IRenderConventionHost.ResolveDirectionSlot 解析出的
//     (方向槽位 Id, 镜像标志)——同一档位内 RawRadians 无论怎么抖动，重合成都应该是空操作。
//   决策 2：真正合法的重合成（方向槽位真变化/装备变化）发生后，命中 ADR-0072 纸娃娃层逐层动画的层
//     应当立即回填当前播放帧（SpriteViewBase.OnLayersComposed -> UnitySpriteView.LayersComposed ->
//     UnityViewFactory 订阅后按播放器当前帧号重新 SetLayerSprite），不等下一次 OnFrameChanged，
//     避免"重合成→短暂显示静态图→下一帧才纠正回动画帧"的可见闪烁。
//
// 判断记录（沿用 ADR-0093 DirectionAwareAnimClipTests 的夹具做法，不用 data/_sample 真实资源）：
// 本文件需要精确控制"方向 A/方向 B 各自的逐层剪辑内容是否存在、内容是什么"，改用
// EffectFramesDocument 精确构造的最小 atlas+frames.json 落在 RootDirOverrideForTests 指向的隔离
// 临时目录，与 PaperdollLayerAnimTests.cs（用真实 data/_sample creature.sample_hero 数据）覆盖的
// 是同一套生产装配路径，只是数据来源不同——两份夹具验收的行为互补，不重复。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Expr;
using Core.Foundation.EventBus;
using NUnit.Framework;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>只承载一条 <c>display.anim_set</c> 记录的最小 <see cref="IDataRegistryView"/> 测试替身
    /// ——同 <see cref="DirectionAwareAnimClipTests"/>.<c>FakeAnimSetRegistry</c> 逐字节一致的既有惯例
    /// （本文件不复用该私有嵌套类型，独立声明一份同名同构的替身，两个测试类互不依赖）。</summary>
    internal sealed class FakeAnimSetRegistryForRecompose : IDataRegistryView
    {
        private readonly Dictionary<string, DataRecord> _byId = new Dictionary<string, DataRecord>(StringComparer.Ordinal);

        public void Add(DataRecord record) => _byId[record.Id!.Value.Value] = record;

        public DataRecord? Get(string table, string key) => _byId.TryGetValue(key, out var r) ? r : null;

        public DataRecord? Get(string table, Id id) => _byId.TryGetValue(id.Value, out var r) ? r : null;

        public IReadOnlyList<DataRecord> GetAll(string table) => Array.Empty<DataRecord>();

        public IReadOnlyList<DataRecord> Query(string table, ExprNode predicate) => throw new NotSupportedException();

        public IReadOnlyList<DataRecord> Query(string table, string predicateText) => throw new NotSupportedException();

        public IReadOnlyList<string> Tables => throw new NotSupportedException();

        public TableSchema? GetSchema(string table) => null;
    }

    public sealed class PaperdollRecomposeOnDirectionJitterTests : PlayModeTestBase
    {
        private const string DisplayMapIdValue = "display.map.test_hero_0099";
        private const string AnimSetIdValue = "display.anim_set.test_hero_0099";
        private const string MoveResourceRefValue = "sprite_anim.test_hero_0099_move";
        private const string SpriteSetIdValue = "sprite.creature.test_hero_0099";

        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;
        private string _scratchRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("PaperdollRecomposeOnDirectionJitterTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);

            _scratchRoot = Path.Combine(Application.temporaryCachePath, "adr0099_" + Guid.NewGuid().ToString("N"));
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
                // 判断记录同 DirectionAwareAnimClipTests.TearDown：Windows 文件句柄未及时释放不应该让
                // 测试本身失败，Application.temporaryCachePath 本就是操作系统按需清理的缓存区。
            }
        }

        /// <summary>与 <see cref="DirectionAwareAnimClipTests.WriteEffectResource"/> 逐字节一致的最小
        /// 两帧序列帧资源写盘帮助方法（本文件独立一份，两个测试类互不依赖）。</summary>
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
        }

        /// <summary>本文件公用夹具：<c>paperdoll_layers: ["body", "cape"]</c>——"body" 层会被预热两个
        /// 方向（front/side_r）的逐层走路剪辑，用于验证决策 1/2；"cape" 层自始至终没有任何逐层剪辑
        /// 资源，用于验证决策 2 不变量③"无逐层动画的层不被误伤"。8 方向、无显式 <c>mirror_pairs</c>
        /// （<c>front</c>/<c>side_r</c> 本身都是"两处都没有命中镜像回退"的规范档位，见
        /// <c>RenderConventionHost.ResolveDirectionSlot</c> 判断记录，不需要额外配置）。</summary>
        private (IEventBus Bus, UnityViewFactory Factory, UnitySpriteView View, Id EntityId, UnityFrameAnimPlayer Player, Id MoveClipId) BuildFixture()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in Core.Foundation.EventBus.EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var sprite = new SpriteInfo(
                spriteSetId: SpriteSetIdValue, directionCount: 8,
                paperdollLayers: new[] { "body", "cape" });
            var info = new DisplayInfo(
                id: new Id(DisplayMapIdValue),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.test_hero_0099"),
                kind: DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.None, sortOffset: 0.0, weaponStyleRef: null,
                sprite: sprite, model: null);

            var displayInfoRegistry = new FakeDisplayInfoRegistryForAnim();
            displayInfoRegistry.Add(info);

            var animSetJson = "{\"id\":\"" + AnimSetIdValue + "\",\"clips\":{\"move\":{\"resource_ref\":\"" + MoveResourceRefValue + "\"}}}";
            var animSetRaw = (JsonObject)JsonReader.Parse(animSetJson);
            var animSetSchema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            var animSetRecord = new DataRecord(animSetSchema, AnimSetIdValue, new Id(AnimSetIdValue), animSetRaw);
            var dataRegistry = new FakeAnimSetRegistryForRecompose();
            dataRegistry.Add(animSetRecord);

            var entityId = new Id("unit.adr0099_test_entity_" + Guid.NewGuid().ToString("N"));
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfoRegistry, _resourceLoader, bus: bus, dataRegistry: dataRegistry);

            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            view.Bind(entityId);
            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");

            var moveClipId = new Id($"anim.default.{DisplayMapIdValue}.move");

            return (bus, factory, view, entityId, player!, moveClipId);
        }

        private static Transform FindLayer(Transform layersRoot, int index)
        {
            var child = layersRoot.Find($"Layer_{index}");
            Assert.IsNotNull(child, $"应当存在纸娃娃层子物体 Layer_{index}");
            return child!;
        }

        /// <summary>判断记录：本工程 Unity 运行时的 API 兼容级别下 <c>Math.BitIncrement</c>/
        /// <c>Math.BitDecrement</c>（.NET Core 3.0+ 才有）不可用（实测 CS0117 编译错误），改用逐位操作
        /// 手写"取相邻可表示浮点值"，只覆盖本文件唯一用到的场景（<paramref name="value"/> 恒为正数、
        /// 远离 0，不需要处理跨零/次正规数等边界）。<paramref name="up"/> 为 true 时取比
        /// <paramref name="value"/> 大的下一个可表示值，否则取小的下一个。</summary>
        private static double NextRepresentableDouble(double value, bool up)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            bits += up ? 1L : -1L;
            return BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>决策 1 核心复现：方向档位不变（front），只有 <c>RawRadians</c> 在同一档位内以
        /// <see cref="NextRepresentableDouble"/>（相邻浮点值）幅度来回抖动（模拟核心侧 MovementTickHandler 沿直线
        /// 段逐 tick 现算 Atan2 的最后一位浮点舍入误差，根治前实测 3 秒内 28 次变化）——连续 60 次
        /// <see cref="Presentation.Render.SpriteViewBase.SyncPose"/> 都不应该触发纸娃娃层重合成，命中
        /// 逐层走路动画的 body 层应当全程保持动画帧，一次都不应该等于刚合成时的静态层贴图（根治前：
        /// 判据是 <c>Direction.Equals</c>，RawRadians 每变化一次就重合成一次，把逐层动画的当前帧覆盖
        /// 回静态图，要等下一次 <c>OnFrameChanged</c> 才纠正回来）。</summary>
        [UnityTest]
        public IEnumerator SyncPose_SameDirectionSlotRawRadiansJitter_DoesNotRecomposeOrResetAnimatedLayer()
        {
            var frontBodyRef = new Id(MoveResourceRefValue + "__front__body");
            WriteEffectResource(frontBodyRef, Color.red, Color.green);
            yield return WarmEffectCache(frontBodyRef);

            var (bus, _, view, entityId, player, moveClipId) = BuildFixture();

            // 判断记录（同 PaperdollLayerAnimTests 既有惯例）："Layer_N" 子物体由 UnityRenderer2D.SetLayers
            // 在首次合成纸娃娃层时才建出来，CreateView/Bind 阶段 LayersRoot 底下还没有子物体——必须先
            // SyncPose 一次触发首次合成，再去找 Layer_0。
            const double frontFacing = Math.PI / 2.0; // 8 方向表 index2 = front，不经镜像回退。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(frontFacing, 8), 0.0);

            var layersRoot = _renderer.GetLayersRoot(view.EngineHandle);
            Assert.IsNotNull(layersRoot, "应当已经建好 LayersRoot 子物体");
            var bodyRenderer = FindLayer(layersRoot!, 0).GetComponent<SpriteRenderer>();
            var staticFrontSprite = bodyRenderer.sprite;
            Assert.IsNotNull(staticFrontSprite, "首次 SyncPose 应当已经给 body 层写入静态解析出的贴图");
            Assert.AreEqual(1, view.PaperdollRecomposeCountForTests, "首次 SyncPose 必然触发一次重合成（_layersInitialized 从 false 变 true）");

            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));
            Assert.AreEqual(moveClipId, player.CurrentClipId!.Value, "Walk 应当播放 move 剪辑");

            // 推进到 body 层的逐层动画真的写过至少一帧（OnFrameChanged 由 UnityFrameAnimPlayer.Update
            // 驱动，需要至少一次真实引擎帧）。
            yield return null;
            var animatedSprite = bodyRenderer.sprite;
            Assert.AreNotEqual(staticFrontSprite, animatedSprite,
                "逐层走路动画命中后，body 层贴图应当已经切换成动画帧，不再是刚合成时的静态贴图");

            var recomposeCountBeforeJitter = view.PaperdollRecomposeCountForTests;
            var staticSpriteHitCount = 0;

            // 连续 60 次 SyncPose，朝向在同一档位内交替 θ 与 θ 的相邻浮点值（BitIncrement/BitDecrement
            // 交替，覆盖"变大"和"变小"两个方向的抖动，同消费方 CSV 实测"两个值来回交替"的模式）。不
            // yield（不推进 UnityFrameAnimPlayer 的时间轴）——本用例只关心"重合成是否被同档位内的抖动
            // 误触发"，不是"动画是否继续播放"，两件事独立，混在一起会让断言难以归因。
            var jitterUp = NextRepresentableDouble(frontFacing, up: true);
            var jitterDown = NextRepresentableDouble(frontFacing, up: false);
            for (var i = 0; i < 60; i++)
            {
                var facing = (i % 2 == 0) ? jitterUp : jitterDown;
                view.SyncPose(Vec2.Zero, Direction.FromQuantized(facing, 8), 0.0);

                if (bodyRenderer.sprite == staticFrontSprite)
                {
                    staticSpriteHitCount++;
                }
            }

            Assert.AreEqual(0, staticSpriteHitCount,
                "ADR-0099 决策 1 核心断言：60 次同档位内的 RawRadians 抖动，body 层贴图 0 次等于静态层贴图" +
                "（根治前会在每次抖动时短暂跳回静态图）");
            Assert.AreEqual(animatedSprite, bodyRenderer.sprite,
                "抖动全程 body 层应当保持同一张动画帧贴图不变（没有发生任何一次重合成/覆盖）");
            Assert.AreEqual(recomposeCountBeforeJitter, view.PaperdollRecomposeCountForTests,
                "ADR-0099 决策 1：60 次同档位内的 RawRadians 抖动，重合成次数应当为 0（PaperdollRecomposeCountForTests 不变）");

            view.Destroy();
        }

        /// <summary>
        /// 决策 2 不变量（分支合一）：
        /// ① 方向槽位真变化（front -&gt; side_r）：应当恰好触发一次重合成，且这一次 <c>SyncPose</c>
        ///    调用返回后（不需要等下一次 <c>OnFrameChanged</c>），body 层已经是新方向的走路动画帧，
        ///    不是刚合成的新方向静态贴图。
        /// ② 装备变化（<c>slot.body</c> 覆盖 body 层）触发的重合成：同样应当立即回填当前帧，不需要
        ///    等下一次 <c>OnFrameChanged</c>。
        /// ③ 没有任何逐层动画的 cape 层：以上两次重合成都不应该误伤它——层仍然存在、贴图非空，不抛
        ///    异常（回填只处理命中逐层动画的层，不该动的层保持不动）。
        /// </summary>
        [UnityTest]
        public IEnumerator SyncPose_RealSlotChangeOrEquipChange_RecomposesAndImmediatelyBackfillsCurrentFrame()
        {
            var frontBodyRef = new Id(MoveResourceRefValue + "__front__body");
            var sideRBodyRef = new Id(MoveResourceRefValue + "__side_r__body");
            WriteEffectResource(frontBodyRef, Color.red, Color.green);
            WriteEffectResource(sideRBodyRef, Color.blue, Color.yellow);
            yield return WarmEffectCache(frontBodyRef);
            yield return WarmEffectCache(sideRBodyRef);

            var (bus, _, view, entityId, player, moveClipId) = BuildFixture();

            const double frontFacing = Math.PI / 2.0; // index2 = front。
            const double sideRFacing = 0.0; // index0，8 方向表按 RenderConventionHost 默认镜像規則本身即解析为 side_r（无 mirror_pairs 时的默认回退，见该类型判断记录）。

            // 判断记录（同上一用例）："Layer_N" 子物体要等首次 SyncPose 触发合成才建出来。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(frontFacing, 8), 0.0);

            var layersRoot = _renderer.GetLayersRoot(view.EngineHandle);
            Assert.IsNotNull(layersRoot, "应当已经建好 LayersRoot 子物体");
            var bodyRenderer = FindLayer(layersRoot!, 0).GetComponent<SpriteRenderer>();
            var capeRenderer = FindLayer(layersRoot!, 1).GetComponent<SpriteRenderer>();
            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));
            Assert.AreEqual(moveClipId, player.CurrentClipId!.Value, "Walk 应当播放 move 剪辑");
            yield return null; // 让 body 层至少真的写过一次 front 方向的动画帧。

            Assert.IsNotNull(capeRenderer.sprite, "不变量③前置：cape 层挂接时应当已有静态贴图（即便是占位方块）");

            // ① 真实方向槽位变化：front -> side_r。
            var recomposeCountBeforeSlotChange = view.PaperdollRecomposeCountForTests;
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(sideRFacing, 8), 0.0);
            Assert.AreEqual(recomposeCountBeforeSlotChange + 1, view.PaperdollRecomposeCountForTests,
                "①：方向槽位真变化（front -> side_r）应当恰好触发一次重合成");

            var sideRBodyClipId = new Id($"anim.default.{DisplayMapIdValue}.move.layer.body");
            var expectedSideRFrame = player.GetFrame(sideRBodyClipId, player.CurrentFrame);
            Assert.IsNotNull(expectedSideRFrame,
                "测试前置：side_r 方向的 body 逐层剪辑应当已经同步命中缓存（WarmEffectCache 已预热）");
            Assert.AreEqual(expectedSideRFrame, bodyRenderer.sprite,
                "①核心断言：SyncPose 换到 side_r 返回后（不等下一次 OnFrameChanged），body 层应当已经是" +
                "side_r 方向的走路动画帧，不是刚合成的 side_r 静态贴图");
            Assert.IsNotNull(capeRenderer.sprite, "①不变量③：cape 层不应该被这次回填误伤，贴图仍然非空");

            // ② 装备变化：slot.body 覆盖 body 层（不需要真实磁盘资产——本用例只关心"重合成是否立即
            // 回填当前帧"，不关心装备本身的静态贴图是否加载成功）。
            var bodyOverrideTemplateId = new Id("item.test_hero_0099_body_override");
            var bodyOverrideInstanceId = new Id("item_instance.body_override_2");
            var bodyOverrideMeshRef = new Id("paperdoll.item.test_hero_0099_body_override");
            // 判断记录：SpriteViewBase._equipVisuals（构造参数 equipVisualByItemInstanceId）按"物品
            // 实例 id"索引，不是按 item.template id（见该字段注释"为何按物品实例 id 而不是 item_id
            // 索引"）——键必须是下面 ItemAddedEvent/ItemEquippedEvent 用的同一个 bodyOverrideInstanceId，
            // 不是 bodyOverrideTemplateId（EquipVisualDef.ItemId 字段本身仍然记录 template id，只是不
            // 作为字典键）。
            var equipCatalog = new Dictionary<Id, EquipVisualDef>
            {
                [bodyOverrideInstanceId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_hero_0099_body_override"), bodyOverrideTemplateId,
                    EquipVisualMode.SlotMesh, slotId: new Id("slot.body"), meshRef: bodyOverrideMeshRef,
                    socketId: null, modelRef: null),
            };
            // 判断记录（为什么另建一个 view2 而不是复用本用例已有的 view）：UnitySpriteView 的
            // equipVisualByItemInstanceId 只能在 UnityViewFactory 构造期注入（见
            // SpriteEquipVisualWiringTests.BuildFixture 一贯惯例，SpriteViewBase._equipVisuals 是
            // 构造期一次性赋值的只读字段，事后没有"补注入"的入口）——本用例的 view 挂接时用的
            // BuildFixture 没有传这个参数，_equipVisuals 恒为 null，OnEvent 会直接短路。改为用
            // BuildFixtureWithEquipCatalog 新建一个装配期就注入了 equipCatalog 的独立 view2，复用同一套
            // 已预热到 UnityResourceLoader 缓存里的 front/side_r body 逐层剪辑资源（缓存是
            // UnityResourceLoader 实例级的，本文件 SetUp 里两个 view 共用同一个 _resourceLoader）。
            var (bus2, _, view2, entityId2, player2, _) = BuildFixtureWithEquipCatalog(equipCatalog);

            // 判断记录（同上两处）："Layer_N" 子物体要等首次 SyncPose 触发合成才建出来。
            view2.SyncPose(Vec2.Zero, Direction.FromQuantized(sideRFacing, 8), 0.0);
            var layersRoot2 = _renderer.GetLayersRoot(view2.EngineHandle);
            var bodyRenderer2 = FindLayer(layersRoot2!, 0).GetComponent<SpriteRenderer>();
            var capeRenderer2 = FindLayer(layersRoot2!, 1).GetComponent<SpriteRenderer>();

            bus2.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId2, "Idle", "Walk"));
            yield return null;

            var sideRBodyClipId2 = new Id($"anim.default.{DisplayMapIdValue}.move.layer.body");
            var equipEvt = new ItemEquippedEvent(entityId2, bodyOverrideInstanceId, new Id("slot.body"));
            bus2.PublishImmediate(new ItemAddedEvent(entityId2, bodyOverrideInstanceId, bodyOverrideTemplateId, count: 1));
            bus2.PublishImmediate(equipEvt);
            view2.OnEvent(equipEvt); // RebuildEquippedLayers -> ComposeAndApplyEquipAwareLayers -> OnLayersComposed。

            var expectedSideRFrame2 = player2.GetFrame(sideRBodyClipId2, player2.CurrentFrame);
            Assert.IsNotNull(expectedSideRFrame2, "②测试前置：side_r 方向 body 逐层剪辑应当命中缓存");
            Assert.AreEqual(expectedSideRFrame2, bodyRenderer2.sprite,
                "②核心断言：装备变化触发的重合成（view2.OnEvent）返回后，body 层应当已经立即回填为当前" +
                "走路动画帧，不是装备覆盖刚写入的静态贴图");
            Assert.IsNotNull(capeRenderer2.sprite, "②不变量③：cape 层不应该被装备变化这次回填误伤");

            view.Destroy();
            view2.Destroy();
        }

        /// <summary>②用例专属：构造一个注入了 <paramref name="equipCatalog"/> 的 View（
        /// <c>UnityViewFactory</c> 的 <c>equipVisualByItemInstanceId</c> 构造参数只能在 <c>CreateView</c>
        /// 之前装配好，不能事后补——同 <see cref="SpriteEquipVisualWiringTests.BuildFixture"/> 一贯的
        /// "equipVisualByItemInstanceId 随 factory 一起构造"惯例），与 <see cref="BuildFixture"/> 其余
        /// 部分逐字节一致，只多这一个参数。</summary>
        private (IEventBus Bus, UnityViewFactory Factory, UnitySpriteView View, Id EntityId, UnityFrameAnimPlayer Player, Id MoveClipId) BuildFixtureWithEquipCatalog(
            IReadOnlyDictionary<Id, EquipVisualDef> equipCatalog)
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in Core.Foundation.EventBus.EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var sprite = new SpriteInfo(
                spriteSetId: SpriteSetIdValue, directionCount: 8,
                paperdollLayers: new[] { "body", "cape" });
            var info = new DisplayInfo(
                id: new Id(DisplayMapIdValue),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.test_hero_0099_equip"),
                kind: DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.None, sortOffset: 0.0, weaponStyleRef: null,
                sprite: sprite, model: null);

            var displayInfoRegistry = new FakeDisplayInfoRegistryForAnim();
            displayInfoRegistry.Add(info);

            var animSetJson = "{\"id\":\"" + AnimSetIdValue + "\",\"clips\":{\"move\":{\"resource_ref\":\"" + MoveResourceRefValue + "\"}}}";
            var animSetRaw = (JsonObject)JsonReader.Parse(animSetJson);
            var animSetSchema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            var animSetRecord = new DataRecord(animSetSchema, AnimSetIdValue, new Id(AnimSetIdValue), animSetRaw);
            var dataRegistry = new FakeAnimSetRegistryForRecompose();
            dataRegistry.Add(animSetRecord);

            var entityId = new Id("unit.adr0099_test_entity_equip_" + Guid.NewGuid().ToString("N"));
            var factory = new UnityViewFactory(
                _renderer, new RenderConventionHost(), displayInfoRegistry, _resourceLoader, bus: bus, dataRegistry: dataRegistry,
                equipVisualByItemInstanceId: equipCatalog);

            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            view.Bind(entityId);
            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");

            var moveClipId = new Id($"anim.default.{DisplayMapIdValue}.move");

            return (bus, factory, view, entityId, player!, moveClipId);
        }
    }
}
