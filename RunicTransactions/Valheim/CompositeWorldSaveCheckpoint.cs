using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Runic.Foundation.Transactions;
using RunicTransactions.Coordination;
using RunicTransactions.Contracts;
using UnityEngine;

namespace RunicTransactions.Valheim
{
    /// <summary>
    /// Conservative local-world checkpoint bridge. WorldSaveFinished is ignored:
    /// vanilla's save thread catches failures and that signal is false-green. A candidate is
    /// captured before PrepareSave, carried only on the exact save thread, and acknowledged only
    /// after the local database ReplaceOldFile call returns normally for the exact expected paths.
    /// Cloud/LegacyCloud saves never advance the WAL checkpoint.
    /// </summary>
    [HarmonyPatch]
    internal static class CompositeWorldSaveCheckpoint
    {
        private static readonly object Sync = new object();
        private const string MarkerPrefabName = "RunicTransactions_CompositeCheckpoint";
        private const string MarkerStorageKey =
            "runic.transactions.composite.world-checkpoint.v1";
        private const string MarkerLogicalMagic = "runic-checkpoint";
        private const string MarkerLogicalSchema = "v2";
        private static readonly int MarkerPrefabHash = MarkerPrefabName.GetStableHashCode();
        private static readonly MethodInfo CreateExactZdoMethod = AccessTools.Method(
            typeof(ZDOMan),
            "CreateNewZDO",
            new[] { typeof(ZDOID), typeof(Vector3), typeof(int) });
        private static readonly string ProcessInstanceId = Guid.NewGuid().ToString("N");
        private static readonly List<ZDO> MarkerScan = new List<ZDO>();
        private const int MaximumMarkerCreationFailures = 3;
        private static readonly TimeSpan MarkerCreationRetryDelay = TimeSpan.FromSeconds(1);
        private static DurableCompositeOperationCoordinatorFactory _factory;
        private static ZNet _attachedNetwork;
        private static Candidate _pending;
        private static ZDOMan _markerManager;
        private static ZDO _marker;
        private static int _markerScanIndex;
        private static bool _markerScanComplete;
        private static bool _recoveryAttempted;
        private static int _markerCreationFailures;
        private static DateTime _nextMarkerCreationAttemptUtc;
        private static bool _markerCreationExhausted;
        private static bool _markerResolutionFailed;

        [ThreadStatic]
        private static Candidate _saveThreadCandidate;

        internal static void Attach(DurableCompositeOperationCoordinatorFactory factory)
        {
            lock (Sync) _factory = factory;
            Update();
        }

        internal static void Update()
        {
            bool refresh;
            lock (Sync)
            {
                ZNet current = ZNet.instance;
                if (_pending != null &&
                    (DateTime.UtcNow - _pending.CreatedUtc) > TimeSpan.FromSeconds(30))
                {
                    _pending.ReleaseBarrier();
                    _pending = null;
                }
                if (ReferenceEquals(current, _attachedNetwork))
                {
                    refresh = current != null;
                }
                else
                {
                if (_attachedNetwork != null)
                {
                    try { ZNet.WorldSaveStarted -= OnWorldSaveStarted; }
                    catch { }
                }
                _attachedNetwork = current;
                ResetMarkerScanLocked();
                if (_attachedNetwork != null)
                {
                    try { ZNet.WorldSaveStarted += OnWorldSaveStarted; }
                    catch { _attachedNetwork = null; }
                }
                    refresh = _attachedNetwork != null;
                }
            }
            if (refresh) RefreshMarkerAndRecover();
        }

        internal static void Detach()
        {
            lock (Sync)
            {
                if (_attachedNetwork != null)
                {
                    try { ZNet.WorldSaveStarted -= OnWorldSaveStarted; }
                    catch { }
                }
                _pending?.ReleaseBarrier();
                _attachedNetwork = null;
                _pending = null;
                _factory = null;
                ResetMarkerScanLocked();
            }
            _saveThreadCandidate = null;
        }

