using System;
using Core.Carriers.Unit;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Core.Numbers.PowerSet;
using Core.Rules.Common;

namespace Core.Gameplay.Assembly
{
    /// <summary>
    /// <c>player.vitals</c> 段（外部审核阻塞项 2 收口，见 <c>architecture/落地计划/audit-20260907/
    /// followup-2026-09-07.md</c>"外部审核阻塞项处理"一节）：持久化玩家的存活状态
    /// （<see cref="Unit.Alive"/>）与 <see cref="WellKnownPowers.Health"/> 当前值。
    /// <para>
    /// 判断记录（10 §2.5"哪些不存"未覆盖本段字段，属实现期补录的框架缺口）：10 第 2.5 节只列举
    /// StatBlock 聚合结果（属性上限一类"能从基础来源重新聚合得到"的派生值）、光环运行时实例、战斗
    /// 中间状态三类"默认不存"的数据，均不适用于本段——<see cref="WellKnownPowers.Health"/> 当前值
    /// 是运行期唯一真相的资源池余量，不是"能从基础来源重算"的派生结果（<c>PowerHost.RegisterUnit</c>
    /// 只在首次注册时按 <c>start_full</c> 初始化，之后完全由 <c>ModifyPower</c> 增量改写，没有"重新
    /// 聚合"的公式可用）；<see cref="Unit.Alive"/> 同样是运行期状态而非派生值。此前
    /// <c>GameplayAssembly.RegisterPersistables</c> 完全未提及 <c>IPowerHost</c>/<c>Unit.Alive</c>，
    /// 是外部审核"死亡回档流程不完整"的根因之一：<c>DeathPolicyHost</c>（<c>reload_save</c> 策略）
    /// 读取的是"最近一次自动存档"（存档点/任务完成时写入，死亡时不再写，见
    /// <c>GameplayAssembly.RequestAutosave</c>），该时刻玩家理应存活，但 <see cref="Unit.Alive"/>/
    /// <see cref="WellKnownPowers.Health"/> 只活在运行期 <c>Unit</c>/<c>PowerHost</c> 对象里，
    /// <c>SaveSystem.Load</c> 若不经本段显式覆盖，死亡（<c>Alive=false</c>、<c>Health&lt;=0</c>）后
    /// 读档会让这两个字段原样保留"死亡"状态，与刚被其它段覆盖的地图/位置/库存互相矛盾。
    /// </para>
    /// <para>
    /// 判断记录（<c>Alive</c> 直接读写 <see cref="PlayerUnit"/> 对象字段，不经 <c>IUnitAccess</c>——
    /// 根治修复，替换本类型早期实现）：早期实现用 <c>IUnitAccess.IsAlive</c>/<c>SetAlive</c>，
    /// 而 <c>WorldUnitAccess</c>（唯一生产实现）内部经 <c>IWorldSim.GetEntity</c> 找不到该单位时
    /// 直接抛 <c>InvalidOperationException</c>（"单位不存在"）——PlayMode 实测复现："新游戏"流程
    /// （<c>Presentation.Shell.ShellHost.NewGame</c>）在玩家实体真正 <c>World.AddEntity</c>（由
    /// <c>post_load</c> 钩子在场景切换完成后才做，见 <c>FrameworkResidentHost.HandlePostLoad</c>
    /// 判断记录"这样无论是'新游戏'还是'读档'都走同一条 <c>HandlePostLoad</c> 负责挂载的路径"）之前
    /// 就已经调用 <c>ISaveSystem.Save</c>（<c>NewGame</c> 顺序：难度应用 → <c>NewGameStarter</c> →
    /// <c>Save</c> → <c>LoadScene</c>），此时本段 <c>Save()</c> 经 <c>IUnitAccess.IsAlive</c> 必然
    /// 撞上"单位不存在"异常，被 <c>ISaveSystem.Save</c> 捕获为失败，导致 <c>NewGame</c> 在场景切换
    /// 之前就直接返回 false——同 <c>UnitPersistable.CurrentMapIdPersistable</c>/
    /// <c>CurrentPositionPersistable</c>（10 第 2.3 节 <c>world.current_map_id</c>/
    /// <c>current_position</c> 两段）的既有惯例改为直接持有 <see cref="PlayerUnit"/> 对象引用、
    /// 读写其 <see cref="Unit.Alive"/> 字段本身——该字段是长期存活的 C# 对象字段，与是否已经
    /// <c>World.AddEntity</c> 无关，"新游戏"最初调用 <c>Save()</c> 时读到的是构造期默认值
    /// （<c>Alive = true</c>），语义上也完全正确（新游戏的玩家本就存活）。
    /// </para>
    /// <para>
    /// 判断记录（<c>Health</c> 仍经 <see cref="IPowerHost"/>，不搬到 <see cref="PlayerUnit"/> 对象
    /// 字段）：资源池当前值的唯一真相存放在 <see cref="IPowerHost"/> 自己的内部登记表（按
    /// <see cref="IPowerHost.RegisterUnit"/> 注册的 unitId 索引），不是 <see cref="Unit"/> 类的字段
    /// （对照 05 对象模型：<c>Unit</c> 不持有资源池状态本身）；该登记表的生命周期与
    /// <see cref="IWorldSim"/> 无关（<c>Core.Rules.Assembly.RulesAssembly.RegisterUnit</c> 在
    /// "新游戏"/程序启动早期即调用，见 <c>FrameworkResidentHost</c> 构造函数判断记录"这两个注册只
    /// 影响 Rules/Economy 各自的内部登记表，不受 WorldSim.ClearAll 影响"），因此
    /// <see cref="IPowerHost.HasPower"/>/<see cref="IPowerHost.GetPower"/> 在玩家尚未
    /// <c>World.AddEntity</c> 时依然可以安全调用，不会重现 <c>Alive</c> 字段那个问题。
    /// </para>
    /// <para>
    /// 判断记录（只处理 <see cref="WellKnownPowers.Health"/> 一种资源）：呼应
    /// <c>DeathPolicyOptions.RespawnHealthFraction</c>/06 第 4.6 节死亡判定同样只绑定生命值这一种
    /// 资源——法力/怒气等其它资源池不参与"是否存活"的判定，不属于本次审核阻塞项范围，具体游戏若
    /// 需要它们跨读档保留，应自行按同样手法扩展一个独立段（<see cref="IPowerHost"/> 本身不提供
    /// "枚举某单位已注册的全部资源类型"的查询，见该接口——本类型不试图做成通用的"全部资源池"持久化）。
    /// </para>
    /// <para>
    /// 判断记录（读档时用 <c>ModifyPower(delta)</c> 而非"直接赋值"）：<see cref="IPowerHost"/> 契约
    /// 本就没有"设置绝对值"的方法（06 原文只给出 <c>getPower</c>/<c>getPowerMax</c>/<c>modifyPower</c>
    /// 三个方法，见该接口类型注释），本类型按既有契约用"存档值 - 当前值"的增量调用
    /// <see cref="IPowerHost.ModifyPower"/>，不新增契约方法（不新增原语）。
    /// </para>
    /// </summary>
    public sealed class PlayerVitalsPersistable : IPersistable
    {
        private static readonly Core.Foundation.Common.Id ReloadSource = new Core.Foundation.Common.Id("system.reload_save");

