namespace CMSAPI.Application.Models;

public class FinancialAttributionDto
{
    public decimal TotalSpend { get; set; }
    public decimal TotalRevenue { get; set; }
    public int TotalLeads { get; set; }
    public int TotalQualifiedLeads { get; set; }
    public int TotalCustomers { get; set; }
    
    // Calculated metrics
    public decimal CAC => TotalCustomers > 0 ? TotalSpend / TotalCustomers : 0;
    public decimal CPL => TotalLeads > 0 ? TotalSpend / TotalLeads : 0;
    public decimal CPQL => TotalQualifiedLeads > 0 ? TotalSpend / TotalQualifiedLeads : 0;
    public decimal ROAS => TotalSpend > 0 ? TotalRevenue / TotalSpend : 0;
}
