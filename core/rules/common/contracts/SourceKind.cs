namespace Core.Rules.Common
{
    /// <summary>
    /// 结算管线"来源类别"（T-N1-6，[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    /// 决策 5；06 第 4.1 节 2026-09-14 修订段）：施法者是玩家单位还是生物单位，由载体类型给出（见
    /// <see cref="IUnitAccess.GetSourceKind"/>），"目标乘区"步骤据此只读取 <c>stat.definition.scope</c>
    /// 匹配的承伤/被暴击减免属性——<c>scope: any</c> 恒读取，<c>scope: from_player</c>/
    /// <c>scope: from_creature</c> 只在 <see cref="SourceKind"/> 匹配时读取。
    /// <para>
    /// 06 §4.1 原文只登记 <c>player|creature</c> 两个取值（"由施法者载体类型自动填充"）；本类型额外
    /// 登记 <see cref="Unknown"/>（数值 0，C# 默认值）作为"载体类型尚未查询/无法判定"时的保守占位
    /// ——不是契约新增的第三种玩法取值，只是契约后果段"来源类别进入结算上下文是契约扩展，所有结算
    /// 调用方要补这一入参（默认由载体类型自动填充，调用方通常无感）"落到类型层面的必然需要：既有
    /// <see cref="EffectContext"/> 十五/十六参构造函数（ABI 规则禁止改动其签名）无法反过来向调用方
    /// 索要一次 <c>IUnitAccess</c> 查询，只能给一个安全默认值；若把默认值定成 <see cref="Player"/>
    /// 或 <see cref="Creature"/> 中的任意一个，会让"生产路径漏填 sourceKind"这一遗漏悄悄伪装成
    /// "确有其事的来源类别"（<c>scope: from_player</c>/<c>scope: from_creature</c> 之一因此静默匹配，
    /// 另一个静默不匹配，行为错得不易被察觉）；<see cref="Unknown"/> 则让两类作用域属性对它一律不
    /// 匹配（与 <c>scope: any</c> 恒匹配、单机下 <c>scope: from_player</c> 永远读不到同属"未参与/不
    /// 生效"的零成本退化路径），生产路径（<c>CastPipeline.ExecuteEffectsOnly</c>/
    /// <c>AuraHost.FirePeriodic</c>/投射物命中结算）必须显式经 <see cref="IUnitAccess.GetSourceKind"/>
    /// 查询真实值，不得依赖本默认值。
    /// </para>
    /// </summary>
    public enum SourceKind
    {
        Unknown,
        Player,
        Creature,
    }
}
