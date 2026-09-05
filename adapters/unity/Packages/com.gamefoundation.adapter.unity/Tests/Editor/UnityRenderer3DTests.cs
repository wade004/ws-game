#nullable enable
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using NUnit.Framework;

namespace Adapter.Unity.Tests.Editor
{
    /// <summary>IRenderer3D 声明降级：全部方法必须抛 NotSupportedException（见
    /// UnityRenderer3D.cs 类型注释与落地计划阶段 4 验收 1）。</summary>
    public sealed class UnityRenderer3DTests
    {
        [Test]
        public void CreateModelInstance_ThrowsNotSupportedException()
        {
            var renderer = new UnityRenderer3D();

            Assert.Throws<System.NotSupportedException>(() => renderer.CreateModelInstance(new Id("model.sample_creature")));
        }
    }
}
