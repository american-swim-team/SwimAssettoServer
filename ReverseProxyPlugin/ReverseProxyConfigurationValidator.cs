using FluentValidation;
using JetBrains.Annotations;
using Serilog;
using System.Text;

namespace ReverseProxyPlugin;

[UsedImplicitly]
public class ReverseProxyConfigurationValidator : AbstractValidator<ReverseProxyConfiguration>
{
    public ReverseProxyConfigurationValidator()
    {
        RuleFor(x => x.ReverseProxyIp).NotEmpty();
        RuleFor(x => x.ReverseHttpPort).NotEmpty();
        RuleFor(x => x.ReverseUdpPort).NotEmpty();
        RuleFor(x => x.ReverseTcpPort).NotEmpty();
    }
}