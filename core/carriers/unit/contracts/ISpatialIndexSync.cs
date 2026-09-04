using Core.Foundation.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 拍板（见任务书"WorldUnitAccess : IUnitAccess"一节）：<c>ISpatialQuery</c>（见 02 第 1.9 节）
    /// 本身只有查询方法，没有登记/更新方法——"对象如何进入索引"是引擎适配层实现的内部细节
    /// （见 <c>Adapters.Stub.StubSpatialQuery</c> 顶部注释）。<see cref="WorldUnitAccess.SetPosition"/>
    /// 写入单位新位置后，若空间索引存在，需要同步更新该索引，否则后续 <c>ISpatialQuery</c> 查询会
    /// 读到过期位置。本接口是这一"更新"能力的最小契约：任何具体 <c>ISpatialQuery</c> 实现（如
    /// <c>Adapters.Stub.StubSpatialQuery</c>）都可以额外实现或用一个小适配器包装出本接口，供
    /// <see cref="WorldUnitAccess"/> 可选注入（未注入时 <see cref="WorldUnitAccess.SetPosition"/> 只
    /// 写位置，不做任何空间索引同步，调用方需要自行保证一致性）。
    /// </summary>
    public interface ISpatialIndexSync
    {
        /// <summary>登记或更新 <paramref name="id"/> 在空间索引中的位置与半径。</summary>
        void Upsert(Id id, Vec2 position, double radius);

        /// <summary>从空间索引中移除 <paramref name="id"/>。</summary>
        void Remove(Id id);
    }
}
