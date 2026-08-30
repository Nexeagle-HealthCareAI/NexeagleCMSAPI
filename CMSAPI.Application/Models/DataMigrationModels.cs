using System;
using System.Collections.Generic;

namespace CMSAPI.Application.Models;

// ── Existing-patient snapshot, fed into the Python /transform call's identity crosswalk ──
public class PatientIdentitySnapshot
{
    public string PatientId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? GuardianName { get; set; }
    public string? GuardianRelation { get; set; }
    public short? Age { get; set; }
    public string? Sex { get; set; }
}

// ── Known doctor, fed into the Python /transform call for auto-seeding exact-name matches ──
public class KnownDoctorDto
{
    public Guid DoctorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<string> Departments { get; set; } = new();
}

// ── Batch list/detail (CMS <-> CMSAPI) ──
public class BatchListItem
{
    public Guid BatchId { get; set; }
    public Guid HospitalId { get; set; }
    public string DataType { get; set; } = string.Empty;
    public string SourceFileName { get; set; } = string.Empty;
    public int? SourceRowCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class BatchDetail : BatchListItem
{
    public List<ColumnMappingEntry> ColumnMapping { get; set; } = new();
    public MigrationSummaryDto? Summary { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> RawHeaders { get; set; } = new();
    public List<Dictionary<string, string?>> SampleRawRows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ColumnMappingEntry
{
    public string TargetField { get; set; } = string.Empty;
    public string? SourceHeader { get; set; }
    public double Confidence { get; set; }
    public string Source { get; set; } = "deterministic"; // 'deterministic' | 'groq' | 'manual'
}

// ── Preview grid row (CMSAPI <-> CMS) ──
public class MigrationRowDto
{
    public Guid RowId { get; set; }
    public int SourceRowNumber { get; set; }
    public Dictionary<string, string?> Raw { get; set; } = new();
    public Dictionary<string, string?>? Transformed { get; set; }
    public string? ResolvedPatientId { get; set; }
    public bool IsNewPatient { get; set; }
    public List<string> Flags { get; set; } = new();
    public string RowStatus { get; set; } = "Pending";
}

// ── Doctor mapping (CMSAPI <-> CMS) ──
public class DoctorMapEntryDto
{
    public Guid? MapId { get; set; }
    public string SourceDoctorName { get; set; } = string.Empty;
    public string? SourceDepartment { get; set; }
    public Guid? MappedDoctorId { get; set; }
    public string? MappedDoctorName { get; set; }
}

public class UpdateDoctorMapRequest
{
    public List<DoctorMapEntryDto> Entries { get; set; } = new();
}

public class UpdateColumnMappingRequest
{
    public List<ColumnMappingEntry> ColumnMapping { get; set; } = new();
}

public class MigrationSummaryDto
{
    public int TotalRows { get; set; }
    public int NewPatients { get; set; }
    public int ReusedWithinBatch { get; set; }
    public int MatchedExistingDbPatients { get; set; }
    public int FlaggedRows { get; set; }
    public int ExcludedRows { get; set; }
    public MigrationNarrativeDto? Narrative { get; set; }
}

public class MigrationNarrativeDto
{
    public string Outlook { get; set; } = string.Empty;
    public List<string> Insights { get; set; } = new();
    public bool GroqUsed { get; set; }
}

// ── DTOs exchanged with the Python DataMigrationService (internal, not exposed to CMS) ──
public class PythonDetectResponse
{
    public List<string> Headers { get; set; } = new();
    public int RowCount { get; set; }
    public List<Dictionary<string, string?>> AllRawRows { get; set; } = new();
    public List<Dictionary<string, string?>> SampleRawRows { get; set; } = new();
    public List<ColumnMappingEntry> SuggestedMapping { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool GroqUsed { get; set; }
}

public class PythonTransformRequest
{
    public string DataType { get; set; } = string.Empty;
    public List<ColumnMappingEntry> ColumnMapping { get; set; } = new();
    public List<PythonTransformRequestRow> Rows { get; set; } = new();
    public List<PatientIdentitySnapshot> ExistingPatients { get; set; } = new();
    public List<DoctorMapEntryDto> DoctorMapOverrides { get; set; } = new();
    public List<KnownDoctorDto> KnownHospitalDoctors { get; set; } = new();
}

public class PythonTransformRequestRow
{
    public int SourceRowNumber { get; set; }
    public Dictionary<string, string?> Raw { get; set; } = new();
}

public class PythonTransformResponse
{
    public List<PythonTransformedRow> Rows { get; set; } = new();
    public List<DoctorMapEntryDto> DistinctUnmappedDoctors { get; set; } = new();
    public MigrationSummaryDto Summary { get; set; } = new();
    public MigrationNarrativeDto? Narrative { get; set; }
}

public class PythonTransformedRow
{
    public int SourceRowNumber { get; set; }
    public Dictionary<string, string?> Transformed { get; set; } = new();
    public string? IdentityKey { get; set; }
    public string? ResolvedPatientId { get; set; }
    public bool IsNewPatient { get; set; }
    public List<string> Flags { get; set; } = new();
    public string RowStatus { get; set; } = "Ready";
}
