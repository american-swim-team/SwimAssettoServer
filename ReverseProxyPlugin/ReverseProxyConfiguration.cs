using AssettoServer.Server.Configuration;
using JetBrains.Annotations;

namespace ReverseProxyPlugin;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class ReverseProxyConfiguration : IValidateConfiguration<ReverseProxyConfigurationValidator>
{
    public string? ReverseProxyIp { get; init; }
    public ushort ReverseHttpPort { get; init; }
    public ushort ReverseUdpPort { get; init; }
    public ushort ReverseTcpPort { get; init; }
}