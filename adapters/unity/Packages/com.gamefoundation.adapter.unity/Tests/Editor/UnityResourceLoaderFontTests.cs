#nullable enable
// 缺口 1（字体资源按 id 解析）验收测试：Font 种类的 LoadAsync 在 Resources.Load 判定完成前不需要
// 任何协程等待（不像其它种类要经后台线程 Task.Run，见 UnityResourceLoader.cs 顶部"判断记录
// （Font 资源种类）"），LoadAsync 后立即调用一次 Tick() 即可同步拿到结果，因此可以用纯 EditMode
// [Test]（不需要 [UnityTest] 协程），验证与包 README"资源 id → 路径规则"约定一致。
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityResourceLoaderFontTests
    {
        private UnityResourceLoader _loader = null!;

        [SetUp]
        public void SetUp()
        {
            _loader = new UnityResourceLoader();
        }

        /// <summary>依赖 build.ps1 -SyncContent 已经把 assets/_placeholder/fonts/noto_sans_cjk_sc.otf
        /// 同步进 adapters/unity/Assets/Framework/Resources/Fonts/（见包 README"资源 id → 路径规则"/
        /// "内容同步"两节）；本仓库该文件已直接提交（不依赖同步也能通过，同步只是保证与源目录内容
        /// 一致），命令行跑测试前无需额外准备。</summary>
        [Test]
        public void LoadAsync_ExistingFontResource_MarksLoadedSynchronouslyAfterTick()
        {
            var resourceId = new Id("font.noto_sans_cjk_sc");
            bool? success = null;

            Assert.IsFalse(_loader.IsLoaded(resourceId), "LoadAsync 之前不应当已经是已加载状态");

            _loader.LoadAsync(resourceId, ResourceKind.Font, (id, ok) => success = ok);
            _loader.Tick();

            Assert.IsNotNull(success, "Font 种类的判定应当在一次 Tick() 内完成，不需要等待协程/多帧");
            Assert.IsTrue(success!.Value, "Resources/Fonts/noto_sans_cjk_sc 应当能被 Resources.Load<Font> 取到");
            Assert.IsTrue(_loader.IsLoaded(resourceId), "加载成功后 IsLoaded 应当为 true（与 LoadCallback 结果一致）");
            Assert.IsTrue(_loader.TryGetFont(resourceId, out var font));
            Assert.IsNotNull(font, "TryGetFont 应当能取回已加载的 UnityEngine.Font 对象");
        }

        [Test]
        public void LoadAsync_UnknownFontResource_InvokesCallbackWithFalse_AndNotLoaded()
        {
            var resourceId = new Id("font.missing");
            bool? success = null;

            _loader.LoadAsync(resourceId, ResourceKind.Font, (id, ok) => success = ok);
            _loader.Tick();

            Assert.IsNotNull(success);
            Assert.IsFalse(success!.Value, "Resources/Fonts/missing 不存在，加载应当判定失败");
            Assert.IsFalse(_loader.IsLoaded(resourceId));
            Assert.IsFalse(_loader.TryGetFont(resourceId, out _));
        }
    }
}
