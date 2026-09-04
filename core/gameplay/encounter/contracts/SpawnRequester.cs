using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// 按刷新表条目生成参战单位的具名委托（见 00 架构总则"携带参数的回调按各自接口语义定义具名
    /// 委托，不复用裸 Action/Func"惯例，同 <c>core/carriers/creature</c> 的 <c>AiRegistrar</c>）。
    /// <para>
    /// 契约缺口判断记录：<c>core/gameplay/spawn</c>（刷新模块，08 第 8 节，与本任务并行由另一
    /// agent 开发）尚未提供公开契约供本模块直接引用；<see cref="EncounterUnitSpec.SpawnRef"/>/
    /// <see cref="EncounterWaveDefinition.SpawnRefs"/> 都需要"给一个 <c>spawn.table</c> 条目 id，
    /// 生成对应单位、返回生成的实体 id 列表"这一能力，本委托就是组装层（持有具体
    /// <c>SpawnHost</c>/等价实现）注入 <see cref="EncounterHost"/> 的桥接点——组装期把
    /// <c>(spawnId, mapId) =&gt; spawnHost.Execute(spawnId, mapId)</c> 一类适配逻辑接到本委托签名。
    /// </para>
    /// </summary>
    /// <param name="spawnId"><c>spawn.table</c> 条目引用。</param>
    /// <param name="mapId">生成目标地图。</param>
    /// <returns>本次调用生成的运行期实体 id 列表（可能为空——如刷新表条目因冷却/上限暂不生成）。</returns>
    public delegate IReadOnlyList<Id> SpawnRequester(Id spawnId, Id mapId);
}
