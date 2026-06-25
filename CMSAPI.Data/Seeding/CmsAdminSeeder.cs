using CMSAPI.Application.Interfaces;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Seeding;

/// <summary>
/// Creates the first CMS admin user on startup from env vars
/// (CMS_SEED_ADMIN_EMAIL / CMS_SEED_ADMIN_PASSWORD), hashing the password with the
/// app's hasher. Idempotent: does nothing if the env vars are unset or the user exists.
/// The Administrator role + permissions are seeded by the CMSDatabase SQL repo.
/// </summary>
public class CmsAdminSeeder
{
    private readonly CmsDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;

    public CmsAdminSeeder(CmsDbContext db, IPasswordHasher hasher, Microsoft.Extensions.Configuration.IConfiguration config)
    {
        _db = db;
        _hasher = hasher;
        _config = config;
    }

    public async Task SeedAsync()
    {
        var email = _config["CmsSeed:AdminEmail"] ?? Environment.GetEnvironmentVariable("CMS_SEED_ADMIN_EMAIL")?.Trim() ?? "info@nexeagle.com";
        var password = _config["CmsSeed:AdminPassword"] ?? Environment.GetEnvironmentVariable("CMS_SEED_ADMIN_PASSWORD") ?? "Admin@123";
        var phone = _config["CmsSeed:AdminPhone"] ?? Environment.GetEnvironmentVariable("CMS_SEED_ADMIN_PHONE")?.Trim() ?? "8074906808";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("[CmsAdminSeeder] Email or Password missing — skipping admin seed.");
            return;
        }

        try
        {
            if (await _db.CmsUsers.AnyAsync(u => u.Email == email))
            {
                Console.WriteLine($"[CmsAdminSeeder] Admin '{email}' already exists — skipping.");
                return;
            }

            var adminRole = await _db.CmsRoles.FirstOrDefaultAsync(r => r.Name == "Administrator");
            if (adminRole == null)
            {
                Console.WriteLine("[CmsAdminSeeder] 'Administrator' role missing — run the CMSDatabase seed first. Skipping.");
                return;
            }

            var user = new CmsUser
            {
                UserId = Guid.NewGuid(),
                Email = email,
                FullName = "CMS Administrator",
                PasswordHash = _hasher.Hash(password),
                PhoneNumber = phone,
                IsActive = true,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.CmsUsers.Add(user);
            _db.CmsUserRoles.Add(new CmsUserRole { UserId = user.UserId, RoleId = adminRole.RoleId });
            await _db.SaveChangesAsync();

            Console.WriteLine($"[CmsAdminSeeder] Created admin '{email}' (must change password on first login).");
        }
        catch (Exception ex)
        {
            // Never block startup if CMSDatabase isn't reachable/migrated yet.
            Console.WriteLine($"[CmsAdminSeeder] Skipped (error): {ex.Message}");
        }
    }
}
