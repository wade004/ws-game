using Core.Foundation.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// 生物模板的只读查询出口（见 07 第 2 节 Creature 补充信息表"契约：只读模板查询（经
    /// DataRegistry），运行期状态经 Unit 相关契约（06）"）。由 <see cref="CreatureFactory"/> 实现
    /// （构造期已经从 <c>Core.Foundation.DataRegistry.IDataRegistryView</c> 解析出全部
    /// <see cref="CreatureTemplate"/>，本接口只是把这份内存索引对外暴露一层只读视图，不重新查询
    /// registry）。
    /// </summary>
    public interface ICreatureTemplateQuery
    {
        /// <summary>按 <paramref name="templateId"/> 取强类型模板；未登记的模板 id 抛
        /// <see cref="System.ArgumentException"/>。</summary>
        CreatureTemplate Get(Id templateId);

        /// <summary>该模板是否挂了 <paramref name="flag"/> 这个职能标志（见 07 第 2.2 节）；
        /// 未登记的模板 id 抛 <see cref="System.ArgumentException"/>。</summary>
        bool HasFlag(Id templateId, NpcFlag flag);
    }
}
