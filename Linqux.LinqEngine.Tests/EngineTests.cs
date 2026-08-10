using Linqux.LinqEngine;

// "Engine" inside namespace "Linqux.LinqEngine.Tests" resolves to the namespace, so alias the type.
using QueryEngine = Linqux.LinqEngine.Engine;

namespace Linqux.Engine.Tests;

public class EngineTests
{
    /// <summary>A host that refuses connections immediately, so query materialization fails fast.</summary>
    private const string UnreachableServer =
        "Server=localhost,1;Database=any;User Id=sa;Password=irrelevant;TrustServerCertificate=True;Connect Timeout=1;";

    private static ScaffoldedModel Model => FakeModelFactory.Create();

    [Test]
    public async Task QueryableResult_GeneratesSql_EvenWhenServerIsUnreachable()
    {
        var result = await QueryEngine.ExecuteQueryAsync(Model, UnreachableServer, "db.Widgets.Take(5)");

        Assert.Multiple(() =>
        {
            Assert.That(result.GeneratedSql, Does.Contain("SELECT"));
            Assert.That(result.GeneratedSql, Does.Contain("[Widgets]"));
            Assert.That(result.GeneratedSql, Does.Contain("TOP"));
            // Enumerating the result hit the unreachable server, but SQL generation already succeeded.
            Assert.That(result.ErrorMessage, Is.Not.Null);
        });
    }

    [Test]
    public async Task SyntaxError_ReturnsFriendlyMessageWithoutQuerying()
    {
        var result = await QueryEngine.ExecuteQueryAsync(Model, UnreachableServer, "db.Widgets.Take(");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Does.Contain("Syntax error"));
            Assert.That(result.ErrorMessage, Does.Contain("CS"));
            Assert.That(result.Data, Is.Null);
            Assert.That(result.GeneratedSql, Is.Null);
        });
    }

    [Test]
    public async Task UnknownMember_ReturnsSyntaxError()
    {
        var result = await QueryEngine.ExecuteQueryAsync(Model, UnreachableServer, "db.NonExistent.Take(1)");

        Assert.That(result.ErrorMessage, Does.Contain("Syntax error"));
        Assert.That(result.Data, Is.Null);
    }

    [Test]
    public async Task ConnectionFailure_ReturnsErrorMessage()
    {
        var result = await QueryEngine.ExecuteQueryAsync(Model, UnreachableServer, "db.Widgets.ToList()");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Does.Contain("An error occurred"));
            Assert.That(result.Data, Is.Null);
            Assert.That(result.GeneratedSql, Is.Null);
        });
    }

    [Test]
    public async Task ScalarExpression_ReturnsNoDataAndNoError()
    {
        var result = await QueryEngine.ExecuteQueryAsync(Model, UnreachableServer, "2 + 2");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.Data, Is.Null);
            Assert.That(result.GeneratedSql, Is.Null);
        });
    }

    [Test]
    public async Task NullExpression_ReturnsNoDataAndNoError()
    {
        var result = await QueryEngine.ExecuteQueryAsync(Model, UnreachableServer, "null");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.Data, Is.Null);
            Assert.That(result.GeneratedSql, Is.Null);
        });
    }

    [Test]
    public async Task QueryBindsAgainstScaffoldedModel()
    {
        var result = await QueryEngine.ExecuteQueryAsync(Model, UnreachableServer, "db.Widgets");

        // If the script could not bind to the scaffolded DbSet, this would be a syntax error.
        Assert.That(result.ErrorMessage, Is.Null.Or.Not.Contain("Syntax error"));
        Assert.That(result.GeneratedSql, Does.Contain("[Widgets]"));
    }
}
