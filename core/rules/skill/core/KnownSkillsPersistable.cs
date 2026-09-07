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
    /// 判断记录（C09 收口，外部审计 7e63d66 第四轮，P2；取代原"<c>Load</c> 直接
    /// <see cref="SkillHost.LearnSkill"/> 逐个还原，不先清空"判断记录）：原判断记录的前提——"读档
    /// 发生在一局全新构造的 <c>Core.Gameplay.Assembly.GameplayAssembly</c> 上，永久技能集合总是从
    /// 空开始"——只在"整局重新构造"这一种读档场景下成立；<c>Core.Gameplay.Assembly.GameplayAssembly.RestoreFromSlot</c>
    /// 复用当前已在运行的 <see cref="SkillHost"/> 实例（同图读档/切档一类场景），此时单位名下可能
    /// 已经积累了快照之外的永久技能（例如两次存档之间又学会的新技能）。原实现只增不减——<c>Load</c>
    /// 逐个 <see cref="SkillHost.LearnSkill"/> 快照里的技能，从未撤销快照之外、当前仍标记为永久来源
    /// 的技能，读一份"更早、技能更少"的存档反而不会让技能变少，与"读档 = 恢复到那个时间点的状态"
    /// 这一基本语义矛盾（外部审计 C09 复现：同一 host 上先存 {A}，再学会 B（变成 {A,B}），再读那份
    /// 只有 A 的快照，正确结果应只剩 A，原实现结果是 {A,B} 原样保留）。
    /// </para>
    /// <para>
    /// 修复为替换语义：<see cref="Load"/> 先经 <see cref="SkillHost.GetPermanentlyKnownSkills"/> 取出
    /// 当前全部永久技能，对"当前有、快照没有"的技能逐个 <see cref="SkillHost.ForgetSkill(Id,Id)"/>
    /// （不带来源参数，归属 <see cref="SkillHost"/> 内部的永久来源哨兵——只撤销永久来源这一份引用，
    /// 不触碰任何装备等临时来源，见 <see cref="SkillHost"/> 的来源引用计数判断记录），再对快照里的
    /// 全部技能逐个 <see cref="SkillHost.LearnSkill(Id,Id)"/> 补回（幂等，"快照有、当前也有"的技能
    /// 不受影响）。装备等临时来源的技能因为从不经由本类型 <c>Save</c>/<c>Load</c>（见下方 N07 判断
    /// 记录），也从不出现在快照里，因此永远不会被这里的"撤销"逻辑触碰到——保持独立生命周期，卸装备
    /// 仍然只由 <c>EquipmentHost</c> 自己经带来源的 <see cref="SkillHost.ForgetSkill(Id,Id,Id)"/> 管理，
    /// 不会被这次"清空永久集合"误伤。
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
                // N07 收边补齐（外部审计 68c9bed，P2）：改用 GetPermanentlyKnownSkills（只含永久来源
                // 授予的技能），不再用 GetKnownSkills（全部来源并集）——避免装备授予的临时技能被当成
                // 永久技能快照进存档，见 SkillHost.GetPermanentlyKnownSkills 判断记录。装备授予的技能
                // 改由 Core.Carriers.Item.ItemPersistable.Load 恢复装备时重新走 SkillGranter 授予。
                var known = _host.GetPermanentlyKnownSkills(_unitId);
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
                    // C09 收口：JsonNull 视为"空快照"（惯例同 UnitPersistable.Load 对 JsonNull 的
                    // 处理），同样要走替换语义——空快照意味着"这个时间点没有任何永久技能"，必须把
                    // 当前已有的永久技能一并清空，不能因为快照本身是 null 就退化成 no-op（否则同一
                    // host 上先学会技能、再读一份从未写过 known_skills 段的旧存档，技能会原样保留，
                    // 与"读档 = 恢复到那个时间点"矛盾，参见类型注释判断记录）。
                    ReplacePermanentlyKnownSkills(Array.Empty<Id>());
                    return;
                }

                if (!(data is JsonArray arr))
                {
                    throw new FormatException(
                        $"player.known_skills 段的数据不是 JSON 数组（实际种类：{data.Kind}）");
                }

                var snapshot = new List<Id>(arr.Count);
                for (var i = 0; i < arr.Count; i++)
                {
                    if (!(arr[i] is JsonString text) || !Id.TryParse(text.Value, out var skillId))
                    {
                        throw new FormatException($"player.known_skills[{i}] 不是合法的 Id 字符串");
                    }

                    snapshot.Add(skillId);
                }

                ReplacePermanentlyKnownSkills(snapshot);
            }

            /// <summary>C09 收口：把当前永久技能集合原子替换为 <paramref name="snapshot"/>——先撤销
            /// "当前有、快照没有"的永久技能（<see cref="SkillHost.ForgetSkill(Id,Id)"/>，只影响永久
            /// 来源那一份引用），再补上快照里的全部技能（<see cref="SkillHost.LearnSkill(Id,Id)"/>，
            /// 对"快照有、当前也有"的技能是安全的幂等 no-op）。见类型注释判断记录。</summary>
            private void ReplacePermanentlyKnownSkills(IReadOnlyList<Id> snapshot)
            {
                var snapshotSet = new HashSet<Id>(snapshot);
                var currentlyPermanent = _host.GetPermanentlyKnownSkills(_unitId);
                foreach (var skillId in currentlyPermanent)
                {
                    if (!snapshotSet.Contains(skillId))
                    {
                        _host.ForgetSkill(_unitId, skillId);
                    }
                }

                foreach (var skillId in snapshot)
                {
                    _host.LearnSkill(_unitId, skillId);
                }
            }
        }
    }
}
