using System;
using System.Linq;
using Core.Foundation.DataRegistry;
using Core.Gameplay.Spawn;
using Xunit;

namespace Tests.Gameplay.Spawn
{
    /// <summary>
    /// ADR-0019 / F1b 勘察结论：<c>spawn.table</c>（<see cref="SpawnSchemas.Table"/>）的全部字段均为
    /// 标量（<c>Id</c>/<c>Vec2</c>/<c>Number</c>/<c>String</c>/<c>Expr</c>/<c>Enum</c>），不含任何
    /// <see cref="FieldKind.Object"/>/<see cref="FieldKind.Array"/> 复合字段——本轮任务书点名的
    /// "<c>spawn.table</c> 条目与重生策略（<see cref="SpawnRespawnPolicyFieldGroupRule"/> 若为纯结构则
    /// 退役为变体登记）"核实结论是**不适用**：<c>respawn_policy</c>（<c>Enum</c>）与
    /// <c>respawn_timer</c>（<c>Number</c>）是 <c>spawn.table</c> 行内两个平级的标量字段，不是"某个
    /// 复合字段的内部子结构"——<see cref="FieldSchema.Variants"/> 要求判别字段与被判别子字段同处一个
    /// <c>JsonObject</c>（<see cref="VariantSchema"/>），而 <c>respawn_policy</c>/<c>respawn_timer</c>
    /// 之间根本没有共同的父 <c>Object</c> 字段可以挂载 <c>Variants</c>；<c>Fields</c> 同样要求先有一个
    /// <see cref="FieldKind.Object"/> 字段。ADR-0019 的 <c>Fields</c>/<c>Item</c>/<c>Variants</c> 机制
    /// 面向"复合字段的子结构登记"（04 第 3.2 节），不面向"表内任意两个平级标量字段之间的一致性"，
    /// 因此本模块没有任何字段可以升级登记，<see cref="SpawnRespawnPolicyFieldGroupRule"/> 原样保留
    /// （详见 <c>schema/README.md</c>"退役规则"一节）。本文件改为锁定两条仍然有效、值得机器守护的
    /// 一致性：<c>respawn_policy</c> 的 wire 值集合与 <see cref="RespawnPolicy"/> 枚举全集一致、
    /// <c>spawn.table</c> 确实不含任何 Object/Array 字段（后续若真的新增复合字段，本测试会失败，
    /// 提醒作者重新评估是否需要补登记）。
    /// </summary>
    public sealed class SpawnSchemaCoverageTests
    {
        [Fact]
        public void RespawnPolicyValues_MatchRespawnPolicyNamesFullSet()
        {
            var registered = new System.Collections.Generic.HashSet<string>(SpawnSchemas.RespawnPolicyValues, StringComparer.Ordinal);
            var expected = new System.Collections.Generic.HashSet<string>(
                ((RespawnPolicy[])Enum.GetValues(typeof(RespawnPolicy))).Select(RespawnPolicyNames.ToText),
                StringComparer.Ordinal);

            Assert.Equal(expected, registered);

            var respawnPolicyField = SpawnSchemas.Table.GetField("respawn_policy");
            Assert.NotNull(respawnPolicyField);
            Assert.Equal(FieldKind.Enum, respawnPolicyField!.Kind);
            Assert.Equal(expected, new System.Collections.Generic.HashSet<string>(respawnPolicyField.EnumValues!, StringComparer.Ordinal));
        }

        [Fact]
        public void SpawnTable_HasNoObjectOrArrayFields_NoSubstructureToRegister()
        {
            Assert.DoesNotContain(SpawnSchemas.Table.Fields, f => f.Kind == FieldKind.Object || f.Kind == FieldKind.Array);
        }
    }
}
