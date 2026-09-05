using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CMSAPI.Application.Interfaces;
using CMSAPI.Application.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CMSAPI.Data.Repositories
{
    // Reads tables AppDbContext has no EF entity for (Admission, PathologyOrder,
    // BillingChargeEvent/BillingInvoiceChargeEvent) -- they live in the same easyHMSDatabase
    // catalog this context is already connected to (see AppDbContext's DefaultConnection), just
    // without mappings, since CMSAPI has never needed them until now. Raw SQL rather than adding
    // full entity mappings for tables this repository only ever aggregates, never writes.
    public class HospitalOperationsRepository : IHospitalOperationsRepository
    {
        private readonly AppDbContext _db;

        public HospitalOperationsRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task<List<HospitalOperationsSummaryItem>> GetSummaryAsync(DateTime fromDate, DateTime toDateExclusive)
        {
            const string sql = @"
                SELECT
                    h.HospitalID AS HospitalId,
                    h.Name AS HospitalName,
                    ISNULL(adm.Cnt, 0) AS AdmissionsCount,
                    ISNULL(path.Cnt, 0) AS PathologyOrdersCount,
                    ISNULL(pharm.InvoiceCount, 0) AS PharmacyInvoiceCount,
                    ISNULL(pharm.Revenue, 0) AS PharmacyRevenue,
                    ISNULL(appt.Cnt, 0) AS OnlineAppointmentsCount
                FROM dbo.Hospital h
                LEFT JOIN (
                    SELECT HospitalId, COUNT(*) AS Cnt
                    FROM dbo.Admission
                    WHERE AdmittedAt >= @fromDate AND AdmittedAt < @toDate AND StatusCode <> 'CANCELLED'
                    GROUP BY HospitalId
                ) adm ON adm.HospitalId = h.HospitalID
                LEFT JOIN (
                    SELECT HospitalId, COUNT(*) AS Cnt
                    FROM dbo.PathologyOrder
                    WHERE OrderDate >= @fromDate AND OrderDate < @toDate AND Status <> 'CANCELLED'
                    GROUP BY HospitalId
                ) path ON path.HospitalId = h.HospitalID
                LEFT JOIN (
                    SELECT bce.HospitalId, COUNT(DISTINCT bice.InvoiceId) AS InvoiceCount, SUM(bce.NetAmount) AS Revenue
                    FROM dbo.BillingChargeEvent bce
                    JOIN dbo.BillingInvoiceChargeEvent bice ON bice.ChargeEventId = bce.ChargeEventId
                    WHERE bce.SourceModule IN ('PHARMACY_COUNTER', 'PHARMACY_IPD')
                        AND bce.StatusCode <> 'VOID'
                        AND bce.ServiceDate >= @fromDate AND bce.ServiceDate < @toDate
                    GROUP BY bce.HospitalId
                ) pharm ON pharm.HospitalId = h.HospitalID
                LEFT JOIN (
                    SELECT HospitalID, COUNT(*) AS Cnt
                    FROM dbo.Appointment
                    WHERE BookingSource = 'NEXEAGLE_PUBLIC' AND CreatedAt >= @fromDate AND CreatedAt < @toDate
                    GROUP BY HospitalID
                ) appt ON appt.HospitalID = h.HospitalID
                WHERE h.IsArchived = 0
                ORDER BY h.Name";

            var fromParam = new SqlParameter("@fromDate", fromDate);
            var toParam = new SqlParameter("@toDate", toDateExclusive);

            return await _db.Database
                .SqlQueryRaw<HospitalOperationsSummaryItem>(sql, fromParam, toParam)
                .ToListAsync();
        }
    }
}
