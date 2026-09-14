using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RunicWorldEngine.Core
{
    // Audited hosting consumers, not every occurrence of the integer 10.
    // The legacy (< network version 35) remote-server browser fallback is not a host limit.
    internal static class CapacityAudit
    {
        internal sealed class Site
        {
            internal string Type, Method, MemberType, Member;
            internal bool Previous, Transport;
            internal int Baseline;
            internal int Token;
            internal int Rva;
        }

        internal static Site[] Sites(bool dedicated) => new[]
        {
            new Site { Type = "ZNet", Method = "RPC_PeerInfo", MemberType = "ZNet", Member = "GetNrOfPlayers", Previous = true, Baseline = 10 },
            new Site { Type = "ZSteamMatchmaking", Method = "RegisterServer", MemberType = dedicated ? "Steamworks.SteamGameServer" : "Steamworks.SteamMatchmaking", Member = dedicated ? "SetMaxPlayerCount" : "CreateLobby", Baseline = 10 },
            new Site { Type = "ZPlayFabMatchmaking", Method = "SetPlatformMatchmakingData", MemberType = "Splatform.MultiplayerSessionData", Member = "m_maxPlayers", Baseline = 10 },
            new Site { Type = "ZPlayFabMatchmaking", Method = "CreateLobby", MemberType = "PlayFab.MultiplayerModels.CreateLobbyRequest", Member = "MaxPlayers", Baseline = dedicated ? 11 : 10, Transport = true },
            new Site { Type = "ZPlayFabMatchmaking", Method = "CreateAndJoinNetwork", MemberType = "PlayFab.Party.PlayFabNetworkConfiguration", Member = "set_MaxPlayerCount", Baseline = dedicated ? 11 : 10, Transport = true }
        }.Concat(dedicated ? new[] { new Site { Type = "SteamManager", Method = "Awake", MemberType = "Steamworks.SteamGameServer", Member = "SetMaxPlayerCount", Baseline = 64 } } : Array.Empty<Site>()).ToArray();

        // Whole method bodies + exception regions, independently audited for client/listen and dedicated 1.0.12.
        internal static readonly HashSet<string> Fingerprints = new HashSet<string>(StringComparer.Ordinal)
        {
            "EC5CA7EC57BA5C5A2EFF1C41FA3C7F6B2D75A497FC990D58F8ADDC3CD54BC7E0",
            "459DD8DBAB089DF496991274244B62297AD68764D988D78475D8251A20343E63",
            "F54B5B813E98AD518E07CF56E69A4864C4515FC3184472D31A4FFC3036D3D2A3",
            "F66F7EDC4D1A499F505B65F7924BF175A5383F7EC6EEC4C11FD6869DDD900DCE",
            "490240F2759B3EDF43BE848F6ACBC4DB524913E72514C6051C434062DFC77434",
            "3AFFCFECC1E54E67D24BCA4783D924B027F8858F2CF9DAEC11EE49E3B2ACE6FE",
            "DD13270F59DB84CCCDBB943803EE216854704FDCED7612443D7CDB6222AF1117",
            "4C2CF929012527CCDCE74F530E008B34A250C8A4006A16C2EFD65AFA374A95A4",
            "C25809B87CB42B4553CF5EB3E6AB3FF058CD0F7575C22E83D30179A769A68DAE",
            "CD68B17D73F15C90C439F2E0A2622188140C0767BEAAEE4DB1AF6807898A674C"
        };

        internal static int Limit(int players, bool dedicated, bool transport)
        {
            if (players < 2 || players > 64) throw new ArgumentOutOfRangeException(nameof(players));
            return checked(players + (dedicated && transport ? 1 : 0));
        }

        internal static string Fingerprint(MethodDefinition method)
        {
            string text = string.Join("\n", method.Body.Instructions.Select(i => i.ToString())) + "\n" +
                string.Join("\n", method.Body.ExceptionHandlers.Select(e =>
                    $"{e.HandlerType}|{e.TryStart?.Offset}|{e.TryEnd?.Offset}|{e.HandlerStart?.Offset}|{e.HandlerEnd?.Offset}|{e.FilterStart?.Offset}|{e.CatchType?.FullName}"));
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "");
        }

        internal static Site[] Validate(AssemblyDefinition assembly, bool dedicated)
        {
            Site[] sites = Sites(dedicated);
            var accepted = new HashSet<Instruction>();
            foreach (Site site in sites)
            {
                var type = assembly.MainModule.Types.Single(t => t.FullName == site.Type);
                var method = type.Methods.Single(m => m.Name == site.Method);
                if (!method.HasBody || !Fingerprints.Contains(Fingerprint(method)))
                    throw new InvalidOperationException("Unaudited player-cap method: " + site.Type + "." + site.Method);
                var il = method.Body.Instructions;
                int hits = 0;
                for (int i = 0; i < il.Count; i++)
                {
                    int adjacent = i + (site.Previous ? -1 : 1);
                    if (!TryConstant(il[i], out int value) || adjacent < 0 || adjacent >= il.Count) continue;
                    if (!(il[adjacent].Operand is MemberReference member) || member.Name != site.Member || member.DeclaringType.FullName != site.MemberType) continue;
                    if (value != site.Baseline) throw new InvalidOperationException("Changed capacity literal in " + method.FullName);
                    hits++;
                    accepted.Add(il[i]);
                }
                if (hits != 1) throw new InvalidOperationException("Expected exactly one cap site: " + method.FullName);
                site.Token = method.MetadataToken.ToInt32();
                site.Rva = method.RVA;
            }
            // Detect additional literal hosting-limit consumers, including nested/generated methods.
            foreach (var type in AllTypes(assembly.MainModule.Types))
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                var il = method.Body.Instructions;
                for (int i = 0; i < il.Count; i++)
                {
                    if (!TryConstant(il[i], out int literal) || accepted.Contains(il[i])) continue;
                    var next = i + 1 < il.Count ? il[i + 1].Operand as MemberReference : null;
                    var prev = i > 0 ? il[i - 1].Operand as MemberReference : null;
                    if (type.FullName == "ServerListEntryData" && method.Name == ".ctor" && next?.Name == "m_playerLimit") continue;
                    if (type.FullName == "ServerMatchmakingData" && method.Name == ".ctor" && next?.Name == "m_playerLimit" && literal == 0) continue;
                    if (type.FullName == "ZNet" && method.Name == "UpdateNetTime" && prev?.Name == "GetNrOfPlayers" && literal == 0) continue;
                    if (prev?.Name == "GetNrOfPlayers" || (next != null &&
                        new[] { "SetMaxPlayerCount", "set_MaxPlayerCount", "MaxPlayers", "m_maxPlayers", "m_playerLimit", "CreateLobby", "SetLobbyMemberLimit" }.Contains(next.Name)))
                        throw new InvalidOperationException("Unaccounted literal limit: " + method.FullName + " / " + il[i]);
                }
            }
            return sites;
        }

        internal static bool IsDedicated(AssemblyDefinition assembly)
        {
            var method = assembly.MainModule.Types.Single(t => t.Name == "ZNet").Methods.Single(m => m.Name == "IsDedicated");
            var il = method.Body.Instructions;
            if (il.Count != 2 || il[1].OpCode.Code != Code.Ret || !TryConstant(il[0], out int value) || (value != 0 && value != 1))
                throw new InvalidOperationException("Unknown dedicated-server build marker.");
            return value == 1;
        }

        internal static byte[] MethodCode(byte[] image, int rva)
        {
            // Read the audited PE method's IL so preloader-rewritten, in-memory bodies cannot
            // pass merely because the untouched file on disk passed the Cecil audit.
            if (image == null || image.Length < 64 || image[0] != 'M' || image[1] != 'Z') throw new InvalidOperationException("Invalid game PE image.");
            int pe = BitConverter.ToInt32(image, 0x3c);
            if (BitConverter.ToInt32(image, pe) != 0x4550) throw new InvalidOperationException("Invalid PE signature.");
            int count = BitConverter.ToUInt16(image, pe + 6);
            int sections = pe + 24 + BitConverter.ToUInt16(image, pe + 20);
            for (int i = 0; i < count; i++)
            {
                int section = checked(sections + i * 40);
                int start = BitConverter.ToInt32(image, section + 12);
                int rawSize = BitConverter.ToInt32(image, section + 16);
                if (rva < start || (long)rva - start >= rawSize) continue;
                int offset = checked(BitConverter.ToInt32(image, section + 20) + rva - start);
                int header, size;
                if ((image[offset] & 3) == 2) { header = 1; size = image[offset] >> 2; }
                else if ((image[offset] & 3) == 3)
                { header = (BitConverter.ToUInt16(image, offset) >> 12) * 4; size = BitConverter.ToInt32(image, offset + 4); }
                else throw new InvalidOperationException("Unknown method header.");
                if (header < 1 || size < 0 || (long)offset + header + size > image.Length) throw new InvalidOperationException("Truncated method body.");
                var result = new byte[size];
                Buffer.BlockCopy(image, offset + header, result, 0, size);
                return result;
            }
            throw new InvalidOperationException("Method RVA outside game PE sections.");
        }

        internal static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
        {
            foreach (var type in types)
            {
                yield return type;
                foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
            }
        }

        internal static bool TryConstant(Instruction instruction, out int value)
        {
            value = 0;
            if (instruction.OpCode.Code == Code.Ldc_I4 || instruction.OpCode.Code == Code.Ldc_I4_S)
            { value = Convert.ToInt32(instruction.Operand); return true; }
            if (instruction.OpCode.Code >= Code.Ldc_I4_0 && instruction.OpCode.Code <= Code.Ldc_I4_8)
            { value = instruction.OpCode.Code - Code.Ldc_I4_0; return true; }
            return false;
        }
    }
}
