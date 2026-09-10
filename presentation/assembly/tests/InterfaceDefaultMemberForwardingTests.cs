using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Presentation.Assembly;
using Xunit;
using ReflectionAssembly = System.Reflection.Assembly;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 通用门禁（消费方反馈（编辑器）第 27 条根治的一部分，2026-09-11，见
    /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第27条.md）：接口新增"带默认实现的成员"
    /// （C# 8+ default interface member，<c>IsVirtual &amp;&amp; !IsAbstract</c>）本意是"不破坏既有
    /// 实现方的编译"（不构成公开 API 表面破坏性变更），但这天然会掩盖"组合/包装实现方忘了转发新成员"
    /// 这类遗漏——该成员在实现方眼里悄悄退化成默认值，编译期不报错、运行期也不抛异常，只是结果不对
    /// （<see cref="Core.Foundation.Expr.IExprSchema.KnownKeys"/> 在多个组合/包装实现上正是这样，见
    /// <c>core/rules/expr_host/CompositeExprSchema.cs</c>/<c>RulesExprSchema.cs</c>、
    /// <c>core/gameplay/quest/contracts/QuestExprSchemaEntries.cs</c> 的 <c>EventGroupPermissiveSchema</c>、
    /// <c>core/foundation/data_registry/core/RecordExprSchema.cs</c> 判断记录）。
    /// <para>
    /// 本测试反射枚举六个核心程序集（<see cref="SixAssemblies"/>：Core.Foundation/Core.Numbers/
    /// Core.Carriers/Core.Rules/Core.Gameplay/Presentation.Common，经
    /// <see cref="Presentation.Assembly.PresentationSchemaCatalog"/> 所在的 Presentation.Common
    /// 传递依赖链可达全部六个）中，全部在这六个程序集内声明的公开接口的"带默认实现的成员"，再枚举
    /// 这六个程序集内全部实现该接口的非抽象类型（含内部/私有嵌套类型——<see cref="Assembly.GetTypes"/>
    /// 本就返回程序集内定义的全部类型，不受可见性限制），用
    /// <see cref="Type.GetInterfaceMap(Type)"/> 判断该类型对每个默认成员的落地方法
    /// （<see cref="InterfaceMapping.TargetMethods"/>）是否仍然指向接口自身声明的默认实现方法
    /// （<c>DeclaringType == 接口类型</c>，意味着该类型没有显式转发/重写，悄悄吃掉了默认值）——若是，
    /// 断言失败并列出"类型.成员"，除非该组合已登记在 <see cref="Exemptions"/>（显式豁免清单，附理由）。
    /// </para>
    /// <para>
    /// 判断记录（豁免清单当前收录哪些、为什么）：见 <see cref="Exemptions"/> 字典各条目注释——均属于
    /// "该实现方是测试替身，专门用来验证默认实现兜底行为本身"（如
    /// <c>Tests.Foundation.Expr.ExprSchemaKnownKeysTests+LegacyFakeSchema</c>）或"默认值本身就是正确
    /// 语义，没有更好的值可转发"（如 <c>InMemoryDataSource.Root</c>——内存数据源没有根目录概念）。
    /// 本测试只反射六个生产程序集本身，不含各自配对的测试程序集（<c>Tests.Foundation</c>/
    /// <c>Tests.Rules</c> 等是独立程序集，不在 <see cref="SixAssemblies"/> 依赖链上，反射不到，
    /// 也就不会把这些"故意不转发"的测试替身当成生产代码遗漏——因此豁免清单里列出的测试替身条目仅供
    /// 人工核对时对照文档记录，本测试实际运行不会触发到它们；豁免清单真正拦截的是生产程序集内的
    /// 条目（如 <c>InMemoryDataSource.Root</c>）。
    /// </para>
    /// </summary>
    public class InterfaceDefaultMemberForwardingTests
    {
        /// <summary>
        /// 六个核心程序集（见类型注释）：用各自命名空间下一个具体类型的 <see cref="Type.Assembly"/>
        /// 取得程序集句柄，避免硬编码程序集文件名。
        /// </summary>
        private static readonly ReflectionAssembly[] SixAssemblies =
        {
            typeof(Core.Foundation.Expr.IExprSchema).Assembly,
            typeof(Core.Numbers.Progression.ProgressionHost).Assembly,
            typeof(Core.Carriers.Creature.CreatureFactory).Assembly,
            typeof(Core.Rules.ExprHost.RulesExprSchema).Assembly,
            typeof(Core.Gameplay.Assembly.GameplaySchemaCatalog).Assembly,
            typeof(PresentationSchemaCatalog).Assembly,
        };

        /// <summary>
        /// 显式豁免清单："类型全名.成员友好名" → 理由。只在确有"默认行为即正确、或该类型本就是专门
        /// 测试默认实现兜底行为的测试替身"时登记，登记前需在此写明具体理由（判断记录同 11 第 7/8 节
        /// 勘误"必须显式转发或登记豁免"）。
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> Exemptions = BuildExemptions();

        private static IReadOnlyDictionary<string, string> BuildExemptions()
        {
            var exemptions = new Dictionary<string, string>
            {
                ["Core.Foundation.DataRegistry.InMemoryDataSource.Root (getter)"] =
                    "内存数据源没有文件系统根目录概念（IDataSource.Root 的默认实现即恒返回 null，" +
                    "语义是“退化为用完整 Location 本身作为相对路径”，见 IDataSource.Root 成员判断记录）—— " +
                    "InMemoryDataSource 没有比 null 更好的值可转发，默认值本身就是正确语义，不是遗漏。",
            };

            // IPersistable.KeepStateWhenSectionMissing（见该接口成员判断记录）是刻意设计的"opt-in"
            // 默认接口成员：默认值 false（"存档缺段时清空到从未发生过的默认态"）是"绝大多数段"的正确
            // 语义，接口文档原句"不覆盖时对全部既有实现完全透明，不需要逐一改动既有 IPersistable
            // 实现签名"——不像 IExprSchema.KnownKeys 那样"包装/组合实现方应该转发内层结果、默认值必定
            // 是错的"，这里的"不覆盖"本身就是一个需要逐段判断、多数情况下答案正确的设计决策，不是
            // 遗漏。该成员此前已经过一轮专项复核（AUD-02 收边，architecture/落地计划/audit-85f1f4f-20260908），
            // 复核结论是：只有"缺段时清空到默认值会产生错误状态"的少数段需要显式覆盖为 true
            // （core/carriers/unit/core/UnitPersistable.cs 的 CurrentMapIdPersistable/ArchetypeIdPersistable/
            // CurrentPositionPersistable、core/foundation/save_system/core/RngStreamsPersistable.cs——
            // 均已按该复核显式覆盖，可在各自源码里核对判断记录），其余段（含 RaceIdPersistable——同一
            // 文件里紧邻 CurrentMapIdPersistable，判断记录明确写"不声明例外"并给出理由）经同一轮复核
            // 确认沿用默认值 false 是正确语义，逐一登记如下（成员统一为 KeepStateWhenSectionMissing
            // (getter)）。
            foreach (var type in new[]
            {
                "Core.Carriers.Gobj.GobjPendingLootPersistable",
                "Core.Carriers.Item.EquipmentPersistable",
                "Core.Carriers.Item.InventoryPersistable",
                "Core.Carriers.Unit.SkillBindingPersistable+Impl",
                "Core.Carriers.Unit.UnitPersistable+RaceIdPersistable",
                "Core.Foundation.SimLoop.TurnScheduler",
                "Core.Gameplay.Achievement.AchievementHost",
                "Core.Gameplay.Assembly.PlayerVitalsPersistable",
                "Core.Gameplay.Difficulty.DifficultyHost",
                "Core.Gameplay.Economy.CurrencyPersistable",
                "Core.Gameplay.Economy.VendorStockPersistable",
                "Core.Gameplay.Loot.DroppedLootPersistable",
                "Core.Gameplay.Quest.QuestPersistable",
                "Core.Gameplay.Spawn.SpawnHost",
                "Core.Gameplay.WorldState.WorldState",
                "Core.Numbers.Progression.ProgressionPersistable+Impl",
                "Core.Rules.Skill.KnownSkillsPersistable+Impl",
            })
            {
                exemptions[$"{type}.KeepStateWhenSectionMissing (getter)"] =
                    "IPersistable.KeepStateWhenSectionMissing 的 opt-in 设计（见接口成员判断记录）下，" +
                    "本段沿用默认值 false（存档缺段时清空为从未发生过的默认态）是 AUD-02 收边复核" +
                    "（architecture/落地计划/audit-85f1f4f-20260908）确认过的正确语义，不是遗漏——" +
                    "该段状态在“旧存档没有这个字段”时归零/清空属预期行为（新存档格式引入前的档案本就" +
                    "没有这段进度/状态），与需要显式覆盖为 true 的少数段（必填标识符缺乏合法空值、或" +
                    "与另一段存在必须同生共死的一致性约束）性质不同。";
            }

            return exemptions;
        }

        private static string FriendlyMemberName(MethodInfo m)
        {
            if (m.IsSpecialName)
            {
                if (m.Name.StartsWith("get_", StringComparison.Ordinal))
                {
                    return m.Name.Substring(4) + " (getter)";
                }
                if (m.Name.StartsWith("set_", StringComparison.Ordinal))
                {
                    return m.Name.Substring(4) + " (setter)";
                }
            }

            var paramTypes = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name));
            return $"{m.Name}({paramTypes})";
        }

        /// <summary>接口自身声明（<see cref="BindingFlags.DeclaredOnly"/>，不含继承自父接口的成员——
        /// 父接口的默认成员在枚举父接口本身时已经单独覆盖到）的公开、非静态"带默认实现"方法：
        /// <c>IsVirtual &amp;&amp; !IsAbstract</c>（接口里的必须实现成员同样 <c>IsVirtual == true</c>，
        /// 但 <c>IsAbstract == true</c>；带默认实现的成员是 <c>IsAbstract == false</c>，这是两者唯一
        /// 的区分点）。属性/索引器的 get/set 各自作为独立的方法出现在结果里（<see cref="FriendlyMemberName"/>
        /// 负责把 <c>get_Foo</c>/<c>set_Foo</c> 转成人类可读的 "Foo (getter)"/"Foo (setter)"）。</summary>
        private static MethodInfo[] DeclaredDefaultMethods(Type iface) =>
            iface.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.IsVirtual && !m.IsAbstract && !m.IsStatic)
                .ToArray();

        private static bool IsPubliclyVisible(Type t) => t.IsPublic || t.IsNestedPublic;

        [Fact]
        public void AllDefaultInterfaceMembers_AreExplicitlyForwardedOrExempted()
        {
            var allTypes = SixAssemblies.SelectMany(a => a.GetTypes()).ToArray();
            var sixAssemblySet = new HashSet<ReflectionAssembly>(SixAssemblies);

            // 候选实现方：六个程序集内全部非接口、非抽象类型（含内部/私有嵌套类型；抽象基类本身不需要
            // 转发——它的具体派生类如果转发了，GetInterfaceMap 在派生类上会显示 TargetMethod 指向该
            // 派生类/中间基类而不是接口本身，自然通过；如果没有任何具体派生类，说明这条抽象基类本身
            // 从未被真正实例化过，无需在这里报告)。
            var candidates = allTypes.Where(t => t.IsClass || t.IsValueType).Where(t => !t.IsAbstract).ToArray();

            var violations = new List<string>();

            foreach (var type in candidates)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (!sixAssemblySet.Contains(iface.Assembly)) continue;
                    if (!IsPubliclyVisible(iface)) continue;

                    var defaultMethods = DeclaredDefaultMethods(iface);
                    if (defaultMethods.Length == 0) continue;

                    InterfaceMapping map;
                    try
                    {
                        map = type.GetInterfaceMap(iface);
                    }
                    catch (Exception)
                    {
                        // 极端形态（如某些反射受限的生成类型）拿不到接口映射时跳过，不参与本门禁判定。
                        continue;
                    }

                    // 按 (Name, 参数类型序列) 建立 接口方法 -> 落地方法 的查找表，避免直接依赖
                    // MethodInfo 引用相等/Equals 在极少数反射路径下的不确定性。
                    var targetByKey = new Dictionary<string, MethodInfo>();
                    for (var i = 0; i < map.InterfaceMethods.Length; i++)
                    {
                        var key = MethodKey(map.InterfaceMethods[i]);
                        targetByKey[key] = map.TargetMethods[i];
                    }

                    foreach (var dm in defaultMethods)
                    {
                        if (!targetByKey.TryGetValue(MethodKey(dm), out var target))
                        {
                            continue; // 理论上不会发生：dm 本就来自同一个 iface。
                        }

                        var usesDefault = target.DeclaringType == dm.DeclaringType;
                        if (!usesDefault) continue;

                        var member = FriendlyMemberName(dm);
                        var exemptionKey = $"{type.FullName}.{member}";
                        if (Exemptions.ContainsKey(exemptionKey)) continue;

                        violations.Add(
                            $"{type.FullName} 实现 {iface.FullName} 时未显式转发/重写默认成员 {member}" +
                            $"（落回接口默认实现）");
                    }
                }
            }

            violations.Sort(StringComparer.Ordinal);

            Assert.True(
                violations.Count == 0,
                "以下类型对带默认实现的接口成员未显式转发/重写，也未登记豁免（见本类型 Exemptions，" +
                "需要逐个判断是补转发还是登记豁免+理由）：" + Environment.NewLine +
                string.Join(Environment.NewLine, violations));
        }

        private static string MethodKey(MethodInfo m) =>
            m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)) + ")";
    }
}
