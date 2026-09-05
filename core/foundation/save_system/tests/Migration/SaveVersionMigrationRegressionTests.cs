using System;
using System.IO;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Xunit;

namespace Tests.Foundation.SaveSystem.Migration
{
    /// <summary>
    /// 存档版本迁移回归（存档版本迁移回归任务新增，见 <c>core/foundation/save_system/README.md</c>
    /// "迁移回归样例"一节）：<see cref="SaveSystemTests"/> 已经用纯内存构造的 JSON 文档验证了迁移
    /// 链机制本身（<c>RenameSectionMigration</c> 等测试替身，见该文件），但没有一份"手工构造、
    /// 提交到仓库、可被人工审阅"的旧版本存档样例——本文件补上这一环：<see cref="v1_sample.save.json"/>
    /// 是一份货真价实的旧版本（<c>save_version: 1</c>）存档文档，<see cref="v_future_sample.save.json"/>
    /// 是一份"运行时尚未见过"的未来版本样例；两者都是仓库内固定产物，测试只读不写（惯例同
    /// <c>core/gameplay/tests/Replay/ReplayBaselineTests.cs</c> 对固定录像文件的处理）。
    /// <para>
    /// 判断记录（选用的"真实且最小"迁移步骤：<c>world.current_position</c> 从数组形态迁移到对象
    /// 形态）：框架本身（<c>core/foundation/save_system</c>）不拥有任何具体游戏内容段（那些是
    /// 载体/玩法层的职责，见本模块 README"基础架构提供 / 游戏层提供"一节"迁移函数链机制：是
    /// （机制）/具体每次 schema 变更对应的 ISaveMigration 实现：否（游戏层）"），因此没有游戏层
    /// 段可供框架自己登记"真实"迁移；但 <c>world.current_position</c> 段是个例外——它被
    /// <see cref="ISaveSystem.Load"/> 自身直接解析（<see cref="LoadResult.CurrentPosition"/>，不
    /// 依赖调用方是否注册了对应 <see cref="IPersistable"/>，见该属性文档），要求的形状固定为
    /// <c>{x: Number, y: Number}</c>（<c>SaveSystem.TryGetSectionVec2</c>）。本任务假定该字段
    /// 曾经（存档 <c>save_version 1</c>）以 <c>[x, y]</c> 两元素数组形式写出——这是一处框架自身
    /// 就能定义、也真正需要迁移函数才能升级（数组 → 对象是结构变化，不是"缺省值补齐"就能解决的
    /// 表面调整）的字段，因此选它作为"真实且最小"的迁移步骤，不需要越权替游戏层的段（如
    /// <c>player.inventory</c>）编造一个不属于框架职责的迁移决定。
    /// </para>
    /// </summary>
    public sealed class SaveVersionMigrationRegressionTests
    {
        private static readonly Id GameId = new Id("game.demo");
        private static readonly Id SlotId = new Id("slot.migration_sample");

        private const int LegacyVersion = 1;
        private const int CurrentVersionForThisSuite = 2;

        private static string FindDirectory([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            return Path.GetDirectoryName(sourceFilePath) ?? throw new InvalidOperationException("CallerFilePath 为空");
        }

        private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(FindDirectory(), fileName));

