using System;
using Core.Foundation.DataRegistry;

public static class Program
{
    public static void Main()
    {
        // 1.12.0's public constructor requires the third argument; this keeps
        // the emitted call at the old seven-parameter signature for the ABI probe.
        var field = new FieldSchema("id", FieldKind.Id, false);
        Console.WriteLine("FieldSchema name=" + field.Name + " kind=" + field.Kind + " required=" + field.Required);
    }
}
