using System;
using System.Linq;
using System.Reflection;

var asm = Assembly.LoadFrom("/home/felix/.nuget/packages/modelcontextprotocol.aspnetcore/2.0.0-preview.1/lib/net10.0/ModelContextProtocol.AspNetCore.dll");
var t = asm.GetType("Microsoft.AspNetCore.Builder.McpEndpointRouteBuilderExtensions");
Console.WriteLine($"=== {t?.FullName} ===");
foreach (var m in t!.GetMethods(BindingFlags.Public|BindingFlags.Static))
{
    var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
    Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({parms})");
}
