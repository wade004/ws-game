using System;

namespace Core.Foundation.AppLifecycle
{
    /// <summary>
    /// InWorld 子状态的统一标识：内置枚举值（<see cref="InWorldSubState"/>）与游戏层扩展的
    /// 自定义子状态名都映射到本类型（见任务书"子状态用字符串 id 表示；为此子状态实际用
    /// SubStateId（readonly struct，内置枚举值与自定义名都映射到它）表达，枚举只是内置值的
    /// 便捷入口"）。内置值转换为其 <see cref="InWorldSubState"/> 名字的字符串形式（例如
    /// <see cref="InWorldSubState.Combat"/> → <c>"Combat"</c>），自定义值取调用方传入的原始
    /// 名字；两者按名字做相等比较，不区分"来自内置枚举"还是"来自自定义字符串"——这样
    /// 游戏层新增一个自定义子状态不会与内置值产生命名空间上的特殊差异。
    /// </summary>
    public readonly struct SubStateId : IEquatable<SubStateId>
    {
        public static readonly SubStateId Explore = new SubStateId(InWorldSubState.Explore);
        public static readonly SubStateId Combat = new SubStateId(InWorldSubState.Combat);
        public static readonly SubStateId Dialog = new SubStateId(InWorldSubState.Dialog);
        public static readonly SubStateId MenuOverlay = new SubStateId(InWorldSubState.MenuOverlay);
        public static readonly SubStateId Cutscene = new SubStateId(InWorldSubState.Cutscene);

        /// <summary>规范名字：内置值取枚举名字符串，自定义值取原始传入名字。</summary>
        public string Name { get; }

        public SubStateId(InWorldSubState builtin)
        {
            Name = builtin.ToString();
        }

        /// <summary>由自定义子状态名构造（见 <see cref="AppStateMachineConfig.AddCustomSubState"/>）。</summary>
        public SubStateId(string customName)
        {
            if (string.IsNullOrWhiteSpace(customName))
            {
                throw new ArgumentException("自定义子状态名不能为空", nameof(customName));
            }

            Name = customName;
        }

        public bool Equals(SubStateId other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is SubStateId other && Equals(other);

        public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

        public override string ToString() => Name ?? string.Empty;

        public static bool operator ==(SubStateId left, SubStateId right) => left.Equals(right);

        public static bool operator !=(SubStateId left, SubStateId right) => !left.Equals(right);

        /// <summary>内置枚举值到 <see cref="SubStateId"/> 的隐式转换，供
        /// <c>PushSubState(InWorldSubState.Combat)</c> 这类调用直接写枚举值。</summary>
        public static implicit operator SubStateId(InWorldSubState builtin) => new SubStateId(builtin);
    }
}
