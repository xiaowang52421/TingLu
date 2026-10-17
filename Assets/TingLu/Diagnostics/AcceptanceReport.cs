using System;
using System.Collections.Generic;
using System.Linq;

namespace TingLu.Diagnostics
{
    /// <summary>Small data-only result types shared by the managed acceptance runner.</summary>
    public sealed class AcceptanceCheckResult
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string Detail { get; set; }
    }

    public sealed class AcceptanceReport
    {
        public IReadOnlyList<AcceptanceCheckResult> Results { get; private set; }
        public int Passed { get { return Results.Count(x => x.Status == "PASS"); } }
        public int Failed { get { return Results.Count(x => x.Status == "FAIL"); } }
        public int NotRun { get { return Results.Count(x => x.Status == "NOT RUN"); } }
        public bool HasFailures { get { return Failed != 0; } }

        public AcceptanceReport(IEnumerable<AcceptanceCheckResult> results)
        {
            if (results == null) throw new ArgumentNullException("results");
            var copy = new List<AcceptanceCheckResult>(results);
            foreach (AcceptanceCheckResult result in copy)
                if (result == null || String.IsNullOrWhiteSpace(result.Id) ||
                    (result.Status != "PASS" && result.Status != "FAIL" && result.Status != "NOT RUN"))
                    throw new ArgumentException("Each acceptance result needs an ID and a supported status", "results");
            Results = copy.AsReadOnly();
        }
    }
}
