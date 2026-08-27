using System.Diagnostics.Metrics;
using BacklinkStudio.Application;

namespace BacklinkStudio.UnitTests;

public sealed class OperationalTelemetryTests
{
    [Fact]
    public void OperationalGauges_ExposeLatestDurableSnapshotAndBoundedUtilization()
    {
        var longs = new Dictionary<string, long>(StringComparer.Ordinal);
        var doubles = new Dictionary<string, double>(StringComparer.Ordinal);
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == BacklinkStudioTelemetry.SourceName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => longs[instrument.Name] = value);
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => doubles[instrument.Name] = value);
        listener.Start();

        BacklinkStudioTelemetry.UpdateOperationalMetrics(new OperationalMetricsSnapshot(7, 3, 2, 1, 4, 5));
        BacklinkStudioTelemetry.UpdateWorkerUtilization(3, 4);
        listener.RecordObservableInstruments();

        Assert.Equal(7, longs["backlinkstudio.jobs.queue_depth"]);
        Assert.Equal(3, longs["backlinkstudio.jobs.running"]);
        Assert.Equal(2, longs["backlinkstudio.jobs.failed_current"]);
        Assert.Equal(1, longs["backlinkstudio.jobs.dead_letter_current"]);
        Assert.Equal(4, longs["backlinkstudio.workers.online"]);
        Assert.Equal(5, longs["backlinkstudio.schedules.due"]);
        Assert.Equal(0.75, doubles["backlinkstudio.worker.utilization"]);

        BacklinkStudioTelemetry.UpdateWorkerUtilization(9, 4);
        listener.RecordObservableInstruments();
        Assert.Equal(1, doubles["backlinkstudio.worker.utilization"]);
    }
}
