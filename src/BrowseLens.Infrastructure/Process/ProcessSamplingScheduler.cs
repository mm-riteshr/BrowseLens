using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BrowseLens.Core.Entities;
using BrowseLens.Core.Models;
using BrowseLens.Core.Process;
using BrowseLens.Core.Repositories;

namespace BrowseLens.Infrastructure.Process
{
    public class ProcessSamplingScheduler : IDisposable
    {
        private readonly WindowsProcessMetricsProvider _metricsProvider;
        private readonly ProcessMetricsAggregator _aggregator;
        private readonly IProcessSampleRepository _repository;
        private readonly TimeSpan _samplingInterval;
        private readonly TimeSpan _dbFlushInterval;
        private readonly List<ProcessSampleEntity> _unflushedBatch = new List<ProcessSampleEntity>();
        private readonly object _lock = new object();

        private Timer? _timer;
        private DateTime _lastDbFlushUtc = DateTime.UtcNow;
        private volatile bool _isRunning;
        private bool _disposed;

        public event Action<BrowserResourceSummary, BrowserResourceSummary>? MetricsSampled;

        public ProcessSamplingScheduler(
            WindowsProcessMetricsProvider metricsProvider,
            ProcessMetricsAggregator aggregator,
            IProcessSampleRepository repository,
            TimeSpan? samplingInterval = null,
            TimeSpan? dbFlushInterval = null)
        {
            _metricsProvider = metricsProvider ?? throw new ArgumentNullException(nameof(metricsProvider));
            _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _samplingInterval = samplingInterval ?? TimeSpan.FromSeconds(2.0);
            _dbFlushInterval = dbFlushInterval ?? TimeSpan.FromSeconds(10.0);
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            _lastDbFlushUtc = DateTime.UtcNow;

            _timer = new Timer(async state => await ExecuteCycleAsync(), null, TimeSpan.Zero, _samplingInterval);
        }

        public async Task StopAsync()
        {
            _isRunning = false;
            if (_timer != null)
            {
                await _timer.DisposeAsync();
                _timer = null;
            }

            // Flush any remaining samples to DB before exiting
            await FlushBatchToDatabaseAsync();
        }

        private async Task ExecuteCycleAsync()
        {
            if (!_isRunning) return;

            try
            {
                DateTime nowUtc = DateTime.UtcNow;

                var chromeSummary = _metricsProvider.SampleBrowserProcesses(BrowserType.Chrome);
                var edgeSummary = _metricsProvider.SampleBrowserProcesses(BrowserType.Edge);

                var cycleEntities = new List<ProcessSampleEntity>();
                foreach (var s in chromeSummary.IndividualSamples)
                {
                    cycleEntities.Add(new ProcessSampleEntity
                    {
                        BrowserType = "Chrome",
                        ProcessId = s.ProcessId,
                        ProcessStartIdentity = s.ProcessStartIdentity,
                        SampledAtUtc = s.SampledAtUtc,
                        CpuPercent = s.CpuPercent,
                        MemoryBytes = s.MemoryWorkingSetBytes,
                        CpuMetricDefinition = s.CpuMetricDefinition,
                        MemoryMetricDefinition = s.MemoryMetricDefinition
                    });
                }
                foreach (var s in edgeSummary.IndividualSamples)
                {
                    cycleEntities.Add(new ProcessSampleEntity
                    {
                        BrowserType = "Edge",
                        ProcessId = s.ProcessId,
                        ProcessStartIdentity = s.ProcessStartIdentity,
                        SampledAtUtc = s.SampledAtUtc,
                        CpuPercent = s.CpuPercent,
                        MemoryBytes = s.MemoryWorkingSetBytes,
                        CpuMetricDefinition = s.CpuMetricDefinition,
                        MemoryMetricDefinition = s.MemoryMetricDefinition
                    });
                }

                // 1. Update in-memory sliding window aggregator
                _aggregator.AddSamples(cycleEntities);

                // 2. Buffer for database batch insert
                bool shouldFlush = false;
                lock (_lock)
                {
                    _unflushedBatch.AddRange(cycleEntities);
                    if (nowUtc - _lastDbFlushUtc >= _dbFlushInterval || _unflushedBatch.Count >= 100)
                    {
                        shouldFlush = true;
                    }
                }

                if (shouldFlush)
                {
                    await FlushBatchToDatabaseAsync();
                }

                // 3. Notify live subscribers (e.g. Desktop Companion UI / console)
                MetricsSampled?.Invoke(chromeSummary, edgeSummary);
            }
            catch (Exception)
            {
                // Never let a sampling exception crash the background scheduler
            }
        }

        private async Task FlushBatchToDatabaseAsync()
        {
            List<ProcessSampleEntity> toWrite;
            lock (_lock)
            {
                if (_unflushedBatch.Count == 0) return;
                toWrite = new List<ProcessSampleEntity>(_unflushedBatch);
                _unflushedBatch.Clear();
                _lastDbFlushUtc = DateTime.UtcNow;
            }

            try
            {
                await _repository.InsertSamplesBatchAsync(toWrite);
            }
            catch
            {
                // In case of transient DB busy, push back into batch
                lock (_lock)
                {
                    _unflushedBatch.AddRange(toWrite);
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _isRunning = false;
                _timer?.Dispose();
            }
        }
    }
}

