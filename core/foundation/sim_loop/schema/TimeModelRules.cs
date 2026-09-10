using Core.Foundation.DataRegistry;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// ADR-0022（04 第 3.4 节"时间字段单位与作用域"）："时间字段与时间模型一致"校验规则内部判定
    /// （TableSchema.TimeScope、FieldSchema.Unit）的公开只读入口——此前该判定只存在于
    /// <see cref="TimeFieldConsistencyRule"/> 与各装配层手写的 <see cref="TimeFieldDeclaration"/>
    /// 列表内部（见该类型判断记录"04 第 3.1 节…改为由各表登记 Unit=时间驱动"），消费方（编辑器等
    /// 工具）拿不到"这张表/这个字段是否属于时间模型"的判断，只能按表名首段猜（消费方反馈第 15 条）。
    /// 本类型把同一份元数据（登记在 <see cref="TableSchema"/>/<see cref="FieldSchema"/> 上，单一
    /// 来源）暴露为公开静态方法，供框架内部规则与外部工具共用，不需要各自维护一份判定逻辑。
    /// </summary>
    public static class TimeModelRules
    {
        /// <summary>某张表登记的时间模型作用域（04 第 3.1 节"探索/战斗"，见
        /// <see cref="TableSchema.TimeScope"/>）。</summary>
        public static TimeScope GetTimeScope(TableSchema table) => table.TimeScope;

        /// <summary>某个字段是否为 04 第 3.1 节"全部与时间相关的数据字段"覆盖的时间字段（见
        /// <see cref="FieldSchema.Unit"/>）。</summary>
        public static bool IsTimeField(FieldSchema field) => field.Unit == FieldUnit.Time;
    }
}
