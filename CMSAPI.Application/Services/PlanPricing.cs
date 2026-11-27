namespace CMSAPI.Application.Services
{
    /// <summary>One module line in a price breakdown.</summary>
    public sealed record ModuleLine(string Module, decimal Charge);

    /// <summary>Full, itemised price breakdown for a set of doctors, beds, and modules.</summary>
    public sealed record PriceBreakdown
    {
        public int Doctors { get; init; }
        public int Beds { get; init; }
        public decimal RatePerDoctor { get; init; }
        public decimal DoctorsSubtotal { get; init; }
        public decimal RatePerBed { get; init; }
        public decimal BedsSubtotal { get; init; }
        public IReadOnlyList<ModuleLine> Modules { get; init; } = System.Array.Empty<ModuleLine>();
        public decimal ModulesSubtotal { get; init; }
        public decimal Total { get; init; }
    }

    /// <summary>
    /// Volume-discounted plan pricing. Per-unit rates fall as capacity grows but the
    /// discount is capped, so a rate never drops below its floor:
    ///   doctor: 250 − min(250 × doctors%, 50)  → floor 200
    ///   bed:     25 − min( 25 × beds%,    10)  → floor 15
    ///   total = rate/doctor × doctors + rate/bed × beds + Σ module charges
    /// </summary>
    public static class PlanPricing
    {
        public const decimal DoctorBase = 250m;
        public const decimal DoctorDiscountCap = 50m;   // → floor of 200
        public const decimal BedBase = 25m;
        public const decimal BedDiscountCap = 10m;      // → floor of 15

        /// <summary>Canonical module keys, in display order.</summary>
        public static readonly IReadOnlyList<string> ModuleKeys =
            new[] { "IPD", "OPD", "Billing", "Lab", "RAD" };

        public static decimal RatePerDoctor(int doctors)
        {
            if (doctors <= 0) return DoctorBase;
            var discount = System.Math.Min(DoctorBase * doctors / 100m, DoctorDiscountCap);
            return DoctorBase - discount;
        }

        public static decimal RatePerBed(int beds)
        {
            if (beds <= 0) return BedBase;
            var discount = System.Math.Min(BedBase * beds / 100m, BedDiscountCap);
            return BedBase - discount;
        }

        /// <summary>
        /// Computes the price for <paramref name="doctors"/> doctors, <paramref name="beds"/> beds,
        /// and the given enabled module lines (each with its admin-set charge; 0 = free).
        /// </summary>
        public static PriceBreakdown Calculate(int doctors, int beds, IEnumerable<ModuleLine>? enabledModules)
        {
            doctors = System.Math.Max(0, doctors);
            beds = System.Math.Max(0, beds);

            var ratePerDoctor = RatePerDoctor(doctors);
            var ratePerBed = RatePerBed(beds);
            var modules = enabledModules?.ToList() ?? new List<ModuleLine>();

            var doctorsSubtotal = ratePerDoctor * doctors;
            var bedsSubtotal = ratePerBed * beds;
            var modulesSubtotal = modules.Sum(m => m.Charge);

            return new PriceBreakdown
            {
                Doctors = doctors,
                Beds = beds,
                RatePerDoctor = ratePerDoctor,
                DoctorsSubtotal = doctorsSubtotal,
                RatePerBed = ratePerBed,
                BedsSubtotal = bedsSubtotal,
                Modules = modules,
                ModulesSubtotal = modulesSubtotal,
                Total = doctorsSubtotal + bedsSubtotal + modulesSubtotal,
            };
        }
    }
}
