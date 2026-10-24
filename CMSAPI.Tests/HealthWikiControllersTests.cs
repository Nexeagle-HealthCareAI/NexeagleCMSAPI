using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Authorization;
using CMSAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

public class HealthWikiControllersTests
{
    private readonly Mock<IHealthWikiService> _service = new();
    private static readonly HealthWikiProxyResponse Ok = new() { Success = true, StatusCode = 200, Data = JsonNode.Parse("[]") };

    private static T WithUser<T>(T controller, params Claim[] claims) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
        };
        return controller;
    }

    private HealthArticlesController Articles(params Claim[] claims) => WithUser(new HealthArticlesController(_service.Object), claims);
    private HealthWikiController Wiki(params Claim[] claims) => WithUser(new HealthWikiController(_service.Object), claims);

    // ------------------------------------------------------------------------------------------ who may use it

    [Theory]
    [InlineData(typeof(HealthArticlesController))]
    [InlineData(typeof(HealthWikiController))]
    public void EveryRoute_NeedsALoginAndTheHealthWikiPermission(Type controller)
    {
        // [Authorize] and [HasPermission] are both Authorize attributes, so they are read as a list.
        var attributes = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        Assert.Contains(attributes, a => a is not HasPermissionAttribute && a.Policy == null);
        var permission = Assert.Single(attributes.OfType<HasPermissionAttribute>());
        Assert.Equal($"{PermissionConstants.PolicyPrefix}health-wiki.manage", permission.Policy);
        // no action may switch it off
        Assert.DoesNotContain(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
    }

    [Fact]
    public async Task TheActor_IsTheSignedInUsersName_FallingBackToTheEmail()
    {
        _service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<HealthWikiShape>())).ReturnsAsync(Ok);

        await Articles(new Claim("name", "Priya Sharma"), new Claim(ClaimTypes.Email, "p@x.com")).List(null, null);
        await Articles(new Claim(ClaimTypes.Email, "p@x.com")).List(null, null);
        await Articles().List(null, null);

        _service.Verify(s => s.GetAsync("internal/health-articles", "", "Priya Sharma", HealthWikiShape.Whole), Times.Once);
        _service.Verify(s => s.GetAsync("internal/health-articles", "", "p@x.com", HealthWikiShape.Whole), Times.Once);
        _service.Verify(s => s.GetAsync("internal/health-articles", "", "CMS", HealthWikiShape.Whole), Times.Once);
    }

    // ------------------------------------------------------------------------------------------ the answer's shape

    [Fact]
    public async Task ASuccess_IsSuccessAndData_LikeTheRestOfThisApi()
    {
        _service.Setup(s => s.GetAsync("internal/health-wiki/summary", null, "CMS", HealthWikiShape.Whole))
            .ReturnsAsync(new HealthWikiProxyResponse { Success = true, StatusCode = 200, Data = JsonNode.Parse("{\"pendingContributors\":2,\"openTopics\":1}") });

        var result = Assert.IsType<ObjectResult>(await Wiki().Summary());

        Assert.Equal(200, result.StatusCode);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"pendingContributors\":2", json);
        Assert.DoesNotContain("link", json);
    }

    [Fact]
    public async Task AnInvite_ReturnsTheLinkToCopyAlongsideTheData()
    {
        _service.Setup(s => s.SendAsync(HttpMethod.Post, "internal/health-wiki/contributors/invite", It.IsAny<JsonElement?>(), "CMS", HealthWikiShape.Contributor))
            .ReturnsAsync(new HealthWikiProxyResponse { Success = true, StatusCode = 201, Data = JsonNode.Parse("{\"fullName\":\"Dr. Asha\"}"), Link = "https://dd.example.com/contribute/invite/abc", LinkDelivered = false });

        var result = Assert.IsType<ObjectResult>(await Wiki().Invite(JsonDocument.Parse("{}").RootElement));

        Assert.Equal(201, result.StatusCode);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"link\":\"https://dd.example.com/contribute/invite/abc\"", json);
        Assert.Contains("\"linkDelivered\":false", json);
    }

    [Fact]
    public async Task AFailure_KeepsItsStatus_AndCarriesTheMessage()
    {
        _service.Setup(s => s.SendAsync(It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<JsonElement?>(), It.IsAny<string>(), It.IsAny<HealthWikiShape>()))
            .ReturnsAsync(HealthWikiProxyResponse.Fail(409, "This is a hospital doctor. Verify their registration in the CMS doctor list."));

        var result = Assert.IsType<ObjectResult>(await Wiki().Verify(Guid.NewGuid()));

        Assert.Equal(409, result.StatusCode);
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("\"success\":false", json);
        Assert.Contains("hospital doctor", json);
    }

    // ------------------------------------------------------------------------------------------ the paths

    [Fact]
    public async Task ASlugIsEscaped_SoItCannotChangeWhereTheCallGoes()
    {
        _service.Setup(s => s.GetAsync(It.IsAny<string>(), null, "CMS", HealthWikiShape.Whole)).ReturnsAsync(Ok);

        await Articles().Get("a b/../c");

        _service.Verify(s => s.GetAsync("internal/health-articles/a%20b%2F..%2Fc", null, "CMS", HealthWikiShape.Whole), Times.Once);
    }

    [Fact]
    public async Task TheListFilters_AreEncodedAndOnlyThoseGiven()
    {
        _service.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<string?>(), "CMS", HealthWikiShape.Whole)).ReturnsAsync(Ok);

        await Articles().List("IN_REVIEW", "SECTOR UPDATE");
        await Wiki().Contributors(null, "WRITER");

        _service.Verify(s => s.GetAsync("internal/health-articles", "status=IN_REVIEW&type=SECTOR%20UPDATE", "CMS", HealthWikiShape.Whole), Times.Once);
        _service.Verify(s => s.GetAsync("internal/health-wiki/contributors", "type=WRITER", "CMS", HealthWikiShape.Whole), Times.Once);
    }

    [Fact]
    public async Task EachWriteGoesToTheMatchingInternalRoute_WithTheRightShape()
    {
        _service.Setup(s => s.SendAsync(It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<JsonElement?>(), It.IsAny<string>(), It.IsAny<HealthWikiShape>())).ReturnsAsync(Ok);
        var id = Guid.NewGuid();
        var body = JsonDocument.Parse("{\"reason\":\"x\"}").RootElement;

        await Articles().Create(body);
        await Articles().Update("a", body);
        await Articles().Approve("a");
        await Articles().Withdraw("a", body);
        await Articles().Unpublish("a", body);
        await Articles().PullBack("a", body);
        await Wiki().SendLink(id, body);
        await Wiki().Reject(id, body);
        await Wiki().Accept(id);
        await Wiki().Decline(id, body);
        await Wiki().AskDetail(id, body);

        void Sent(HttpMethod m, string path, HealthWikiShape shape) =>
            _service.Verify(s => s.SendAsync(m, path, It.IsAny<JsonElement?>(), "CMS", shape), Times.Once);
        Sent(HttpMethod.Post, "internal/health-articles", HealthWikiShape.Article);
        Sent(HttpMethod.Patch, "internal/health-articles/a", HealthWikiShape.Article);
        Sent(HttpMethod.Post, "internal/health-articles/a/approve", HealthWikiShape.Article);
        Sent(HttpMethod.Post, "internal/health-articles/a/withdraw", HealthWikiShape.Article);
        Sent(HttpMethod.Post, "internal/health-articles/a/unpublish", HealthWikiShape.Article);
        Sent(HttpMethod.Post, "internal/health-articles/a/pull-back", HealthWikiShape.Article);
        Sent(HttpMethod.Post, $"internal/health-wiki/contributors/{id}/send-link", HealthWikiShape.Contributor);
        Sent(HttpMethod.Post, $"internal/health-wiki/contributors/{id}/reject", HealthWikiShape.Contributor);
        Sent(HttpMethod.Post, $"internal/health-wiki/topic-requests/{id}/accept", HealthWikiShape.Whole);
        Sent(HttpMethod.Post, $"internal/health-wiki/topic-requests/{id}/decline", HealthWikiShape.Whole);
        Sent(HttpMethod.Post, $"internal/health-wiki/topic-requests/{id}/ask-detail", HealthWikiShape.Whole);
    }

    // ------------------------------------------------------------------------------------------ the image door

    private static IFormFile Image(long length, string name = "cover.png")
    {
        var stream = new MemoryStream(new byte[Math.Min(length, 16)]);
        var file = new Mock<IFormFile>();
        file.SetupGet(f => f.Length).Returns(length);
        file.SetupGet(f => f.FileName).Returns(name);
        file.SetupGet(f => f.ContentType).Returns("image/png");
        file.Setup(f => f.OpenReadStream()).Returns(stream);
        return file.Object;
    }

    [Fact]
    public async Task AnImage_NeedsAFile_AndIsLimitedTo3MbBeforeItIsPassedOn()
    {
        var none = Assert.IsType<BadRequestObjectResult>(await Articles().UploadImage(null));
        var empty = Assert.IsType<BadRequestObjectResult>(await Articles().UploadImage(Image(0)));
        var huge = Assert.IsType<ObjectResult>(await Articles().UploadImage(Image(3 * 1024 * 1024 + 1)));

        Assert.Equal(400, none.StatusCode);
        Assert.Equal(400, empty.StatusCode);
        Assert.Equal(413, huge.StatusCode);
        _service.Verify(s => s.UploadImageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task AGoodImage_IsPassedOnWithItsNameTypeAndTheActor()
    {
        _service.Setup(s => s.UploadImageAsync("cover.png", "image/png", It.IsAny<Stream>(), "Priya"))
            .ReturnsAsync(new HealthWikiProxyResponse { Success = true, StatusCode = 201, Data = JsonNode.Parse("{\"url\":\"https://x/a.png\"}") });

        var result = Assert.IsType<ObjectResult>(await Articles(new Claim("name", "Priya")).UploadImage(Image(1000)));

        Assert.Equal(201, result.StatusCode);
        Assert.Contains("https://x/a.png", JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task Accept_PassesTheWriterChoiceOn_ToTheEasyHmsApi()
    {
        JsonElement? sent = null;
        _service.Setup(s => s.SendAsync(It.IsAny<HttpMethod>(), It.IsAny<string>(), It.IsAny<JsonElement?>(), It.IsAny<string>(), It.IsAny<HealthWikiShape>()))
            .Callback<HttpMethod, string, JsonElement?, string, HealthWikiShape>((_, _, b, _, _) => sent = b).ReturnsAsync(Ok);
        var body = JsonDocument.Parse("{\"contentTeam\":true}").RootElement;

        await Wiki().Accept(Guid.NewGuid(), body);

        Assert.True(sent.HasValue);
        Assert.True(sent.Value.GetProperty("contentTeam").GetBoolean());
    }
}
