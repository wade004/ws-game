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
        SfxHandle PlaySfx(Id soundId, double volume, double pitch);

        void StopSfx(SfxHandle handle);

        void PlayMusic(Id trackId, double fadeInSeconds, bool loop);

        void StopMusic(double fadeOutSeconds);

        void SetBusVolume(AudioBus bus, double volume);
    }
}
