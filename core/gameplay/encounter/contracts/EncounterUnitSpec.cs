using System;
using Core.Foundation.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <c>encounter.def.units[]</c> 的一条参战单位来源（见 08 第 4.1 节
    /// <c>units: List&lt;{spawnRef 或 creature.template, position}&gt;</c>、第 8 节 Spawn 归属说明
    /// "常驻于地图的可重复刷新对象走 spawnRef，仅本次遭遇临时生成走内联 creature.template"）。
    /// <see cref="SpawnRef"/>/<see cref="TemplateRef"/> 二选一（<see cref="EncounterContentValidationRule"/>
    /// 校验），<see cref="Position"/> 按任务书字段表标注可选——内联模板缺省时退化为
    /// <see cref="Vec2.Zero"/>（见 <c>EncounterHost</c> 判断记录）；<c>spawnRef</c> 来源缺省时忽略
    /// （该单位的落点由刷新表条目自己决定）。
    /// </summary>
    public sealed class EncounterUnitSpec
    {
        public Id? SpawnRef { get; }

        public Id? TemplateRef { get; }

        public Vec2? Position { get; }

        public EncounterUnitSpec(Id? spawnRef, Id? templateRef, Vec2? position)
        {
            if (spawnRef.HasValue == templateRef.HasValue)
            {
                throw new ArgumentException("spawnRef 与 templateRef 必须二选一（不能都提供或都不提供）");
            }
            SpawnRef = spawnRef;
            TemplateRef = templateRef;
            Position = position;
        }
    }
}
