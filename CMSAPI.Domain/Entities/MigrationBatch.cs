using System;

namespace CMSAPI.Domain.Entities
{
    public class MigrationBatch
    {
        public Guid BatchId { get; set; }

        // No FK -- Hospital lives in easyHMSDatabase, a different physical database.
        public Guid HospitalId { get; set; }

        public string DataType { get; set; } = null!; // 'AppointmentsRegister' | 'PatientMaster'
        public string SourceFileName { get; set; } = null!;
        public int? SourceRowCount { get; set; }
        public string Status { get; set; } = "Uploaded";
        public string? ColumnMappingJson { get; set; }
        public string? SummaryJson { get; set; }
        public string? ErrorMessage { get; set; }

        public Guid CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Reserved for Phase 2 (commit) / Phase 3 (rollback) -- unused until those phases exist.
        public DateTime? CommittedAt { get; set; }
        public Guid? CommittedByUserId { get; set; }
        public DateTime? RolledBackAt { get; set; }
        public Guid? RolledBackByUserId { get; set; }
    }
}
