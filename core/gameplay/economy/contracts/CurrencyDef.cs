using Core.Foundation.Common;

namespace Core.Gameplay.Economy
{
    /// <summary>一条 <c>econ.currency</c> 记录的强类型视图（见 08 第 7.1 节字段表 +
    /// 任务书拍板补录 <c>name_key</c>）。</summary>
    public sealed class CurrencyDef
    {
        public Id Id { get; }

        public Id NameKey { get; }

        /// <summary>上限；null 表示无上限（见 08 第 7.1 节 <c>cap: Optional&lt;Int&gt;</c>）。</summary>
        public long? Cap { get; }

        public Id DisplayRef { get; }

        public CurrencyDef(Id id, Id nameKey, long? cap, Id displayRef)
        {
            Id = id;
            NameKey = nameKey;
            Cap = cap;
            DisplayRef = displayRef;
        }
    }
}
