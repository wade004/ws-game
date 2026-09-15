using Core.Foundation.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>
    /// 货币与商人契约（见 08 第 9 节汇总表 Economy 行 <c>EconomyHost.buy/sell(...)</c>）。<see
    /// cref="EconomyHost"/> 是唯一实现。
    /// </summary>
    public interface IEconomyHost
    {
        /// <summary>为 <paramref name="unitId"/> 建立一份货币余额记录（默认全部为 0）；重复调用无副
        /// 作用。</summary>
        void RegisterUnit(Id unitId);

        long GetBalance(Id unitId, Id currencyId);

        /// <summary>增减 <paramref name="unitId"/> 的 <paramref name="currencyId"/> 余额（<paramref
        /// name="amount"/> 可正可负），按 <see cref="CurrencyDef.Cap"/> 夹取到 <c>[0, cap]</c>；实际
        /// 发生变化时发 <c>economy.currency_changed</c>。<paramref name="sourceId"/> 供事件追溯来源，
        /// 不做任何权限校验（惯例同 <c>core/gameplay/world_state.IWorldState.Set</c>"谁能写"）。</summary>
        bool Add(Id unitId, Id currencyId, long amount, Id sourceId);

        /// <summary>把 <paramref name="unitId"/> 的 <paramref name="currencyId"/> 余额直接替换为
        /// <paramref name="amount"/>（按 <see cref="CurrencyDef.Cap"/> 夹取到 <c>[0, cap]</c>），不与
        /// 当前余额相加；供读档等"以快照为准"的场景使用。实际发生变化时发
        /// <c>economy.currency_changed</c>。</summary>
        bool SetBalance(Id unitId, Id currencyId, long amount);

        /// <summary>尝试扣除 <paramref name="amount"/>（要求非负），余额不足则不生效、返回 false。</summary>
        bool TryPay(Id unitId, Id currencyId, long amount);

        /// <summary>
        /// T-N4-8（ADR-0034 决策 5；08 第 7.4 节修订段"tryCharge/TryPay(unitId, currencyId, amount,
        /// reason)——余额足够时一次性扣除并发 economy.charged{unitId, currencyId, amount, reason}；
        /// 不足时不扣、不发、返回 false"）：<see cref="TryPay(Id,Id,long)"/> 的带 <paramref
        /// name="reason"/> 重载，语义（原子：先只读校验余额是否足够，足够才一次性扣除，不产生"扣了
        /// 一部分"的中间态；不足时不扣不发直接返回 false）与旧签名完全一致，仅额外携带"这次扣费是为了
        /// 什么"（如 <c>"vendor_buy"</c>/<c>"respawn_fee"</c>），随成功时发出的 <see
        /// cref="EconomyChargedEvent.Reason"/> 字段供下游按用途拆分统计/日志。
        /// <para>
        /// 默认接口成员（ABI 硬性规则"只允许新增，禁止删除旧 TryPay"）：默认转发旧无 reason 签名
        /// <see cref="TryPay(Id,Id,long)"/>，<b>不</b>发 <see cref="EconomyChargedEvent"/>——默认值
        /// 语义是"这一次扣费调用方根本没有走带 reason 的新契约，也就不去凑一个假 reason 发一条新事件"，
        /// 与 <see cref="TryGetGoldBaseAmount"/>/<see cref="DepositPolicy"/> 同一惯例（默认值是"中性
        /// 退化"，不是"正确答案"）。唯一生产实现 <see cref="EconomyHost"/> 显式覆写为真正的原子扣费 +
        /// 发事件逻辑，且其自身的 <see cref="TryPay(Id,Id,long)"/> 反过来转发本方法——两者是"接口默认
        /// 值该是什么"与"唯一实现的旧签名该不该发事件"两个不同层次的独立判断，见 <see
        /// cref="EconomyHost"/> 对应判断记录，不矛盾。组合/包装实现（如 <c>core/gameplay/assembly.
        /// GameplayAssembly.DeferredEconomyHost</c>）须显式转发到内层真实宿主，见 <see
        /// cref="TryGetGoldBaseAmount"/> 判断记录同款 <c>Tests.Presentation.Assembly.
        /// InterfaceDefaultMemberForwardingTests</c> 门禁。
        /// </para>
        /// </summary>
        bool TryPay(Id unitId, Id currencyId, long amount, string reason) => TryPay(unitId, currencyId, amount);

        PurchaseResult Buy(Id unitId, Id vendorId, Id itemId, int count);

        SellResult Sell(Id unitId, Id vendorId, Id itemInstanceId, int count);

        /// <summary><paramref name="mapId"/> 上全部 <c>restock_policy=on_map_enter</c> 的商人条目补满，
        /// 每个实际发生补货的商人发一次 <c>economy.vendor_restocked</c>。</summary>
        void OnMapEnter(Id mapId);

        /// <summary>推进 <c>restock_policy=timer</c> 的补货倒计时（见 <see
        /// cref="VendorRestockPolicy.Timer"/>）。</summary>
        void Update(double dt);

        /// <summary>当前剩余库存；无限量或未知商人/物品返回 null。</summary>
        int? GetStock(Id vendorId, Id itemId);

        /// <summary>
        /// T-N4-7（ADR-0034 决策 3；08 第 1.1/7.4 节修订段）：按 <see
        /// cref="EconomyOptions.GoldBaseCurveId"/> 指定的 <c>econ.gold_base_curve</c> 曲线取
        /// <paramref name="level"/> 对应的金币基数（"一只同级普通怪的金币当量为 1 时应发的金币数"），
        /// 供 <c>core/gameplay/loot.LootHost</c> 的货币掉落条目换算实际数量——把曲线求值留在
        /// Economy 模块内部，Loot 模块只问"这个等级值多少钱"，不需要反向解析
        /// <c>EconomySchemas.GoldBaseCurve</c> 的记录结构（同 <see cref="EconomyPriceFormula"/> 被
        /// <see cref="EconomyHost"/>/<see cref="EconomyPriceDeviatesFormulaRule"/> 共用同一份算法的
        /// 理由）。曲线未加载/找不到该 id 时返回 <c>null</c>（"无法算出"，不是"算出 0"）。
        /// <para>
        /// 默认接口成员（ABI 硬性规则 5"只允许新增"）：默认返回 <c>null</c>（"没有曲线数据"这一
        /// 中性默认值），唯一生产实现 <see cref="EconomyHost"/> 显式覆写为真实曲线求值；组合/包装
        /// 实现（如 <c>core/gameplay/assembly.GameplayAssembly.DeferredEconomyHost</c>）须显式转发
        /// 到内层真实宿主，见 <c>Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests</c>
        /// 门禁与 <see cref="Core.Carriers.Common.ILootRoller.RollDetailed"/> 同款判断记录。
        /// </para>
        /// </summary>
        double? TryGetGoldBaseAmount(int level) => null;

        /// <summary>
        /// T-N4-7（ADR-0034 决策 4；08 第 7.4 节修订段）：货币入账方式策略项当前取值，见 <see
        /// cref="EconomyOptions.DepositPolicy"/>/<see cref="CurrencyDepositPolicy"/> 判断记录——
        /// <c>core/gameplay/loot.CreatureDeathLootListener</c> 据此判断死亡结算时是否把货币产出
        /// 直接入账给击杀者（<see cref="CurrencyDepositPolicy.OnKill"/>）还是让货币随其它掉落物
        /// 一并落地、拾取时才入账（<see cref="CurrencyDepositPolicy.GroundPickup"/>）。默认接口成员
        /// （同 <see cref="TryGetGoldBaseAmount"/> 判断记录）：默认值 <see
        /// cref="CurrencyDepositPolicy.OnKill"/>，与 <see cref="EconomyOptions.DepositPolicy"/> 的
        /// 默认值同一语义；唯一生产实现 <see cref="EconomyHost"/> 显式覆写为转发
        /// <see cref="EconomyOptions.DepositPolicy"/> 的当前配置值。
        /// </summary>
        CurrencyDepositPolicy DepositPolicy => CurrencyDepositPolicy.OnKill;

        /// <summary>
        /// T-N4-11（阶段 N4 独立复核缺口修复；ADR-0034 决策 2 同一条价格解析路径）：<paramref
        /// name="vendorId"/> 的出售清单里 <paramref name="itemId"/> 那一条的"单价"——<see
        /// cref="VendorSellItem.HasPriceAmount"/> 为真时是手填 <see cref="VendorSellItem.PriceAmount"/>
        /// 原样值；为假（T-N4-6 起 <c>price_amount</c> 可选，未填时买价改按价格公式）时是 <see
        /// cref="EconomyPriceFormula.TryComputeBaseValue"/> 算出的基准价值（算不出按 0 处理），与
        /// <see cref="Buy"/> 内部实际扣款用的单价出自同一条解析路径（惯例同 <see
        /// cref="TryGetGoldBaseAmount"/> 判断记录"运行时/展示层共用同一份算法，不各自重复实现一遍"）
        /// ——本成员补的是"展示层（如商店货架 UI）该读哪个价目，而不是直接读 <see
        /// cref="VendorSellItem.PriceAmount"/> 这个字段本身"这一缺口：<c>price_amount</c> 未填时该
        /// 字段恒为 0（占位，不代表实际单价，见其判断记录），展示层若不经本成员会显示错误的 0 价。
        /// <paramref name="vendorId"/> 未知，或该商人未出售 <paramref name="itemId"/>，返回
        /// <c>null</c>（"查不到"，不是"算出 0"，同 <see cref="TryGetGoldBaseAmount"/> 判断记录一贯
        /// 口径）。
        /// <para>
        /// 默认接口成员（ABI 硬性规则"只允许新增"）：默认返回 <c>null</c>（中性退化——接口自身没有
        /// 暴露"某商人登记了哪些出售条目"这份静态清单，无法在不持有具体宿主内部状态的前提下算出
        /// 任何有意义的值，见 <see cref="Presentation.Ui.ShopViewModel"/> 类型判断记录"接口契约暂缺
        /// 对应能力"）；唯一生产实现 <see cref="EconomyHost"/> 显式覆写为真实价格解析。组合/包装
        /// 实现（如 <c>core/gameplay/assembly.GameplayAssembly.DeferredEconomyHost</c>）须显式转发
        /// 到内层真实宿主，见 <see cref="TryGetGoldBaseAmount"/> 判断记录同款
        /// <c>Tests.Presentation.Assembly.InterfaceDefaultMemberForwardingTests</c> 门禁。
        /// </para>
        /// </summary>
        long? TryGetSellItemPrice(Id vendorId, Id itemId) => null;
    }
}