        private static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(SaveEventKeys.SaveCompleted, "save", new[] { "slotId" }),
                new EventDefinition(SaveEventKeys.SaveLoaded, "save", new[] { "slotId" }),
                new EventDefinition(SaveEventKeys.SaveMigrated, "save", new[] { "slotId", "fromVersion", "toVersion" }),
            });
            return new EventBus(catalog);
        }

        /// <summary>把手工构造的旧版本存档样例文本写进桩文件系统的正式存档路径，模拟"玩家磁盘上
        /// 已经有一份旧版本存档"。</summary>
        private static StubFileSystem SeedFileSystemWithFixture(string fixtureFileName, Id slotId, string savesDir = "saves")
        {
            var fs = new StubFileSystem();
            var path = $"user://{savesDir}/{slotId.Value}.json";
            fs.WriteTextAtomic(path, ReadFixture(fixtureFileName));
            return fs;
        }

        // -----------------------------------------------------------------
        // "真实且最小"的迁移步骤：world.current_position 从 [x, y] 数组迁移到 {x, y} 对象
        // （见类型顶部判断记录）。
        // -----------------------------------------------------------------

        private sealed class WorldCurrentPositionArrayToObjectMigration : ISaveMigration
        {
            public int FromVersion => LegacyVersion;

            public int ToVersion => CurrentVersionForThisSuite;

            /// <summary>可逆：新形态 <c>{x, y}</c> 携带的信息与旧形态 <c>[x, y]</c> 完全等价，
            /// 理论上可以逆向拆回数组（本架构不提供逆向迁移执行入口，这里如实标注"信息不丢失"，
            /// 见 <see cref="ISaveMigration.Irreversible"/> 文档"该标记只供上层/工具在展示或校验时
            /// 读取判断"）。</summary>
            public bool Irreversible => false;

            public JsonObject Migrate(JsonObject document)
            {
                if (!(document["sections"] is JsonObject sections))
                {
                    return document;
                }

                if (!sections.TryGetValue(SaveSections.WorldCurrentPosition, out var posRaw) ||
                    !(posRaw is JsonArray arr) || arr.Count != 2 ||
                    !(arr[0] is JsonNumber xNum) || !(arr[1] is JsonNumber yNum))
                {
                    // 没有旧形态字段需要迁移（例如该段本就缺失），原样返回——迁移函数应当对
                    // "字段不存在"宽容，不是每一份旧存档都一定填了每一个可选字段。
                    return document;
                }

                var newPosition = new JsonObjectBuilder()
                    .Add("x", xNum)
                    .Add("y", yNum)
                    .Build();

                var newSections = new JsonObjectBuilder();
                foreach (var entry in sections)
                {
                    newSections.Add(entry.Key, entry.Key == SaveSections.WorldCurrentPosition ? (JsonValue)newPosition : entry.Value);
                }

                return new JsonObjectBuilder()
                    .Add("save_version", document["save_version"])
                    .Add("sections", newSections.Build())
                    .Build();
            }
        }

        /// <summary>最小 <see cref="IPersistable"/> 桩，只为让 <see cref="SaveSystem.Save"/> 把
        /// <see cref="SaveSections.WorldCurrentMapId"/> 段写回磁盘（见
        /// <see cref="LoadOldSample_ThenSaveAndReloadAgain_NoDataLoss"/> 判断记录）。</summary>
        private sealed class WorldMapIdPersistable : IPersistable
        {
            public string SectionKey => SaveSections.WorldCurrentMapId;

            public Id Value { get; set; }

            public JsonValue Save() => new JsonString(Value.Value);

            public void Load(JsonValue data)
            {
                if (data is JsonString s && Id.TryParse(s.Value, out var id))
                {
                    Value = id;
                }
            }
        }

        /// <summary>同 <see cref="WorldMapIdPersistable"/>，对应 <see cref="SaveSections.WorldCurrentPosition"/>。</summary>
        private sealed class WorldPositionPersistable : IPersistable
        {
            public string SectionKey => SaveSections.WorldCurrentPosition;

            public Vec2 Value { get; set; }

            public JsonValue Save() => new JsonObjectBuilder().Add("x", new JsonNumber(Value.X)).Add("y", new JsonNumber(Value.Y)).Build();

            public void Load(JsonValue data)
            {
                if (data is JsonObject o && o.TryGetValue("x", out var xv) && xv is JsonNumber xn
                                          && o.TryGetValue("y", out var yv) && yv is JsonNumber yn)
                {
                    Value = new Vec2(xn.Value, yn.Value);
                }
            }
        }

        private static Core.Foundation.SaveSystem.SaveSystem BuildSutWithMigration(StubFileSystem fs, IEventBus? bus = null)
        {
            var options = new SaveSystemOptions(GameId) { CurrentSaveVersion = CurrentVersionForThisSuite };
            var sut = new Core.Foundation.SaveSystem.SaveSystem(fs, options, bus);
            sut.RegisterMigration(new WorldCurrentPositionArrayToObjectMigration());
            return sut;
        }

        // -----------------------------------------------------------------
        // 1. 加载旧样例 → MigratedFromVersion 正确 → 各段可读
        // -----------------------------------------------------------------

        [Fact]
        public void LoadOldSample_MigratesSuccessfully_MigratedFromVersionIsCorrect()
        {
            var fs = SeedFileSystemWithFixture("v1_sample.save.json", SlotId);
            var sut = BuildSutWithMigration(fs);

            var result = sut.Load(SlotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            Assert.Equal(LegacyVersion, result.MigratedFromVersion);
            Assert.NotNull(result.Meta);
            // meta.save_version 未参与本次结构迁移，仍是旧样例里写的原始值 1——SaveSystem 只强制
            // 覆盖信封顶层的 save_version（见 ISaveMigration.Migrate 文档"调用方会在调用后强制把
            // 结果文档的 save_version 覆盖为 ToVersion"），meta 段内容本身随迁移函数的实现而定；
            // 本迁移函数没有改 meta，所以 SaveMeta.SaveVersion 保持旧样例原值，是 MigratedFromVersion
            // 而不是当前版本——这条断言同时钉死"迁移前后两个不同版本号各自该在哪读到"。
            Assert.Equal(LegacyVersion, result.Meta!.SaveVersion);
        }

        [Fact]
        public void LoadOldSample_MigratesSuccessfully_AllSectionsReadable()
        {
            var fs = SeedFileSystemWithFixture("v1_sample.save.json", SlotId);
            var sut = BuildSutWithMigration(fs);

            var rng = new RngHost(0UL);
            sut.RegisterPersistable(new RngStreamsPersistable(rng));

            var result = sut.Load(SlotId);

            Assert.Equal(LoadStatus.Loaded, result.Status);
            // world.current_position：迁移把 [12.5, -3.25] 转成 {x, y} 后，SaveSystem 自身的
            // TryGetSectionVec2 才能正确解析出来——迁移生效与否，靠这个字段能不能被读出来直接验证。
            Assert.Equal(new Vec2(12.5, -3.25), result.CurrentPosition);
            Assert.Equal(new Id("map.migration_sample_area"), result.CurrentMapId);

            // rng.stream_states：迁移函数没有改动这一段，应原样经 RngStreamsPersistable.Load 生效。
            var restored = rng.GetStreamState(new Id("rng.default"));
            Assert.Equal(new RngStreamState(1, 2, 3, 4), restored);
        }

        // -----------------------------------------------------------------
        // 2. 再存再读无损：迁移后的世界状态重新落盘，用新版本格式再读一次，数值不变
        // -----------------------------------------------------------------

        [Fact]
        public void LoadOldSample_ThenSaveAndReloadAgain_NoDataLoss()
        {
            // 判断记录：world.current_map_id/current_position 两段虽然 Load 时由 SaveSystem 自身
            // 直接解析（不依赖注册，见 LoadResult.CurrentMapId 文档），但 Save 只写"已注册
            // IPersistable 的段"（见 SaveSystem.ComputeWriteOrder）——不注册就不会被写回磁盘。
            // 这两个测试用最小 IPersistable 桩把"读到的值"原样接住再写回，模拟真实游戏层模块
            // （如 core/carriers/unit 的位置持久化）应当承担的职责，验证的是"框架的迁移+落盘+
            // 再读机制本身无损"，不是这两个具体桩类。
            var fs = SeedFileSystemWithFixture("v1_sample.save.json", SlotId);
            var sut = BuildSutWithMigration(fs);

            var rng = new RngHost(0UL);
            sut.RegisterPersistable(new RngStreamsPersistable(rng));
            var mapIdPersistable = new WorldMapIdPersistable();
            var positionPersistable = new WorldPositionPersistable();
            sut.RegisterPersistable(mapIdPersistable);
            sut.RegisterPersistable(positionPersistable);

            var firstLoad = sut.Load(SlotId);
            Assert.Equal(LoadStatus.Loaded, firstLoad.Status);
            // 两个桩没有向 SaveSections.KnownOrder 里的段名注册专门的 Load 逻辑之外的行为——它们
            // 的 Load 只在 sections 里存在对应 key 时才会被调用；这里手工从 LoadResult 的直接解析
            // 结果种回去，模拟真实模块会做的事（真实模块的 Load 会自己解析同一段 JSON，效果等价）。
            mapIdPersistable.Value = firstLoad.CurrentMapId!.Value;
            positionPersistable.Value = firstLoad.CurrentPosition!.Value;

            var saveResult = sut.Save(new SaveRequest(SlotId, "2026-01-03T00:00:00Z"));
            Assert.True(saveResult.Success, saveResult.Message);

            // 用一组全新的实例重新读一遍——避免"内存里的对象还留着旧状态"掩盖"落盘内容本身是否
            // 正确"这个问题，只信任重新读盘的结果。
            var rng2 = new RngHost(0UL);
            var sut2 = BuildSutWithMigration(fs);
            sut2.RegisterPersistable(new RngStreamsPersistable(rng2));
            sut2.RegisterPersistable(new WorldMapIdPersistable());
            sut2.RegisterPersistable(new WorldPositionPersistable());

            var secondLoad = sut2.Load(SlotId);

            Assert.Equal(LoadStatus.Loaded, secondLoad.Status);
            Assert.Null(secondLoad.MigratedFromVersion); // 这次落盘已经是当前版本，不需要再迁移。
            Assert.Equal(CurrentVersionForThisSuite, secondLoad.Meta!.SaveVersion);
            Assert.Equal(new Vec2(12.5, -3.25), secondLoad.CurrentPosition);
            Assert.Equal(new Id("map.migration_sample_area"), secondLoad.CurrentMapId);
            Assert.Equal(new RngStreamState(1, 2, 3, 4), rng2.GetStreamState(new Id("rng.default")));

            // 落盘内容本身也应该是新（对象）形态，而不是继续携带旧数组形态——否则"再存"这一步
            // 没有真正把内存里迁移后的结构写回磁盘，只是凑巧还能读。
            var writtenText = fs.ReadText($"user://saves/{SlotId.Value}.json")!;
            var writtenDoc = (JsonObject)JsonReader.Parse(writtenText);
            var writtenSections = (JsonObject)writtenDoc["sections"];
            Assert.IsType<JsonObject>(writtenSections[SaveSections.WorldCurrentPosition]);
        }

        // -----------------------------------------------------------------
        // 3. 未来版本样例 → 拒绝并给出明确原因
        // -----------------------------------------------------------------

        [Fact]
        public void LoadFutureVersionSample_RejectsWithMigrationFailed_AndExplainsWhy()
        {
            var futureSlotId = new Id("slot.migration_future_sample");
            var fs = SeedFileSystemWithFixture("v_future_sample.save.json", futureSlotId);
            var sut = BuildSutWithMigration(fs);

            var result = sut.Load(futureSlotId);

            Assert.Equal(LoadStatus.MigrationFailed, result.Status);
            Assert.Null(result.Meta);
            Assert.Null(result.MigratedFromVersion);
            Assert.NotNull(result.Message);
            // 明确原因：消息里必须点出两个具体版本号，而不是一句笼统的"迁移失败"——呼应 10 第 5 节
            // "损坏存档处理...须捕获后转入'存档损坏'提示流程"，玩家/开发者需要知道具体是版本太新。
            Assert.Contains("999", result.Message);
            Assert.Contains(CurrentVersionForThisSuite.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Message);
        }

        [Fact]
        public void LoadFutureVersionSample_OriginalFileNotOverwrittenOrDeleted()
        {
            var futureSlotId = new Id("slot.migration_future_sample");
            var fs = SeedFileSystemWithFixture("v_future_sample.save.json", futureSlotId);
            var sut = BuildSutWithMigration(fs);
            var originalText = fs.ReadText($"user://saves/{futureSlotId.Value}.json");

            sut.Load(futureSlotId);

            // 10 第 5 节"损坏存档处理...并保留原始损坏文件不覆盖"——版本过新同样归入"不得覆盖/
            // 删除原始文件"的范畴（见 LoadStatus.MigrationFailed 文档）。
            Assert.Equal(originalText, fs.ReadText($"user://saves/{futureSlotId.Value}.json"));
        }
    }
}
