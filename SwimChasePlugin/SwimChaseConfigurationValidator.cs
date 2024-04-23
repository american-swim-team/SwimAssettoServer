using FluentValidation;
using JetBrains.Annotations;
using Serilog;
using System.Text;

namespace SwimWhitelistPlugin;

[UsedImplicitly]
public class SwimWhitelistConfigurationValidator : AbstractValidator<SwimWhitelistConfiguration>
{
    private HttpClient _httpClient = new HttpClient();

    private async Task<bool> EndpointUrlExists(Uri? endpointUrl, CancellationToken cancellationToken)
    {
        if (endpointUrl is null)
        {
            return false;
        }

        try
        {
            var response = await _httpClient.GetAsync(endpointUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check if endpoint URL exists");
            return false;
        }
    }

    public SwimWhitelistConfigurationValidator()
    {
        RuleFor(x => x.EndpointUrl)
            .NotNull()
            .WithMessage("Endpoint URL is required")
            .MustAsync(EndpointUrlExists)
            .WithMessage("Endpoint URL does not exist");
        RuleFor(x => x.CollisionsPer25)
            .InclusiveBetween(1, 100)
            .WithMessage("Collisions per 25 must be between 1 and 100");
        RuleFor(x => x.CaptureTime)
            .InclusiveBetween(1, 100)
            .WithMessage("Capture time must be between 1 and 100");
        RuleFor(x => x.CaptureRadius)
            .InclusiveBetween(1, 100)
            .WithMessage("Capture radius must be between 1 and 100");
        RuleFor(x => x.DetectionRadius)
            .InclusiveBetween(1, 100)
            .WithMessage("Detection radius must be between 1 and 100");
    }
}