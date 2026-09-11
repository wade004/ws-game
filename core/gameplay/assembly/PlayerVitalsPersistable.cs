using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Core.Numbers.PowerSet;
using Core.Rules.Combat;
using Core.Rules.Common;

namespace Core.Gameplay.Assembly
{
    /// <summary>
    /// <c>player.vitals</c> 段（外部审核阻塞项 2 收口，见 <c>architecture/落地计划/audit-20260907/
    /// followup-2026-09-07.md</c>"外部审核阻塞项处理"一节）：持久化玩家的存活状态
    /// （<see cref="Unit.Alive"/>）、进出战斗运行态（<see cref="PowerHost.IsInCombat"/>）与该单位
    /// 当前已注册的<b>全部</b>资源池当前值（<see cref="PowerHost.GetRegisteredPowerTypes"/>，不再
    /// 只覆盖 <see cref="WellKnownPowers.Health"/>，见下方 CORE-111-01 判断记录）。
    /// <para>
    /// 判断记录（10 §2.5"哪些不存"未覆盖本段字段，属实现期补录的框架缺口）：10 第 2.5 节只列举
    /// StatBlock 聚合结果（属性上限一类"能从基础来源重新聚合得到"的派生值）、光环运行时实例、战斗
    /// 中间状态三类"默认不存"的数据，均不适用于本段——资源池当前值不是"能从基础来源重算"的派生
    /// 结果（<c>PowerHost.RegisterUnit</c> 只在首次注册时按 <c>start_full</c> 初始化，之后完全由
    /// <c>ModifyPower</c> 增量改写，没有"重新聚合"的公式可用）；<see cref="Unit.Alive"/> 同样是
    /// 运行期状态而非派生值。此前 <c>GameplayAssembly.RegisterPersistables</c> 完全未提及
    /// <c>IPowerHost</c>/<c>Unit.Alive</c>，是外部审核"死亡回档流程不完整"的根因之一：
    /// <c>DeathPolicyHost</c>（<c>reload_save</c> 策略）读取的是"最近一次自动存档"（存档点/任务
    /// 完成时写入，死亡时不再写，见 <c>GameplayAssembly.RequestAutosave</c>），该时刻玩家理应存活，
    /// 但 <see cref="Unit.Alive"/>/资源池当前值只活在运行期 <c>Unit</c>/<c>PowerHost</c> 对象里，
    /// <c>SaveSystem.Load</c> 若不经本段显式覆盖，死亡（<c>Alive=false</c>、生命值 &lt;=0）后
    /// 读档会让这两类字段原样保留"死亡"状态，与刚被其它段覆盖的地图/位置/库存互相矛盾。
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
    /// 判断记录（资源池当前值/进出战斗状态仍经 <see cref="PowerHost"/>，不搬到 <see cref="PlayerUnit"/>
    /// 对象字段）：资源池状态的唯一真相存放在 <see cref="PowerHost"/> 自己的内部登记表（按
    /// <see cref="IPowerHost.RegisterUnit"/> 注册的 unitId 索引），不是 <see cref="Unit"/> 类的字段
    /// （对照 05 对象模型：<c>Unit</c> 不持有资源池状态本身）；该登记表的生命周期与
    /// <see cref="IWorldSim"/> 无关（<c>Core.Rules.Assembly.RulesAssembly.RegisterUnit</c> 在
    /// "新游戏"/程序启动早期即调用，见 <c>FrameworkResidentHost</c> 构造函数判断记录"这两个注册只
    /// 影响 Rules/Economy 各自的内部登记表，不受 WorldSim.ClearAll 影响"），因此 <see
    /// cref="IPowerHost.HasPower"/>/<see cref="IPowerHost.GetPower"/> 在玩家尚未 <c>World.AddEntity</c>
    /// 时依然可以安全调用，不会重现 <c>Alive</c> 字段那个问题。
    /// </para>
    /// <para>
    /// 判断记录（CORE-111-01 根治，architecture/落地计划/audit-6739f50-20260909，P2，主审已确认，
    /// 取代下方两条已废止的旧判断记录"只处理 Health 一种资源"/"构造函数持有 <see cref="IPowerHost"/>
    /// 接口"）：真实探针复现——A 存档运行期 Mana=30（<c>start_full=false</c>，脱战回复 10）且
    /// <c>InCombat=true</c>，读取不同 power 集合的 B 存档在后段失败后触发正向顺序回滚：
    /// <c>player.race_id</c>/<c>player.archetype</c> 段的回滚重放 <c>RulesAssembly.
    /// ReloadArchetypeAndRace</c>（见该方法类型判断记录"随后 player.vitals 段会用存档里的真实当前值
    /// 覆盖这份初始值"——本段正是那份"最终解释权"），对资源类型集合确实变化的单位整体
    /// <c>UnregisterUnit</c>+<c>RegisterUnit</c>，重建出的全新 <c>PowerHost</c> 内部状态把 Mana 按
    /// <c>start_full</c> 规则初始化为 0、把 <c>InCombat</c> 重置为 false——这两个字段此前都不是本段
    /// 自己的字段（本段 <c>Save()</c> 修复前只序列化 <c>alive</c>/<c>health</c>），<c>player.vitals</c>
    /// 段自己的回滚重放因此没有任何数据可以把它们纠正回来，`Advance(1)` 随即按脱战回复速率把 Mana
    /// 从 0 推到 10，永久性丢失了失败读档前的运行时快照。修复：(a) 本段泛化为持久化该单位"读档前
    /// 快照那一刻"已注册的全部资源类型当前值（<c>powers</c> 字段，键为资源类型 id）与进出战斗状态
    /// （<c>in_combat</c> 字段），而不再只挑 <see cref="WellKnownPowers.Health"/> 一种；(b)
    /// 构造函数参数类型从 <see cref="IPowerHost"/> 收窄为具体 <see cref="PowerHost"/>——枚举"某单位
    /// 已注册的全部资源类型"与查询"当前进出战斗状态"都不在 <see cref="IPowerHost"/> 契约里（该契约
    /// 从未提供这两个读方法，见 <see cref="PowerHost.GetRegisteredPowerTypes"/>/<see
    /// cref="PowerHost.IsInCombat"/> 判断记录"为什么新增到具体类型、不进契约"），本类型唯一的生产
    /// 注入点（<c>GameplayAssembly.RegisterPersistables</c>）传入的实参本就是 <c>RulesAssembly.
    /// Powers</c>——该字段对外公开的类型本就是具体 <see cref="PowerHost"/>，不是接口，收窄参数类型
    /// 不改变任何真实装配路径，也不需要新增/修改 <see cref="IPowerHost"/> 契约、不波及本仓库其它
    /// 独立实现该接口的测试假类型。旧存档只有 <c>health</c>、没有 <c>powers</c>/<c>in_combat</c>
    /// 两个新字段时按向后兼容规则处理（见 <see cref="Load"/> 判断记录），不破坏旧档可读性。
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
        private static readonly Id ReloadSource = new Id("system.reload_save");

