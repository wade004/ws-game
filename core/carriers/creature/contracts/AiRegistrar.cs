using Core.Foundation.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// 把新生成的生物实体接入 AI 行为外壳的具名委托（见 00 架构总则"携带参数的回调按各自接口
    /// 语义定义具名委托，不复用裸 Action/Func"惯例）。
    /// <para>
    /// 契约缺口判断记录：<c>Core.Rules.Common.IAiHost</c> 只声明了 <c>Evaluate</c>/
    /// <c>GetBehaviorState</c>/<c>SetRotation</c>，以及供遭遇脚本强制切换行为状态的方法，共四个
    /// （06 第 6.5/7 节），
    /// 真正的 <c>RegisterUnit(unitId, profileId, spawnPoint)</c> 是 <c>core/rules/ai</c> 的
    /// <c>AiHost</c> 类在契约之外追加的"内部驱动版本"方法（见该模块 README），不在
    /// <see cref="Core.Rules.Common.IAiHost"/> 签名内，<c>core/carriers/creature</c>（L3）不能
    /// 直接引用 <c>core/rules/ai</c>（同层模块，见 01 第 3 节"同层模块之间只经契约接口与事件总线
    /// 交互"）。本委托是组装层（持有具体 <c>AiHost</c> 实例）注入 <see cref="Core.Carriers.Creature.CreatureFactory"/>
    /// 的桥接点：组装期把
    /// <c>(unitId, profileId, spawnPoint, rotationId) =&gt; { aiHost.RegisterUnit(unitId, profileId, spawnPoint);
    /// if (rotationId.HasValue) aiHost.SetRotation(unitId, rotationId.Value); }</c>
    /// 适配成本委托签名注入。
    /// </para>
    /// </summary>
    /// <param name="unitId">新生成的生物实体 id。</param>
    /// <param name="profileId">该生物模板的 <c>ai.behavior_profile</c> 引用（<c>creature.template.ai_behavior_ref</c>）。</param>
    /// <param name="spawnPoint">生成点坐标，供行为外壳的 leash/return 判定使用。</param>
    /// <param name="rotationId">非空时把该单位的 <c>ai.rotation</c> 从行为档案默认值切换为该表
    /// （<c>creature.template.ai_rotation_ref</c>）。</param>
    public delegate void AiRegistrar(Id unitId, Id profileId, Vec2 spawnPoint, Id? rotationId);
}
