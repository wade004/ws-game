using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <c>player.skill_bindings</c> 段（10_存档与持久化.md 第 2.2 节 <c>Map&lt;String, Id&gt;</c>）的
    /// <see cref="IPersistable"/> 实现，惯例同 <see cref="UnitPersistable"/>：静态工厂给一个只针对
    /// 指定 <see cref="PlayerUnit"/> 的实例，调用方（<c>GameplayAssembly.RegisterPersistables</c>）
    /// 自行向 <see cref="ISaveSystem"/> 注册。
    /// <para>
    /// 判断记录（G1 遗留恢复：<see cref="Load"/> 改回经 <see cref="ISkillBindingHost.Bind"/> 的
    /// "已知技能"校验）：此前本类 <see cref="Load"/> 绕开 <see cref="ISkillBindingHost.Bind"/>、直接
    /// 整体覆盖绑定表，原因是 <c>player.known_skills</c> 段当时没有 <see cref="IPersistable"/>
    /// 实现，读档时刻单位的"已知技能"集合总是空，经 <c>Bind</c> 校验会让读档整批丢弃全部绑定。现在
    /// <c>Core.Rules.Skill.KnownSkillsPersistable</c> 已补齐该段实现，且在
    /// <see cref="SaveSections.KnownOrder"/> 里排在本段（<c>player.skill_bindings</c>，同属步骤 5）
    /// 之前——<see cref="ISaveSystem"/> 按该顺序依次调用两段的 <c>Load</c>，本段 <c>Load</c> 执行时
    /// 已知技能集合已经还原完毕，绕开校验的前提已经消除，本类因此改回逐条调用
    /// <see cref="ISkillBindingHost.Bind"/>（存档里技能已被后续版本移除等极端情况下，对应槽位会被
    /// <c>Bind</c> 静默拒绝、不写入——同 <c>Bind</c> 本身"未知技能返回 false，不改变任何状态"的一贯
    /// 语义，不抛异常中断整份存档的读取）。
    /// </para>
    /// </summary>
    public static class SkillBindingPersistable
    {
        public static IPersistable For(SkillBindingHost host, PlayerUnit player) => new Impl(host, player);

        private sealed class Impl : IPersistable
        {
            private readonly SkillBindingHost _host;
            private readonly PlayerUnit _player;

            public Impl(SkillBindingHost host, PlayerUnit player)
            {
                _host = host ?? throw new ArgumentNullException(nameof(host));
                _player = player ?? throw new ArgumentNullException(nameof(player));
            }

            public string SectionKey => SaveSections.PlayerSkillBindings;

            public JsonValue Save()
            {
                var bindings = _host.GetBindings(_player.EntityId);
                var builder = new JsonObjectBuilder();
                foreach (var kv in bindings)
                {
                    builder.Add(kv.Key, new JsonString(kv.Value.Value));
                }

                return builder.Build();
            }

            public void Load(JsonValue data)
            {
                // AUD-02 根治（architecture/落地计划/audit-85f1f4f-20260908，P2）：先把该单位当前
                // 全部绑定解绑（惯例同 ItemPersistable.EquipmentPersistable.Load 判断记录"先清空、
                // 再按快照重建"），保证空快照/JsonNull（本段在存档里整体缺失）都能正确让绑定表归空，
                // 而不是保留读档前的运行期绑定——否则同一宿主先后读两个存档槽，缺本段的旧档不会
                // 清掉前一个槽留下的绑定。
                foreach (var kv in new List<KeyValuePair<string, Id>>(_host.GetBindings(_player.EntityId)))
                {
                    _host.Unbind(_player.EntityId, kv.Key);
                }

                if (data is JsonNull)
                {
                    return;
                }

                if (!(data is JsonObject obj))
                {
                    throw new FormatException(
                        $"player.skill_bindings 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
                }

                foreach (var kv in obj)
                {
                    if (!(kv.Value is JsonString text) || !Id.TryParse(text.Value, out var skillId))
                    {
                        throw new FormatException(
                            $"player.skill_bindings.{kv.Key} 不是合法的 Id 字符串");
                    }

                    _host.Bind(_player.EntityId, kv.Key, skillId);
                }
            }
        }
    }
}
