#nullable enable
// EngineLabHostCrossHostTests：引擎宿主与无头宿主的跨宿主逻辑不变量（手感设计/06 第 4 节）。
// 不变量：同一脚本同一格子，引擎宿主跑出的逻辑组指纹（默认注册表、仅逻辑类度量）与无头宿主逐字节一致——引擎侧表现驱动不回流逻辑。
using System.Collections.Generic;
using System.Text;
using Adapter.Unity.LabHost;
using Lab;
using NUnit.Framework;

namespace Adapter.Unity.Tests.LabHost
{
    [Category("module:lab")]
    public sealed class EngineLabHostCrossHostTests
    {
        /// <summary>
        /// 复现 + 不变量：所选格子集合上每个手感场景脚本，两个宿主的逻辑组文本相等，且引擎侧没有吞掉任何异常。
        /// 期望值由无头宿主按同一规则算出（不写死数值）；格子集合与运行个数由环境变量控制，见 <see cref="LabHostTestSupport"/>。
        /// </summary>
        [Test]
        public void FeelScripts_LogicFingerprint_IsByteIdentical_OnEngineAndHeadlessHosts()
        {
            var host = LabHostTestSupport.Host;
            var filter = LabHostTestSupport.CellFilter();
            var max = LabHostTestSupport.MaxRuns();
            var failures = new StringBuilder();
            var runs = 0;
            foreach (var script in LabHostTestSupport.FeelScripts())
            {
                foreach (var cell in host.RunnableCells(script))
                {
                    if (filter != null && !filter.Contains(cell))
                    {
                        continue;
                    }

                    if (max > 0 && runs >= max)
                    {
                        break;
                    }

                    runs++;
                    var headless = host.RunHeadlessLogic(script, cell);
                    var engine = host.Run(script, cell);
                    if (!string.Equals(headless, engine.LogicProjection, System.StringComparison.Ordinal))
                    {
                        failures.AppendLine($"{script.Meta.ScriptId}@{cell}：逻辑组文本与无头宿主不一致");
                    }

                    if (engine.Engine.Errors.Count > 0)
                    {
                        failures.AppendLine($"{script.Meta.ScriptId}@{cell}：引擎侧错误 {string.Join(" | ", engine.Engine.Errors)}");
                    }
                }
            }

            Assert.Greater(runs, 0, "没有跑任何（脚本，格子）组合，检查 GF_LAB_CELLS");
            Assert.AreEqual(string.Empty, failures.ToString(), $"共 {runs} 个组合");
        }
    }
}
