#nullable enable
// DirectionAwareAnimClipTests：ADR-0093（动画剪辑随朝向档位切换）收口验收——
// UnityViewFactory.RegisterDefaultClips 登记的整身默认剪辑此前只在挂接（AttachDefaultAnimation）
// 那一刻按默认朝向（Direction.FromQuantized(0.0, ...)）探测一次 resource_ref，此后
// SpriteViewBase.SyncPose 无论朝向怎么变化都不会重新解析——玩家转身后动画剪辑贴图永远停在挂接时
// 那一档朝向。ADR-0093 决策 4 给 SpriteViewBase 新增 protected virtual OnDirectionSlotChanged 钩子，
// UnitySpriteView 转发为 DirectionSlotChanged 事件，UnityViewFactory.ReprobeDirectionAwareAnimation
// 订阅该事件，按新方向裸档位名重新探测 "sprite_anim.<去类别前缀的资源引用>__<方向裸档位名>" 候选
// （决策 5，整身剪辑；本文件用的固定装/无纸娃娃层的 DisplayInfo 走的正是这条整身路线，逐层路线见
// ADR-0072 既有 PaperdollLayerAnimTests.cs，不重复覆盖）。
//
// 判断记录（不用 data/_sample 真实资源，改用 RootDirOverrideForTests 指向的隔离临时目录）：本文件
// 断言的核心是"同一个 stateClipId 在方向切换后内容确实换成了新方向的资源"，需要精确控制两个方向
// 各自的独立、可按引用相等断言的 Sprite——data/_sample 现有占位角色资源没有为同一状态准备两套内容
//不同的方向变体，改为按 EffectFramesDocument 精确构造的最小 atlas+frames.json（同
// UnityViewFactoryDefaultAnimationTests.CreateView_ColdAnimSetResource... 一类既有测试直接构造隔离
// 资源目录的惯例，只是那边用的是真实 data/_sample，这里改用测试专属临时目录）。
//
// 判断记录（不声明 paperdoll_layers，只覆盖决策 5"整身默认剪辑"这一条路线）：ADR-0093 决策 1（逐层
// 剪辑随朝向切换）复用的是与决策 5 完全同一套 ProbeLayersSequential/ProbeLayerClipTier 探测原语，
// 唯一区别是候选资源 id 多一段层名；ADR-0072 既有 PaperdollLayerAnimTests.cs 已经详尽覆盖了"按层名
// 逐层探测"这条既有机制本身，本文件只需要证明"方向变化会触发重新探测"这一新增行为，选最简的整身
// 路线（不声明纸娃娃层）即可覆盖到 ReprobeDirectionAwareAnimation/ReprobeWholeBodyClipForDirection
// 这两个新增方法，不需要重复搭一遍逐层夹具。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
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
    /// ——本文件不需要真实 <see cref="DataRegistry"/> 的加载/校验流程，只需要
    /// <c>UnityViewFactory.TryResolveAnimSet</c>（私有方法，见该类型判断记录）唯一会调用的
    /// <see cref="Get(string, Id)"/> 这一个方法返回预先手工构造好的 <see cref="DataRecord"/>（同
    /// <c>FakeDisplayInfoRegistryForAnim</c> 一类"最小夹具，不依赖真实 DataRegistry"惯例，见
    /// <c>UnityViewFactoryDefaultAnimationTests.cs</c>）。
    /// <para>
    /// 判断记录（<see cref="GetAll"/> 必须对未知表返回空列表，不能像最初设想那样一律抛
    /// <see cref="NotSupportedException"/>）：实测发现 <c>UnityViewFactory.EnsureAnimClipResolver</c>
    /// 会无条件调用 <c>ResolveWeaponStyleCatalog</c>，后者对 <c>display.weapon_style</c> 表调用
    /// <see cref="GetAll"/>（与本文件是否声明武器风格无关，任何一次 <c>CreateView</c> 都会触发）——
    /// 最初按"本文件用不到"假设让它抛异常，实测直接导致两条用例在 <c>BuildFixture</c> 阶段就失败，
    /// 与 ADR-0093/0094 待验证的行为无关，纯属测试夹具本身对生产代码真实调用面的覆盖不足。改为对
    /// 任何表名都返回空列表（本文件确实不需要武器风格数据），其余未覆盖到的查询类成员
    /// （<see cref="Query"/> 两个重载、<see cref="Tables"/>）保持抛异常——命中说明测试假设有误，需要
    /// 补齐对应的最小夹具数据，而不是静默返回空列表掩盖问题。
    /// </para>
    /// </summary>
    internal sealed class FakeAnimSetRegistry : IDataRegistryView
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

    public sealed class DirectionAwareAnimClipTests : PlayModeTestBase
    {
        private const string DisplayMapIdValue = "display.map.test_hero_0093";
        private const string AnimSetIdValue = "display.anim_set.test_hero_0093";
        private const string MoveResourceRefValue = "sprite_anim.test_hero_0093_move";

        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;
        private string _scratchRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("DirectionAwareAnimClipTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);

            _scratchRoot = Path.Combine(Application.temporaryCachePath, "adr0093_" + Guid.NewGuid().ToString("N"));
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
                // 判断记录：临时目录清理失败（文件句柄未及时释放一类常见 Windows 现象，涵盖
                // IOException/UnauthorizedAccessException 等具体子类）不应该让测试本身失败——
                // Application.temporaryCachePath 下的内容本就是操作系统按需清理的缓存区，残留一次
                // 不影响后续测试（每次 SetUp 都用新的 Guid 子目录，不会互相冲突）。
            }
        }

        /// <summary>按 <see cref="EffectFramesDocument"/> 的精确 JSON 结构，在 <see cref="_scratchRoot"/>
        /// 下写出一份最小两帧序列帧资源：<c>sprite_anim/&lt;去类别前缀的 resourceId&gt;/{atlas.png,
        /// frames.json}</c>——与 <see cref="AssetRefConventions.SpriteAnimDir"/> 的路径推导逐字对应
        /// （<c>UnityResourceLoader.ResolveEffectDir</c> 内部即调用该方法），两帧时长固定 0.25 秒
        /// （<c>frameRate = 1/duration = 4fps</c>，循环一圈 0.5 秒），供两个不同方向各自的资源用同一套
        /// 时序参数，使"方向切换后播放进度是否保留"的断言不需要处理帧率不一致的换算。</summary>
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
            // 判断记录：同 UnityResourceLoaderTests.cs 既有惯例——EncodeToPNG 是
            // UnityEngine.ImageConversion 的扩展方法，改用全限定静态调用，不依赖扩展方法语法糖是否
            // 在当前 using 集合下能正确解析。
            File.WriteAllBytes(Path.Combine(dir, "atlas.png"), UnityEngine.ImageConversion.EncodeToPNG(atlas));
            UnityEngine.Object.DestroyImmediate(atlas);

            const string framesJson = "{\"fps\":4,\"loop\":true,\"frames\":[" +
                "{\"x\":0,\"y\":0,\"w\":4,\"h\":4,\"duration\":0.25}," +
                "{\"x\":4,\"y\":0,\"w\":4,\"h\":4,\"duration\":0.25}]}";
            File.WriteAllText(Path.Combine(dir, "frames.json"), framesJson);
        }

        /// <summary>同步把 <paramref name="resourceId"/> 加载进 <see cref="_resourceLoader"/> 的缓存
        /// （<see cref="UnityResourceLoader.TryGetEffect"/> 命中）——本文件预先"预热"全部测试要用到的
        /// 方向资源，使 <c>UnityViewFactory.ReprobeDirectionAwareAnimation</c>（私有方法，见该类型判断
        /// 记录）内部的 <c>ProbeLayerClipTier</c> 在方向切换那一刻直接同步命中缓存，不需要测试代码在每次
        /// <c>SyncPose</c> 之后再额外跑一段 <c>Tick</c>/<c>yield</c> 轮询等待异步加载——被测的是"方向
        /// 变化是否触发了重新探测/正确注册"，不是资源异步加载管线本身（该管线已由 GP-06 一类既有测试
        /// 覆盖）。</summary>
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

        /// <summary>本文件公用夹具：不声明 <c>paperdoll_layers</c>（只覆盖 ADR-0093 决策 5"整身默认
        /// 剪辑"路线，见文件顶部判断记录）、8 方向、无 <c>mirror_pairs</c>（<c>front</c>/<c>back</c>
        /// 均为原创绘制档位，不经默认镜像回退，见 <see cref="Presentation.Common.DirectionSlots"/>
        /// 判断记录），<c>display.anim_set</c> 只声明 <c>move</c> 一个状态引用
        /// <see cref="MoveResourceRefValue"/>。</summary>
        private (IEventBus Bus, UnityViewFactory Factory, UnitySpriteView View, Id EntityId, UnityFrameAnimPlayer Player, Id MoveClipId) BuildFixture()
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in Core.Foundation.EventBus.EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var sprite = new SpriteInfo(spriteSetId: "sprite.creature.test_hero_0093", directionCount: 8);
            var info = new DisplayInfo(
                id: new Id(DisplayMapIdValue),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.test_hero_0093"),
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
            var dataRegistry = new FakeAnimSetRegistry();
            dataRegistry.Add(animSetRecord);

            var entityId = new Id("unit.adr0093_test_entity_" + Guid.NewGuid().ToString("N"));
            var factory = new UnityViewFactory(_renderer, new RenderConventionHost(), displayInfoRegistry, _resourceLoader, bus: bus, dataRegistry: dataRegistry);

            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");

            var moveClipId = new Id($"anim.default.{DisplayMapIdValue}.move");

            return (bus, factory, view, entityId, player!, moveClipId);
        }

        /// <summary>ADR-0093 核心复现：把单位从一个方向（front）转到另一个方向（back），"move"
        /// 状态当前播放中的剪辑内容应当切换成新方向对应的资源，且不从头播放（保留已经过去的播放进度，
        /// 决策 2）——改动前 <c>RegisterDefaultClips</c> 只在挂接那一刻解析一次
        /// <see cref="Core.Foundation.DisplayInfo.AnimClipDef.ResourceRef"/> 本身（不带方向段），此后
        /// <c>SyncPose</c> 换向永远不会重新解析，本用例在根治前必然红（断言"切换后的贴图应为 back
        /// 方向资源"会失败，因为压根没有任何代码会在朝向变化时重新探测 <c>__back</c> 变体）。</summary>
        [UnityTest]
        public IEnumerator SyncPose_DirectionChanges_MoveClipSwitchesToNewDirectionResource_PreservingProgress()
        {
            var frontRef = new Id(MoveResourceRefValue + "__front");
            var backRef = new Id(MoveResourceRefValue + "__back");
            WriteEffectResource(frontRef, Color.red, Color.green);
            WriteEffectResource(backRef, Color.blue, Color.yellow);
            yield return WarmEffectCache(frontRef);
            yield return WarmEffectCache(backRef);
            _resourceLoader.TryGetEffect(frontRef, out var frontEffect);
            _resourceLoader.TryGetEffect(backRef, out var backEffect);

            var (bus, factory, view, entityId, player, moveClipId) = BuildFixture();

            // front：raw facing = 90°（index2，8 方向表 2=front，见 DirectionSlots 类型注释"8 方向完整
            // 对照表"），与构造/挂接期的默认朝向（raw=0.0 → index0=side_l → 镜像自 side_r）不同一个
            // 解析后的档位，触发一次方向变化重探测。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI / 2.0, 8), 0.0);
            Assert.AreEqual(1, factory.DirectionAwareReprobeCountForTests, "转到 front 应当恰好触发一次重探测");

            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));
            Assert.AreEqual(moveClipId, player.CurrentClipId!.Value, "Walk 应当播放 move 剪辑");

            // 播放约 0.3 秒（两帧、单帧 0.25 秒、循环 0.5 秒一圈）——落在第 1 帧（index 1）区间内，
            // 用真实 Time.deltaTime 推进（UnityFrameAnimPlayer.Update 内部即读取 Time.deltaTime），
            // 不直接摆弄内部状态。
            var elapsed = 0f;
            while (elapsed < 0.3f)
            {
                yield return null;
                elapsed += Time.deltaTime;
            }
            var frameBeforeSwitch = player.CurrentFrame;
            Assert.AreEqual(
                frontEffect.Frames[frameBeforeSwitch].Sprite, player.SpriteRenderer.sprite,
                "切换方向之前，当前贴图应当已经是 front 方向资源的对应帧");

            // 转到 back：raw facing = 270°（index6，8 方向表 6=back）。
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI * 1.5, 8), 0.0);
            Assert.AreEqual(2, factory.DirectionAwareReprobeCountForTests, "转到 back 应当再触发一次重探测");

            // 内容切换只在 UnityFrameAnimPlayer.Update 的下一次 Update() 才会被 FrameAnimPlayer 感知
            // （N18 机制：每次 Update 开头才重新从共享字典查最新版本），推进一帧即可。
            yield return null;

            Assert.AreEqual(moveClipId, player.CurrentClipId!.Value, "换向不应该打断当前正在播放的状态本身");
            Assert.AreEqual(
                backEffect.Frames[player.CurrentFrame].Sprite, player.SpriteRenderer.sprite,
                "ADR-0093 核心断言：换向后应当已经切换成 back 方向资源的对应帧（根治前会一直停在 front 内容，本断言即为红→绿分界线）");
            Assert.Greater(
                player.CurrentFrame, 0,
                "ADR-0093 决策 2：换向不应该从头播放——已经经过的播放进度（elapsedSeconds）应当被保留，" +
                "按新剪辑的帧率重新换算出的帧下标不应该被重置为 0");
        }

        /// <summary>ADR-0093 不变量：①目标方向没有对应资源时应当回退到无方向段的原始
        /// <c>resource_ref</c>，不抛异常、不清空已有内容；②同一方向档位内重复 <c>SyncPose</c>
        /// 不应该重复触发重探测（决策 3"只在档位变化时探测，不逐帧探测"）。</summary>
        [UnityTest]
        public IEnumerator ReprobeDirectionAwareAnimation_MissingDirectionFallsBackWithoutError_AndSameSlotDoesNotReprobe()
        {
            var fallbackRef = new Id(MoveResourceRefValue);
            WriteEffectResource(fallbackRef, Color.white, Color.black);
            yield return WarmEffectCache(fallbackRef);
            _resourceLoader.TryGetEffect(fallbackRef, out var fallbackEffect);

            var (bus, factory, view, entityId, player, moveClipId) = BuildFixture();

            // back（index6，8 方向表 6=back）：与构造/挂接期的默认朝向（raw=0.0 → index0=side_l →
            // 镜像自 side_r）解析出的档位不同，会触发一次方向变化重探测；本例没有为 back 写专属
            // "__back" 资源，两级候选的第一级（"<ref>__back"）必然未命中，应当落到第二级——无方向段
            // 的原始 resource_ref（本例已预热的 fallbackRef）——不抛异常，也不是"保持原有单帧占位"。
            Assert.DoesNotThrow(
                () => view.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI * 1.5, 8), 0.0),
                "目标方向没有专属资源时不应该抛异常，应当静默回退到无方向段的候选");
            Assert.AreEqual(1, factory.DirectionAwareReprobeCountForTests, "转到 back 应当恰好触发一次重探测");

            bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(entityId, "Idle", "Walk"));
            Assert.AreEqual(moveClipId, player.CurrentClipId!.Value, "缺失专属方向资源不应该阻断 Walk 状态切换本身播放 move 剪辑");
            yield return null;

            Assert.AreEqual(
                fallbackEffect.Frames[player.CurrentFrame].Sprite, player.SpriteRenderer.sprite,
                "ADR-0093 不变量①：专属方向资源缺失时应当回退到无方向段的 resource_ref 本身（决策 5 候选②），不报错、不停留在挂接期的单帧占位");

            // 不变量②：重复用同一个 raw facing 调用 SyncPose 多次——解析出的方向槽位不变，不应该重复
            // 触发探测（决策 3"只在档位变化时探测，不逐帧探测"）。
            for (var i = 0; i < 3; i++)
            {
                view.SyncPose(Vec2.Zero, Direction.FromQuantized(Math.PI * 1.5, 8), 0.0);
            }
            Assert.AreEqual(1, factory.DirectionAwareReprobeCountForTests, "同一方向档位内重复 SyncPose 不应该重复触发重探测");
        }
    }
}
