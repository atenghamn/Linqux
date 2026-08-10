using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Linqux.LinqEngine;

namespace Linqux.Engine.Tests;

/// <summary>
/// Builds a minimal <see cref="ScaffoldedModel"/> (compiled in-memory with Roslyn, mirroring
/// <see cref="ScaffoldingService"/>) so <see cref="Engine"/> can be exercised without a database.
/// </summary>
internal static class FakeModelFactory
{
    public const string Namespace = "Linqux.RuntimeModels";

    private const string AssemblyName = "Linqux.RuntimeModels.Tests";

    private static System.Reflection.Assembly? _latestAssembly;
    private static int _resolverAttached;

    public static ScaffoldedModel Create()
    {
        var sources = new[]
        {
            $$"""
            using Microsoft.EntityFrameworkCore;

            namespace {{Namespace}};

            public partial class ScaffoldedDbContext : DbContext
            {
                public ScaffoldedDbContext(DbContextOptions options) : base(options) { }

                public DbSet<Widget> Widgets { get; set; }

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<Widget>().ToTable("Widgets");
                }
            }
            """,
            $$"""
            using System;

            namespace {{Namespace}};

            public partial class Widget
            {
                public int Id { get; set; }
                public string? Name { get; set; }
                public decimal Amount { get; set; }
            }
            """,
            $$"""
            namespace {{Namespace}};

            public class LinquxQueryGlobals
            {
                public LinquxQueryGlobals(ScaffoldedDbContext db) { this.db = db; }
                public ScaffoldedDbContext db { get; }
            }
            """,
        };

        var trees = sources.Select(s =>
            CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)));

        var compilation = CSharpCompilation.Create(
            AssemblyName,
            trees,
            BuildReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
        {
            throw new InvalidOperationException(string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));
        }

        var image = ms.ToArray();
        var assembly = Assembly.Load(image);

        // Mirror ScaffoldingService: a single resolver returning the latest in-memory models
        // assembly, so the script engine can resolve the model by name after re-scaffolding.
        _latestAssembly = assembly;
        if (Interlocked.Exchange(ref _resolverAttached, 1) == 0)
        {
            AssemblyLoadContext.Default.Resolving += (_, name) =>
                name.Name == AssemblyName ? _latestAssembly : null;
        }

        return new ScaffoldedModel
        {
            Assembly = assembly,
            Image = image,
            DbContextType = assembly.GetType($"{Namespace}.ScaffoldedDbContext")!,
            GlobalsType = assembly.GetType($"{Namespace}.LinquxQueryGlobals")!,
            ModelsDirectory = "",
            EntityCount = 1,
        };
    }

    private static IEnumerable<MetadataReference> BuildReferences()
    {
        var keyTypes = new[]
        {
            typeof(object),
            typeof(System.Collections.Generic.List<>),
            typeof(System.Linq.Enumerable),
            typeof(System.Linq.Expressions.Expression),
            typeof(System.ComponentModel.INotifyPropertyChanged),
            typeof(Microsoft.EntityFrameworkCore.DbContext),
            typeof(Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions),
            typeof(Microsoft.EntityFrameworkCore.RelationalEntityTypeBuilderExtensions),
        };

        var assemblies = keyTypes
            .Select(t => t.Assembly)
            .Concat(AppDomain.CurrentDomain.GetAssemblies())
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .DistinctBy(a => a.GetName().Name)
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToList();

        return assemblies;
    }
}
