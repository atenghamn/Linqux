using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace Linqux.LinqEngine;

/// <summary>
/// Converts a deferred query result (any <see cref="IEnumerable"/>) into a list of plain objects
/// with one public property per result column, so that Avalonia's DataGrid can auto-generate its
/// columns from the row type (it reflects on <c>Type.GetProperties()</c>, which excludes the
/// dynamic members of <see cref="System.Dynamic.ExpandoObject"/> and the internals of a
/// <see cref="System.Data.DataTable"/>).
/// </summary>
public static class RowItemBuilder
{
    private static readonly ConcurrentDictionary<string, Type> RowTypeCache = new(StringComparer.Ordinal);

    private static readonly Type[] ScalarTypes =
    {
        typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset),
        typeof(DateOnly), typeof(TimeOnly), typeof(Guid), typeof(TimeSpan),
    };

    public static IReadOnlyList<object> Build(IEnumerable items)
    {
        var snapshot = items.Cast<object?>().ToList();

        PropertyInfo[]? properties = null;
        foreach (var item in snapshot)
        {
            if (item == null) continue;
            properties = item.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => IsScalar(p.PropertyType))
                .ToArray();
            break;
        }

        if (properties == null || properties.Length == 0)
        {
            return snapshot
                .Select(value => Activator.CreateInstance(GetRowType(["Value"]), new Dictionary<string, object?> { ["Value"] = value?.ToString() })!)
                .ToList();
        }

        var rowType = GetRowType(properties.Select(p => p.Name).ToArray());
        var rows = new List<object>(snapshot.Count);
        foreach (var item in snapshot)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (item != null)
            {
                foreach (var property in properties)
                {
                    values[property.Name] = property.GetValue(item);
                }
            }

            rows.Add(Activator.CreateInstance(rowType, values)!);
        }

        return rows;
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum || type.IsPrimitive)
        {
            return true;
        }

        return ScalarTypes.Contains(type);
    }

    private static Type GetRowType(string[] columnNames)
    {
        var key = string.Join("\0", columnNames);
        return RowTypeCache.GetOrAdd(key, _ => CreateRowType(columnNames));
    }

    private static Type CreateRowType(string[] columnNames)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("Linqux.Rows"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Linqux.Rows");
        var type = module.DefineType("Row", TypeAttributes.Public | TypeAttributes.Class);
        var dataField = type.DefineField("_data", typeof(Dictionary<string, object?>), FieldAttributes.Private);

        var ctor = type.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(Dictionary<string, object?>) });
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, dataField);
        ctorIl.Emit(OpCodes.Ret);

        var indexerGet = typeof(Dictionary<string, object?>).GetProperty("Item")!.GetMethod!;
        foreach (var columnName in columnNames)
        {
            var getter = type.DefineMethod(
                "get_" + columnName,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                typeof(object),
                Type.EmptyTypes);
            var getterIl = getter.GetILGenerator();
            getterIl.Emit(OpCodes.Ldarg_0);
            getterIl.Emit(OpCodes.Ldfld, dataField);
            getterIl.Emit(OpCodes.Ldstr, columnName);
            getterIl.Emit(OpCodes.Callvirt, indexerGet);
            getterIl.Emit(OpCodes.Ret);

            type.DefineProperty(columnName, PropertyAttributes.None, typeof(object), Type.EmptyTypes)
                .SetGetMethod(getter);
        }

        return type.CreateTypeInfo().AsType();
    }
}
