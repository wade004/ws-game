using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;
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

        /// <summary>ADR-0026《技能位移的连续模式》：<c>move</c> 效果原语新增的 <c>motion</c> 字段
        /// 取值——与既有 <c>mode</c>（<see cref="MoveModeValues"/>）是正交的两个维度，见
        /// <c>EffectDispatcher.ApplyMove</c> 判断记录"命名，不复用既有 mode 字段"。</summary>
        public static readonly string[] MoveMotionValues = { "instant", "continuous" };

        /// <summary>ADR-0026：<c>motion: continuous</c> 时可选的阻挡后处理策略。</summary>
        public static readonly string[] DisplacementBlockingValues = { "stop", "revert" };
        public static readonly string[] ModStatOpValues = { "flat", "pct", "mult" };
        public static readonly string[] ControlFlagValues = { "no_move", "no_cast", "no_attack", "no_interact" };

        /// <summary>一个发现的交付缺口（2026-09-21，ADR-0060）：<c>skill.aura_def.polarity</c>
        /// 合法取值——该光环对承受者是有利（<c>beneficial</c>）还是有害（<c>harmful</c>）。固定
        /// 两值，不含"中性"：光环未声明本字段即代表"未声明"，不需要再用一个取值表达同一件事。</summary>
        public static readonly string[] AuraPolarityValues = { "beneficial", "harmful" };

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
        // T-N3-2（ADR-0031 决策 1；06 第 3.2 节 2026-09-14 修订段）：效果值契约
        // 效果值 = 基础值 + Σ(缩放属性最终值 × 系数) 的新写法——scaling 列表 + 可选 base_curve_ref。
        // 与旧单字段 scaling_stat/coefficient 是同一语义的两种表达，不是两套并行契约：scaling 非空
        // 时权威、旧字段忽略；scaling 缺失（含未经 1→2 迁移的旧数据）时回退旧字段（见
        // EffectDispatcher.ApplyDamageOrHeal 判断记录，硬性规则"禁止删除旧 scaling_stat 读取路径"）。
        // school_damage/heal（DamageOrHealParams）与 periodic_damage/periodic_heal
        // （PeriodicParamsCase）两处共用同一份字段实例（"登记一次、多处复用"，同 EffectsItemSchema
        // 既有惯例），因为两条效果路径本就共用同一条 EffectDispatcher.ApplyDamageOrHeal 结算逻辑
        // （P3-04 判断记录，见 AuraHost.FirePeriodic 把 entry.Params 原样转发进 EffectContext.Params）。
        // -----------------------------------------------------------------

        private static readonly FieldSchema ScalingEntrySchema = new FieldSchema(
            "<scaling_entry>", FieldKind.Object, required: true, fields: new[]
            {
                new FieldSchema("stat", FieldKind.Reference, required: true, referenceTable: "stat.definition",
                    description: "缩放属性引用（06 第 3.2 节 2026-09-14 修订段：通常为攻击强度或法术强度一类派生属性）"),
                new FieldSchema("coefficient", FieldKind.Number, required: true, description: "该缩放属性对应的系数"),
            }, description: "{stat: Reference(stat.definition), coefficient: Number}，效果值缩放条目之一（ADR-0031 决策 1）");

        private static readonly FieldSchema ScalingListField = new FieldSchema(
            "scaling", FieldKind.Array, required: false, item: ScalingEntrySchema,
            description: "缩放属性列表，效果值 = 基础值 + Σ(scaling[i].coefficient × 施法者 scaling[i].stat 最终值)，允许多条求和" +
                "（ADR-0031 决策 1；06 第 3.2 节 2026-09-14 修订段）；权威写法，非空时取代旧单字段 scaling_stat/coefficient" +
                "（见 EffectDispatcher.ApplyDamageOrHeal 判断记录、本表 scaling_stat 字段判断记录）");

        private static readonly FieldSchema BaseCurveRefField = new FieldSchema(
            "base_curve_ref", FieldKind.Reference, required: false, referenceTable: "skill.base_curve",
            description: "可选引用施法者等级到基础值的曲线，存在时取代 base_value（ADR-0031 决策 1\"基础值可选引用等级曲线\"，缺省为零）。" +
                "设计层裁定（2026-09-15）：采纳——目标表登记为 skill.base_curve（横轴施法者等级，" +
                "见 SkillSchemas.BaseCurve 类型注释）");

        // -----------------------------------------------------------------
        // school_damage / heal 共用参数（EffectDispatcher.ApplyDamageOrHeal 非 WeaponDamagePct 分支）：
        // base_value/coefficient 均可省（缺省取 EffectContext 传入的默认值），school 缺省取
        // skill.def.school（CastPipeline.ExecuteEffectsOnly：ParamsX.GetIdOpt(..,"school") ?? def.School），
        // scaling_stat 缺省不缩放。
        // -----------------------------------------------------------------
        private static readonly IReadOnlyList<FieldSchema> DamageOrHealParams = new[]
        {
            new FieldSchema("base_value", FieldKind.Number, required: false, description: "基础值，缺省 0；base_curve_ref 存在时被其取代（见该字段判断记录）"),
            new FieldSchema("coefficient", FieldKind.Number, required: false, description: "缩放系数，缺省 0；scaling 列表存在时不参与效果值组装，只随 EffectContext 原样转发（见 EffectDispatcher.ApplyDamageOrHeal 判断记录）")
                // 消费方反馈第 46 条（04 第 3.4 节勘误"字段废弃元数据"）：与 scaling_stat 是同一语义
                // 的旧写法一半（见上方 Description、本文件顶部判断记录），升级指南附录 C 未单列本字段，
                // 但反馈原文明确把 scaling_stat/coefficient 一起列为待打标对象，一并登记。
                .WithDeprecated("1.33.0", "scaling"),
            new FieldSchema("school", FieldKind.Id, required: false, description: "缺省取 skill.def.school"),
            new FieldSchema("scaling_stat", FieldKind.Reference, required: false, referenceTable: "stat.definition",
                description: "旧单字段缩放属性写法，缺省不缩放（判断记录：stat.definition 属 L1，本模块已依赖 StatBlock 程序集，登记为 Reference 不违反分层）；" +
                    "T-N3-2 起 scaling 列表为权威写法，本字段与 coefficient 搭配的旧读取路径保留兼容（硬性规则：禁止删除），scaling 列表非空时不再读取本字段")
                .WithDeprecated("1.33.0", "scaling"),
            ScalingListField,
            BaseCurveRefField,
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
                // ADR-0026《技能位移的连续模式》：补 motion/speed/duration/blocking/sample_step 五个
                // 字段——motion 缺省 instant 时行为与登记扩容之前逐字节一致（EffectDispatcher.
                // ApplyMove 未改动的既有 switch 分支）；后四项只在 motion=continuous 时被读取（同
                // 上面判断记录"四个子字段按 mode 分别只使用其中一部分"的既有登记风格，本次同样不再
                // 拆二级 Variants，扁平超集登记即可，见该判断记录"避免过度设计"）。speed/duration/
                // sample_step 三者都是"若声明则必须为正数"（消费方反馈 2026-09-10"技能效果参数数值
                // 范围校验改进建议"、ADR-0021 同一口径），speed/duration 至少声明一个才能算出有效
                // 速度（业务判断，登记层不表达"二选一必填"，同上面 modify_cooldown 的 skill_id/
                // category 判断记录）——两者皆缺或皆非正时 EffectDispatcher.ApplyContinuousMove
                // 按 no-op 处理，不抛异常。
                [EffectKindNames.ToText(EffectKind.Move)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("mode", FieldKind.Enum, required: false, enumValues: MoveModeValues, description: "缺省 charge"),
                    new FieldSchema("point", FieldKind.Vec2, required: false, description: "leap 目标点"),
                    new FieldSchema("distance", FieldKind.Number, required: false, description: "knockback 距离，缺省 5"),
                    new FieldSchema("stop_distance", FieldKind.Number, required: false, description: "charge 停止距离，缺省 1.0"),
                    new FieldSchema("motion", FieldKind.Enum, required: false, enumValues: MoveMotionValues,
                        description: "ADR-0026：缺省 instant（现行三种位移的既有语义）；continuous 转交 IControlledDisplacementSink 逐 tick 推进"),
                    new FieldSchema("speed", FieldKind.Number, required: false, description: "ADR-0026：仅 motion=continuous 生效，每秒位移距离，若声明必须 > 0")
                        .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                    new FieldSchema("duration", FieldKind.Number, required: false, description: "ADR-0026：仅 motion=continuous 且未声明 speed 时生效，按 |target-origin|/duration 换算速度，若声明必须 > 0")
                        .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                    new FieldSchema("blocking", FieldKind.Enum, required: false, enumValues: DisplacementBlockingValues,
                        description: "ADR-0026：仅 motion=continuous 生效，缺省 stop（停在阻挡前最后可通行采样点），revert 回到起点"),
                    new FieldSchema("sample_step", FieldKind.Number, required: false, description: "ADR-0026：仅 motion=continuous 生效，路径采样步长，若声明必须 > 0，缺省取 MovementOptions.DefaultDisplacementSampleStep")
                        .WithRange(FieldRange.Range(min: 0, minExclusive: true)),
                }, "按 mode（charge|leap|knockback）位移施法者或目标；motion=instant（缺省）只写最终逻辑位置，不做寻路/碰撞，motion=continuous 见 ADR-0026"),

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

                // script：06 第 3.2 节"钩子 id"，对应 found.hook（Core.Foundation.HookRegistry.
                // FoundHookSchema.Table，L0，本模块 L2，跨层）。消费方反馈第 39 条（2026-09-13）核实：
                // found.hook 早在收边 I1 就已完成 TableSchema 登记（04 变更记录 2026-09-05 行"仍无
                // 对应实现级 schema 登记"为当时快照，见该行同日追加注记），本字段判断记录此前描述的
                // "found.hook 当前无实现级 schema 登记"已经过时，与 dialog 两处（actions[].script.ref/
                // performance_hook_ref）、encounter.def.on_enter_hook 是同一根因的四处症状，同批修复。
                // 仍不升级为 FieldKind.Reference（保持 Id + SoftReferenceTable）：found.hook 是运行时
                // 按 id 分发的挂载点注册表，不希望把"是否存在"变成加载期硬阻断。
                [EffectKindNames.ToText(EffectKind.Script)] = ParamsCase(required: true, new[]
                {
                    new FieldSchema("hook_id", FieldKind.Id, required: false,
                        description: "found.hook 钩子 id（消费方反馈第 39 条：登记为软引用，仅供内容工具补全/跳转）；required 收窄为 false（同 summon.creature_template 判断记录，本模块无落地实现）")
                        .WithSoftReference(table: "found.hook"),
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
                    // T-N3-6（ADR-0031 决策 8；06 第 3.3 节 2026-09-14 修订段）：控制类别，免疫按类别
                    // 判——可选（不升 skill.aura_def schema 版本，无需迁移，见 AuraHost.ApplyStaticEffects
                    // 判断记录"缺省视为未分类，退回旧的按标志位静态免疫判定"）。取值集合与
                    // creature.tier_definition.control_immune_categories 共用同一份
                    // Core.Rules.Common.ControlCategoryValues，避免两处漂移。
                    new FieldSchema("category", FieldKind.Enum, required: false, enumValues: ControlCategoryValues.All,
                        description: "控制类别（stun|root|silence|disarm|fear|polymorph），免疫按类别判；缺省（未分类）时静态免疫退回旧的按标志位判定（IStaticImmunityProvider.GetControlImmunity），见 AuraHost.ApplyStaticEffects 判断记录"),
                }, "对目标施加控制标志位，见 flags/category 子字段"),

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
            new FieldSchema("coefficient", FieldKind.Number, required: false, description: "缺省 0")
                // 消费方反馈第 46 条：同 DamageOrHealParams.coefficient 判断记录，周期效果分支的
                // coefficient/scaling_stat 是同一套旧写法，一并打标。
                .WithDeprecated("1.33.0", "scaling"),
            new FieldSchema("school", FieldKind.Id, required: true, description: "无缺省（见判断记录）"),
            // P3-04 根治（外部审计 audit-c9ff301-20260909）：AuraHost.FirePeriodic 组装的
            // EffectContext 与 school_damage/heal 走的是同一条 EffectDispatcher.ApplyDamageOrHeal
            // 结算路径（见该方法 scaling_stat = ParamsX.GetIdOpt(context.Params, "scaling_stat")
            // 判断记录），运行期确实会读取并消费 params.scaling_stat 对周期效果的缩放贡献——此前只
            // 在非周期的 DamageOrHealParams（本文件 :67）登记了这个字段，periodic 变体漏登记，导致
            // 内容作者/编辑器看不到这个受支持的可选参数（schema 漏项，不是运行期行为缺陷：未登记
            // 字段不报错，不影响已经这样填写的数据）。
            new FieldSchema("scaling_stat", FieldKind.Reference, required: false, referenceTable: "stat.definition",
                description: "旧单字段缩放属性写法，缺省不缩放（同 DamageOrHealParams.scaling_stat，两条效果路径共用同一份 EffectDispatcher.ApplyDamageOrHeal 结算逻辑）；" +
                    "T-N3-2 起 scaling 列表为权威写法，见该字段判断记录")
                .WithDeprecated("1.33.0", "scaling"),
            // T-N3-2：与 DamageOrHealParams 共用同一份字段实例（"登记一次、多处复用"），见本文件
            // ScalingListField/BaseCurveRefField 顶部判断记录。
            ScalingListField,
            BaseCurveRefField,
        }, description);

        private static IReadOnlyList<FieldSchema> ParamsCase(bool required, IReadOnlyList<FieldSchema> paramFields, string description) => new[]
        {
            new FieldSchema("params", FieldKind.Object, required: required, fields: paramFields, description: description),
        };

        // -----------------------------------------------------------------
        // T-N3-2：skill.def/skill.aura_def 的 1→2 迁移——effects[] 里 school_damage/heal（skill.def）
        // 或 periodic_damage/periodic_heal（skill.aura_def）四种取值，若 params 声明了旧单字段
        // scaling_stat 且尚未声明新列表 scaling，追加 scaling: [{stat: <scaling_stat 的值>,
        // coefficient: <coefficient 缺省 0>}]；旧字段（scaling_stat/coefficient）原样保留，不删除
        // （硬性规则"禁止删除旧 scaling_stat 读取路径"）。已声明 scaling 的条目、其余效果原语（其
        // params 不含这套字段）原样透传，不做任何改动——只做结构转换，惯例同
        // CurveSchema.MigrateBreakpointsFieldNames（本类型不做任何校验，形状问题留给迁移后的
        // 字段级校验报告）。
        // -----------------------------------------------------------------

        private static readonly string[] ScalingMigrationEffectKinds =
        {
            EffectKindNames.ToText(EffectKind.SchoolDamage),
            EffectKindNames.ToText(EffectKind.Heal),
        };

        private static readonly string[] ScalingMigrationAuraEffectKinds =
        {
            AuraEffectKindNames.ToText(AuraEffectKind.PeriodicDamage),
            AuraEffectKindNames.ToText(AuraEffectKind.PeriodicHeal),
        };

        private static JsonObject MigrateEffectsScalingStatToList(JsonObject row, string effectsField, IReadOnlyCollection<string> kinds)
        {
            var kindSet = new HashSet<string>(kinds, StringComparer.Ordinal);
            var builder = new JsonObjectBuilder();
            foreach (var entry in row)
            {
                if (entry.Key == effectsField && entry.Value is JsonArray effects)
                {
                    var migrated = new JsonValue[effects.Count];
                    for (var i = 0; i < effects.Count; i++)
                    {
                        migrated[i] = effects[i] is JsonObject effectObj ? MigrateEffectEntryScaling(effectObj, kindSet) : effects[i];
                    }
                    builder.Add(entry.Key, new JsonArray(migrated));
                }
                else
                {
                    builder.Add(entry.Key, entry.Value);
                }
            }
            return builder.Build();
        }

        private static JsonObject MigrateEffectEntryScaling(JsonObject effect, HashSet<string> kinds)
        {
            if (!effect.TryGetValue("kind", out var kindValue) || !(kindValue is JsonString kindStr) || !kinds.Contains(kindStr.Value))
            {
                return effect;
            }

            if (!effect.TryGetValue("params", out var paramsValue) || !(paramsValue is JsonObject paramsObj))
            {
                return effect;
            }

            if (paramsObj.ContainsKey("scaling") || !paramsObj.TryGetValue("scaling_stat", out var statValue) || !(statValue is JsonString statStr))
            {
                return effect;
            }

            var coefficient = paramsObj.TryGetValue("coefficient", out var coeffValue) && coeffValue is JsonNumber coeffNum ? coeffNum.Value : 0;

            var scalingEntry = new JsonObjectBuilder()
                .Add("stat", new JsonString(statStr.Value))
                .Add("coefficient", new JsonNumber(coefficient))
                .Build();

            var newParamsBuilder = new JsonObjectBuilder();
            foreach (var kv in paramsObj)
            {
                newParamsBuilder.Add(kv.Key, kv.Value);
            }
            newParamsBuilder.Add("scaling", new JsonArray(new JsonValue[] { scalingEntry }));
            var newParams = newParamsBuilder.Build();

            var newEffectBuilder = new JsonObjectBuilder();
            foreach (var kv in effect)
            {
                newEffectBuilder.Add(kv.Key, kv.Key == "params" ? newParams : kv.Value);
            }
            return newEffectBuilder.Build();
        }

        /// <summary><c>skill.base_curve</c>：施法者等级到效果基础值的曲线（分阶段落地计划 T-N3-2；
        /// ADR-0031 决策 1"基础值可选引用等级曲线（base_curve_ref），默认为零"；06 第 3.2 节
        /// 2026-09-14 修订段）。
        /// <para>
        /// 设计层裁定（2026-09-15）：采纳——06 第 3.2 节与 ADR-0031 决策 1 均只说"基础值可选引用
        /// 等级曲线（base_curve_ref）"，未指明这张曲线表的表名，目标表登记为 <c>skill.base_curve</c>，
        /// 横轴取施法者等级（<see cref="CurveAxis.Level"/>，04 第 3.6 节通用断点表形态），与
        /// <c>item.armor_curve</c> 等新表同一惯例——新表，直接按通用断点表形态登记，不需要迁移，
        /// 不影响已落地的"scaling 列表求和"这一主线契约（决策 1 的另一半，本任务已明确落地，不依赖
        /// 本表是否存在）。</para></summary>
        public static TableSchema BaseCurve { get; } = new TableSchema(
            name: "skill.base_curve",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.base_curve.<name>"),
                CurveSchema.BreakpointsField("entries", CurveAxis.Level, required: true,
                    description: "断点表 [{x: 施法者等级(Int), y: 基础值(Number)}]，按 x 线性插值、越界夹取到端点" +
                        "（04 第 3.6 节通用曲线形态；ADR-0031 决策 1）",
                    xDescription: "采样点对应的施法者等级",
                    yDescription: "该等级对应的效果基础值，供 school_damage/heal/periodic_damage/periodic_heal 的 base_curve_ref 引用"),
            }).WithOwnership(SchemaLayer.Rules, "skill");

        /// <summary><c>skill.budget_rule</c>：技能预算规则表的最小骨架（分阶段落地计划 T-N3-3；
        /// [ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2"技能预算规则
        /// 表与警告级校验"、决策 10"一拍常数只是记账单位……施放时间当量 = max(动作时长, 一拍
        /// 常数)"；06 第 3.10 节字段表）。
        /// <para>
        /// 判断记录（分两步落地，不是自行发明契约）：06 第 3.10 节"数据表 skill.budget_rule（04）：
        /// 一拍常数、施放时间当量规则、冷却溢价/范围折价/消耗溢价三条曲线、带宽、硬上限、控制类别
        /// 权重、玩家档/怪物档"给出的是完整表，落地改动点清单第 14 节 N3 任务表把它整体排到
        /// T-N3-9；但 T-N3-3（`weapon_damage_pct` 改接"秒伤 × 一拍常数"）在 T-N3-9 之前就需要运行期
        /// 读到"一拍常数"，任务书就此裁定"本任务先登记最小骨架（只含 id 与 beat_seconds），其余
        /// 字段先不登记"——本表只登记这两个字段，<see cref="Def"/>/<see cref="AuraDef"/> 等其余五张
        /// 表旁的完整字段集（施放时间当量规则、三条溢价/折价曲线、带宽、硬上限、控制类别权重、
        /// 玩家档/怪物档）留给 T-N3-9 在同一 <c>TableSchema</c> 上继续补登记（新增字段，非破坏性，
        /// 不需要 schema 版本递增）。
        /// </para>
        /// <para>
        /// 设计层裁定（2026-09-15）：采纳——06 第 3.10 节原文只有"一拍常数只是记账单位（如半秒）"
        /// 这句散文描述，字段名按落地方案措辞、参照 <c>cast_time</c>/<c>cooldown_duration</c>
        /// 既有时间字段的英文命名惯例定为 <c>beat_seconds</c>，全部读取点
        /// （<see cref="Core.Rules.Skill.SkillDefCache.TryGetBeatSeconds"/>、
        /// <see cref="Core.Rules.Skill.EffectDispatcher"/> 的 <c>ResolveBeatSeconds</c>）与本表
        /// 字段名一致。
        /// </para>
        /// <para>
        /// 设计层裁定（2026-09-15）：采纳——<see cref="FieldUnit.Time"/>/<see cref="TimeScope.Combat"/>
        /// 只是元数据声明，不接入运行期模式换算。<c>beat_seconds</c> 语义上是一段时长，标记
        /// <see cref="FieldUnit.Time"/> 满足 <c>SchemaAudit</c>"time_scope_declared"检查（04 第
        /// 3.4 节），表按技能同惯例声明 <see cref="TimeScope.Combat"/>；<c>cast_time</c>/
        /// <c>cooldown_duration</c> 那一类字段在模式切换（<c>TimeModelRescaledEvent</c>）时由
        /// <c>CooldownTracker</c>/<c>AuraHost</c> 内部的换算系数实时折算，但 <c>beat_seconds</c>
        /// 按秒表达即为权威值，不接入同一套连续/离散模式换算系数——`weapon_damage_pct` 这一在
        /// "预算记账"之外于结算路径直接消费该常数的消费者同样按秒表达值直接使用，离散模式下的
        /// 换算核对留给阶段 N6 仿真接入锚点时统一核对，不在效果层做。
        /// </para>
        /// </summary>
        /// <summary>控制类别权重子结构（T-N3-9；06 第 3.10 节"控制价值 = 时长 × 目标数 × 控制类别
        /// 权重"；<see cref="ControlCategoryValues.All"/> 六值）。
        /// <para>
        /// 判断记录（六个具名可选字段而不是登记为 <c>Map</c>）：<c>control_category_weights</c> 的键
        /// 集合是 <see cref="ControlCategoryValues.All"/> 固定六值（不像 <c>base_stats</c> 那样键随
        /// <c>stat.definition</c> 内容表增减），登记为逐个具名字段可以给每个权重单独写 <c>description</c>
        /// 与范围约束，比 <c>MapSchema.ReferenceKeyTable</c> 更贴合"固定小枚举"这一形状；缺省全部
        /// 为 1.0（见 <see cref="Core.Rules.Skill.SkillBudgetAnalyzer"/> 判断记录"未登记的控制类别
        /// 权重取中性值 1.0，不放大也不折价"）。
        /// </para></summary>
        private static readonly FieldSchema[] ControlCategoryWeightFields =
        {
            new FieldSchema("stun", FieldKind.Number, required: false, description: "眩晕类控制的价值权重，缺省 1.0")
                .WithRange(FieldRange.Range(min: 0)),
            new FieldSchema("root", FieldKind.Number, required: false, description: "定身类控制的价值权重，缺省 1.0")
                .WithRange(FieldRange.Range(min: 0)),
            new FieldSchema("silence", FieldKind.Number, required: false, description: "沉默类控制的价值权重，缺省 1.0")
                .WithRange(FieldRange.Range(min: 0)),
            new FieldSchema("disarm", FieldKind.Number, required: false, description: "缴械类控制的价值权重，缺省 1.0")
                .WithRange(FieldRange.Range(min: 0)),
            new FieldSchema("fear", FieldKind.Number, required: false, description: "恐惧类控制的价值权重，缺省 1.0")
                .WithRange(FieldRange.Range(min: 0)),
            new FieldSchema("polymorph", FieldKind.Number, required: false, description: "变形类控制的价值权重，缺省 1.0")
                .WithRange(FieldRange.Range(min: 0)),
        };

        /// <summary>
        /// T-N3-9（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2；06
        /// 第 3.10 节字段表原文；04 第 5 节数值类校验项分级表）：在 T-N3-3 登记的最小骨架
        /// （<c>id</c>/<c>beat_seconds</c>）基础上补齐 06 原文列出的其余字段——冷却溢价/范围折价/
        /// 消耗溢价三条曲线、带宽、硬上限、控制类别权重。schema 版本不递增（新增字段，非破坏性，同
        /// <see cref="BaseCurve"/> 类型判断记录"新增字段，非破坏性，不需要 schema 版本递增"惯例）。
        /// <para>
        /// 设计层裁定（2026-09-15）：采纳，见 <see cref="Core.Rules.Skill.SkillBudgetAnalyzer"/>
        /// 判断记录。06 原文"带宽、硬上限……玩家档/怪物档"把"玩家档/怪物档"与"带宽""硬上限"并列写在
        /// 同一句里描述 <c>skill.budget_rule</c> 表结构，但紧接着的独立一句又明确"玩家档/怪物档由
        /// 反向引用决定"——裁定采纳后一句：反向引用决定"用哪一组带宽/硬上限"，玩家档/怪物档本身不是
        /// 本表登记的字段——怪物技能的合理超模幅度（数值设计 02"Boss 秒杀技本来就是几十倍超模"）与
        /// 玩家技能的手滑容差不可能共用同一个数字，故本表按玩家/怪物两档分别登记
        /// <c>player_bandwidth</c>/<c>monster_bandwidth</c>、<c>player_hard_cap</c>/
        /// <c>monster_hard_cap</c> 四个字段（而不是单一 <c>bandwidth</c>/<c>hard_cap</c>），
        /// <c>SkillBudgetAnalyzer</c> 按 <c>SkillDefCache</c> 反查出的档位选择对应一组。
        /// </para>
        /// <para>
        /// 设计层裁定（2026-09-15）：采纳——06 原文"施放时间当量规则"未给出独立字段名，T-N3-3 已把
        /// "一拍常数"本身登记为 <c>beat_seconds</c>；"周期效果按总持续时间乘折价"（06 第 3.10 节
        /// 公式行注释）里的"折价"系数字段名登记为 <c>periodic_time_discount</c>（缺省 1.0，见
        /// <see cref="Core.Rules.Skill.SkillBudgetAnalyzer"/> 判断记录"周期效果的施放时间当量"）。
        /// </para>
        /// </summary>
        public static TableSchema BudgetRule { get; } = new TableSchema(
            name: "skill.budget_rule",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.budget_rule.<name>"),
                new FieldSchema("beat_seconds", FieldKind.Number, required: false,
                        description: "一拍常数：06 第 3.10 节预算公式的记账单位（施放时间当量 = " +
                            "max(动作时长, 一拍常数)），T-N3-3 起同时是 weapon_damage_pct 原语运行期" +
                            "公式\"武器秒伤 × 一拍常数 × 百分比\"的乘数（ADR-0031 决策 1/2、06 第 3.2 " +
                            "节 2026-09-14 修订段）；缺省 1.0（记录不存在/表未注册时同一缺省值，见 " +
                            "EffectDispatcher.ResolveBeatSeconds 判断记录）。设计层裁定（深度复审 " +
                            "C-S3，2026-09-16）：本字段是预算记账常数，不是按 tick 执行、参与离散步" +
                            "推进/衰减的\"持续时间状态\"，离散模式下不要求取整数——不纳入" +
                            "TimeFieldConsistencyRule 的离散整数一致性校验（同 cast_time/" +
                            "cooldown_duration 等真正参与离散步计数的时间字段区分对待），06 文档给出的" +
                            "示例值（如半秒）在纯离散模式游戏里同样合法")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true))
                    .WithUnit(FieldUnit.Time),
                new FieldSchema("periodic_time_discount", FieldKind.Number, required: false,
                        description: "T-N3-9：周期效果（apply_aura 引用的光环含 periodic_damage/" +
                            "periodic_heal）的施放时间当量折价系数——T = 光环总持续时间 × 本字段（06 " +
                            "第 3.10 节公式行注释\"周期效果按总持续时间乘折价\"）；缺省 1.0（不折价，" +
                            "字段名为本任务临时判定，见本表类型判断记录\"施放时间当量规则\"）。范围 (0,1]。" +
                            "设计层裁定（深度复审 C-S3，2026-09-16）：本字段同 beat_seconds，是预算记账" +
                            "常数（无量纲折价系数，不是时长），不纳入 TimeFieldConsistencyRule 的离散" +
                            "整数一致性校验")
                    .WithRange(FieldRange.Range(min: 0, minExclusive: true, max: 1)),
                CurveSchema.BreakpointsField("cooldown_premium_curve", CurveAxis.Value, required: false,
                    description: "T-N3-9：冷却溢价曲线，横轴为\"冷却 ÷ 施放时间当量 T\"的比值，纵轴为" +
                        "溢价倍数（06 第 3.10 节公式\"技能预算 = DPS(L) × T × 冷却溢价(冷却÷T) × …\"）；" +
                        "字段缺失/空断点表时 SkillBudgetAnalyzer 取中性倍数 1.0（不是 PiecewiseCurve 的" +
                        "空表恒 0 语义，见该类型判断记录）",
                    xDescription: "冷却 ÷ 施放时间当量 T",
                    yDescription: "冷却溢价倍数",
                    yRange: FieldRange.Range(min: 0)),
                // 判断记录（y 登记为"折价除数"而不是直接的折价倍数）：范围折价语义上应随
                // max_targets 增大而递减（同一份预算铺到更多目标，单目标价值应更低——数值设计 02
                // "群体折价"），但 04 第 5 节 curve_monotonic_finite 对全部断点表形态字段统一生效、
                // 要求纵轴不递减（不允许递减曲线）。本任务临时判定：登记为随 max_targets 增大而
                // 递增的"折价除数"，SkillBudgetAnalyzer 运行期按 1.0 / 本曲线取值 换算成实际相乘的
                // 折价倍数（见该类型判断记录"范围折价"），曲线本身满足 curve_monotonic_finite，最终
                // 生效的折价倍数仍随 max_targets 增大而递减——设计层裁定（2026-09-15）：采纳，登记为
                // 随目标数递增的除数，不另立不受 curve_monotonic_finite 约束的曲线形态。
                CurveSchema.BreakpointsField("range_discount_curve", CurveAxis.Value, required: false,
                    description: "T-N3-9：范围折价除数曲线，横轴为目标形状 max_targets，纵轴为折价除数" +
                        "（06 第 3.7/3.10 节\"范围折价曲线以 max_targets 为输入\"；y 登记为除数而非直接" +
                        "倍数，见本字段判断记录）；SkillBudgetAnalyzer 按 1.0/本曲线值 换算实际倍数；" +
                        "字段缺失/空断点表/技能目标形状未声明 max_targets（无上限）时取中性倍数 1.0",
                    xDescription: "目标形状 max_targets（无上限技能不参与本曲线，见 SkillBudgetAnalyzer 判断记录）",
                    yDescription: "范围折价除数（>= max_targets 越大取值越大，运行期按 1.0/本值 换算实际折价倍数）",
                    yRange: FieldRange.Range(min: 0, minExclusive: true)),
                CurveSchema.BreakpointsField("cost_premium_curve", CurveAxis.Value, required: false,
                    description: "T-N3-9：消耗溢价曲线，横轴为\"消耗 ÷ 期望回复率\"的比值（期望回复率" +
                        "取 cost[0].power_type 指向的 arch.power_type.regen_in_combat，见 " +
                        "SkillBudgetAnalyzer 判断记录），纵轴为溢价倍数；字段缺失/空断点表/技能无 " +
                        "cost 时取中性倍数 1.0",
                    xDescription: "消耗 ÷ 期望回复率",
                    yDescription: "消耗溢价倍数",
                    yRange: FieldRange.Range(min: 0)),
                new FieldSchema("player_bandwidth", FieldKind.Number, required: false,
                        description: "T-N3-9：玩家档技能预算带宽——比值在 [1-本值, 1+本值] 内视为" +
                            "\"带宽内通过\"（06 第 3.10 节；玩家/怪物分档见本表类型判断记录）；缺省 0.2" +
                            "（±20%，本任务临时判定的默认值，见 SkillBudgetAnalyzer 判断记录\"只检查" +
                            "超出上界\"）。范围 >= 0")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("monster_bandwidth", FieldKind.Number, required: false,
                        description: "T-N3-9：怪物档技能预算带宽，语义同 player_bandwidth；缺省 5.0" +
                            "（怪物技能合理超模幅度远大于玩家技能，见本表类型判断记录）。范围 >= 0")
                    .WithRange(FieldRange.Range(min: 0)),
                new FieldSchema("player_hard_cap", FieldKind.Number, required: false,
                        description: "T-N3-9：玩家档技能预算硬上限——比值超过本值且技能未填 " +
                            "skill.def.budget_note 为阻断（04 第 5 节\"技能预算硬上限\"）；缺省 3.0" +
                            "（本任务临时判定的默认值）。范围 > 1")
                    .WithRange(FieldRange.Range(min: 1, minExclusive: true)),
                new FieldSchema("monster_hard_cap", FieldKind.Number, required: false,
                        description: "T-N3-9：怪物档技能预算硬上限，语义同 player_hard_cap；缺省 50.0" +
                            "（本任务临时判定的默认值，见本表类型判断记录）。范围 > 1")
                    .WithRange(FieldRange.Range(min: 1, minExclusive: true)),
                new FieldSchema("control_category_weights", FieldKind.Object, required: false,
                    fields: ControlCategoryWeightFields,
                    description: "T-N3-9：控制类别权重（06 第 3.10 节\"控制价值 = 时长 × 目标数 × " +
                        "控制类别权重\"），六个具名可选字段，见 ControlCategoryWeightFields 判断记录；" +
                        "整体缺省全部按 1.0"),
            }).WithOwnership(SchemaLayer.Rules, "skill").WithTimeScope(TimeScope.Combat);

        public static TableSchema Def { get; } = new TableSchema(
            name: "skill.def",
            primaryKey: "id",
            currentSchemaVersion: 2,
            migrations: new[]
            {
                new TableMigration(1, 2, row => MigrateEffectsScalingStatToList(row, "effects", ScalingMigrationEffectKinds)),
            },
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.<name>"),
                // 消费方反馈第 3 条（2026-09-20，ADR-0048）：技能名称此前无字段可登记，表现层只能
                // 硬编码/直接显示 id 短串。命名与字段类型沿用 ADR-0043 dialog.gossip_menu.greeting_key
                // 确立的 TextKey 惯例（"语义名 + _key" 后缀、FieldKind.TextKey、required: false，
                // 缺省时表现层隐藏不渲染、不回退占位文案），不新造一套命名——见 SkillDef.NameKey 判断
                // 记录。纯新增可选字段，不提 schema_version，既有全部 skill.def 行零改动仍合法。
                new FieldSchema("name_key", FieldKind.TextKey, required: false, description: "技能名称文本键，缺省时表现层不渲染名称（不回退占位文案），见 ADR-0048"),
                new FieldSchema("school", FieldKind.Id, required: true, description: "学派"),
                new FieldSchema("kind", FieldKind.Enum, required: true, enumValues: SkillKindValues,
                    description: "active|passive"),
                new FieldSchema("range", FieldKind.Number, required: true, description: "射程，0 表示无限制/作用于自身"),
                new FieldSchema("tags", FieldKind.IdList, required: false, description: "标签集合")
                    .WithFreeIds("技能标签当前没有独立登记表，是内容作者自由声明的分类标签"),
                // 修订（2026-09-14，ADR-0031 决策 10；06 第 3.1 节 2026-09-14 修订段）：cast_time
                // 同时承担"动作时长"语义——期间不能开始下一个技能，效果在动作结束时生效（命中帧
                // 同步沿 ADR-0017）；挥剑半秒与火球读条两秒本质相同，表现层按阈值决定显示读条条还
                // 是播放动画，逻辑层不区分。急速是否缩短动作时长及其下限为策略配置项（默认不受
                // 影响），替代此前"公共冷却是否受急速影响"。数值总纲"一拍常数"只是技能预算公式
                // （3.10 节）的记账单位（施放时间当量 = max(动作时长, 一拍常数)），运行期不存在
                // 任何锁——即 cast_time: 0 的瞬发技能不因这个记账常数而占用任何实际节拍窗口，"是否
                // 受节拍锁约束"完全由 respects_gcd 决定（见该字段本次修订的描述）。
                new FieldSchema("cast_time", FieldKind.Number, required: true,
                    description: "读条时间，0 表示瞬发；同时承担\"动作时长\"语义——期间不能开始下一个技能，效果在动作结束时生效，逻辑层不区分读条与快速挥砍（ADR-0031 决策 10）")
                    .WithUnit(FieldUnit.Time),
                new FieldSchema("channel_time", FieldKind.Number, required: false, description: "引导时长，与 cast_time 互斥").WithUnit(FieldUnit.Time),
                // 修订（2026-09-14，ADR-0031 决策 3；06 第 3.1 节 2026-09-14 修订段）：只有固定值
                // 写法（不做资源上限百分比、引导每秒等模式）；引导技能开始时一次性扣；非战斗技能
                // （开锁、传送、坐骑、造物等）消耗为零，约束改由 use_condition、动作时长与冷却承担；
                // 免费/标准/大招三档定价是游戏层配平建议（数值总纲第 4.5 节），不是本字段的约束。
                new FieldSchema("cost", FieldKind.Array, required: false,
                    item: new FieldSchema("<cost_entry>", FieldKind.Object, required: true, fields: new[]
                    {
                        new FieldSchema("power_type", FieldKind.Id, required: true, description: "消耗的资源类型引用，多数取值是 arch.power_type 的记录 id，也允许内置特例（同 energize.power_type 判断记录；消费方反馈第 30 条：登记为软引用）")
                            .WithSoftReference(table: "arch.power_type"),
                        new FieldSchema("amount", FieldKind.Number, required: true, description: "消耗数量，只有固定值写法，不随等级成长"),
                    }, description: "{power_type: Id, amount: Number}，单条消耗资源条目"),
                    description: "消耗资源列表，[{power_type: Id, amount: Number}, ...]；引导技能施法开始时一次性扣，非战斗技能消耗为零（ADR-0031 决策 3）"),
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
                // ADR-0027《地面坐标施法请求》：是否允许经 ISkillHost.CastSkillAtGround 以地面坐标为
                // 落点施放，缺省 false（见 SkillDef.AllowGroundTarget 判断记录，保持既有技能行为不变）。
                new FieldSchema("ground_target", FieldKind.Bool, required: false,
                    description: "是否允许地面坐标施法请求（ISkillHost.CastSkillAtGround），缺省 false"),
                // 修订（2026-09-14，ADR-0031 决策 10；06 第 3.1 节 2026-09-14 修订段）：字段名
                // 保留，语义由"是否受公共冷却影响"扩展为"是否受节拍锁约束"——开公共冷却的游戏里
                // 节拍锁是公共冷却，关公共冷却（13 第 8 节口味配置项默认关闭）的游戏里节拍锁是当前
                // 动作时长（cast_time）；声明为 false 的反应类技能（打断、格挡、保命）可在他技能
                // 动作中插入。
                new FieldSchema("respects_gcd", FieldKind.Bool, required: true,
                    description: "是否受节拍锁约束——开公共冷却时节拍锁是公共冷却，关公共冷却（默认）时节拍锁是当前动作时长（cast_time）；false 声明反应类技能（打断/格挡/保命），可在他技能动作中插入（ADR-0031 决策 10）"),
                new FieldSchema("target_shape_ref", FieldKind.Id, required: true,
                    description: "指向 target.chain_def（本模块按此语义解析，见 README；消费方反馈第 29 条：登记为软引用，仅供内容工具补全/跳转）")
                    .WithSoftReference(table: "target.chain_def"),
                new FieldSchema("effects", FieldKind.Array, required: true, item: EffectsItemSchema,
                    description: "[{kind: String, params: Object}, ...]，kind 取值见 EffectKindNames"),
                new FieldSchema("interrupt_flags", FieldKind.Array, required: false,
                    item: new FieldSchema("<flag>", FieldKind.Enum, required: true, enumValues: InterruptFlagValues,
                        description: "打断当前读条/引导的触发条件，取值 movement|damage_taken|control 之一"),
                    description: "[movement|damage_taken|control, ...]"),
                // 新增（2026-09-14，ADR-0031 决策 9；06 第 3.1 节 2026-09-14 修订段）：宿主为施法者
                // 上下文，Expr 文本，self/combat/target 分组（04 第 6.2 节"宿主引用分组"）；施法管线
                // 在"存活与状态"步骤之后插入"使用条件"步骤检查（T-N3-4 落地），为假时返回失败原因码
                // ConditionNotMet，就绪查询（getSkillReadiness）同步反映；"脱战才能用""仅限战斗中"
                // "目标是物件""已习得骑术"均用它表达。登记为 FieldKind.Expr 后自动获得
                // DataRegistry 内建 expr_parsable 校验，不需要额外注册。
                new FieldSchema("use_condition", FieldKind.Expr, required: false,
                    description: "使用条件（宿主为施法者上下文，self/combat/target 分组，见 04 第 6.2 节）；为假时施法返回 ConditionNotMet（T-N3-4 落地），就绪查询同步反映；\"脱战才能用\"\"仅限战斗中\"\"目标是物件\"均用它表达（ADR-0031 决策 9）"),
                // 新增（2026-09-14，ADR-0031 决策 2；06 第 3.1/3.10 节 2026-09-14 修订段）：超模
                // 说明——技能预算（锚点秒伤(技能等级) × 施放时间当量 × 冷却溢价 × 范围折价 × 消耗
                // 溢价）偏离带宽时填写意图，SkillBudgetAnalyzer（T-N3-9）按有无本字段把带宽外的技能
                // 分为已确认/待确认两组报告；比值超硬上限且本字段为空为阻断。效果列表不含结算类
                // 原语（见 SettlementEffectKinds）的技能不参与预算校验，本字段留空即可。
                new FieldSchema("budget_note", FieldKind.String, required: false,
                    description: "超模说明：技能预算偏离带宽时填写意图，SkillBudgetAnalyzer（T-N3-9）按此归入已确认组；比值超硬上限且本字段为空为阻断（ADR-0031 决策 2，见 06 第 3.10 节）"),
            }).WithOwnership(SchemaLayer.Rules, "skill").WithTimeScope(TimeScope.Combat);

        public static TableSchema AuraDef { get; } = new TableSchema(
            name: "skill.aura_def",
            primaryKey: "id",
            currentSchemaVersion: 2,
            migrations: new[]
            {
                new TableMigration(1, 2, row => MigrateEffectsScalingStatToList(row, "effects", ScalingMigrationAuraEffectKinds)),
            },
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "skill.aura_def.<name>"),
                // 消费方反馈第 4 条（2026-09-21，ADR-0056）：光环显示名此前无字段可登记，表现层增益/
                // 减益列表只能展示内部引用短串。命名与字段类型沿用 ADR-0048/ADR-0043 确立的 TextKey
                // 惯例（"语义名 + _key" 后缀、FieldKind.TextKey、required: false，缺省时表现层不
                // 渲染名称、不回退占位文案），不新造一套命名——见 skill.def.name_key 同名字段判断
                // 记录。纯新增可选字段，不提 schema_version，既有全部 skill.aura_def 行零改动仍合法。
                new FieldSchema("name_key", FieldKind.TextKey, required: false, description: "光环名称文本键，缺省时表现层不渲染名称（不回退占位文案），见 ADR-0056"),
                // 一个发现的交付缺口（2026-09-21，ADR-0060）：极性/图标引用此前均未登记（ADR-0056
                // 决策 4 当时明确留给后续独立评审），接入方画增益/减益边框、图标只能自行维护一张 id
                // 清单硬编码。两个字段均可选、缺省缺失，既有全部 skill.aura_def 行零改动仍合法，不
                // 提升 schema_version（同 name_key 落地时的做法）。
                new FieldSchema("polarity", FieldKind.Enum, required: false, enumValues: AuraPolarityValues,
                    description: "光环极性（对承受者有利/有害），缺省未声明——不代表任一极性，见 ADR-0060"),
                // 判断记录（Description 刻意不含"引用"/"指向"字样）：同 display.anim_set.clips.resource_ref/
                // display.equip_visual.mesh_ref 既有惯例——本字段是不透明资源标识，由引擎适配层解析，
                // 不对应任何内容表（不登记 ReferenceTable/SoftReferenceTable），若描述里出现这两个字样
                // 会触发 SchemaAudit id_description_reference_hint 告警（消费方反馈第 30 条自洽检查），
                // 见 RealRegisteredSchemas_WithRepoAllowlist_ZeroErrors 门禁。
                new FieldSchema("icon_ref", FieldKind.Id, required: false,
                    description: "光环图标资源标识；类别前缀限定 icon（资产根相对路径），见 ADR-0038/0039、ADR-0060")
                    .WithAllowedRefCategories("icon"),
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
                new FieldSchema("trigger_skill", FieldKind.Id, required: true,
                    description: "触发后释放的技能，指向 skill.def（消费方反馈第 37 条：登记为软引用，仅供内容工具补全/跳转，与 RulesSchemaCatalog.DeclareKnownReferences 的加载期硬校验并列）")
                    .WithSoftReference(table: "skill.def"),
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