        private static void OnWorldSaveStarted()
        {
            IDisposable mutationBarrier = null;
            bool handedOff = false;
            try
            {
                DurableCompositeOperationCoordinatorFactory factory;
                ZNet network;
                lock (Sync)
                {
                    factory = _factory;
                    network = _attachedNetwork;
                    _pending?.ReleaseBarrier();
                    _pending = null;
                }
                if (factory == null || network == null || !network.IsServer()) return;
                World world = ZNet.World;
                if (world == null) return;
                string actual = Path.GetFullPath(world.GetDBPath());
                string local = Path.GetFullPath(
                    world.GetDBPath(FileHelpers.FileSource.Local));
                if (!string.Equals(actual, local, PathComparison)) return;
                if (!TryGetMarker(out ZDO marker)) return;
                if (!factory.TryBeginWorldCheckpointCapture(
                        out long sequence,
                        out IDurableCompositeJournalStore journal,
                        out mutationBarrier) ||
                    sequence < 1 || journal == null) return;
                if (!journal.TryStageWorldCheckpoint(
                        sequence,
                        actual,
                        Guid.NewGuid().ToString("N"),
                        ProcessInstanceId,
                        marker.m_uid.UserID,
                        marker.m_uid.ID,
                        out DurableCompositeWorldCheckpointStage stage,
                        out _)) return;
                marker.Persistent = true;
                marker.SetOwner(ZNet.GetUID());
                string markerValue = BuildStagedMarkerValue(stage, journal.WorldScope);
                marker.Set(MarkerStorageKey, markerValue);
                if (!string.Equals(
                        marker.GetString(MarkerStorageKey, string.Empty),
                        markerValue,
                        StringComparison.Ordinal) ||
                    ZDOMan.instance == null || ZDOMan.instance.GetZDO(marker.m_uid) != marker)
                    return;
                var candidate = new Candidate(
                    journal,
                    stage.ThroughCommitSequence,
                    actual,
                    actual + ".new",
                    actual + ".old",
                    stage.SaveCycleId,
                    mutationBarrier);
                lock (Sync)
                    if (ReferenceEquals(network, _attachedNetwork))
                    {
                        _pending = candidate;
                        handedOff = true;
                    }
            }
            catch
            {
                lock (Sync) _pending = null;
            }
            finally
            {
                if (!handedOff)
                {
                    try { mutationBarrier?.Dispose(); }
                    catch { }
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(ZNet), "SaveWorldThread")]
        private static void SaveWorldThreadPrefix(ZNet __instance)
        {
            try
            {
                lock (Sync)
                {
                    _saveThreadCandidate = ReferenceEquals(__instance, _attachedNetwork)
                        ? _pending
                        : null;
                    _pending = null;
                }
                // SaveWorldThread starts only after ZDOMan.PrepareSave has cloned the world.
                _saveThreadCandidate?.ReleaseBarrier();
            }
            catch { _saveThreadCandidate = null; }
        }

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(ZNet), "SaveWorld", typeof(bool))]
        private static Exception SaveWorldFinalizer(Exception __exception)
        {
            if (__exception == null) return null;
            lock (Sync)
            {
                _pending?.ReleaseBarrier();
                _pending = null;
            }
            return __exception;
        }

        [HarmonyPostfix]
        [HarmonyPatch(
            typeof(FileHelpers),
            nameof(FileHelpers.ReplaceOldFile),
            new[]
            {
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(FileHelpers.FileSource)
            })]
        private static void ReplaceOldFilePostfix(
            string saveFile,
            string newFile,
            string oldFile,
            FileHelpers.FileSource fileSource)
        {
            Candidate candidate = _saveThreadCandidate;
            if (candidate == null || fileSource != FileHelpers.FileSource.Local) return;
            bool exactDatabaseReplace = false;
            try
            {
                if (!IsExactDatabaseReplace(
                        saveFile,
                        newFile,
                        oldFile,
                        candidate.DatabasePath,
                        candidate.NewDatabasePath,
                        candidate.OldDatabasePath)) return;
                exactDatabaseReplace = true;
                candidate.Journal.TryAdvanceVerifiedWorldCheckpoint(
                    candidate.CommitSequence,
                    candidate.DatabasePath,
                    candidate.SaveCycleId,
                    out _);
            }
            catch { }
            finally
            {
                if (exactDatabaseReplace) _saveThreadCandidate = null;
            }
        }