        private const string AliveKey = "alive";
        private const string HealthKey = "health";
        private const string InCombatKey = "in_combat";
        private const string PowersKey = "powers";

        private readonly PlayerUnit _player;
        private readonly PowerHost _powers;
        private readonly CombatHost? _combat;

        public PlayerVitalsPersistable(PlayerUnit player, PowerHost powers)
            : this(player, powers, combat: null)
        {
        }

        /// <summary>
        /// C11-RELOAD 根治新增重载（architecture/落地计划/消费方反馈-2026-09-11-读档空间索引与复活
        /// 生命周期.md 第 2 项）：额外注入 <see cref="CombatHost"/>——<see cref="Load"/> 恢复
        /// <c>in_combat</c> 字段时改经 <see cref="CombatHost.RestoreCombatState"/>（唯一来源，见该
        /// 方法判断记录），不再直接调用 <see cref="IPowerHost.SetInCombat"/>：直接调用只会改到
        /// <see cref="PowerHost"/> 自己的内部状态，<see cref="CombatHost"/> 自身的进战登记表对此一无
        /// 所知，读档后 <see cref="CombatHost.IsInCombat"/> 与 <see cref="IPowerHost.IsInCombat"/>
        /// 会出现分歧（真实探针复现，见消费方反馈第 2 项）。
        /// <para>
        /// <paramref name="combat"/> 为 <c>null</c>（本类型保留的旧两参构造函数路径，源码兼容）时
        /// <see cref="Load"/> 退回旧行为——只同步 <see cref="PowerHost"/>，不修复上述分歧；生产装配
        /// （<c>GameplayAssembly.RegisterPersistables</c>）已经改用本重载并传入真实
        /// <c>Core.Rules.Assembly.RulesAssembly.Combat</c>，不受影响。
        /// </para>
        /// </summary>
        public PlayerVitalsPersistable(PlayerUnit player, PowerHost powers, CombatHost? combat)
        {
            _player = player ?? throw new ArgumentNullException(nameof(player));
            _powers = powers ?? throw new ArgumentNullException(nameof(powers));
            _combat = combat;
        }

