using System;

namespace Presentation.Common
{
    /// <summary>
    /// View 的种类（见 09_表现层.md 第 2 节"（建议）ViewKind 的枚举值建议至少覆盖：unit、
    /// gobj、projectile、areaTrigger、droppedLoot，与 05 中的逻辑对象分类一一对应"）。
    /// </summary>
    /// <remarks>
    /// 判断记录（第八方深度审核自检，`architecture/落地计划/audit-5c444f1-20260908/`）：本枚举成员
    /// 原名 <c>GameObject</c>，与具体引擎的核心类型同名，在技术无关的架构文档/接口正文里构成模糊的
    /// 技术名误报；改名为 <see cref="Gobj"/>，与 <c>Core.Foundation.SimLoop.EntityKinds.Gobj</c>
    /// （常量值 <c>"gobj"</c>，<c>core/carriers/gobj</c> 模块既有的中立缩写）保持一致，语义不变
    /// （仍是"泛化交互物件/世界对象"这一逻辑分类，不特指任何引擎的具体类型）。
    /// </remarks>
    /// <remarks>
    /// 判断记录（PJ140-01 兼容层，`architecture/落地计划/audit-c86bfa9-20260908/`
    /// 第七方审核）：上一条改名本身是正确的技术名去模糊，但对 1.3 消费方源码是破坏性变更——
    /// <c>ViewKind.GameObject</c> 在 1.4 编译成 <c>CS0117</c>（实测见 <c>consumer14_build_final.log</c>），
    /// 而 CHANGELOG/工程规范都把这类契约签名变化归为 MAJOR，不应该在次版本号发布里发生。加回
    /// <see cref="GameObject"/> 作为与 <see cref="Gobj"/> 数值相同的别名成员（C# 允许多个枚举成员共享
    /// 同一底层数值），标 <see cref="ObsoleteAttribute"/> 引导新代码改用 <see cref="Gobj"/>；两个成员
    /// 数值相等，<c>ViewKind.GameObject == ViewKind.Gobj</c> 恒真，任何 <c>switch</c>/比较逻辑不需要
    /// 同时处理两个分支。该别名成员本身只存在于代码，不出现在 architecture 正文（本仓库
    /// <c>architecture/00～13</c> 与 <c>architecture/adr/</c> 一律使用 <see cref="Gobj"/>），不违反本
    /// 仓库"架构正文不出现具体引擎/语言/框架/工具名"的硬性规则——枚举成员名本身不是技术名，只是与某个
    /// 引擎的核心类型同名，规则约束的是"文档正文"而不是"代码里允许存在的兼容别名标识符"。
    /// </remarks>
    public enum ViewKind
    {
        Unit,
        Gobj,

        /// <summary>PJ140-01 兼容层：<see cref="Gobj"/> 的 1.3 命名别名，见类型 remarks 判断记录。</summary>
        [Obsolete("改用 ViewKind.Gobj：本成员是 1.3 源码兼容别名，数值与 Gobj 相同。")]
        GameObject = Gobj,

        Projectile,
        AreaTrigger,
        DroppedLoot
    }
}