        internal static bool IsExactDatabaseReplace(
            string saveFile,
            string newFile,
            string oldFile,
            string expectedSaveFile,
            string expectedNewFile,
            string expectedOldFile)
        {
            try
            {
                return !string.IsNullOrEmpty(saveFile) &&
                    !string.IsNullOrEmpty(newFile) &&
                    !string.IsNullOrEmpty(oldFile) &&
                    !string.IsNullOrEmpty(expectedSaveFile) &&
                    !string.IsNullOrEmpty(expectedNewFile) &&
                    !string.IsNullOrEmpty(expectedOldFile) &&
                    string.Equals(
                        Path.GetFullPath(saveFile),
                        Path.GetFullPath(expectedSaveFile),
                        PathComparison) &&
                    string.Equals(
                        Path.GetFullPath(newFile),
                        Path.GetFullPath(expectedNewFile),
                        PathComparison) &&
                    string.Equals(
                        Path.GetFullPath(oldFile),
                        Path.GetFullPath(expectedOldFile),
                        PathComparison);
            }
            catch { return false; }
        }

        private static StringComparison PathComparison =>
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static void ResetMarkerScanLocked()
        {
            _markerManager = null;
            _marker = null;
            _markerScanIndex = 0;
            _markerScanComplete = false;
            _recoveryAttempted = false;
            _markerCreationFailures = 0;
            _nextMarkerCreationAttemptUtc = DateTime.MinValue;
            _markerCreationExhausted = false;
            _markerResolutionFailed = false;
            MarkerScan.Clear();
        }

        private static bool TryGetMarker(out ZDO marker)
        {
            lock (Sync)
            {
                marker = _marker;
                return marker != null && ZDOMan.instance != null &&
                       IsExactMarkerForReuse(
                           marker.IsValid(),
                           ZDOMan.instance.GetZDO(marker.m_uid) == marker,
                           marker.GetPrefab(),
                           marker.Persistent,
                           MarkerPrefabHash);
            }
        }

