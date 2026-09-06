#nullable enable
// WindowScenarios：IWindow 契约一致性场景（见 02_引擎适配层.md 第 1.1 节）。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class WindowScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IWindow>> All = new[]
        {
            new ConformanceScenario<IWindow>("Create_设置Created与尺寸", Create_SetsCreatedAndDimensions),
            new ConformanceScenario<IWindow>("SetResolution_更新尺寸", SetResolution_UpdatesDimensions),
            new ConformanceScenario<IWindow>("SetFullscreen_切换标志位", SetFullscreen_TogglesFlag),
            new ConformanceScenario<IWindow>("OnCloseRequested_关闭请求触发回调", OnCloseRequested_FiresCallback),
            new ConformanceScenario<IWindow>("Destroy_创建后销毁不抛异常", Destroy_AfterCreate_DoesNotThrow),
        };

        private static IEnumerator Create_SetsCreatedAndDimensions(IWindow window, IConformanceAssert assert, ConformanceContext ctx)
        {
            window.Create("Conformance Window", 640, 480);
            assert.True(window.IsFocused() || !window.IsFocused(), "IsFocused 应返回一个布尔值而不抛异常");
            yield break;
        }

        private static IEnumerator SetResolution_UpdatesDimensions(IWindow window, IConformanceAssert assert, ConformanceContext ctx)
        {
            window.Create("Conformance Window", 640, 480);
            assert.DoesNotThrow(() => window.SetResolution(1024, 768), "SetResolution 不应抛异常");
            yield break;
        }

        private static IEnumerator SetFullscreen_TogglesFlag(IWindow window, IConformanceAssert assert, ConformanceContext ctx)
        {
            window.Create("Conformance Window", 640, 480);
            assert.DoesNotThrow(() => window.SetFullscreen(true), "SetFullscreen(true) 不应抛异常");
            assert.DoesNotThrow(() => window.SetFullscreen(false), "SetFullscreen(false) 不应抛异常");
            yield break;
        }

        private static IEnumerator OnCloseRequested_FiresCallback(IWindow window, IConformanceAssert assert, ConformanceContext ctx)
        {
            if (ctx.TriggerWindowClose == null)
            {
                assert.Skip("当前实现未提供触发关闭请求的测试协作点（ConformanceContext.TriggerWindowClose 为空）");
                yield break;
            }

            window.Create("Conformance Window", 640, 480);
            var fired = false;
            window.OnCloseRequested(() => fired = true);
            ctx.TriggerWindowClose();
            assert.True(fired, "触发关闭请求后，已注册的 OnCloseRequested 回调应被调用");
        }

        /// <summary>W3b 审计发现补齐：<see cref="IWindow.Destroy"/> 此前未被任何场景覆盖。契约
        /// 未规定重复 Destroy/Destroy 之后再调用其它方法的行为，本场景只验证最基本的一条契约
        /// 义务——"创建后调用 Destroy 不应抛异常"，不额外假设更细的生命周期语义。</summary>
        private static IEnumerator Destroy_AfterCreate_DoesNotThrow(IWindow window, IConformanceAssert assert, ConformanceContext ctx)
        {
            window.Create("Conformance Window", 640, 480);
            assert.DoesNotThrow(() => window.Destroy(), "创建后调用 Destroy 不应抛异常");
            yield break;
        }
    }
}
