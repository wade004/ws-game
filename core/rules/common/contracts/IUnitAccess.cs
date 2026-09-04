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
    }
}
