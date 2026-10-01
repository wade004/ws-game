using System;
using System.Collections.Generic;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>一次姿势解析的结果（手感设计/04 第 2.2 节回落链）。</summary>
    public readonly struct PoseResolution
    {
        /// <summary>是否找到可用的键（回落链末端的基础键也没有时为 false）。</summary>
        public bool Found { get; }

        /// <summary>命中的键在表里的<b>实际拼写</b>（旧键 <c>combat_idle</c> 命中时就是 <c>combat_idle</c>）；未找到为空串。</summary>
        public string TableKey { get; }

        /// <summary>命中的键的规范拼写（<c>idle.combat</c>）；未找到为空串。</summary>
        public string CanonicalKey { get; }

        /// <summary>命中项在回落链里的下标：0 表示请求键本身就在表里（未回落），越大表示回落越多；未找到为 -1。</summary>
        public int FallbackDepth { get; }

        /// <summary>按顺序尝试过的规范键（含命中的那一个；未找到时是整条链），即"回落链逐级记录"。</summary>
        public IReadOnlyList<string> Tried { get; }

        public PoseResolution(bool found, string tableKey, string canonicalKey, int fallbackDepth, IReadOnlyList<string> tried)
        {
            Found = found;
            TableKey = tableKey;
            CanonicalKey = canonicalKey;
            FallbackDepth = fallbackDepth;
            Tried = tried;
        }

        public override string ToString() =>
            Found ? $"{CanonicalKey} (depth {FallbackDepth}; tried {string.Join(" > ", Tried)})" : $"<none> (tried {string.Join(" > ", Tried)})";
    }

    /// <summary>
    /// 姿势解析（手感设计/04 第 2.2 节）：在一张姿势键表里，按 <see cref="PoseRequest.Chain"/> 的顺序
    /// 去变体 → 去武器族 → 去姿态 → 去步态 → 基础键，返回第一个存在（且可用）的键。纯函数、无状态。
    /// <para>
    /// 旧键 <c>combat_&lt;state&gt;</c> 与 <c>&lt;state&gt;.combat</c> 等价（<see cref="PoseKeys"/>）：候选是规范键
    /// <c>&lt;state&gt;.combat</c> 时，表里写的是旧键也命中；两种写法并存时规范写法优先。
    /// </para>
    /// <para>
    /// 判断记录（可用性探针只对非基础键咨询）：<c>isUsable</c> 让调用方表达"这条剪辑内容此刻还没加载完"（冷加载）——
    /// 未就绪的候选被跳过、继续回落；基础键不咨询（永远可作为最后兜底），与 ADR-0111 既有的"变体未就绪退回普通键"
    /// 同一规则，因此没有任何维度键的表解析结果与改动前逐位一致。
    /// </para>
    /// </summary>
    public static class PoseResolver
    {
        /// <summary>
        /// 通用入口：<paramref name="has"/> 回答"表里有没有这个键（按表里的实际拼写）"，
        /// <paramref name="isUsable"/>（可空）回答"表里这个键此刻是否可用"，只对非基础键咨询。
        /// </summary>
        public static PoseResolution Resolve(PoseRequest request, Func<string, bool> has, Func<string, bool>? isUsable = null)
        {
            if (has == null) throw new ArgumentNullException(nameof(has));
            var chain = request.Chain();
            var tried = new List<string>(chain.Count);
            for (var i = 0; i < chain.Count; i++)
            {
                var canonical = chain[i];
                tried.Add(canonical);
                var isBase = i == chain.Count - 1;

                string? hit = null;
                if (has(canonical))
                {
                    hit = canonical;
                }
                else if (PoseKeys.TryGetLegacyAlias(canonical, out var legacy) && has(legacy))
                {
                    hit = legacy;
                }

                if (hit == null)
                {
                    continue;
                }

                if (!isBase && isUsable != null && !isUsable(hit))
                {
                    continue;
                }

                return new PoseResolution(true, hit, canonical, i, tried);
            }

            return new PoseResolution(false, string.Empty, string.Empty, -1, tried);
        }

        /// <summary>字典入口：表键即规范或旧写法；命中后取出值。</summary>
        public static bool TryResolve<T>(
            PoseRequest request,
            IReadOnlyDictionary<string, T> table,
            out T value,
            out PoseResolution resolution,
            Func<string, bool>? isUsable = null)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            resolution = Resolve(request, table.ContainsKey, isUsable);
            if (resolution.Found && table.TryGetValue(resolution.TableKey, out var found))
            {
                value = found;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>
        /// 对一个<b>具体的键</b>（而不是结构化请求）求回落目标：逐段去尾直到命中表里存在的键（含旧键别名），
        /// 用于完整性报告"缺项回落到哪个键"。键本身存在返回它自己；一路去到基础键仍不存在返回 null。
        /// </summary>
        public static string? FallbackTargetOf(string key, Func<string, bool> has)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (has == null) throw new ArgumentNullException(nameof(has));
            var current = key;
            while (true)
            {
                if (has(current)) return current;
                if (PoseKeys.TryGetLegacyAlias(current, out var legacy) && has(legacy)) return legacy;
                if (!PoseKeys.TryDropLastSegment(current, out var shorter)) return null;
                current = shorter;
            }
        }
    }
}
