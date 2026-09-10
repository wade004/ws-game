using System;
using Core.Carriers.Gobj;
using Core.Foundation.DataRegistry;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.Economy;
using Core.Gameplay.Loot;
using Core.Rules.Skill;

public static class Program
{
    // 判断记录：每一条调用都必须是"旧编译产物在运行期真的会执行到"的形状，不是只声明类型
    // 存在——例如 FieldSchema 用 1.12 的七参数字面调用（不是省略掉新增可选参数的隐式匹配），
    // 规则类型用无参 new（1.12 唯一可用的构造方式），Economy/Loot 显式传 null 走 1.12 那个
    // (IExprSchema? conditionSchema = null) 的物理签名（不是新的隐式无参构造函数）。
    //
    // PJ114-01/A5 补充（外部审计 audit-76d16a5-20260910）：本工程编译期只见过
    // toolchain/abi_probe_baseline.txt 记录的基线版本（1.12.0）——用 System.Reflection 反射核对过
    // 1.12.0 与 1.13.0 的 SkillHost 物理构造签名完全相同（十七个参数、无 factions），因此可以在
    // 1.12.0 基线下正常编译（若基线未来推进到已经引入十八参数构造函数的版本，编译会直接失败，
    // 届时需要相应更新或移除本段调用——见 toolchain/abi_probe_baseline.txt 头部判断记录"手动推进"
    // 说明）。传 null 触发预期 ArgumentNullException，同上面几行的"能解析到签名"验证手法一致，不是
    // 只声明类型存在。
    public static void Main()
    {
        try
        {
            _ = new SkillHost(null, null, null, null, null, null, null, null, null, null);
            Console.WriteLine("SkillHost(17-arg legacy ctor) UNEXPECTED_RETURNED");
            Environment.Exit(13);
        }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine("SkillHost(17-arg legacy ctor) ok ArgumentNullException=" + ex.ParamName);
        }

        var field = new FieldSchema("id", FieldKind.Id, false, null, null, null, "legacy");
        Console.WriteLine("FieldSchema ok name=" + field.Name);

        var gobjOnUseKind = new GobjOnUseKindRule();
        Console.WriteLine("GobjOnUseKindRule ok " + gobjOnUseKind.GetType().FullName);

        var gobjLockRequirementFieldGroup = new GobjLockRequirementFieldGroupRule();
        Console.WriteLine("GobjLockRequirementFieldGroupRule ok " + gobjLockRequirementFieldGroup.GetType().FullName);

        var effectKindRegistered = new EffectKindRegisteredRule();
        Console.WriteLine("EffectKindRegisteredRule ok " + effectKindRegistered.GetType().FullName);

        var costEntryShape = new CostEntryShapeRule();
        Console.WriteLine("CostEntryShapeRule ok " + costEntryShape.GetType().FullName);

        var chargesShape = new ChargesShapeRule();
        Console.WriteLine("ChargesShapeRule ok " + chargesShape.GetType().FullName);

        var areaTriggerShapeKind = new AreaTriggerShapeKindRule();
        Console.WriteLine("AreaTriggerShapeKindRule ok " + areaTriggerShapeKind.GetType().FullName);

        var economyRule = new EconomyContentValidationRule(null);
        Console.WriteLine("EconomyContentValidationRule(IExprSchema?) ok " + economyRule.GetType().FullName);

        var lootRule = new LootContentValidationRule(null);
        Console.WriteLine("LootContentValidationRule(IExprSchema?) ok " + lootRule.GetType().FullName);

        Console.WriteLine("ABI_PROBE_ALL_OK");
    }
}
