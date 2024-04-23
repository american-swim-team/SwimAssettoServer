using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace AssettoServer.Server.UserGroup;

public class SwimUserGroup : IUserGroup
{
    private readonly HttpClient _httpClient;
    private readonly string _apiUrl;
    private readonly int _roleId;
    public event EventHandler<IUserGroup, EventArgs>? Changed;

    public SwimUserGroup(string apiUrl, int roleId)
    {
        _httpClient = new HttpClient();
        _apiUrl = apiUrl;
        _roleId = roleId;
    }

    public async Task<bool> ContainsAsync(ulong guid)
    {
        var requestObj = new { guid = guid.ToString() };
        var requestJson = JsonSerializer.Serialize(requestObj);
        var content = new StringContent(requestJson, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_apiUrl, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var responseObject = JsonSerializer.Deserialize<ResponseObject>(responseJson);

            return responseObject?.IsMember ?? false;
        }
        catch (Exception ex)
        {
            // Log the exception or handle it as needed
            Log.Error($"Error checking user group membership: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> AddAsync(ulong guid)
    {
        // Implement based on your API's capability to add a user to a group
        throw new NotImplementedException();
    }

    private class ResponseObject
    {
        public bool IsMember { get; set; }
    }

    // Implement IDisposable to properly dispose of the HttpClient
    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}