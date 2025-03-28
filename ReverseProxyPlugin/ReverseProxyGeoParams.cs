using AssettoServer.Server.GeoParams;
using Newtonsoft.Json;

namespace ReverseProxyPlugin;

public class ReverseProxyGeoParamsProvider : IGeoParamsProvider {

    private readonly HttpClient _httpClient;
    private readonly ReverseProxyConfiguration _config;


    public ReverseProxyGeoParamsProvider(HttpClient httpClient, ReverseProxyConfiguration config) {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<GeoParams?> GetAsync()
    {
        var response = await _httpClient.GetAsync($"http://ip-api.com/json/{_config.ReverseProxyIp}");

        if (response.IsSuccessStatusCode)
        {
            string jsonString = await response.Content.ReadAsStringAsync();
            Dictionary<string, string> json = JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString) ?? throw new JsonException("Cannot deserialize ip-api.com response");
            return new GeoParams
            {
                Ip = json["query"],
                City = json["city"],
                Country = json["country"],
                CountryCode = json["countryCode"]
            };
        }

        return null;
    }
}