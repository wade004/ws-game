#nullable enable
// PerLayerDirectionCacheReregistrationTests：消费方反馈第四十九批·反馈 1 根治收口验收——
// UnityViewFactory.GetOrProbePerLayerForDirection/GetOrProbeOverrideClipPerLayer 命中 (实体, 方向)
// 缓存时此前直接 `return cached;`，只把缓存的 (层名 -> clipId) 映射表换进 ActivePerLayerByState，
// 从不重新调用 UnityFrameAnimPlayer.RegisterClipFromEffect——而逐层剪辑的注册 clipId
// （<stateClipId>.layer.<层名>，见 ProbeComposedLayersSequential 判断记录）与方向无关，front/side_r/
// back 三个方向都注册到同一个 clipId，后一次访问的方向会把前一次访问过的方向的内容覆盖掉：consumer
// 在 1.82.0 冻结提交 46e36155 上逐帧读数证实，走两圈后 +Y/-Y 方向的行走与停下都显示 side_r 的帧，
// 攻击起手第一帧同样错误，静态装备层与逻辑朝向本身都是对的。
//
// 判断记录（不用 data/_sample 真实资源，改用 RootDirOverrideForTests 指向的隔离临时目录 + 手工构造的
// 最小 IDataRegistryView/IDisplayInfoRegistry 夹具）：同 DirectionAwareAnimClipTests.cs/
// PerLayerClipEquipAndOverrideTests.cs 一贯做法——本文件需要精确控制 front/side_r/back 三个方向各自
// 独立、可按 Sprite.name 子串断言的逐层帧集，复用两个既有文件的 WriteEffectResource/WarmEffectCache/
// WaitUntilEffectCached/WaitUntilOrFail/FindChild/FindLayerRenderer 写法（逐字节拷贝，未抽公共基类
// ——同两个既有文件之间的既有重复惯例一致，不在本次缺陷修复任务里顺带做测试基础设施重构）。复用同
// 命名空间下已有的 FakeDisplayInfoRegistryForAnim（UnityViewFactoryDefaultAnimationTests.cs）与
// FakeAnimSetAndWeaponStyleRegistry/FixedWeaponStyleSource（PerLayerClipEquipAndOverrideTests.cs/
// AnimClipResolverTests.cs），不重复定义。
//
// 判断记录（4 方向精灵，不是既有测试常用的 8 方向）：任务书要求用 4 方向精灵复现——
// Presentation.Common.DirectionSlots 的量化换算对 front（90°）/side_r（180°）/back（270°）三个裸档位
// 名在 4/8 方向下取值相同（front 恒为 directionCount/4、back 恒为 directionCount*3/4、side_r 恒为
// 180°物理方向，与 direction_count 无关），因此本文件沿用既有测试同一套 Direction.FromQuantized 角度
// 常量（Math.PI/2.0=front、Math.PI=side_r、Math.PI*1.5=back），只改 SpriteInfo.DirectionCount=4。
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
    public sealed class PerLayerDirectionCacheReregistrationTests : PlayModeTestBase
    {
        private GameObject _rootGo = null!;
        private UnityResourceLoader _resourceLoader = null!;
        private UnityRenderer2D _renderer = null!;
        private string _scratchRoot = null!;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("PerLayerDirectionCacheReregistrationTestsRoot");
            _resourceLoader = new UnityResourceLoader();
            _renderer = new UnityRenderer2D(_rootGo.transform, _resourceLoader);

            _scratchRoot = Path.Combine(Application.temporaryCachePath, "consumer49_" + Guid.NewGuid().ToString("N"));
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
                // 判断记录：同 DirectionAwareAnimClipTests/PerLayerClipEquipAndOverrideTests 一贯做法
                // ——临时目录清理失败不应该让测试本身失败，每次 SetUp 都用新的 Guid 子目录。
            }
        }

        /// <summary>逐字节拷贝自 PerLayerClipEquipAndOverrideTests.WriteEffectResource（同一套判断记录：
        /// 不用 data/_sample 真实资源，本文件需要精确控制两帧颜色 + 资源 id 里含方向段，才能断言
        /// "当前贴图确实是这个方向候选的内容"——sprite.name 由 UnityResourceLoader 按
        /// "&lt;resourceId&gt;_frame&lt;i&gt;" 命名，资源 id 含 "__front__"/"__side_r__"/"__back__"
        /// 时天然可以按子串断言方向）。</summary>
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
        /// <paramref name="resourceId"/> 加载进 <see cref="_resourceLoader"/> 缓存，供热路径（同步命中）
        /// 场景使用；不变量③冷加载场景刻意不调用本方法。</summary>
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
        /// <paramref name="resourceId"/> 出现在缓存里（冷加载场景专用，靠换向触发的真实异步 LoadAsync
        /// 完成，不预热）。</summary>
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

        /// <summary>按当前层名列表定位 <c>Layer_&lt;下标&gt;</c> 子物体的 <see cref="SpriteRenderer"/>；
        /// 层不存在时返回 <c>null</c>。逐字节拷贝自 PerLayerClipEquipAndOverrideTests.FindLayerRenderer。</summary>
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

        /// <summary>本文件公用夹具：4 方向、无 mirror_pairs、<c>paperdoll_layers:["body"]</c>，
        /// <c>display.anim_set</c> 声明 <c>move</c>/<c>attack</c> 两个状态（分别引用
        /// <paramref name="moveResourceRefValue"/>/<paramref name="attackResourceRefValue"/>）——
        /// <paramref name="weaponStyleAutoAttackClipId"/> 为 <c>null</c> 时不装配 <c>IWeaponStyleSource</c>，
        /// Attack 状态走默认剪辑表（<c>anim.default.&lt;map&gt;.attack</c>，验证
        /// <c>GetOrProbePerLayerForDirection</c> 缓存命中同时覆盖多个默认状态）；非 null 时装配
        /// <c>FixedWeaponStyleSource</c>，<c>auto_attack_anim</c> 取该值（验证
        /// <c>GetOrProbeOverrideClipPerLayer</c> 覆盖剪辑缓存命中同一缺陷模式）。</summary>
        private (IEventBus Bus, UnityViewFactory Factory, UnitySpriteView View, Id EntityId, UnityFrameAnimPlayer Player,
            Id MoveClipId, Id AttackClipId, Dictionary<Id, EquipVisualDef> Catalog, EquipmentVisualSource EquipSource)
            BuildFixture(string suffix, string moveResourceRefValue, string attackResourceRefValue, Id? weaponStyleAutoAttackClipId)
        {
            var definitions = new List<EventDefinition>();
            foreach (var key in Core.Foundation.EventBus.EventKeys.All)
            {
                definitions.Add(new EventDefinition(key, key.Domain, Array.Empty<string>()));
            }
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var displayMapIdValue = "display.map.test_0149_" + suffix;
            var animSetIdValue = "display.anim_set.test_0149_" + suffix;
            var weaponStyleIdValue = "display.weapon_style.test_0149_" + suffix;

            var sprite = new SpriteInfo(spriteSetId: "sprite.creature.test_0149_" + suffix, directionCount: 4, paperdollLayers: new[] { "body" });
            var info = new DisplayInfo(
                id: new Id(displayMapIdValue),
                category: DisplayCategory.Creature,
                logicalId: new Id("creature.test_0149_" + suffix),
                kind: DisplayKind.Sprite,
                iconId: null, vfxId: null, sfxId: null, scale: 1.0,
                shadow: Core.Foundation.DisplayInfo.ShadowMode.None, sortOffset: 0.0, weaponStyleRef: null,
                sprite: sprite, model: null);

            var displayInfoRegistry = new FakeDisplayInfoRegistryForAnim();
            displayInfoRegistry.Add(info);

            var dataRegistry = new FakeAnimSetAndWeaponStyleRegistry();

            var animSetJson = "{\"id\":\"" + animSetIdValue + "\",\"clips\":{" +
                "\"move\":{\"resource_ref\":\"" + moveResourceRefValue + "\"}," +
                "\"attack\":{\"resource_ref\":\"" + attackResourceRefValue + "\"}}}";
            var animSetRaw = (JsonObject)JsonReader.Parse(animSetJson);
            var animSetSchema = new TableSchema("display.anim_set", "id", 1, Array.Empty<FieldSchema>());
            dataRegistry.Add("display.anim_set", new DataRecord(animSetSchema, animSetIdValue, new Id(animSetIdValue), animSetRaw));

            FixedWeaponStyleSource? weaponStyleSource = null;
            if (weaponStyleAutoAttackClipId.HasValue)
            {
                var weaponStyleJson = "{\"id\":\"" + weaponStyleIdValue + "\",\"auto_attack_anim\":\"" + weaponStyleAutoAttackClipId.Value.Value + "\"}";
                var weaponStyleRaw = (JsonObject)JsonReader.Parse(weaponStyleJson);
                var weaponStyleSchema = new TableSchema("display.weapon_style", "id", 1, Array.Empty<FieldSchema>());
                dataRegistry.Add("display.weapon_style", new DataRecord(weaponStyleSchema, weaponStyleIdValue, new Id(weaponStyleIdValue), weaponStyleRaw));
                weaponStyleSource = new FixedWeaponStyleSource(new Id(weaponStyleIdValue));
            }

            var equipCatalog = new Dictionary<Id, EquipVisualDef>();
            var equipSource = new EquipmentVisualSource(bus, equipCatalog);

            var entityId = new Id("unit.test_0149_" + suffix + "_" + Guid.NewGuid().ToString("N"));
            var factory = new UnityViewFactory(
                _renderer, new RenderConventionHost(), displayInfoRegistry, _resourceLoader,
                bus: bus, dataRegistry: dataRegistry,
                weaponStyleSource: weaponStyleSource,
                equipVisualByItemInstanceId: equipSource.VisualByItemInstanceId);

            var view = (UnitySpriteView)factory.CreateView(ViewKind.Unit, info.LogicalId, entityId);
            view.Bind(entityId);
            view.SyncPose(Vec2.Zero, Direction.FromQuantized(0.0, 4), height: 0.0);

            var root = _renderer.GetSpriteRoot(view.EngineHandle);
            var player = root!.GetComponentInChildren<UnityFrameAnimPlayer>();
            Assert.IsNotNull(player, "生物分类应当已挂接默认动画");

            var moveClipId = new Id($"anim.default.{displayMapIdValue}.move");
            var attackClipId = new Id($"anim.default.{displayMapIdValue}.attack");

            return (bus, factory, view, entityId, player!, moveClipId, attackClipId, equipCatalog, equipSource);
        }

        private static void Equip(IEventBus bus, UnitySpriteView view, Id entityId, Id itemTemplateId, Id itemInstanceId, Id slotId)
        {
            var equipEvt = new ItemEquippedEvent(entityId, itemInstanceId, slotId);
            bus.PublishImmediate(new ItemAddedEvent(entityId, itemInstanceId, itemTemplateId, count: 1));
            bus.PublishImmediate(equipEvt);
            view.OnEvent(equipEvt);
        }

        /// <summary>消费方反馈第四十九批·反馈 1 核心复现：4 向精灵 + <c>paperdoll_layers:["body"]</c>，
        /// <c>move</c>/<c>attack</c> 两个状态各自的 body 逐层帧集在 front/side_r/back 三个方向各写一份
        /// 可按 <c>Sprite.name</c> 子串区分方向的内容。依次换向 back → side_r → back（第二次到达 back，
        /// 命中 (实体, 方向) 缓存）——根治前 <c>UnityViewFactory.GetOrProbePerLayerForDirection</c>
        /// 命中缓存直接 <c>return cached;</c>，不重新调用 <c>RegisterClipFromEffect</c>，逐层 clipId（与
        /// 方向无关）的内容停留在上一次访问过的 side_r，本用例修复前必然红。同一用例额外覆盖 attack 状态
        /// 第二次到达 back 时的第一帧方向段（消费方原始症状"攻击时身体不朝向目标"，四种起手读数都错在
        /// 这里）——move/attack 共用同一份 (实体, 方向) 缓存（<c>UnityViewFactory.GetOrProbePerLayerForDirection</c>
        /// 一次探测全部六个默认状态），验证重新登记分支正确遍历了不止第一个状态。</summary>
        [UnityTest]
        public IEnumerator ReprobeDirectionAwareAnimation_PerLayerCacheHit_ReregistersCorrectDirection_ForMoveAndAttackStates()
        {
            var moveFront = new Id("sprite_anim.dircache_repro_move__front__body");
            var moveSideR = new Id("sprite_anim.dircache_repro_move__side_r__body");
            var moveBack = new Id("sprite_anim.dircache_repro_move__back__body");
            var attackSideR = new Id("sprite_anim.dircache_repro_attack__side_r__body");
            var attackBack = new Id("sprite_anim.dircache_repro_attack__back__body");

            WriteEffectResource(moveFront, Color.red, Color.green);
            WriteEffectResource(moveSideR, Color.blue, Color.yellow);
            WriteEffectResource(moveBack, Color.black, Color.white);
            WriteEffectResource(attackSideR, Color.cyan, Color.magenta);
            WriteEffectResource(attackBack, Color.gray, new Color(1f, 0.5f, 0f, 1f));
            yield return WarmEffectCache(moveFront);
            yield return WarmEffectCache(moveSideR);
            yield return WarmEffectCache(moveBack);
            yield return WarmEffectCache(attackSideR);
            yield return WarmEffectCache(attackBack);

            var fx = BuildFixture("repro", "sprite_anim.dircache_repro_move", "sprite_anim.dircache_repro_attack", weaponStyleAutoAttackClipId: null);
            var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
            Assert.IsNotNull(layersRoot, "应当已经建好 LayersRoot 子物体");

            // 1st：back（首次探测，(实体, 方向) 缓存未命中）。
            yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick);
            Assert.AreEqual(1, fx.Factory.DirectionAwareReprobeCountForTests, "转到 back 应当恰好触发一次重探测");

            // 2nd：side_r（另一方向首次探测，未命中——把逐层 clipId 的内容覆盖成 side_r，是复现缺陷的
            // 关键前置条件：clipId 与方向无关，跨方向共用同一个 id）。
            yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI, 4), _resourceLoader.Tick);
            Assert.AreEqual(2, fx.Factory.DirectionAwareReprobeCountForTests, "转到 side_r 应当再触发一次重探测");

            // 3rd：back 第二次——命中 (实体, back) 缓存。根治前这里直接 return cached，不重新登记。
            yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick);
            Assert.AreEqual(3, fx.Factory.DirectionAwareReprobeCountForTests, "第二次转到 back 应当仍然触发重探测（命中缓存也算一次重探测尝试）");

            fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
            yield return null;

            var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
            Assert.IsNotNull(bodyRenderer, "body 层应当存在");
            StringAssert.Contains(
                "__back__", bodyRenderer!.sprite.name,
                "消费方反馈第四十九批·反馈 1 核心断言：第二次到达 back 方向后，move 状态身体层应当显示 back " +
                "方向的帧——根治前会一直停在上一次访问过的 side_r 方向，本断言即为红→绿分界线");

            // 同一用例覆盖 attack 状态：同一次 (实体, back) 缓存命中要同时正确重登记全部默认状态，不止
            // 第一个被遍历到的状态。
            fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, new Id("skill.dircache_repro_strike"), castTime: 0.0));
            yield return null;

            Assert.AreEqual(fx.AttackClipId, fx.Player.CurrentClipId!.Value, "应当已经切到 attack 剪辑（未装配武器风格覆盖，走默认剪辑表）");
            var bodyRendererAttack = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
            StringAssert.Contains(
                "__back__", bodyRendererAttack!.sprite.name,
                "消费方反馈第四十九批·反馈 1 核心断言（attack 状态）：第二次到达 back 方向后，attack 状态起手" +
                "第一帧也应当是 back 方向——消费方原始症状'攻击时身体不朝向目标'，四种起手读数都错在这里");

            fx.EquipSource.Dispose();
            fx.View.Destroy();
        }

        /// <summary>
        /// 不变量（4 个分支合一，各用独立实体避免互相干扰，同 PerLayerClipEquipAndOverrideTests 既有
        /// 组织方式）：
        /// ① (实体, 方向) 缓存命中重新登记不应该重置播放进度（ADR-0093 决策 2"保持时间轴"）。
        /// ② 覆盖剪辑（武器风格）逐层缓存第二次命中同一方向同样需要重新登记（<c>UnityViewFactory.GetOrProbeOverrideClipPerLayer</c>
        ///    与默认状态同一缺陷模式，消费方未实测，按任务书要求一并覆盖）。
        /// ③ 冷加载：换向那一刻目标方向的逐层帧集尚未加载完成，加载完成后方向正确；再次换向（含换回
        ///    冷加载已落定的方向）缓存命中同样正确——冷热路径归于同一出口。
        /// ④ 没有任何逐层剪辑候选的静态装备层（mainhand）不受 (实体, 方向) 缓存重新登记影响：本次改动
        ///    只遍历已经在 layerMap 里的层，不应该误伤从未命中过、只走静态合成的层。
        /// </summary>
        [UnityTest]
        public IEnumerator Invariant_PreservesProgress_OverrideClipCacheHit_ColdLoadCatchesUp_StaticLayerUnaffected()
        {
            // ---------- ① 缓存命中不重置播放进度 ----------
            {
                var frontRef = new Id("sprite_anim.dircache_inv1_move__front__body");
                var backRef = new Id("sprite_anim.dircache_inv1_move__back__body");
                WriteEffectResource(frontRef, Color.red, Color.green);
                WriteEffectResource(backRef, Color.blue, Color.yellow);
                yield return WarmEffectCache(frontRef);
                yield return WarmEffectCache(backRef);

                var fx = BuildFixture("inv1", "sprite_anim.dircache_inv1_move", "sprite_anim.dircache_inv1_attack_unused", weaponStyleAutoAttackClipId: null);

                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI / 2.0, 4), _resourceLoader.Tick); // front，1st，未命中
                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));

                var elapsed = 0f;
                while (elapsed < 0.3f)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                }
                var frameBeforeSwitch = fx.Player.CurrentFrame;
                Assert.Greater(frameBeforeSwitch, 0, "前置条件：播放应当已经推进到非 0 帧（单帧 0.25 秒，已等待 0.3 秒）");

                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick); // back，2nd，未命中
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI / 2.0, 4), _resourceLoader.Tick); // front 第二次，3rd，命中缓存
                yield return null;

                Assert.Greater(
                    fx.Player.CurrentFrame, 0,
                    "不变量①：(实体, 方向) 缓存命中重新登记不应该重置播放进度（ADR-0093 决策 2\"保持时间轴\"，" +
                    "RegisterClipFromEffect 对同一 clipId 原地覆盖内容，不重置 FrameAnimPlayer 已经过的播放时间）");
                Assert.AreEqual(fx.MoveClipId, fx.Player.CurrentClipId!.Value, "换向缓存命中不应该打断正在播放的状态本身");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ② 覆盖剪辑逐层缓存第二次命中同一方向 ----------
            {
                var autoAttackClipId = new Id("sprite_anim.dircache_inv2_ovr_atk");
                var ovrFront = new Id("sprite_anim.dircache_inv2_ovr_atk__front__body");
                var ovrBack = new Id("sprite_anim.dircache_inv2_ovr_atk__back__body");
                WriteEffectResource(ovrFront, Color.red, Color.green);
                WriteEffectResource(ovrBack, Color.blue, Color.yellow);
                yield return WarmEffectCache(ovrFront);
                yield return WarmEffectCache(ovrBack);

                var fx = BuildFixture(
                    "inv2", "sprite_anim.dircache_inv2_move_unused", "sprite_anim.dircache_inv2_attack_unused",
                    weaponStyleAutoAttackClipId: autoAttackClipId);
                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);

                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick); // back
                fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.EntityId, new Id("skill.dircache_inv2_strike"), castTime: 0.0));
                yield return null;

                Assert.AreEqual(autoAttackClipId, fx.Player.CurrentClipId!.Value, "应当已经切到武器风格覆盖剪辑（back，首次探测）");
                var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                StringAssert.Contains("__back__", bodyRenderer!.sprite.name, "覆盖剪辑首次探测应当命中 back 方向逐层夹具");

                // 换向 front：覆盖剪辑逐层缓存对该方向首次探测（未命中），把 clipId 内容覆盖成 front。
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI / 2.0, 4), _resourceLoader.Tick);
                yield return null;
                bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                StringAssert.Contains("__front__", bodyRenderer!.sprite.name, "前置条件：换向 front 后覆盖剪辑应当先切到 front 方向内容");

                // 换回 back 第二次：命中 (实体, clipId, back) 缓存。
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick);
                yield return null;
                bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                StringAssert.Contains(
                    "__back__", bodyRenderer!.sprite.name,
                    "不变量②核心断言：覆盖剪辑逐层缓存第二次命中同一方向（back）应当重新登记为 back 内容，不" +
                    "应该停留在 front（GetOrProbeOverrideClipPerLayer 与 GetOrProbePerLayerForDirection 同一" +
                    "缺陷模式，消费方未实测，按任务书要求一并修复）");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ③ 冷加载：换向那一刻目标方向尚未加载完成 ----------
            {
                var frontRef = new Id("sprite_anim.dircache_inv3_move__front__body");
                var backRef = new Id("sprite_anim.dircache_inv3_move__back__body");
                WriteEffectResource(frontRef, Color.red, Color.green);
                WriteEffectResource(backRef, Color.blue, Color.yellow); // 写盘但不预热——冷加载。
                yield return WarmEffectCache(frontRef);

                var fx = BuildFixture("inv3", "sprite_anim.dircache_inv3_move", "sprite_anim.dircache_inv3_attack_unused", weaponStyleAutoAttackClipId: null);
                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);

                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI / 2.0, 4), _resourceLoader.Tick); // front，热路径，未命中
                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;
                var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                StringAssert.Contains("__front__", bodyRenderer!.sprite.name, "热路径前置条件：front 方向应当已经正确显示");

                // 换到 back：这一刻 backRef 尚未加载完成（冷加载在途），(实体, back) 缓存条目已创建但
                // body 层还没有已解析的效果——冷加载路径必须与热路径同一出口（AGENTS.md 不变量），不允许
                // 被写成"已知限制"。
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick);
                yield return WaitUntilEffectCached(backRef);
                yield return WaitUntilOrFail(
                    () =>
                    {
                        var r = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                        return r != null && r.sprite != null && r.sprite.name.Contains("__back__");
                    },
                    "不变量③：冷加载完成后 body 层应当追上并显示 back 方向的帧，与预热完成后的热路径最终状态一致",
                    timeoutSeconds: 3f);

                // 再换回 front：命中 (实体, front) 缓存——验证冷加载落定之后，缓存命中路径仍然正确。
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI / 2.0, 4), _resourceLoader.Tick);
                yield return null;
                bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                StringAssert.Contains("__front__", bodyRenderer!.sprite.name, "冷加载落定后再次命中 front 缓存仍应正确");

                // 再换回 back 第二次：命中 (实体, back) 缓存（效果此前已经异步解析完成并写入了缓存对象）
                // ——核心断言：缓存命中重新登记同样正确，不停留在 front。
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick);
                yield return null;
                bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                StringAssert.Contains(
                    "__back__", bodyRenderer!.sprite.name,
                    "不变量③核心断言：冷加载落定后的 (实体, back) 缓存命中同样需要重新登记，与热路径同一出口");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }

            // ---------- ④ 静态装备层（无逐层剪辑候选）不受影响 ----------
            {
                var frontRef = new Id("sprite_anim.dircache_inv4_move__front__body");
                var sideRRef = new Id("sprite_anim.dircache_inv4_move__side_r__body");
                var backRef = new Id("sprite_anim.dircache_inv4_move__back__body");
                WriteEffectResource(frontRef, Color.red, Color.green);
                WriteEffectResource(sideRRef, Color.black, Color.white);
                WriteEffectResource(backRef, Color.blue, Color.yellow);
                yield return WarmEffectCache(frontRef);
                yield return WarmEffectCache(sideRRef);
                yield return WarmEffectCache(backRef);

                var fx = BuildFixture("inv4", "sprite_anim.dircache_inv4_move", "sprite_anim.dircache_inv4_attack_unused", weaponStyleAutoAttackClipId: null);
                var slotId = new Id("slot.mainhand");
                var itemTemplateId = new Id("item.test_0149_inv4_static");
                var itemInstanceId = new Id("item_instance.test_0149_inv4_static_1");
                // mesh_ref 没有任何 sprite_anim 候选（两级候选均不存在于磁盘）——逐层探测恒 onExhausted，
                // 本层永远不会出现在 (实体, 方向) 缓存的 layerMap 里，只走静态合成（RecomposeAndApply）。
                fx.Catalog[itemTemplateId] = new EquipVisualDef(
                    new Id("display.equip_visual.test_0149_inv4_static"), itemTemplateId, EquipVisualMode.SlotMesh,
                    slotId: slotId, meshRef: new Id("mesh.static_only_inv4"), socketId: null, modelRef: null);
                Equip(fx.Bus, fx.View, fx.EntityId, itemTemplateId, itemInstanceId, slotId);

                fx.Bus.PublishImmediate(new UnitStateChangedEvent(fx.EntityId, "Idle", "Walk"));
                yield return null;

                var layersRoot = _renderer.GetLayersRoot(fx.View.EngineHandle);
                var mainhandRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "mainhand");
                Assert.IsNotNull(mainhandRenderer, "装备后 mainhand 应当作为静态层存在");

                // 方向环：back -> side_r -> back（命中缓存）——与复现用例同一套换向节奏，验证不会因为
                // 逐层缓存重新登记逻辑而对 mainhand 产生任何副作用（不抛异常、不被误登记进逐层动画）。
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick);
                yield return null;
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI, 4), _resourceLoader.Tick);
                yield return null;
                yield return DirectionSwitchSettle.SyncUntilCommitted(fx.View, Direction.FromQuantized(Math.PI * 1.5, 4), _resourceLoader.Tick);
                yield return null;

                var bodyRenderer = FindLayerRenderer(fx.View.EngineHandle, layersRoot!, "body");
                var mainhandSpriteAfterCycle = mainhandRenderer!.sprite;
                var bodySpriteAfterCycle = bodyRenderer!.sprite;

                // 判断记录：用固定时长的 WaitForSecondsRealtime 断言"body 帧一定变了"存在与帧循环周期
                // 对齐的巧合风险（body 效果 2 帧 * 0.25s = 0.5s 一个循环，等待时长恰好落在循环整数倍附近时
                // 会重新落回同一帧下标，并非动画停摆）。改为轮询等待"body 贴图与基线不同"，超时时间覆盖多个
                // 循环周期，既证明时间轴真的在推进，又不受具体等待时长与帧周期是否对齐影响。
                yield return WaitUntilOrFail(
                    () => bodyRenderer!.sprite != bodySpriteAfterCycle,
                    "对照：body 层应当继续随共享时间轴推进正常切换贴图（证明本轮换向后动画本身仍在正常工作，" +
                    "mainhand 的静止不是因为整个逐层动画停摆）",
                    timeoutSeconds: 2f);

                Assert.AreEqual(
                    mainhandSpriteAfterCycle, mainhandRenderer!.sprite,
                    "不变量④：没有任何逐层剪辑候选的静态装备层应当继续维持静态，不随共享时间轴推进切换贴图" +
                    "——(实体, 方向) 缓存命中重新登记只应该遍历已经在 layerMap 里的层，不应该误伤从未命中过的层");

                fx.EquipSource.Dispose();
                fx.View.Destroy();
            }
        }
    }
}
