using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RunicWorldEngine.Core;
using HarmonyLib;

namespace RunicWorldEngine.Tests
{
    internal static partial class Program
    {
        private static readonly string[] GameAssemblies =
        {
            @"E:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll",
            @"E:\SteamLibrary\steamapps\common\Valheim dedicated server\valheim_server_Data\Managed\assembly_valheim.dll",
            @"E:\Valheim Mods\Server Operations\deploy-20260911-valheim-1.0.12\assembly_valheim-linux.dll"
        };
        private static void ByteRatesAreSafe()
        {
            var rate = new ByteRate();
            False(rate.Sample(1000, 0).HasValue);
            Equal(100d, rate.Sample(1200, 2).Value);
            False(rate.Sample(0, 3).HasValue);
            Equal(100d, rate.Sample(100, 4).Value);
            False(rate.Sample(150, 4).HasValue);
            rate = new ByteRate();
            rate.Sample(int.MaxValue - 20, 0);
            Equal(41d, rate.Sample(int.MinValue + 20, 1).Value);
        }
        private static void WarningsAreBounded()
        {
            var gate = new WarningLatch();
            Equal(0, gate.Observe(true, 0, 5, 60));
            Equal(0, gate.Observe(true, 4, 5, 60));
            Equal(1, gate.Observe(true, 5, 5, 60));
            Equal(0, gate.Observe(true, 64, 5, 60));
            Equal(1, gate.Observe(true, 65, 5, 60));
            Equal(0, gate.Observe(false, 66, 5, 60));
            Equal(-1, gate.Observe(false, 71, 5, 60));
            Equal(0, gate.Observe(false, 90, 5, 60));
            Equal(0, gate.Observe(true, 91, 5, 60));
            Equal(0, gate.Observe(true, 96, 5, 60));
            Equal(1, gate.Observe(true, 125, 5, 60));
        }
        private static void PayloadMetersAreIndependent()
        {
            var meter = new PayloadMeter();
            False(meter.Sample(0).Sent.HasValue);
            System.Threading.Tasks.Parallel.For(0, 10000, _ => { meter.Sent(2); meter.Received(3); });
            meter.Sent(-100); meter.Received(-100);
            var rates = meter.Sample(1);
            Equal(20000d, rates.Sent.Value); Equal(30000d, rates.Received.Value);
            Equal(0d, meter.Sample(2).Sent.Value);
            var another = new PayloadMeter();
            another.Sample(2); another.Sent(500);
            Equal(500d, another.Sample(3).Sent.Value);
            Equal(0d, meter.Sample(3).Sent.Value);
        }
        private static void SendWindowEstimatesAreSafe()
        {
            Equal((bool?)true, SendWindowPressure.Estimate(false, 8192, 0, 0));
            Equal((bool?)false, SendWindowPressure.Estimate(false, 8000, 0, 0));
            Equal((bool?)true, SendWindowPressure.Estimate(true, null, 32768, 0));
            Equal((bool?)false, SendWindowPressure.Estimate(true, null, 8192, 0));
            Equal((bool?)true, SendWindowPressure.Estimate(false, null, null, 1));
            False(SendWindowPressure.Estimate(false, null, null, 0).HasValue);
        }
        private static void TransfersAreBounded()
        {
            var counter = new TransferCounter<int>(2);
            counter.Record(1, 0, 1, 0);
            counter.Record(1, 1, 1, 0);
            counter.Record(1, 1, 2, 1);
            counter.Record(1, 2, 1, 2);
            counter.Record(1, 1, 0, 3);
            Equal(1L, counter.Assigned); Equal(1L, counter.Released);
            Equal(2L, counter.Transferred); Equal(1L, counter.RapidTransfers);
            counter.ResetInterval(); Equal(0L, counter.Transferred);
            counter.Record(2, 1, 2, 4); counter.Record(3, 1, 2, 5);
            Equal(2, counter.Tracked);
            counter.Record(1, 2, 1, 6); Equal(0L, counter.RapidTransfers);
            counter.Clear(); Equal(0, counter.Tracked);
        }
        private static void CapacityTargetsAreAudited()
        {
            foreach (string path in GameAssemblies)
            {
                using var assembly = AssemblyDefinition.ReadAssembly(path);
                bool dedicated = CapacityAudit.IsDedicated(assembly);
                Equal(path.Contains("dedicated server") || path.Contains("linux.dll"), dedicated);
                var sites = CapacityAudit.Validate(assembly, dedicated);
                Equal(dedicated ? 6 : 5, sites.Length);
                Equal(sites.Length, sites.Select(s => s.Token).Distinct().Count());
            }
        }
        private static void ChangedCapacityIsRejected()
        {
            foreach (string path in GameAssemblies)
            {
                // Every site must independently invalidate the whole audit, not just admission.
                for (int i = 0; i < (path.Contains("dedicated server") || path.Contains("linux.dll") ? 6 : 5); i++)
                {
                    using var assembly = AssemblyDefinition.ReadAssembly(path);
                    bool dedicated = CapacityAudit.IsDedicated(assembly);
                    var site = CapacityAudit.Sites(dedicated)[i];
                    var method = assembly.MainModule.Types.Single(t => t.Name == site.Type).Methods.Single(m => m.Name == site.Method);
                    method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Nop));
                    Throws(() => CapacityAudit.Validate(assembly, dedicated));
                }
            }
        }
        private static void MissingCapacityIsRejected()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(GameAssemblies[0]);
            var type = assembly.MainModule.Types.Single(t => t.Name == "ZPlayFabMatchmaking");
            type.Methods.Remove(type.Methods.Single(m => m.Name == "CreateLobby"));
            Throws(() => CapacityAudit.Validate(assembly, false));
        }
        private static void ExtraCapacityIsRejected()
        {
            using var assembly = AssemblyDefinition.ReadAssembly(GameAssemblies[0]);
            var type = assembly.MainModule.Types.Single(t => t.Name == "ZPlayFabMatchmaking");
            var nested = new TypeDefinition("", "UnexpectedLimit", TypeAttributes.NestedPrivate, assembly.MainModule.TypeSystem.Object);
            type.NestedTypes.Add(nested);
            var method = new MethodDefinition("OtherLimit", MethodAttributes.Static, assembly.MainModule.TypeSystem.Void);
            nested.Methods.Add(method);
            var target = type.Methods.Single(m => m.Name == "CreateAndJoinNetwork").Body.Instructions.Single(i => i.Operand is MethodReference m && m.Name == "set_MaxPlayerCount").Operand;
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 12));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Callvirt, (MethodReference)target));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            Throws(() => CapacityAudit.Validate(assembly, false));
        }
        private static void CapacityBoundsAreCorrect()
        {
            foreach (int cap in new[] { 2, 10, 20, 64 })
            {
                Equal(cap, CapacityAudit.Limit(cap, true, false));
                Equal(cap, CapacityAudit.Limit(cap, false, true));
                Equal(cap + 1, CapacityAudit.Limit(cap, true, true));
            }
            Throws(() => CapacityAudit.Limit(1, true, true));
            Throws(() => CapacityAudit.Limit(65, true, true));
        }
        private static void HealthIsReadOnly()
        {
            string health = Read("RunicWorldEngine", "Integration", "HealthRuntime.cs");
            string transport = Read("RunicWorldEngine", "Integration", "TransportSampler.cs");
            foreach (string forbidden in new[] { "GetAndResetStats(", "GetSendQueueSize(", "GetCurrentSendRate(", "FindSectorObjects(", "GetSaveClone(", "GetAllZDOs(", "Task.Run", "new Thread" })
                False((health + transport).Contains(forbidden));
            Contains(health, "MaximumPeers = 128");
            Contains(health, "now + 1d");
            Contains(health, "new TransferCounter<ZDOID>(1024)");
            Contains(transport, "PartyEndpointGetEndpointStatistics");
            Contains(transport, "GetConnectionRealTimeStatus");
            Contains(health, "not remote-server telemetry");
        }
        private static System.Reflection.MemberInfo FakeMember(CapacityAudit.Site site)
        {
            var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new System.Reflection.AssemblyName("CapacityFixture" + Guid.NewGuid().ToString("N")), System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
            var module = assembly.DefineDynamicModule("fixture");
            var type = module.DefineType(site.MemberType, System.Reflection.TypeAttributes.Public);
            var method = type.DefineMethod(site.Member, System.Reflection.MethodAttributes.Public | System.Reflection.MethodAttributes.Static, typeof(int), Type.EmptyTypes);
            var generator = method.GetILGenerator();
            generator.Emit(System.Reflection.Emit.OpCodes.Ldc_I4_0);
            generator.Emit(System.Reflection.Emit.OpCodes.Ret);
            return type.CreateType().GetMethod(site.Member);
        }
        private static CodeInstruction[] Fixture(CapacityAudit.Site site)
        {
            var constant = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_I4_S, (sbyte)site.Baseline);
            var member = new CodeInstruction(System.Reflection.Emit.OpCodes.Call, FakeMember(site));
            return site.Previous ? new[] { member, constant } : new[] { constant, member };
        }
        private static void CapacityRewritesAreExact()
        {
            foreach (bool dedicated in new[] { false, true })
            foreach (var site in CapacityAudit.Sites(dedicated))
            foreach (int cap in new[] { 2, 10, 20, 64 })
            {
                var fixture = Fixture(site).ToList();
                var constant = fixture[site.Previous ? 1 : 0];
                var label = new System.Reflection.Emit.DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
                constant.labels.Add(label);
                constant.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
                var unrelated = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldc_I4_S, (sbyte)10);
                fixture.Add(new CodeInstruction(System.Reflection.Emit.OpCodes.Nop));
                fixture.Add(unrelated);
                var actual = CapacityRewrite.Apply(fixture, site, cap, dedicated);
                True(ReferenceEquals(constant, actual[site.Previous ? 1 : 0]));
                Equal(CapacityAudit.Limit(cap, dedicated, site.Transport), (int)constant.operand);
                Equal(label, constant.labels.Single());
                Equal(ExceptionBlockType.BeginExceptionBlock, constant.blocks.Single().blockType);
                Equal((sbyte)10, (sbyte)unrelated.operand);
            }
        }
        private static void CapacityRewritesRejectAmbiguity()
        {
            foreach (var site in CapacityAudit.Sites(true))
            {
                Throws(() => CapacityRewrite.Apply(Array.Empty<CodeInstruction>(), site, 20, true));
                var duplicate = Fixture(site).Concat(Fixture(site)).ToList();
                Throws(() => CapacityRewrite.Apply(duplicate, site, 20, true));
                Equal((sbyte)site.Baseline, (sbyte)duplicate[site.Previous ? 1 : 0].operand);
            }
        }
        private static void HealthContractsAreExact()
        {
            foreach (string path in GameAssemblies)
            {
                using var assembly = AssemblyDefinition.ReadAssembly(path);
                var types = CapacityAudit.AllTypes(assembly.MainModule.Types).ToArray();
                foreach (var expected in new[]
                {
                    ("ZNetStats", "m_sentBytes", "System.Int32"), ("ZNetStats", "m_recvBytes", "System.Int32"),
                    ("ZSteamSocket", "m_con", "Steamworks.HSteamNetConnection"),
                    ("ZPlayFabSocket", "m_peer", "PlayFab.Party.PlayFabPlayer[]"),
                    ("ZPlayFabSocket", "m_inFlightQueue", "ZPlayFabSocket/InFlightQueue"),
                    ("ZDOMan/ZDOPeer", "m_peer", "ZNetPeer"),
                    ("ZDOMan/ZDOPeer", "m_forceSend", "System.Collections.Generic.HashSet`1<ZDOID>"),
                    ("ZDOMan/ZDOPeer", "m_invalidSector", "System.Collections.Generic.HashSet`1<ZDOID>")
                })
                    Equal(expected.Item3, types.Single(t => t.FullName == expected.Item1).Fields.Single(f => f.Name == expected.Item2).FieldType.FullName);
                foreach (var tuple in new[] { ("ZSteamSocket", "m_sendQueue"), ("ZSteamSocket", "m_pkgQueue"), ("ZPlayFabSocket", "m_sendQueue"), ("ZPlayFabSocket", "m_recvQueue"), ("ZPlayFabSocket", "m_outOfOrderQueue") })
                {
                    var field = types.Single(t => t.FullName == tuple.Item1).Fields.Single(f => f.Name == tuple.Item2);
                    True(field.FieldType.FullName.StartsWith("System.Collections.Generic.Queue`1") || field.FieldType.FullName.StartsWith("System.Collections.Generic.Dictionary`2"));
                }
                Method(types.Single(t => t.Name == "ZDO"), "SetOwnerInternal", "System.Int64");
                Method(types.Single(t => t.Name == "ZNetStats"), "IncSentBytes", "System.Int32");
                Method(types.Single(t => t.Name == "ZNetStats"), "IncRecvBytes", "System.Int32");
                var send = Method(types.Single(t => t.Name == "ZDOMan"), "SendZDOs", "ZDOMan/ZDOPeer", "System.Boolean");
                Equal("System.Boolean", send.ReturnType.FullName);
            }
        }
        private static void RawMethodCodeIsExact()
        {
            foreach (string path in GameAssemblies)
            {
                byte[] bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes);
                using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
                using var assembly = AssemblyDefinition.ReadAssembly(path);
                foreach (var site in CapacityAudit.Validate(assembly, CapacityAudit.IsDedicated(assembly)))
                    Sequence(System.Reflection.Metadata.PEReaderExtensions.GetMethodBody(pe, site.Rva).GetILBytes(), CapacityAudit.MethodCode(bytes, site.Rva));
            }
            Throws(() => CapacityAudit.MethodCode(Array.Empty<byte>(), 0));
            Throws(() => CapacityAudit.MethodCode(File.ReadAllBytes(GameAssemblies[0]), int.MaxValue));
        }
        private static void Throws(Action run)
        {
            try { run(); } catch { return; }
            throw new InvalidOperationException("Expected rejection.");
        }
    }
}
