using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 一次音效播放实例的不透明句柄。文档 02 第 1.4 节把 playSfx/stopSfx 的返回/参数类型
    /// 标注为 Id，但语义上是运行时分配的播放实例编号（同一个 soundId 可以并发播放多次，
    /// 每次拿到不同编号），不满足 Id 的数据行格式，故按句柄处理（见任务汇报第 5 节判断记录）。
    /// </summary>
    public readonly struct SfxHandle : System.IEquatable<SfxHandle>
    {
        public int Value { get; }

        public SfxHandle(int value) => Value = value;

        public bool Equals(SfxHandle other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is SfxHandle other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(SfxHandle left, SfxHandle right) => left.Equals(right);

        public static bool operator !=(SfxHandle left, SfxHandle right) => !left.Equals(right);
    }

    /// <summary>音频总线分组：音乐/音效/界面/环境。</summary>
    public enum AudioBus
    {
        Music,
        Sfx,
        Ui,
        Ambient
    }

    /// <summary>
    /// 音效、音乐、分层叠放、对象池（见 02_引擎适配层.md 第 1.4 节）。必需接口。
    /// 音效对象池由实现方内部管理，接口调用方不感知池的存在。
    /// </summary>
    public interface IAudio
    {
        /// <summary>
        /// position 为空时按无空间衰减方式播放（与既有行为一致，向后兼容）；非空时允许实现
        /// 按二维位置做空间衰减/声像（对应 09_表现层.md 第 5.3 节 SfxPlayer.Play 的 at 参数）；
        /// 不支持空间音频的实现可以忽略该参数按无空间方式播放，但不得因该参数报错或拒绝播放
        /// （见 ADR-0016 决策 3）。
        /// </summary>
        SfxHandle PlaySfx(Id soundId, double volume, double pitch, Vec2? position);

        /// <summary>
        /// ADR-0089 新增：默认接口成员（ABI 只加法，见 <see cref="Core.Foundation.Common"/> 命名空间
        /// 下同惯例的既有默认成员）——<paramref name="loop"/> 为 true 时按循环方式播放，直到调用方
        /// 显式 <see cref="StopSfx"/>。默认体转调不带 <paramref name="loop"/> 的既有重载、忽略
        /// <paramref name="loop"/>（不满足循环播放能力的实现退化为一次性播放，而不是抛异常或报错
        /// 拒绝播放，同 <paramref name="position"/> 参数"不支持可忽略"的既有惯例，见该参数判断
        /// 记录）：未覆写本成员的既有 <see cref="IAudio"/> 实现方不需要改一行代码即可继续编译、运行。
        /// <c>adapters/unity</c>（<c>AudioSource.loop = true</c>）与 <c>adapters/stub</c> 均已覆写
        /// 为真正的循环播放。
        /// </summary>
        SfxHandle PlaySfx(Id soundId, double volume, double pitch, Vec2? position, bool loop) =>
            PlaySfx(soundId, volume, pitch, position);

        void StopSfx(SfxHandle handle);

        void PlayMusic(Id trackId, double fadeInSeconds, bool loop);

        void StopMusic(double fadeOutSeconds);

        void SetBusVolume(AudioBus bus, double volume);
    }
}
