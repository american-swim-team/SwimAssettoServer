using System;
using FluentValidation;
using JetBrains.Annotations;

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
        RuleFor(x => x.LobbyRelayUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .When(x => !string.IsNullOrEmpty(x.LobbyRelayUrl))
            .WithMessage("LobbyRelayUrl must be a valid absolute URL");
    }
}