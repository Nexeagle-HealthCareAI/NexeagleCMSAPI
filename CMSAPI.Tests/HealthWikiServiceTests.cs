using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class HealthWikiServiceTests
{
    private const string Key = "hwk_test_key_1234567890";

    // A stand-in for 1HMSAPI: remembers what it was sent and answers with whatever the test says.
    private sealed class Upstream : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public string? ContentType;
        public int Calls;
        public Func<HttpResponseMessage> Respond = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        public Exception? Throw;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Request = request;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Throw != null) throw Throw;
            return Respond();
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (HealthWikiService Service, Upstream Upstream) Make(string? key = Key, string? baseUrl = "https://1hms-dev-api.example.com/")
    {
        var settings = new Dictionary<string, string?> { ["EasyHmsApi:BaseUrl"] = baseUrl, ["ServiceAuth:HealthWikiServiceKey"] = key };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var upstream = new Upstream();
        return (new HealthWikiService(new HttpClient(upstream), config, NullLogger<HealthWikiService>.Instance), upstream);
    }

    // ------------------------------------------------------------------------------------------ configuration

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<set-via-env-ServiceAuth-HealthWikiServiceKey>")]
    public async Task WithoutAKey_ItAnswers503_AndCallsNothing(string? key)
    {
        var (service, upstream) = Make(key);

        var r = await service.GetAsync("internal/health-wiki/summary", null, "Priya");

        Assert.False(r.Success);
        Assert.Equal(503, r.StatusCode);
        Assert.Equal(0, upstream.Calls);
    }

    [Fact]
    public async Task WithoutAnAddress_ItAnswers503()
    {
        var (service, upstream) = Make(baseUrl: "");
        Assert.Equal(503, (await service.GetAsync("internal/health-wiki/summary", null, "Priya")).StatusCode);
        Assert.Equal(0, upstream.Calls);
    }

    // ------------------------------------------------------------------------------------------ what is sent

    [Fact]
    public async Task ItSendsTheKeyAndWhoIsActing_ToTheRightAddress()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.OK, "[]");

        var r = await service.GetAsync("internal/health-articles", "status=PUBLISHED&type=MEDICAL", "Priya Sharma");

        Assert.True(r.Success);
        Assert.Equal("https://1hms-dev-api.example.com/internal/health-articles?status=PUBLISHED&type=MEDICAL", upstream.Request!.RequestUri!.ToString());
        Assert.Equal(Key, Assert.Single(upstream.Request.Headers.GetValues("X-Internal-Key")));
        Assert.Equal("Priya Sharma", Assert.Single(upstream.Request.Headers.GetValues("X-Actor")));
    }

    [Fact]
    public async Task AnEditsBodyIsPassedOnUntouched_SoNullStillMeansClearAndAbsentStillMeansUnchanged()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.OK, "{\"article\":{\"slug\":\"a\"}}");
        var body = JsonDocument.Parse("{\"title\":\"New\",\"coverImageUrl\":null}").RootElement;

        await service.SendAsync(HttpMethod.Patch, "internal/health-articles/a", body, "Priya", HealthWikiShape.Article);

        Assert.Equal(HttpMethod.Patch, upstream.Request!.Method);
        Assert.Equal("{\"title\":\"New\",\"coverImageUrl\":null}", upstream.Body);
        Assert.Equal("application/json", upstream.ContentType);
    }

    [Fact]
    public async Task APostWithNoBody_SendsNone()
    {
        var (service, upstream) = Make();
        await service.SendAsync(HttpMethod.Post, "internal/health-articles/a/approve", null, "Priya", HealthWikiShape.Article);
        Assert.Null(upstream.Body);
    }

    [Fact]
    public async Task AnImageIsSentAsMultipart_WithItsNameAndType()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.Created, "{\"url\":\"https://x.example.com/articles/a.png\"}");

        var r = await service.UploadImageAsync("cover.png", "image/png", new MemoryStream(new byte[] { 1, 2, 3 }), "Priya");

        Assert.True(r.Success);
        Assert.Equal("https://x.example.com/articles/a.png", r.Data!["url"]!.GetValue<string>());
        Assert.Equal("multipart/form-data", upstream.ContentType);
        Assert.EndsWith("/internal/health-articles/images", upstream.Request!.RequestUri!.AbsolutePath);
        Assert.Contains("name=file", upstream.Body!.Replace("\"", ""));
        Assert.Contains("filename=cover.png", upstream.Body.Replace("\"", ""));
    }

    // ------------------------------------------------------------------------------------------ what comes back

    [Fact]
    public async Task ASave_HandsTheCmsTheArticle_NotTheWholeAnswer()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.Created, "{\"success\":true,\"statusCode\":201,\"slug\":\"a\",\"article\":{\"slug\":\"a\",\"title\":\"T\",\"status\":\"DRAFT\"}}");

        var r = await service.SendAsync(HttpMethod.Post, "internal/health-articles", JsonDocument.Parse("{}").RootElement, "Priya", HealthWikiShape.Article);

        Assert.True(r.Success);
        Assert.Equal(201, r.StatusCode);
        Assert.Equal("T", r.Data!["title"]!.GetValue<string>());
        Assert.Null(r.Data["success"]);
    }

    [Fact]
    public async Task AnInvite_HandsTheCmsTheContributor_AndTheLinkToCopy()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.Created,
            "{\"success\":true,\"contributor\":{\"contributorId\":\"c1\",\"fullName\":\"Dr. Asha\",\"linkSentAt\":null},\"link\":\"https://dd.example.com/contribute/invite/abc\",\"linkDelivered\":false}");

        var r = await service.SendAsync(HttpMethod.Post, "internal/health-wiki/contributors/invite", JsonDocument.Parse("{}").RootElement, "Priya", HealthWikiShape.Contributor);

        Assert.Equal("Dr. Asha", r.Data!["fullName"]!.GetValue<string>());
        Assert.Equal("https://dd.example.com/contribute/invite/abc", r.Link);
        Assert.False(r.LinkDelivered);
    }

    [Fact]
    public async Task ATimestampForTheCms_BecomesReadableTextInIndianTime()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.OK,
            "[{\"fullName\":\"A\",\"linkSentAt\":\"2026-10-11T06:42:52.716Z\"},{\"fullName\":\"B\",\"linkSentAt\":null},{\"fullName\":\"C\"}]");

        var r = await service.GetAsync("internal/health-wiki/contributors", null, "Priya");

        var list = r.Data!.AsArray();
        Assert.Equal("11 Oct 2026, 12:12 PM IST", list[0]!["linkSentAt"]!.GetValue<string>());
        Assert.Null(list[1]!["linkSentAt"]);
        Assert.Null(list[2]!["linkSentAt"]);
    }

    [Fact]
    public async Task ASuccessWithNoBody_IsStillASuccess()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => new HttpResponseMessage(HttpStatusCode.NoContent);
        var r = await service.SendAsync(HttpMethod.Post, "internal/health-wiki/topic-requests/x/accept", null, "Priya", HealthWikiShape.Whole);
        Assert.True(r.Success);
        Assert.Null(r.Data);
    }

    // ------------------------------------------------------------------------------------------ when 1HMSAPI says no

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(429)]
    public async Task ARuleRefusal_ReachesTheCmsWithItsStatusAndItsOwnWords(int status)
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json((HttpStatusCode)status, "{\"success\":false,\"message\":\"A medical article is published only after its verified doctor reviewer approves it.\"}");

        var r = await service.SendAsync(HttpMethod.Patch, "internal/health-articles/a", JsonDocument.Parse("{}").RootElement, "Priya", HealthWikiShape.Article);

        Assert.False(r.Success);
        Assert.Equal(status, r.StatusCode);
        Assert.Equal("A medical article is published only after its verified doctor reviewer approves it.", r.Message);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task WhenTheKeyIsRefused_TheCmsUserIsNotToldTheirSessionEnded(int status)
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json((HttpStatusCode)status, "{\"message\":\"Invalid or missing internal key.\"}");

        var r = await service.GetAsync("internal/health-articles", null, "Priya");

        Assert.Equal(502, r.StatusCode);
        Assert.DoesNotContain("key", r.Message!.ToLowerInvariant().Replace("this server", ""), StringComparison.Ordinal);
        Assert.NotEqual(401, r.StatusCode);
        Assert.NotEqual(403, r.StatusCode);
    }

    [Fact]
    public async Task WhenTheOtherServerIsNotConfigured_ItSaysSo_And500sBecome502()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.ServiceUnavailable, "{\"message\":\"Internal API is not configured.\"}");
        Assert.Equal(503, (await service.GetAsync("internal/health-articles", null, "P")).StatusCode);

        upstream.Respond = () => Json(HttpStatusCode.InternalServerError, "<html>boom</html>");
        var r = await service.GetAsync("internal/health-articles", null, "P");
        Assert.Equal(502, r.StatusCode);
        Assert.DoesNotContain("boom", r.Message);
    }

    [Fact]
    public async Task AnImageThatCouldNotBeSaved_PassesTheStorageErrorCodeToTheCmsTeam()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.BadGateway, "{\"message\":\"The image could not be saved. Please try again.\",\"diagnostic\":\"403 AccessDenied\"}");

        var r = await service.UploadImageAsync("a.png", "image/png", new MemoryStream(new byte[] { 1 }), "Priya");

        Assert.Equal(502, r.StatusCode);
        Assert.Equal("403 AccessDenied", r.Diagnostic);
    }

    [Fact]
    public async Task ASlowOrUnreachableServer_GivesAPlainMessage()
    {
        var (service, upstream) = Make();
        upstream.Throw = new TaskCanceledException();
        Assert.Equal(504, (await service.GetAsync("internal/health-articles", null, "P")).StatusCode);

        upstream.Throw = new HttpRequestException("connection refused: 10.0.0.5:5001");
        var r = await service.GetAsync("internal/health-articles", null, "P");
        Assert.Equal(502, r.StatusCode);
        Assert.DoesNotContain("10.0.0.5", r.Message);
    }

    [Fact]
    public async Task TheKeyNeverAppearsInAnAnswer()
    {
        var (service, upstream) = Make();
        upstream.Respond = () => Json(HttpStatusCode.BadRequest, "{\"message\":\"title is required.\"}");
        var r = await service.GetAsync("internal/health-articles", null, "P");
        Assert.DoesNotContain(Key, JsonSerializer.Serialize(r));
    }

    // ------------------------------------------------------------------------------------------ the actor header

    [Theory]
    [InlineData("Priya Sharma", "Priya Sharma")]
    [InlineData("Priya\r\nX-Evil: 1", "PriyaX-Evil: 1")]
    [InlineData("प्रिया", "CMS")]
    [InlineData("", "CMS")]
    [InlineData(null, "CMS")]
    public void TheActorNameIsSafeInAHeader(string? name, string expected) => Assert.Equal(expected, HealthWikiService.SafeActor(name));

    [Fact]
    public void ALongActorNameIsCut() => Assert.Equal(200, HealthWikiService.SafeActor(new string('a', 500)).Length);
}
