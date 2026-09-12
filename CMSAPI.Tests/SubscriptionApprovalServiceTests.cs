using System;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Models;
using CMSAPI.Data;
using CMSAPI.Data.Entities;
using CMSAPI.Domain.Entities;
using CMSAPI.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CMSAPI.Tests
{
    public class SubscriptionApprovalServiceTests
    {
        private static AppDbContext NewAppDb() =>
            new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static CmsDbContext NewCmsDb() =>
            new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        private static HospitalSubscription SeedSubscription(
            AppDbContext db, Guid hospitalId, Guid planId, string status,
            DateTime? subscriptionEndDate = null)
        {
            db.Hospitals.Add(new Hospital { HospitalID = hospitalId, Name = "Test Hospital" });
            var sub = new HospitalSubscription
            {
                HospitalSubscriptionId = Guid.NewGuid(),
                HospitalId = hospitalId,
                PlanId = planId,
                Status = status,
                SubscriptionEndDate = subscriptionEndDate,
                UpdatedAt = DateTime.UtcNow,
            };
            db.HospitalSubscriptions.Add(sub);
            db.SaveChanges();
            return sub;
        }

        private static EasyHmsSubscriptionPlan SeedEasyHmsPlan(
            CmsDbContext cms, string billingCycle = "Monthly", int? maxDoctors = null, int? maxBeds = null)
        {
            var plan = new EasyHmsSubscriptionPlan
            {
                PlanId = Guid.NewGuid(),
                Name = "Test Plan",
                BillingCycle = billingCycle,
                IsActive = true,
                MaxDoctors = maxDoctors,
                MaxBeds = maxBeds,
            };
            cms.EasyHmsSubscriptionPlans.Add(plan);
            cms.SaveChanges();
            return plan;
        }

        private static void SeedDoctors(AppDbContext db, Guid hospitalId, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var userId = Guid.NewGuid();
                db.Users.Add(new User { UserID = userId, MobileNumber = $"90000000{i:D2}", UserStatusId = 1 });
                db.Doctors.Add(new Doctor { DoctorID = Guid.NewGuid(), HospitalID = hospitalId, UserID = userId });
            }
            db.SaveChanges();
        }

        [Fact]
        public async Task RenewSubscriptionAsync_MissingReference_ReturnsError()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var svc = new SubscriptionApprovalService(appDb, cmsDb);

            var result = await svc.RenewSubscriptionAsync(Guid.NewGuid(), new RenewSubscriptionRequest { Reference = "" });

            Assert.False(result.Success);
            Assert.Contains("reference", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_NoPlanSelected_ReturnsError()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            appDb.Hospitals.Add(new Hospital { HospitalID = hospitalId, Name = "H" });
            appDb.HospitalSubscriptions.Add(new HospitalSubscription
            { HospitalSubscriptionId = Guid.NewGuid(), HospitalId = hospitalId, PlanId = null, Status = "Trial" });
            appDb.SaveChanges();

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest { Reference = "Cash payment" });

            Assert.False(result.Success);
            Assert.Contains("has not selected a plan", result.ErrorMessage);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_OverLimit_WithoutOverride_ReturnsConfirmationRequired()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb, maxDoctors: 1);
            SeedSubscription(appDb, hospitalId, plan.PlanId, "Active", DateTime.UtcNow.AddDays(5));
            SeedDoctors(appDb, hospitalId, 2); // exceeds MaxDoctors=1

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest { Reference = "Bank transfer" });

            Assert.False(result.Success);
            Assert.True(result.RequiresOverrideConfirmation);
            Assert.Contains(result.OverLimitDetails, d => d.Contains("doctors"));
        }

        [Fact]
        public async Task RenewSubscriptionAsync_OverLimit_WithOverride_Succeeds()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb, maxDoctors: 1);
            SeedSubscription(appDb, hospitalId, plan.PlanId, "Blocked");
            SeedDoctors(appDb, hospitalId, 2);

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest
            {
                Reference = "Manager approved override",
                AllowOverLimit = true,
            });

            Assert.True(result.Success);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_ExtendsFromCurrentEndDate_WhenStillActive()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb, billingCycle: "Monthly");
            var currentEnd = DateTime.UtcNow.AddDays(10); // still active, renewing early
            var sub = SeedSubscription(appDb, hospitalId, plan.PlanId, "Active", currentEnd);

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest { Reference = "Cheque #123" });

            Assert.True(result.Success);
            var updated = appDb.HospitalSubscriptions.Single(s => s.HospitalId == hospitalId);
            Assert.Equal(currentEnd, updated.SubscriptionStartDate);
            // Monthly from currentEnd, not from "now" — proves early renewal doesn't lose paid time.
            Assert.Equal(currentEnd.AddMonths(1), updated.SubscriptionEndDate);
            Assert.Equal("Active", updated.Status);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_ReactivatesExpired_StartsFromNow()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb, billingCycle: "Yearly");
            var pastEnd = DateTime.UtcNow.AddDays(-30); // lapsed
            SeedSubscription(appDb, hospitalId, plan.PlanId, "Expired", pastEnd);

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var before = DateTime.UtcNow;
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest { Reference = "Cash — reactivation" });
            var after = DateTime.UtcNow;

            Assert.True(result.Success);
            var updated = appDb.HospitalSubscriptions.Single(s => s.HospitalId == hospitalId);
            Assert.InRange(updated.SubscriptionStartDate!.Value, before, after);
            Assert.Equal("Active", updated.Status);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_CustomEndDate_OverridesComputedDate()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb, billingCycle: "Monthly");
            SeedSubscription(appDb, hospitalId, plan.PlanId, "Trial");

            var customEnd = DateTime.UtcNow.AddMonths(3).Date;
            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest
            {
                Reference = "Promo extension",
                SubscriptionEndDate = customEnd,
            });

            Assert.True(result.Success);
            Assert.Equal(customEnd, result.SubscriptionEndDate);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_EndDateBeforeStart_ReturnsError()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb);
            SeedSubscription(appDb, hospitalId, plan.PlanId, "Trial");

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest
            {
                Reference = "Bad date",
                SubscriptionEndDate = DateTime.UtcNow.AddDays(-1),
            });

            Assert.False(result.Success);
            Assert.Contains("after", result.ErrorMessage);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_RecordsManualPayment_AndClosesDanglingPending()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb);
            var sub = SeedSubscription(appDb, hospitalId, plan.PlanId, "PendingApproval");

            appDb.HospitalSubscriptionPayments.Add(new HospitalSubscriptionPayment
            {
                PaymentId = Guid.NewGuid(),
                HospitalId = hospitalId,
                HospitalSubscriptionId = sub.HospitalSubscriptionId,
                PlanId = plan.PlanId,
                Amount = 999,
                Reference = "old-in-app-submission",
                Status = "PendingApproval",
                SubmittedAt = DateTime.UtcNow.AddDays(-1),
            });
            appDb.SaveChanges();

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest
            {
                Reference = "Offline bank transfer",
                Amount = 1500,
                PaymentMode = "Bank Transfer",
            });

            Assert.True(result.Success);

            var payments = appDb.HospitalSubscriptionPayments.Where(p => p.HospitalId == hospitalId).ToList();
            Assert.Equal(2, payments.Count);
            Assert.All(payments, p => Assert.Equal("Approved", p.Status));

            var manual = payments.Single(p => p.Reference == "Offline bank transfer");
            Assert.Equal(1500, manual.Amount);
            Assert.Equal("Bank Transfer", manual.PaymentMode);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_DefaultsPaymentModeToManual_WhenNotProvided()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var plan = SeedEasyHmsPlan(cmsDb);
            SeedSubscription(appDb, hospitalId, plan.PlanId, "Trial");

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest { Reference = "Cash" });

            var payment = appDb.HospitalSubscriptionPayments.Single(p => p.HospitalId == hospitalId);
            Assert.Equal("Manual", payment.PaymentMode);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_LegacyPlan_UsesLegacyBillingCycle_NoLimitCheck()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var hospitalId = Guid.NewGuid();
            var legacyPlanId = Guid.NewGuid();
            cmsDb.SubscriptionPlans.Add(new SubscriptionPlan
            {
                PlanId = legacyPlanId, Name = "Legacy 1Rad Plan", BillingCycle = "Quarterly",
                ApplicationName = "1Rad", IsActive = true,
            });
            cmsDb.SaveChanges();
            SeedSubscription(appDb, hospitalId, legacyPlanId, "Trial");
            SeedDoctors(appDb, hospitalId, 50); // would be over-limit if checked against an EasyHms cap, but legacy plans carry none

            var svc = new SubscriptionApprovalService(appDb, cmsDb);
            var result = await svc.RenewSubscriptionAsync(hospitalId, new RenewSubscriptionRequest { Reference = "Legacy renewal" });

            Assert.True(result.Success);
        }

        [Fact]
        public async Task RenewSubscriptionAsync_HospitalNotFound_ReturnsNotFoundMessage()
        {
            using var appDb = NewAppDb();
            using var cmsDb = NewCmsDb();
            var svc = new SubscriptionApprovalService(appDb, cmsDb);

            var result = await svc.RenewSubscriptionAsync(Guid.NewGuid(), new RenewSubscriptionRequest { Reference = "x" });

            Assert.False(result.Success);
            Assert.Equal("Hospital subscription not found.", result.ErrorMessage);
        }
    }
}
