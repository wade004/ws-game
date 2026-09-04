using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 召唤与宠物接口（见 07 第 4 节"召唤与宠物复用 CreatureUnit，额外携带 ownerId 字段"、01 L3
    /// 模块表 <c>summon</c> 行）。由 <c>core/carriers/summon</c> 实现。
    /// </summary>
    public interface ISummonHost
    {
        /// <summary>由 <c>summon</c> 效果创建一个召唤物（见 07 第 4 节），返回其运行期实体 id；
        /// <paramref name="duration"/> 为 null 时按"跟随 owner 直到主动取消/死亡"处理，否则到期自动
        /// 销毁（见 07 第 4 节"召唤物生命周期"），成功时发出 <c>summon.created</c>。</summary>
        Id Summon(Id ownerId, Id creatureTemplateId, Vec2 position, double? duration = null);

        /// <summary>主动取消一个召唤物（见 07 第 4 节"直到主动取消"），发出 <c>summon.expired</c>。</summary>
        void Dismiss(Id summonId);

        /// <summary>查询某召唤物的拥有者；不存在或非召唤物时返回 null。</summary>
        Id? GetOwner(Id summonId);

        /// <summary>查询某单位当前拥有的全部召唤物 id 列表。</summary>
        IReadOnlyList<Id> GetSummons(Id ownerId);
    }
}
