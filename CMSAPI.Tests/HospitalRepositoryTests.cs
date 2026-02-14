using System;
using System.Linq;
using System.Threading.Tasks;
using CMSAPI.Data;
using CMSAPI.Data.Repositories;
using CMSAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CMSAPI.Tests
{
    public class HospitalRepositoryTests
    {
        private AppDbContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        [Fact]
        public async Task GetHospitalByIdAsync_ReturnsUserLoginDetails()
        {
            // Arrange
            using var context = GetDbContext();
            
            var hospitalId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();
            
            // Create Status first to get the ID
            var userStatus = new UserStatus { StatusName = "Active" };
            context.UserStatus.Add(userStatus);
            await context.SaveChangesAsync();
            var statusId = userStatus.UserStatusId;

            // Hospital
            var hospital = new Hospital 
            { 
                HospitalID = hospitalId, 
                Name = "Test Hospital",
                Type = "General",
                Contact = "1234567890",
                Location = "Test St",
                City = "Test City",
                State = "Test State",
                Country = "Test Country",
                Pincode = "123456",
                RegistrationNumber = "REG123",
                CreatedByUserID = userId,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            // User
            var user = new User 
            { 
                UserID = userId, 
                MobileNumber = "9876543210", 
                Email = "test@example.com",
                UserStatusId = statusId,
                CreatedAt = DateTime.UtcNow
            };

            // UserProfile
            var userProfile = new UserProfile 
            { 
                UserProfileID = Guid.NewGuid(),
                UserID = userId, 
                FullName = "Test User",
                UserStatusId = statusId,
                CreatedAt = DateTime.UtcNow,
                ProfileCompletionPercent = 100
            };

            // UserAuth
            var userAuth = new UserAuth 
            { 
                UserAuthID = Guid.NewGuid(),
                UserID = userId, 
                UserStatusId = statusId,
                LastLoginTime = DateTime.UtcNow.AddMinutes(-5),
                LoginMethod = "Email",
                CreatedAt = DateTime.UtcNow
            };

            // Role
            var role = new Role 
            { 
                RoleID = roleId, 
                RoleName = "Admin",
                IsActive = true
            };

            // UserRole
            var userRole = new UserRole 
            { 
                UserID = userId, 
                RoleID = roleId 
            };

            // HospitalUser
            var hospitalUser = new HospitalUser 
            { 
                HospitalUserID = Guid.NewGuid(),
                HospitalID = hospitalId, 
                UserID = userId, 
                IsPrimary = true,
                CreatedAt = DateTime.UtcNow
            };

            context.Hospitals.Add(hospital);
            context.Users.Add(user);
            context.UserProfiles.Add(userProfile);
            context.UserAuths.Add(userAuth);
            context.Roles.Add(role);
            context.UserRoles.Add(userRole);
            context.HospitalUsers.Add(hospitalUser);
            
            await context.SaveChangesAsync();

            var repository = new HospitalRepository(context);

            // Act
            var result = await repository.GetHospitalByIdAsync(hospitalId);

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Users);
            var userInfo = result.Users.FirstOrDefault();
            Assert.NotNull(userInfo);
            
            Assert.Equal("Test User", userInfo.Name);
            Assert.Equal("Admin", userInfo.Role);
            Assert.Equal("Active", userInfo.Status);
            Assert.Equal(userAuth.LastLoginTime, userInfo.LastLoginTime);
            Assert.Equal("Email", userInfo.LoginMethod);
        }
    }
}
