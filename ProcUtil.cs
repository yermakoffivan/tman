using System.ComponentModel;
using System.Diagnostics;

namespace Tman;

/// <summary>What a recorded pid names now, measured against the process tman recorded under it.</summary>
public enum ProcessIdentity
{
    /// <summary>The pid still names the process tman recorded.</summary>
    Mine,
    /// <summary>No process holds the pid any more.</summary>
    Gone,
    /// <summary>
    /// Another process holds the pid: the OS handed it out again, or it names a process this user
    /// may not open. tman records only processes it spawned as this user, so one it may not open is
    /// someone else's.
    /// </summary>
    NotMine,
}

public static class ProcUtil
{
    /// <summary>
    /// How far a re-read wall-clock start may sit from the recorded one and still name the same
    /// process. Linux recomputes a start from boot time on every read, so it moves with the clock.
    /// </summary>
    static readonly TimeSpan WallClockTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The one question every sweep asks of a recorded pid: is it still the process tman recorded?
    /// Liveness and identity are one check, because a pid the OS reused is alive and still not ours.
    /// </summary>
    public static ProcessIdentity Identify(int pid, DateTime recordedStartUtc)
    {
        if (pid <= 0) return ProcessIdentity.Gone;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return ProcessIdentity.Gone;
            return MatchesWallClock(p.StartTime.ToUniversalTime(), recordedStartUtc)
                ? ProcessIdentity.Mine
                : ProcessIdentity.NotMine;
        }
        catch (Exception e) when (VerdictFor(e) is { } verdict) { return verdict; }
    }

    internal static bool MatchesWallClock(DateTime actualUtc, DateTime recordedUtc) =>
        (actualUtc - recordedUtc).Duration() < WallClockTolerance;

    /// <summary>
    /// The verdict an exception raised while inspecting a pid carries, or null when it carries none
    /// and must propagate. Only failures the runtime documents for a missing or foreign process map.
    /// </summary>
    internal static ProcessIdentity? VerdictFor(Exception e) => e switch
    {
        // GetProcessById: no process holds the pid
        ArgumentException => ProcessIdentity.Gone,
        // Windows: OpenProcess refused (access denied). Linux/macOS: the start time is unreadable —
        // another user's process, hidepid, or another pid namespace
        Win32Exception => ProcessIdentity.NotMine,
        _ => null,
    };

    public static DateTime? StartTimeUtc(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.StartTime.ToUniversalTime();
        }
        catch { return null; }
    }

    public static bool TryRefresh(int pid, out Process? proc)
    {
        proc = null;
        try
        {
            var p = Process.GetProcessById(pid);
            if (p.HasExited) { p.Dispose(); return false; }
            p.Refresh();
            proc = p;
            return true;
        }
        catch { return false; }
    }

    public static void KillTree(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            if (!p.HasExited) p.Kill(entireProcessTree: true);
        }
        catch { }
    }
}
