using System.Diagnostics.Metrics;

namespace Bibliotheca.Backend.Services;

internal static class IsbnImportMetrics
{
    internal const string MeterName = "Bibliotheca.IsbnImport";
    private static readonly Meter Meter = new(MeterName);

    internal static readonly Histogram<double> Queue = Meter.CreateHistogram<double>("bibliotheca.isbn.queue", "ms");
    internal static readonly Histogram<double> Metadata = Meter.CreateHistogram<double>("bibliotheca.isbn.metadata", "ms");
    internal static readonly Histogram<double> Cover = Meter.CreateHistogram<double>("bibliotheca.isbn.cover", "ms");
}
