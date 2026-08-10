using System.Text.Json;

namespace Linqux.LinqEngine;

public sealed record SavedConnection(string Name, string ConnectionString);

/// <summary>
/// Persists saved connection strings outside the repository (user profile), so they are never
/// committed to source control and can be reused without re-entering the details each launch.
/// </summary>
public static class ConnectionConfigStore
{
    private static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "linqux");

    private static string FilePath => Path.Combine(ConfigDir, "connections.json");

    public static List<SavedConnection> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<SavedConnection>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(List<SavedConnection> connections)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(connections, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public static void Delete(string name)
    {
        var connections = Load();
        connections.RemoveAll(c => c.Name == name);
        Save(connections);
    }
}
