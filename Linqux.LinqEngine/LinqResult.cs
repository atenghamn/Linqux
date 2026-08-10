using System.Data;

namespace Linqux.LinqEngine;

public class LinqResult
{
    public DataTable? Data { get; set; }
    public string? GeneratedSql { get; set; }
    public string? ErrorMessage { get; set; }
}