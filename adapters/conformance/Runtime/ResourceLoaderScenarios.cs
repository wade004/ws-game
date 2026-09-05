#nullable enable
// ResourceLoaderScenarios：IResourceLoader 契约一致性场景（见 02_引擎适配层.md 第 1.7 节 /
// ADR-0016 决策 5、决策 6）。
//
// 判断记录（不要求"登记后加载成功"这一路径）：桩实现的"成功加载"只需要测试方法 Register(id)
// 登记一下即可同步成功；Unity 实现真的会去 StreamingAssets 目录读一份磁盘文件，要让这条路径
// 在两侧都可靠、确定性地成功，需要预先在两侧都放一份内容一致的资源文件，超出"引擎适配层契约"
// 本身要验证的范围（属于内容管线职责）。因此本场景组只覆盖两侧都能不依赖真实资源文件而确定性
// 触发的契约条款：未注册资源的加载失败路径、IsLoaded/GetLoadProgress 的取值范围、Unload 对
// 未加载资源的宽松语义。"加载完成回调总在主线程排队执行"这一条款由 ctx.AdvanceTime 轮询等待
// （桩是同步回调，第一次判断即为 true；Unity 是 Task.Run 后台线程读取 + Tick 主线程回调，
// 需要真的等上若干帧，见 UnityResourceLoader.cs 类型顶部"加载方式"判断记录）。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class ResourceLoaderScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IResourceLoader>> All = new[]
        {
            new ConformanceScenario<IResourceLoader>("未注册资源加载最终以失败回调", UnregisteredResource_EventuallyCallsBackFalse),
            new ConformanceScenario<IResourceLoader>("IsLoaded_加载前为false", IsLoaded_FalseBeforeLoad),
            new ConformanceScenario<IResourceLoader>("GetLoadProgress_取值范围在0到1之间", GetLoadProgress_WithinZeroToOne),
            new ConformanceScenario<IResourceLoader>("Unload_未加载或未知id不抛异常", Unload_UnknownId_DoesNotThrow),
        };

        private static readonly Id NeverExistsId = new Id("data.conformance_never_exists_probe");

        /// <summary>最多轮询这么多次"推进一帧"等待异步回调落地，覆盖 Unity 侧后台线程读取 +
        /// 下一次 Tick 完成回调的正常时延；桩侧的同步回调第一次检查就会命中，不消耗轮询次数。</summary>
        private const int MaxPollAttempts = 60;

        private static IEnumerator UnregisteredResource_EventuallyCallsBackFalse(IResourceLoader loader, IConformanceAssert assert, ConformanceContext ctx)
        {
            var callbackFired = false;
            var callbackSuccess = true;
            loader.LoadAsync(NeverExistsId, ResourceKind.DataTable, (id, success) =>
            {
                callbackFired = true;
                callbackSuccess = success;
            });

            var attempts = 0;
            while (!callbackFired && attempts < MaxPollAttempts)
            {
                yield return ctx.AdvanceTime(0.05);
                attempts++;
            }

            assert.True(callbackFired, $"LoadAsync 的回调应在 {MaxPollAttempts} 次轮询内触发（未注册/不存在的资源也必须最终回调，不能永远挂起）");
            assert.False(callbackSuccess, "未注册/不存在的资源，加载回调的 success 应为 false");
        }

        private static IEnumerator IsLoaded_FalseBeforeLoad(IResourceLoader loader, IConformanceAssert assert, ConformanceContext ctx)
        {
            var freshId = new Id("data.conformance_fresh_probe");
            assert.False(loader.IsLoaded(freshId), "从未 LoadAsync 过的资源 id，IsLoaded 应为 false");
            yield break;
        }

        private static IEnumerator GetLoadProgress_WithinZeroToOne(IResourceLoader loader, IConformanceAssert assert, ConformanceContext ctx)
        {
            var freshId = new Id("data.conformance_progress_probe");
            var progressBefore = loader.GetLoadProgress(freshId);
            assert.True(progressBefore >= 0.0 && progressBefore <= 1.0, $"GetLoadProgress 取值应在 [0,1]，实际 {progressBefore}");

            var fired = false;
            loader.LoadAsync(freshId, ResourceKind.DataTable, (_, __) => fired = true);
            var progressDuring = loader.GetLoadProgress(freshId);
            assert.True(progressDuring >= 0.0 && progressDuring <= 1.0, $"加载中 GetLoadProgress 取值应在 [0,1]，实际 {progressDuring}");

            var attempts = 0;
            while (!fired && attempts < MaxPollAttempts)
            {
                yield return ctx.AdvanceTime(0.05);
                attempts++;
            }
        }

        private static IEnumerator Unload_UnknownId_DoesNotThrow(IResourceLoader loader, IConformanceAssert assert, ConformanceContext ctx)
        {
            var freshId = new Id("data.conformance_unload_probe");
            assert.DoesNotThrow(() => loader.Unload(freshId), "对未加载/未知的资源 id 调用 Unload 不应抛异常");
            assert.DoesNotThrow(() => loader.Unload(freshId), "重复 Unload 同一个未加载的资源 id 不应抛异常");
            yield break;
        }
    }
}
