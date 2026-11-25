using System.Text.Json;

namespace Platform.EventBus;

/// <summary>Єдині правила JSON для publisher і consumer: camelCase, enum — рядками, без чутливості до регістру при читанні.</summary>
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
