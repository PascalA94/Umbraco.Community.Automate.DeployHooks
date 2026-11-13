using System.Reflection;
using System.Reflection.Emit;
using Umbraco.Community.Automate.DeployHooks.Http;

namespace Umbraco.Community.Automate.DeployHooks.Tests;

/// <summary>Structural rules for a package that owns secrets (plan Task 9, pitfalls P5, S1, S3, S5).</summary>
public class ArchitectureTests
{
    private static readonly Assembly Package = typeof(DeployHooksAssemblyMarker).Assembly;

    private static Type[] SafeTypes() => Package.GetTypes();

    [Fact]
    public void NoControllersViewsOrEditors()
    {
        var forbidden = new[] { "ControllerBase", "Controller", "SurfaceController", "UmbracoApiController", "ViewComponent", "PageModel", "RazorPage", "DataEditor", "DataValueEditor", "ManagementApiControllerBase" };
        var offenders = SafeTypes()
            .Where(t => BaseChain(t).Any(b => forbidden.Contains(b.Name)))
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(offenders);

        var assets = Package.GetManifestResourceNames().Where(n => n.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(assets);
        Assert.DoesNotContain(SafeTypes(), t => t.GetCustomAttributes().Any(a => a.GetType().Name is "ApiControllerAttribute" or "VersionedApiBackOfficeRouteAttribute"));
    }

    [Fact]
    public void OnlySenders_CallTryGet()
    {
        var tryGet = typeof(DeployHooksCredentialStore).GetMethod(nameof(DeployHooksCredentialStore.TryGet))!;
        var allowed = new[] { typeof(DeployHookSender), typeof(RevalidateSender) };
        var callers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in SafeTypes().Where(t => t != typeof(DeployHooksCredentialStore)))
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)))
            {
                if (CalledMethods(method).Any(called => called == tryGet))
                {
                    callers.Add(Root(type).FullName!);
                }
            }
        }

        // Sanity: the scan finds the real callers, so an empty result can't pass for "nobody calls it".
        Assert.Equal(allowed.Select(t => t.FullName!).Order(), callers.Order());
    }

    private static Type Root(Type type) => type.DeclaringType is { } outer ? Root(outer) : type;

    private static IEnumerable<Type> BaseChain(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    // Walks the IL and yields every method the body calls or takes the address of.
    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null)
        {
            yield break;
        }

        var typeArgs = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
        var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
        for (var i = 0; i < il.Length;)
        {
            var code = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
            var op = OpCodesByValue[code];
            i += op.Size;
            var operandSize = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, i) * 4),
                _ => 4,
            };

            if (op.OperandType == OperandType.InlineMethod)
            {
                MethodBase? resolved = null;
                try
                {
                    resolved = method.Module.ResolveMethod(BitConverter.ToInt32(il, i), typeArgs, methodArgs);
                }
                catch (ArgumentException)
                {
                    // A generic instantiation the module can't resolve here is not a call to the store.
                }

                if (resolved is not null)
                {
                    yield return resolved;
                }
            }

            i += operandSize;
        }
    }
}
