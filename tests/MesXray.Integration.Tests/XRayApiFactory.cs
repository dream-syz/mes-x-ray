using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MesXray.Integration.Tests;

/// <summary>
/// Boots the real API (scanners + curated graph + fixture runtime) once per test class. Configuration is the shipped
/// appsettings: rule-based AI provider, fixture runtime, no network.
/// </summary>
public sealed class XRayApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("XRay:AI:Provider", "rules");
    }

    public static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonNode.Parse(text) ?? throw new InvalidOperationException($"Empty JSON body (status {(int)response.StatusCode}).");
    }

    public static StringContent Json(object body)
        => new(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
}