        private static void RefreshMarkerAndRecover()
        {
            DurableCompositeOperationCoordinatorFactory factory;
            ZNet network;
            lock (Sync)
            {
                factory = _factory;
                network = _attachedNetwork;
            }
            if (factory == null || network == null || !network.IsServer() ||
                ZDOMan.instance == null) return;
            try
            {
                ZDOMan manager = ZDOMan.instance;
                if (!factory.TryGetJournal(out IDurableCompositeJournalStore journal) ||
                    journal.ReadStagedWorldCheckpoint(
                        out DurableCompositeWorldCheckpointStage stage,
                        out _) != DurableCompositeHistoryReadState.Ready ||
                    journal.ReadCheckpointStreamIdentity(
                        out string worldEpoch,
                        out _) != DurableCompositeHistoryReadState.Ready) return;
                string worldScope = journal.WorldScope;
                lock (Sync)
                {
                    if (!ReferenceEquals(_markerManager, manager))
                    {
                        _markerManager = manager;
                        _marker = null;
                        _markerScanIndex = 0;
                        _markerScanComplete = false;
                        _recoveryAttempted = false;
                        _markerCreationFailures = 0;
                        _nextMarkerCreationAttemptUtc = DateTime.MinValue;
                        _markerCreationExhausted = false;
                        _markerResolutionFailed = false;
                        MarkerScan.Clear();
                    }
                    if (_marker != null &&
                        !IsExactMarkerForReuse(
                            _marker.IsValid(),
                            manager.GetZDO(_marker.m_uid) == _marker,
                            _marker.GetPrefab(),
                            _marker.Persistent,
                            MarkerPrefabHash))
                        _marker = null;
                }
                lock (Sync)
                {
                    if (_markerResolutionFailed) return;
                    if (_marker == null && !_markerScanComplete)
                        _markerScanComplete = manager.GetAllZDOsWithPrefabIterative(
                            MarkerPrefabName, MarkerScan, ref _markerScanIndex);
                }
                if (!_markerScanComplete) return;

                var candidates = new List<ZDO>(MarkerScan);
                lock (Sync)
                    if (_marker != null && !candidates.Contains(_marker))
                        candidates.Add(_marker);
                var valid = new List<ZDO>();
                int rejected = 0;
                foreach (ZDO candidate in candidates)
                {
                    if (candidate == null || !IsExactMarkerForReuse(
                            candidate.IsValid(),
                            manager.GetZDO(candidate.m_uid) == candidate,
                            candidate.GetPrefab(),
                            candidate.Persistent,
                            MarkerPrefabHash))
                        continue;
                    if (IsLogicalMarkerValue(
                            candidate.GetString(MarkerStorageKey, string.Empty),
                            worldScope,
                            worldEpoch,
                            stage?.CanonicalMarkerValue,
                            stage?.ThroughCommitSequence ?? 0,
                            stage?.SaveCycleId ?? string.Empty))
                        valid.Add(candidate);
                    else rejected++;
                }
                CheckpointMarkerResolution resolution = DecideMarkerResolution(
                    stage != null, valid.Count, rejected);
                if (resolution == CheckpointMarkerResolution.FailClosed)
                {
                    FailMarkerResolution(stage != null, valid.Count, rejected);
                    return;
                }
                if (resolution == CheckpointMarkerResolution.Create)
                {
                    if (!TryCreateMarker(manager, null, out ZDO createdMarker)) return;
                    string bootstrap = BuildBootstrapMarkerValue(worldScope, worldEpoch);
                    if (!TryPublishMarkerValue(manager, createdMarker, bootstrap) ||
                        !TryMakeCreatedMarkerPersistent(manager, createdMarker))
                    {
                        DiscardFailedMarker(manager, createdMarker);
                        RecordMarkerCreationFailure();
                        return;
                    }
                    createdMarker.SetOwner(ZNet.GetUID());
                    lock (Sync) _marker = createdMarker;
                    RecordMarkerCreationSuccess();
                    return;
                }

                ZDO exactMarker = valid[0];
                string expectedValue = stage == null
                    ? exactMarker.GetString(MarkerStorageKey, string.Empty)
                    : BuildStagedMarkerValue(stage, worldScope);
                if (!TryPublishMarkerValue(manager, exactMarker, expectedValue))
                {
                    FailMarkerResolution(stage != null, valid.Count, rejected);
                    return;
                }
                if (stage != null &&
                    (stage.MarkerUserId != exactMarker.m_uid.UserID ||
                     stage.MarkerObjectId != exactMarker.m_uid.ID))
                {
                    if (!journal.TryRebindStagedWorldCheckpointMarker(
                            stage.MarkerUserId,
                            stage.MarkerObjectId,
                            exactMarker.m_uid.UserID,
                            exactMarker.m_uid.ID,
                            out DurableCompositeWorldCheckpointStage rebound,
                            out _))
                    {
                        FailMarkerResolution(true, valid.Count, rejected);
                        return;
                    }
                    stage = rebound;
                }
                exactMarker.SetOwner(ZNet.GetUID());
                lock (Sync)
                {
                    _marker = exactMarker;
                    if (_recoveryAttempted || stage == null) return;
                    _recoveryAttempted = true;
                }
                if (!stage.HasFirstGenerationProof ||
                    string.Equals(
                        stage.OriginProcessId,
                        ProcessInstanceId,
                        StringComparison.Ordinal)) return;
                World world = ZNet.World;
                if (world == null || !string.Equals(
                        Path.GetFullPath(world.GetDBPath()),
                        stage.DatabasePath,
                        PathComparison)) return;
                journal.TryAdvanceVerifiedWorldCheckpoint(
                    stage.ThroughCommitSequence,
                    stage.DatabasePath,
                    stage.SaveCycleId,
                    out _);
            }
            catch { }
        }

        internal enum CheckpointMarkerResolution
        {
            BindExisting = 0,
            Create = 1,
            FailClosed = 2
        }

        internal static CheckpointMarkerResolution DecideMarkerResolution(
            bool hasStagedCheckpoint,
            int validMarkerCount,
            int rejectedMarkerCount)
        {
            if (validMarkerCount < 0 || rejectedMarkerCount < 0 ||
                rejectedMarkerCount != 0 || validMarkerCount > 1)
                return CheckpointMarkerResolution.FailClosed;
            if (validMarkerCount == 1) return CheckpointMarkerResolution.BindExisting;
            return hasStagedCheckpoint
                ? CheckpointMarkerResolution.FailClosed
                : CheckpointMarkerResolution.Create;
        }

