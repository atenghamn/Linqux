using System.Reflection;
using Linqux.LinqEngine;

namespace Linqux.Engine.Tests;

public class RowItemBuilderTests
{
    private sealed class SampleItem
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Amount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public SampleItem? Parent { get; set; }
        public List<SampleItem> Children { get; set; } = [];
    }

    private static SampleItem[] Samples =>
    [
        new()
        {
            Id = 1,
            Name = "A",
            Amount = 10.5m,
            CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        },
        new()
        {
            Id = 2,
            Name = "B",
            Amount = 20.5m,
            CreatedAt = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
        },
    ];

    [Test]
    public void Build_ExposesOnlyScalarColumns()
    {
        var rows = RowItemBuilder.Build(Samples);

        Assert.That(rows, Has.Count.EqualTo(2));

        var names = rows[0].GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name);
        Assert.That(names, Is.EqualTo(new[] { "Id", "Name", "Amount", "CreatedAt" }));
    }

    [Test]
    public void Build_AllRowsShareTheSameType()
    {
        var rows = RowItemBuilder.Build(Samples);

        Assert.That(rows[1].GetType(), Is.SameAs(rows[0].GetType()));
    }

    [Test]
    public void Build_PopulatesValuesPerColumn()
    {
        var rows = RowItemBuilder.Build(Samples);
        var props = rows[0].GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(props.Single(p => p.Name == "Id").GetValue(rows[0]), Is.EqualTo(1));
            Assert.That(props.Single(p => p.Name == "Name").GetValue(rows[0]), Is.EqualTo("A"));
            Assert.That(props.Single(p => p.Name == "Amount").GetValue(rows[0]), Is.EqualTo(10.5m));
            Assert.That(props.Single(p => p.Name == "CreatedAt").GetValue(rows[0]),
                Is.EqualTo(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

            Assert.That(props.Single(p => p.Name == "Id").GetValue(rows[1]), Is.EqualTo(2));
        }
    }

    [Test]
    public void Build_NullItemStillProducesARowWithNullValues()
    {
        var rows = RowItemBuilder.Build(new SampleItem?[] { Samples[0], null });

        Assert.That(rows, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows[0].GetType().GetProperty("Id")!.GetValue(rows[0]), Is.EqualTo(1));
            Assert.That(rows[1].GetType().GetProperty("Id")!.GetValue(rows[1]), Is.Null);
        }
    }

    [Test]
    public void Build_DeferredEnumerableIsMaterialized()
    {
        var rows = RowItemBuilder.Build(Samples.Where(s => s.Id > 1));

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].GetType().GetProperty("Id")!.GetValue(rows[0]), Is.EqualTo(2));
    }

    [Test]
    public void Build_ScalarResultFallsBackToValueColumn()
    {
        var rows = RowItemBuilder.Build(new[] { 1, 2, 3 });

        Assert.That(rows, Has.Count.EqualTo(3));

        var props = rows[0].GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(props.Select(p => p.Name), Is.EqualTo(new[] { "Value" }));
            Assert.That(props[0].GetValue(rows[0]), Is.EqualTo("1"));
            Assert.That(props[0].GetValue(rows[2]), Is.EqualTo("3"));
        }
    }

    [Test]
    public void Build_EmptyResultReturnsEmptyList()
    {
        var rows = RowItemBuilder.Build(Array.Empty<object>());

        Assert.That(rows, Is.Empty);
    }
}
