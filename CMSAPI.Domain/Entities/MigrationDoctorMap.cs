using System;

namespace CMSAPI.Domain.Entities
{
    public class MigrationDoctorMap
    {
        public Guid MapId { get; set; }
        public Guid BatchId { get; set; }
        public MigrationBatch? Batch { get; set; }

        public string SourceDoctorName { get; set; } = null!;
        public string? SourceDepartment { get; set; }

        // No FK -- Doctor lives in easyHMSDatabase, a different physical database.
        public Guid? MappedDoctorId { get; set; }
        public string? MappedDoctorName { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
