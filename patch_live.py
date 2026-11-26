from pathlib import Path
root=Path('/mnt/data/runmind-live-resource')
p=root/'Shared/SystemManager.cs'
s=p.read_text(encoding='utf-8-sig')
old='''    private sealed record ResourceProcessSample(int ProcessId, long StartedAtTicks, long CpuTicks, long WorkingSetBytes, long PrivateMemoryBytes, string Name);
    private sealed class ResourceProcessAggregate
'''
new='''    private sealed record ResourceProcessSample(int ProcessId, long StartedAtTicks, long CpuTicks, long WorkingSetBytes, long PrivateMemoryBytes, string Name);
    private sealed record NetworkByteSnapshot(long ReceivedBytes, long SentBytes, int InterfaceCount);
    private sealed class LiveResourceSession
    {
        public required string Id { get; init; }
        public required int DurationSeconds { get; init; }
        public required int IntervalSeconds { get; init; }
        public required DateTimeOffset StartedAtUtc { get; init; }
        public required Stopwatch Clock { get; init; }
        public required long PreviousSampleTimestamp { get; set; }
        public required Dictionary<int, ResourceProcessSample> PreviousProcesses { get; set; }
        public (ulong Idle, ulong Kernel, ulong User)? PreviousSystemCpu { get; set; }
        public NetworkByteSnapshot? PreviousNetwork { get; set; }
        public long Sequence { get; set; }
    }
    private sealed class ResourceProcessAggregate
'''
if old not in s: raise SystemExit('record anchor missing')
s=s.replace(old,new,1)
old='''    private static Dictionary<int, ResourceProcessSample> ReadResourceProcessSnapshot(out bool truncated)
    {
'''
new='''    private static NetworkByteSnapshot? ReadNetworkByteSnapshot()
    {
        try
        {
            long received = 0;
            long sent = 0;
            var count = 0;
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                try
                {
                    var statistics = networkInterface.GetIPStatistics();
                    if (statistics.BytesReceived < 0 || statistics.BytesSent < 0) continue;
                    received = checked(received + statistics.BytesReceived);
                    sent = checked(sent + statistics.BytesSent);
                    count++;
                }
                catch { }
            }
            return count > 0 ? new NetworkByteSnapshot(received, sent, count) : null;
        }
        catch { return null; }
    }

    private static Dictionary<int, ResourceProcessSample> ReadResourceProcessSnapshot(out bool truncated)
    {
'''
if old not in s: raise SystemExit('process snapshot anchor missing')
s=s.replace(old,new,1)
# Insert live session methods immediately after existing snapshot CPU reader, before finite one-shot monitor.
anchor='''    private static (ulong Idle, ulong Kernel, ulong User)? ReadSystemCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        return (idle.Value, kernel.Value, user.Value);
    }

    private static async Task<object> SystemManagerSampleResourcesAsync(JsonElement args, CancellationToken cancellationToken)
'''
insert='''    private static (ulong Idle, ulong Kernel, ulong User)? ReadSystemCpuTimes()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        return (idle.Value, kernel.Value, user.Value);
    }

    private object SystemManagerLiveResourcesStart(JsonElement args)
    {
        RequireSystemManagerCapability("resourceMonitoring");
        var duration = args.TryGetProperty("duration_seconds", out var durationValue) && durationValue.TryGetInt32(out var requestedDuration) ? requestedDuration : 300;
        var interval = args.TryGetProperty("interval_seconds", out var intervalValue) && intervalValue.TryGetInt32(out var requestedInterval) ? requestedInterval : 1;
        if (duration is < 30 or > 900) throw new InvalidDataException("Live resource sessions must be between 30 seconds and 15 minutes.");
        if (interval is not (1 or 2 or 5)) throw new InvalidDataException("Live resource sampling interval must be 1, 2 or 5 seconds.");

        lock (_liveResourceSync)
        {
            if (_liveResourceSession is { } active && active.Clock.Elapsed.TotalSeconds < active.DurationSeconds)
                throw new InvalidOperationException("A live resource monitoring session is already active in this RunMind window.");

            var now = Stopwatch.GetTimestamp();
            var session = new LiveResourceSession
            {
                Id = Guid.NewGuid().ToString("N"),
                DurationSeconds = duration,
                IntervalSeconds = interval,
                StartedAtUtc = DateTimeOffset.UtcNow,
                Clock = Stopwatch.StartNew(),
                PreviousSampleTimestamp = now,
                PreviousProcesses = ReadResourceProcessSnapshot(out _),
                PreviousSystemCpu = ReadSystemCpuTimes(),
                PreviousNetwork = ReadNetworkByteSnapshot()
            };
            _liveResourceSession = session;
            return new
            {
                success = true,
                sessionId = session.Id,
                startedAtUtc = session.StartedAtUtc,
                durationSeconds = duration,
                intervalSeconds = interval,
                processorCount = Environment.ProcessorCount,
                storage = "volatile-memory-only",
                note = "Monitoring is user-started, read-only and bounded. Samples remain in this window's memory and are not persisted. Closing the System Manager panel or RunMind window stops the session."
            };
        }
    }

    private object SystemManagerLiveResourcesSample(JsonElement args)
    {
        RequireSystemManagerCapability("resourceMonitoring");
        var requestedId = ReadBoundedExactString(args, "session_id", 64);
        if (requestedId.Length != 32 || !requestedId.All(Uri.IsHexDigit)) throw new InvalidDataException("A valid live resource session ID is required.");

        lock (_liveResourceSync)
        {
            var session = _liveResourceSession;
            if (session is null || !string.Equals(session.Id, requestedId, StringComparison.Ordinal))
                return new { success = false, accepted = false, running = false, stopped = true, reason = "session-not-found" };
            if (session.Clock.Elapsed.TotalSeconds >= session.DurationSeconds)
            {
                _liveResourceSession = null;
                return new { success = true, accepted = false, running = false, expired = true, elapsedSeconds = Math.Round(session.Clock.Elapsed.TotalSeconds, 2), reason = "duration-limit-reached" };
            }

            var currentAt = Stopwatch.GetTimestamp();
            var elapsed = Math.Max(0.001, (double)(currentAt - session.PreviousSampleTimestamp) / Stopwatch.Frequency);
            var minimumInterval = Math.Max(0.5, session.IntervalSeconds * 0.7);
            if (elapsed < minimumInterval)
                return new { success = true, accepted = false, running = true, retryAfterMilliseconds = (int)Math.Ceiling((minimumInterval - elapsed) * 1000), sequence = session.Sequence };

            var currentProcesses = ReadResourceProcessSnapshot(out var processListTruncated);
            var currentSystemCpu = ReadSystemCpuTimes();
            double? machineCpuPercent = null;
            if (session.PreviousSystemCpu.HasValue && currentSystemCpu.HasValue)
            {
                var totalDelta = (currentSystemCpu.Value.Kernel - session.PreviousSystemCpu.Value.Kernel) + (currentSystemCpu.Value.User - session.PreviousSystemCpu.Value.User);
                var idleDelta = currentSystemCpu.Value.Idle - session.PreviousSystemCpu.Value.Idle;
                if (totalDelta > 0 && totalDelta >= idleDelta)
                    machineCpuPercent = Math.Round(Math.Clamp((double)(totalDelta - idleDelta) * 100d / totalDelta, 0, 100), 2);
            }

            var topCpu = new List<object>();
            var processorCount = Math.Max(1, Environment.ProcessorCount);
            foreach (var current in currentProcesses.Values)
            {
                if (current.CpuTicks < 0 || current.StartedAtTicks <= 0 || !session.PreviousProcesses.TryGetValue(current.ProcessId, out var previous) || previous.CpuTicks < 0 || previous.StartedAtTicks != current.StartedAtTicks) continue;
                var cpuSeconds = Math.Max(0, current.CpuTicks - previous.CpuTicks) / (double)TimeSpan.TicksPerSecond;
                var machineShare = Math.Clamp(cpuSeconds / elapsed / processorCount * 100d, 0, 100);
                if (machineShare < 0.01) continue;
                topCpu.Add(new
                {
                    processId = current.ProcessId,
                    name = current.Name,
                    machineCpuSharePercent = Math.Round(machineShare, 2),
                    singleCoreEquivalentPercent = Math.Round(Math.Clamp(cpuSeconds / elapsed * 100d, 0, 100 * processorCount), 2),
                    workingSetBytes = current.WorkingSetBytes,
                    privateMemoryBytes = current.PrivateMemoryBytes,
                    startedAtUtc = new DateTimeOffset(current.StartedAtTicks, TimeSpan.Zero)
                });
            }
            var orderedCpu = topCpu.OrderByDescending(item =>
            {
                var json = JsonSerializer.SerializeToElement(item);
                return json.TryGetProperty("machineCpuSharePercent", out var value) && value.TryGetDouble(out var number) ? number : 0;
            }).Take(8).ToArray();
            var topMemory = currentProcesses.Values.OrderByDescending(item => item.WorkingSetBytes).Take(8).Select(item => new
            {
                processId = item.ProcessId,
                name = item.Name,
                workingSetBytes = item.WorkingSetBytes,
                privateMemoryBytes = item.PrivateMemoryBytes,
                startedAtUtc = item.StartedAtTicks > 0 ? new DateTimeOffset(item.StartedAtTicks, TimeSpan.Zero) : (DateTimeOffset?)null
            }).ToArray();

            var currentNetwork = ReadNetworkByteSnapshot();
            double? receiveBytesPerSecond = null;
            double? sendBytesPerSecond = null;
            if (currentNetwork is not null && session.PreviousNetwork is not null && currentNetwork.InterfaceCount > 0)
            {
                var receivedDelta = currentNetwork.ReceivedBytes - session.PreviousNetwork.ReceivedBytes;
                var sentDelta = currentNetwork.SentBytes - session.PreviousNetwork.SentBytes;
                if (receivedDelta >= 0 && sentDelta >= 0)
                {
                    receiveBytesPerSecond = Math.Round(receivedDelta / elapsed, 1);
                    sendBytesPerSecond = Math.Round(sentDelta / elapsed, 1);
                }
            }

            var memory = ReadSystemMemoryStatus();
            var totalWorkingSet = currentProcesses.Values.Sum(item => Math.Max(0, item.WorkingSetBytes));
            var totalPrivateMemory = currentProcesses.Values.Sum(item => Math.Max(0, item.PrivateMemoryBytes));
            session.PreviousProcesses = currentProcesses;
            session.PreviousSystemCpu = currentSystemCpu;
            session.PreviousNetwork = currentNetwork;
            session.PreviousSampleTimestamp = currentAt;
            session.Sequence++;

            return new
            {
                success = true,
                accepted = true,
                running = true,
                sessionId = session.Id,
                sequence = session.Sequence,
                sampledAtUtc = DateTimeOffset.UtcNow,
                elapsedSeconds = Math.Round(session.Clock.Elapsed.TotalSeconds, 2),
                sampleIntervalSeconds = Math.Round(elapsed, 2),
                remainingSeconds = Math.Max(0, (int)Math.Ceiling(session.DurationSeconds - session.Clock.Elapsed.TotalSeconds)),
                processorCount,
                machineCpuPercent,
                memory,
                network = new
                {
                    available = receiveBytesPerSecond.HasValue && sendBytesPerSecond.HasValue,
                    receiveBytesPerSecond,
                    sendBytesPerSecond,
                    interfaceCount = currentNetwork?.InterfaceCount ?? 0,
                    note = "Rates sum observed operational non-loopback interfaces. VPN, bridge and virtual interfaces may overlap; these are interface counters, not application attribution."
                },
                processes = new
                {
                    count = currentProcesses.Count,
                    truncated = processListTruncated,
                    totalWorkingSetBytes = totalWorkingSet,
                    totalPrivateMemoryBytes = totalPrivateMemory,
                    topCpu = orderedCpu,
                    topMemory
                },
                alerts = new
                {
                    highCpu = machineCpuPercent.HasValue && machineCpuPercent.Value >= 85,
                    highMemory = memory is not null && JsonSerializer.SerializeToElement(memory).TryGetProperty("memoryLoadPercent", out var memoryLoad) && memoryLoad.TryGetDouble(out var memoryPercent) && memoryPercent >= 90
                },
                notes = new[]
                {
                    "CPU and memory are Windows host measurements. A missing measurement remains null/unavailable, never zero.",
                    "Process CPU uses process CPU-time deltas matched by PID and process start time; inaccessible or short-lived processes may be absent.",
                    "Process working-set/private-memory values are sampled for up to the first 1,000 processes returned by Windows.",
                    "Network rates sum operational non-loopback interfaces and can overlap on VPN, bridge or virtual networking configurations.",
                    "Physical disk throughput is deliberately not polled by this low-overhead live session. Use the finite resource report for supported physical-disk counters.",
                    "CPU/memory alert flags are fixed display thresholds (85% CPU, 90% memory), not root-cause or anomaly claims."
                }
            };
        }
    }

    private object SystemManagerLiveResourcesStop(JsonElement args)
    {
        var requestedId = ReadBoundedExactString(args, "session_id", 64);
        lock (_liveResourceSync)
        {
            if (_liveResourceSession is null) return new { success = true, stopped = true, alreadyStopped = true };
            if (requestedId.Length > 0 && !string.Equals(_liveResourceSession.Id, requestedId, StringComparison.Ordinal))
                return new { success = false, stopped = false, reason = "session-id-mismatch" };
            _liveResourceSession = null;
            return new { success = true, stopped = true, stoppedAtUtc = DateTimeOffset.UtcNow };
        }
    }

    private void StopLiveResourceSessionForDispose()
    {
        lock (_liveResourceSync) _liveResourceSession = null;
    }

    private static async Task<object> SystemManagerSampleResourcesAsync(JsonElement args, CancellationToken cancellationToken)
'''
if anchor not in s: raise SystemExit('live methods anchor missing')
s=s.replace(anchor,insert,1)
p.write_text(s,encoding='utf-8-sig')
