using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CMSAPI.Application.Services;
using CMSAPI.Controllers;
using CMSAPI.Data;
using CMSAPI.Data.Entities;
using CMSAPI.Domain.Entities;
using CMSAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class PlanPricingTests
{
    [Theory]
    [InlineData(0, 250)]
    [InlineData(5, 237.5)]
    [InlineData(20, 200)]   // 250*20% = 50 → cap
    [InlineData(30, 200)]   // 75 → capped 50 → floor 200
    public void RatePerDoctor_appliesCappedDiscount(int doctors, decimal expected)
        => Assert.Equal(expected, PlanPricing.RatePerDoctor(doctors));

    [Theory]
    [InlineData(0, 25)]
    [InlineData(20, 20)]    // 25*20% = 5
    [InlineData(40, 15)]    // 25*40% = 10 → cap
    [InlineData(45, 15)]    // 11.25 → capped 10 → floor 15
    public void RatePerBed_appliesCappedDiscount(int beds, decimal expected)
        => Assert.Equal(expected, PlanPricing.RatePerBed(beds));

    [Fact]
    public void Calculate_matchesWorkedExample()
    {
        // 3 doctors, 45 beds, IPD 500 + Lab 300
        var b = PlanPricing.Calculate(3, 45, new[]
        {
            new ModuleLine("IPD", 500m),
            new ModuleLine("Lab", 300m),
        });

        Assert.Equal(242.5m, b.RatePerDoctor);
        Assert.Equal(727.5m, b.DoctorsSubtotal);
        Assert.Equal(15m, b.RatePerBed);
        Assert.Equal(675m, b.BedsSubtotal);
        Assert.Equal(800m, b.ModulesSubtotal);
        Assert.Equal(2202.5m, b.Total);
    }
}

public class PlanCalculatorControllerTests
{
    private static CmsDbContext NewCms() =>
        new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase($"cms-{Guid.NewGuid()}").Options);

    private static void SeedCharges(CmsDbContext cms, string app, params (string mod, decimal charge)[] charges)
    {
        foreach (var (mod, charge) in charges)
            cms.ModuleCharges.Add(new ModuleCharge { ModuleChargeId = Guid.NewGuid(), ApplicationName = app, ModuleKey = mod, Charge = charge });
        cms.SaveChanges();
    }

    [Fact]
    public async Task Calculate_returnsBreakdown_andMatchingTier()
    {
        using var cms = NewCms();
        cms.SubscriptionPlans.Add(new SubscriptionPlan
        {
            PlanId = Guid.NewGuid(), Name = "Elite", ApplicationName = "EasyHMS", BillingCycle = "Monthly",
            IsActive = true, IsCustom = false, MaxDoctors = 2, MaxBeds = 50, DiscountPrice = 999,
        });
        cms.SaveChanges();

        var c = new PlanCalculatorController(cms);
        var res = Assert.IsType<OkObjectResult>(await c.Calculate(
            new CalculatePlanRequest { ApplicationName = "EasyHMS", Doctors = 2, Beds = 50 }, CancellationToken.None));
        var body = Assert.IsType<CalculatePlanResponse>(res.Value);

        Assert.NotNull(body.MatchedTier);
        Assert.Equal("Elite", body.MatchedTier!.Name);
        // 2 doctors @245 + 50 beds @15 = 490 + 750, no modules
        Assert.Equal(1240m, body.Breakdown.Total);
    }

    [Fact]
    public async Task Calculate_noFittingTier_returnsNullTier()
    {
        using var cms = NewCms();
        cms.SubscriptionPlans.Add(new SubscriptionPlan
        {
            PlanId = Guid.NewGuid(), Name = "Elite", ApplicationName = "EasyHMS", BillingCycle = "Monthly",
            IsActive = true, IsCustom = false, MaxDoctors = 2, MaxBeds = 50,
        });
        cms.SaveChanges();

        var c = new PlanCalculatorController(cms);
        var res = Assert.IsType<OkObjectResult>(await c.Calculate(
            new CalculatePlanRequest { ApplicationName = "EasyHMS", Doctors = 10, Beds = 50 }, CancellationToken.None));
        Assert.Null(Assert.IsType<CalculatePlanResponse>(res.Value).MatchedTier);
    }

    [Fact]
    public async Task Choose_custom_mintsPlanWithModulesAndPrice()
    {
        using var cms = NewCms();
        SeedCharges(cms, "EasyHMS", ("IPD", 500m), ("Lab", 300m));

        var c = new PlanCalculatorController(cms);
        var res = Assert.IsType<OkObjectResult>(await c.Choose(new ChoosePlanRequest
        {
            ApplicationName = "EasyHMS", Doctors = 3, Beds = 45, Modules = new() { "IPD", "Lab" },
        }, CancellationToken.None));
        var body = Assert.IsType<ChoosePlanResponse>(res.Value);

        Assert.True(body.IsCustom);
        Assert.Equal(2202.5m, body.Price);

        var plan = cms.SubscriptionPlans.Single(p => p.PlanId == body.PlanId);
        Assert.True(plan.IsCustom);
        Assert.Equal(3, plan.MaxDoctors);
        Assert.Equal(45, plan.MaxBeds);
        Assert.True(plan.ModuleIPD);
        Assert.True(plan.ModuleLab);
        Assert.False(plan.ModuleOPD);
    }

    [Fact]
    public async Task Choose_existingTier_returnsSharedPlanId()
    {
        using var cms = NewCms();
        var tierId = Guid.NewGuid();
        cms.SubscriptionPlans.Add(new SubscriptionPlan
        {
            PlanId = tierId, Name = "Premium", ApplicationName = "EasyHMS", BillingCycle = "Monthly",
            IsActive = true, IsCustom = false, MaxDoctors = 5, MaxBeds = 100, DiscountPrice = 1500,
        });
        cms.SaveChanges();

        var c = new PlanCalculatorController(cms);
        var res = Assert.IsType<OkObjectResult>(await c.Choose(new ChoosePlanRequest
        {
            ApplicationName = "EasyHMS", PlanId = tierId,
        }, CancellationToken.None));
        var body = Assert.IsType<ChoosePlanResponse>(res.Value);

        Assert.Equal(tierId, body.PlanId);   // shared, not a new id
        Assert.False(body.IsCustom);
        Assert.Single(cms.SubscriptionPlans);  // no new plan created
    }

    [Fact]
    public async Task ModuleCharges_putThenGet_roundtrips()
    {
        using var cms = NewCms();
        var c = new PlanCalculatorController(cms);

        var put = Assert.IsType<OkObjectResult>(await c.PutModuleCharges(new PutModuleChargesRequest
        {
            ApplicationName = "EasyHMS",
            Charges = new() { new ModuleChargeDto { Module = "IPD", Charge = 500 } },
        }, CancellationToken.None));

        var list = Assert.IsAssignableFrom<IEnumerable<ModuleChargeDto>>(put.Value).ToList();
        Assert.Equal(5, list.Count);  // all modules returned
        Assert.Equal(500m, list.Single(m => m.Module == "IPD").Charge);
        Assert.Equal(0m, list.Single(m => m.Module == "OPD").Charge);
    }

    [Fact]
    public async Task ModuleCharges_unknownModule_badRequest()
    {
        using var cms = NewCms();
        var c = new PlanCalculatorController(cms);
        var res = await c.PutModuleCharges(new PutModuleChargesRequest
        {
            ApplicationName = "EasyHMS",
            Charges = new() { new ModuleChargeDto { Module = "XYZ", Charge = 10 } },
        }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(res);
    }
}