        /// <summary>
        /// 判断记录（源码兼容重载，第十五方深度审核 CORE-111-01 补录，
        /// architecture/落地计划/audit-6739f50-20260909）：CORE-111-01 把上面这个构造函数的
        /// 第二参数类型从 <see cref="IPowerHost"/> 收窄为具体 <see cref="PowerHost"/>（枚举"某单位
        /// 已注册的全部资源类型"与查询"当前进出战斗状态"不在 <see cref="IPowerHost"/> 契约里）。
        /// 本类型是 <c>public sealed</c>，构造函数公开，不能排除本仓库之外的消费方（其它游戏仓库、
        /// 未来新增的装配点）曾直接以旧签名 <c>new PlayerVitalsPersistable(player, someIPowerHost)</c>
        /// 构造——为保持源码兼容，保留这个旧签名重载，仅做类型收窄转发：若传入的
        /// <paramref name="powers"/> 实际不是 <see cref="PowerHost"/>（<see cref="IPowerHost"/> 目前
        /// 唯一的生产实现就是 <see cref="PowerHost"/>；其它实现只出现在测试假类型里），转发前先给出
        /// 明确异常而不是让后续调用 <see cref="PowerHost.GetRegisteredPowerTypes"/>/
        /// <see cref="PowerHost.IsInCombat"/> 时产生更难懂的 <see cref="InvalidCastException"/>。
        /// 标记 <see cref="ObsoleteAttribute"/>（非报错级）引导新代码改用上面的具体类型重载，不强制
        /// 迁移；真实生产装配点 <c>GameplayAssembly.RegisterPersistables</c> 已经改用具体类型重载，
        /// 不受影响。
        /// </summary>
        [Obsolete("请改用 PlayerVitalsPersistable(PlayerUnit, PowerHost) 重载；本重载仅为源码兼容保留，" +
            "传入的 IPowerHost 必须实际是 PowerHost 实例。")]
        public PlayerVitalsPersistable(PlayerUnit player, IPowerHost powers)
            : this(player, CastToPowerHost(powers))
        {
        }

        private static PowerHost CastToPowerHost(IPowerHost powers)
        {
            if (powers is PowerHost concrete)
            {
                return concrete;
            }

            throw new ArgumentException(
                $"{nameof(PlayerVitalsPersistable)} 的兼容构造函数要求 {nameof(powers)} 实际是 " +
                $"{nameof(PowerHost)} 实例（当前实际类型：{powers?.GetType().FullName ?? "null"}）——" +
                $"{nameof(IPowerHost)} 契约本身不提供 CORE-111-01 需要的 " +
                $"{nameof(PowerHost.GetRegisteredPowerTypes)}/{nameof(PowerHost.IsInCombat)} 两个方法。",
                nameof(powers));
        }

        public string SectionKey => "player.vitals";

        public JsonValue Save()
        {
            var builder = new JsonObjectBuilder().Add(AliveKey, JsonBool.Of(_player.Alive));

            if (_powers.HasPower(_player.EntityId, WellKnownPowers.Health))
            {
                // 兼容字段：与 CORE-111-01 之前的存档格式保持字节级一致的 health 键（同一份值也会
                // 出现在下面的 powers 里），供任何仍直接读取旧字段名的外部工具/存档查看器过渡使用；
                // 本段自己的 Load 只以 powers 为准（见该方法判断记录）。
                builder.Add(HealthKey, new JsonNumber(_powers.GetPower(_player.EntityId, WellKnownPowers.Health)));
            }

            if (_powers.IsRegistered(_player.EntityId))
            {
                builder.Add(InCombatKey, JsonBool.Of(_powers.IsInCombat(_player.EntityId)));

                var powersBuilder = new JsonObjectBuilder();
                foreach (var powerType in _powers.GetRegisteredPowerTypes(_player.EntityId))
                {
                    powersBuilder.Add(powerType.Value, new JsonNumber(_powers.GetPower(_player.EntityId, powerType)));
                }

                builder.Add(PowersKey, powersBuilder.Build());
            }

            return builder.Build();
        }

