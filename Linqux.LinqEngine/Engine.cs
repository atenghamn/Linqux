using System.Collections;
using System.Data;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.EntityFrameworkCore;

namespace Linqux.LinqEngine;

public static class Engine
{
    public static async Task<LinqResult> ExecuteQueryAsync(ScaffoldedModel model, string connectionString, string linqQuery)
    {
        var result = new LinqResult();

        try
        {
            var options = new DbContextOptionsBuilder()
                .UseSqlServer(connectionString)
                .Options;

            var dbContext = (DbContext)Activator.CreateInstance(model.DbContextType, options)!;
            await using (dbContext)
            {
                var globals = Activator.CreateInstance(model.GlobalsType, dbContext)!;

                var scriptOptions = ScriptOptions.Default
                    .WithImports(
                        "System",
                        "System.Linq",
                        "System.Collections.Generic",
                        "Microsoft.EntityFrameworkCore",
                        ScaffoldingService.ModelsNamespace
                    )
                    .WithReferences(
                        MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
                        MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location),
                        MetadataReference.CreateFromFile(Assembly.Load("System.Linq").Location),
                        MetadataReference.CreateFromFile(Assembly.Load("System.Linq.Queryable").Location),
                        MetadataReference.CreateFromFile(Assembly.Load("System.Linq.Expressions").Location),
                        MetadataReference.CreateFromFile(typeof(EntityFrameworkQueryableExtensions).Assembly.Location),
                        MetadataReference.CreateFromImage(model.Image)
                    );

                var rawResult = await CSharpScript.EvaluateAsync(linqQuery, scriptOptions, globals: globals);

                if (rawResult is IQueryable queryable)
                {
                    result.GeneratedSql = queryable.ToQueryString();
                }

                if (rawResult is IEnumerable enumerable)
                {
                    result.Data = ConvertToDataTable(enumerable);
                }
            }
        }
        catch (CompilationErrorException ce)
        {
            result.ErrorMessage = "Syntax error in the expression:\n" + string.Join("\n", ce.Diagnostics);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"An error occurred:\n{ex.Message}";
        }

        return result;
    }

    private static DataTable ConvertToDataTable(IEnumerable items)
    {
        var dataTable = new DataTable();
        PropertyInfo[]? properties = null;

        foreach (var item in items)
        {
            if (item == null) continue;

            if (properties == null)
            {
                properties = item.GetType().GetProperties();
                foreach (var prop in properties)
                {
                    Type columnType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    if (!columnType.IsValueType && columnType != typeof(string))
                    {
                        columnType = typeof(string);
                    }

                    dataTable.Columns.Add(prop.Name, columnType);
                }
            }

            var row = dataTable.NewRow();
            foreach (var prop in properties)
            {
                var val = prop.GetValue(item);
                if (val != null && !prop.PropertyType.IsValueType && prop.PropertyType != typeof(string))
                {
                    row[prop.Name] = val.ToString();
                }
                else
                {
                    row[prop.Name] = val ?? DBNull.Value;
                }
            }
            dataTable.Rows.Add(row);
        }

        return dataTable;
    }
}
