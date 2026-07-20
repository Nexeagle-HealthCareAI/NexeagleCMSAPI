using Microsoft.EntityFrameworkCore;
using CMSAPI.Domain.Entities;

namespace CMSAPI.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Hospital> Hospitals { get; set; } = null!;
    public DbSet<HospitalUser> HospitalUsers { get; set; } = null!;
    public DbSet<PatientRegistration> PatientRegistrations { get; set; } = null!;
    public DbSet<UserProfile> UserProfiles { get; set; } = null!;
    public DbSet<UserStatus> UserStatus { get; set; } = null!;
    public DbSet<UserHistory> UserHistories { get; set; } = null!;
    public DbSet<UserAuth> UserAuths { get; set; } = null!;
    public DbSet<Department> Departments { get; set; } = null!;
    public DbSet<Specialization> Specializations { get; set; } = null!;
    public DbSet<HospitalDepartmentMapping> HospitalDepartmentMappings { get; set; } = null!;
    public DbSet<Doctor> Doctors { get; set; } = null!;
    public DbSet<DoctorDepartment> DoctorDepartments { get; set; } = null!;
    public DbSet<DoctorSpecialization> DoctorSpecializations { get; set; } = null!;
    public DbSet<HospitalProfileStatus> HospitalProfileStatuses { get; set; } = null!;
    public DbSet<Appointment> Appointments { get; set; } = null!;
    public DbSet<StatusMaster> StatusMaster { get; set; } = null!;
    public DbSet<UserRole> UserRoles { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<SupportSession> SupportSessions { get; set; } = null!;
    public DbSet<SupportMessage> SupportMessages { get; set; } = null!;
    public DbSet<HospitalSubscription> HospitalSubscriptions { get; set; } = null!;
    public DbSet<HospitalSubscriptionPayment> HospitalSubscriptionPayments { get; set; } = null!;
    public DbSet<BedMaster> BedMaster { get; set; } = null!;
    public DbSet<DoctorFee> DoctorFees { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DoctorFee>(entity =>
        {
            entity.ToTable("DoctorFee");
            entity.HasKey(e => e.DoctorFeeId);
            entity.Property(e => e.Amount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Hospital>(entity =>
        {
            entity.ToTable("Hospitals");
            entity.HasKey(e => e.HospitalID);

            entity.Property(e => e.HospitalID)
                  .HasDefaultValueSql("newid()");

            entity.Property(e => e.Name).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(150).IsRequired(false);
            entity.Property(e => e.Contact).HasMaxLength(20).IsRequired();
            entity.Property(e => e.AlternateContact).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.Website).HasMaxLength(150).IsRequired(false);
            entity.Property(e => e.Location).HasMaxLength(255).IsRequired();
            entity.Property(e => e.City).HasMaxLength(100).IsRequired();
            entity.Property(e => e.State).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Country).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Pincode).HasMaxLength(10).IsRequired();
            entity.Property(e => e.TimeZone).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.RegistrationNumber).HasMaxLength(100).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(e => e.CreatedByUserID).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
        });

        modelBuilder.Entity<StatusMaster>(entity =>
        {
            entity.ToTable("StatusMaster");
            entity.HasKey(e => e.StatusCode);
            entity.Property(e => e.StatusCode).HasMaxLength(40).IsRequired();
            entity.Property(e => e.StatusName).HasMaxLength(200).IsRequired(false);
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("Appointments");
            entity.HasKey(e => e.ApptId);
            entity.Property(e => e.ApptId).HasDefaultValueSql("newid()");
            entity.Property(e => e.HospitalID).IsRequired();
            entity.Property(e => e.DoctorID).IsRequired();
            entity.Property(e => e.PatientID).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ApptDate).HasColumnType("date").IsRequired();
            entity.Property(e => e.StartAt).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.EndAt).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.Reason).HasMaxLength(200).IsRequired(false);
            entity.Property(e => e.InsuranceId).HasMaxLength(50).IsRequired(false);
            entity.Property(e => e.PaymentMode).HasMaxLength(30).IsRequired(false);
            entity.Property(e => e.AppointmentType).HasMaxLength(30).IsRequired(false);
            entity.Property(e => e.CurrentStatusCode).HasMaxLength(40).HasDefaultValue("VITALS_REQUIRED").IsRequired();
            entity.Property(e => e.StatusHistoryJson).IsRequired().HasDefaultValue("[]");
            entity.Property(e => e.LastStatusCodeAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.CreatedBy).IsRequired(false);
            entity.Property(e => e.PdfUrl).HasMaxLength(500).IsRequired(false);
            entity.Property(e => e.ValidUptoDate).HasColumnType("date").IsRequired(false);
            entity.Property(e => e.BookingSource).HasMaxLength(50).IsRequired(false);

            entity.HasOne(e => e.Doctor)
                  .WithMany(d => d.Appointments)
                  .HasForeignKey(e => e.DoctorID);

            entity.HasOne(e => e.Hospital)
                  .WithMany(h => h.Appointments)
                  .HasForeignKey(e => e.HospitalID);

            entity.HasOne(e => e.StatusMaster)
                  .WithMany(s => s.Appointments)
                  .HasForeignKey(e => e.CurrentStatusCode);

            // JSON and time range constraints not representable in EF model; keep as DB checks if applied in migration
        });

        modelBuilder.Entity<DoctorDepartment>(entity =>
        {
            entity.ToTable("DoctorDepartments");
            entity.HasKey(e => e.DoctorDepartmentID);
            entity.Property(e => e.DoctorDepartmentID).HasDefaultValueSql("newid()");
            entity.Property(e => e.AssignedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasIndex(e => new { e.DoctorID, e.DepartmentID }).IsUnique().HasDatabaseName("UQ_DoctorDepartments");

            entity.HasOne(e => e.Department)
                  .WithMany(d => d.DoctorDepartments)
                  .HasForeignKey(e => e.DepartmentID);

            entity.HasOne(e => e.Doctor)
                  .WithMany(d => d.DoctorDepartments)
                  .HasForeignKey(e => e.DoctorID);

            entity.HasOne(e => e.Hospital)
                  .WithMany(h => h.DoctorDepartments)
                  .HasForeignKey(e => e.HospitalID);
        });

        modelBuilder.Entity<HospitalDepartmentMapping>(entity =>
        {
            entity.ToTable("HospitalDepartmentMappings");
            entity.HasKey(e => e.MappingID);
            entity.Property(e => e.MappingID).HasDefaultValueSql("newid()");
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(e => e.MappedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasIndex(e => new { e.HospitalID, e.DepartmentID }).IsUnique().HasDatabaseName("UQ_HDM");

            entity.HasOne(e => e.Department)
                  .WithMany(d => d.HospitalDepartmentMappings)
                  .HasForeignKey(e => e.DepartmentID);

            entity.HasOne(e => e.Hospital)
                  .WithMany(h => h.HospitalDepartmentMappings)
                  .HasForeignKey(e => e.HospitalID);
        });

        modelBuilder.Entity<HospitalProfileStatus>(entity =>
        {
            entity.ToTable("HospitalProfileStatus");
            entity.HasKey(e => e.HospitalID);

            entity.Property(e => e.IsBasicInfoComplete).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.IsContactInfoComplete).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.IsLocationInfoComplete).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.ProfileCompletionPercent).HasDefaultValue(0).IsRequired();
            entity.Property(e => e.LastUpdatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne(e => e.Hospital)
                  .WithOne(h => h.HospitalProfileStatus)
                  .HasForeignKey<HospitalProfileStatus>(e => e.HospitalID);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(e => e.DepartmentID);
            entity.Property(e => e.DepartmentID).HasDefaultValueSql("newid()");
            entity.Property(e => e.HospitalID).IsRequired(false);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(e => e.CreatedByUserID).IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasIndex(e => new { e.HospitalID, e.Name }).IsUnique().HasDatabaseName("UQ_Departments_Hosp_Name");
        });

        modelBuilder.Entity<Specialization>(entity =>
        {
            entity.ToTable("Specializations");
            entity.HasKey(e => e.SpecializationID);
            entity.Property(e => e.SpecializationID).HasDefaultValueSql("newid()");
            entity.Property(e => e.DepartmentID).IsRequired();
            entity.Property(e => e.HospitalID).IsRequired(false);
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(e => e.CreatedByUserID).IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasIndex(e => new { e.HospitalID, e.DepartmentID, e.Name }).IsUnique().HasDatabaseName("UQ_Spec");

            entity.HasOne(e => e.Department)
                  .WithMany(d => d.Specializations)
                  .HasForeignKey(e => e.DepartmentID);

            entity.HasOne(e => e.Hospital)
                  .WithMany()
                  .HasForeignKey(e => e.HospitalID);
        });

        modelBuilder.Entity<DoctorSpecialization>(entity =>
        {
            entity.ToTable("DoctorSpecializations");
            entity.HasKey(e => e.DoctorSpecializationID);
            entity.Property(e => e.DoctorSpecializationID).HasDefaultValueSql("newid()");

            entity.HasOne(e => e.Doctor)
                  .WithMany(d => d.DoctorSpecializations)
                  .HasForeignKey(e => e.DoctorID);

            entity.HasOne(e => e.Specialization)
                  .WithMany(s => s.DoctorSpecializations)
                  .HasForeignKey(e => e.SpecializationID);
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("UserProfiles");
            entity.HasKey(e => e.UserProfileID);

            entity.Property(e => e.UserProfileID).HasDefaultValueSql("newid()");
            entity.Property(e => e.UserID).IsRequired();
            entity.Property(e => e.FullName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Gender).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.Language).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.ProfilePictureURL).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.EmployeeID).HasMaxLength(50).IsRequired(false);
            entity.Property(e => e.DateOfBirth).HasColumnType("date").IsRequired(false);
            entity.Property(e => e.BloodGroup).HasMaxLength(10).IsRequired(false);
            entity.Property(e => e.AddressLine1).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.AddressLine2).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.City).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.State).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.Country).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.Pincode).HasMaxLength(10).IsRequired(false);
            entity.Property(e => e.EmergencyContactName).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.EmergencyContactNumber).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.ProfileCompletionPercent).HasDefaultValue(0).IsRequired();
            entity.Property(e => e.UserStatusId).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasIndex(e => e.UserID).IsUnique().HasDatabaseName("UQ_UserProfiles_User");
        });

        modelBuilder.Entity<UserStatus>(entity =>
        {
            entity.ToTable("UserStatus");
            entity.HasKey(e => e.UserStatusId);
            entity.Property(e => e.UserStatusId).ValueGeneratedOnAdd();
            entity.Property(e => e.StatusName).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.StatusName).IsUnique().HasDatabaseName("UQ_UserStatus_StatusName");
        });

        modelBuilder.Entity<UserAuth>(entity =>
        {
            entity.ToTable("UserAuth");
            entity.HasKey(e => e.UserAuthID);

            entity.Property(e => e.UserAuthID).HasDefaultValueSql("newid()");
            entity.Property(e => e.HashedPassword).HasMaxLength(256).IsRequired(false);
            entity.Property(e => e.LoginMethod).HasMaxLength(50).IsRequired(false);
            entity.Property(e => e.Otp).HasMaxLength(50).IsRequired(false);
            entity.Property(e => e.OtpSentDateTime).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.IsOtpUsed).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.FailedLoginAttempts).HasDefaultValue(0).IsRequired();
            entity.Property(e => e.IsLocked).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.LastLoginIP).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.LastLoginTime).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.OtpExpireAt).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.PasswordSetAt).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.UserStatusId).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            // FK to Users table will work once User entity is added
        });

        modelBuilder.Entity<UserHistory>(entity =>
        {
            entity.ToTable("UserHistory");
            entity.HasKey(e => new { e.UserId, e.UpdatedDate });

            entity.Property(e => e.UpdatedDate).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne(e => e.UserStatus)
                  .WithMany(us => us.UserHistories)
                  .HasForeignKey(e => e.UserStatusId);
        });

        modelBuilder.Entity<PatientRegistration>(entity =>
        {
            entity.ToTable("PatientRegistrations");
            entity.HasKey(e => e.RegistrationId);

            entity.Property(e => e.RegistrationId).HasDefaultValueSql("newid()");
            entity.Property(e => e.PatientID).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RegisteredAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.FullName).HasMaxLength(150).IsRequired();
            entity.Property(e => e.Mobile).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.AgeYears).HasColumnType("smallint").IsRequired(false);
            entity.Property(e => e.Sex).HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.AddressLine).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.City).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.State).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.Country).HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.Pincode).HasMaxLength(10).IsRequired(false);
            entity.Property(e => e.InsuranceId).HasMaxLength(50).IsRequired(false);

            entity.HasOne(e => e.Hospital)
                  .WithMany(h => h.PatientRegistrations)
                  .HasForeignKey(e => e.HospitalID);
        });

        modelBuilder.Entity<HospitalUser>(entity =>
        {
            entity.ToTable("HospitalUsers");
            entity.HasKey(e => e.HospitalUserID);

            entity.Property(e => e.HospitalUserID).HasDefaultValueSql("newid()");
            entity.Property(e => e.IsPrimary).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.EmployeeID).HasMaxLength(50).IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne(e => e.Hospital)
                  .WithMany(h => h.HospitalUsers)
                  .HasForeignKey(e => e.HospitalID);
        });

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("UserRoles");
            entity.HasKey(e => new { e.UserID, e.RoleID });
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(e => e.RoleID);

            entity.Property(e => e.RoleID)
                  .HasDefaultValueSql("newid()");

            entity.Property(e => e.RoleName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.IsSystemDefined).HasDefaultValue(false).IsRequired();
            entity.Property(e => e.IsActive).HasDefaultValue(true).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasIndex(e => new { e.HospitalID, e.RoleName })
                  .IsUnique()
                  .HasDatabaseName("UQ_Roles");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.UserID);

            entity.Property(e => e.MobileNumber).HasMaxLength(15).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(150).IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasIndex(e => e.MobileNumber)
                  .IsUnique()
                  .HasDatabaseName("UQ_Users_Mobile");
        });

        modelBuilder.Entity<SupportSession>(entity =>
        {
            entity.ToTable("SupportSessions");
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.SessionId).HasDefaultValueSql("newid()");
            entity.Property(e => e.GuestId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("Active");
            entity.Property(e => e.StartedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
        });

        modelBuilder.Entity<SupportMessage>(entity =>
        {
            entity.ToTable("SupportMessages");
            entity.HasKey(e => e.MessageId);
            entity.Property(e => e.MessageId).HasDefaultValueSql("newid()");
            entity.Property(e => e.SenderType).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SentAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Messages)
                  .HasForeignKey(e => e.SessionId);
        });

        modelBuilder.Entity<HospitalSubscription>(entity =>
        {
            entity.ToTable("HospitalSubscriptions");
            entity.HasKey(e => e.HospitalSubscriptionId);
            entity.Property(e => e.HospitalSubscriptionId).HasDefaultValueSql("newid()");
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TrialStartDate).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.TrialEndDate).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.SubscriptionStartDate).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.SubscriptionEndDate).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.NextBillingDate).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");

            entity.HasOne(e => e.Hospital)
                  .WithMany()
                  .HasForeignKey(e => e.HospitalId);
        });

        modelBuilder.Entity<HospitalSubscriptionPayment>(entity =>
        {
            entity.ToTable("HospitalSubscriptionPayments");
            entity.HasKey(e => e.PaymentId);
            entity.Property(e => e.PaymentId).HasDefaultValueSql("newid()");
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Reference).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50).IsRequired();
            entity.Property(e => e.SubmittedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.ReviewedAt).HasColumnType("datetime2(3)").IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("sysutcdatetime()");
            entity.Property(e => e.ProratedCreditAmount).HasPrecision(18, 2);
            entity.Property(e => e.IsProratedSwitch).HasDefaultValue(false);
        });

        base.OnModelCreating(modelBuilder);
    }

}
