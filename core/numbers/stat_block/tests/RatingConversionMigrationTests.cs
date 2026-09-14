using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Xunit;

namespace Tests.Numbers.StatBlock
{
    /// <summary>
    /// 分阶段落地计划 T-N0-4 验收（<c>stat.rating_conversion</c> 侧）：v1 数据 <c>{level, points_per_percent}</c>
    /// 经 1→2 迁移加载后，<c>StatHost</c> 的评级换算结果与 v2 数据直接加载、以及迁移前手写插值式子逐位一致
    /// （≥ 3 个等级：端点内、端点外两侧、段内）。
    /// </summary>
    public sealed class RatingConversionMigrationTests
    {
        private static readonly Id RatingStat = new Id("stat.mig_rating");
        private static readonly Id Unit = new Id("unit.mig_a");

        private static IEventBus MakeBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

        private const string Definitions = "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
            "{\"id\": \"stat.mig_rating\", \"name_key\": \"l10n.stat.mig_rating.name\", \"group\": \"secondary\", " +
            "\"is_rating\": true, \"rating_conversion_ref\": \"stat.rating.mig\"}]}";

        private const string V1 = "{\"table\": \"stat.rating_conversion\", \"schema_version\": 1, \"rows\": [" +
            "{\"id\": \"stat.rating.mig\", \"entries\": [{\"level\": 10, \"points_per_percent\": 25}, {\"level\": 1, \"points_per_percent\": 10}, {\"level\": 4, \"points_per_percent\": 16}]}]}";

        private const string V2 = "{\"table\": \"stat.rating_conversion\", \"schema_version\": 2, \"rows\": [" +
            "{\"id\": \"stat.rating.mig\", \"entries\": [{\"x\": 10, \"y\": 25}, {\"x\": 1, \"y\": 10}, {\"x\": 4, \"y\": 16}]}]}";

        private static StatHost MakeHost(string conversionJson, int level)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", Definitions)
                .Add("stat.rating_conversion", conversionJson);
            var registry = new DataRegistry(source, MakeBus());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(StatSchemas.RatingConversion);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var host = new StatHost(registry, MakeBus(), new StatHostOptions
            {
                EnableRatingConversion = true,
                LevelLookup = _ => level,
            });
            host.RegisterUnit(Unit);
            return host;
        }

        /// <summary>迁移前 <c>StatHost.ConvertRating</c> 的原式（逐字复刻，作为基准）。</summary>
        private static double LegacyConvert(List<(int Level, double Ppp)> entries, int level, double rawValue)
        {
            entries.Sort((a, b) => a.Level.CompareTo(b.Level));
            if (level <= entries[0].Level) return rawValue / entries[0].Ppp;
            if (level >= entries[entries.Count - 1].Level) return rawValue / entries[entries.Count - 1].Ppp;
            var low = entries[0];
            var high = entries[entries.Count - 1];
            for (int i = 0; i < entries.Count - 1; i++)
            {
                if (level >= entries[i].Level && level <= entries[i + 1].Level)
                {
                    low = entries[i];
                    high = entries[i + 1];
                    break;
                }
            }
            var t = (level - low.Level) / (double)(high.Level - low.Level);
            var ppp = low.Ppp + t * (high.Ppp - low.Ppp);
            return rawValue / ppp;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(10)]
        [InlineData(80)]
        public void V1AndV2_ConvertRating_MatchLegacyFormulaBitForBit(int level)
        {
            const double raw = 123.0;
            var expected = LegacyConvert(new List<(int, double)> { (10, 25.0), (1, 10.0), (4, 16.0) }, level, raw);

            var v1 = MakeHost(V1, level);
            var v2 = MakeHost(V2, level);
            v1.SetBase(Unit, RatingStat, raw);
            v2.SetBase(Unit, RatingStat, raw);

            Assert.Equal(expected, v1.GetStat(Unit, RatingStat));
            Assert.Equal(expected, v2.GetStat(Unit, RatingStat));
        }
    }
}
