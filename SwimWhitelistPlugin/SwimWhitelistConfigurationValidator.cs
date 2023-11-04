using FluentValidation;
using JetBrains.Annotations;
using Serilog;
using System.Text;

namespace SwimWhitelistPlugin;

[UsedImplicitly]
public class SwimWhitelistConfigurationValidator : AbstractValidator<SwimWhitelistConfiguration>
{
    private HttpClient _httpClient = new HttpClient();

    public SwimWhitelistConfigurationValidator()
    {
        RuleFor(x => x.EndpointUrl).NotEmpty()
        .Must(endpointUrl => {
            try
            {
                // The JSON structure copied from SwimWhitelist.cs, assuming this is the expected structure
                var jsonPayload = "{\"roles\": [1111111111111111111], \"steamid\": 1111111111111111111}";
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // Use .Result or .GetAwaiter().GetResult() to run synchronously
                var response = _httpClient.PostAsync(endpointUrl, content).Result;
                return response.StatusCode == System.Net.HttpStatusCode.OK || 
                   response.StatusCode == System.Net.HttpStatusCode.ExpectationFailed;
            }
            catch (Exception ex)
            {
                // Log the exception if necessary
                Log.Error(ex, "API is not reachable. {Message}", ex.Message);
                return false;
            }
        }).WithMessage("The API endpoint specified is not reachable.");
        RuleFor(x => x.ReservedSlots).GreaterThanOrEqualTo(0).WithMessage("Reserved slots cannot be negative.");
        RuleForEach(x => x.ReservedCars).ChildRules(sr => 
        {
            sr.RuleFor(x => x.Model).NotEmpty().WithMessage("Car name cannot be empty.");
            sr.RuleFor(x => x.Amount).GreaterThanOrEqualTo(0).WithMessage("Reserved slots cannot be negative.");
            sr.RuleFor(x => x.Roles).NotEmpty().WithMessage("At least one role must be specified.");
        });
    }
}
