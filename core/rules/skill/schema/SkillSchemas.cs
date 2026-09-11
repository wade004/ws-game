using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// <c>skill.*</c> 五张表的 <see cref="TableSchema"/> 声明（见 06 第 3.1/3.3/3.4/3.5 节字段表、
    /// 04 第 1.1 节表清单、schema/README.md 字段表）。调用方在构造
    /// <see cref="Core.Foundation.DataRegistry.IDataRegistry"/> 后需要
    /// <c>RegisterSchema(SkillSchemas.Def)</c> 等依次注册才能加载对应数据文件（本模块不自动
    /// 注册，注册时机由宿主统一掌控，与 <c>power_set.PowerSchemas</c>/<c>stat_block.StatSchemas</c>
    /// 同一惯例）。字段类型说明见 schema/README.md。
    /// <para>
    /// ADR-0019 首批登记（F1a）：<c>effects</c>（技能/光环两处）按判别字段 <c>kind</c> 登记为
    /// <see cref="VariantSchema"/>，键集合分别与 <see cref="EffectKindNames"/>/
    /// <see cref="AuraEffectKindNames"/> 的全集一致（见 <c>SkillSchemaCoverageTests</c> 用测试锁死
    /// 这条一致性，04 第 5 节"变体表与原语注册集合一致"落地形式的判断记录）。参数表以运行时解析
    /// 代码（<c>EffectDispatcher</c>/<c>AuraHost</c>/<c>ProjectileHost</c>/各 <c>IEffectExtension</c>
    /// 实现）为唯一依据逐个原语对照，见 schema/README.md"效果原语参数表"一节的完整对照与判断记录。
    /// </para>
    /// </summary>
    public static class SkillSchemas
    {
        /// <summary><c>kind</c> 字段合法取值（06 第 3.1 节）。</summary>
        public static readonly string[] SkillKindValues = { "active", "passive" };

        /// <summary><c>interrupt_flags[]</c> 元素合法取值（06 第 3.1 节"movement|damage_taken|..."，
        /// 任务书补齐第三项 <c>control</c>）。</summary>
        public static readonly string[] InterruptFlagValues = { "movement", "damage_taken", "control" };

        public static readonly string[] ProjectileTravelModeValues = { "straight", "arc", "homing" };
        public static readonly string[] ProjectileHitBehaviorValues = { "impact_on_first", "pierce", "impact_on_expiry" };

        /// <summary>ADR-0028 新增：<c>projectile.params.relation_policy</c> 合法取值——
        /// <c>default</c>（缺省，现行按 <c>HitQueryTags</c> 标签过滤的行为，不做阵营判定）、
        /// <c>hostile_only</c>/<c>friendly_only</c>（按 <c>IFactionMatrix</c> 判定，未注入时退化为
        /// <c>default</c>）、<c>locked_target_only</c>（只命中施法时选中的目标）。见
        /// <c>Core.Carriers.Projectile.ProjectileHost.PassesRelationPolicy</c>。</summary>
        public static readonly string[] ProjectileRelationPolicyValues =
            { "default", "hostile_only", "friendly_only", "locked_target_only" };

        /// <summary>ADR-0028 新增：<c>projectile.params.pierce_order</c> 合法取值——<c>nearest</c>
        /// （缺省，按到起点距离升序，现行 <c>pierce</c> 命中行为的既有顺序）、<c>hostile_first</c>
        /// （先处理阵营反应为 Hostile 的候选，未注入 <c>IFactionMatrix</c> 时退化为 <c>nearest</c>）。
        /// 见 <c>Core.Carriers.Projectile.ProjectileHost.BuildPierceComparer</c>。</summary>
        public static readonly string[] ProjectilePierceOrderValues = { "nearest", "hostile_first" };
        public static readonly string[] MoveModeValues = { "charge", "leap", "knockback" };
        public static readonly string[] ModStatOpValues = { "flat", "pct", "mult" };
        public static readonly string[] ControlFlagValues = { "no_move", "no_cast", "no_attack", "no_interact" };

        /// <summary>全部 19 种 <see cref="EffectKind"/> 的 snake_case 文本（供
        /// <c>immunity.params.effect_kinds[]</c> 登记为枚举数组元素；见 <c>AuraHost.ApplyStaticEffects</c>
        /// 对 <c>immunity</c> 效果 <c>effect_kinds</c> 参数逐个 <see cref="EffectKindNames.TryParse"/>
        /// 的读取方式）。</summary>
        public static readonly string[] AllEffectKindValues = BuildAllEffectKindValues();

        private static string[] BuildAllEffectKindValues()
        {
            var values = (EffectKind[])Enum.GetValues(typeof(EffectKind));
            var names = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                names[i] = EffectKindNames.ToText(values[i]);
            }
            return names;
        }

        // -----------------------------------------------------------------
        // school_damage / heal 共用参数（EffectDispatcher.ApplyDamageOrHeal 非 WeaponDamagePct 分支）：
        // base_value/coefficient 均可省（缺省取 EffectContext 传入的默认值），school 缺省取
        // skill.def.school（CastPipeline.ExecuteEffectsOnly：ParamsX.GetIdOpt(..,"school") ?? def.School），
        // scaling_stat 缺省不缩放。
        // -----------------------------------------------------------------
        private static readonly IReadOnlyList<FieldSchema> DamageOrHealParams = new[]
        {
            new FieldSchema("base_value", FieldKind.Number, required: false, description: "基础值，缺省 0"),
            new FieldSchema("coefficient", FieldKind.Number, required: false, description: "缩放系数，缺省 0"),
            new FieldSchema("school", FieldKind.Id, required: false, description: "缺省取 skill.def.school"),
            new FieldSchema("scaling_stat", FieldKind.Reference, required: false, referenceTable: "stat.definition",
                description: "缩放属性，缺省不缩放（判断记录：stat.definition 属 L1，本模块已依赖 StatBlock 程序集，登记为 Reference 不违反分层）"),
        };

        /// <summary>技能效果列表（<c>skill.def.effects</c>）与投射物命中后效果列表
        /// （<c>projectile.params.on_hit_effects</c>）共用的元素结构：按判别字段 <c>kind</c> 分派
        /// 到 19 种效果原语各自的 <c>params</c> 形状（见 06 第 3.2 节、ADR-0019"登记一次、多处
        /// 复用"记法示例）。<c>Name</c>/<c>Required</c> 仅作占位（本字段只作为 <see cref="FieldSchema.Item"/>
        /// 使用，不会被当作某个对象的具名子字段校验，见 <c>DataRegistry.ValidateArrayField</c>）。</summary>
        public static readonly FieldSchema EffectsItemSchema = new FieldSchema(
            "<effect>", FieldKind.Object, required: true, variants: BuildEffectVariants(),
            description: "{kind: EffectKind, params: Object}，19 种效果原语见 06 第 3.2 节");

        private static VariantSchema BuildEffectVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                [EffectKindNames.ToText(EffectKind.SchoolDamage)] = ParamsCase(required: true, DamageOrHealParams,
                    "对目标造成一次学派伤害，数值由 base_value/coefficient/scaling_stat 组装后交由战斗结算"),
                [EffectKindNames.ToText(EffectKind.Heal)] = ParamsCase(required: true, DamageOrHealParams,
                    "对目标造成一次治疗，数值由 base_value/coefficient/scaling_stat 组装后交由战斗结算"),

                // RC-11：pct 缺省退回 EffectContext.BaseValue（即 params.base_value，若也未提供则 0），
                // 见 EffectDispatcher.ApplyDamageOrHeal 判断记录；本登记只列权威参数名 pct。
                [EffectKindNames.ToText(EffectKind.WeaponDamagePct)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("pct", FieldKind.Number, required: false, description: "武器基础伤害的百分比，缺省 0"),
                }, "按施法者当前武器基础伤害的百分比造成伤害，见 pct 子字段"),

                [EffectKindNames.ToText(EffectKind.ApplyAura)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("aura_def", FieldKind.Reference, required: true, referenceTable: "skill.aura_def",
                        description: "要施加的光环定义引用"),
                    new FieldSchema("duration_override", FieldKind.Number, required: false, description: "覆盖 aura_def.duration")
                        .WithRange(FieldRange.Range(min: 0)),
                    // 依据（ADR-0021、消费方反馈 2026-09-10"技能效果参数数值范围校验改进建议"）：
                    // EffectDispatcher.ApplyAuraEffectPrimitive（本文件同目录 core/EffectDispatcher.cs :235）
                    // 原样把 duration_override 转发给 AuraHost.ApplyAura 作为光环剩余时长——负值会
                    // 产生一个负的剩余持续时间，语义上不可能存在（"持续 -1 秒"没有意义）；省略该字段
                    // 时沿用 aura_def.duration（可空=永久），这条既有约定不受本次登记影响。0 合法
                    // （代表极短/几乎立即到期的光环，登记层不禁止这种边界用法）。
                }, "对目标施加一个光环实例，见 aura_def/duration_override 子字段"),

                [EffectKindNames.ToText(EffectKind.Dispel)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("category", FieldKind.Id, required: false, description: "对应 skill.aura_def.dispel_type"),
                    new FieldSchema("count", FieldKind.Int, required: false, description: "缺省 1"),
                }, "按 category/count 驱散目标身上匹配的光环"),

                [EffectKindNames.ToText(EffectKind.Energize)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("power_type", FieldKind.Id, required: true,
                        description: "判断记录：多数取值是 arch.power_type 的记录 id，但也允许内置特例（如 health，不是数据行），" +
                            "不是干净的单一表引用，按 Id 登记（消费方反馈第 30 条：常见情形目标确定，登记为软引用，内置特例不解析属预期降级）")
                        .WithSoftReference(table: "arch.power_type"),
                    new FieldSchema("amount", FieldKind.Number, required: false, description: "缺省 0"),
                }, "为目标恢复/扣减指定资源类型的数值，见 power_type/amount 子字段"),

                // 判断记录（偏离方案第 36 行"skill_id→skill.def"示例，收窄为 Id）：trigger_spell/
                // modify_cooldown/add_charge 三处的 skill_id 若登记为 Reference，会与既有测试套件
                // （EffectPrimitiveDispatchTests.AddCharge_UnknownSkillId_WarnsAndDoesNotThrow 等）
                // 已经覆盖的"引用一个当前未加载/不存在的技能，EffectDispatcher 在运行期温和降级为
                // 警告而不抛异常"这条防御路径冲突——CastPipeline.TriggerCast/EffectDispatcher.
                // ApplyModifyCooldown/ApplyAddCharge 对目标技能缺失均已有专门的 Warn 分支（见各自
                // 实现），不是"缺失即崩溃"的硬依赖，说明这三处的语义更接近 04 §5.1 判断记录
                // "arch.class.power_types 等……当前未与本模块静态耦合"的口径，而不是
                // apply_aura.aura_def 那种缺目标即时崩溃、必须强校验的场景（AuraHost.ApplyAura 对
                // 不存在的 aura_def 没有同等的温和降级）。三处按 Id 登记，不做跨表存在性检查。
                [EffectKindNames.ToText(EffectKind.TriggerSpell)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("skill_id", FieldKind.Id, required: true, description: "要触发施放的技能引用（消费方反馈第 30 条：登记为软引用）")
                        .WithSoftReference(table: "skill.def"),
                }, "以当前效果的触发链深度同步触发一次技能施放，见 skill_id 子字段"),

                [EffectKindNames.ToText(EffectKind.ModifyCooldown)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("skill_id", FieldKind.Id, required: false,
                        description: "skill_id/category 至少填一个（业务判断，登记层不表达“二选一必填”）"),
                    new FieldSchema("category", FieldKind.Id, required: false, description: "冷却分类标签，与 skill_id 至少填一个；无独立登记表，是 skill.def.cooldown_category 使用的同一套自由标签"),
                    new FieldSchema("delta", FieldKind.Number, required: false, description: "缺省 0"),
                }, "按 skill_id 或 category 调整目标技能/分类的冷却，delta 为增量（正数延长、负数缩短）"),

                [EffectKindNames.ToText(EffectKind.AddCharge)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("skill_id", FieldKind.Id, required: true, description: "要增加充能的技能引用（消费方反馈第 30 条：登记为软引用）")
                        .WithSoftReference(table: "skill.def"),
                    new FieldSchema("amount", FieldKind.Int, required: false, description: "缺省 1"),
                }, "为目标指定技能增加充能次数，见 skill_id/amount 子字段"),

                [EffectKindNames.ToText(EffectKind.Projectile)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("travel_mode", FieldKind.Enum, required: false, enumValues: ProjectileTravelModeValues,
                        description: "缺省 straight"),
                    new FieldSchema("hit_behavior", FieldKind.Enum, required: false, enumValues: ProjectileHitBehaviorValues,
                        description: "缺省 impact_on_first"),
                    new FieldSchema("speed", FieldKind.Number, required: false, description: "缺省 ProjectileOptions.DefaultSpeed"),
                    new FieldSchema("max_range", FieldKind.Number, required: false, description: "缺省 ProjectileOptions.DefaultMaxRange"),
                    new FieldSchema("arc_height", FieldKind.Number, required: false, description: "缺省 ProjectileOptions.DefaultArcHeight，仅 arc 生效"),
                    new FieldSchema("impact_radius", FieldKind.Number, required: false, description: "缺省 ProjectileOptions.DefaultImpactRadius，仅 impact_on_expiry 生效"),
                    new FieldSchema("max_pierce_count", FieldKind.Int, required: false, description: "仅 pierce 生效，缺省不限"),
                    new FieldSchema("relation_policy", FieldKind.Enum, required: false, enumValues: ProjectileRelationPolicyValues,
                        description: "ADR-0028，缺省 default（不做阵营过滤，现行行为）"),
                    new FieldSchema("pierce_order", FieldKind.Enum, required: false, enumValues: ProjectilePierceOrderValues,
                        description: "ADR-0028，仅 pierce 生效，缺省 nearest（按距离升序，现行行为）"),
                    new FieldSchema("display_ref", FieldKind.Id, required: false,
                        description: "判断记录（分层边界）：display.map 属 L3/L5，本模块（L2）不可 Reference（04 §5.1 口径），退回 Id（消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                        .WithSoftReference(table: "display.map"),
                    new FieldSchema("on_hit_effects", FieldKind.Array, required: false, itemFactory: () => EffectsItemSchema,
                        description: "命中后触发的效果列表，元素结构复用本 variants 自身（同一原语注册表，见 04 第 3.2 节记法示例“登记一次、多处复用”）"),
                }, "生成一枚投射物并按落体轨迹结算命中，具体参数见各子字段"),

                // 判断记录：move 的四个子字段按 mode（charge|leap|knockback）分别只使用其中一部分
                // （EffectDispatcher.ApplyMove），本次登记不再按 mode 拆一层嵌套 Variants——四个字段
                // 作为一份扁平的"该原语参数超集"登记即可覆盖 F1a 目标（mode 本身已有 Enum 校验），
                // 二级 Variants 留待后续确有需要时再引入，避免过度设计。
                [EffectKindNames.ToText(EffectKind.Move)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("mode", FieldKind.Enum, required: false, enumValues: MoveModeValues, description: "缺省 charge"),
                    new FieldSchema("point", FieldKind.Vec2, required: false, description: "leap 目标点"),
                    new FieldSchema("distance", FieldKind.Number, required: false, description: "knockback 距离，缺省 5"),
                    new FieldSchema("stop_distance", FieldKind.Number, required: false, description: "charge 停止距离，缺省 1.0"),
                }, "按 mode（charge|leap|knockback）位移施法者或目标，只写最终逻辑位置，不做寻路/碰撞"),

                [EffectKindNames.ToText(EffectKind.Summon)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("creature_template", FieldKind.Id, required: false,
                        description: "判断记录（分层边界+既有测试）：creature.template 属 L3，本模块（L2）不可 Reference（04 §5.1 口径），退回 Id；" +
                        "required 收窄为 false——本模块（L2）不落地 summon 实现（见 IEffectExtension 顶部注释，L3 carriers/summon 才有真正的 SummonEffectExtension），" +
                        "EffectPrimitiveDispatchTests 的扩展点分派测试按 kind 探测分派、不关心 params 内容，与既有测试套件的调用惯例保持一致"),
                    new FieldSchema("duration", FieldKind.Number, required: false, description: "缺省永久"),
                    new FieldSchema("position", FieldKind.Vec2, required: false, description: "缺省沿施法者朝向偏移一段距离"),
                }, "召唤一个生物实体，具体落地由 IEffectExtension 扩展点实现（本模块不落地）"),

                [EffectKindNames.ToText(EffectKind.Interrupt)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("lock_school", FieldKind.Id, required: false, description: "缺省不锁学派"),
                    new FieldSchema("lock_duration", FieldKind.Number, required: false, description: "缺省 0"),
                }, "打断目标当前的读条/引导，可选按 lock_school 锁定该学派施法一段时间"),

                [EffectKindNames.ToText(EffectKind.Teleport)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("point", FieldKind.Vec2, required: false, description: "缺省目标当前位置（等价 no-op）"),
                }, "将目标瞬移到指定点"),

                // open_lock：06 第 3.2 节"参数要点：无"，params 本身可省略（见任务书判断记录）。
                [EffectKindNames.ToText(EffectKind.OpenLock)] = new[]
                {
                    new FieldSchema("params", FieldKind.Object, required: false, fields: Array.Empty<FieldSchema>(),
                        description: "无参数（对目标 GameObject 尝试开锁）"),
                },

                [EffectKindNames.ToText(EffectKind.CreateItem)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("item_template", FieldKind.Id, required: false,
                        description: "判断记录（分层边界+既有测试，同 summon.creature_template）：item.template 属 L3，本模块（L2）不可 Reference（04 §5.1 口径），退回 Id；required 收窄为 false"),
                    new FieldSchema("count", FieldKind.Int, required: false, description: "缺省 1"),
                }, "为目标创建物品，具体落地由 IEffectExtension 扩展点实现（本模块不落地）"),

                // 判断记录（偏离方案第 36 行示例，同 trigger_spell/modify_cooldown/add_charge）：
                // EffectDispatcher.ApplyLearnSkill 对 skillId 无任何存在性检查，直接转调
                // _learnSkill(targetId, skillId) 回调——"学会一个当前未加载的技能 id"是该回调实现
                // 自身的职责边界，不属于本模块能静态判定的范围；且 LearnSkillEffect_GrantsSkillToTarget
                // 既有测试明确以一个不在 skill.def 表中的 id 驱动，按 Id 登记。
                [EffectKindNames.ToText(EffectKind.LearnSkill)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("skill_id", FieldKind.Id, required: true, description: "要学会的技能引用（消费方反馈第 30 条：登记为软引用）")
                        .WithSoftReference(table: "skill.def"),
                }, "使目标学会指定技能，见 skill_id 子字段"),

                // set_world_flag：06 第 3.2 节"flagKey、值"，本模块尚无落地的 IEffectExtension 实现
                // （见 IEffectExtension 顶部注释，五类扩展效果之一，实现由持有 L4 WorldState 类型的
                // 宿主注入）。判断记录：值的类型是 Bool｜Id（05 第 8.2 节 WorldState 取值类型），
                // FieldKind 无法表达联合类型，本次只登记 flag_key（Id，同 gobj.lock.requirement 的
                // world_flag 变体 flag_key 命名惯例），value 暂不登记类型（未登记子字段默认不报错，
                // 不影响内容作者填写）。
                [EffectKindNames.ToText(EffectKind.SetWorldFlag)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("flag_key", FieldKind.Id, required: false,
                        description: "世界标志键，见 world.flag_schema；required 收窄为 false（同 summon.creature_template 判断记录，本模块无落地实现）"),
                }, "写入一个世界标志，具体落地由持有 WorldState 的宿主实现（本模块不落地）"),

                // script：06 第 3.2 节"钩子 id"，对应 found.hook。判断记录（分层边界）：found.hook
                // 当前无实现级 schema 登记（见 04 变更记录 2026-09-05 行"仍无对应实现级 schema 登记"），
                // Reference 到未登记 schema 的表在校验期恒不存在、会把每条使用 script 效果的记录判为
                // 引用失效，先退回 Id，待 found.hook 补齐登记后再升级为 Reference。
                [EffectKindNames.ToText(EffectKind.Script)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("hook_id", FieldKind.Id, required: false,
                        description: "found.hook 钩子 id，退回 Id 见判断记录；required 收窄为 false（同 summon.creature_template 判断记录，本模块无落地实现）"),
                }, "调用一个脚本钩子，见 hook_id 子字段（本模块不落地）"),
            };

            return new VariantSchema("kind", cases);
        }

        /// <summary>光环效果列表（<c>skill.aura_def.effects</c>）元素结构：按判别字段 <c>kind</c>
        /// 分派到 10 种光环效果类型（06 第 3.3 节）。</summary>
        public static readonly FieldSchema AuraEffectsItemSchema = new FieldSchema(
            "<aura_effect>", FieldKind.Object, required: true, variants: BuildAuraEffectVariants(),
            description: "{kind: AuraEffectKind, params: Object}，10 种光环效果见 06 第 3.3 节");

        private static VariantSchema BuildAuraEffectVariants()
        {
            var cases = new Dictionary<string, IReadOnlyList<FieldSchema>>(StringComparer.Ordinal)
            {
                [AuraEffectKindNames.ToText(AuraEffectKind.ModStat)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("stat", FieldKind.Reference, required: true, referenceTable: "stat.definition",
                        description: "被修正的属性引用"),
                    new FieldSchema("op", FieldKind.Enum, required: false, enumValues: ModStatOpValues, description: "缺省 flat"),
                    new FieldSchema("value", FieldKind.Number, required: false, description: "缺省 0"),
                }, "对目标施加一条持续属性修正，见 stat/op/value 子字段"),

                // periodic_damage/periodic_heal：AuraHost.FirePeriodic 对 school 用
                // ParamsX.GetId(entry.Params, "school", default) 读取，fallback 是零值 Id（不同于
                // 顶层效果 school 缺省取 skill.def.school 那样有意义的兜底）——缺失会得到一个无效
                // Id 继续参与免疫/SpellMod 过滤，判断记录：本登记把 school 标为必填，在登记层拦下这
                // 类会产生无效 Id 的缺失，而不是放任其通过校验、运行期才暴露。interval 同理：缺失时
                // AuraHost.Update 读到 0，`interval <= 0` 分支直接 continue（周期效果整体不生效，
                // 与"登记了却完全不生效"的作者意图不符），标为必填。
                [AuraEffectKindNames.ToText(AuraEffectKind.PeriodicDamage)] = PeriodicParamsCase("按 interval 周期对目标造成一次学派伤害，见子字段"),
                [AuraEffectKindNames.ToText(AuraEffectKind.PeriodicHeal)] = PeriodicParamsCase("按 interval 周期对目标造成一次治疗，见子字段"),

                [AuraEffectKindNames.ToText(AuraEffectKind.Absorb)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("amount", FieldKind.Number, required: true, description: "吸收总量"),
                    new FieldSchema("school", FieldKind.Id, required: false, description: "缺省吸收全部学派"),
                }, "为目标提供一层可吸收伤害的护盾，见 amount/school 子字段"),

                [AuraEffectKindNames.ToText(AuraEffectKind.Immunity)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("schools", FieldKind.IdList, required: false, description: "免疫的学派集合，缺省不限学派")
                        .WithFreeIds("学派（school）当前没有独立登记表，只是随 skill.def.school 自由填写的 Id，见本文件多处 school 字段判断记录"),
                    new FieldSchema("effect_kinds", FieldKind.Array, required: false,
                        item: new FieldSchema("<effect_kind>", FieldKind.Enum, required: true, enumValues: AllEffectKindValues,
                            description: "免疫的效果原语取值，见 EffectKindNames 全集"),
                        description: "免疫的效果原语集合，缺省不限原语"),
                }, "使目标免疫指定学派/效果原语，见 schools/effect_kinds 子字段"),

                [AuraEffectKindNames.ToText(AuraEffectKind.ProcTrigger)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("proc_ref", FieldKind.Reference, required: true, referenceTable: "skill.proc_def",
                        description: "触发规则定义引用"),
                }, "为目标登记一个触发器，命中触发事件后按 proc_ref 引用的规则触发技能"),

                [AuraEffectKindNames.ToText(AuraEffectKind.SpellMod)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("spell_mod_ref", FieldKind.Reference, required: true, referenceTable: "skill.spell_mod_def",
                        description: "法术修正定义引用"),
                }, "为目标施加一条法术修正，具体作用维度/运算/数值见 spell_mod_ref 引用的定义"),

                [AuraEffectKindNames.ToText(AuraEffectKind.OverrideSkill)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("from", FieldKind.Reference, required: true, referenceTable: "skill.def",
                        description: "被替换的原技能引用"),
                    new FieldSchema("to", FieldKind.Reference, required: true, referenceTable: "skill.def",
                        description: "替换后实际释放的技能引用"),
                }, "施加期间将目标释放的某个技能替换为另一个技能，见 from/to 子字段"),

                [AuraEffectKindNames.ToText(AuraEffectKind.Control)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("flags", FieldKind.Array, required: false,
                        item: new FieldSchema("<flag>", FieldKind.Enum, required: true, enumValues: ControlFlagValues,
                            description: "控制标志位取值，见 ControlFlagValues（no_move|no_cast|no_attack|no_interact）"),
                        description: "控制标志位集合，缺省不施加任何控制"),
                }, "对目标施加控制标志位，见 flags 子字段"),

                // flag：06 第 3.3 节"单纯的标志位光环……不产生其它效果"，params 可省略。
                [AuraEffectKindNames.ToText(AuraEffectKind.Flag)] = new[]
                {
                    new FieldSchema("params", FieldKind.Object, required: false, fields: Array.Empty<FieldSchema>(),
                        description: "无参数"),
                },
            };

            return new VariantSchema("kind", cases);
        }

        private static IReadOnlyList<FieldSchema> PeriodicParamsCase(string description) => ParamsCase(required: true, new[]
        {
            // 依据（ADR-0021、消费方反馈 2026-09-10"技能效果参数数值范围校验改进建议"）：
            // AuraHost.Update（core/AuraHost.cs :377-381）对折算后的 interval <= 0 直接跳过本次周期
            // 结算（"周期效果整体不生效"）——interval<=0 在登记表层面就是内容错误（作者本意是"每隔
            // N 单位触发一次"，<=0 无法表达任何有意义的周期），此前只能在运行期被静默吞掉，登记范围
            // 后改为加载期 0 error 阻断，见 AuraHost.Update 分支旁新增诊断判断记录。
            new FieldSchema("interval", FieldKind.Number, required: true, description: "周期间隔，以时间单位计（见判断记录）")
                .WithRange(FieldRange.Range(min: 0, minExclusive: true)).WithUnit(FieldUnit.Time),
            new FieldSchema("base_value", FieldKind.Number, required: false, description: "缺省 0"),
            new FieldSchema("coefficient", FieldKind.Number, required: false, description: "缺省 0"),
            new FieldSchema("school", FieldKind.Id, required: true, description: "无缺省（见判断记录）"),
            // P3-04 根治（外部审计 audit-c9ff301-20260909）：AuraHost.FirePeriodic 组装的
            // EffectContext 与 school_damage/heal 走的是同一条 EffectDispatcher.ApplyDamageOrHeal
            // 结算路径（见该方法 scaling_stat = ParamsX.GetIdOpt(context.Params, "scaling_stat")
            // 判断记录），运行期确实会读取并消费 params.scaling_stat 对周期效果的缩放贡献——此前只
            // 在非周期的 DamageOrHealParams（本文件 :67）登记了这个字段，periodic 变体漏登记，导致
            // 内容作者/编辑器看不到这个受支持的可选参数（schema 漏项，不是运行期行为缺陷：未登记
            // 字段不报错，不影响已经这样填写的数据）。
            new FieldSchema("scaling_stat", FieldKind.Reference, required: false, referenceTable: "stat.definition",
                description: "缩放属性，缺省不缩放（同 DamageOrHealParams.scaling_stat，两条效果路径共用同一份 EffectDispatcher.ApplyDamageOrHeal 结算逻辑）"),
        }, description);

        private static IReadOnlyList<FieldSchema> ParamsCase(bool required, IReadOnlyList<FieldSchema> paramFields, string description) => new[]
        {
            new FieldSchema("params", FieldKind.Object, required: required, fields: paramFields, description: description),
        };

        public static TableSchema Def { get; } = new TableSchema(
            name: "skill.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.<name>"),
                new FieldSchema("school", FieldKind.Id, required: true, description: "学派"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: SkillKindValues,
                    description: "active|passive"),
                new FieldSchema("range", FieldKind.Number, required: true, description: "射程，0 表示无限制/作用于自身"),
                new FieldSchema("tags", FieldKind.IdList, required: false, description: "标签集合")
                    .WithFreeIds("技能标签当前没有独立登记表，是内容作者自由声明的分类标签"),
                new FieldSchema("cast_time", FieldKind.Number, required: true, description: "读条时间，0 表示瞬发").WithUnit(FieldUnit.Time),
                new FieldSchema("channel_time", FieldKind.Number, required: false, description: "引导时长，与 cast_time 互斥").WithUnit(FieldUnit.Time),
                new FieldSchema("cost", FieldKind.Array, required: false,
                    item: new FieldSchema("<cost_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("power_type", FieldKind.Id, required: true, description: "消耗的资源类型引用，多数取值是 arch.power_type 的记录 id，也允许内置特例（同 energize.power_type 判断记录；消费方反馈第 30 条：登记为软引用）")
                            .WithSoftReference(table: "arch.power_type"),
                        new FieldSchema("amount", FieldKind.Number, required: true, description: "消耗数量"),
                    }, description: "{power_type: Id, amount: Number}，单条消耗资源条目"),
                    description: "[{power_type: Id, amount: Number}, ...]"),
                new FieldSchema("cooldown_category", FieldKind.Id, required: false, description: "冷却分类标签，无独立登记表，是内容作者自由声明的分类标签（同 modify_cooldown.category 判断记录）"),
                new FieldSchema("cooldown_duration", FieldKind.Number, required: false, description: "冷却时长，缺省 0").WithUnit(FieldUnit.Time),
                new FieldSchema("charges", FieldKind.Object, required: false,
                    fields: new[]
                    {
                        // 依据（ADR-0021）：SkillValidationRules.ChargesMaxAtLeastOneRule（check
                        // "charges_max_invalid"）既有判断"charges.max 必须是 >= 1 的整数"——该判断记录
                        // 原文写"这条数值范围约束不是 FieldSchema.Fields 能表达的……登记层只保证 max
                        // 是 Int，0 与负数同样是合法 Int"，是在 Range 能力加入之前写的，现已过时，
                        // 补登为加载期 field_range；ChargesMaxAtLeastOneRule 保留（不重复移除，见
                        // LootSchemas 类型注释同款判断记录）。
                        new FieldSchema("max", FieldKind.Int, required: true, description: "最大充能次数，>= 1")
                            .WithRange(FieldRange.Range(min: 1)),
                        // recharge_time 的 <= 0 不登记 Range：CooldownTracker.StartCooldown 判断记录
                        // 明确 0/负数被引擎解读为"即时恢复"这一合法语义（不是数据错误），只由
                        // ChargesRechargeTimeZeroWarningRule 给 Warning 提醒复核，不能升级为阻断——
                        // 与本次消费方反馈里"interval<=0 应阻断"是相反的既有设计结论，不能一概而论。
                        new FieldSchema("recharge_time", FieldKind.Number, required: true, description: "单次充能所需时间").WithUnit(FieldUnit.Time),
                    },
                    description: "{max: Int, recharge_time: Number}"),
                new FieldSchema("action_cost", FieldKind.Number, required: false, description: "离散模式行动点消耗"),
                new FieldSchema("respects_gcd", FieldKind.Bool, required: true, description: "是否受公共冷却影响"),
                new FieldSchema("target_shape_ref", FieldKind.Id, required: true,
                    description: "指向 target.chain_def（本模块按此语义解析，见 README；消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "target.chain_def"),
                new FieldSchema("effects", FieldKind.Array, required: true, item: EffectsItemSchema,
                    description: "[{kind: String, params: Object}, ...]，kind 取值见 EffectKindNames"),
                new FieldSchema("interrupt_flags", FieldKind.Array, required: false,
                    item: new FieldSchema("<flag>", FieldKind.Enum, required: true, enumValues: InterruptFlagValues,
                        description: "打断当前读条/引导的触发条件，取值 movement|damage_taken|control 之一"),
                    description: "[movement|damage_taken|control, ...]"),
            }).WithOwnership(SchemaLayer.Rules, "skill").WithTimeScope(TimeScope.Combat);

        public static TableSchema AuraDef { get; } = new TableSchema(
            name: "skill.aura_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.aura_def.<name>"),
                new FieldSchema("duration", FieldKind.Number, required: false, description: "空表示永久直到被移除").WithUnit(FieldUnit.Time),
                new FieldSchema("max_stacks", FieldKind.Int, required: false, description: "缺省 1"),
                new FieldSchema("stack_category", FieldKind.Id, required: false, description: "叠加冲突检测用类别"),
                new FieldSchema("dispel_type", FieldKind.Id, required: false, description: "供 dispel 效果按类别筛选"),
                new FieldSchema("effects", FieldKind.Array, required: true, item: AuraEffectsItemSchema,
                    description: "[{kind: String, params: Object}, ...]，kind 取值见 06 第 3.3 节"),
            }).WithOwnership(SchemaLayer.Rules, "skill").WithTimeScope(TimeScope.Combat);

        public static TableSchema ProcDef { get; } = new TableSchema(
            name: "skill.proc_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.proc_def.<name>"),
                new FieldSchema("trigger_event", FieldKind.Id, required: true, description: "监听的事件 key"),
                new FieldSchema("condition", FieldKind.Expr, required: false, description: "触发条件"),
                new FieldSchema("trigger_skill", FieldKind.Id, required: true, description: "触发后释放的技能"),
                new FieldSchema("internal_cooldown", FieldKind.Number, required: false, description: "触发器自身冷却").WithUnit(FieldUnit.Time),
                // 依据（ADR-0021）：字段描述本身已明确"触发概率 0~1"；ProcHost.TryProc（core/ProcHost.cs
                // :165 "roll >= attachment.Def.ProcChance"）把它当均匀分布 [0,1) 随机数的比较阈值使用，
                // 越界值不会崩溃但会产生"必定触发/必定不触发"这类偏离概率语义的静默行为。
                new FieldSchema("proc_chance", FieldKind.Number, required: true, description: "触发概率 0~1")
                    .WithRange(FieldRange.Range(min: 0, max: 1)),
            }).WithOwnership(SchemaLayer.Rules, "skill").WithTimeScope(TimeScope.Combat);

        public static readonly string[] SpellModDimensionValues =
            { "cast_time", "cost", "cooldown", "crit_chance", "effect_value", "charges" };

        public static readonly string[] SpellModOpValues = { "flat", "pct" };

        public static TableSchema SpellModDef { get; } = new TableSchema(
            name: "skill.spell_mod_def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.spell_mod_def.<name>"),
                new FieldSchema("target_dimension", FieldKind.Enum, required: true, enumValues: SpellModDimensionValues,
                    description: "修正作用的维度，取值见 SpellModDimensionValues"),
                new FieldSchema("op", FieldKind.Enum, required: true, enumValues: SpellModOpValues,
                    description: "运算方式，flat 为加法、pct 为百分比"),
                new FieldSchema("value", FieldKind.Number, required: true, description: "修正数值"),
                new FieldSchema("affects", FieldKind.Object, required: false,
                    fields: new[]
                    {
                        new FieldSchema("schools", FieldKind.IdList, required: false, description: "限定生效的学派集合，缺省不限学派")
                            .WithFreeIds("学派（school）当前没有独立登记表，见本文件多处 school 字段判断记录"),
                        new FieldSchema("tags", FieldKind.IdList, required: false, description: "限定生效的技能标签集合，缺省不限标签")
                            .WithFreeIds("技能标签当前没有独立登记表，是内容作者自由声明的分类标签"),
                        // 判断记录（2026-09-10，测试回归修复）：本字段是"过滤条件"，不是必须存在的
                        // 内容关系——既有测试 SpellModTests.Affects_FiltersBySkillId 用从未注册进
                        // skill.def 的合成 id 驱动过滤匹配断言（不关心该 id 是否真的是一条已加载的
                        // 技能记录，只关心字符串是否命中）；若登记为 Reference(skill.def)，
                        // reference_integrity 会让这类既有用例的测试夹具在加载期就判为阻断错误，
                        // 与 schools/tags 两个同处 affects 对象的字段同一取舍，退回 WithFreeIds。
                        new FieldSchema("skill_ids", FieldKind.IdList, required: false,
                            description: "限定生效的技能集合，缺省不限技能")
                            .WithFreeIds("过滤条件而非内容关系；existing 测试用未注册进 skill.def 的合成 id 驱动匹配断言，见本字段判断记录"),
                    },
                    description: "{schools: [Id], tags: [Id], skill_ids: [Id]}"),
            }).WithOwnership(SchemaLayer.Rules, "skill");

        public static TableSchema Book { get; } = new TableSchema(
            name: "skill.book",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.book.<name>"),
                new FieldSchema("entries", FieldKind.Array, required: true,
                    item: new FieldSchema("<entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("level", FieldKind.Int, required: true, description: "习得该技能所需的等级"),
                        new FieldSchema("skill_id", FieldKind.Reference, required: true, referenceTable: "skill.def",
                            description: "习得的技能引用"),
                    }, description: "{level: Int, skill_id: Reference(skill.def)}，单条技能书条目"),
                    description: "[{level: Int, skill_id: Reference(skill.def)}, ...]"),
            }).WithOwnership(SchemaLayer.Rules, "skill");
    }
}
