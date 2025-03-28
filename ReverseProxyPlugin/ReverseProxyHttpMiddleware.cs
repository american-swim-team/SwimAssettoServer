using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ReverseProxyPlugin;

public class ReverseProxyMiddleware
{
    private readonly ReverseProxyConfiguration _config;
    private readonly RequestDelegate _next;

    public ReverseProxyMiddleware(ReverseProxyConfiguration config, RequestDelegate next)
    {
        _config = config;
        _next = next;

        Log.Information("ReverseProxy HTTP middleware initialized!");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/INFO", StringComparison.OrdinalIgnoreCase) || context.Request.Path.StartsWithSegments("/api/details", StringComparison.OrdinalIgnoreCase))
        {
            var originalResponseStream = context.Response.Body;
            using (var memoryStream = new MemoryStream())
            {
                context.Response.Body = memoryStream;

                await _next(context);

                memoryStream.Seek(0, SeekOrigin.Begin);
                var responseBody = new StreamReader(memoryStream).ReadToEnd();

                responseBody = ModifyResponse(responseBody);

                context.Response.Body = originalResponseStream;
                await context.Response.WriteAsync(responseBody);
            }
        }
        else
        {
            await _next(context);
        }
    }

    private string ModifyResponse(string responseBody)
        {
            JObject jsonResponse = JObject.Parse(responseBody);

            if (jsonResponse["cport"] != null)
            {
                jsonResponse["cport"] = $"{_config.ReverseHttpPort}";
            }

            if (jsonResponse["port"] != null)
            {
                jsonResponse["port"] = $"{_config.ReverseUdpPort}";
            }

            if (jsonResponse["tport"] != null)
            {
                jsonResponse["tport"] = $"{_config.ReverseTcpPort}";
            }

            if (jsonResponse["wrappedPort"] != null)
            {
                jsonResponse["wrappedPort"] = $"{_config.ReverseHttpPort}";
            }

            if (jsonResponse["name"] != null)
            {
                string originalName = jsonResponse["name"]!.ToString();

                // Regex pattern to detect "i{PORT_NUMBER}" at the end of the name
                var match = Regex.Match(originalName, @"ℹ(\d+)$");

                if (match.Success)
                {
                    // Extract the old port number
                    string oldPort = match.Groups[1].Value;

                    // Replace the old port number with the new one
                    string modifiedName = Regex.Replace(originalName, $"ℹ{oldPort}$", $"ℹ{_config.ReverseHttpPort}");
                    jsonResponse["name"] = modifiedName;
                }
            }
           
            return jsonResponse.ToString(Formatting.Indented);
        }
}