        private readonly PlayerUnit _player;
        private readonly IPowerHost _powers;

        public PlayerVitalsPersistable(PlayerUnit player, IPowerHost powers)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
        }

        public string SectionKey => "player.vitals";

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder().Add("alive", JsonBool.Of(_player.Alive));

            if (_powers.HasPower(_player.EntityId, WellKnownPowers.Health))
            {
                builder.Add("health", new JsonNumber(_powers.GetPower(_player.EntityId, WellKnownPowers.Health)));
            }

            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            // AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：本段整体缺失
            // （data is JsonNull）时必须重置到"从未发生过"的默认态——存活、满血——而不是 no-op
            // 保留读档前的运行期状态（例如刚死亡还没重新存档、这份旧档又恰好没有本段）。默认态
            // 选"存活 + 满血"而非"清零"，理由同 <see cref="Save"/> 上方类型注释判断记录"新游戏
            // 的玩家本就存活"——这正是本段从未被写入过时（新游戏首次 Save 之前）的语义。
            if (data is JsonNull)
            {
                _player.Alive = true;
                if (_powers.HasPower(_player.EntityId, WellKnownPowers.Health))
                {
                    var current = _powers.GetPower(_player.EntityId, WellKnownPowers.Health);
                    var max = _powers.GetPowerMax(_player.EntityId, WellKnownPowers.Health);
                    _powers.ModifyPower(_player.EntityId, WellKnownPowers.Health, max - current, ReloadSource);
                }

                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException($"{SectionKey} 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            if (obj.TryGetValue("alive", out var aliveRaw) && aliveRaw is JsonBool aliveBool)
            {
                _player.Alive = aliveBool.Value;
            }

            if (obj.TryGetValue("health", out var healthRaw) && healthRaw is JsonNumber healthNumber &&
                _powers.HasPower(_player.EntityId, WellKnownPowers.Health))
            {
                var current = _powers.GetPower(_player.EntityId, WellKnownPowers.Health);
                _powers.ModifyPower(_player.EntityId, WellKnownPowers.Health, healthNumber.Value - current, ReloadSource);
            }
        }
    }
}
