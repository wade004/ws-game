using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 精灵实例的不透明句柄（对应 02_引擎适配层.md 记法约定中的 Handle 类型）。
    /// 内部只是一个自增编号，不承载任何引擎符号。
    /// </summary>
    public readonly struct SpriteHandle : System.IEquatable<SpriteHandle>
    {
        public int Value { get; }

        public SpriteHandle(int value) => Value = value;

        public bool Equals(SpriteHandle other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is SpriteHandle other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(SpriteHandle left, SpriteHandle right) => left.Equals(right);

        public static bool operator !=(SpriteHandle left, SpriteHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// 粒子播放实例的不透明句柄。文档 02 第 1.3 节把 emitParticle/stopParticle 的返回/参数类型
    /// 标注为 Id，但语义上它是运行时分配的播放实例编号，不满足 Id 的 "领域.名称" 数据行格式，
    /// 因此按拍板的句柄处理原则改为独立句柄类型（见本文件末尾"判断记录"或任务汇报第 5 节）。
    /// </summary>
    public readonly struct ParticleHandle : System.IEquatable<ParticleHandle>
    {
        public int Value { get; }

        public ParticleHandle(int value) => Value = value;

        public bool Equals(ParticleHandle other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ParticleHandle other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(ParticleHandle left, ParticleHandle right) => left.Equals(right);

        public static bool operator !=(ParticleHandle left, ParticleHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// 粒子发射的固定混合模式（ADR-0074）：整次发射从头到尾的固定属性，不是需要逐帧/播放期间改动
    /// 的着色器参数——因此只在 <see cref="IRenderer2D"/> 新增的 <c>EmitParticle</c> 发射重载携带，
    /// 不经 <see cref="IRenderer2D.SetShaderParam"/> 一类"对已创建实例持续设参"的通道传递（该通道
    /// 只认 <see cref="SpriteHandle"/>，本就不接受 <see cref="ParticleHandle"/>，见 ADR-0074
    /// "备选方案与为什么不选"）。
    /// </summary>
    public enum VfxBlendMode
    {
        /// <summary>逐帧覆盖（标准透明度混合），今天的默认且唯一行为。</summary>
        Alpha,

        /// <summary>叠加变亮，常见于法术/能量类特效的"发光"观感。</summary>
        Additive,
    }

    /// <summary>
    /// 精灵实例句柄、分层与 Y 排序、纸娃娃层合成、粒子发射、着色器参数
    /// （见 02_引擎适配层.md 第 1.3 节）。必需接口。采用保留句柄模式：CreateSpriteInstance
    /// 创建一个持久化的精灵实例并返回句柄，后续通过句柄更新分层/变换/着色器参数，
    /// 直到 DestroySpriteInstance 释放。镜头变换与震屏见 ICamera（1.13 节）。
    /// </summary>
    public interface IRenderer2D
    {
        SpriteHandle CreateSpriteInstance(Id spriteSetId);

        /// <summary>设置/更新该实例当前应绘制的层集合（例如角色的身体、装备、武器等纸娃娃分层）。</summary>
        void SetLayers(SpriteHandle handle, IReadOnlyList<Id> layers);

        /// <summary>
        /// height：跳跃/击飞/悬浮类效果的纵向绘制偏移，只平移绘制位置，不参与 sortY 排序、
        /// 不平移影子（语义见 09_表现层.md 第 3.4 节，与 IRenderer3D.SetPlacement 的 height
        /// 参数对齐，见 ADR-0016 决策 2）；sortY：同层内按纵坐标排序，用于 2.5D 的前后遮挡，
        /// 且与 IRenderer3D 模型实例的 sortY 共享同一排序空间；layer：离散图层。
        /// </summary>
        void SetTransform(SpriteHandle handle, Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX);

        /// <summary>
        /// 参数含义由 DisplayInfo 映射决定，本接口不理解参数语义；不承载高度这类已有正式
        /// 参数通路的量——高度一律经 SetTransform 的 height 参数传递（见 ADR-0016 决策 2）。
        /// </summary>
        void SetShaderParam(SpriteHandle handle, string paramName, double value);

        /// <summary>
        /// GP-PRES-05 收口新增（09 第 3.4 节"每个可见单位默认携带一个地面投影影子"）：设置该 sprite
        /// 实例的地面影子呈现模式，与 <see cref="IRenderer3D.SetShadow"/> 共享同一个
        /// <see cref="ShadowMode"/> 枚举、同样的语义——影子锚定在逻辑平面坐标（<see cref="SetTransform"/>
        /// 的 <c>position</c>，不随 <c>height</c> 偏移，见该方法参数注释"不平移影子"）。
        /// <see cref="ShadowMode.Projected"/> 在纯 2D 渲染管线下允许降级为
        /// <see cref="ShadowMode.Blob"/>（09 明确 model 路线的投影阴影是可选能力，sprite 路线未
        /// 强制要求真正的投影阴影几何），降级时实现应记诊断，不静默；不是必需接口的实现可以直接
        /// 忽略本调用（no-op），但不应该抛异常——"没有影子"本身也是 <see cref="ShadowMode.None"/>
        /// 的合法表现。
        /// </summary>
        void SetShadow(SpriteHandle handle, ShadowMode mode);

        void DestroySpriteInstance(SpriteHandle handle);

        ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters);

        /// <summary>
        /// ADR-0074 新增重载：承载 <c>vfx.def.blend_mode</c>（<see cref="VfxBlendMode"/>）。带默认实现
        /// （C# 8+ default interface member，同 <c>presentation/assembly/tests/
        /// InterfaceDefaultMemberForwardingTests.cs</c> 通用门禁惯例）——默认转发到不带
        /// <paramref name="blendMode"/> 的既有重载，即按 <see cref="VfxBlendMode.Alpha"/> 处理，与该重载
        /// 改动前逐字一致；这保证本仓库之外、只实现了旧三参重载的既有 <see cref="IRenderer2D"/> 实现方
        /// 不需要改一行代码即可继续编译通过（ABI 只加法）。本仓库内的具体实现
        /// （<c>UnityRenderer2D</c>/<c>StubRenderer2D</c>）显式覆盖本方法，不依赖默认转发。
        /// </summary>
        ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters, VfxBlendMode blendMode) =>
            EmitParticle(effectId, position, parameters);

        void StopParticle(ParticleHandle handle);
    }
}
