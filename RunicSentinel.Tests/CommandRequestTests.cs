using System;
using System.Collections.Generic;
using System.Text;
using RunicSentinel.Core;

namespace RunicSentinel.Tests
{
    internal static partial class Program
    {
        private static void IntegratedCommandBounds()
        {
            foreach(string name in new[]{"save","skiptime","event","stopevent","players","SETKEY"})True(SentinelCommandRouting.IsWorldCommand(name));
            foreach(string name in new[]{"spawn","debugmode","nocost","fly","goto","save;shutdown"})False(SentinelCommandRouting.IsWorldCommand(name));
            True(SentinelCommandRouting.TryCoordinates("100,-200,30",out float x,out float y,out float z));Equal(100f,x);Equal(30f,y);Equal(-200f,z);
            foreach(string invalid in new[]{"NaN,0,0","Infinity,1,1","20001,0,0","1,2","1,2,3,4","1;shutdown,2,3"})False(SentinelCommandRouting.TryCoordinates(invalid,out _,out _,out _));
            True(SentinelCommandRouting.TryReport("steam:123",out string account,out string kind));Equal("steam:123",account);Equal("all",kind);
            foreach(string valid in new[]{"objects","activity","snapshot","all"}){True(SentinelCommandRouting.TryReport("steam:123\n"+valid,out _,out kind));Equal(valid,kind);}
            foreach(string invalid in new[]{"","\nall","steam:123\nall\nextra","steam:123\ndelete","steam:12\r3\nobjects",new string('x',129)})False(SentinelCommandRouting.TryReport(invalid,out _,out _));
        }
        private static void CommandPayloadBounds()
        {
            foreach (string valid in new[] { "tp Bjorn Astrid", "server stopevent", "spawn Wood 10", "nocost", "debugmode", "alias tour goto 1,2;wait 1000;goto 3,4", "bind F7 fly;wait 100;fly" })
            {
                True(SentinelCommandRequest.TryDecode(SentinelCommandRequest.Encode(valid), out string result));
                Equal(valid, result);
            }
            foreach (string invalid in new[] { "", "  ", "spawn Wood\nshutdown", "god; shutdown", "god\r", "god\0", "god\u2028shutdown", new string('x', 1025), new string('é', 513), "\ud800" })
                False(SentinelCommandRequest.TryNormalize(invalid, out _));
            False(SentinelCommandRequest.TryDecode(new byte[] { 0xc3, 0x28 }, out _));
            False(SentinelCommandRequest.TryDecode(null, out _));
            True(SentinelCommandRequest.TryNormalize("  spawn Wood 10  ", out string trimmed));
            Equal("spawn Wood 10", trimmed);
        }

        private static void CommandDispatchRequiresFreshAuthorization()
        {
            Action<bool, string> approval = null;
            var executions = new List<string>();
            var results = new List<bool>();
            bool connection = true;
            SentinelCommandRequest.Dispatch("spawn Wood 10", (_, callback) => approval = callback,
                () => connection, executions.Add, (ok, _) => results.Add(ok));
            Equal(0, executions.Count);
            approval(false, "revoked");
            approval(true, "late duplicate");
            Equal(0, executions.Count);
            Equal(1, results.Count);
            False(results[0]);

            results.Clear();
            SentinelCommandRequest.Dispatch("tp Bjorn Astrid", (_, callback) => approval = callback,
                () => connection, executions.Add, (ok, _) => results.Add(ok));
            connection = false;
            approval(true, "ok");
            Equal(0, executions.Count);
            False(results[0]);

            connection = true;
            results.Clear();
            SentinelCommandRequest.Dispatch("nocost", (_, callback) => approval = callback,
                () => connection, executions.Add, (ok, _) => results.Add(ok));
            approval(true, "ok");
            approval(true, "duplicate");
            Equal(1, executions.Count);
            Equal("nocost", executions[0]);
            Equal(1, results.Count);
            True(results[0]);

            bool authCalled = false;
            SentinelCommandRequest.Dispatch("god;shutdown", (_, callback) => authCalled = true,
                () => true, executions.Add, (ok, _) => False(ok));
            False(authCalled);
            SentinelCommandRequest.Dispatch("nocost", (_, callback) => callback(true, "ok"),
                () => true, _ => throw new InvalidOperationException("provider unavailable"),
                (ok, reason) => { False(ok); Equal("provider unavailable", reason); });
        }
    }
}
