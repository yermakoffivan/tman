using System.ComponentModel;
using System.Diagnostics;
using Tman;
using Xunit;

namespace Tman.Tests;

public class ProcUtilTests
{
    /// <summary>A pid no OS hands out in practice: int.MaxValue - 1.</summary>
    const int UnusedPid = 2147483646;

    // PID 4 is the Windows System process: it always exists and a normal user can never open a
    // handle to it, so Process.HasExited throws Win32Exception "Access is denied". That is the shape
    // of a recorded pid that Windows has since handed to a process tman may not open, and it took
    // down every command that sweeps run records (`tman ls` included) instead of reading as gone.
    [WindowsFact("PID 4 is the protected System process only on Windows")]
    public void Identify_AProcessTmanMayNotOpen_IsNotOneOfItsRuns()
    {
        Assert.Equal(ProcessIdentity.NotMine, ProcUtil.Identify(4, DateTime.UtcNow));
    }

    [Fact]
    public void Identify_ThisProcessAtItsRecordedStart_IsMine()
    {
        using var self = Process.GetCurrentProcess();
        var start = self.StartTime.ToUniversalTime();

        Assert.Equal(ProcessIdentity.Mine, ProcUtil.Identify(self.Id, start));
    }

    [Fact]
    public void Identify_ALivePidStartedAtAnotherTime_IsNotMine()
    {
        using var self = Process.GetCurrentProcess();
        var someoneElsesStart = self.StartTime.ToUniversalTime() - TimeSpan.FromHours(1);

        Assert.Equal(ProcessIdentity.NotMine, ProcUtil.Identify(self.Id, someoneElsesStart));
    }

    [Theory]
    [InlineData(UnusedPid)]
    // a record whose pid defaulted to 0 names the Windows idle process, and every process on POSIX
    // to kill(2); neither is a run
    [InlineData(0)]
    [InlineData(-1)]
    public void Identify_APidNoRunHolds_IsGone(int pid)
    {
        Assert.Equal(ProcessIdentity.Gone, ProcUtil.Identify(pid, DateTime.UtcNow));
    }

    [Fact]
    public void VerdictFor_MapsOnlyTheFailuresTheRuntimeDocumentsForAMissingOrForeignPid()
    {
        Assert.Equal(ProcessIdentity.Gone, ProcUtil.VerdictFor(new ArgumentException("no such pid")));
        Assert.Equal(ProcessIdentity.NotMine, ProcUtil.VerdictFor(new Win32Exception(5)));
        // anything else is a defect in tman or the runtime, and must surface rather than read as a verdict
        Assert.Null(ProcUtil.VerdictFor(new InvalidOperationException()));
        Assert.Null(ProcUtil.VerdictFor(new NullReferenceException()));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1.9, true)]
    [InlineData(-1.9, true)]
    [InlineData(2, false)]
    [InlineData(-10, false)]
    public void MatchesWallClock_ToleratesTheRecomputationJitterOnly(double offsetSeconds, bool same)
    {
        var recorded = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(same, ProcUtil.MatchesWallClock(recorded.AddSeconds(offsetSeconds), recorded));
    }
}
