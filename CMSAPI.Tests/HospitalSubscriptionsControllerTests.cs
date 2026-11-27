using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Controllers;
using CMSAPI.Data;
using CMSAPI.Data.Entities;
using CMSAPI.Domain.Entities;
using CMSAPI.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

public class HospitalSubscriptionsControllerTests
{
    // In-memory product DB factory: one store per platform, stable across Create() calls.
    private sealed class TestFactory : IProductDbContextFactory
    {
        private readonly Dictionary<string, string> _stores = new(StringComparer.OrdinalIgnoreCase);

        public TestFactory()
        {
            foreach (var p in Platforms) _stores[p] = $"{p}-{Guid.NewGuid()}";
        }

        public IReadOnlyList<string> Platforms { get; } = new[] { "EasyHMS", "1Rad" };

        public string? NormalizePlatform(string? applicationName)
        {
            if (string.IsNullOrWhiteSpace(applicationName)) return null;
            return Platforms.FirstOrDefault(p => string.Equals(p, applicationName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public string GetConnectionString(string platform) => _stores[NormalizePlatform(platform)!];

        public AppDbContext Create(string platform)
        {
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(_stores[NormalizePlatform(platform)!])
                .Options;
            return new AppDbContext(opts);
        }
    }

    private static CmsDbContext NewCmsDb() =>
        new(new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase($"cms-{Guid.NewGuid()}").Options);

    private static HospitalSubscriptionsController BuildController(TestFactory factory, CmsDbContext cms)
    {
        var controller = new HospitalSubscriptionsController(
            factory, cms, Mock.Of<ILogger<HospitalSubscriptionsController>>());

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Email, "admin@nexeagle.com"),
        }, "test"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user },
        };
        return controller;
    }

    private static Guid SeedSubscription(
        TestFactory factory, string platform, string status,
        Guid? planId = null, DateTime? trialEnd = null, DateTime? subEnd = null)
    {
        var hospitalId = Guid.NewGuid();
        using var db = factory.Create(platform);
        db.Hospitals.Add(new Hospital { HospitalID = hospitalId, Name = $"Hospital {hospitalId.ToString()[..8]}" });
        db.HospitalSubscriptions.Add(new HospitalSubscription
        {
            HospitalSubscriptionId = Guid.NewGuid(),
            HospitalId = hospitalId,
            PlanId = planId,
            Status = status,
            TrialEndDate = trialEnd,
            SubscriptionEndDate = subEnd,
        });
        db.SaveChanges();
        return hospitalId;
    }

    private static HospitalSubscription ReadSub(TestFactory factory, string platform, Guid hospitalId)
    {
        using var db = factory.Create(platform);
        return db.HospitalSubscriptions.Single(s => s.HospitalId == hospitalId);
    }

    // ── Reads ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ComputesEffectiveStatus_ForPastDueActive()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var hospitalId = SeedSubscription(factory, "EasyHMS", "Active",
            planId: null, subEnd: DateTime.UtcNow.AddDays(-1)); // past due

        var controller = BuildController(factory, cms);
        var result = Assert.IsType<OkObjectResult>(await controller.List("EasyHMS"));

        var items = GetItems(result.Value);
        var dto = Assert.Single(items);
        Assert.Equal("Expired", dto.Status);        // effective
        Assert.Equal("Active", dto.StoredStatus);   // raw
        Assert.Equal(hospitalId, dto.HospitalId);
    }

    [Fact]
    public async Task List_UnknownPlatform_ReturnsBadRequest()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var controller = BuildController(factory, cms);
        Assert.IsType<BadRequestObjectResult>(await controller.List("Nope"));
    }

    [Fact]
    public async Task Detail_NoSubscription_ReturnsNotFound()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var controller = BuildController(factory, cms);
        Assert.IsType<NotFoundObjectResult>(await controller.Detail("EasyHMS", Guid.NewGuid()));
    }

    [Fact]
    public async Task Summary_CountsByEffectiveStatus()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        SeedSubscription(factory, "EasyHMS", "Active", planId: Guid.NewGuid(), subEnd: DateTime.UtcNow.AddDays(10));
        SeedSubscription(factory, "EasyHMS", "Active", planId: Guid.NewGuid(), subEnd: DateTime.UtcNow.AddDays(-5)); // expired
        SeedSubscription(factory, "EasyHMS", "Trial", trialEnd: DateTime.UtcNow.AddDays(3));

        var controller = BuildController(factory, cms);
        var result = Assert.IsType<OkObjectResult>(await controller.Summary());
        var resp = Assert.IsType<SubscriptionSummaryResponse>(result.Value);

        var easy = resp.Platforms.Single(p => p.Platform == "EasyHMS");
        Assert.Equal(3, easy.Total);
        Assert.Equal(1, easy.Active);
        Assert.Equal(1, easy.Expired);
        Assert.Equal(1, easy.Trial);
        Assert.Equal(3, resp.Overall.Total);
    }

    // ── Mutations ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetStatus_ActivateWithoutPlan_ReturnsBadRequest()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var hospitalId = SeedSubscription(factory, "EasyHMS", "Pending", planId: null);

        var controller = BuildController(factory, cms);
        var res = await controller.SetStatus("EasyHMS", hospitalId, new SetStatusRequest { Active = true });
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task SetStatus_ActivateWithPlan_SetsActive_AndAudits()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var hospitalId = SeedSubscription(factory, "EasyHMS", "Pending", planId: Guid.NewGuid());

        var controller = BuildController(factory, cms);
        var res = await controller.SetStatus("EasyHMS", hospitalId, new SetStatusRequest { Active = true });

        Assert.IsType<OkObjectResult>(res);
        Assert.Equal("Active", ReadSub(factory, "EasyHMS", hospitalId).Status);
        Assert.Single(cms.SubscriptionAuditLogs.Where(a => a.HospitalId == hospitalId && a.Action == "status"));
    }

    [Fact]
    public async Task SetStatus_Deactivate_SetsBlocked()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var hospitalId = SeedSubscription(factory, "EasyHMS", "Active", planId: Guid.NewGuid());

        var controller = BuildController(factory, cms);
        await controller.SetStatus("EasyHMS", hospitalId, new SetStatusRequest { Active = false });
        Assert.Equal("Blocked", ReadSub(factory, "EasyHMS", hospitalId).Status);
    }

    [Fact]
    public async Task SetTrial_EndBeforeStart_ReturnsBadRequest()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var hospitalId = SeedSubscription(factory, "EasyHMS", "Trial");

        var controller = BuildController(factory, cms);
        var res = await controller.SetTrial("EasyHMS", hospitalId, new SetTrialRequest
        {
            TrialStartDate = DateTime.UtcNow.AddDays(10),
            TrialEndDate = DateTime.UtcNow.AddDays(1),
        });
        Assert.IsType<BadRequestObjectResult>(res);
    }

    [Fact]
    public async Task AssignPlan_PlanFromOtherPlatform_ReturnsBadRequest()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var planId = Guid.NewGuid();
        cms.SubscriptionPlans.Add(new SubscriptionPlan
        {
            PlanId = planId, Name = "1Rad Elite", BillingCycle = "Monthly",
            ApplicationName = "1Rad", IsActive = true,
        });
        cms.SaveChanges();
        var hospitalId = SeedSubscription(factory, "EasyHMS", "Trial");

        var controller = BuildController(factory, cms);
        var res = await controller.AssignPlan("EasyHMS", hospitalId, new AssignPlanRequest { PlanId = planId });
        Assert.IsType<BadRequestObjectResult>(res); // platform mismatch
    }

    [Fact]
    public async Task AssignPlan_ValidPlan_SetsPlanId()
    {
        var factory = new TestFactory();
        using var cms = NewCmsDb();
        var planId = Guid.NewGuid();
        cms.SubscriptionPlans.Add(new SubscriptionPlan
        {
            PlanId = planId, Name = "EasyHMS Pro", BillingCycle = "Yearly",
            ApplicationName = "EasyHMS", IsActive = true,
        });
        cms.SaveChanges();
        var hospitalId = SeedSubscription(factory, "EasyHMS", "Trial");

        var controller = BuildController(factory, cms);
        var res = await controller.AssignPlan("EasyHMS", hospitalId, new AssignPlanRequest { PlanId = planId });

        Assert.IsType<OkObjectResult>(res);
        Assert.Equal(planId, ReadSub(factory, "EasyHMS", hospitalId).PlanId);
    }

    // ── helper: unwrap the anonymous { items, unavailablePlatforms } payload ──
    private static List<HospitalSubscriptionDto> GetItems(object? value)
    {
        var prop = value!.GetType().GetProperty("items");
        return ((IEnumerable<HospitalSubscriptionDto>)prop!.GetValue(value)!).ToList();
    }
}
