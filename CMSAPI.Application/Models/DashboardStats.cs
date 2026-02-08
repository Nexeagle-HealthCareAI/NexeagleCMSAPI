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
    public DashboardMetric TotalUsers { get; set; } = new DashboardMetric();
    public DashboardCharts Charts { get; set; } = new DashboardCharts();
}

public class DashboardCharts
{
    public ChartData Hospitals { get; set; } = new ChartData();
    public ChartData Doctors { get; set; } = new ChartData();
    public ChartData Patients { get; set; } = new ChartData();
    public ChartData Users { get; set; } = new ChartData();
}

public class ChartData
{
    public List<StatPoint> Daily { get; set; } = new();
    public List<StatPoint> Weekly { get; set; } = new();
    public List<StatPoint> Monthly { get; set; } = new();
    public List<StatPoint> Yearly { get; set; } = new();
}
