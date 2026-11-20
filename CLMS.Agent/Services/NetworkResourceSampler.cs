using System.Net.NetworkInformation;
using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

public sealed record NetworkByteCounters(string InterfaceId, long Received, long Sent);

/// <summary>Samples active non-loopback interfaces without requiring native performance counters.</summary>
public sealed class NetworkResourceSampler(
    Func<IReadOnlyList<NetworkByteCounters>>? readCounters = null, TimeProvider? clock = null)
{
    private readonly object sync = new();
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private Dictionary<string, NetworkByteCounters> previous = [];
    private long? previousTimestamp;

    public void Reset()
    {
        lock (sync) { previous.Clear(); previousTimestamp = null; }
    }

    public NetworkResourceReading? Sample()
    {
        lock (sync)
        {
            var counters = (readCounters ?? ReadCounters)();
            var timestamp = time.GetTimestamp();
            var seconds = previousTimestamp.HasValue
                ? time.GetElapsedTime(previousTimestamp.Value, timestamp).TotalSeconds : 0;
            double received = 0, sent = 0;
            var matched = false;
            foreach (var current in counters)
            {
                if (!previous.TryGetValue(current.InterfaceId, out var old)
                    || current.Received < old.Received || current.Sent < old.Sent) continue;
                received += current.Received - old.Received;
                sent += current.Sent - old.Sent;
                matched = true;
            }
            previous = counters.ToDictionary(counter => counter.InterfaceId);
            previousTimestamp = timestamp;
            return matched && seconds > 0 ? new(received / seconds, sent / seconds) : null;
        }
    }

    private static IReadOnlyList<NetworkByteCounters> ReadCounters()
    {
        var counters = new List<NetworkByteCounters>();
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            try
            {
                if (adapter.OperationalStatus != OperationalStatus.Up
                    || adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = adapter.GetIPStatistics();
                counters.Add(new(adapter.Id, stats.BytesReceived, stats.BytesSent));
            }
            catch (NetworkInformationException) { /* An adapter may disappear during enumeration. */ }
        }
        return counters;
    }
}
