using System;
using System.Collections.Generic;
using UnityEngine;

namespace RunicProduction.Integration
{
    /// <summary>
    /// Only the existing chest owner can yield to a station's current owner. The receipt is
    /// published in the same native ZDO snapshot as the inventory, never as a client-supplied
    /// inventory or a replayable item operation. No requester forcibly claims a remote chest.
    /// </summary>
    internal static class ProductionChestHandoff
    {
        internal const string RequestRpc = "RunicProduction_ChestHandoff_v1";
        internal const string ReceiptKey = "runic.production.handoff-receipt";
        private const int MaximumContainers = 16384;
        private const float RetrySeconds = 3f;
        private sealed class State
        {
            internal ZNetView View;
            internal string Pending;
            internal long RequestedOwner;
            internal float NextRequest;
            internal float YieldAfter;
            internal float NextReceive;
            internal float ContentionUntil;
            internal float ContentionBackoff;
        }
        private static readonly Dictionary<Container, State> States = new Dictionary<Container, State>();
        private static readonly HashSet<State> Outstanding = new HashSet<State>();
        private static bool _active;
        private static int _requestFrame = -1;
        private static int _requestsThisFrame;

        internal static void Initialize() { Clear(); _active = true; }
        internal static void Shutdown() { _active = false; Clear(); }
        internal static void Clear()
        {
            States.Clear();
            Outstanding.Clear();
            _requestFrame = -1;
            _requestsThisFrame = 0;
        }

        internal static void Register(Container chest)
        {
            if (!_active || chest == null || States.ContainsKey(chest) ||
                States.Count >= MaximumContainers) return;
            ZNetView view = ValheimAccess.View(chest);
            if (view == null || !view.IsValid()) return;
            var state = new State { View = view };
            view.Register<ZDOID, long, string>(RequestRpc,
                (sender, station, principal, nonce) => Receive(chest, state, sender, station, principal, nonce));
            States.Add(chest, state);
            chest.gameObject.AddComponent<ProductionHandoffLifetime>().Chest = chest;
        }

        internal static void Remove(Container chest)
        {
            if (!ReferenceEquals(chest, null) && States.TryGetValue(chest, out State state))
            {
                Outstanding.Remove(state);
                States.Remove(chest);
            }
        }

        internal static bool TryAcquire(Component station, Container chest, long principal)
        {
            if (!_active || !ValheimAccess.IsNativeOwner(station) || chest == null ||
                !ProductionRuntime.AuthorizesChest(station, chest, principal)) return false;
            Register(chest);
            if (!States.TryGetValue(chest, out State state) || !state.View.IsValid() ||
                !ValheimAccess.ContainerWritable(chest)) return false;
            ZDO zdo = state.View.GetZDO();
            long owner = zdo.GetOwner();
            if (state.View.IsOwner())
            {
                // Ownership alone can arrive before inventory data. Wait for the snapshot
                // carrying our exact receipt before allowing any automated read/write.
                return ObserveReceipt(state, zdo);
            }
            if (owner == 0L || ZDOMan.instance == null) return false;
            float now = Time.realtimeSinceStartup;
            if (owner != state.RequestedOwner)
            {
                state.Pending = null;
                state.NextRequest = 0f;
            }
            if (now < state.NextRequest) return false;
            PruneOutstanding(now);
            if (Outstanding.Count >= 256 && !Outstanding.Contains(state)) return false;
            if (_requestFrame != Time.frameCount) { _requestFrame = Time.frameCount; _requestsThisFrame = 0; }
            if (_requestsThisFrame >= 32) return false;
            _requestsThisFrame++;
            state.Pending = state.Pending ?? Guid.NewGuid().ToString("N");
            state.RequestedOwner = owner;
            state.NextRequest = now + RetrySeconds;
            Outstanding.Add(state);
            ZDO stationZdo = ValheimAccess.Zdo(station);
            // Publish recent link edits before asking the owner to validate them. If packets
            // arrive out of order, the request is retried, not granted from request data.
            ZDOMan.instance.ForceSendZDO(owner, stationZdo.m_uid);
            state.View.InvokeRPC(owner, RequestRpc, stationZdo.m_uid, principal, state.Pending);
            return false;
        }

