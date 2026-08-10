namespace Linqux.LinqEngine;

public class LinqResult
{
    public IReadOnlyList<object>? Data { get; set; }
    public string? GeneratedSql { get; set; }
    public string? ErrorMessage { get; set; }
}
