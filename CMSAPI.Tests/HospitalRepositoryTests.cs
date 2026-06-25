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

        [Fact]
        public async Task GetHospitalByIdAsync_ReturnsCorrectDoctorStats()
        {
            // Arrange
            using var context = GetDbContext();
            
            var hospitalId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var doctorUserId = Guid.NewGuid();
            var doctorId = Guid.NewGuid();
            
            // Generate ID for Status
            var userStatus = new UserStatus { StatusName = "Active" };
            context.UserStatus.Add(userStatus);
            await context.SaveChangesAsync();
            var statusId = userStatus.UserStatusId;

            var hospital = new Hospital 
            { 
                HospitalID = hospitalId, 
                Name = "Stats Hospital",
                CreatedAt = DateTime.UtcNow,
                Location = "Loc", City = "City", State = "State", Contact = "123", Type = "General"
            };

            var doctorUser = new User { UserID = doctorUserId, MobileNumber = "123", UserStatusId = statusId };
            var doctorProfile = new UserProfile { UserProfileID = Guid.NewGuid(), UserID = doctorUserId, FullName = "Dr. Test", UserStatusId = statusId };
            
            var doctor = new Doctor 
            { 
                DoctorID = doctorId, 
                UserID = doctorUserId, 
                Qualification = "MBBS", 
                LicenseNumber = "LIC123",
                HospitalID = hospitalId // Use HospitalID property if exists or handle relationship manually
            };

            // Link Doctor to Hospital via DoctorDepartmet (as per repository logic)
            // Repository uses: 
            // var doctorIds = h.DoctorDepartments?.Select(d => d.DoctorID).Distinct().ToList() ?? new();
            // So we must add a DoctorDepartment entry.
            
            var department = new Department { DepartmentID = Guid.NewGuid(), Name = "Cardiology", HospitalID = hospitalId };
            var doctorDepartment = new DoctorDepartment 
            { 
                DoctorDepartmentID = Guid.NewGuid(), 
                DoctorID = doctorId, 
                DepartmentID = department.DepartmentID, 
                HospitalID = hospitalId 
            };

            // Appointments
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var patient1 = Guid.NewGuid().ToString();
            var patient2 = Guid.NewGuid().ToString();

            var appt1 = new Appointment { ApptId = Guid.NewGuid(), HospitalID = hospitalId, DoctorID = doctorId, ApptDate = today, PatientID = patient1 };
            var appt2 = new Appointment { ApptId = Guid.NewGuid(), HospitalID = hospitalId, DoctorID = doctorId, ApptDate = today, PatientID = patient1 }; // Same patient, today
            var appt3 = new Appointment { ApptId = Guid.NewGuid(), HospitalID = hospitalId, DoctorID = doctorId, ApptDate = today.AddDays(-2), PatientID = patient2 }; // Different patient, this week
            var appt4 = new Appointment { ApptId = Guid.NewGuid(), HospitalID = hospitalId, DoctorID = doctorId, ApptDate = today.AddDays(-20), PatientID = patient1 }; // Same patient 1, this month, not this week

            context.Hospitals.Add(hospital);
            context.Users.Add(doctorUser);
            context.UserProfiles.Add(doctorProfile);
            context.Doctors.Add(doctor);
            context.Departments.Add(department);
            context.DoctorDepartments.Add(doctorDepartment);
            context.Appointments.AddRange(appt1, appt2, appt3, appt4);
            
            await context.SaveChangesAsync();

            var repository = new HospitalRepository(context);

            // Act
            var result = await repository.GetHospitalByIdAsync(hospitalId);

            // Assert
            Assert.NotNull(result);
            var doctorInfo = result.Doctors.FirstOrDefault();
            Assert.NotNull(doctorInfo);
            Assert.Equal("Dr. Test", doctorInfo.Name);

            // Verify Appointments Count
            // Daily: 2 (appt1, appt2)
            // Weekly: 3 (appt1, appt2, appt3)
            // Monthly: 4 (appt1, appt2, appt3, appt4)
            // Yearly: 4
            Assert.Equal(2, doctorInfo.Appointments.Daily);
            Assert.Equal(3, doctorInfo.Appointments.Weekly);
            Assert.Equal(4, doctorInfo.Appointments.Monthly);
            Assert.Equal(4, doctorInfo.Appointments.Yearly);

            // Verify Unique Patients Count
            // Daily: 1 (patient1)
            // Weekly: 2 (patient1, patient2)
            // Monthly: 2 (patient1, patient2)
            // Yearly: 2
            Assert.Equal(1, doctorInfo.UniquePatients.Daily);
            Assert.Equal(2, doctorInfo.UniquePatients.Weekly);
            Assert.Equal(2, doctorInfo.UniquePatients.Monthly);
            Assert.Equal(2, doctorInfo.UniquePatients.Yearly);
        }
    }
}
