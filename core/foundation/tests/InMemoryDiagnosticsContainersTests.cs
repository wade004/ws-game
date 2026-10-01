using System;
using System.Collections.Generic;
using Core.Foundation.AppLifecycle;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Xunit;

namespace Tests.Foundation
{
    /// <summary>
    /// 各模块 <c>InMemory*Diagnostics</c> 记录容器与 <see cref="PlatformEventDiagnostics"/> 的直接用例
    /// （T-L3 foundation 半，2026-10-01 测试覆盖第四批）：初始为空、按调用顺序累积、重复消息不去重、
    /// Error 记录携带消息与异常（异常可为 null）、Warn 与 Error 互不串用、返回的是实时视图。
    /// 它们是各模块诊断断言的基础设施，自身的行为必须有直接保证。
    /// </summary>
    public sealed class InMemoryDiagnosticsContainersTests
    {
        // -----------------------------------------------------------------
        // 仅 Warn 的容器：app_lifecycle 有 Error，其余见下
        // -----------------------------------------------------------------

        [Fact]
        public void InputMap_Warn_AccumulatesInOrder_AndKeepsDuplicates()
        {
            var d = new InMemoryInputMapDiagnostics();
            Assert.Empty(d.Warnings);

            d.Warn("a");
            d.Warn("b");
            d.Warn("a");

            Assert.Equal(new[] { "a", "b", "a" }, d.Warnings);
        }

        [Fact]
        public void Localization_Warn_AccumulatesInOrder_AndKeepsDuplicates()
        {
            var d = new InMemoryL10nDiagnostics();
            Assert.Empty(d.Warnings);

            d.Warn("x");
            d.Warn("x");

            Assert.Equal(new[] { "x", "x" }, d.Warnings);
        }

        // -----------------------------------------------------------------
        // Warn + Error 的容器：逐个具体类型（记录结构体类型各不相同，故不泛化）
        // -----------------------------------------------------------------

        [Fact]
        public void AppLifecycle_WarnAndError_AreRecordedSeparately_WithExceptionPreserved()
        {
            var d = new InMemoryAppLifecycleDiagnostics();
            Assert.Empty(d.Warnings);
            Assert.Empty(d.Errors);
            var ex = new InvalidOperationException("boom");

            d.Warn("w1");
            d.Error("e1", ex);
            d.Error("e2", null);
            d.Warn("w2");

            Assert.Equal(new[] { "w1", "w2" }, d.Warnings);
            Assert.Equal(2, d.Errors.Count);
            Assert.Equal("e1", d.Errors[0].Message);
            Assert.Same(ex, d.Errors[0].Exception);
            Assert.Equal("e2", d.Errors[1].Message);
            Assert.Null(d.Errors[1].Exception);
        }

        [Fact]
        public void EventBus_WarnAndError_AreRecordedSeparately_WithExceptionPreserved()
        {
            var d = new InMemoryEventDiagnostics();
            Assert.Empty(d.Warnings);
            Assert.Empty(d.Errors);
            var ex = new InvalidOperationException("boom");

            d.Error("e1", ex);
            d.Warn("w1");
            d.Error("e2", null);

            Assert.Equal(new[] { "w1" }, d.Warnings);
            Assert.Equal(2, d.Errors.Count);
            Assert.Same(ex, d.Errors[0].Exception);
            Assert.Equal("e1", d.Errors[0].Message);
            Assert.Null(d.Errors[1].Exception);
        }

        [Fact]
        public void HookRegistry_WarnAndError_AreRecordedSeparately_WithExceptionPreserved()
        {
            var d = new InMemoryHookDiagnostics();
            Assert.Empty(d.Warnings);
            Assert.Empty(d.Errors);
            var ex = new FormatException("bad");

            d.Warn("w");
            d.Error("e", ex);

            Assert.Equal(new[] { "w" }, d.Warnings);
            var record = Assert.Single(d.Errors);
            Assert.Equal("e", record.Message);
            Assert.Same(ex, record.Exception);
        }

        [Fact]
        public void SaveSystem_WarnAndError_AreRecordedSeparately_WithExceptionPreserved()
        {
            var d = new InMemorySaveDiagnostics();
            Assert.Empty(d.Warnings);
            Assert.Empty(d.Errors);
            var ex = new System.IO.IOException("disk");

            d.Error("e", ex);
            d.Warn("w");

            Assert.Equal(new[] { "w" }, d.Warnings);
            var record = Assert.Single(d.Errors);
            Assert.Equal("e", record.Message);
            Assert.Same(ex, record.Exception);
        }

