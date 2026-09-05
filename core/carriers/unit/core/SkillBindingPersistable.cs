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
    /// 判断记录（<see cref="Load"/> 不经 <see cref="ISkillBindingHost.Bind"/> 的"已知技能"校验）：
    /// 10 第 3 节汇总顺序把 <c>player.known_skills</c> 排在 <c>player.skill_bindings</c> 之前（同属
    /// 步骤 5），但 <c>player.known_skills</c> 目前没有 <see cref="IPersistable"/> 实现（已知缺口，
    /// 不在本任务范围）——若本类 <see cref="Load"/> 改经 <see cref="ISkillBindingHost.Bind"/> 校验，
    /// 现状下会让读档整批丢弃全部绑定（因为读档时刻单位的"已知技能"集合总是空的）。本类改用
    /// <see cref="SkillBindingHost.ReplaceAll"/> 直接整体还原，不重新校验业务不变量——与
    /// <see cref="UnitPersistable"/> 各段 <c>Load</c> 直接赋值、不做业务校验的一贯做法一致；等
    /// <c>player.known_skills</c> 的 <see cref="IPersistable"/> 补上后，二者仍会按声明顺序
    /// （<see cref="SaveSections.KnownOrder"/>）先后加载，不需要改动本类。
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
                if (data is JsonNull)
                {
                    return;
                }

                if (!(data is JsonObject obj))
                {
                    throw new FormatException(
                        $"player.skill_bindings 段的数据不是 JSON 对象（实际种类：{data.Kind}）");
                }

                var bindings = new Dictionary<string, Id>(obj.Count, StringComparer.Ordinal);
                foreach (var kv in obj)
                {
                    if (!(kv.Value is JsonString text) || !Id.TryParse(text.Value, out var skillId))
                    {
                        throw new FormatException(
                            $"player.skill_bindings.{kv.Key} 不是合法的 Id 字符串");
                    }

                    bindings[kv.Key] = skillId;
                }

                _host.ReplaceAll(_player.EntityId, bindings);
            }
        }
    }
}