public class PlanDeleteGuardTests
{
    private sealed class TestFactory : IProductDbContextFactory
    {
        private readonly Dictionary<string, string> _stores = new(StringComparer.OrdinalIgnoreCase);
        public TestFactory() { foreach (var p in Platforms) _stores[p] = $"{p}-{Guid.NewGuid()}"; }
        public IReadOnlyList<string> Platforms { get; } = new[] { "EasyHMS", "1Rad" };
        public string? NormalizePlatform(string? a) => Platforms.FirstOrDefault(p => string.Equals(p, a?.Trim(), StringComparison.OrdinalIgnoreCase));
        public string GetConnectionString(string platform) => _stores[NormalizePlatform(platform)!];
        public AppDbContext Create(string platform) =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_stores[NormalizePlatform(platform)!]).Options);
    }

    private static CmsDbContext NewCms() =>
        new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase($"cms-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task Delete_blockedWhenHospitalMapped()
    {
        using var cms = NewCms();
        var factory = new TestFactory();
        var planId = Guid.NewGuid();
        cms.SubscriptionPlans.Add(new SubscriptionPlan
        {
            PlanId = planId, Name = "Elite", ApplicationName = "EasyHMS", BillingCycle = "Monthly", IsActive = true,
        });
        cms.SaveChanges();

        using (var pdb = factory.Create("EasyHMS"))
        {
            pdb.HospitalSubscriptions.Add(new HospitalSubscription
            { HospitalSubscriptionId = Guid.NewGuid(), HospitalId = Guid.NewGuid(), PlanId = planId, Status = "Active" });
            pdb.SaveChanges();
        }

        var c = new SubscriptionPlansController(cms, factory);
        var res = await c.DeletePlan(planId);
        Assert.IsType<ConflictObjectResult>(res);
        Assert.Single(cms.SubscriptionPlans); // not deleted
    }

    [Fact]
    public async Task Delete_succeedsWhenUnused()
    {
        using var cms = NewCms();
        var factory = new TestFactory();
        var planId = Guid.NewGuid();
        cms.SubscriptionPlans.Add(new SubscriptionPlan
        {
            PlanId = planId, Name = "Elite", ApplicationName = "EasyHMS", BillingCycle = "Monthly", IsActive = true,
        });
        cms.SaveChanges();

        var c = new SubscriptionPlansController(cms, factory);
        var res = await c.DeletePlan(planId);
        Assert.IsType<OkObjectResult>(res);
        Assert.Empty(cms.SubscriptionPlans);
    }
}
