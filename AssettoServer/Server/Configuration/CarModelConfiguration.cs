namespace AssettoServer.Server.Configuration;

/// <summary>
/// Per-model configuration extracted from entry_list.ini for dynamic car selection.
/// </summary>
public class CarModelConfiguration
{
    public required string Model { get; init; }
    public float Ballast { get; init; }
    public int Restrictor { get; init; }
    public string? LegalTyres { get; init; }
    public string? DefaultSkin { get; init; }
}
