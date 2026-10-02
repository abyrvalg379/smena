using System;
using SMENA.Services;
using Xunit;

namespace SMENA.Tests
{
    /// <summary>PowerWatch message constants are the risky part — a wrong wParam mapping
    /// would close blocks at wrong moments. The map is pinned here.</summary>
    public class PowerWatchTests
    {
        [Theory]
        [InlineData(0x0218, 0x0004, PowerWatch.Kind.Suspended)]   // WM_POWERBROADCAST / PBT_APMSUSPEND
        [InlineData(0x0218, 0x0012, PowerWatch.Kind.Resumed)]     // WM_POWERBROADCAST / PBT_APMRESUMEAUTOMATIC
        [InlineData(0x0218, 0x0007, PowerWatch.Kind.None)]        // PBT_APMRESUMESUSPEND — ignore (RESUMEAUTOMATIC already fired)
        [InlineData(0x0218, 0x000A, PowerWatch.Kind.None)]        // PBT_APMPOWERSTATUSCHANGE
        [InlineData(0x02B1, 0x7, PowerWatch.Kind.Locked)]         // WM_WTSSESSION_CHANGE / WTS_SESSION_LOCK
        [InlineData(0x02B1, 0x8, PowerWatch.Kind.Unlocked)]       // WM_WTSSESSION_CHANGE / WTS_SESSION_UNLOCK
        [InlineData(0x02B1, 0x9, PowerWatch.Kind.None)]           // WTS_SESSION_LOGOFF — not ours
        [InlineData(0x00FF, 0x4, PowerWatch.Kind.None)]           // unrelated message
        public void Classify_Maps_Event_Constants(int msg, long wParam, PowerWatch.Kind expected)
        {
            Assert.Equal(expected, PowerWatch.Classify(msg, new IntPtr(wParam)));
        }
    }
}
