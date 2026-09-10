namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// ADR-0022（04 第 3.4 节"时间字段单位与作用域"）：字段取值的单位语义。<see cref="Time"/> 标记
    /// 04 第 3.1 节"全部与时间相关的数据字段一律以数据集声明的时间单位计"覆盖的那一类字段——具体是
    /// 秒还是回合，取决于该字段所属表登记的 <see cref="TimeScope"/> 在 <c>found.time_model</c> 里
    /// 声明的 <c>mode</c>（连续=秒、离散=回合），<see cref="FieldUnit"/> 本身不区分。
    /// </summary>
    public enum FieldUnit
    {
        /// <summary>无特殊单位语义（默认）。</summary>
        None,

        /// <summary>以时间模型单位计（连续=秒、离散=回合，见 04 第 3.1 节）。</summary>
        Time,

        /// <summary>百分比（取值语义为 0~1 或 0~100，具体由字段描述说明）。</summary>
        Percent,
    }
}
