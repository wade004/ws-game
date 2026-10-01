using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>读取了另一半（判定型/呈现型）字段或未登记字段时抛出（手感设计/05 第 5 节"防止越界读取"）。</summary>
    public sealed class FeelHalfViolationException : InvalidOperationException
    {
        public FeelHalfViolationException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// 一半手感视图（只读）：判定型视图只能读判定型字段，呈现型视图只能读呈现型字段，读另一半的字段抛
    /// <see cref="FeelHalfViolationException"/>（手感设计/05 第 5 节、第 10 节第 8 条）。两个具体类型
    /// （<see cref="JudgingFeelView"/>/<see cref="PresentingFeelView"/>）没有继承关系可互转，
    /// 规则层只拿到前者，表现层只拿到后者。
    /// </summary>
    public abstract class FeelHalfView
    {
        private readonly ResolvedFeel _owner;
        private readonly FeelHalf _half;
        private readonly string[] _names;

        internal FeelHalfView(ResolvedFeel owner, FeelHalf half)
        {
            _owner = owner;
            _half = half;
            var list = new List<string>();
            var fields = owner.Fields;
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Half == half) list.Add(fields[i].Name);
            }
            _names = list.ToArray();
        }

        public FeelHalf Half => _half;

        public Id UnitId => _owner.UnitId;

        /// <summary>解析版本号（每次重算递增，消费者据此判断是否需要重读）。</summary>
        public int Version => _owner.Version;

        /// <summary>本半全部字段名（登记顺序）。</summary>
        public IReadOnlyList<string> Names => _names;

        /// <summary>该半有此字段（另一半的字段与未登记字段返回 false，不抛异常）。</summary>
        public bool Contains(string field) =>
            _owner.Fields.TryGet(field, out var def) && def.Half == _half;

        private FeelFieldDef Require(string field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (!_owner.Fields.TryGet(field, out var def))
            {
                throw new FeelHalfViolationException($"手感字段 \"{field}\" 未登记");
            }
            if (def.Half != _half)
            {
                throw new FeelHalfViolationException(
                    $"手感字段 \"{field}\" 是{(def.Half == FeelHalf.Judging ? "判定型" : "呈现型")}，不能经{(_half == FeelHalf.Judging ? "判定型" : "呈现型")}视图读取");
            }
            return def;
        }

        /// <summary>标定前的相对值（可选字段未设置为 <see cref="FeelValue.None"/>）。</summary>
        public FeelValue GetRaw(string field)
        {
            Require(field);
            return _owner.GetRaw(field);
        }

        /// <summary>标定后的绝对值（可选字段未设置为 <see cref="FeelValue.None"/>）。</summary>
        public FeelValue GetAbsolute(string field)
        {
            Require(field);
            return _owner.GetAbsolute(field);
        }

        /// <summary>数值字段的绝对值（字段为空或非数值抛异常）。</summary>
        public double GetNumber(string field)
        {
            Require(field);
            return _owner.GetAbsolute(field).AsNumber();
        }

        public bool GetBool(string field)
        {
            Require(field);
            return _owner.GetAbsolute(field).AsBool();
        }

        public string GetText(string field)
        {
            Require(field);
            return _owner.GetAbsolute(field).AsText();
        }

        /// <summary>
        /// 字段已设置则取绝对数值，否则 false（可选字段用）。
        /// </summary>
        public bool TryGetNumber(string field, out double value)
        {
            Require(field);
            var v = _owner.GetAbsolute(field);
            if (v.Kind == FeelValueKind.Number)
            {
                value = v.AsNumber();
                return true;
            }
            value = 0;
            return false;
        }

        /// <summary>该字段是否有 tick 值（只有判定型毫秒字段有）。</summary>
        public bool HasTicks(string field)
        {
            var def = Require(field);
            return def.HasTicks;
        }

        /// <summary>判定型毫秒字段换算后的 tick 数（四舍五入、非零至少 1、零保持零）；字段未设置返回 0。</summary>
        public int GetTicks(string field)
        {
            var def = Require(field);
            if (!def.HasTicks) throw new InvalidOperationException($"手感字段 \"{field}\" 不是判定型毫秒字段，没有 tick 值");
            return _owner.GetTicks(field);
        }

        public override string ToString() => $"{GetType().Name}({_owner.UnitId}, v{_owner.Version}, {_names.Length} 字段)";
    }

    /// <summary>判定型手感视图：规则层（输入缓冲、动作时间线、运动仲裁、命中与受击）只经它读手感。</summary>
    public sealed class JudgingFeelView : FeelHalfView
    {
        internal JudgingFeelView(ResolvedFeel owner) : base(owner, FeelHalf.Judging)
        {
        }
    }

    /// <summary>呈现型手感视图：表现层（镜头、反馈包、音效、步态）只经它读手感。</summary>
    public sealed class PresentingFeelView : FeelHalfView
    {
        internal PresentingFeelView(ResolvedFeel owner) : base(owner, FeelHalf.Presenting)
        {
        }
    }

    /// <summary>
    /// 解析后的手感表（手感设计/05 第 5 节 <c>ResolvedFeel</c>）：不可变；同时携带标定前的相对值
    /// （<see cref="GetRaw"/>）与标定后的绝对值（<see cref="GetAbsolute"/>）、判定型毫秒字段的 tick 值、
    /// 被限幅的字段清单、每个字段的溯源链。动作开始时快照的就是这个对象本身（不可变，之后的重算产生新对象，
    /// 不改动它）。
    /// <para>
    /// 完整对象同时暴露两半，只用于解析器持有者、实验室与测试；业务消费者一律经 <see cref="Judging"/>/
    /// <see cref="Presenting"/> 两个只读视图（或解析器的 <see cref="IFeelJudgingSource"/>/
    /// <see cref="IFeelPresentingSource"/>）读取。
    /// </para>
    /// </summary>
    public sealed class ResolvedFeel
    {
        private readonly FeelValue[] _raw;
        private readonly FeelValue[] _absolute;
        private readonly int[] _ticks;
        private readonly IReadOnlyList<FeelProvenanceEntry>[] _provenance;

        public Id UnitId { get; }

        /// <summary>该单位的解析版本号（首次解析为 1，每次重算 +1）。</summary>
        public int Version { get; }

        public FeelFieldSet Fields { get; }

        /// <summary>被登记范围限幅的字段名（登记顺序）。</summary>
        public IReadOnlyList<string> ClampedFields { get; }

        /// <summary>解析过程的诊断（被忽略的违规写入等；数据经校验时恒为空）。</summary>
        public IReadOnlyList<string> Diagnostics { get; }

        public JudgingFeelView Judging { get; }

        public PresentingFeelView Presenting { get; }

        public ResolvedFeel(
            Id unitId,
            int version,
            FeelFieldSet fields,
            FeelValue[] raw,
            FeelValue[] absolute,
            int[] ticks,
            IReadOnlyList<FeelProvenanceEntry>[] provenance,
            IReadOnlyList<string> clampedFields,
            IReadOnlyList<string> diagnostics)
        {
            Fields = fields ?? throw new ArgumentNullException(nameof(fields));
            if (raw == null || absolute == null || ticks == null || provenance == null)
            {
                throw new ArgumentNullException(nameof(raw));
            }
            if (raw.Length != fields.Count || absolute.Length != fields.Count || ticks.Length != fields.Count || provenance.Length != fields.Count)
            {
                throw new ArgumentException("解析结果数组长度必须等于字段登记数");
            }
            UnitId = unitId;
            Version = version;
            _raw = raw;
            _absolute = absolute;
            _ticks = ticks;
            _provenance = provenance;
            ClampedFields = clampedFields ?? throw new ArgumentNullException(nameof(clampedFields));
            Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            Judging = new JudgingFeelView(this);
            Presenting = new PresentingFeelView(this);
        }

        /// <summary>标定前的相对值（可选字段未设置为 <see cref="FeelValue.None"/>）。</summary>
        public FeelValue GetRaw(string field) => _raw[Index(field)];

        /// <summary>标定后的绝对值。</summary>
        public FeelValue GetAbsolute(string field) => _absolute[Index(field)];

        /// <summary>判定型毫秒字段的 tick 值（其它字段/未设置为 0；先问 <see cref="FeelFieldDef.HasTicks"/>）。</summary>
        public int GetTicks(string field)
        {
            var i = Index(field);
            return _ticks[i] < 0 ? 0 : _ticks[i];
        }

        /// <summary>字段的溯源链（按应用顺序；字段无值时为空）。最后一条的 <c>ValueAfter</c> 等于 <see cref="GetRaw"/>。</summary>
        public IReadOnlyList<FeelProvenanceEntry> GetProvenance(string field) => _provenance[Index(field)];

        private int Index(string field)
        {
            var i = Fields.IndexOf(field);
            if (i < 0) throw new FeelHalfViolationException($"手感字段 \"{field}\" 未登记");
            return i;
        }
    }
}
