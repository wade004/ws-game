#nullable enable
using System.Collections;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class UnityResourceLoaderTests : PlayModeTestBase
    {
        private UnityResourceLoader _loader = null!;

        [SetUp]
        public void SetUp()
        {
            _loader = new UnityResourceLoader();
        }

        [Test]
        public void IsLoaded_BeforeAnyLoad_ReturnsFalse()
        {
            Assert.IsFalse(_loader.IsLoaded(new Id("sprite.sample_unknown")));
            Assert.AreEqual(0.0, _loader.GetLoadProgress(new Id("sprite.sample_unknown")));
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingResource_InvokesCallbackWithFalse()
        {
            var resourceId = new Id("sprite.sample_missing");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Image, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success);
            Assert.IsFalse(success!.Value);
            Assert.IsFalse(_loader.IsLoaded(resourceId));
        }

        [UnityTest]
        public IEnumerator LoadAsync_GetLoadProgress_IsInProgressOrDoneAfterCallback()
        {
            var resourceId = new Id("data.sample_table");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.DataTable, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= 0.02f;
            }

            Assert.IsNotNull(success);
            // 资源不存在，最终进度应回落到 0（未加载）。
            Assert.AreEqual(0.0, _loader.GetLoadProgress(resourceId));
        }

        [Test]
        public void ResolvePath_StripsCategoryPrefixAndUsesKindSubfolder()
        {
            var path = UnityResourceLoader.ResolvePath(new Id("sprite.creature.sample_wolf"), ResourceKind.Image);

            StringAssert.Contains("sprites", path);
            StringAssert.Contains("creature_sample_wolf.png", path);
        }

        [Test]
        public void ResolvePath_LayerCategory_ResolvesNestedDirectionLayerPath()
        {
            var path = UnityResourceLoader.ResolvePath(new Id("layer.placeholder_hero__front__body"), ResourceKind.Image);

            var expectedSuffix = System.IO.Path.Combine("sprites", "placeholder_hero", "front", "body.png");
            StringAssert.EndsWith(expectedSuffix, path);
        }

        [Test]
        public void ResolvePath_SceneAndNavMesh_UseDedicatedSubfolders()
        {
            // ADR-0016 决策 5：Scene/NavMesh 不再借用 DataTable 的 "data/" 子目录。
            var scenePath = UnityResourceLoader.ResolvePath(new Id("scene.sample_field"), ResourceKind.Scene);
            var navPath = UnityResourceLoader.ResolvePath(new Id("nav.sample_field"), ResourceKind.NavMesh);

            StringAssert.Contains("scene", scenePath);
            StringAssert.EndsWith("sample_field.json", scenePath);
            StringAssert.Contains("nav_mesh", navPath);
            StringAssert.EndsWith("sample_field.json", navPath);
        }

        [Test]
        public void ResolveEffectDir_StripsCategoryPrefix_UsesVfxSubfolder()
        {
            var dir = UnityResourceLoader.ResolveEffectDir(new Id("vfx.hit_spark"));

            StringAssert.Contains("vfx", dir);
            StringAssert.EndsWith(System.IO.Path.Combine("vfx", "hit_spark"), dir);
        }

        /// <summary>ADR-0038 适配层接线（2026-09-19）：sprite 型动画剪辑改用 sprite_anim 前缀后，
        /// ResolveEffectDir 必须按类别前缀分派到独立的 sprite_anim 子目录，不能像此前那样固定落进
        /// vfx 子目录（见该方法判断记录）。</summary>
        [Test]
        public void ResolveEffectDir_SpriteAnimCategory_UsesSpriteAnimSubfolder()
        {
            var dir = UnityResourceLoader.ResolveEffectDir(new Id("sprite_anim.sample_hero_idle"));

            StringAssert.Contains("sprite_anim", dir);
            StringAssert.DoesNotContain(
                System.IO.Path.Combine("vfx", "sample_hero_idle"), dir,
                "sprite_anim 类别不应落进 vfx 子目录（此前固定拼接的遗留错误行为）");
            StringAssert.EndsWith(System.IO.Path.Combine("sprite_anim", "sample_hero_idle"), dir);
        }

        /// <summary>一致性对照测试（ADR-0038 适配层接线，见 AGENTS.md"一致性守护"惯例）：
        /// UnityResourceLoader.ResolveEffectDir 现直接转发 AssetRefConventions.ResolvePathSpace，
        /// 本用例锚定"适配层产出的目录 = RootDir + 契约面产出的相对路径"这一关系本身，防止未来有人
        /// 在其中一侧改动分派规则却忘了另一侧（虽然当前是同一次调用，结构上不会漂移，但作为回归锚点
        /// 保留，同 core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs 的对照测试
        /// 惯例）。</summary>
        [Test]
        public void ResolveEffectDir_MatchesAssetRefConventionsResolvePathSpace_ForVfxAndSpriteAnim()
        {
            foreach (var resourceRef in new[] { "vfx.hit_spark", "sprite_anim.sample_hero_idle" })
            {
                var id = new Id(resourceRef);
                var (_, expectedRelative) = Core.Foundation.EngineAdapter.AssetRefConventions.ResolvePathSpace(id);
                var dir = UnityResourceLoader.ResolveEffectDir(id);

                StringAssert.EndsWith(
                    expectedRelative.Replace('/', System.IO.Path.DirectorySeparatorChar), dir,
                    $"ResolveEffectDir(\"{resourceRef}\") 应以 AssetRefConventions.ResolvePathSpace 给出的相对路径结尾");
            }
        }

        /// <summary>ADR-0038 适配层接线（2026-09-19）：display.equip_visual.mesh_ref 的 sprite 型取值
        /// 改用 paperdoll 前缀后，ResolvePath 必须转发 AssetRefConventions.PaperdollLayerFile 解析为
        /// 单个扁平文件，不能像此前那样落进"sprites/&lt;name&gt;.png"的通用回退分支（该分支此后只
        /// 覆盖 sprite 类别本身，见该方法判断记录）。</summary>
        [Test]
        public void ResolvePath_PaperdollCategory_ResolvesFlatPngUnderPaperdollSubfolder()
        {
            var path = UnityResourceLoader.ResolvePath(new Id("paperdoll.item.sample_hero_hat_test"), ResourceKind.Image);

            StringAssert.Contains("paperdoll", path);
            StringAssert.EndsWith(
                System.IO.Path.Combine("paperdoll", "item_sample_hero_hat_test.png"), path);
        }

        /// <summary>ADR-0038 适配层接线：ResolveModelResourcesPath/ResolveAnimClipResourcesPath 现直接
        /// 转发 AssetRefConventions 的对应公开方法（见类型顶部"ADR-0038 适配层接线"判断记录），本用例
        /// 锚定二者字符串结果始终一致，防止未来重新分叉出第二份拼接实现。</summary>
        [Test]
        public void ResolveModelResourcesPathAndResolveAnimClipResourcesPath_MatchAssetRefConventions()
        {
            var modelId = new Id("model.placeholder_biped");
            Assert.AreEqual(
                Core.Foundation.EngineAdapter.AssetRefConventions.ModelLogicalPath(modelId),
                UnityResourceLoader.ResolveModelResourcesPath(modelId));

            var animId = new Id("anim.attack");
            Assert.AreEqual(
                Core.Foundation.EngineAdapter.AssetRefConventions.AnimClipLogicalPath(animId),
                UnityResourceLoader.ResolveAnimClipResourcesPath(animId));
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingSceneResource_InvokesCallbackWithFalse()
        {
            var resourceId = new Id("scene.sample_missing");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Scene, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= 0.02f;
            }

            Assert.IsNotNull(success);
            Assert.IsFalse(success!.Value);
        }

        [UnityTest]
        public IEnumerator LoadAsync_MissingEffectResource_InvokesCallbackWithFalse()
        {
            var resourceId = new Id("vfx.sample_missing_effect");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Effect, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= 0.02f;
            }

            Assert.IsNotNull(success);
            Assert.IsFalse(success!.Value);
            Assert.IsFalse(_loader.TryGetEffect(resourceId, out _));
        }

        /// <summary>依赖 build.ps1 -SyncContent 已经把 assets/_placeholder/vfx/ 同步进
        /// StreamingAssets/GameFoundation/vfx/（见包 README"内容同步"一节），同
        /// LoadAsync_PlaceholderHeroFrontBody_LoadsWithExpectedSize 判断记录。</summary>
        [UnityTest]
        public IEnumerator LoadAsync_PlaceholderHitSparkEffect_LoadsFramesSuccessfully()
        {
            var resourceId = new Id("vfx.hit_spark");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Effect, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(success!.Value,
                "占位 hit_spark 特效资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步占位资产");
            Assert.IsTrue(_loader.TryGetEffect(resourceId, out var asset));
            Assert.AreEqual(8, asset.Frames.Length, "assets/_placeholder/vfx/hit_spark/frames.json 声明了 8 帧");
            Assert.IsFalse(asset.Loop, "assets/_placeholder/vfx/hit_spark/frames.json 的 loop 为 false");
        }

        /// <summary>U2-1 验收要求（任务书原句）："加载占位英雄 front/body 成功、宽高 64×96"。依赖
        /// build.ps1 -SyncContent 已经把 assets/_placeholder/sprites/ 同步进
        /// StreamingAssets/GameFoundation/sprites/（见包 README"内容同步"一节）；命令行跑测试前
        /// 需要先跑过一次同步，测试本身不负责触发同步。</summary>
        [UnityTest]
        public IEnumerator LoadAsync_PlaceholderHeroFrontBody_LoadsWithExpectedSize()
        {
            var resourceId = new Id("layer.placeholder_hero__front__body");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Image, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(success!.Value,
                "占位英雄 front/body 层资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步占位资产");
            Assert.IsTrue(_loader.TryGetSprite(resourceId, out var sprite));
            Assert.AreEqual(64, sprite.rect.width, 0.01f, "占位英雄精灵宽度应为 64 像素（见 assets/_placeholder/MANIFEST.json）");
            Assert.AreEqual(96, sprite.rect.height, 0.01f, "占位英雄精灵高度应为 96 像素（见 assets/_placeholder/MANIFEST.json）");
        }

        /// <summary>
        /// [ADR-0081](../../../../../../../architecture/adr/0081-精灵集自带像素密度在运行期生效.md)
        /// 决策 1 主路径实测：占位英雄精灵集 <c>assets/_placeholder/sprites/placeholder_hero/anchors.json</c>
        /// 顶层声明 <c>"pixels_per_unit": 32</c>（<see cref="DeclaredPixelsPerUnit"/>），画布像素尺寸
        /// 64×96（同 <see cref="LoadAsync_PlaceholderHeroFrontBody_LoadsWithExpectedSize"/>）。期望的
        /// 世界尺寸由"画布像素 ÷ 该集声明的 pixels_per_unit"这条规则在用例里现算，不写死裸数——同时
        /// 与"若忽略声明、沿用加载器全局默认值会得到的尺寸"对照，证明同一张图在声明生效前后渲染尺寸
        /// 确实不同（不是恰好凑巧相等）。
        /// </summary>
        [UnityTest]
        public IEnumerator LoadAsync_PlaceholderHeroFrontBody_UsesDeclaredPixelsPerUnit_NotGlobalDefault()
        {
            const float canvasWidthPx = 64f;
            const float canvasHeightPx = 96f;
            const float declaredPixelsPerUnit = 32f; // assets/_placeholder/sprites/placeholder_hero/anchors.json 顶层 pixels_per_unit

            var resourceId = new Id("layer.placeholder_hero__front__body");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Image, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(success!.Value,
                "占位英雄 front/body 层资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步占位资产");
            Assert.IsTrue(_loader.TryGetSprite(resourceId, out var sprite));

            var expectedWorldWidth = canvasWidthPx / declaredPixelsPerUnit;
            var expectedWorldHeight = canvasHeightPx / declaredPixelsPerUnit;
            var worldWidthIfGlobalDefaultUsed = canvasWidthPx / _loader.PixelsPerUnit;

            Assert.AreEqual(expectedWorldWidth, sprite.bounds.size.x, 0.001f,
                "精灵世界宽度应等于画布像素宽度 ÷ 精灵集自己声明的 pixels_per_unit（ADR-0081 决策 1）");
            Assert.AreEqual(expectedWorldHeight, sprite.bounds.size.y, 0.001f,
                "精灵世界高度应等于画布像素高度 ÷ 精灵集自己声明的 pixels_per_unit（ADR-0081 决策 1）");
            Assert.Greater(UnityEngine.Mathf.Abs(worldWidthIfGlobalDefaultUsed - sprite.bounds.size.x), 0.001f,
                "声明的 pixels_per_unit 与加载器全局默认值不同，实测世界尺寸必须随之真的变化，" +
                "不能仍是忽略声明、沿用全局默认值算出的尺寸");
        }

        /// <summary>
        /// [ADR-0081](../../../../../../../architecture/adr/0081-精灵集自带像素密度在运行期生效.md)
        /// 决策 1 回退路径实测：<c>assets/_sample/sprites/creature_sample_hero/anchors.json</c>
        /// （`toolchain/asset_import/sprite_cmd.py` 产出，"按方向档位分层" schema）顶层没有
        /// <c>pixels_per_unit</c> 字段（见该 ADR"背景"一节落地期核实结论），画布像素尺寸同样是
        /// 64×96（`front`/`canvas_size`）。渲染尺寸应等于"画布像素 ÷ 加载器全局 PixelsPerUnit"，
        /// 与改动前逐字节一致——回退路径不应受本次改动影响。
        /// </summary>
        [UnityTest]
        public IEnumerator LoadAsync_SampleHeroFrontBody_NoDeclaredPixelsPerUnit_FallsBackToGlobalDefault()
        {
            const float canvasWidthPx = 64f;
            const float canvasHeightPx = 96f;

            var resourceId = new Id("layer.creature_sample_hero__front__body");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Image, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(success!.Value,
                "样例英雄 front/body 层资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步样例资产");
            Assert.IsTrue(_loader.TryGetSprite(resourceId, out var sprite));

            var expectedWorldWidth = canvasWidthPx / _loader.PixelsPerUnit;
            var expectedWorldHeight = canvasHeightPx / _loader.PixelsPerUnit;

            Assert.AreEqual(expectedWorldWidth, sprite.bounds.size.x, 0.001f,
                "anchors.json 未声明 pixels_per_unit 时，精灵世界宽度应回退为画布像素宽度 ÷ 加载器全局 PixelsPerUnit");
            Assert.AreEqual(expectedWorldHeight, sprite.bounds.size.y, 0.001f,
                "anchors.json 未声明 pixels_per_unit 时，精灵世界高度应回退为画布像素高度 ÷ 加载器全局 PixelsPerUnit");
        }

        /// <summary>
        /// [ADR-0081](../../../../../../../architecture/adr/0081-精灵集自带像素密度在运行期生效.md)
        /// 决策 6/7 缓存实测：同一精灵集（占位英雄）连续解码两张不同的层图片（front/body、
        /// front/head），<c>anchors.json</c> 只应被实际读盘+解析一次——第二次解码应命中
        /// <see cref="UnityResourceLoader.SpriteSetAnchorsJsonReadCount"/> 缓存，不重复触发磁盘 IO。
        /// </summary>
        [UnityTest]
        public IEnumerator LoadAsync_TwoImagesInSameSpriteSet_ReadsAnchorsJsonOnlyOnce()
        {
            var firstId = new Id("layer.placeholder_hero__front__body");
            var secondId = new Id("layer.placeholder_hero__front__head");
            bool? firstSuccess = null;
            bool? secondSuccess = null;

            Assert.AreEqual(0, _loader.SpriteSetAnchorsJsonReadCount, "加载任何资源之前不应发生过 anchors.json 读取");

            _loader.LoadAsync(firstId, ResourceKind.Image, (id, ok) => firstSuccess = ok);

            var timeout = 5f;
            while (firstSuccess == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(firstSuccess, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(firstSuccess!.Value, "占位英雄 front/body 层资源加载应当成功");
            Assert.AreEqual(1, _loader.SpriteSetAnchorsJsonReadCount, "解码同一精灵集第一张图应当触发恰好一次 anchors.json 读取");

            _loader.LoadAsync(secondId, ResourceKind.Image, (id, ok) => secondSuccess = ok);

            timeout = 5f;
            while (secondSuccess == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(secondSuccess, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(secondSuccess!.Value, "占位英雄 front/head 层资源加载应当成功");
            Assert.AreEqual(1, _loader.SpriteSetAnchorsJsonReadCount,
                "同一精灵集下解码第二张图应当命中缓存，anchors.json 读取计数应仍为 1（不应变成 2）");
        }

        /// <summary>[ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md)
        /// 用完必须还原（该字段是 static，跨全部加载器实例共享，不还原会串味到其它用例，见该字段
        /// 类型注释）。</summary>
        [TearDown]
        public void TearDownRootDirOverride()
        {
            UnityResourceLoader.RootDirOverrideForTests = null;
            if (_tempFixtureRoot != null && System.IO.Directory.Exists(_tempFixtureRoot))
            {
                System.IO.Directory.Delete(_tempFixtureRoot, true);
            }
            _tempFixtureRoot = null;
        }

        private string? _tempFixtureRoot;

        /// <summary>
        /// [ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md) 决策 1
        /// 复现用例（修复前应失败）：占位野兽精灵集 assets/_placeholder/sprites/placeholder_beast/
        /// anchors.json（结构①）声明 <c>directions.front.root = [24, 44]</c>，画布 48x48（与实际解码
        /// 出的纹理尺寸一致，见 <c>front/body.png</c>）。期望枢轴由"root 像素坐标 ÷ 实际纹理宽高、
        /// Y 轴翻转（anchors.json 原点左上，Unity Sprite.pivot 原点左下）"这条规则在用例里现算，不
        /// 写死裸数。<c>Sprite.pivot</c> 属性本身返回的是像素坐标（相对 <c>rect</c>），除以
        /// <c>rect.width</c>/<c>rect.height</c> 换算回归一化值，与传给 <c>Sprite.Create</c> 的
        /// <c>pivot</c> 实参同一量纲。
        /// </summary>
        [UnityTest]
        public IEnumerator LoadAsync_PlaceholderBeastFrontBody_PivotComesFromAnchorsRoot()
        {
            const double rootX = 24d;
            const double rootY = 44d;

            var resourceId = new Id("layer.placeholder_beast__front__body");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Image, (id, ok) => success = ok);

            var timeout = 5f;
            while (success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(success, "加载在超时前应当有结果（成功或失败），不应当悬而不决");
            Assert.IsTrue(success!.Value,
                "占位野兽 front/body 层资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步占位资产");
            Assert.IsTrue(_loader.TryGetSprite(resourceId, out var sprite));

            var expectedPivotX = (float)(rootX / sprite.rect.width);
            var expectedPivotY = 1f - (float)(rootY / sprite.rect.height);
            var actualPivotX = sprite.pivot.x / sprite.rect.width;
            var actualPivotY = sprite.pivot.y / sprite.rect.height;

            Assert.AreEqual(expectedPivotX, actualPivotX, 0.001f,
                "枢轴 X 应等于脚底锚点像素 X ÷ 实际纹理宽度（ADR-0091 决策 1）；修复前固定写死 0.5，" +
                "只有画布恰好左右对称的锚点才会碰巧相等——本例宽度 48、root.x=24 时两者数值相同，" +
                "不足以证明枢轴取自 anchors.json，真正的区分力在下面的 Y 轴断言");
            Assert.AreEqual(expectedPivotY, actualPivotY, 0.001f,
                "枢轴 Y 应等于 1 - 脚底锚点像素 Y ÷ 实际纹理高度（原点左下 vs anchors.json 原点左上，" +
                "ADR-0091 决策 1）；修复前固定写死 0.5，root.y=44/canvas 44 时期望值约 0.083，" +
                "与写死值 0.5 有显著差异，能证伪未接入 anchors.json 的旧实现");
            Assert.Greater(UnityEngine.Mathf.Abs(actualPivotY - 0.5f), 0.001f,
                "期望的枢轴 Y 与默认写死值 (0.5) 必须有实测差异，否则本用例无法区分'枢轴取自 anchors.json' " +
                "与'仍是写死默认值、恰好数值相同'两种情况");
        }

        /// <summary>
        /// [ADR-0091](../../../../../../../architecture/adr/0091-精灵枢轴取自精灵集脚底锚点.md) 不变量
        /// 用例（三分支合一）：
        /// <list type="number">
        /// <item>不属于任何精灵集的图像（paperdoll 类别，无伴生 anchors.json）——枢轴保持默认
        /// (0.5, 0.5)，同改动前逐字节一致（决策 3）。</item>
        /// <item>结构②（<c>toolchain/asset_import/sprite_cmd.py</c> 产出的"按方向档位分层"
        /// anchors.json）——本用例用 <see cref="UnityResourceLoader.RootDirOverrideForTests"/> 把
        /// <c>RootDir</c> 临时指向 scratchpad 下现造的一个精灵集夹具（不进 Unity 资产管线，不触发
        /// meta 门禁），验证该结构同样能正确算出枢轴（决策 1）。</item>
        /// <item>root 越界（不在 <c>[0,w]×[0,h]</c>）——回退默认枢轴 (0.5, 0.5)（决策 3）。</item>
        /// </list>
        /// </summary>
        [UnityTest]
        public IEnumerator LoadAsync_Image_PivotInvariants_NonSpriteSetStructure2AndOutOfBoundsRoot()
        {
            // 分支 1：不属于任何精灵集的图像——沿用既有用例 ResolvePath_PaperdollCategory_... 已验证
            // 过的真实同步资产，加载后枢轴应为默认值，不做任何 anchors.json 查找。
            var paperdollId = new Id("paperdoll.item.sample_hero_hat_test");
            bool? paperdollSuccess = null;
            _loader.LoadAsync(paperdollId, ResourceKind.Image, (id, ok) => paperdollSuccess = ok);
            var timeout = 5f;
            while (paperdollSuccess == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }
            Assert.IsNotNull(paperdollSuccess, "加载在超时前应当有结果，不应当悬而不决");
            Assert.IsTrue(paperdollSuccess!.Value,
                "纸娃娃层测试资源加载应当成功——若失败，请先跑一次 build.ps1 -SyncContent 同步样例资产");
            Assert.IsTrue(_loader.TryGetSprite(paperdollId, out var paperdollSprite));
            Assert.AreEqual(0.5f, paperdollSprite.pivot.x / paperdollSprite.rect.width, 0.001f,
                "不属于任何精灵集的图像（paperdoll 类别）枢轴 X 应保持默认 0.5（ADR-0091 决策 3）");
            Assert.AreEqual(0.5f, paperdollSprite.pivot.y / paperdollSprite.rect.height, 0.001f,
                "不属于任何精灵集的图像（paperdoll 类别）枢轴 Y 应保持默认 0.5（ADR-0091 决策 3）");

            // 分支 2/3 共用同一个临时精灵集根目录，覆盖结构②解析 + root 越界回退。
            _tempFixtureRoot = System.IO.Path.Combine(
                UnityEngine.Application.temporaryCachePath, "adr0091_fixture_" + System.Guid.NewGuid().ToString("N"));

            const int canvasW = 40;
            const int canvasH = 60;
            WritePngFixture(System.IO.Path.Combine(_tempFixtureRoot, "sprites", "testcat_testname.png"), canvasW, canvasH);
            WriteTextFixture(
                System.IO.Path.Combine(_tempFixtureRoot, "sprites", "testcat_testname", "anchors.json"),
                "{\"front\": {\"canvas_size\": [40, 60], \"anchors\": {\"root\": [20, 55]}}, " +
                "\"back\": {\"canvas_size\": [40, 60], \"anchors\": {\"root\": [20, 55]}}}");

            WritePngFixture(System.IO.Path.Combine(_tempFixtureRoot, "sprites", "testcat_oob.png"), canvasW, canvasH);
            WriteTextFixture(
                System.IO.Path.Combine(_tempFixtureRoot, "sprites", "testcat_oob", "anchors.json"),
                "{\"front\": {\"canvas_size\": [40, 60], \"anchors\": {\"root\": [999, 999]}}}");

            UnityResourceLoader.RootDirOverrideForTests = _tempFixtureRoot;

            // 分支 2：结构②，front/back 两个方向 root 相同（testname 资源不带方向信息，走决策 2
            // "全部方向相同则直接用、不算歧义"分支），期望枢轴 = (20/40, 1-55/60)。
            var structure2Id = new Id("sprite.testcat.testname");
            bool? structure2Success = null;
            _loader.LoadAsync(structure2Id, ResourceKind.Image, (id, ok) => structure2Success = ok);
            timeout = 5f;
            while (structure2Success == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }
            Assert.IsNotNull(structure2Success, "加载在超时前应当有结果，不应当悬而不决");
            Assert.IsTrue(structure2Success!.Value, "结构②临时精灵集夹具加载应当成功");
            Assert.IsTrue(_loader.TryGetSprite(structure2Id, out var structure2Sprite));
            Assert.AreEqual(20f / canvasW, structure2Sprite.pivot.x / structure2Sprite.rect.width, 0.001f,
                "结构②（按方向档位分层）anchors.json 应能正确算出枢轴 X（ADR-0091 决策 1）");
            Assert.AreEqual(1f - 55f / canvasH, structure2Sprite.pivot.y / structure2Sprite.rect.height, 0.001f,
                "结构②（按方向档位分层）anchors.json 应能正确算出枢轴 Y（ADR-0091 决策 1）");

            // 分支 3：root [999,999] 越界（画布只有 40x60），回退默认枢轴。
            var oobId = new Id("sprite.testcat.oob");
            bool? oobSuccess = null;
            _loader.LoadAsync(oobId, ResourceKind.Image, (id, ok) => oobSuccess = ok);
            timeout = 5f;
            while (oobSuccess == null && timeout > 0f)
            {
                _loader.Tick();
                yield return null;
                timeout -= UnityEngine.Time.unscaledDeltaTime > 0 ? UnityEngine.Time.unscaledDeltaTime : 0.02f;
            }
            Assert.IsNotNull(oobSuccess, "加载在超时前应当有结果，不应当悬而不决");
            Assert.IsTrue(oobSuccess!.Value, "root 越界的临时精灵集夹具本身仍应加载成功（只是枢轴回退，不是加载失败）");
            Assert.IsTrue(_loader.TryGetSprite(oobId, out var oobSprite));
            Assert.AreEqual(0.5f, oobSprite.pivot.x / oobSprite.rect.width, 0.001f,
                "root 越界时枢轴 X 应回退默认 0.5（ADR-0091 决策 3）");
            Assert.AreEqual(0.5f, oobSprite.pivot.y / oobSprite.rect.height, 0.001f,
                "root 越界时枢轴 Y 应回退默认 0.5（ADR-0091 决策 3）");
        }

        /// <summary>测试夹具用：在指定路径写一张纯白 PNG（内容本身不重要，只需要是一张能被
        /// <c>Texture2D.LoadImage</c> 成功解码、尺寸已知的合法图片）。</summary>
        private static void WritePngFixture(string path, int width, int height)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            var texture = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);
            var pixels = new UnityEngine.Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new UnityEngine.Color32(255, 255, 255, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            // 本文件没有 `using UnityEngine;`（全篇用 UnityEngine.X 全限定名，见既有惯例），
            // 扩展方法语法 texture.EncodeToPNG() 找不到方法（CS1061）——改用全限定静态调用。
            var bytes = UnityEngine.ImageConversion.EncodeToPNG(texture);
            System.IO.File.WriteAllBytes(path, bytes);
            UnityEngine.Object.Destroy(texture);
        }

        /// <summary>测试夹具用：在指定路径写一个文本文件（本用例只用来写 anchors.json），自动创建
        /// 父目录。</summary>
        private static void WriteTextFixture(string path, string content)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }
            System.IO.File.WriteAllText(path, content);
        }
    }
}
