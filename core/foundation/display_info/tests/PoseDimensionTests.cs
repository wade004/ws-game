using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary>
    /// 手感设计/04（ADR-0119）姿势维度：键语法与回落链、旧键等价、anim_set 继承、标准姿势清单校验、框架级假人姿势集零错误。
    /// 期望值一律由规则算出（链按维度顺序拼，缺项按"去尾"求），不写死裸数。
    /// </summary>
    public class PoseDimensionTests
    {
        // ------------------------------------------------------------------ 键语法与回落链

        [Fact]
        public void Chain_FullRequest_DropsVariantFamilyStanceGaitInOrder()
        {
            var request = new PoseRequest("move", gait: "run", stance: "combat", family: "2h", variant: "wounded");

            Assert.Equal(
                new[] { "move.run.combat.2h.wounded", "move.run.combat.2h", "move.run.combat", "move.run", "move" },
                request.Chain());
        }

        [Fact]
        public void Chain_AbsentDimensionsAreSkipped_PeaceStanceIsOmitted()
        {
            // 姿态 peace 是缺省值，键里省略；没有步态/变体的维度不产生条目。
            var request = new PoseRequest("idle", stance: "peace", family: "1h");
            Assert.Equal(new[] { "idle.1h", "idle" }, request.Chain());
            Assert.Equal(new[] { "idle.combat", "idle" }, new PoseRequest("idle", stance: "combat").Chain());
        }

        [Fact]
        public void Chain_GaitOnlyAppliesToMoveState()
        {
            var attack = new PoseRequest("attack", gait: "run", stance: "combat", family: "greatsword", variant: "heavy");
            Assert.Equal(
                new[] { "attack.combat.greatsword.heavy", "attack.combat.greatsword", "attack.combat", "attack" },
                attack.Chain());
        }

        [Fact]
        public void Resolve_AttackRunCombatGreatswordHeavy_FallsBackToAttackCombat_RecordingEveryStep()
        {
            // 04 第 2.2 节：请求带步态/姿态/武器族/变体，表里只有 attack.combat 与 attack。
            var table = Table("attack", "attack.combat");
            var request = new PoseRequest("attack", gait: "run", stance: "combat", family: "greatsword", variant: "heavy");

            var ok = PoseResolver.TryResolve(request, table, out var value, out var resolution);

            Assert.True(ok);
            Assert.Equal("attack.combat", resolution.CanonicalKey);
            Assert.Equal("attack.combat", resolution.TableKey);
            Assert.Equal(2, resolution.FallbackDepth); // 去变体、去武器族后命中
            Assert.Equal(
                new[] { "attack.combat.greatsword.heavy", "attack.combat.greatsword", "attack.combat" },
                resolution.Tried);
            Assert.Equal("attack.combat", value);
        }

        [Fact]
        public void Chain_SprintRequest_RetriesWithRunBeforeDroppingGait()
        {
            var request = new PoseRequest("move", gait: "sprint", stance: "combat", family: "2h");
            Assert.Equal(
                new[] { "move.sprint.combat.2h", "move.sprint.combat", "move.sprint",
                        "move.run.combat.2h", "move.run.combat", "move.run", "move" },
                request.Chain());

            // 只有 move.run 与 move.walk 的姿势集（框架必备键，没有基础键 move）：冲刺落到 move.run，而不是解析不到。
            PoseResolver.TryResolve(request, Table("move.run", "move.walk"), out _, out var resolution);
            Assert.Equal("move.run", resolution.CanonicalKey);
        }

        [Fact]
        public void Resolve_PicksMostSpecificPresentKey_AndStopsThere()
        {
            var table = Table("move", "move.run", "move.run.combat.2h", "move.walk");
            var request = new PoseRequest("move", gait: "run", stance: "combat", family: "2h", variant: "wounded");

            PoseResolver.TryResolve(request, table, out _, out var resolution);

            Assert.Equal("move.run.combat.2h", resolution.CanonicalKey);
            Assert.Equal(1, resolution.FallbackDepth);
            Assert.Equal(new[] { "move.run.combat.2h.wounded", "move.run.combat.2h" }, resolution.Tried);
        }

        [Fact]
        public void Resolve_WhenOnlyBaseKeyExists_AnyDimensionCombinationResolves()
        {
            // 不变量：只要基础键存在，任意维度组合都能解析（04 第 9 节第 4 条）。
            var table = Table("idle", "move", "attack");
            var gaits = new string?[] { null, "walk", "run", "sprint" };
            var stances = new string?[] { null, "peace", "combat" };
            var families = new string?[] { null, "1h", "2h", "bow" };
            var variants = new string?[] { null, "wounded" };

            foreach (var state in new[] { "idle", "move", "attack" })
            foreach (var gait in gaits)
            foreach (var stance in stances)
            foreach (var family in families)
            foreach (var variant in variants)
            {
                var request = new PoseRequest(state, gait, stance, family, variant);
                Assert.True(PoseResolver.TryResolve(request, table, out _, out var resolution), request.ToString());
                Assert.Equal(state, resolution.CanonicalKey);
            }
        }

        [Fact]
        public void Resolve_NoBaseKey_ReportsNotFoundWithFullChain()
        {
            var table = Table("idle");
            var ok = PoseResolver.TryResolve(new PoseRequest("move", gait: "run"), table, out _, out var resolution);

            Assert.False(ok);
            Assert.False(resolution.Found);
            Assert.Equal(new[] { "move.run", "move" }, resolution.Tried);
        }

        [Fact]
        public void Resolve_UsabilityProbeOnlyAppliesToNonBaseKeys()
        {
            var table = Table("idle", "idle.combat");
            var request = new PoseRequest("idle", stance: "combat");

            // 变体未就绪 -> 回落基础键；基础键即使探针说"不可用"也作为最后兜底。
            PoseResolver.TryResolve(request, table, out _, out var notReady, k => false);
            Assert.Equal("idle", notReady.CanonicalKey);

            PoseResolver.TryResolve(request, table, out _, out var ready, k => true);
            Assert.Equal("idle.combat", ready.CanonicalKey);
        }

        // ------------------------------------------------------------------ 旧键 combat_<state> 等价

        [Fact]
        public void LegacyKey_CombatPrefix_IsEquivalentToStateDotCombat()
        {
            Assert.Equal("idle.combat", PoseKeys.Canonicalize("combat_idle"));
            Assert.Equal("idle", PoseKeys.Canonicalize("idle"));
            Assert.Equal("combat_move.run", PoseKeys.Canonicalize("combat_move.run")); // 非"状态+combat"两段形态不改写
            Assert.True(PoseKeys.TryGetLegacyAlias("move.combat", out var legacy));
            Assert.Equal("combat_move", legacy);
            Assert.False(PoseKeys.TryGetLegacyAlias("move.run.combat", out _));
        }

        [Fact]
        public void Resolve_LegacyTableKey_IsFoundByCanonicalRequest_AndCanonicalWinsWhenBothExist()
        {
            var legacyOnly = Table("idle", "combat_idle");
            PoseResolver.TryResolve(new PoseRequest("idle", stance: "combat"), legacyOnly, out var clip, out var r1);
            Assert.Equal("combat_idle", r1.TableKey);
            Assert.Equal("idle.combat", r1.CanonicalKey);
            Assert.Equal("combat_idle", clip);

            var both = Table("idle", "combat_idle", "idle.combat");
            PoseResolver.TryResolve(new PoseRequest("idle", stance: "combat"), both, out _, out var r2);
            Assert.Equal("idle.combat", r2.TableKey);
        }

        [Fact]
        public void Resolve_OnLegacyTables_MatchesPreDimensionLookup_ForEveryStateAndStance()
        {
            // 复现 + 不变量：既有数据（只有基础键与 combat_<state> 变体键）在"状态 + 战斗姿态"两维下选出的键，
            // 与改动前的两级查表（战斗中先 combat_<state> 后 <state>，否则 <state>）逐位一致。
            var states = new[] { "idle", "move", "attack", "cast", "hit", "death", "jump" };
            var subsetKeys = states.Concat(states.Select(s => "combat_" + s)).ToArray();
            var rng = new Random(20261002);

            for (var trial = 0; trial < 400; trial++)
            {
                var keys = subsetKeys.Where(_ => rng.Next(2) == 0).ToArray();
                var table = keys.ToDictionary(k => k, k => k, StringComparer.Ordinal);

                foreach (var state in states)
                foreach (var inCombat in new[] { false, true })
                {
                    string? legacy = null;
                    if (inCombat && table.ContainsKey("combat_" + state)) legacy = "combat_" + state;
                    else if (table.ContainsKey(state)) legacy = state;

                    var request = new PoseRequest(state, stance: inCombat ? "combat" : null);
                    var found = PoseResolver.TryResolve(request, table, out var picked, out _);

                    Assert.Equal(legacy != null, found);
                    Assert.Equal(legacy, found ? picked : null);
                }
            }
        }

        [Fact]
        public void KeySyntax_WellFormedAndFallbackTarget()
        {
            Assert.True(PoseKeys.IsWellFormed("move.run.combat.2h"));
            Assert.True(PoseKeys.IsWellFormed("combat_idle"));
            Assert.False(PoseKeys.IsWellFormed("Move.Run"));
            Assert.False(PoseKeys.IsWellFormed("move..run"));
            Assert.False(PoseKeys.IsWellFormed(".move"));
            Assert.False(PoseKeys.IsWellFormed(""));

            var present = new HashSet<string>(new[] { "move", "move.run" }, StringComparer.Ordinal);
            Assert.Equal("move.run", PoseResolver.FallbackTargetOf("move.run.combat.2h", present.Contains));
            Assert.Null(PoseResolver.FallbackTargetOf("cast.combat", present.Contains));
        }

        // ------------------------------------------------------------------ anim_set extends

        private const string ParentRow = @"{
          ""id"": ""display.anim_set.base_biped"",
          ""clips"": {
            ""idle"": {""resource_ref"": ""sprite_anim.base_idle"", ""events"": []},
            ""combat_idle"": {""resource_ref"": ""sprite_anim.base_idle_combat"", ""events"": []},
            ""move.walk"": {""resource_ref"": ""sprite_anim.base_walk"", ""events"": [{""name"": ""footstep"", ""time_pct"": 0.25}]},
            ""attack"": {""resource_ref"": ""sprite_anim.base_attack"", ""events"": [{""name"": ""hit"", ""time_pct"": 0.4}]}
          }
        }";

        private static (IDataRegistry Registry, ValidationReport Report) Build(params string[] rows) =>
            DisplayInfoTestSupport.BuildRegistry(
                new Dictionary<string, string> { ["display.anim_set"] = "[" + string.Join(",", rows) + "]" },
                new IValidationRule[] { new AnimSetPoseRule() });

        [Fact]
        public void Extends_ChildOverridesOneKey_RestInheritedFromParent()
        {
            const string child = @"{
              ""id"": ""display.anim_set.heavy_biped"",
              ""extends"": ""display.anim_set.base_biped"",
              ""clips"": { ""move.walk"": {""resource_ref"": ""sprite_anim.heavy_walk"", ""events"": []} }
            }";
            var (registry, report) = Build(ParentRow, child);
            Assert.False(report.IsBlocking);

            var def = AnimSetDef.FromRecord(registry.Get("display.anim_set", "display.anim_set.heavy_biped")!, registry);

            Assert.Equal(new Id("display.anim_set.base_biped"), def.Extends);
            Assert.Equal(new[] { "attack", "combat_idle", "idle", "move.walk" }, def.Clips.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            // 覆盖的键用子集的资源，其余键逐项等于父集的。
            Assert.Equal(new Id("sprite_anim.heavy_walk"), def.Clips["move.walk"].ResourceRef);
            Assert.Empty(def.Clips["move.walk"].Events);
            var parent = AnimSetDef.FromRecord(registry.Get("display.anim_set", "display.anim_set.base_biped")!, registry);
            foreach (var key in new[] { "idle", "combat_idle", "attack" })
            {
                Assert.Equal(parent.Clips[key].ResourceRef, def.Clips[key].ResourceRef);
            }
        }

        [Fact]
        public void Extends_OverrideMatchesByCanonicalKey_NewSpellingReplacesLegacySpelling()
        {
            const string child = @"{
              ""id"": ""display.anim_set.c1"",
              ""extends"": ""display.anim_set.base_biped"",
              ""clips"": { ""idle.combat"": {""resource_ref"": ""sprite_anim.c1_idle_combat"", ""events"": []} }
            }";
            var (registry, _) = Build(ParentRow, child);

            var def = AnimSetDef.FromRecord(registry.Get("display.anim_set", "display.anim_set.c1")!, registry);

            Assert.False(def.Clips.ContainsKey("combat_idle")); // 被同一规范键的新写法替换，不并存
            Assert.Equal(new Id("sprite_anim.c1_idle_combat"), def.Clips["idle.combat"].ResourceRef);

            PoseResolver.TryResolve(new PoseRequest("idle", stance: "combat"), def.Clips, out var clip, out _);
            Assert.Equal(new Id("sprite_anim.c1_idle_combat"), clip.ResourceRef);
        }

        [Fact]
        public void Extends_MultiLevelChain_NearestAncestorWins_AndMatchesHandWrittenEquivalent()
        {
            const string mid = @"{ ""id"": ""display.anim_set.mid"", ""extends"": ""display.anim_set.base_biped"",
              ""clips"": { ""attack"": {""resource_ref"": ""sprite_anim.mid_attack"", ""events"": []},
                           ""hit"": {""resource_ref"": ""sprite_anim.mid_hit"", ""events"": []} } }";
            const string leaf = @"{ ""id"": ""display.anim_set.leaf"", ""extends"": ""display.anim_set.mid"",
              ""clips"": { ""hit"": {""resource_ref"": ""sprite_anim.leaf_hit"", ""events"": []} } }";
            const string handWritten = @"{ ""id"": ""display.anim_set.hand"", ""clips"": {
              ""idle"": {""resource_ref"": ""sprite_anim.base_idle"", ""events"": []},
              ""combat_idle"": {""resource_ref"": ""sprite_anim.base_idle_combat"", ""events"": []},
              ""move.walk"": {""resource_ref"": ""sprite_anim.base_walk"", ""events"": [{""name"": ""footstep"", ""time_pct"": 0.25}]},
              ""attack"": {""resource_ref"": ""sprite_anim.mid_attack"", ""events"": []},
              ""hit"": {""resource_ref"": ""sprite_anim.leaf_hit"", ""events"": []} } }";
            var (registry, report) = Build(ParentRow, mid, leaf, handWritten);
            Assert.False(report.IsBlocking);

            var merged = AnimSetDef.FromRecord(registry.Get("display.anim_set", "display.anim_set.leaf")!, registry);
            var hand = AnimSetDef.FromRecord(registry.Get("display.anim_set", "display.anim_set.hand")!, registry);

            Assert.Equal(
                hand.Clips.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value.ResourceRef.Value).ToArray(),
                merged.Clips.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value.ResourceRef.Value).ToArray());
            // 继承合并后的完整性报告与手写等价集一致（04 第 9 节第 5 条）
            var a = PoseChecklist.Evaluate(merged.Clips.Keys);
            var b = PoseChecklist.Evaluate(hand.Clips.Keys);
            Assert.Equal(a.MissingRequired.Select(f => f.Describe()), b.MissingRequired.Select(f => f.Describe()));
            Assert.Equal(a.MissingRecommended.Select(f => f.Describe()), b.MissingRecommended.Select(f => f.Describe()));
        }

        [Fact]
        public void Extends_WithoutExtends_FromRecordWithRegistryEqualsPlainFromRecord()
        {
            var (registry, _) = Build(ParentRow);
            var record = registry.Get("display.anim_set", "display.anim_set.base_biped")!;

            var plain = AnimSetDef.FromRecord(record);
            var withRegistry = AnimSetDef.FromRecord(record, registry);

            Assert.Null(withRegistry.Extends);
            Assert.Equal(plain.Clips.Keys.OrderBy(k => k, StringComparer.Ordinal), withRegistry.Clips.Keys.OrderBy(k => k, StringComparer.Ordinal));
        }

        [Fact]
        public void Extends_Cycle_IsReportedAsErrorAndThrowsOnResolve()
        {
            const string a = @"{ ""id"": ""display.anim_set.cyc_a"", ""extends"": ""display.anim_set.cyc_b"", ""clips"": {} }";
            const string b = @"{ ""id"": ""display.anim_set.cyc_b"", ""extends"": ""display.anim_set.cyc_a"", ""clips"": {} }";
            var (registry, report) = Build(a, b);

            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == AnimSetPoseRule.CheckExtends && i.Severity == ValidationSeverity.Error
                && i.RecordKey == "display.anim_set.cyc_a");
            Assert.Contains(report.Issues, i => i.Check == AnimSetPoseRule.CheckExtends && i.RecordKey == "display.anim_set.cyc_b");

            // 阻断态的 registry 直接 Get 会抛；用只读容错视图拿记录，解析继承时按环抛 DataFieldException（不静默降级）。
            var tolerant = new TolerantRegistryView(registry);
            var record = tolerant.Get("display.anim_set", "display.anim_set.cyc_a")!;
            Assert.Throws<DataFieldException>(() => AnimSetDef.FromRecord(record, tolerant));
        }

        [Fact]
        public void Extends_Self_IsError()
        {
            const string a = @"{ ""id"": ""display.anim_set.selfie"", ""extends"": ""display.anim_set.selfie"", ""clips"": {} }";
            var (_, report) = Build(a);

            Assert.Contains(report.Issues, i => i.Check == AnimSetPoseRule.CheckExtends && i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void Extends_MissingParent_IsReportedByReferenceIntegrity()
        {
            const string a = @"{ ""id"": ""display.anim_set.orphan"", ""extends"": ""display.anim_set.nobody"", ""clips"": {} }";
            var (_, report) = Build(a);

            // 不存在的父集由 extends 字段的引用完整性检查报告（Error），规则本身不重复。
            Assert.True(report.IsBlocking);
            Assert.DoesNotContain(report.Issues, i => i.Check == AnimSetPoseRule.CheckExtends);
        }

        // ------------------------------------------------------------------ 标准姿势清单校验

        private static string StdRow(string id, IEnumerable<string> keys, string extra = "") =>
            "{ \"id\": \"" + id + "\", " + extra + "\"clips\": {"
            + string.Join(",", keys.Select(k => "\"" + k + "\": {\"resource_ref\": \"sprite_anim.x_" + k.ToLowerInvariant().Replace('.', '_').Replace('-', '_') + "\", \"events\": []}"))
            + "} }";

        private static readonly string[] Required = { "idle", "move.walk", "move.run", "attack", "hit", "death" };

        [Fact]
        public void Checklist_FrameworkSetMissingRequired_IsErrorPerMissingKey()
        {
            var keys = Required.Where(k => k != "death" && k != "move.run");
            var (_, report) = Build(StdRow("display.anim_set.std_probe", keys));

            var errors = report.Issues.Where(i => i.Check == AnimSetPoseRule.CheckRequired).ToArray();
            Assert.Equal(2, errors.Length);
            Assert.All(errors, e => Assert.Equal(ValidationSeverity.Error, e.Severity));
            Assert.Contains(errors, e => e.Message.Contains("move.run"));
            Assert.Contains(errors, e => e.Message.Contains("death"));
            Assert.True(report.IsBlocking);
        }

        [Fact]
        public void Checklist_MissingRecommended_IsWarningNamingTheFallbackKey()
        {
            // 必备齐全、只有 idle.combat 缺项之外的推荐键也缺 -> 警告，且 hit.heavy 的回落目标是 hit。
            var (_, report) = Build(StdRow("display.anim_set.std_probe2", Required));

            Assert.False(report.IsBlocking);
            var warnings = report.Issues.Where(i => i.Check == AnimSetPoseRule.CheckRecommended).ToArray();
            Assert.All(warnings, w => Assert.Equal(ValidationSeverity.Warning, w.Severity));
            var heavy = Assert.Single(warnings, w => w.Message.Contains("hit.heavy"));
            Assert.Contains("回落到 hit", heavy.Message);
            var combatRun = Assert.Single(warnings, w => w.Message.Contains("move.run.combat"));
            Assert.Contains("回落到 move.run", combatRun.Message);
            // 没有可回落键的推荐项（cast）如实写出
            Assert.Contains(warnings, w => w.Message.Contains("cast") && w.Message.Contains("无可回落"));
            // 推荐键总数 = 清单推荐项数（全部缺）
            Assert.Equal(PoseChecklist.Entries.Count(e => e.Tier == PoseTier.Recommended), warnings.Length);
        }

        [Fact]
        public void Checklist_OptionalMissing_IsSilent_AndLegacyKeysCountAsCanonical()
        {
            var keys = Required.Concat(new[] { "combat_idle", "move.run.combat", "attack.1h.02", "attack.2h.03",
                "hit.heavy", "hit.knockback", "hit.knockdown", "hit.getup", "cast", "dodge", "jump" });
            var (_, report) = Build(StdRow("display.anim_set.std_complete", keys));

            Assert.Empty(report.Issues.Where(i => i.Check == AnimSetPoseRule.CheckRequired || i.Check == AnimSetPoseRule.CheckRecommended));
            // 完整性报告里可选项（move.sprint 等）仍列出，但不产生校验问题
            var evaluated = PoseChecklist.Evaluate(keys);
            Assert.True(evaluated.IsPublishable);
            Assert.Contains(evaluated.MissingOptional, f => f.Entry.Key == "move.sprint");
            Assert.Contains(evaluated.MissingOptional, f => f.Entry.Key == "move.start" && f.Describe().Contains("混合"));
        }

        [Fact]
        public void Checklist_IsOptIn_LegacyAndSampleSetsAreUntouched()
        {
            // 只有 7 个状态键、没有 move.walk/run 的既有姿势集不声明 pose_standard 就不受清单约束。
            var legacy = StdRow("display.anim_set.legacy_sample", new[] { "idle", "move", "attack", "cast", "hit", "death" });
            var (_, report) = Build(legacy);
            Assert.Empty(report.Issues);

            // 显式声明后同一份内容才按清单校验（必备缺 move.walk、move.run）
            var optedIn = StdRow("display.anim_set.legacy_opted", new[] { "idle", "move", "attack", "cast", "hit", "death" }, "\"pose_standard\": true, ");
            var (_, report2) = Build(optedIn);
            Assert.Equal(2, report2.Issues.Count(i => i.Check == AnimSetPoseRule.CheckRequired));
        }

        [Fact]
        public void Checklist_ExtendsMergedSetSatisfiesRequiredKeys()
        {
            var parent = StdRow("display.anim_set.std_parent", Required);
            var child = StdRow("display.anim_set.std_child", new[] { "hit.heavy" }, "\"extends\": \"display.anim_set.std_parent\", ");
            var (_, report) = Build(parent, child);

            // 子集只声明一个键，必备键经继承齐全：无必备错误。
            Assert.Empty(report.Issues.Where(i => i.Check == AnimSetPoseRule.CheckRequired));
        }

        [Fact]
        public void KeySyntax_BadKeyIsWarning_OnlyForOptedInSets()
        {
            // 选入清单的集：非法键名为警告（不是错误）
            var optedIn = StdRow("display.anim_set.odd", Required.Concat(new[] { "Weird-Key" }), "\"pose_standard\": true, ");
            var (_, report) = Build(optedIn);
            var issue = Assert.Single(report.Issues, i => i.Check == AnimSetPoseRule.CheckKeySyntax);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);

            // 未选入的既有数据：自由键名不受影响（默认严格级别下警告也阻断加载，不能让既有数据新增阻断）
            var legacy = StdRow("display.anim_set.legacy_odd", new[] { "idle", "Weird-Key" });
            var (_, report2) = Build(legacy);
            Assert.Empty(report2.Issues);
            Assert.False(report2.IsBlocking);
        }

        // ------------------------------------------------------------------ 框架级假人姿势集

        [Fact]
        public void StdDummyBiped_FrameworkData_PassesPoseValidationWithZeroIssues()
        {
            var path = Path.Combine(FindRepoRoot(), "data", "_framework", "display", "display.anim_set.json");
            var source = new InMemoryDataSource();
            source.Add("display.anim_set", File.ReadAllText(path, Encoding.UTF8));
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, DisplayInfoTestSupport.CreateBus());
            foreach (var schema in DisplaySchemas.All) registry.RegisterSchema(schema);
            registry.RegisterValidationRule(new AnimSetPoseRule());
            registry.RegisterValidationRule(new AnimSetEventsShapeRule());
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking);
            Assert.Empty(report.Issues);

            var def = AnimSetDef.FromRecord(registry.Get("display.anim_set", "display.anim_set.std_dummy_biped")!, registry);
            var checklist = PoseChecklist.Evaluate(def.Clips.Keys);
            Assert.True(checklist.IsPublishable);
            Assert.Empty(checklist.MissingRecommended);

            // 解析抽查：2h 武器族跑步战斗姿态命中 move.run.combat.2h；
            PoseResolver.TryResolve(new PoseRequest("move", "run", "combat", "2h"), def.Clips, out _, out var r1);
            Assert.Equal("move.run.combat.2h", r1.CanonicalKey);
            // 假人集没有 move.sprint 系列：冲刺（战斗、2h）先试完带 sprint 的候选，再按 run 重走，命中 move.run.combat.2h。
            PoseResolver.TryResolve(new PoseRequest("move", "sprint", "combat", "2h"), def.Clips, out _, out var r2);
            Assert.Equal("move.sprint.combat.2h", r2.Tried[0]);
            Assert.Equal("move.run.combat.2h", r2.CanonicalKey);
        }

        // ------------------------------------------------------------------ 辅助

        private static Dictionary<string, string> Table(params string[] keys) =>
            keys.ToDictionary(k => k, k => k, StringComparer.Ordinal);

        private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            {
                dir = dir.Parent;
            }
            return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根");
        }
    }
}
