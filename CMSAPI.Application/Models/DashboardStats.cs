namespace CMSAPI.Application.Models;

public class DashboardMetric
{
    public decimal Value { get; set; }
    public decimal Change { get; set; }
    public string ChangeType { get; set; } = "increase"; // "increase" | "decrease" | "nochange"
    public string Period { get; set; } = string.Empty;
}

public class DashboardResponse
{
    public DashboardMetric TotalHospitals { get; set; } = new DashboardMetric();
    public DashboardMetric TotalDoctors { get; set; } = new DashboardMetric();
    public DashboardMetric TotalPatients { get; set; } = new DashboardMetric();
}
