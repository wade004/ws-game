using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// ADR-0083 新增：<see cref="Presentation.VfxSfx.Core.SfxPlayer"/> 播放路径的单调累计诊断——
    /// 与本文件既有的 <see cref="IPresentationDiagnostics"/>（"只增不减的文本消息列表"，供
    /// architecture/adr/0042-诊断契约统一转发到宿主控制台.md 的轮询集线器转发）是两套形状不同、
    /// 刻意不合并的契约：消费方反馈第二十五/二十六批指出，音效瞬态播放窗口可能短至几帧（见
    /// <c>Presentation.VfxSfx.Core.SfxPlayer</c> 判断记录 19），消费方轮询"是否正在播放"这一
    /// 引擎侧瞬时状态本来就可能抓不到，需要一个不依赖抓瞬态、只增不减的计数式凭据——错过某一帧
    /// 不会丢失信息（计数只增不减，随时读取都能看到"曾经发生过"），这与 <see
    /// cref="IPresentationDiagnostics"/> 那种"文本消息，按发生顺序追加"的形状不是同一维度的信息，
    /// 因此另起一个独立接口，不是给 <see cref="IPresentationDiagnostics"/> 加新成员（后者已有
    /// <c>VfxPlayer</c>/<c>FeedbackBinder</c>/<c>CompositeFeedbackSink</c>/<c>SpriteCharacterRig</c>
    /// 等多个持有者共享同一套"只有 Warn"的最小契约，强行加播放计数语义对其余持有者无意义）。
    /// <para>
    /// 判断记录（不接入 ADR-0042 统一转发集线器）：该集线器的登记契约是"一个来源名 + 一份只增不减
    /// 的消息列表引用"，按 ADR-0042 决策 2 的设计前提逐帧比较列表长度、转发新增的那一段文本；本
    /// 契约的四个成员是独立计数器与一条可覆写的"最近一次播放"记录，不是消息列表，没有"新增的一段
    /// 文本"可供该集线器逐帧比较转发，结构性不兼容——同 ADR-0042 决策 5 描述的"不适用轮询式转发"
    /// 情形，留给消费方直接读取 <see cref="Presentation.Assembly.PresentationAssembly.SfxPlaybackDiagnostics"/>
    /// 这一透传属性自行处理呈现（如运行期设置面板、外部埋点），不牵强接入集线器制造"看起来已经接好了"
    /// 的假象。
    /// </para>
    /// </summary>
    public interface ISfxPlaybackDiagnostics
    {
        /// <summary>累计 <see cref="Presentation.VfxSfx.Core.SfxPlayer.Play"/> 被调用的次数（含
        /// 后续因未登记/加载失败/超时而未真正播放的请求），只增不减。</summary>
        long PlayRequestedCount { get; }

        /// <summary>累计真正调用 <see cref="Core.Foundation.EngineAdapter.IAudio.PlaySfx"/> 的次数——
        /// 覆盖"资源已加载，立即播放"与"首次冷加载完成后补播放"两条路径（见 <c>SfxPlayer.Play</c>/
        /// <c>SfxPlayer.OnResourceLoadCompleted</c> 判断记录），不覆盖被抢占停止的既有播放（那是
        /// 提前结束一次已经计入本计数的播放，不是"未开始"，见类型注释判断记录"MakeRoomIfNeeded 的
        /// 已知边界"）。只增不减。</summary>
        long PlayStartedCount { get; }

        /// <summary>累计因资源缺失/加载失败/首次加载超时而放弃、从未真正播放的次数——三条路径
        /// （<c>sfx.def</c> 未登记、<see cref="Core.Foundation.EngineAdapter.IResourceLoader.LoadAsync"/>
        /// 回调失败、<c>FirstLoadTimeoutSeconds</c> 到期）统一计入同一个桶，均已各自伴随一条
        /// <see cref="IPresentationDiagnostics.Warn"/> 文本（见 <c>SfxPlayer</c> 判断记录），本计数
        /// 只是同一批事件的数值化镜像，不是新增的判定逻辑。只增不减。</summary>
        long PlayDroppedCount { get; }

        /// <summary>最近一次成功调用 <see cref="Core.Foundation.EngineAdapter.IAudio.PlaySfx"/> 的
        /// 记录；从未成功播放过时为 <c>null</c>。</summary>
        SfxPlaybackRecord? LastPlay { get; }
    }

    /// <summary>一次成功播放的最小凭据：实际播放的资源引用 id（<c>sfx.def.resource_ref</c> 或命中
    /// 的具体 <c>variants</c> 项）+ 该次播放在 <see cref="ISfxPlaybackDiagnostics.PlayStartedCount"/>
    /// 序列中的序号（即产生这条记录时 <see cref="ISfxPlaybackDiagnostics.PlayStartedCount"/> 的取值，
    /// 单调递增、从 1 起数，不单独维护一份重复的序列计数器，见 <c>SfxPlayer</c> 判断记录）。</summary>
    public readonly struct SfxPlaybackRecord
    {
        public Id ResourceRef { get; }

        public long Sequence { get; }

        public SfxPlaybackRecord(Id resourceRef, long sequence)
        {
            ResourceRef = resourceRef;
            Sequence = sequence;
        }
    }

    /// <summary><see cref="ISfxPlaybackDiagnostics"/> 的默认内存实现，供 <c>SfxPlayer</c> 内部持有并
    /// 写入（写入方法为具体类型的公开方法，不属于只读契约本身，同 <see cref="PresentationDiagnosticsRecorder"/>
    /// "读写分离：契约只读、具体类型可写"惯例）。</summary>
    public sealed class SfxPlaybackDiagnosticsRecorder : ISfxPlaybackDiagnostics
    {
        public long PlayRequestedCount { get; private set; }

        public long PlayStartedCount { get; private set; }

        public long PlayDroppedCount { get; private set; }

        public SfxPlaybackRecord? LastPlay { get; private set; }

        public void RecordRequested() => PlayRequestedCount++;

        public void RecordDropped() => PlayDroppedCount++;

        public void RecordStarted(Id resourceRef)
        {
            PlayStartedCount++;
            LastPlay = new SfxPlaybackRecord(resourceRef, PlayStartedCount);
        }
    }
}
