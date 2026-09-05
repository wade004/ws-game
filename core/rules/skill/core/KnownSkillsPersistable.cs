using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;

namespace Core.Rules.Skill
{
    /// <summary>
    /// G1 遗留恢复：<c>player.known_skills</c> 段（10_存档与持久化.md 第 2.2 节 <c>List&lt;Id&gt;</c>、
    /// 第 3 节步骤 5 与 <c>player.skill_bindings</c> 同组）的 <see cref="IPersistable"/> 实现，取代
    /// <c>SkillBindingPersistable</c> 类型注释此前记录的"已知缺口"。惯例同
    /// <c>Core.Carriers.Unit.SkillBindingPersistable</c>：静态工厂给一个只针对指定单位 id 的实例，
    /// 调用方（<c>GameplayAssembly.RegisterPersistables</c>，L4）自行向 <see cref="ISaveSystem"/>
    /// 注册；本类型放在 <c>core/rules/skill</c>（L2，已知技能集合的宿主 <see cref="SkillHost"/> 所在
    /// 层）而不是 <c>core/carriers/unit</c>（L3），按 01_分层与依赖.md 的依赖方向，本类型只接受
    /// <see cref="Id"/> 形式的单位 id 参数，不引用 L3 的 <c>PlayerUnit</c> 类型。
    /// <para>
    /// 判断记录（<c>Load</c> 直接 <see cref="SkillHost.LearnSkill"/> 逐个还原，不先清空）：读档发生在
    /// 一局全新构造的 <c>Core.Gameplay.Assembly.GameplayAssembly</c> 上（同
    /// <c>SkillBindingPersistable</c> 类型注释判断记录"读档时刻单位的'已知技能'集合总是空的"），
    /// <see cref="SkillHost.LearnSkill"/> 本身按 <see cref="HashSet{T}"/> 语义幂等（重复学习同一技能
    /// 无副作用），不需要额外的"清空已知技能"原语。
    /// </para>
    /// <para>
    /// 判断记录（<c>SkillBindingPersistable.Load</c> 改回经已知技能校验的前提）：本类型在
    /// <see cref="SaveSections.KnownOrder"/> 里排在 <c>player.skill_bindings</c> 之前（同为步骤 5，
    /// 数组序在先），<see cref="ISaveSystem"/> 按该顺序依次调用各段 <c>Load</c>（10 第 3、5 节），
    /// 故 <c>player.skill_bindings.Load</c> 执行时本类型已经把已知技能集合还原完毕——
    /// <c>SkillBindingPersistable.Load</c> 此前因为"补齐前二者顺序调换会整批丢弃绑定"而绕开
    /// <c>Core.Carriers.Unit.ISkillBindingHost.Bind</c> 的已知技能校验、直接调用
    /// <c>Core.Carriers.Unit.SkillBindingHost.ReplaceAll</c>；本类型落地后前提已经消除，见该类型的
    /// 对应改动。
    /// </para>
    /// </summary>
    public static class KnownSkillsPersistable
    {
        public static IPersistable For(SkillHost host, Id unitId) => new Impl(host, unitId);

        private sealed class Impl : IPersistable
        {
            private readonly SkillHost _host;
            private readonly Id _unitId;

            public Impl(SkillHost host, Id unitId)
            {
                _host = host ?? throw new ArgumentNullException(nameof(host));
                _unitId = unitId;
            }

            public string SectionKey => SaveSections.PlayerKnownSkills;

            public JsonValue Save()
            {
                var known = _host.GetKnownSkills(_unitId);
                var items = new List<JsonValue>(known.Count);
                foreach (var skillId in known)
                {
                    items.Add(new JsonString(skillId.Value));
                }

                return new JsonArray(items);
            }

            public void Load(JsonValue data)
            {
                if (data is JsonNull)
                {
                    return;
                }

                if (!(data is JsonArray arr))
                {
                    throw new FormatException(
                        $"player.known_skills 段的数据不是 JSON 数组（实际种类：{data.Kind}）");
                }

                for (var i = 0; i < arr.Count; i++)
                {
                    if (!(arr[i] is JsonString text) || !Id.TryParse(text.Value, out var skillId))
                    {
                        throw new FormatException($"player.known_skills[{i}] 不是合法的 Id 字符串");
                    }

                    _host.LearnSkill(_unitId, skillId);
                }
            }
        }
    }
}
