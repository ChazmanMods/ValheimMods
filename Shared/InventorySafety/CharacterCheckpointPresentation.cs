using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using HarmonyLib;

namespace RunicAutomation.Operations
{
        // Required checkpoint writes and verification still execute. A queued Vault upload can
        // reuse that exact verified profile instead of writing it again solely to trigger upload.
        // Framework delegates keep scope and proof shared by all embedded module copies.
    internal static class CharacterCheckpointPresentation
    {
        private static Dictionary<string, Delegate> Routes => SharedState.Get("operations.checkpoint-presentation.v1", MakeRoutes);
        internal static bool Active => ((Func<bool>)Routes["active"])();
        internal static IDisposable Enter()
        {
            EnsureVaultCompatibility();
            return EnterScope(true);
        }
        private static IDisposable EnterScope(bool silent) => ((Func<bool, IDisposable>)Routes["enter"])(silent);
        internal static void EnsureVaultCompatibility() => ((Action)Routes["vault-install"])();
        internal static void RecordVaultUpload(object service, string before, string after, bool silent) =>
            ((Action<object, string, string, bool>)Routes["vault-record"])(service, before, after, silent);
        internal static void ClearVaultOrigin(object service) => ((Action<object>)Routes["vault-clear"])(service);
        internal static bool IsVaultFollowUpSilent(object service, string pending) =>
            ((Func<object, string, bool>)Routes["vault-silent"])(service, pending);
        internal static void Verified(PlayerProfile profile, byte[] playerData)
        {
            try { ((Action<PlayerProfile, byte[]>)Routes["verified"])(profile, playerData); }
            catch (Exception error) { Warn("verified profile proof unavailable: " + error.GetType().Name); }
        }

        private sealed class Scope : IDisposable
        {
            private Action close;
            internal Scope(Action close) => this.close = close;
            public void Dispose() { var action = close; close = null; action?.Invoke(); }
        }
        private static Dictionary<string, Delegate> MakeRoutes()
        {
            var scopes = new ThreadLocal<LinkedList<bool>>(() => new LinkedList<bool>());
            var origins = new ConditionalWeakTable<object, StrongBox<string>>();
            var verified = new ConditionalWeakTable<PlayerProfile, Tuple<string, int, long, string, byte[]>>();
            bool installed = false, installing = false, failed = false;
            return new Dictionary<string, Delegate>
            {
                ["active"] = new Func<bool>(() => scopes.Value.Last?.Value == true),
                ["verified"] = new Action<PlayerProfile, byte[]>((profile, data) =>
                {
                    if (profile == null) return;
                    verified.Remove(profile);
                    if (data != null) verified.Add(profile, Tuple.Create(profile.GetFilename(), (int)profile.m_fileSource,
                        profile.GetPlayerID(), profile.GetName(), (byte[])data.Clone()));
                }),
                ["verified-data"] = new Func<PlayerProfile, byte[]>(profile => profile != null && verified.TryGetValue(profile, out var proof) &&
                    proof.Item1 == profile.GetFilename() && proof.Item2 == (int)profile.m_fileSource && proof.Item3 == profile.GetPlayerID() &&
                    proof.Item4 == profile.GetName() ? proof.Item5 : null),
                ["enter"] = new Func<bool, IDisposable>(silent =>
                {
                    var list = scopes.Value; var entry = list.AddLast(silent);
                    return new Scope(() => { if (entry.List != null) list.Remove(entry); });
                }),
                ["vault-record"] = new Action<object, string, string, bool>((service, before, after, silent) =>
                {
                    if (service == null) return;
                    bool previousSilent = before != null && origins.TryGetValue(service, out var previous) && previous.Value == before;
                    origins.Remove(service);
                    // A normal save joining an existing ID permanently makes that pending
                    // request visible. Later quiet saves cannot upgrade mixed-origin work.
                    if (silent && after != null && (before == null || before == after && previousSilent))
                        origins.Add(service, new StrongBox<string>(after));
                }),
                ["vault-clear"] = new Action<object>(service => { if (service != null) origins.Remove(service); }),
                ["vault-silent"] = new Func<object, string, bool>((service, pending) => service != null && pending != null &&
                    origins.TryGetValue(service, out var origin) && origin.Value == pending),
                ["vault-install"] = new Action(() =>
                {
                    lock (SharedState.Sync)
                    {
                        if (installed || installing || failed) return;
                        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "RunicCharacterVault");
                        if (assembly == null) return; // Optional plugin may load after this module.
                        installing = true;
                        try { InstallVault(assembly.GetType("RunicCharacterVault.ProfileTransferService", true)); installed = true; }
                        catch (Exception error)
                        {
                            failed = true;
                            try { new Harmony(VaultHarmonyId).UnpatchSelf(); } catch { }
                            Warn("optional Character Vault queued-save compatibility unavailable: " + error.GetType().Name);
                        }
                        finally { installing = false; }
                    }
                })
            };
        }

