#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using CMSAPI.Application.Services;
using CMSAPI.Data;
using CMSAPI.Data.Repositories;
using CMSAPI.Data.Services;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// People's profiles: My profile, photos, and administrators changing someone's details in
// Users & access (email, phone number, job title), with the same rules both ways.
public class ProfilesTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Bilal = Guid.NewGuid();
    private static readonly Guid TeamMember = Guid.NewGuid();
    private static readonly Guid OldRole = Guid.NewGuid();

    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F' };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D };
    private static readonly byte[] Webp = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 1, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => "hash:" + password;
        public bool Verify(string password, string storedHash) => storedHash == "hash:" + password;
    }

    private static CmsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.CmsUsers.Add(new CmsUser { UserId = Asha, FullName = "Asha Rao", Email = "asha@example.com", PhoneNumber = "+919800000001", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsUsers.Add(new CmsUser { UserId = Bilal, FullName = "Bilal Khan", Email = "bilal@example.com", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsRoles.Add(new CmsRole { RoleId = TeamMember, Name = "Team member", IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsRoles.Add(new CmsRole { RoleId = OldRole, Name = "Hospital Manager", IsActive = false, CreatedAt = DateTime.UtcNow });
        db.CmsUserRoles.Add(new CmsUserRole { UserId = Asha, RoleId = TeamMember });
        db.CmsUserRoles.Add(new CmsUserRole { UserId = Asha, RoleId = OldRole });
        db.SaveChanges();
        return db;
    }

    private static CmsProfileService Profiles(CmsDbContext db) => new(db, NullLogger<CmsProfileService>.Instance);
    private static CmsAdminService Admin(CmsDbContext db) => new(new CmsAdminRepository(db), new PlainHasher(), NullLogger<CmsAdminService>.Instance);

    // ── Rules ──

    [Theory]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("098765-43210", "09876543210")]
    [InlineData("(022) 2345 6789", "02223456789")]
    public void Phone_numbers_are_kept_the_way_sign_in_looks_them_up(string typed, string stored)
    {
        var (phone, error) = ProfileRules.CleanPhone(typed);
        Assert.Null(error);
        Assert.Equal(stored, phone);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+1234567890123456")]
    [InlineData("98765abc43")]
    [InlineData("98+76543210")]
    public void Wrong_phone_numbers_are_refused(string typed)
    {
        Assert.NotNull(ProfileRules.CleanPhone(typed).Error);
    }

    [Fact]
    public void An_empty_phone_number_removes_it()
    {
        var (phone, error) = ProfileRules.CleanPhone("  ");
        Assert.Null(phone);
        Assert.Null(error);
    }

    [Fact]
    public void Photos_are_recognised_by_their_first_bytes()
    {
        Assert.Equal("image/jpeg", ProfileRules.SniffImageType(Jpeg));
        Assert.Equal("image/png", ProfileRules.SniffImageType(Png));
        Assert.Equal("image/webp", ProfileRules.SniffImageType(Webp));
        Assert.Null(ProfileRules.SniffImageType(System.Text.Encoding.ASCII.GetBytes("<svg onload=alert(1)>")));
    }

    // ── My profile ──

    [Fact]
    public async Task My_profile_shows_my_details_and_only_roles_still_in_use()
    {
        using var db = NewDb();
        var result = await Profiles(db).GetAsync(Asha);
        Assert.True(result.Ok);
        Assert.Equal("Asha Rao", result.Data!.FullName);
        Assert.Equal("+919800000001", result.Data.PhoneNumber);
        Assert.Equal(new[] { "Team member" }, result.Data.Roles);
        Assert.Null(result.Data.PhotoVersion);
    }

    [Fact]
    public async Task I_can_change_my_name_phone_and_job_title()
    {
        using var db = NewDb();
        var result = await Profiles(db).UpdateAsync(Asha, new UpdateProfileRequest { FullName = "  Asha R. ", PhoneNumber = "+91 98000 00009", JobTitle = "Support lead" });
        Assert.True(result.Ok);
        Assert.Equal("Asha R.", result.Data!.FullName);
        Assert.Equal("+919800000009", result.Data.PhoneNumber);
        Assert.Equal("Support lead", result.Data.JobTitle);

        var cleared = await Profiles(db).UpdateAsync(Asha, new UpdateProfileRequest { PhoneNumber = "", JobTitle = "" });
        Assert.Null(cleared.Data!.PhoneNumber);
        Assert.Null(cleared.Data.JobTitle);
        Assert.Equal("Asha R.", cleared.Data.FullName); // left alone
    }

    [Fact]
    public async Task I_cannot_take_someone_elses_phone_number_or_blank_my_name()
    {
        using var db = NewDb();
        var taken = await Profiles(db).UpdateAsync(Bilal, new UpdateProfileRequest { PhoneNumber = "+91 98000 00001" });
        Assert.Equal(409, taken.Status);
        var blank = await Profiles(db).UpdateAsync(Bilal, new UpdateProfileRequest { FullName = "   " });
        Assert.Equal(400, blank.Status);
        Assert.Equal("Bilal Khan", (await db.CmsUsers.FindAsync(Bilal))!.FullName);
    }

    // ── Photos ──

    [Fact]
    public async Task A_photo_is_stored_replaced_and_removed()
    {
        using var db = NewDb();
        var profiles = Profiles(db);

        var first = await profiles.SetPhotoAsync(Bilal, Jpeg);
        Assert.True(first.Ok);
        var photo = await profiles.GetPhotoAsync(Bilal);
        Assert.Equal("image/jpeg", photo!.ContentType);
        Assert.Equal(first.Data, (await profiles.GetAsync(Bilal)).Data!.PhotoVersion);

        await Task.Delay(5);
        var second = await profiles.SetPhotoAsync(Bilal, Png);
        Assert.NotEqual(first.Data, second.Data);
        Assert.Equal("image/png", (await profiles.GetPhotoAsync(Bilal))!.ContentType);
        Assert.Single(db.CmsUserPhotos);

        Assert.True((await profiles.RemovePhotoAsync(Bilal)).Ok);
        Assert.Null(await profiles.GetPhotoAsync(Bilal));
        Assert.Null((await profiles.GetAsync(Bilal)).Data!.PhotoVersion);
    }

    [Fact]
    public async Task Only_small_jpeg_png_or_webp_photos_are_taken()
    {
        using var db = NewDb();
        var profiles = Profiles(db);
        Assert.Equal(415, (await profiles.SetPhotoAsync(Bilal, System.Text.Encoding.ASCII.GetBytes("GIF89a......"))).Status);
        var big = new byte[ProfileRules.MaxPhotoBytes + 1];
        Jpeg.CopyTo(big, 0);
        Assert.Equal(413, (await profiles.SetPhotoAsync(Bilal, big)).Status);
        Assert.Equal(400, (await profiles.SetPhotoAsync(Bilal, Array.Empty<byte>())).Status);
        Assert.Equal(404, (await profiles.SetPhotoAsync(Guid.NewGuid(), Jpeg)).Status);
        Assert.Empty(db.CmsUserPhotos);
    }

    // ── Users & access ──

    [Fact]
    public async Task An_administrator_can_change_someones_email_phone_and_job_title()
    {
        using var db = NewDb();
        var (user, error) = await Admin(db).UpdateUserAsync(Bilal, new UpdateUserRequest
        {
            Email = " bilal.khan@example.com ", PhoneNumber = "+91 98000 00002", JobTitle = "Sales",
        });
        Assert.Null(error);
        Assert.Equal("bilal.khan@example.com", user!.Email);
        Assert.Equal("+919800000002", user.PhoneNumber);
        Assert.Equal("Sales", user.JobTitle);
    }

    [Fact]
    public async Task An_administrator_cannot_give_two_people_the_same_email_or_phone()
    {
        using var db = NewDb();
        var admin = Admin(db);
        Assert.Equal("A user with that email already exists.", (await admin.UpdateUserAsync(Bilal, new UpdateUserRequest { Email = "asha@example.com" })).Error);
        Assert.Equal("Someone else already has that phone number.", (await admin.UpdateUserAsync(Bilal, new UpdateUserRequest { PhoneNumber = "+919800000001" })).Error);
        Assert.NotNull((await admin.UpdateUserAsync(Bilal, new UpdateUserRequest { Email = "not an email" })).Error);

        // Keeping your own email or number is fine.
        Assert.Null((await admin.UpdateUserAsync(Asha, new UpdateUserRequest { Email = "asha@example.com", PhoneNumber = "+919800000001" })).Error);
    }

    [Fact]
    public async Task A_bad_field_changes_nothing()
    {
        using var db = NewDb();
        var (_, error) = await Admin(db).UpdateUserAsync(Bilal, new UpdateUserRequest { FullName = "Bilal K", PhoneNumber = "123" });
        Assert.NotNull(error);
        Assert.Equal("Bilal Khan", (await db.CmsUsers.AsNoTracking().FirstAsync(u => u.UserId == Bilal)).FullName);
    }

    [Fact]
    public async Task A_new_person_can_be_added_with_phone_and_job_title()
    {
        using var db = NewDb();
        var (user, error) = await Admin(db).CreateUserAsync(new CreateUserRequest
        {
            Email = "chitra@example.com", FullName = "Chitra Das", Password = "longenough", PhoneNumber = "+91 98000 00003", JobTitle = "Doctor relations", RoleIds = { TeamMember },
        });
        Assert.Null(error);
        Assert.Equal("+919800000003", user!.PhoneNumber);
        Assert.Equal("Doctor relations", user.JobTitle);
        Assert.Equal(new[] { "Team member" }, user.Roles);

        var (_, dup) = await Admin(db).CreateUserAsync(new CreateUserRequest { Email = "d@example.com", FullName = "D", Password = "longenough", PhoneNumber = "+919800000003" });
        Assert.Equal("Someone else already has that phone number.", dup);
    }
}

public class AdministratorRoleTests
{
    [Fact]
    public async Task The_administrator_role_keeps_every_permission()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var db = new CmsDbContext(options);
        var perm = new CmsPermission { PermissionId = Guid.NewGuid(), Key = "user-management.manage", PageKey = "user-management", Action = "manage", DisplayName = "x" };
        db.CmsPermissions.Add(perm);
        db.CmsRoles.Add(new CmsRole { RoleId = CmsAdminService.AdministratorRoleId, Name = "Administrator", IsSystemDefined = true, IsActive = true, CreatedAt = DateTime.UtcNow });
        db.CmsRolePermissions.Add(new CmsRolePermission { RoleId = CmsAdminService.AdministratorRoleId, PermissionId = perm.PermissionId });
        db.SaveChanges();

        var admin = new CmsAdminService(new CMSAPI.Data.Repositories.CmsAdminRepository(db), new NoHasher(), Microsoft.Extensions.Logging.Abstractions.NullLogger<CmsAdminService>.Instance);
        var role = await admin.UpdateRoleAsync(CmsAdminService.AdministratorRoleId, new UpdateRoleRequest { PermissionKeys = new() });
        Assert.Contains("user-management.manage", role!.PermissionKeys);
    }

    private sealed class NoHasher : IPasswordHasher
    {
        public string Hash(string password) => password;
        public bool Verify(string password, string storedHash) => false;
    }
}