        internal static bool IsLogicalMarkerValue(
            string value,
            string worldScope,
            string worldEpoch,
            string stagedLegacyValue,
            long stagedSequence,
            string stagedSaveCycle)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(worldScope) ||
                !Guid.TryParseExact(worldEpoch, "N", out Guid epoch) || epoch == Guid.Empty ||
                !string.Equals(epoch.ToString("N"), worldEpoch, StringComparison.Ordinal))
                return false;
            if (!string.IsNullOrEmpty(stagedLegacyValue) &&
                string.Equals(value, stagedLegacyValue, StringComparison.Ordinal))
                return stagedSequence > 0 && TryCanonicalGuid(stagedSaveCycle);
            string[] parts = value.Split(':');
            if (parts.Length == 5 &&
                string.Equals(parts[0], MarkerLogicalSchema, StringComparison.Ordinal) &&
                string.Equals(parts[1], MarkerLogicalMagic, StringComparison.Ordinal) &&
                string.Equals(parts[2], worldScope, StringComparison.Ordinal) &&
                string.Equals(parts[3], worldEpoch, StringComparison.Ordinal) &&
                string.Equals(parts[4], "bootstrap", StringComparison.Ordinal))
                return string.IsNullOrEmpty(stagedLegacyValue) && stagedSequence == 0 &&
                       string.IsNullOrEmpty(stagedSaveCycle);
            if (parts.Length != 6 ||
                !string.Equals(parts[0], MarkerLogicalSchema, StringComparison.Ordinal) ||
                !string.Equals(parts[1], MarkerLogicalMagic, StringComparison.Ordinal) ||
                !string.Equals(parts[2], worldScope, StringComparison.Ordinal) ||
                !string.Equals(parts[3], worldEpoch, StringComparison.Ordinal) ||
                !long.TryParse(
                    parts[4],
                    System.Globalization.NumberStyles.AllowHexSpecifier,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out long sequence) || sequence < 1 || !TryCanonicalGuid(parts[5]))
                return false;
            if (string.IsNullOrEmpty(stagedLegacyValue)) return true;
            return sequence == stagedSequence &&
                   string.Equals(parts[5], stagedSaveCycle, StringComparison.Ordinal);
        }

        private static string BuildBootstrapMarkerValue(string worldScope, string worldEpoch) =>
            MarkerLogicalSchema + ":" + MarkerLogicalMagic + ":" + worldScope + ":" +
            worldEpoch + ":bootstrap";

        private static string BuildStagedMarkerValue(
            DurableCompositeWorldCheckpointStage stage,
            string worldScope) =>
            MarkerLogicalSchema + ":" + MarkerLogicalMagic + ":" + worldScope + ":" +
            stage.WorldEpoch + ":" + stage.ThroughCommitSequence.ToString(
                "x16", System.Globalization.CultureInfo.InvariantCulture) + ":" +
            stage.SaveCycleId;

        private static bool TryCanonicalGuid(string value) =>
            Guid.TryParseExact(value, "N", out Guid parsed) && parsed != Guid.Empty &&
            string.Equals(parsed.ToString("N"), value, StringComparison.Ordinal);

        private static bool TryPublishMarkerValue(
            ZDOMan manager,
            ZDO marker,
            string value)
        {
            if (manager == null || marker == null || string.IsNullOrEmpty(value) ||
                marker.GetPrefab() != MarkerPrefabHash ||
                manager.GetZDO(marker.m_uid) != marker) return false;
            try
            {
                marker.Set(MarkerStorageKey, value);
                return string.Equals(
                    marker.GetString(MarkerStorageKey, string.Empty),
                    value,
                    StringComparison.Ordinal);
            }
            catch { return false; }
        }

        private static void FailMarkerResolution(
            bool staged,
            int validCount,
            int rejectedCount)
        {
            lock (Sync)
            {
                if (_markerResolutionFailed) return;
                _markerResolutionFailed = true;
            }
            try
            {
                Debug.LogError(
                    "RUNIC_TRANSACTIONS_CHECKPOINT_FAIL reason=logical-marker-conflict" +
                    " staged=" + staged + " valid_count=" + validCount +
                    " rejected_count=" + rejectedCount);
            }
            catch { }
        }

        private static bool TryCreateMarker(
            ZDOMan manager,
            ZDOID? exactId,
            out ZDO marker)
        {
            marker = null;
            if (manager == null || MarkerPrefabHash == 0 || !CanAttemptMarkerCreation())
                return false;
            ZDO candidate = null;
            try
            {
                candidate = exactId.HasValue
                    ? CreateExactZdoMethod?.Invoke(
                        manager,
                        new object[] { exactId.Value, Vector3.zero, MarkerPrefabHash }) as ZDO
                    : manager.CreateNewZDO(Vector3.zero, MarkerPrefabHash);
                if (candidate == null || candidate.Persistent) throw new InvalidOperationException();
                candidate.SetPrefab(MarkerPrefabHash);
                if (candidate.GetPrefab() != MarkerPrefabHash ||
                    manager.GetZDO(candidate.m_uid) != candidate)
                    throw new InvalidOperationException();
                marker = candidate;
                return true;
            }
            catch
            {
                DiscardFailedMarker(manager, candidate);
                RecordMarkerCreationFailure();
                return false;
            }
        }

        private static bool TryMakeCreatedMarkerPersistent(ZDOMan manager, ZDO marker)
        {
            if (manager == null || marker == null || marker.Persistent ||
                marker.GetPrefab() != MarkerPrefabHash ||
                manager.GetZDO(marker.m_uid) != marker) return false;
            try
            {
                marker.Persistent = true;
                if (marker.Persistent && marker.GetPrefab() == MarkerPrefabHash &&
                    manager.GetZDO(marker.m_uid) == marker) return true;
                marker.Persistent = false;
            }
            catch
            {
                try { marker.Persistent = false; }
                catch { }
            }
            return false;
        }

        private static void DiscardFailedMarker(ZDOMan manager, ZDO marker)
        {
            if (manager == null || marker == null) return;
            try { marker.Persistent = false; }
            catch { }
            try
            {
                if (manager.GetZDO(marker.m_uid) == marker) manager.DestroyZDO(marker);
            }
            catch { }
        }

        private static bool CanAttemptMarkerCreation()
        {
            lock (Sync)
                return !_markerCreationExhausted && MarkerCreationAttemptAllowed(
                    _markerCreationFailures,
                    _nextMarkerCreationAttemptUtc,
                    DateTime.UtcNow);
        }

        private static void RecordMarkerCreationFailure()
        {
            lock (Sync)
            {
                if (_markerCreationExhausted) return;
                _markerCreationFailures++;
                _markerCreationExhausted =
                    _markerCreationFailures >= MaximumMarkerCreationFailures;
                _nextMarkerCreationAttemptUtc = DateTime.UtcNow + MarkerCreationRetryDelay;
            }
        }

        private static void RecordMarkerCreationSuccess()
        {
            lock (Sync)
            {
                _markerCreationFailures = 0;
                _nextMarkerCreationAttemptUtc = DateTime.MinValue;
                _markerCreationExhausted = false;
            }
        }

        internal static bool MarkerCreationAttemptAllowed(
            int failureCount,
            DateTime nextAttemptUtc,
            DateTime nowUtc) =>
            failureCount >= 0 && failureCount < MaximumMarkerCreationFailures &&
            nowUtc >= nextAttemptUtc;

        internal static bool IsExactMarkerForReuse(
            bool valid,
            bool managerMatches,
            int prefabHash,
            bool persistent,
            int expectedPrefabHash) =>
            valid && managerMatches && persistent && expectedPrefabHash != 0 &&
            prefabHash == expectedPrefabHash;

        private sealed class Candidate
        {
            internal Candidate(
                IDurableCompositeJournalStore journal,
                long commitSequence,
                string databasePath,
                string newDatabasePath,
                string oldDatabasePath,
                string saveCycleId,
                IDisposable mutationBarrier)
            {
                Journal = journal;
                CommitSequence = commitSequence;
                DatabasePath = databasePath;
                NewDatabasePath = newDatabasePath;
                OldDatabasePath = oldDatabasePath;
                SaveCycleId = saveCycleId;
                _mutationBarrier = mutationBarrier ??
                    throw new ArgumentNullException(nameof(mutationBarrier));
                CreatedUtc = DateTime.UtcNow;
            }

            internal IDurableCompositeJournalStore Journal { get; }
            internal long CommitSequence { get; }
            internal string DatabasePath { get; }
            internal string NewDatabasePath { get; }
            internal string OldDatabasePath { get; }
            internal string SaveCycleId { get; }
            internal DateTime CreatedUtc { get; }

            internal void ReleaseBarrier()
            {
                IDisposable barrier = _mutationBarrier;
                _mutationBarrier = null;
                try { barrier?.Dispose(); }
                catch { }
            }

            private IDisposable _mutationBarrier;
        }
    }
}
