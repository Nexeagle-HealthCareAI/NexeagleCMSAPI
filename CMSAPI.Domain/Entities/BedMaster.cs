using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CMSAPI.Domain.Entities;

// Minimal mirror of easyHMSAPI's BedMaster entity — only the fields CMSAPI actually needs
// (checking a hospital's current active bed count before approving a plan downgrade).
[Table("BedMaster")]
public class BedMaster
{
    [Key]
    public Guid BedId { get; set; }
    public Guid HospitalId { get; set; }
    public bool IsActive { get; set; }
}