        public void Load(JsonValue data)
        {
            // AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：本段整体缺失
            // （data is JsonNull）时必须重置到"从未发生过"的默认态——存活、满血——而不是 no-op
            // 保留读档前的运行期状态（例如刚死亡还没重新存档、这份旧档又恰好没有本段）。默认态
            // 选"存活 + 满血"而非"清零"，理由同 <see cref="Save"/> 上方类型注释判断记录"新游戏
            // 的玩家本就存活"——这正是本段从未被写入过时（新游戏首次 Save 之前）的语义。其它资源
            // 池（非 Health）不在本分支处理：它们已经由 PowerHost.RegisterUnit 按各自 start_full/
            // 默认规则初始化过，本段整体缺失时没有更具体的信息去覆盖它们，维持各自默认值即是
            // CORE-111-01 判断记录"旧档只有 health 时其它池按各自 start_full/默认规则初始化"这条
            // 结论的自然结果。
            if (data is JsonNull)
            {
                _player.Alive = true;
                if (_powers.HasPower(_player.EntityId, WellKnownPowers.Health))
                {
                    var current = _powers.GetPower(_player.EntityId, WellKnownPowers.Health);
                    var max = _powers.GetPowerMax(_player.EntityId, WellKnownPowers.Health);
                    _powers.ModifyPower(_player.EntityId, WellKnownPowers.Health, max - current, ReloadSource);
                }

                // C11-RELOAD 根治：段整体缺失同样按"从未发生过"归零进出战斗状态（新游戏/本段从未
                // 写入过的玩家本就不在战），见下方 RestoreInCombat 判断记录——与"有段但缺
                // in_combat 字段"（旧格式存档）走同一条默认 false 路径，保持 CombatHost/PowerHost
                // 恒一致（消费方反馈第 2 项 c 条断言）。
                if (_powers.IsRegistered(_player.EntityId))
                {
                    RestoreInCombat(false);
                }

                return;
            }

            if (!(data is JsonObject obj))
            {
                throw new FormatException($"{SectionKey} 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
            }

            if (obj.TryGetValue(AliveKey, out var aliveRaw) && aliveRaw is JsonBool aliveBool)
            {
                _player.Alive = aliveBool.Value;
            }

            // CORE-111-01 根治：优先使用泛化后的 powers 字段（覆盖该单位当前已注册的全部资源池）；
            // 只有旧格式存档（没有 powers 字段，只有单独的 health 字段）才退回只恢复 Health 的旧
            // 路径——两个分支互斥，不会重复对同一个资源类型调用两次 ModifyPower。对 powers 里列出、
            // 但该单位当前未持有（如职业已切换、该资源类型已被移除）的条目静默跳过：这正是"该池
            // 不存在于当前配置"时不做任何事的既有语义，不是需要显式处理的错误。
            var restoredViaPowersMap = false;
            if (obj.TryGetValue(PowersKey, out var powersRaw) && powersRaw is JsonObject powersObj)
            {
                restoredViaPowersMap = true;
                foreach (var entry in powersObj)
                {
                    if (!(entry.Value is JsonNumber powerNumber) || !Id.TryParse(entry.Key, out var powerType))
                    {
                        continue;
                    }

                    if (!_powers.HasPower(_player.EntityId, powerType))
                    {
                        continue;
                    }

                    var current = _powers.GetPower(_player.EntityId, powerType);
                    _powers.ModifyPower(_player.EntityId, powerType, powerNumber.Value - current, ReloadSource);
                }
            }

            if (!restoredViaPowersMap &&
                obj.TryGetValue(HealthKey, out var healthRaw) && healthRaw is JsonNumber healthNumber &&
                _powers.HasPower(_player.EntityId, WellKnownPowers.Health))
            {
                var current = _powers.GetPower(_player.EntityId, WellKnownPowers.Health);
                _powers.ModifyPower(_player.EntityId, WellKnownPowers.Health, healthNumber.Value - current, ReloadSource);
            }

            // C11-RELOAD 根治：不再只在 in_combat 键存在时才写——旧格式存档（没有 in_combat 字段）
            // 同样要显式归零（见 RestoreInCombat 判断记录），否则 CombatHost（经
            // GameplayAssembly.DerivedStateRebuilder.BeforeLoad 已经清空为 false，见该方法判断
            // 记录）与 PowerHost（本段缺键时此前保持读档前的旧值不动）在读档后会分歧。
            if (_powers.IsRegistered(_player.EntityId))
            {
                var inCombat = obj.TryGetValue(InCombatKey, out var inCombatRaw) &&
                    inCombatRaw is JsonBool inCombatBool && inCombatBool.Value;
                RestoreInCombat(inCombat);
            }
        }

        /// <summary>
        /// C11-RELOAD 根治新增：把 <paramref name="inCombat"/> 写回"进出战斗状态的唯一来源"——已注入
        /// <see cref="CombatHost"/>（生产装配路径）时经 <see cref="CombatHost.RestoreCombatState"/>
        /// 写入（同时同步 <see cref="PowerHost"/>，见该方法判断记录）；未注入（旧两参构造函数，源码
        /// 兼容路径）时退回直接调用 <see cref="IPowerHost.SetInCombat"/> 的旧行为，见构造函数判断
        /// 记录。
        /// </summary>
        private void RestoreInCombat(bool inCombat)
        {
            if (_combat != null)
            {
                _combat.RestoreCombatState(_player.EntityId, inCombat);
            }
            else
            {
                _powers.SetInCombat(_player.EntityId, inCombat);
            }
        }
    }
}