        [Fact]
        public void SceneRouter_WarnAndError_AreRecordedSeparately_WithExceptionPreserved()
        {
            var d = new InMemorySceneDiagnostics();
            Assert.Empty(d.Warnings);
            Assert.Empty(d.Errors);
            var ex = new TimeoutException("slow");

            d.Warn("w");
            d.Error("e", ex);
            d.Error("e", null);

            Assert.Equal(new[] { "w" }, d.Warnings);
            Assert.Equal(2, d.Errors.Count);
            Assert.Same(ex, d.Errors[0].Exception);
            Assert.Null(d.Errors[1].Exception);
        }

        [Fact]
        public void Containers_ReturnLiveViews_NotSnapshots()
        {
            var d = new InMemoryEventDiagnostics();
            var warnings = d.Warnings;
            var errors = d.Errors;

            d.Warn("late");
            d.Error("late", null);

            Assert.Single(warnings);
            Assert.Single(errors);
        }

        [Fact]
        public void ErrorRecordStructs_CarryMessageAndException_AcrossModules()
        {
            var ex = new Exception("x");

            Assert.Equal("m", new AppLifecycleDiagnosticsErrorRecord("m", ex).Message);
            Assert.Same(ex, new EventDiagnosticsErrorRecord("m", ex).Exception);
            Assert.Same(ex, new HookDiagnosticsErrorRecord("m", ex).Exception);
            Assert.Same(ex, new SaveDiagnosticsErrorRecord("m", ex).Exception);
            Assert.Same(ex, new SceneDiagnosticsErrorRecord("m", ex).Exception);
            Assert.Null(new SceneDiagnosticsErrorRecord("m", null).Exception);
        }

        // -----------------------------------------------------------------
        // PlatformEventDiagnostics
        // -----------------------------------------------------------------

        [Fact]
        public void PlatformEventDiagnostics_NullPlatform_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PlatformEventDiagnostics(null!));
        }

        [Fact]
        public void PlatformEventDiagnostics_Warn_IsDeliberatelyANoOp()
        {
            var platform = new Adapters.Stub.StubPlatform();
            var d = new PlatformEventDiagnostics(platform);

            d.Warn("ignored");

            Assert.Empty(platform.CrashLog);
        }

        [Fact]
        public void PlatformEventDiagnostics_Error_ReportsCrashWithModuleContext()
        {
            var platform = new Adapters.Stub.StubPlatform();
            var d = new PlatformEventDiagnostics(platform);

            d.Error("dispatch failed", null);

            var entry = Assert.Single(platform.CrashLog);
            Assert.Equal("Core.Foundation.EventBus: dispatch failed", entry);
        }

        [Fact]
        public void PlatformEventDiagnostics_Error_WithException_AppendsExceptionAfterMessage()
        {
            var platform = new Adapters.Stub.StubPlatform();
            var d = new PlatformEventDiagnostics(platform);
            var ex = new InvalidOperationException("kaboom");

            d.Error("dispatch failed", ex);

            var entry = Assert.Single(platform.CrashLog);
            Assert.StartsWith("Core.Foundation.EventBus: dispatch failed\n", entry);
            Assert.Contains("InvalidOperationException", entry);
            Assert.Contains("kaboom", entry);
        }

        [Fact]
        public void PlatformEventDiagnostics_WorksAsTheBusDiagnostics_ForAThrowingSubscriber()
        {
            var platform = new Adapters.Stub.StubPlatform();
            var key = new Core.Foundation.Common.Id("test.boom");
            var bus = new EventBus(
                EventCatalog.FromDefinitions(new[] { new EventDefinition(key, "test", new string[0]) }),
                null,
                new PlatformEventDiagnostics(platform));
            bus.Subscribe(key, _ => throw new InvalidOperationException("subscriber failed"));

            bus.PublishImmediate(new GenericEvent(key));

            var entry = Assert.Single(platform.CrashLog);
            Assert.Contains("Core.Foundation.EventBus", entry);
            Assert.Contains("subscriber failed", entry);
        }
    }
}
