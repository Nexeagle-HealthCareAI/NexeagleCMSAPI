using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Application.Services;
using CMSAPI.Authorization;
using CMSAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Vita's emergency takeover in the CMS: every route needs emergency.manage; requests go to 1HMSAPI's /internal/emergency routes with
// the internal key and the CMS user as X-Actor; 1HMSAPI's refusals come back with their message, a key problem never as 401/403.
public class EmergencyTakeoverTests
{
    private sealed class Upstream : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public int Calls;
        public Func<HttpResponseMessage> Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"items\":[]}") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return Respond();
        }
    }

    private static (EmergencyTakeoverService Service, Upstream Upstream) Make(string? key = "internal-key")
    {
        var settings = new Dictionary<string, string?> { ["EasyHmsApi:BaseUrl"] = "https://1hms-dev-api.example.com/", ["ServiceAuth:HealthWikiServiceKey"] = key };
        var upstream = new Upstream();
        var service = new EmergencyTakeoverService(new HttpClient(upstream), new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            NullLogger<EmergencyTakeoverService>.Instance);
        return (service, upstream);
    }

    [Fact]
    public void EveryRoute_NeedsALoginAndTheEmergencyPermission()
    {
        var attributes = typeof(EmergencyTakeoverController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        Assert.Contains(attributes, a => a is not HasPermissionAttribute && a.Policy == null);
        var permission = Assert.Single(attributes.OfType<HasPermissionAttribute>());
        Assert.Equal($"{PermissionConstants.PolicyPrefix}emergency.manage", permission.Policy);
        Assert.DoesNotContain(typeof(EmergencyTakeoverController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
    }

    [Fact]
    public async Task Verify_IsPassedOnWithTheKeyAndTheVerifier()
    {
        var (service, upstream) = Make();
        var body = JsonDocument.Parse("{\"decision\":\"VERIFIED\",\"note\":\"Called the number\"}").RootElement;
        var r = await service.SendAsync(HttpMethod.Post, "internal/emergency/places/5e771000-0000-0000-0000-000000000001/verify", body, "Priya Sen");
        Assert.True(r.Success);
        Assert.Equal("https://1hms-dev-api.example.com/internal/emergency/places/5e771000-0000-0000-0000-000000000001/verify", upstream.Request!.RequestUri!.ToString());
        Assert.Equal("internal-key", upstream.Request.Headers.GetValues("X-Internal-Key").Single());
        Assert.Equal("Priya Sen", upstream.Request.Headers.GetValues("X-Actor").Single());
        Assert.Contains("Called the number", upstream.Body);
    }

    [Fact]
    public async Task A_Refusal_KeepsItsMessage_AndAKeyProblemIsNot401()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StringContent("{\"message\":\"Say why, so the hospital can fix it.\"}", Encoding.UTF8, "application/json") };
        var refused = await service.SendAsync(HttpMethod.Post, "internal/emergency/places/x/verify", null, "Priya");
        Assert.Equal(400, refused.StatusCode);
        Assert.Equal("Say why, so the hospital can fix it.", refused.Message);

        upstream.Respond = () => new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var key = await service.GetAsync("internal/emergency/places", null, "Priya");
        Assert.Equal(502, key.StatusCode);
    }

    [Fact]
    public async Task WithoutAKey_NothingIsCalled()
    {
        var (service, upstream) = Make(key: null);
        var r = await service.GetAsync("internal/emergency/alerts", null, "Priya");
        Assert.Equal(503, r.StatusCode);
        Assert.Equal(0, upstream.Calls);
    }
}
