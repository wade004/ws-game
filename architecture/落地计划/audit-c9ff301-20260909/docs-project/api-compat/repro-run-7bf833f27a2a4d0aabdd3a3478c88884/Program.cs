using System;
using Core.Foundation.DataRegistry;

public static class Program
{
    public static void Main()
    {
        // Supply all seven parameters present in 1.12 metadata so the emitted
        // call remains the old seven-parameter ABI after optional defaults.
        var field = new FieldSchema("id", FieldKind.Id, false, null, null, null, "legacy");
        Console.WriteLine("FieldSchema name=" + field.Name + " kind=" + field.Kind + " required=" + field.Required);
    }
}
