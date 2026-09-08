using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Numbers.Progression
{
    /// <summary>
    /// W1 收边补齐（A4 审计 F1：10_存档与持久化.md 第 2.2 节 <c>progression</c> 字段"必填"，此前
    /// <see cref="ProgressionHost"/> 未实现 <see cref="IPersistable"/>，任何游戏存档→读档后玩家
    /// 等级/经验完全不会被保存/恢复，违反 ADR-0009"玩家的一切进度都落在存档"）——<c>player.progression</c>
    /// 段（见 <see cref="SaveSections.PlayerProgression"/>）的 <see cref="IPersistable"/> 实现。
    /// <para>
    /// 静态工厂类，模式同 <c>core/carriers/unit/core/UnitPersistable.cs</c>：<see cref="ProgressionHost"/>
    /// 本身可能同时管理多个单位（NPC 也可经 <see cref="ProgressionHost.RegisterUnit"/> 注册），但
    /// 存档只关心"哪个单位是玩家"——这一决定权在调用方（游戏层引导代码/<c>GameplayAssembly</c>
    /// 知道谁是玩家），本模块自己不假设"唯一一个已注册单位就是玩家"，因此不让
    /// <see cref="ProgressionHost"/> 自己直接实现 <see cref="IPersistable"/>，而是由本类按调用方
    /// 指定的 <c>unitId</c> 生成一个绑定了该单位的 <see cref="IPersistable"/> 实例。
    /// </para>
    /// </summary>
    public static class ProgressionPersistable
    {
        /// <summary><c>player.progression</c> 段（见 10 第 2.2 节），绑定 <paramref name="unitId"/>
        /// （通常是玩家单位 id）。</summary>
        public static IPersistable For(ProgressionHost host, Id unitId) => new Impl(host, unitId);

        private sealed class Impl : IPersistable
        {
            private readonly ProgressionHost _host;
            private readonly Id _unitId;

            public Impl(ProgressionHost host, Id unitId)
            {
                _host = host ?? throw new ArgumentNullException(nameof(host));
                _unitId = unitId;
            }

            public string SectionKey => SaveSections.PlayerProgression;

            public JsonValue Save() => _host.SaveUnit(_unitId);

            public void Load(JsonValue data)
            {
                // AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：本段整体缺失
                // （data is JsonNull）时必须重置到默认态（1 级、0 经验），不能 no-op 保留读档前的
                // 运行期等级/经验——否则同一宿主先后加载两个存档槽，缺本段的旧档不会清掉前一个槽
                // 留下的等级。曲线 id（<c>curve_id</c>）本身不属于"存档数据"，是内容/装配阶段已经
                // 经 <see cref="ProgressionHost.RegisterUnit"/> 确定的职业/单位归属，缺段清空只清
                // 等级/经验这两个真正的存档字段，沿用 <see cref="Save"/> 当前已登记的曲线（复用
                // Save() 本身取回，不新增契约方法）。<paramref name="data"/> 为 JsonNull 但该单位
                // 从未经 RegisterUnit 注册（例如所属职业没有配置 level_curve_ref，见
                // <see cref="Core.Rules.Assembly.RulesAssembly.RegisterUnit"/> 判断记录"level_curve_ref
                // 存在才注册 Progression"）时，本就没有任何"曲线"可沿用，也没有任何状态需要清空
                // ——保持未注册，不抛异常（既有测试 Save_UnregisteredUnit_Throws 已确立"未注册单位
                // 查询/保存会抛"的一贯行为，Load 遇到同样前提时选择"无操作"而不是代为注册一个调用方
                // 从未选择的曲线）。
                if (data is JsonNull)
                {
                    JsonValue currentSnapshot;
                    try
                    {
                        currentSnapshot = _host.SaveUnit(_unitId);
                    }
                    catch (ArgumentException)
                    {
                        return;
                    }

                    var currentCurveId = ((JsonString)((JsonObject)currentSnapshot)["curve_id"]).Value;
                    _host.RestoreState(_unitId, new Id(currentCurveId), 1, 0);
                    return;
                }

                if (!(data is JsonObject obj) ||
                    !obj.TryGetValue("curve_id", out var curveIdValue) || !(curveIdValue is JsonString curveIdStr) ||
                    !obj.TryGetValue("level", out var levelValue) || !(levelValue is JsonNumber levelNum) ||
                    !levelNum.TryGetInt64(out var level) ||
                    !obj.TryGetValue("xp", out var xpValue) || !(xpValue is JsonNumber xpNum) ||
                    !xpNum.TryGetInt64(out var xp))
                {
                    throw new FormatException(
                        "player.progression 段的数据不是 {curve_id, level, xp} 形状的 JSON 对象");
                }

                _host.RestoreState(_unitId, new Id(curveIdStr.Value), (int)level, xp);
            }
        }
    }
}
