using Linqux.LinqEngine;

namespace Linqux.Engine.Tests;

public class ConnectionStringBuilderTests
{
    [Test]
    public void EnableInteractiveAuth_StripsCredentialsAndAddsInteractiveAuthentication()
    {
        var result = ConnectionStringBuilder.EnableInteractiveAuth(
            "Server=tcp:srv.database.windows.net,1433;Initial Catalog=my-db;User ID=bob;Password=secret;Authentication=Active Directory Password;Encrypt=True;");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Server=tcp:srv.database.windows.net,1433"));
            Assert.That(result, Does.Contain("Initial Catalog=my-db"));
            Assert.That(result, Does.Contain("Encrypt=True"));
            Assert.That(result, Does.Contain("Authentication=Active Directory Interactive"));
            Assert.That(result, Does.Not.Contain("bob"));
            Assert.That(result, Does.Not.Contain("secret"));
            Assert.That(result, Does.Not.Contain("Active Directory Password"));
        });
    }

    [Test]
    public void EnableInteractiveAuth_RemovesAccessTokenAndOtherCredentialKeywords()
    {
        var result = ConnectionStringBuilder.EnableInteractiveAuth(
            "Data Source=x;Access Token=abc;UID=bob;pwd=hunter2;Authentication=Active Directory Integrated;Encrypt=False");

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("Data Source=x"));
            Assert.That(result, Does.Contain("Encrypt=False"));
            Assert.That(result, Does.Contain("Authentication=Active Directory Interactive"));
            Assert.That(result, Does.Not.Contain("abc"));
            Assert.That(result, Does.Not.Contain("bob"));
            Assert.That(result, Does.Not.Contain("hunter2"));
            Assert.That(result, Does.Not.Contain("Active Directory Integrated"));
        });
    }

    [Test]
    public void EnableInteractiveAuth_IsCaseInsensitiveForKeywordMatching()
    {
        var result = ConnectionStringBuilder.EnableInteractiveAuth("Server=x;user id=bob;PASSWORD=secret");

        Assert.That(result, Does.Not.Contain("bob"));
        Assert.That(result, Does.Not.Contain("secret"));
        Assert.That(result, Does.Contain("Authentication=Active Directory Interactive"));
    }

    [Test]
    public void EnableInteractiveAuth_EmptyInputStillAddsAuthentication()
    {
        var result = ConnectionStringBuilder.EnableInteractiveAuth("");

        Assert.That(result, Is.EqualTo("Authentication=Active Directory Interactive;"));
    }

    [Test]
    public void EnableInteractiveAuth_DoubleApplicationDoesNotDuplicateKeywords()
    {
        var once = ConnectionStringBuilder.EnableInteractiveAuth("Server=x;Encrypt=True");
        var twice = ConnectionStringBuilder.EnableInteractiveAuth(once);

        Assert.That(twice, Is.EqualTo(once));
    }

    [Test]
    public void UsesInteractiveAuth_DetectsInteractiveModeCaseInsensitively()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConnectionStringBuilder.UsesInteractiveAuth("Server=x;Authentication=Active Directory Interactive;Encrypt=True"), Is.True);
            Assert.That(ConnectionStringBuilder.UsesInteractiveAuth("Server=x;authentication=active directory interactive;Encrypt=True"), Is.True);
            Assert.That(ConnectionStringBuilder.UsesInteractiveAuth("Server=x;Authentication=Active Directory Password;Encrypt=True"), Is.False);
            Assert.That(ConnectionStringBuilder.UsesInteractiveAuth("Server=x;Authentication=Active Directory Integrated;Encrypt=True"), Is.False);
            Assert.That(ConnectionStringBuilder.UsesInteractiveAuth("Server=x;User ID=bob;Encrypt=True"), Is.False);
            Assert.That(ConnectionStringBuilder.UsesInteractiveAuth("Server=x;Encrypt=True"), Is.False);
            Assert.That(ConnectionStringBuilder.UsesInteractiveAuth(""), Is.False);
        });
    }
}
