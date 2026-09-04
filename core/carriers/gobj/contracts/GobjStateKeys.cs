using Core.Foundation.Common;

namespace Core.Carriers.Gobj
{
    /// <summary>
    /// <c>GameObject.state</c> 可变字段落地为 <c>WorldState</c> 标志时使用的 key 约定（见 07 第 3.4
    /// 节"flagKey 按约定拼为 <c>world.gobj.&lt;实例id&gt;.&lt;字段名&gt;</c>"）。
    /// <para>
    /// 判断记录：实例 id 形如 <c>gobj.inst_&lt;n&gt;</c>（见 <c>IWorldSim.AllocateEntityId("gobj")</c>
    /// 产出的确定性 id 格式），本身已经以 <c>"gobj."</c> 开头，07 原文 <c>world.gobj.&lt;实例id&gt;.&lt;字段名&gt;</c>
    /// 里的 <c>gobj.</c> 段与实例 id 的 <c>gobj.</c> 前缀重合——按任务书给出的具体例子
    /// （<c>gobj.inst_1</c> → <c>world.gobj.inst_1.open_state</c>）反推，key 的构造公式其实就是
    /// "<c>world.</c> + 完整实例 id + <c>.</c> + 字段名"，不需要再对实例 id 做"去掉 gobj 前缀"之类
    /// 的额外处理——两种理解在这个具体例子下算出的结果相同，本类型按"直接拼接完整实例 id"实现，
    /// 更简单且不依赖实例 id 一定以 <c>"gobj."</c> 开头这一假设（手工放置对象的实例 id 命名不受
    /// <c>AllocateEntityId</c> 约束时同样适用）。
    /// </para>
    /// </summary>
    public static class GobjStateKeys
    {
        /// <summary>拼出某个 <paramref name="instanceId"/> 的 <paramref name="field"/> 字段对应的
        /// <c>WorldState</c> flagKey：<c>"world.{instanceId}.{field}"</c>。</summary>
        public static Id For(Id instanceId, string field) => new Id($"world.{instanceId.Value}.{field}");
    }
}