        private const string VaultHarmonyId = "chazman.RunicInventoryCheckpointPresentation.Vault";
        private static FieldInfo vaultPending;
        private static MethodInfo vaultUpload;
        private static readonly FieldInfo PlayerData = AccessTools.Field(typeof(PlayerProfile), "m_playerData");
        private static bool vaultAckValidated;
        private static readonly MethodInfo NativeSave = AccessTools.Method(typeof(Game), "SavePlayerProfile", new[] { typeof(bool), typeof(bool) });
        private static void InstallVault(Type service)
        {
            vaultPending = AccessTools.Field(service, "_pendingRequest");
            var upload = vaultUpload = AccessTools.Method(service, "UploadSavedProfile", new[] { typeof(PlayerProfile) });
            var ack = AccessTools.Method(service, "ReceiveSaveAck", new[] { typeof(ZRpc), typeof(string) });
            var visible = new[] { "BeginFinalDisconnectSave", "ReceiveSaveRequest", "ResetClientState", "SaveManualClientProfile" }
                .Select(name => AccessTools.Method(service, name)).ToArray();
            if (vaultPending?.FieldType != typeof(string) || upload == null || ack == null || NativeSave == null || visible.Any(method => method == null))
                throw new MissingMemberException("Character Vault save boundaries changed");
            var harmony = new Harmony(VaultHarmonyId);
            HarmonyMethod Patch(string name) => new HarmonyMethod(AccessTools.Method(typeof(CharacterCheckpointPresentation), name));
            vaultAckValidated = false;
            harmony.Patch(ack, transpiler: Patch(nameof(VaultAckTranspiler)));
            if (!vaultAckValidated) throw new MissingMethodException("Character Vault queued-save call changed");
            harmony.Patch(upload, prefix: Patch(nameof(VaultUploadPrefix)), postfix: Patch(nameof(VaultUploadPostfix)), finalizer: Patch(nameof(VaultUploadFinalizer)));
            foreach (var method in visible)
                harmony.Patch(method, prefix: Patch(nameof(VaultVisiblePrefix)), finalizer: Patch(nameof(VaultVisibleFinalizer)));
        }
        private static void Warn(string message)
        { try { UnityEngine.Debug.LogWarning("Runic checkpoint presentation: " + message); } catch { } }
        private static string Pending(object service) => (string)vaultPending.GetValue(service);
        private static void VaultUploadPrefix(object __instance, out Tuple<string, bool> __state)
        {
            __state = null;
            try { __state = Tuple.Create(Pending(__instance), Active); }
            catch (Exception error) { ClearVaultOrigin(__instance); Warn("upload origin capture failed: " + error.GetType().Name); }
        }
        private static void VaultUploadPostfix(object __instance, Tuple<string, bool> __state)
        {
            try
            {
                if (__state == null) { ClearVaultOrigin(__instance); return; }
                RecordVaultUpload(__instance, __state.Item1, Pending(__instance), __state.Item2);
            }
            catch (Exception error) { ClearVaultOrigin(__instance); Warn("upload origin update failed: " + error.GetType().Name); }
        }
        private static Exception VaultUploadFinalizer(object __instance, Exception __exception)
        { if (__exception != null) ClearVaultOrigin(__instance); return __exception; }
        private static void VaultVisiblePrefix(object __instance, out IDisposable __state)
        {
            __state = null;
            try { ClearVaultOrigin(__instance); __state = EnterScope(false); }
            catch (Exception error) { Warn("visible save scope failed: " + error.GetType().Name); }
        }
        private static Exception VaultVisibleFinalizer(IDisposable __state, Exception __exception)
        { __state?.Dispose(); return __exception; }
        private static IEnumerable<CodeInstruction> VaultAckTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            if (code.Count(instruction => instruction.Calls(NativeSave)) != 1) return code;
            var result = new List<CodeInstruction>(code.Count + 1);
            foreach (var instruction in code)
            {
                if (instruction.Calls(NativeSave))
                {
                    // Only the final queued save inherits its origin. Earlier ACK callbacks
                    // can initiate a quit save and must never inherit this quiet scope.
                    // Keep all branch labels on the original instruction, so every path
                    // pushes the service argument before reaching the replacement call.
                    instruction.opcode = OpCodes.Ldarg_0;
                    instruction.operand = null;
                    result.Add(instruction);
                    result.Add(new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(CharacterCheckpointPresentation), nameof(SaveVaultFollowUp))));
                    continue;
                }
                result.Add(instruction);
            }
            vaultAckValidated = true; return result;
        }
        private static void SaveVaultFollowUp(Game game, bool setLogoutPoint, bool saveEvent, object service)
        {
            bool silent = false;
            try { silent = IsVaultFollowUpSilent(service, Pending(service)); }
            catch (Exception error) { ClearVaultOrigin(service); Warn("queued save origin lookup failed: " + error.GetType().Name); }
            using (EnterScope(silent))
            {
                PlayerProfile profile = game?.GetPlayerProfile();
                if (silent && vaultUpload != null && ReusableVerifiedProfile(profile))
                {
                    // The queued request came only from Runic checkpoints. Their already-written
                    // profile was verified, and remains the exact active persisted character block.
                    // Use Vault's own uploader/ID/ACK path without a third save or map recapture.
                    try { vaultUpload.Invoke(service, new object[] { profile }); }
                    catch (TargetInvocationException error)
                    {
                        // Never retry a potentially partially-started upload by initiating a save.
                        ExceptionDispatchInfo.Capture(error.InnerException ?? error).Throw();
                    }
                    return;
                }
                game.SavePlayerProfile(setLogoutPoint, saveEvent);
            }
        }
        private static bool ReusableVerifiedProfile(PlayerProfile profile)
        {
            try
            {
                byte[] expected = ((Func<PlayerProfile, byte[]>)Routes["verified-data"])(profile);
                if (expected == null || !OwnerOperationQueue.Same(expected, (byte[])PlayerData.GetValue(profile))) return false;
                var persisted = new PlayerProfile(profile.GetFilename(), profile.m_fileSource);
                return persisted.Load() && persisted.GetPlayerID() == profile.GetPlayerID() && persisted.GetName() == profile.GetName() &&
                    OwnerOperationQueue.Same(expected, (byte[])PlayerData.GetValue(persisted));
            }
            catch { return false; } // Missing, changed or unreadable proof takes the original save path.
        }
    }

    [HarmonyPatch(typeof(Hud), "ProfileSaveStarted")]
    internal static class SilentCharacterCheckpointHudPatch
    {
        private static bool Prefix() => !CharacterCheckpointPresentation.Active;
    }
}