        private static void Receive(Container chest, State state, long sender,
            ZDOID stationId, long principal, string nonce)
        {
            if (!_active || !ProductionDiagnostics.RuntimeAvailable ||
                !(ProductionConfig.Enabled?.Value ?? false) || chest == null ||
                sender == 0L || sender == ZNet.GetUID() || stationId.IsNone() ||
                !ProductionEndpointIdentity.IsCanonicalToken(nonce) ||
                !state.View.IsValid() || !state.View.IsOwner() ||
                !ObserveReceipt(state, state.View.GetZDO()) ||
                Time.realtimeSinceStartup < state.YieldAfter) return;
            if (Time.realtimeSinceStartup < state.NextReceive) return;
            state.NextReceive = Time.realtimeSinceStartup + 0.25f;
            PruneOutstanding(Time.realtimeSinceStartup);
            // Break reciprocal/multi-peer acquisition cycles deterministically. The lower
            // network UID gets a short chance to assemble its inputs. The hold expires even
            // if an unrelated request never succeeds, so an unavailable chest cannot starve
            // other stations indefinitely. No inventory is reserved or removed here.
            if (sender > ZNet.GetUID() && Outstanding.Count > 0)
            {
                float now = Time.realtimeSinceStartup;
                if (now >= state.ContentionBackoff)
                {
                    state.ContentionUntil = now + 4f;
                    state.ContentionBackoff = now + 10f;
                }
                if (now < state.ContentionUntil) return;
            }
            try
            {
                Component station = ProductionRuntime.FindStation(stationId);
                ZDO stationZdo = ValheimAccess.Zdo(station);
                if (stationZdo == null || stationZdo.GetOwner() != sender ||
                    !ProductionRuntime.AuthorizesChest(station, chest, principal) ||
                    !ValheimAccess.TrySynchronizeLocallyOwnedContainer(chest, out _)) return;
                // All checks and the yield are synchronous on Unity's main thread. The old
                // owner stops mutating before the new owner can observe the receipt.
                if (!state.View.IsOwner() || stationZdo.GetOwner() != sender ||
                    !ValheimAccess.ContainerWritable(chest) || ZDOMan.instance == null) return;
                ZDO zdo = state.View.GetZDO();
                zdo.Set(ReceiptKey, nonce);
                zdo.SetOwner(sender);
                ZDOMan.instance.ForceSendZDO(sender, zdo.m_uid);
                state.YieldAfter = Time.realtimeSinceStartup + RetrySeconds;
                if (ProductionConfig.VerboseLogging?.Value ?? false)
                    ProductionDiagnostics.Info("Production chest handoff granted: " + zdo.m_uid + " -> " + sender);
            }
            catch (Exception exception)
            {
                ProductionDiagnostics.Warning("Production chest handoff deferred: " + exception.Message);
            }
        }

        private static bool ObserveReceipt(State state, ZDO zdo)
        {
            if (state.Pending == null) return true;
            if (!string.Equals(zdo.GetString(ReceiptKey, string.Empty), state.Pending,
                    StringComparison.Ordinal)) return false;
            state.Pending = null;
            Outstanding.Remove(state);
            state.YieldAfter = Time.realtimeSinceStartup +
                Mathf.Clamp(ProductionConfig.StockSchedulerIntervalSeconds?.Value ?? 2f, 0.5f, 10f) + 0.5f;
            if (ProductionConfig.VerboseLogging?.Value ?? false)
                ProductionDiagnostics.Info("Production chest handoff synchronized: " + zdo.m_uid);
            return true;
        }

        private static void PruneOutstanding(float now) => Outstanding.RemoveWhere(state =>
            state.Pending == null || now >= state.NextRequest ||
            !state.View.IsValid() || state.View.IsOwner());
    }

    // Unity unload is different from Container.OnDestroyed (which drops chest contents).
    internal sealed class ProductionHandoffLifetime : MonoBehaviour
    {
        internal Container Chest;
        private void OnDestroy() => ProductionChestHandoff.Remove(Chest);
    }
}
