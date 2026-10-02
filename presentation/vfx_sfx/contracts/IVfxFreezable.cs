using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 特效播放器的可选能力：按"宿主单位"暂停/恢复其名下的特效实例（手感落地 M3-C，手感设计/07 第 5 节：局部顿帧期间，
    /// 属于被冻结单位的粒子/特效随之暂停、顿帧结束恢复，其它单位的不受影响）。
    /// <para>
    /// 判断记录（独立成可选能力接口，不给 <see cref="IVfxPlayer"/> 加成员）：同 <c>IPresentationFreezable</c> 对
    /// <c>ICharacterRig</c> 的做法——<see cref="IVfxPlayer"/> 是必需接口，消费方自写实现不必为一个可选能力改代码；探测写法
    /// <c>vfx is IVfxFreezable</c>，探测不到视为"该播放器不响应顿帧"，不抛异常。框架自带 <see cref="Presentation.VfxSfx.Core.VfxPlayer"/> 实现本接口。
    /// </para>
    /// <para>
    /// 判断记录（"属于某单位"的判据）：特效以 <c>anchor</c>/<c>socket</c> 挂接到某实体（<see cref="VfxAttach.EntityId"/> 有值）才有宿主单位；
    /// <c>world</c>/<c>screen</c> 挂接没有，永不随顿帧暂停。因此命中闪光这类以接触点/世界坐标播放的一次性特效天然不冻、
    /// 挂在单位身上的持续特效（拖尾、光环、灼烧）才随单位冻结——是否冻结由特效怎么挂接决定，不新增特效表字段；
    /// 是否启用整层粒子冻结由反馈包 <c>freeze_layers.particles</c> 决定（装配根只在其为真时才调用本接口）。
    /// </para>
    /// <para>
    /// 判断记录（状态型，不是一次性快照）：冻结标记挂在宿主单位上，冻结期间新播放的该单位特效（含资源冷加载补发）同样从暂停状态起播，
    /// 与热路径同一出口；同一单位重复冻结/解冻幂等，不计数。
    /// </para>
    /// </summary>
    public interface IVfxFreezable
    {
        /// <summary>
        /// 暂停（<paramref name="frozen"/> 为真）或恢复宿主单位 <paramref name="ownerEntityId"/> 名下全部特效实例；
        /// 暂停期间这些实例的 <c>lifetime</c> 倒计时也一并停住，恢复后从暂停点继续。幂等。
        /// </summary>
        void SetOwnerFrozen(Id ownerEntityId, bool frozen);

        /// <summary>宿主单位当前是否处于特效冻结中。</summary>
        bool IsOwnerFrozen(Id ownerEntityId);
    }
}
