using System.Collections.Generic;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 模块专属校验规则的扩展点（见 04 第 5 节"效果数上限""预算超标""叠加类别冲突""循环引用
    /// 检测""孤儿记录检测"等由各内容模块自行登记的检查项；本模块——L0
    /// <c>data_registry</c>——不实现任何具体业务规则，只提供注册入口
    /// <see cref="IDataRegistry.RegisterValidationRule"/>）。实现方在 <see cref="Validate"/>
    /// 内部经 <see cref="IDataRegistryView"/> 只读查询已加载数据、产出问题列表；不得修改数据。
    /// </summary>
    public interface IValidationRule
    {
        IEnumerable<ValidationIssue> Validate(IDataRegistryView view);
    }
}
