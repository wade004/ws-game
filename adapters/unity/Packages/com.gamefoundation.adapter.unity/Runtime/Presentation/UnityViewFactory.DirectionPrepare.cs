#nullable enable
// UnityViewFactory 的方向准备与方向预热部分（ADR-0112，消费方反馈第六十三批）。
//
// 一、方向准备（原子化方向切换）
// 视图（SpriteViewBase）每帧由姿态算出"期望方向"，显示用的是"已显示方向"。期望方向的槽位相对已显示方向
// 变化时，视图每帧询问本文件的 PrepareDirectionForView：该方向下——当前合成层集合里每一层的静态图，加上
// 全部状态键（基础键 + 该外形声明的战斗变体键）与已登记覆盖剪辑的逐层/整身剪辑——是否每一项都已有结论
// （加载成功，或已确认不存在：探测各档耗尽/加载失败）。有结论之前整个实体的全部视觉内容保持已显示方向；
// 全部有结论的那一帧，视图在同一次 SyncPose 里提交（先 DirectionSlotChanged，工厂据此走缓存命中路径换入
// 新方向的逐层/整身剪辑；再重合成纸娃娃层）。
//
// 准备阶段只加载、不改显示：不重合成层、不换逐层映射、不重登记播放器上的剪辑内容、不显示占位图。所有
// 探测链一次性并行发起（每层每状态一条链，链内仍按"带方向 -> 不带方向"分档顺序回落，分档语义不变）；
// 结果满足"提交所需内容都已在缓存里"后写入既有的按 (实体, 方向) 缓存，提交时走同步命中路径。
//
// 准备状态（DirPrep）按 (实体, 方向) 保留：准备中期望方向又变了，已发出的加载让它自然完成进加载器缓存，
// 不取消、不报错，改为准备最新的期望方向；期望方向变回已显示方向则无事发生。
//
// 二、方向预热
// PrewarmDirections 对一个实体的每个方向档位（镜像对去重后）依次执行与上面相同的准备，填满缓存、不改变
// 显示。逐档位顺序执行（一个档位有结论后再发起下一个），有实体正在做真实转向的方向准备时在档位之间暂停
// （让路）；预热过的实体换装后自动对新增层补预热（粘性）；实体销毁时取消并清理。走加载器既有的分帧预算，
// 不新增第二套预算。
//
// 已知限制（同 ADR-0112，逐条）：
//   1) 冷转向的保持时间 = 新方向全部资源里最慢的一项完成的时间（含全部状态键，不只是当前状态），没有超时；
//      加载失败按"缺失"算结论，从不无限等待。
//   2) 首次显示与换装不是原子的：层图先以占位图出现，被真图替换（各约 1 帧）。
//   3) 被放弃的目标方向（准备中期望方向又变了）的在途加载不取消，占用一部分加载带宽，结果进缓存。
//   4) 预热的让路只发生在档位之间，一个档位内已发起的加载不会因转向而暂停。
//   5) 预热把该外形全部方向的贴图常驻内存（真实外形实测约 1.4 GB，见 ADR-0112），因此默认不自动预热。
//   6) 整身兜底渲染器的方向不变量只对有纸娃娃层的外形生效。
//   7) 初始方向的整身方向变体美术不在挂接时探测，首次换向后才探测——本次之前就有的行为，未改变。
//   8) DirectionSlotChanged 改为提交时触发：依赖"期望方向一变就通知"的订阅方需改用期望方向的只读查询。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Presentation
{
    /// <summary>ADR-0112 B4：视图工厂的方向预热自动策略，见 <see cref="UnityViewFactory.DirectionPrewarm"/>。</summary>
    public enum DirectionPrewarmPolicy
    {
        /// <summary>不自动预热（默认，不改变既有消费方的内存与加载行为）；仍可对单个实体手工调用
        /// <see cref="UnityViewFactory.PrewarmDirections"/>。</summary>
        None = 0,

        /// <summary>每个带方向剪辑的实体（挂接了默认动画的生物 sprite 视图）挂接后自动预热全部方向。</summary>
        OnAttach = 1,
    }

    public sealed partial class UnityViewFactory
    {
        /// <summary>ADR-0112 B4：方向预热自动策略，默认 <see cref="DirectionPrewarmPolicy.None"/>。须在创建视图
        /// 之前设置（只对设置之后挂接的实体生效）。</summary>
        public DirectionPrewarmPolicy DirectionPrewarm { get; set; } = DirectionPrewarmPolicy.None;

        private enum ChainStatus
        {
            Pending,
            Hit,
            Exhausted,
        }

        /// <summary>一条分档探测链（一层×一状态，或一个整身变体）：候选按 <c>Candidates</c> 顺序逐档回落。</summary>
        private sealed class EffectChain
        {
            public Id[] Candidates = Array.Empty<Id>();
            public int Tier;
            public int RequestedTier = -1;
            public ChainStatus Status;
            public Adapter.Unity.EngineAdapter.UnityResourceLoader.EffectAsset? Effect;
            public int HitTier;
        }

        /// <summary>(实体, 方向) 的准备状态：各条探测链，按稳定字符串键索引（层身份 = 层名 + 装备资源集）。</summary>
        private sealed class DirPrep
        {
            public readonly Dictionary<string, EffectChain> Chains = new Dictionary<string, EffectChain>(StringComparer.Ordinal);
        }

        private readonly Dictionary<Id, Dictionary<string, DirPrep>> _dirPrepByEntity = new Dictionary<Id, Dictionary<string, DirPrep>>();

        /// <summary>正在等待方向准备完成（期望方向领先于已显示方向）的实体集合，供预热让路判断；由闸门维护，
        /// 期望方向变回已显示方向时不再有人调用闸门，因此读取前先按视图状态修剪（<see cref="PruneTurnPending"/>）。</summary>
        private readonly HashSet<Id> _turnPendingEntities = new HashSet<Id>();

        private sealed class PrewarmState
        {
            public List<(string Dir, Id SlotId, Direction Facing)> Slots = new List<(string, Id, Direction)>();
            public int Index;
            public bool SlotStarted;
            public bool Completed;
            public bool CallbackFired;
            public Action<Id, bool>? OnCompleted;
        }

        private readonly Dictionary<Id, PrewarmState> _prewarmByEntity = new Dictionary<Id, PrewarmState>();

        // ------------------------------------------------------------------
        // 已显示方向查询（方向不变量与"只有已显示方向才登记播放器内容"的判据）
        // ------------------------------------------------------------------

        /// <summary>该实体视图当前已显示方向的裸档位名（视图提交时改变，见 <see cref="SpriteViewBase.DisplayedDirection"/>）。</summary>
        private static string DisplayedDirBareName(DirectionAwareAnimContext ctx) =>
            DirectionSlots.StripPrefix(ctx.View.DisplayedDirection.SlotId);

        /// <summary>ADR-0112 决策 8：<paramref name="dirBare"/> 是否是该实体当前已显示方向。没有动画上下文（挂接
        /// 尚未完成，或已销毁）时视为是——挂接期同步登记的内容不需要被拦截。</summary>
        private bool IsDisplayedDirection(Id entityId, string dirBare) =>
            !_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx)
            || ctx.Info.Sprite == null
            || DisplayedDirBareName(ctx) == dirBare;

        /// <summary>ADR-0112 决策 8：整身兜底渲染器的方向不变量判据——<paramref name="clipId"/> 在播放器上登记的
        /// 内容是否可以在已显示方向下展示：内容与方向无关（无标签：单帧占位、无方向段的回退资源）或标签等于已显示
        /// 方向。只对纸娃娃层外形生效（这类外形才有静态层与兜底渲染器同屏的问题；没有纸娃娃层的外形整身渲染器
        /// 是唯一的视觉内容，隐藏它只会让实体消失）。没有动画上下文时视为可展示。</summary>
        private bool ContentMatchesDisplayedDirection(Id entityId, UnityFrameAnimPlayer player, Id clipId)
        {
            if (!_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx) || ctx.ActivePerLayerByState == null)
            {
                return true;
            }

            var tag = player.GetClipContentDirection(clipId);
            return tag == null || tag == DisplayedDirBareName(ctx);
        }

        // ------------------------------------------------------------------
        // 方向准备闸门
        // ------------------------------------------------------------------

        /// <summary>视图每帧调用的方向准备闸门（<see cref="UnitySpriteView.DirectionPreparer"/>）：该方向下全部
        /// 静态图与剪辑都有结论则写入缓存并返回 <c>true</c>（视图当帧提交），否则返回 <c>false</c>（视图保持
        /// 已显示方向）。没有方向相关资源可等待的情形（无动画上下文、加载器不是 <see cref="Adapter.Unity.EngineAdapter.UnityResourceLoader"/>
        /// 的测试替身）恒返回 <c>true</c>。</summary>
        private bool PrepareDirectionForView(Id entityId, Direction facing, Id slotId)
        {
            if (!_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx) || ctx.Info.Sprite == null
                || !(_resourceLoader is Adapter.Unity.EngineAdapter.UnityResourceLoader unityLoader))
            {
                return true;
            }

            var dirBare = DirectionSlots.StripPrefix(slotId);
            var ready = EvaluateDirection(entityId, ctx, unityLoader, dirBare, facing, install: true, allowRequest: true);
            if (ready)
            {
                _turnPendingEntities.Remove(entityId);
            }
            else
            {
                _turnPendingEntities.Add(entityId);
            }
            return ready;
        }

        private void PruneTurnPending()
        {
            if (_turnPendingEntities.Count == 0)
            {
                return;
            }

            List<Id>? stale = null;
            foreach (var entityId in _turnPendingEntities)
            {
                if (!_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx) || !ctx.View.HasPendingDirectionSwitch)
                {
                    (stale ??= new List<Id>()).Add(entityId);
                }
            }
            if (stale != null)
            {
                for (var i = 0; i < stale.Count; i++)
                {
                    _turnPendingEntities.Remove(stale[i]);
                }
            }
        }

        // ------------------------------------------------------------------
        // 方向求值：并行推进全部探测链，全部有结论则组装并（可选）写入缓存
        // ------------------------------------------------------------------

        /// <summary>
        /// 对 (<paramref name="entityId"/>, <paramref name="dirBare"/>) 求一次"是否全部有结论"：
        /// <list type="number">
        /// <item>静态图：<paramref name="facing"/> 朝向下当前合成层集合（纯计算，<see cref="SpriteViewBase.ComposeLayersForDirection"/>）
        /// 每一层的静态图经视图的追踪器预取（<see cref="SpriteViewBase.EnsureLayerImage"/>）；</item>
        /// <item>状态剪辑：全部状态键（<see cref="AnimStateKeysFor"/>）× 合成层，一条分档探测链一层，一次性
        /// 并行推进；该状态所有层都耗尽时再推进整身方向变体链；</item>
        /// <item>覆盖剪辑：已登记的覆盖剪辑同上（逐层链 + 逐层全耗尽时的整身方向变体链）。</item>
        /// </list>
        /// 推进一条链 = 同步命中加载器缓存则记录命中；候选正在加载（不论是谁发起的）则等待；候选已发起过但
        /// 没有进缓存（加载失败）则回落下一档；从未请求过则发起加载。全部有结论且 <paramref name="install"/> 为
        /// 真、该方向不是已显示方向时，把结果组装成与探测路径一致的缓存条目写入
        /// <see cref="_perLayerClipCacheByEntityAndDir"/>/<see cref="_defaultClipCacheByEntityAndDir"/>/
        /// <see cref="_overrideClipPerLayerCacheByEntityAndDir"/>（提交时的 <see cref="ReprobeDirectionAwareAnimation"/>
        /// 因此全部走同步命中路径）。<paramref name="allowRequest"/> 为假时只观察、不发起任何加载。
        /// </summary>
        private bool EvaluateDirection(
            Id entityId, DirectionAwareAnimContext ctx, Adapter.Unity.EngineAdapter.UnityResourceLoader loader,
            string dirBare, Direction facing, bool install, bool allowRequest)
        {
            var sprite = ctx.Info.Sprite!;
            var view = ctx.View;
            var hasPaperdoll = sprite.PaperdollLayers.Count > 0;
            var composed = hasPaperdoll ? view.ComposeLayersForDirection(facing) : Array.Empty<SpriteComposedLayer>();
            var spriteSetId = Id.Parse(sprite.SpriteSetId);
            var dp = GetDirPrep(entityId, dirBare);
            var concluded = true;

            // 1. 每一层的静态图。
            for (var i = 0; i < composed.Count; i++)
            {
                if (view.EnsureLayerImage(composed[i].ResourceId, allowRequest) == SpriteViewBase.LayerImageState.Pending)
                {
                    concluded = false;
                }
            }

            if (ctx.AnimSet == null)
            {
                return concluded;
            }

            var perLayer = ctx.ActivePerLayerByState != null && hasPaperdoll;
            var perLayerStates = new Dictionary<Id, Dictionary<string, PerLayerCacheEntry>>();
            var wholeBodies = new List<(string Key, WholeBodyCacheEntry Entry)>();
            var overrideMaps = new List<(Id ClipId, Dictionary<string, PerLayerCacheEntry> Map)>();

            // 2. 全部状态键（基础键 + 战斗变体键）。
            var stateKeys = AnimStateKeysFor(ctx.AnimSet);
            for (var i = 0; i < stateKeys.Count; i++)
            {
                var stateKey = stateKeys[i];
                if (!ctx.AnimSet.Clips.TryGetValue(stateKey, out var clipDef) || !ctx.StateClipIds.TryGetValue(stateKey, out var stateClipId))
                {
                    continue;
                }

                var strippedRef = AssetRefConventions.StripCategoryPrefix(clipDef.ResourceRef.Value);
                var layerMap = new Dictionary<string, PerLayerCacheEntry>(StringComparer.Ordinal);
                var stateDone = true;

                if (perLayer)
                {
                    for (var l = 0; l < composed.Count; l++)
                    {
                        var layer = composed[l];
                        var chain = GetChain(dp, "s|" + stateClipId.Value + "|" + LayerKey(layer), () => BuildLayerCandidates(layer, strippedRef, dirBare));
                        if (!AdvanceChain(entityId, chain, loader, spriteSetId, allowRequest))
                        {
                            stateDone = false;
                            continue;
                        }
                        if (chain.Status == ChainStatus.Hit)
                        {
                            AddLayerHit(layerMap, stateClipId, layer.LayerName, chain, dirBare);
                        }
                    }
                }

                if (!stateDone)
                {
                    concluded = false;
                    continue;
                }

                perLayerStates[stateClipId] = layerMap;
                if (layerMap.Count == 0)
                {
                    var wholeChain = GetChain(dp, "w|" + stateKey, () => new[]
                    {
                        new Id($"sprite_anim.{strippedRef}__{dirBare}"),
                        clipDef.ResourceRef,
                    });
                    if (!AdvanceChain(entityId, wholeChain, loader, spriteSetId, allowRequest))
                    {
                        concluded = false;
                        continue;
                    }
                    wholeBodies.Add((stateKey, ToWholeBodyEntry(wholeChain, dirBare)));
                }
            }

            // 3. 已登记的覆盖剪辑（武器风格/技能覆盖）。
            if (_overrideClipIdsByEntity.TryGetValue(entityId, out var overrideClipIds))
            {
                foreach (var clipId in overrideClipIds)
                {
                    var strippedClip = AssetRefConventions.StripCategoryPrefix(clipId.Value);
                    var layerMap = new Dictionary<string, PerLayerCacheEntry>(StringComparer.Ordinal);
                    var clipDone = true;

                    for (var l = 0; l < composed.Count; l++)
                    {
                        var layer = composed[l];
                        var chain = GetChain(dp, "o|" + clipId.Value + "|" + LayerKey(layer), () => BuildLayerCandidates(layer, strippedClip, dirBare));
                        if (!AdvanceChain(entityId, chain, loader, spriteSetId, allowRequest))
                        {
                            clipDone = false;
                            continue;
                        }
                        if (chain.Status == ChainStatus.Hit)
                        {
                            AddLayerHit(layerMap, clipId, layer.LayerName, chain, dirBare);
                        }
                    }

                    if (!clipDone)
                    {
                        concluded = false;
                        continue;
                    }

                    overrideMaps.Add((clipId, layerMap));
                    if (layerMap.Count == 0)
                    {
                        var wholeChain = GetChain(dp, "ow|" + clipId.Value, () => new[]
                        {
                            new Id($"sprite_anim.{strippedClip}__{dirBare}"),
                            clipId,
                        });
                        if (!AdvanceChain(entityId, wholeChain, loader, spriteSetId, allowRequest))
                        {
                            concluded = false;
                            continue;
                        }
                        wholeBodies.Add(("override_clip." + clipId.Value, ToWholeBodyEntry(wholeChain, dirBare)));
                    }
                }
            }

            if (concluded && install && DisplayedDirBareName(ctx) != dirBare)
            {
                InstallPrepared(entityId, dirBare, perLayer, perLayerStates, wholeBodies, overrideMaps);
            }

            return concluded;
        }

        private static string LayerKey(SpriteComposedLayer layer) =>
            layer.LayerName + "|" + (layer.EquipMeshRef.HasValue ? layer.EquipMeshRef.Value.Value : string.Empty);

        private static void AddLayerHit(
            Dictionary<string, PerLayerCacheEntry> layerMap, Id stateClipId, string layerName, EffectChain chain, string dirBare)
        {
            layerMap[layerName] = new PerLayerCacheEntry
            {
                ClipId = new Id(stateClipId.Value + ".layer." + layerName),
                Effect = chain.Effect,
                IsFirstForState = layerMap.Count == 0,
                ContentDir = chain.HitTier == 0 ? dirBare : null,
            };
        }

        private static WholeBodyCacheEntry ToWholeBodyEntry(EffectChain chain, string dirBare) =>
            chain.Status == ChainStatus.Hit
                ? new WholeBodyCacheEntry(chain.Effect, chain.HitTier == 0 ? dirBare : null)
                : new WholeBodyCacheEntry(null, null);

        private DirPrep GetDirPrep(Id entityId, string dirBare)
        {
            if (!_dirPrepByEntity.TryGetValue(entityId, out var byDir))
            {
                byDir = new Dictionary<string, DirPrep>(StringComparer.Ordinal);
                _dirPrepByEntity[entityId] = byDir;
            }
            if (!byDir.TryGetValue(dirBare, out var dp))
            {
                dp = new DirPrep();
                byDir[dirBare] = dp;
            }
            return dp;
        }

        private static EffectChain GetChain(DirPrep dp, string key, Func<Id[]> buildCandidates)
        {
            if (!dp.Chains.TryGetValue(key, out var chain))
            {
                chain = new EffectChain { Candidates = buildCandidates() };
                dp.Chains[key] = chain;
            }
            return chain;
        }

        /// <summary>推进一条探测链；返回该链是否已有结论（命中或各档耗尽）。见 <see cref="EvaluateDirection"/>
        /// 判断记录：在途的加载不论由谁发起都只是等待，不重复发起；本链自己请求过的候选没进缓存即视为失败，
        /// 回落下一档。</summary>
        private bool AdvanceChain(
            Id entityId, EffectChain chain, Adapter.Unity.EngineAdapter.UnityResourceLoader loader, Id spriteSetId, bool allowRequest)
        {
            while (chain.Status == ChainStatus.Pending)
            {
                if (chain.Tier >= chain.Candidates.Length)
                {
                    chain.Status = ChainStatus.Exhausted;
                    break;
                }

                var candidate = chain.Candidates[chain.Tier];
                if (loader.TryGetEffect(candidate, out var effect))
                {
                    chain.Effect = effect;
                    chain.HitTier = chain.Tier;
                    chain.Status = ChainStatus.Hit;
                    break;
                }

                if (loader.GetLoadProgress(candidate) > 0.0)
                {
                    // 加载在途（本链、别的链、或探测路径发起的），等它有结果。
                    break;
                }

                if (chain.RequestedTier == chain.Tier || _pendingAnimResourceLoads.Contains(candidate)
                    || _pendingWeaponClipResourceLoads.Contains(candidate))
                {
                    // 已经发起过且没有进缓存、也不在途：加载失败（该档位本就没有对应美术，是探测的正常结果）。
                    chain.Tier++;
                    continue;
                }

                if (!allowRequest)
                {
                    break;
                }

                chain.RequestedTier = chain.Tier;
                loader.LoadAsync(
                    candidate, ResourceKind.Effect, new ResourceLoadHints(spriteSetId),
                    (_, __) => OnDirectionPrepareLoadCompleted(entityId));
                break;
            }

            return chain.Status != ChainStatus.Pending;
        }

        /// <summary>方向准备发起的加载完成（成功或失败）：唤醒该实体的预热推进（转向的准备由视图每帧的
        /// 闸门调用推进，不需要在这里唤醒）。实体已销毁则什么都不做。</summary>
        private void OnDirectionPrepareLoadCompleted(Id entityId)
        {
            if (_prewarmByEntity.ContainsKey(entityId))
            {
                AdvancePrewarm(entityId);
            }
        }

        /// <summary>把方向准备的结果组装成与逐层/整身探测路径逐字节一致的缓存条目写入既有缓存。该方向若已有
        /// 缓存条目（探测路径留下的、可能不完整的条目）一律覆盖：准备阶段的结果是全部有结论的完整版本。</summary>
        private void InstallPrepared(
            Id entityId, string dirBare, bool perLayer,
            Dictionary<Id, Dictionary<string, PerLayerCacheEntry>> perLayerStates,
            List<(string Key, WholeBodyCacheEntry Entry)> wholeBodies,
            List<(Id ClipId, Dictionary<string, PerLayerCacheEntry> Map)> overrideMaps)
        {
            if (perLayer)
            {
                if (!_perLayerClipCacheByEntityAndDir.TryGetValue(entityId, out var byDir))
                {
                    byDir = new Dictionary<string, Dictionary<Id, Dictionary<string, PerLayerCacheEntry>>>(StringComparer.Ordinal);
                    _perLayerClipCacheByEntityAndDir[entityId] = byDir;
                }
                byDir[dirBare] = perLayerStates;
            }

            if (wholeBodies.Count > 0)
            {
                var byState = GetWholeBodyCacheForDirection(entityId, dirBare);
                for (var i = 0; i < wholeBodies.Count; i++)
                {
                    byState[wholeBodies[i].Key] = wholeBodies[i].Entry;
                }
            }

            if (overrideMaps.Count > 0)
            {
                if (!_overrideClipPerLayerCacheByEntityAndDir.TryGetValue(entityId, out var byKey))
                {
                    byKey = new Dictionary<(Id, string), Dictionary<string, PerLayerCacheEntry>>();
                    _overrideClipPerLayerCacheByEntityAndDir[entityId] = byKey;
                }
                for (var i = 0; i < overrideMaps.Count; i++)
                {
                    byKey[(overrideMaps[i].ClipId, dirBare)] = overrideMaps[i].Map;
                }
            }
        }

        // ------------------------------------------------------------------
        // 方向预热（ADR-0112 B）
        // ------------------------------------------------------------------

        /// <summary>
        /// ADR-0112 B1：按实体预热全部方向——对该外形的每个方向档位（镜像对去重后）依次执行与转向相同的
        /// "方向准备"（静态图 + 全部状态/覆盖剪辑的逐层与整身探测），填满缓存，不改变显示。之后首次转到
        /// 任一方向当帧切换。逐档位顺序执行（一个档位有结论后再发起下一个），控制在途加载量与内存峰值；有
        /// 实体正在做真实转向的方向准备时在档位之间暂停，等它完成再继续（让路）。预热过的实体换装后自动补预热
        /// 新增层（粘性），实体销毁时取消后续档位并清理。走加载器既有的分帧预算，不新增第二套预算。
        /// <para>
        /// 已知代价：预热把该外形全部方向的贴图常驻内存，见 ADR-0112。
        /// </para>
        /// </summary>
        /// <param name="entityId">已经创建视图并挂接默认动画的生物 sprite 实体。</param>
        /// <param name="onCompleted">全部方向档位都有结论时调用一次（参数：实体、<c>true</c>）；实体在完成前
        /// 被销毁或工厂清理时调用一次（<c>false</c>）。已经预热完成的实体再次调用时立即回调（<c>true</c>）。</param>
        /// <returns>该实体可以预热（已挂接方向相关的动画上下文且加载器是 <see cref="Adapter.Unity.EngineAdapter.UnityResourceLoader"/>）
        /// 则 <c>true</c>；否则 <c>false</c> 且不会回调。</returns>
        public bool PrewarmDirections(Id entityId, Action<Id, bool>? onCompleted = null)
        {
            if (!_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx) || ctx.Info.Sprite == null
                || !(_resourceLoader is Adapter.Unity.EngineAdapter.UnityResourceLoader))
            {
                return false;
            }

            if (_prewarmByEntity.TryGetValue(entityId, out var existing))
            {
                if (existing.Completed && existing.CallbackFired)
                {
                    onCompleted?.Invoke(entityId, true);
                }
                else if (onCompleted != null)
                {
                    existing.OnCompleted += onCompleted;
                }
                return true;
            }

            var state = new PrewarmState { OnCompleted = onCompleted };
            var slots = ctx.View.GetDistinctDirectionSlots();

            // 顺序：从当前已显示方向开始按量化索引环绕，最先准备的是离玩家当前朝向最近的档位。
            var displayedSlot = ctx.View.DisplayedDirection.SlotId;
            var start = 0;
            for (var i = 0; i < slots.Count; i++)
            {
                if (slots[i].SlotId.Equals(displayedSlot))
                {
                    start = i;
                    break;
                }
            }
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[(start + i) % slots.Count];
                state.Slots.Add((DirectionSlots.StripPrefix(slot.SlotId), slot.SlotId, slot.Facing));
            }

            _prewarmByEntity[entityId] = state;
            ctx.View.AfterSyncPose = () => AdvancePrewarm(entityId);
            AdvancePrewarm(entityId);
            return true;
        }

        /// <summary>
        /// ADR-0112 B1：只读查询该实体已就绪的方向数 / 总方向数（镜像对去重后的方向档位数）。已开始预热的实体
        /// 返回预热进度（单调不减，完成后等于总数；换装触发补预热的一轮内会从头重新累计）；未预热的实体只观察、
        /// 不发起任何加载，统计当前恰好已经全部有结论的方向档位数（已显示方向与仍有加载在途的档位计为未就绪）。
        /// 实体没有挂接方向相关的动画上下文时返回 <c>false</c>。
        /// </summary>
        public bool TryGetDirectionPrewarmProgress(Id entityId, out int readyDirections, out int totalDirections)
        {
            readyDirections = 0;
            totalDirections = 0;
            if (!_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx) || ctx.Info.Sprite == null)
            {
                return false;
            }

            var slots = ctx.View.GetDistinctDirectionSlots();
            totalDirections = slots.Count;

            if (_prewarmByEntity.TryGetValue(entityId, out var state))
            {
                readyDirections = state.Completed ? slots.Count : state.Index;
                return true;
            }

            if (_resourceLoader is Adapter.Unity.EngineAdapter.UnityResourceLoader unityLoader)
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    var dirBare = DirectionSlots.StripPrefix(slots[i].SlotId);
                    if (EvaluateDirection(entityId, ctx, unityLoader, dirBare, slots[i].Facing, install: false, allowRequest: false))
                    {
                        readyDirections++;
                    }
                }
            }
            return true;
        }

        /// <summary>推进该实体的预热：当前档位有结论则进入下一档位；档位之间遇到有实体正在做转向的方向准备
        /// 则暂停。由 <see cref="UnitySpriteView.AfterSyncPose"/>（每帧）与预热自己发起的加载的完成回调驱动。</summary>
        private void AdvancePrewarm(Id entityId)
        {
            if (!_prewarmByEntity.TryGetValue(entityId, out var state) || state.Completed
                || !_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx)
                || !(_resourceLoader is Adapter.Unity.EngineAdapter.UnityResourceLoader unityLoader))
            {
                return;
            }

            PruneTurnPending();

            while (state.Index < state.Slots.Count)
            {
                if (!state.SlotStarted)
                {
                    if (_turnPendingEntities.Count > 0)
                    {
                        // 让路：真实转向的方向准备优先，预热在档位之间暂停。
                        return;
                    }
                    state.SlotStarted = true;
                }

                var slot = state.Slots[state.Index];
                if (!EvaluateDirection(entityId, ctx, unityLoader, slot.Dir, slot.Facing, install: true, allowRequest: true))
                {
                    return;
                }

                state.Index++;
                state.SlotStarted = false;
            }

            state.Completed = true;
            if (!state.CallbackFired)
            {
                state.CallbackFired = true;
                var callback = state.OnCompleted;
                state.OnCompleted = null;
                callback?.Invoke(entityId, true);
            }
        }

        /// <summary>ADR-0112 B3（粘性）：合成层集合变化（换装）后，被预热过的实体重新走一轮预热——已经加载过的资源
        /// 同步命中，只有新增层的资源真正发起加载。下一次 <see cref="UnitySpriteView.AfterSyncPose"/> 起推进。</summary>
        private void MarkPrewarmDirty(Id entityId)
        {
            if (_prewarmByEntity.TryGetValue(entityId, out var state))
            {
                state.Completed = false;
                state.Index = 0;
                state.SlotStarted = false;
            }
        }

        /// <summary>框架自身测试专用：该实体预热已经"开始"的方向档位数（含正在进行的那一档；已完成 = 总数；
        /// 没有预热记账 = 0）。用于断言"档位之间让路"——转向的方向准备在途时这个数不再增长。</summary>
        internal int DirectionPrewarmStartedSlotCountForTests(Id entityId)
        {
            if (!_prewarmByEntity.TryGetValue(entityId, out var state))
            {
                return 0;
            }
            return state.Completed ? state.Slots.Count : state.Index + (state.SlotStarted ? 1 : 0);
        }

        /// <summary>框架自身测试专用：该实体名下方向准备/预热记账是否已被清空（销毁后不许泄漏）。</summary>
        internal bool HasDirectionPreparationStateForTests(Id entityId) =>
            _dirPrepByEntity.ContainsKey(entityId) || _prewarmByEntity.ContainsKey(entityId) || _turnPendingEntities.Contains(entityId);

        /// <summary>实体销毁/工厂清理：取消预热并清空该实体的全部方向准备记账；预热未完成时回调 <c>false</c>。</summary>
        private void ForgetDirectionPreparation(Id entityId)
        {
            _turnPendingEntities.Remove(entityId);
            _dirPrepByEntity.Remove(entityId);

            if (_directionAwareAnimByEntity.TryGetValue(entityId, out var ctx))
            {
                ctx.View.DirectionPreparer = null;
                ctx.View.AfterSyncPose = null;
            }

            if (_prewarmByEntity.TryGetValue(entityId, out var state))
            {
                _prewarmByEntity.Remove(entityId);
                if (!state.CallbackFired)
                {
                    state.CallbackFired = true;
                    var callback = state.OnCompleted;
                    state.OnCompleted = null;
                    callback?.Invoke(entityId, false);
                }
            }
        }
    }
}
