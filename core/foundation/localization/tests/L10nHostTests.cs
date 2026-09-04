using System;
using System.Collections.Generic;
using System.IO;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Localization;
using Xunit;

namespace Tests.Foundation.Localization
{
    public class L10nHostTests
    {
        private static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
                new EventDefinition(L10nEventKeys.LanguageChanged, "l10n", new[] { "locale" }),
            });
            return new EventBus(catalog);
        }

        private static IDataRegistryView BuildRegistry(IEventBus bus, string localeRowsJson, string textRowsJson)
        {
            var localeEnvelope = "{\"table\": \"l10n.locale\", \"schema_version\": 1, \"rows\": " + localeRowsJson + "}";
            var textEnvelope = "{\"table\": \"l10n.text\", \"schema_version\": 1, \"rows\": " + textRowsJson + "}";
            var source = new InMemoryDataSource()
                .Add("l10n.locale", localeEnvelope)
                .Add("l10n.text", textEnvelope);

            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(L10nSchemas.Locale);
            registry.RegisterSchema(L10nSchemas.Text);

            var report = registry.LoadAll();
            if (report.ErrorCount > 0)
            {
                throw new InvalidOperationException("测试夹具数据非法：" + string.Join("; ", report.Issues));
            }
            return registry;
        }

        // -----------------------------------------------------------------
        // 1. 默认语言 / 回退链
        // -----------------------------------------------------------------

        [Fact]
        public void Constructor_ReadsDefaultLocaleFromIsDefaultRecord()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]",
                "[]");

            var host = new L10nHost(registry, bus);

            Assert.Equal(new Id("l10n.locale.zh_cn"), host.DefaultLocale);
            Assert.Equal(new Id("l10n.locale.zh_cn"), host.GetLocale());
            Assert.Single(host.SupportedLocales);
        }

        [Fact]
        public void Text_CurrentLocaleHasText_ReturnsItDirectly()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]",
                "[{\"key\": \"l10n.quest.a.title\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"送信\"}]");
            var host = new L10nHost(registry, bus);

            Assert.Equal("送信", host.Text(new Id("l10n.quest.a.title")));
        }

        [Fact]
        public void Text_FallsBackAlongDeclaredChain_ReturnsFallbackLocaleText()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}," +
                " {\"id\": \"l10n.locale.zh_tw\", \"fallback\": \"l10n.locale.zh_cn\", \"is_default\": false}]",
                "[{\"key\": \"l10n.quest.a.title\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"送信\"}]");
            var host = new L10nHost(registry, bus);
            host.SetLocale(new Id("l10n.locale.zh_tw")); // zh_tw 自己没有该文本键

            Assert.Equal("送信", host.Text(new Id("l10n.quest.a.title")));
        }

        [Fact]
        public void Text_FallsBackToDefaultLocale_WhenDeclaredChainDoesNotReachIt()
        {
            var bus = MakeBus();
            // en_us 的回退链没有显式指向默认语言 zh_cn，仍应最终兜底到默认语言。
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}," +
                " {\"id\": \"l10n.locale.en_us\", \"fallback\": null, \"is_default\": false}]",
                "[{\"key\": \"l10n.quest.a.title\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"送信\"}]");
            var host = new L10nHost(registry, bus);
            host.SetLocale(new Id("l10n.locale.en_us"));

            Assert.Equal("送信", host.Text(new Id("l10n.quest.a.title")));
        }

        // -----------------------------------------------------------------
        // 2. 缺失键：三种策略
        // -----------------------------------------------------------------

        [Fact]
        public void Text_MissingKey_ReturnKeyPolicy_ReturnsKeyItself()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]", "[]");
            var host = new L10nHost(registry, bus, new L10nOptions { MissingKeyPolicy = MissingKeyPolicy.ReturnKey });

            Assert.Equal("l10n.quest.ghost.title", host.Text(new Id("l10n.quest.ghost.title")));
        }

        [Fact]
        public void Text_MissingKey_ReturnEmptyPolicy_ReturnsEmptyString()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]", "[]");
            var host = new L10nHost(registry, bus, new L10nOptions { MissingKeyPolicy = MissingKeyPolicy.ReturnEmpty });

            Assert.Equal(string.Empty, host.Text(new Id("l10n.quest.ghost.title")));
        }

        [Fact]
        public void Text_MissingKey_ReturnMarkerPolicy_ReturnsBracketedKey()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]", "[]");
            var host = new L10nHost(registry, bus, new L10nOptions { MissingKeyPolicy = MissingKeyPolicy.ReturnMarker });

            Assert.Equal("[[l10n.quest.ghost.title]]", host.Text(new Id("l10n.quest.ghost.title")));
        }

        // -----------------------------------------------------------------
        // 3. 变量代入
        // -----------------------------------------------------------------

        [Fact]
        public void Text_SubstitutesProvidedVariable()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]",
                "[{\"key\": \"l10n.combat.hit.msg\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"对 {target_name} 造成 {amount} 点伤害\"}]");
            var host = new L10nHost(registry, bus);

            var vars = new Dictionary<string, string> { ["target_name"] = "哥布林", ["amount"] = "12" };
            Assert.Equal("对 哥布林 造成 12 点伤害", host.Text(new Id("l10n.combat.hit.msg"), vars));
        }

        [Fact]
        public void Text_MissingVariable_LeavesPlaceholderAsIs()
        {
            var bus = MakeBus();
            var diagnostics = new InMemoryL10nDiagnostics();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]",
                "[{\"key\": \"l10n.combat.hit.msg\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"对 {target_name} 造成伤害\"}]");
            var host = new L10nHost(registry, bus, diagnostics: diagnostics);

            var text = host.Text(new Id("l10n.combat.hit.msg"), null);

            Assert.Equal("对 {target_name} 造成伤害", text);
            Assert.Contains(diagnostics.Warnings, w => w.Contains("target_name"));
        }

        // -----------------------------------------------------------------
        // 4. SetLocale：事件 / 未知语言异常 / 未变化不发事件
        // -----------------------------------------------------------------

        [Fact]
        public void SetLocale_KnownLocale_PublishesLanguageChangedEvent()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}," +
                " {\"id\": \"l10n.locale.en_us\", \"fallback\": \"l10n.locale.zh_cn\", \"is_default\": false}]",
                "[]");
            var host = new L10nHost(registry, bus);
            L10nLanguageChangedEvent? received = null;
            bus.Subscribe<L10nLanguageChangedEvent>(L10nEventKeys.LanguageChanged, e => received = e);

            host.SetLocale(new Id("l10n.locale.en_us"));

            Assert.Equal(new Id("l10n.locale.en_us"), host.GetLocale());
            Assert.NotNull(received);
            Assert.Equal(new Id("l10n.locale.en_us"), received!.Locale);
        }

        [Fact]
        public void SetLocale_UnknownLocale_ThrowsArgumentException()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]", "[]");
            var host = new L10nHost(registry, bus);

            Assert.Throws<ArgumentException>(() => host.SetLocale(new Id("l10n.locale.ghost")));
        }

        [Fact]
        public void SetLocale_SameAsCurrent_DoesNotPublishEvent()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]", "[]");
            var host = new L10nHost(registry, bus);
            var fired = false;
            bus.Subscribe(L10nEventKeys.LanguageChanged, _ => fired = true);

            host.SetLocale(new Id("l10n.locale.zh_cn"));

            Assert.False(fired);
        }

        // -----------------------------------------------------------------
        // 5. is_default 缺失 / 重复；回退链成环
        // -----------------------------------------------------------------

        [Fact]
        public void Constructor_NoDefaultLocale_ThrowsArgumentException()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": false}]", "[]");

            Assert.Throws<ArgumentException>(() => new L10nHost(registry, bus));
        }

        [Fact]
        public void Constructor_MultipleDefaultLocales_ThrowsArgumentException()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}," +
                " {\"id\": \"l10n.locale.en_us\", \"fallback\": null, \"is_default\": true}]", "[]");

            Assert.Throws<ArgumentException>(() => new L10nHost(registry, bus));
        }

        [Fact]
        public void Constructor_SelfFallback_ThrowsArgumentException()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": \"l10n.locale.zh_cn\", \"is_default\": true}]", "[]");

            Assert.Throws<ArgumentException>(() => new L10nHost(registry, bus));
        }

        [Fact]
        public void Constructor_TwoLocaleFallbackCycle_ThrowsArgumentException()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": \"l10n.locale.en_us\", \"is_default\": true}," +
                " {\"id\": \"l10n.locale.en_us\", \"fallback\": \"l10n.locale.zh_cn\", \"is_default\": false}]", "[]");

            Assert.Throws<ArgumentException>(() => new L10nHost(registry, bus));
        }

        // -----------------------------------------------------------------
        // 6. HasText
        // -----------------------------------------------------------------

        [Fact]
        public void HasText_ExistingKey_ReturnsTrue()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]",
                "[{\"key\": \"l10n.quest.a.title\", \"locale\": \"l10n.locale.zh_cn\", \"text\": \"送信\"}]");
            var host = new L10nHost(registry, bus);

            Assert.True(host.HasText(new Id("l10n.quest.a.title")));
        }

        [Fact]
        public void HasText_MissingKey_ReturnsFalse()
        {
            var bus = MakeBus();
            var registry = BuildRegistry(bus,
                "[{\"id\": \"l10n.locale.zh_cn\", \"fallback\": null, \"is_default\": true}]", "[]");
            var host = new L10nHost(registry, bus);

            Assert.False(host.HasText(new Id("l10n.quest.ghost.title")));
        }

        // -----------------------------------------------------------------
        // 7. 真实 data/_sample/l10n 两个文件跑通
        // -----------------------------------------------------------------

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            // 本文件路径固定是 <repoRoot>/core/foundation/localization/tests/L10nHostTests.cs，
            // 向上 4 级（tests → localization → foundation → core）即仓库根（与
            // data_registry/tests/DataRegistryTests.cs 的同名方法同一判断记录：不用
            // AppContext.BaseDirectory，因为 dotnet 命令按任务书要求加了 --artifacts-path 指到
            // 仓库外，运行期程序集目录不在仓库树下）。
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空"));
            for (int i = 0; i < 4; i++)
            {
                dir = dir.Parent ?? throw new InvalidOperationException($"源文件路径层级不足，无法定位仓库根目录：{sourceFilePath}");
            }
            return dir.FullName;
        }

        [Fact]
        public void RealSampleL10nFiles_LoadAndResolveText()
        {
            var repoRoot = FindRepoRoot();
            var localeJson = File.ReadAllText(Path.Combine(repoRoot, "data", "_sample", "l10n", "l10n.locale.json"));
            var textJson = File.ReadAllText(Path.Combine(repoRoot, "data", "_sample", "l10n", "l10n.text.json"));

            var bus = MakeBus();
            var source = new InMemoryDataSource()
                .Add("l10n.locale", localeJson)
                .Add("l10n.text", textJson);
            var registry = new DataRegistry(source, bus);
            registry.RegisterSchema(L10nSchemas.Locale);
            registry.RegisterSchema(L10nSchemas.Text);
            var report = registry.LoadAll();
            Assert.Equal(0, report.ErrorCount);

            var host = new L10nHost(registry, bus);

            Assert.Equal(new Id("l10n.locale.zh_cn"), host.DefaultLocale);
            Assert.Equal("力量", host.Text(new Id("l10n.stat.strength.name")));
        }
    }
}
