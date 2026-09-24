using Tman;
using Xunit;

namespace Tman.Tests;

public class ProcUtilTests
{
    // PID 4 is the Windows System process: it always exists and a normal user can never open a
    // handle to it, so Process.HasExited throws Win32Exception "Access is denied". That is the shape
    // of a recorded pid that Windows has since handed to a process tman may not open, and it took
    // down every command that sweeps run records (`tman ls` included) instead of reading as gone.
    [WindowsFact("PID 4 is the protected System process only on Windows")]
    public void IsAlive_AProcessTmanMayNotOpen_IsNotOneOfItsRuns()
    {
        Assert.False(ProcUtil.IsAlive(4));
    }
}
