using System;

namespace CMSAPI.Domain.Entities
{
    public class MigrationBatchRow
    {
        public Guid RowId { get; set; }
        public Guid BatchId { get; set; }
        public MigrationBatch? Batch { get; set; }

        public int SourceRowNumber { get; set; }
        public string RawDataJson { get; set; } = null!;
        public string? TransformedDataJson { get; set; }
        public string? IdentityKey { get; set; }
        public string? ResolvedPatientId { get; set; }
        public bool IsNewPatient { get; set; }
        public string? FlagsJson { get; set; }
        public string RowStatus { get; set; } = "Pending"; // 'Pending' | 'Ready' | 'Flagged' | 'Excluded'

        public DateTime CreatedAt { get; set; }
    }
}
