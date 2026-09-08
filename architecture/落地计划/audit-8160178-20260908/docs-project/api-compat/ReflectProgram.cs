using System;
using System.Linq;
using System.Reflection;

public static class ReflectProgram
{
    public static void Main(string[] args)
    {
        var type = Assembly.LoadFrom(args[0]).GetType("Core.Carriers.Gobj.GameObjectHost");
        foreach (var method in type!.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                     .Where(m => m.Name.Contains("Pending", StringComparison.Ordinal)))
            Console.WriteLine(method);
    }
}
