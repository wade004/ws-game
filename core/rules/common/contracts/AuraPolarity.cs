using System;
using System.Collections.Generic;

namespace Core.Rules.Common
{
    /// <summary>
    /// 一个发现的交付缺口（2026-09-21，[ADR-0060](../../../../architecture/adr/0060-光环极性与图标引用字段补全.md)）：
    /// 光环极性——该光环对承受者是有利还是有害，供接入方画出增益/减益边框、分区。结构化枚举，不是
    /// 字符串也不是布尔（同批交付另一条第 2 条 <c>ActionBarSlotBlockReason</c> 同一惯例）：字符串
    /// 让接入方只能自己拼字面量比较，拼错编译期不报错；布尔表达不出"未声明"这一独立于两个真实取值
    /// 之外的第三种状态。
    /// <para>
    /// 判断记录（<see cref="Undeclared"/> 是显式取值，不是可空枚举 + <c>null</c>）：如果用
    /// <c>AuraPolarity?</c>（可空枚举）表达"未声明"，接入方每次读取都要多判断一层"有没有值"，且
    /// "有值但取值恰好是某个哨兵"与"没有值"两件事分别由类型系统的两个不同机制（`HasValue`/具体取值）
    /// 表达，容易在传递链路中把 <c>null</c> 沿途悄悄折叠掉（例如经过一次 <c>??</c> 兜底）而混淆成
    /// 某个具体极性；用一个显式取值表达"未声明"，全程只有一种"当前取值是什么"的问题，不存在第二套
    /// "有没有取值"的判断维度，与 <see cref="Beneficial"/>/<see cref="Harmful"/> 在类型系统里完全
    /// 平等，调用方一次 <c>switch</c> 就能穷尽全部三种状态，不会漏判。
    /// </para>
    /// </summary>
    public enum AuraPolarity
    {
        /// <summary>未声明——<c>skill.aura_def.polarity</c> 缺省缺失时的解析结果，不代表"中性"这一
        /// 具体设计意图，也不与 <see cref="Beneficial"/>/<see cref="Harmful"/> 共用取值（不编造默认
        /// 极性，见 <c>AuraDef.Polarity</c> 判断记录）。</summary>
        Undeclared = 0,

        /// <summary>该光环对承受者有利（对应数据表取值 <c>beneficial</c>）。</summary>
        Beneficial = 1,

        /// <summary>该光环对承受者有害（对应数据表取值 <c>harmful</c>）。</summary>
        Harmful = 2,
    }

    /// <summary><see cref="AuraPolarity"/> 与数据表使用的 snake_case 文本互转，惯例同
    /// <see cref="AuraEffectKindNames"/>（<see cref="Undeclared"/> 不参与互转——它不是
    /// <c>skill.aura_def.polarity</c> 的合法取值之一，只是字段缺失时的解析结果，见
    /// <see cref="AuraPolarity.Undeclared"/> 判断记录；调用方需要先判断是否为
    /// <see cref="AuraPolarity.Undeclared"/>，再决定要不要调用 <see cref="ToText"/>）。</summary>
    public static class AuraPolarityNames
    {
        private static readonly IReadOnlyDictionary<AuraPolarity, string> ToName = new Dictionary<AuraPolarity, string>
        {
            [AuraPolarity.Beneficial] = "beneficial",
            [AuraPolarity.Harmful] = "harmful",
        };

        private static readonly IReadOnlyDictionary<string, AuraPolarity> FromName = BuildReverse(ToName);

        private static Dictionary<string, AuraPolarity> BuildReverse(IReadOnlyDictionary<AuraPolarity, string> map)
        {
            var reverse = new Dictionary<string, AuraPolarity>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value] = pair.Key;
            }

            return reverse;
        }

        /// <summary><paramref name="polarity"/> 为 <see cref="AuraPolarity.Undeclared"/> 时抛
        /// <see cref="ArgumentException"/>——本方法只负责"真实取值 → 文本"，"未声明要不要输出、
        /// 输出什么"是调用方（数据序列化/表现层路径）的职责，不应该由本方法悄悄决定（同
        /// <see cref="Parse"/> 判断记录"不静默降级"同一立场）。</summary>
        public static string ToText(AuraPolarity polarity)
        {
            if (ToName.TryGetValue(polarity, out var text))
            {
                return text;
            }

            throw new ArgumentException($"AuraPolarity.{polarity} 没有对应的数据表文本（Undeclared 不参与互转，见类型判断记录）", nameof(polarity));
        }

        /// <summary>
        /// 判断记录（未知文本抛异常，不静默降级为 <see cref="AuraPolarity.Undeclared"/>）：
        /// <c>skill.aura_def.polarity</c> 的合法取值集合已由 <c>SkillSchemas.AuraPolarityValues</c>
        /// 登记为 <c>FieldKind.Enum</c>，加载期校验理论上已经挡住非法取值；本方法仍按 AGENTS.md §3
        /// "运行时路径不静默降级"防御性处理——调用方（<c>SkillDefCache.ParseAuraDef</c>，static
        /// 方法，不持有诊断出口）与同一方法内 <see cref="AuraEffectKindNames.Parse"/> 处理
        /// <c>effects[].kind</c> 未知取值时的既有做法一致，直接抛异常。不能把"字段存在但取值非法
        /// （数据损坏，或校验被绕过——如热重载路径）"悄悄当成"字段缺失（未声明）"处理，两者是完全
        /// 不同的错误：前者是数据错误，后者是合法留白。
        /// </summary>
        public static AuraPolarity Parse(string text)
        {
            if (TryParse(text, out var polarity))
            {
                return polarity;
            }

            throw new ArgumentException($"未知的 AuraPolarity 文本：\"{text ?? "<null>"}\"", nameof(text));
        }

        public static bool TryParse(string? text, out AuraPolarity polarity)
        {
            if (text != null && FromName.TryGetValue(text, out polarity))
            {
                return true;
            }

            polarity = AuraPolarity.Undeclared;
            return false;
        }
    }
}
