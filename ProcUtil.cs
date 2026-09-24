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

    /// <summary>Linux process state: a zombie has exited and waits only for its parent to reap it.</summary>
    const char Zombie = 'Z';

    /// <summary>
    /// The one question every sweep asks of a recorded pid: is it still the process tman recorded?
    /// Liveness and identity are one check, because a pid the OS reused is alive and still not ours.
    /// </summary>
    /// <param name="recordedStartUtc">Null when the process exited before its start could be read.</param>
    /// <param name="recordedStartTicks">
    /// Linux: the recorded start in clock ticks after boot, compared exactly — the wall-clock start is
    /// recomputed from boot time on every read, so a clock step moves it. Null off Linux, and on
    /// records written before tman recorded ticks, which fall back to the wall clock.
    /// </param>
    public static ProcessIdentity Identify(int pid, DateTime? recordedStartUtc, long? recordedStartTicks)
    {
        if (pid <= 0 || recordedStartUtc is null) return ProcessIdentity.Gone;
        try
        {
            if (OperatingSystem.IsLinux() && VerdictFromStat(ReadStat(pid), recordedStartTicks) is { } byStat)
                return byStat;
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return ProcessIdentity.Gone;
            return MatchesWallClock(p.StartTime.ToUniversalTime(), recordedStartUtc.Value)
                ? ProcessIdentity.Mine
                : ProcessIdentity.NotMine;
        }
        catch (Exception e) when (VerdictFor(e) is { } verdict) { return verdict; }
    }

    /// <summary>
    /// What a Linux stat line settles on its own: a zombie is gone, and recorded ticks either match
    /// or name another process. Null when there are no recorded ticks — a legacy record, left to the
    /// wall clock.
    /// </summary>
    internal static ProcessIdentity? VerdictFromStat(ProcStat stat, long? recordedStartTicks)
    {
        if (stat.State == Zombie) return ProcessIdentity.Gone;
        if (recordedStartTicks is not { } recorded) return null;
        var actual = stat.StartTicks ?? throw new InvalidDataException("/proc stat line has no starttime field");
        return actual == recorded ? ProcessIdentity.Mine : ProcessIdentity.NotMine;
    }

    internal static bool MatchesWallClock(DateTime actualUtc, DateTime recordedUtc) =>
        (actualUtc - recordedUtc).Duration() < WallClockTolerance;

    /// <summary>ESRCH: the process went away while its /proc entry was being read.</summary>
    const int NoSuchProcess = 3;

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
        // /proc/<pid>: the entry is gone, or went while it was read
        FileNotFoundException or DirectoryNotFoundException => ProcessIdentity.Gone,
        IOException { HResult: NoSuchProcess } when OperatingSystem.IsLinux() => ProcessIdentity.Gone,
        // /proc/<pid>: readable only by its owner
        UnauthorizedAccessException => ProcessIdentity.NotMine,
        _ => null,
    };

    /// <summary>
    /// The start tman records for a process it spawned: the wall clock everywhere, plus on Linux the
    /// boot-relative ticks <see cref="Identify"/> compares there. Throws when the process is gone.
    /// </summary>
    public static (DateTime Utc, long? Ticks) StartStamp(Process p)
    {
        long? ticks = OperatingSystem.IsLinux()
            ? ReadStat(p.Id).StartTicks ?? throw new InvalidDataException($"/proc/{p.Id}/stat has no starttime field")
            : null;
        return (p.StartTime.ToUniversalTime(), ticks);
    }

    /// <summary>This tman's own start stamp. Its own process is always readable, so this never falls back.</summary>
    public static (DateTime Utc, long? Ticks) OwnStart()
    {
        using var self = Process.GetCurrentProcess();
        return StartStamp(self);
    }

    static ProcStat ReadStat(int pid) =>
        TreeStats.TryParseStat(File.ReadAllText($"/proc/{pid}/stat"), out var stat)
            ? stat
            : throw new InvalidDataException($"/proc/{pid}/stat is not a stat line");

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
