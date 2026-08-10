using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.EntityFrameworkCore.SqlServer.Design.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Linqux.LinqEngine;

/// <summary>Holds a runtime-scaffolded, in-memory compiled EF Core model for one database connection.</summary>
public sealed class ScaffoldedModel
{
    public required Assembly Assembly { get; init; }
    public required byte[] Image { get; init; }
    public required Type DbContextType { get; init; }
    public required Type GlobalsType { get; init; }
    public required string ModelsDirectory { get; init; }
    public int EntityCount { get; init; }
}

/// <summary>
/// Scaffolds an EF Core model for any SQL Server/Azure SQL database at runtime using the EF Core
/// design-time services in-process, then compiles the generated code in-memory with Roslyn.
/// Generated sources are cached per connection string in the user profile.
/// </summary>
public static class ScaffoldingService
{
    public const string ModelsNamespace = "Linqux.RuntimeModels";
    private const string DbContextName = "ScaffoldedDbContext";
    private const string GlobalsTypeName = "LinquxQueryGlobals";
    private const string CacheVersion = "2";

    private static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "linqux");

    private static string ModelsCacheDir => Path.Combine(ConfigDir, "models");

    public static async Task<ScaffoldedModel> ScaffoldAsync(string connectionString, bool force = false, CancellationToken ct = default)
    {
        var cacheDir = Path.Combine(ModelsCacheDir, Sha256(connectionString)[..16]);

        if (force)
        {
            DeleteDirectory(cacheDir);
        }
        else
        {
            var marker = Path.Combine(cacheDir, CacheVersionFile);
            if (Directory.Exists(cacheDir) && (!File.Exists(marker) || File.ReadAllText(marker).Trim() != CacheVersion))
            {
                DeleteDirectory(cacheDir);
            }
        }

        if (Directory.Exists(cacheDir) && Directory.EnumerateFiles(cacheDir, "*.cs", SearchOption.AllDirectories).Any())
        {
            return CompileModels(cacheDir);
        }

        Directory.CreateDirectory(cacheDir);
        await ScaffoldInProcessAsync(connectionString, cacheDir, ct);
        WriteSupplementalFiles(cacheDir);
        File.WriteAllText(Path.Combine(cacheDir, CacheVersionFile), CacheVersion);
        return CompileModels(cacheDir);
    }

    private const string CacheVersionFile = ".linqux-cache-version";

    /// <summary>
    /// Runs the EF Core reverse-engineering pipeline (the same one the <c>dotnet ef</c> tool uses)
    /// entirely inside this process, so no command-line tool, build step or child process is needed.
    /// </summary>
    private static async Task ScaffoldInProcessAsync(string connectionString, string modelsDir, CancellationToken ct)
    {
        try
        {
            await Task.Run(() =>
            {
                var services = new ServiceCollection();
                services.AddEntityFrameworkDesignTimeServices();
                new SqlServerDesignTimeServices().ConfigureDesignTimeServices(services);

                using var provider = services.BuildServiceProvider();
                using var scope = provider.CreateScope();
                var scaffolder = scope.ServiceProvider.GetRequiredService<IReverseEngineerScaffolder>();

                var model = scaffolder.ScaffoldModel(
                    connectionString,
                    GetDatabaseModelFactoryOptions(connectionString),
                    new ModelReverseEngineerOptions(),
                    new ModelCodeGenerationOptions
                    {
                        ContextName = DbContextName,
                        ContextNamespace = ModelsNamespace,
                        ModelNamespace = ModelsNamespace,
                        RootNamespace = ModelsNamespace,
                        SuppressOnConfiguring = true,
                        SuppressConnectionStringWarning = true,
                        UseNullableReferenceTypes = true,
                        ProjectDir = modelsDir,
                    });

                WriteScaffoldedFile(modelsDir, model.ContextFile);
                foreach (var file in model.AdditionalFiles)
                {
                    WriteScaffoldedFile(modelsDir, file);
                }
            }, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Scaffolding failed. The database connection string may be wrong, the server unreachable, " +
                "or the account may not have permission to read the database schema.\n\n" + ex.Message, ex);
        }
    }

    private static void WriteScaffoldedFile(string root, ScaffoldedFile file)
    {
        var path = Path.Combine(root, file.Path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, file.Code);
    }

    /// <summary>
    /// Scaffolds only the user schemas, excluding infrastructure schemas such as the per-service
    /// Hangfire schemas that would otherwise produce a flood of duplicate entity types.
    /// Falls back to "all schemas" if the schema list cannot be queried.
    /// </summary>
    private static DatabaseModelFactoryOptions GetDatabaseModelFactoryOptions(string connectionString)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT s.[name]
                FROM sys.schemas s
                WHERE s.[name] NOT IN ('sys', 'INFORMATION_SCHEMA', 'guest')
                  AND s.[name] NOT LIKE 'HangFire%'
                ORDER BY s.[name];
                """;

            var schemas = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                schemas.Add(reader.GetString(0));
            }

            return schemas.Count > 0
                ? new DatabaseModelFactoryOptions(schemas: schemas)
                : new DatabaseModelFactoryOptions();
        }
        catch
        {
            return new DatabaseModelFactoryOptions();
        }
    }

    private static void WriteSupplementalFiles(string modelsDir)
    {
        const string contextPartial = $$"""
                                        namespace {{ModelsNamespace}};

                                        public partial class {{DbContextName}}
                                        {
                                            public {{DbContextName}}(global::Microsoft.EntityFrameworkCore.DbContextOptions options) : base(options) { }
                                        }
                                        """;
        File.WriteAllText(Path.Combine(modelsDir, $"{DbContextName}.OptionsConstructor.cs"), contextPartial);

        const string globals = $$"""
                                 namespace {{ModelsNamespace}};

                                 public class {{GlobalsTypeName}}
                                 {
                                     public {{GlobalsTypeName}}({{DbContextName}} db) { this.db = db; }
                                     public {{DbContextName}} db { get; }
                                 }
                                 """;
        File.WriteAllText(Path.Combine(modelsDir, $"{GlobalsTypeName}.cs"), globals);
    }

    private static ScaffoldedModel CompileModels(string modelsDir)
    {
        var trees = Directory.EnumerateFiles(modelsDir, "*.cs", SearchOption.AllDirectories)
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), new CSharpParseOptions(LanguageVersion.Latest)))
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "Linqux.RuntimeModels",
            trees,
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException("Failed to compile the scaffolded models:\n" + errors);
        }

        var image = ms.ToArray();
        var assembly = Assembly.Load(image);
        var assemblyName = assembly.GetName().Name ?? "Linqux.RuntimeModels";
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            name.Name == assemblyName ? assembly : null;

        var contextType = assembly.GetType($"{ModelsNamespace}.{DbContextName}")
            ?? throw new InvalidOperationException($"Scaffolded DbContext type '{ModelsNamespace}.{DbContextName}' was not found.");
        var globalsType = assembly.GetType($"{ModelsNamespace}.{GlobalsTypeName}")
            ?? throw new InvalidOperationException($"Scaffolded globals type '{ModelsNamespace}.{GlobalsTypeName}' was not found.");

        var entityCount = assembly.GetExportedTypes().Count(t => t != contextType && t != globalsType);

        return new ScaffoldedModel
        {
            Assembly = assembly,
            Image = image,
            DbContextType = contextType,
            GlobalsType = globalsType,
            ModelsDirectory = modelsDir,
            EntityCount = entityCount,
        };
    }

    private static IEnumerable<MetadataReference> BuildReferences()
    {
        var names = new[]
        {
            "System.Runtime",
            "System.Collections",
            "System.Linq",
            "System.Linq.Queryable",
            "System.Linq.Expressions",
            "System.ComponentModel",
            "System.ComponentModel.Annotations",
            "System.Data.Common",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.EntityFrameworkCore.Relational",
            "Microsoft.EntityFrameworkCore.SqlServer",
        };

        var assemblies = new List<Assembly>();
        foreach (var name in names)
        {
            try
            {
                assemblies.Add(Assembly.Load(name));
            }
            catch
            {
                // Some System assemblies are not present on all runtimes; ignore.
            }
        }

        assemblies.Add(typeof(EntityFrameworkQueryableExtensions).Assembly);

        assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location)));

        return assemblies
            .DistinctBy(a => a.GetName().Name)
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();
    }

    private static string Sha256(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes);
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(path, recursive: true);
    }
}
