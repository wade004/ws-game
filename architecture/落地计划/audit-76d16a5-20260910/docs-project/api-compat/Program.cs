using System;
using System.Runtime.CompilerServices;
using Core.Rules.Skill;

public static class Program
{
    public static void Main()
    {
        try
        {
            InvokeLegacyConstructor();
            Console.WriteLine("SKILLHOST_LEGACY_CALL_RETURNED");
            Environment.ExitCode = 2;
        }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine("SKILLHOST_LEGACY_SIGNATURE_FOUND ArgumentNullException=" + ex.ParamName);
            Environment.ExitCode = 0;
        }
        catch (MissingMethodException ex)
        {
            Console.WriteLine("SKILLHOST_LEGACY_SIGNATURE_MISSING " + ex.Message);
            Environment.ExitCode = 11;
        }
        catch (TypeLoadException ex)
        {
            Console.WriteLine("SKILLHOST_LEGACY_TYPELOAD_FAILURE " + ex.Message);
            Environment.ExitCode = 12;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeLegacyConstructor()
    {
        // 编译时只引用 1.13.0：省略 7 个可选实参会把物理 17 参数签名固化到 IL。
        _ = new SkillHost(null, null, null, null, null, null, null, null, null, null);
    }
}
