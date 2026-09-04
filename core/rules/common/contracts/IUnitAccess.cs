using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// L2 四模块（skill/combat/targeting/ai）读写单位运行期状态的唯一通道（见任务书"单位状态门面"）。
    /// 属性/资源本身不经本接口——属性经 L1 <c>Core.Numbers.StatBlock.IStatHost</c>、资源（含生命值，见
    /// <see cref="WellKnownPowers"/>）经 L1 <c>Core.Numbers.PowerSet.IPowerHost</c>，本接口只暴露
    /// 05_对象模型与世界.md 第 1.2 节 <c>Unit</c> 字段里"不属于属性/资源池"的那一部分：存在性、位置、
    /// 阵营、等级、朝向、存活、模板引用、标签。
    /// <para>
    /// 实现方：L3 载体层（把 <c>Unit</c> 运行期实例接入）或测试用假实现；调用方：本目录下 skill/
    /// combat/targeting/ai 四个模块（见 README"谁实现、谁调用"矩阵）。本接口本身不持有任何状态，
    /// 只是一组契约方法签名。
    /// </para>
    /// </summary>
    public interface IUnitAccess
    {
        /// <summary>该 <paramref name="unitId"/> 当前是否存在于世界模拟中。</summary>
        bool Exists(Id unitId);

        /// <summary>全部当前存在的单位 id，按 <see cref="Id"/> 序数（<see cref="System.StringComparer.Ordinal"/>）排序，
        /// 保证同一批单位在不同调用间返回顺序一致（供确定性测试与遍历）。</summary>
        IReadOnlyList<Id> AllUnits { get; }

        /// <summary>世界平面坐标（见 05 第 3.1 节）。</summary>
        Vec2 GetPosition(Id unitId);

        /// <summary>写入世界平面坐标，供 <c>move</c>/<c>teleport</c> 等效果原语调用（见 06 第 3.2 节）。</summary>
        void SetPosition(Id unitId, Vec2 position);

        /// <summary>所属阵营（见 05 第 1.2 节 <c>Unit.factionId</c>）。</summary>
        Id GetFaction(Id unitId);

        /// <summary>当前等级，供命中表、护甲曲线等按等级差计算的公式引用。</summary>
        int GetLevel(Id unitId);

        /// <summary>朝向角度（见 05 第 3.3 节 <c>facing</c>，独立于移动方向存在）。</summary>
        double GetFacing(Id unitId);

        /// <summary>存活状态（见 05 第 1.2 节 <c>Unit.alive</c>）。</summary>
        bool IsAlive(Id unitId);

        /// <summary>写入存活状态，供 Combat 模块的死亡结算（见 06 第 4.6 节）调用。</summary>
        void SetAlive(Id unitId, bool alive);

        /// <summary>来源的内容模板 id（见 05 第 1.1 节 <c>Entity.templateId</c>）；手工放置对象可为空。</summary>
        Id? GetTemplateId(Id unitId);

        /// <summary>该单位的标签集合，供 <see cref="UnitFilter.RequiredTags"/>/<see cref="UnitFilter.ExcludedTags"/>
        /// 一类按标签过滤的场景使用。</summary>
        IReadOnlyList<Id> GetTags(Id unitId);

        /// <summary>
        /// 集成任务补齐的契约缺口：该单位所属地图 id（见 <c>ai</c> 模块 README"契约缺口"一节——
        /// <c>MapId</c> 原先只存在于 <c>Core.Foundation.SimLoop.Entity</c>，L3 载体层把 <c>Unit</c>
        /// 接入 <c>IWorldSim</c> 时才会用到，本接口原版没有暴露；<c>ai</c> 模块曾用
        /// <c>AiOptions.MapId</c> 单个固定值作为权宜之计，只适用于单地图场景）。真正按单位所在
        /// 地图分别寻路（多地图场景）需要这里返回真实值。
        /// <para>
        /// 用 C#8 默认接口方法（<c>=&gt; null</c>）而不是把它做成必须实现的抽象成员：本接口已有
        /// 四个模块各自的测试假实现（<c>combat</c>/<c>targeting</c>/<c>skill</c>/<c>ai</c> 的
        /// <c>FakeUnitAccess</c>/<c>StubUnitAccess</c>），本次集成任务的改动范围明确限定在
        /// <c>common</c>/<c>sim_loop</c>/<c>skill</c>/<c>ai</c>/新建目录，不允许连带修改
        /// <c>combat</c>/<c>targeting</c> 的测试文件；默认实现返回 <c>null</c>（"未知/不接入地图
        /// 概念"）保证这些既有假实现不必跟着改也能继续通过编译，行为等价于集成前
        /// （<c>AiOptions.MapId</c> 分支不受影响）。真正按单位接入地图的实现（如本任务
        /// <c>core/rules/assembly</c> 里基于 <c>Entity.MapId</c> 的 <c>WorldUnitAccess</c>）应
        /// override 本方法返回真实值。
        /// </para>
        /// </summary>
        Id? GetMapId(Id unitId) => null;
    }
}
