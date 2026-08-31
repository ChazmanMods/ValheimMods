using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Runic.Foundation.Core;
using Runic.Foundation.Persistence;
using Runic.Foundation.Transactions;
using RunicTransactions.Coordination;
using RunicTransactions.Contracts;

namespace RunicTransactions.Tests
{
    internal static class DurableCompositeTests
    {
        private static readonly string X = Hash("state-x");
        private static readonly string Y = Hash("state-y");
        private static readonly string Z = Hash("state-z");

        internal static void TokenIssueIsDurableExactAndExplicitlyCancelled()
        {
            using (var fixture = new Fixture())
            {
                string request = Hash("token-request");
                DurableCompositeTokenIssueResult issued =
                    fixture.First.IssueOperationToken(fixture.Actor, request);
                Equal(DurableCompositeTokenIssueCode.Issued, issued.Code, "Token was not issued.");
                Equal(147, issued.Token.CanonicalValue.Length, "Opaque token shape changed.");

                DurableCompositeTokenIssueResult replay =
                    fixture.First.IssueOperationToken(fixture.Actor, request);
                Equal(DurableCompositeTokenIssueCode.Replay, replay.Code, "Exact issue did not replay.");
                Equal(issued.Token.CanonicalValue, replay.Token.CanonicalValue,
                    "Exact issue replay changed the server token.");
                Equal(DurableCompositeTokenIssueCode.ActorBusy,
                    fixture.First.IssueOperationToken(fixture.Actor, Hash("other-request")).Code,
                    "A second live token escaped the per-actor bound.");

                DurableCompositeOutstandingQueryResult outstanding =
                    fixture.Factory.QueryOutstanding(fixture.Actor);
                Equal(DurableCompositeOutstandingQueryState.Ready, outstanding.State,
                    "Issued ledger was unavailable.");
                Equal(1, outstanding.IssuedOperations.Count, "Issued token was not outstanding.");

                Equal(DurableCompositeTokenCancellationCode.Conflict,
                    fixture.First.CancelIssuedOperationToken(
                        fixture.OtherActor, issued.Token, request).Code,
                    "A different bound account cancelled the token.");
                Equal(DurableCompositeTokenCancellationCode.Cancelled,
                    fixture.First.CancelIssuedOperationToken(
                        fixture.Actor, issued.Token, request).Code,
                    "Exact cancellation did not persist.");
                Equal(DurableCompositeTokenCancellationCode.Replay,
                    fixture.First.CancelIssuedOperationToken(
                        fixture.Actor, issued.Token, request).Code,
                    "Exact cancellation was not idempotent.");
                Equal(0, fixture.Factory.QueryOutstanding(fixture.Actor).IssuedOperations.Count,
                    "Cancelled token remained an admission obligation.");
                Equal(DurableCompositeTokenIssueCode.FailedClosed,
                    fixture.First.IssueOperationToken(fixture.Actor, request).Code,
                    "A cancelled durable token was silently reissued.");
            }
        }

        internal static void RequestLookupIsTwoHashReadOnlyAndRestartExact()
        {
            using (var fixture = new Fixture())
            {
                string durableKey = Hash("portal-idempotency-key");
                string request = Hash("portal-canonical-payload-a");
                string conflictingRequest = Hash("portal-canonical-payload-b");
                DurableCompositeTokenIssueResult issued =
                    fixture.First.IssueOperationToken(fixture.Actor, durableKey, request);
                Equal(DurableCompositeTokenIssueCode.Issued, issued.Code,
                    "Two-hash token was not issued.");

                string catalog = CatalogPath(fixture);
                byte[] beforeLookup = File.ReadAllBytes(catalog);
                DurableCompositeRequestLookupResult lookup = fixture.Factory.LookupRequest(
                    fixture.OwnerOne, fixture.Actor, durableKey, request);
                Equal(DurableCompositeRequestLookupState.Issued, lookup.State,
                    "Issued request lookup did not find its token.");
                Equal(issued.Token.CanonicalValue,
                    lookup.Operation.OperationToken.CanonicalValue,
                    "Issued request lookup changed the token.");
                Equal(durableKey, lookup.DurableRequestKeyHash,
                    "Lookup changed the durable request key hash.");
                Exact(beforeLookup, File.ReadAllBytes(catalog),
                    "Read-only issued lookup changed the catalog bytes.");

                Equal(DurableCompositeRequestLookupState.NotFound,
                    fixture.Factory.LookupRequest(
                        fixture.OwnerOne, fixture.OtherActor, durableKey, request).State,
                    "Another account discovered the request token.");
                Equal(DurableCompositeRequestLookupState.NotFound,
                    fixture.Factory.LookupRequest(
                        fixture.OwnerTwo, fixture.Actor, durableKey, request).State,
                    "Another owner module discovered the request token.");
                Equal(DurableCompositeRequestLookupState.Conflict,
                    fixture.Factory.LookupRequest(
                        fixture.OwnerOne, fixture.Actor, durableKey, conflictingRequest).State,
                    "Same durable key with different payload was not a conflict.");
                byte[] beforeConflict = File.ReadAllBytes(catalog);
                Equal(DurableCompositeTokenIssueCode.ReplayConflict,
                    fixture.First.IssueOperationToken(
                        fixture.Actor, durableKey, conflictingRequest).Code,
                    "Same durable key with different payload issued a second token.");
                Exact(beforeConflict, File.ReadAllBytes(catalog),
                    "Replay-conflict issue changed the catalog.");
                Equal(DurableCompositeRequestLookupState.Unavailable,
                    fixture.Factory.LookupRequest(
                        fixture.OwnerOne, fixture.Actor, durableKey.ToUpperInvariant(), request).State,
                    "Noncanonical request key was accepted.");

                EndpointId endpoint = fixture.Store.Add("request-lookup", X, "x.v1");
                var operation = fixture.Intent(
                    fixture.OwnerOne,
                    issued.Token,
                    request,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("lookup-update")));
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(operation).Code,
                    "Lookup root did not prepare.");
                lookup = fixture.Factory.LookupRequest(
                    fixture.OwnerOne, fixture.Actor, durableKey, request);
                Equal(DurableCompositeRequestLookupState.Journaled, lookup.State,
                    "Prepared request was not journaled.");
                Equal(DurableCompositeOperationPhase.Prepared,
                    lookup.Operation.Snapshot.Phase,
                    "Lookup returned the wrong retained phase.");
                Exact(operation.ExactIntent, lookup.Operation.ExactIntent,
                    "Lookup changed exact intent bytes.");

                fixture.RestartDurableSurface();
                lookup = fixture.Factory.LookupRequest(
                    fixture.OwnerOne, fixture.Actor, durableKey, request);
                Equal(DurableCompositeRequestLookupState.Journaled, lookup.State,
                    "Restart lost the durable request lookup.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Commit(lookup.Operation.Reference).Code,
                    "Restarted lookup reference could not drive the root.");
                byte[] beforeTerminalLookup = File.ReadAllBytes(catalog);
                lookup = fixture.Factory.LookupRequest(
                    fixture.OwnerOne, fixture.Actor, durableKey, request);
                Equal(DurableCompositeRequestLookupState.Journaled, lookup.State,
                    "Retained terminal root disappeared from exact lookup.");
                Equal(DurableCompositeOperationPhase.Committed,
                    lookup.Operation.Snapshot.Phase,
                    "Terminal lookup returned the wrong phase.");
                Exact(beforeTerminalLookup, File.ReadAllBytes(catalog),
                    "Terminal lookup changed the catalog bytes.");

                byte[] corrupt = File.ReadAllBytes(catalog);
                corrupt[corrupt.Length / 2] ^= 0x5a;
                File.WriteAllBytes(catalog, corrupt);
                Equal(DurableCompositeRequestLookupState.Corrupt,
                    fixture.Factory.LookupRequest(
                        fixture.OwnerOne, fixture.Actor, durableKey, request).State,
                    "Tampered catalog did not make request lookup fail corrupt.");
            }
        }

        internal static void LegacySingleHashCatalogMigratesWithoutChangingLookup()
        {
            using (var fixture = new Fixture())
            {
                string request = Hash("legacy-request-key-and-payload");
                DurableCompositeTokenIssueResult issued =
                    fixture.First.IssueOperationToken(fixture.Actor, request);
                True(issued.Success, "Legacy-compatible token issue failed.");
                fixture.Journal.RewritePrimaryAsLegacySchema4ForMigrationTest();
                string catalog = CatalogPath(fixture);
                Equal(4, ReadCatalogSchema(catalog), "Schema-4 fixture was not written.");
                byte[] legacyBytes = File.ReadAllBytes(catalog);

                fixture.RestartDurableSurface();
                DurableCompositeRequestLookupResult lookup = fixture.Factory.LookupRequest(
                    fixture.OwnerOne, fixture.Actor, request, request);
                Equal(DurableCompositeRequestLookupState.Issued, lookup.State,
                    "Schema-4 single-hash lease did not migrate in memory.");
                Equal(issued.Token.CanonicalValue,
                    lookup.Operation.OperationToken.CanonicalValue,
                    "Schema-4 lookup changed the authenticated token.");
                Exact(legacyBytes, File.ReadAllBytes(catalog),
                    "Reading schema 4 silently rewrote the primary.");
                Equal(DurableCompositeTokenCancellationCode.Cancelled,
                    fixture.First.CancelIssuedOperationToken(
                        fixture.Actor, issued.Token, request).Code,
                    "Schema-4 lease could not make a normal durable transition.");
                Equal(5, ReadCatalogSchema(catalog),
                    "The next durable mutation did not migrate schema 4 to schema 5.");
            }
        }

        internal static void RootPrecedesClaimsAndPreparedPrecedesMutation()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("a", X, "x.v1");
                string request = Hash("root-before-claim");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                fixture.Store.Journal = fixture.Journal;
                var intent = fixture.Intent(
                    fixture.OwnerOne,
                    token,
                    request,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("x-to-y")));

                DurableCompositeOperationResult prepared = fixture.First.Prepare(intent);
                Equal(DurableCompositeResultCode.Prepared, prepared.Code, "Prepare failed.");
                True(fixture.Store.ClaimSawDurableRoot,
                    "Endpoint claim occurred before the fsynced/read-back root existed.");
                Equal(0, fixture.Store.ApplyCount, "Prepare mutated consumer state.");
                Equal(Y, intent.Endpoints[0].AfterFingerprint, "Intent changed after prepare.");

                DurableCompositeOperationResult committed = fixture.First.Commit(intent.Reference);
                Equal(DurableCompositeResultCode.Committed, committed.Code, "Commit failed.");
                True(fixture.Store.ApplySawCommittingRoot,
                    "Endpoint mutation occurred before the durable Committing publication.");
                Equal(1, fixture.Store.ApplyCount, "Endpoint mutated more than once.");
                fixture.Store.Assert(endpoint, Y, "y.v1", token.WorldEpoch, 1);
            }
        }


        internal static void EndpointBoundsAreSortedUniqueAndMetadataNeutral()
        {
            using (var fixture = new Fixture())
            {
                var endpoints = new List<DurableCompositeEndpointIntent>();
                for (int index = 31; index >= 0; index--)
                {
                    EndpointId id = fixture.Store.Add(index.ToString("d2"), X, "x.v1");
                    endpoints.Add(new DurableCompositeEndpointIntent(
                        id, X, Y, "x.v1", "y.v1", Bytes("mutation-" + index)));
                }
                string request = Hash("thirty-two");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                DurableCompositeOperationIntent intent = fixture.Intent(
                    fixture.OwnerOne, token, request, endpoints.ToArray());
                for (int index = 1; index < intent.Endpoints.Count; index++)
                    True(intent.Endpoints[index - 1].StableEndpointId.CompareTo(
                        intent.Endpoints[index].StableEndpointId) < 0,
                        "Endpoints were not canonically sorted.");
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "32-endpoint prepare failed.");
                True(fixture.Store.RawMetadataRevision >= 32,
                    "Claim metadata did not exercise raw revision churn.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Commit(intent.Reference).Code,
                    "Claim metadata incorrectly invalidated semantic revisions.");

                var duplicate = new[] { endpoints[0], endpoints[0] };
                Throws<ArgumentException>(() => fixture.Intent(
                    fixture.OwnerOne,
                    new DurableCompositeOperationToken(
                        token.WorldEpoch, token.AdmissionSequence + 1, Guid.NewGuid(), Hash("auth")),
                    Hash("duplicate"),
                    duplicate));
                var tooMany = Enumerable.Range(0, 33).Select(index =>
                    new DurableCompositeEndpointIntent(
                        new EndpointId("test.endpoint:overflow-" + index),
                        X, Y, "x.v1", "y.v1", Bytes("m"))).ToArray();
                Throws<ArgumentOutOfRangeException>(() => fixture.Intent(
                    fixture.OwnerOne,
                    new DurableCompositeOperationToken(
                        token.WorldEpoch, token.AdmissionSequence + 2, Guid.NewGuid(), Hash("auth2")),
                    Hash("too-many"),
                    tooMany));
            }
        }

        internal static void UnrelatedDomainMutationFailsBeforeCommit()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("race", X, "x.v1");
                string request = Hash("domain-race");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                DurableCompositeOperationIntent intent = fixture.Intent(
                    fixture.OwnerOne,
                    token,
                    request,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("x-y")));
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "Prepare failed.");
                fixture.Store.SetDomainState(endpoint, Z, "z.v1", string.Empty, 0);
                Equal(DurableCompositeResultCode.EvidenceConflict,
                    fixture.First.Commit(intent.Reference).Code,
                    "An unrelated domain mutation passed the commit recheck.");
                Equal(0, fixture.Store.ApplyCount,
                    "The coordinator mutated after detecting unrelated evidence.");
            }
        }

        internal static void CrossModuleHistoryIsOrderedAndAbaSafe()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("shared", X, "x.v1");
                string requestA = Hash("module-a");
                DurableCompositeOperationToken tokenA = fixture.Issue(fixture.First, requestA);
                DurableCompositeOperationIntent intentA = fixture.Intent(
                    fixture.OwnerOne,
                    tokenA,
                    requestA,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("a")));
                Equal(DurableCompositeResultCode.Prepared, fixture.First.Prepare(intentA).Code,
                    "A prepare failed.");
                Equal(DurableCompositeResultCode.Committed, fixture.First.Commit(intentA.Reference).Code,
                    "A commit failed.");

                string requestB = Hash("module-b");
                DurableCompositeOperationToken tokenB = fixture.Issue(fixture.Second, requestB);
                DurableCompositeOperationIntent intentB = fixture.Intent(
                    fixture.OwnerTwo,
                    tokenB,
                    requestB,
                    new DurableCompositeEndpointIntent(
                        endpoint, Y, X, "y.v1", "x.v2", Bytes("b")));
                Equal(DurableCompositeResultCode.Prepared, fixture.Second.Prepare(intentB).Code,
                    "B prepare failed.");
                Equal(DurableCompositeResultCode.Committed, fixture.Second.Commit(intentB.Reference).Code,
                    "B commit failed.");
                fixture.Store.Assert(endpoint, X, "x.v2", tokenA.WorldEpoch, 2);

                Equal(DurableCompositeJournalReadState.Present,
                    fixture.Journal.Read(
                        intentB.OperationIdText, out byte[] bBytes, out _),
                    "B root was not durable.");
                True(DurableCompositeCodec.TryDecode(bBytes, out CompositeRootRecord bRoot, out _),
                    "B root was corrupt.");
                Equal(1, bRoot.Predecessors.Count, "B did not record A as its predecessor.");
                Equal(1L, bRoot.Predecessors[0].CommitSequence,
                    "B predecessor sequence was not exact.");

                // Crash with a world database from before both operations. Fingerprint-only
                // reconciliation would confuse this X with B's X after state; the marker cannot.
                fixture.Store.SetDomainState(endpoint, X, "x.v1", string.Empty, 0);
                fixture.Store.DropClaims();
                fixture.Store.FailCommitSequenceOnce = 2;
                int beforeReplay = fixture.Store.ApplyCount;
                Equal(DurableCompositeResultCode.EvidenceConflict,
                    fixture.First.Recover(intentA.Reference).Code,
                    "Injected crash cut after replaying A did not fail closed.");
                Equal(beforeReplay + 1, fixture.Store.ApplyCount,
                    "The first replay cut did not stop exactly after A.");
                fixture.Store.Assert(endpoint, Y, "y.v1", tokenA.WorldEpoch, 1);

                // Restart/retry from a world generation that contains A but not B. The exact
                // predecessor marker skips A and resumes B; no timestamp or byte-only ABA guess.
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Recover(intentA.Reference).Code,
                    "Ordered recovery from world-after-A/before-B failed.");
                Equal(beforeReplay + 2, fixture.Store.ApplyCount,
                    "Recovery did not replay A then B exactly once.");
                fixture.Store.Assert(endpoint, X, "x.v2", tokenA.WorldEpoch, 2);

                // A saved post-B world and a later unrelated change retain B's marker. Immutable
                // terminal receipts remain valid without pretending the old after bytes are live.
                fixture.Store.SetDomainState(endpoint, Z, "z.v3", tokenA.WorldEpoch, 2);
                beforeReplay = fixture.Store.ApplyCount;
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Recover(intentA.Reference).Code,
                    "A later unrelated state invalidated immutable receipt replay.");
                Equal(beforeReplay, fixture.Store.ApplyCount,
                    "Receipt replay overwrote a later unrelated endpoint state.");
            }
        }

        internal static void OutstandingIsAccountScopedAndCarriesExactManifest()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("outstanding", X, "x.v1");
                string request = Hash("outstanding-request");
                byte[] manifest = Bytes("origin-session=one;apply-session=two;items=exact");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                var requirement = new DurableCompositeReconciliationRequirement(
                    fixture.OwnerOne.Descriptor.ModuleId,
                    fixture.OwnerOne.Descriptor.ProtocolVersion.Major,
                    RunicCapabilityIds.InventoryDurableOperations);
                var intent = new DurableCompositeOperationIntent(
                    fixture.OwnerOne,
                    token,
                    fixture.Actor,
                    Fixture.Domain,
                    request,
                    manifest,
                    new[]
                    {
                        new DurableCompositeEndpointIntent(
                            endpoint, X, Y, "x.v1", "y.v1", Bytes("outstanding"))
                    },
                    requirement);
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "Prepare failed.");
                DurableCompositeOperationResult committed = fixture.First.Commit(intent.Reference);
                Equal(DurableCompositeResultCode.Committed, committed.Code, "Commit failed.");

                DurableCompositeOutstandingQueryResult own =
                    fixture.Factory.QueryOutstanding(fixture.Actor);
                Equal(1, own.Operations.Count, "Bound account lost its reconciliation hold.");
                Equal(token.CanonicalValue,
                    own.Operations[0].OperationToken.CanonicalValue,
                    "Outstanding summary lost the authenticated server token.");
                Equal(0, fixture.Factory.QueryOutstanding(fixture.OtherActor).Operations.Count,
                    "One account's hold leaked to another account.");
                Equal(DurableCompositeOutstandingReadState.Conflict,
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.OwnerTwo,
                        fixture.Actor,
                        own.Operations[0].OperationToken.CanonicalValue).State,
                    "Outstanding token crossed its owner-module boundary.");
                Equal(DurableCompositeOutstandingReadState.Conflict,
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.OwnerOne,
                        fixture.OtherActor,
                        own.Operations[0].OperationToken.CanonicalValue).State,
                    "Outstanding token crossed its actor-account boundary.");
                string tamperedToken = token.CanonicalValue.Substring(
                    0, token.CanonicalValue.Length - 1) +
                    (token.CanonicalValue[token.CanonicalValue.Length - 1] == '0' ? "1" : "0");
                Equal(DurableCompositeOutstandingReadState.Conflict,
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.OwnerOne, fixture.Actor, tamperedToken).State,
                    "A tampered outstanding authenticator selected a retained root.");

                fixture.RestartDurableSurface();
                own = fixture.Factory.QueryOutstanding(fixture.Actor);
                Equal(1, own.Operations.Count,
                    "Restart lost the committed reconciliation summary.");
                Equal(token.CanonicalValue,
                    own.Operations[0].OperationToken.CanonicalValue,
                    "The retained-root codec changed the token across restart.");
                DurableCompositeOutstandingReadResult detail =
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.OwnerOne,
                        fixture.Actor,
                        own.Operations[0].OperationToken.CanonicalValue);
                Equal(DurableCompositeOutstandingReadState.Journaled, detail.State,
                    "Fresh-client outstanding detail was unavailable.");
                True(detail.Operation.ExactIntent.SequenceEqual(manifest),
                    "Fresh-client exact reconciliation manifest changed.");
                Equal(token.CanonicalValue,
                    detail.Operation.OperationToken.CanonicalValue,
                    "Outstanding detail lost the server token.");
                Equal(committed.Snapshot.Receipt.ReceiptHash,
                    detail.Operation.Snapshot.Receipt.ReceiptHash,
                    "Outstanding detail lost the durable receipt.");

                string acknowledgement = Hash("next-session-proof");
                Equal(DurableCompositeResultCode.Acknowledged,
                    fixture.First.AcknowledgeReconciliation(
                        intent.Reference, acknowledgement).Code,
                    "Exact owner acknowledgement failed.");
                Equal(0, fixture.Factory.QueryOutstanding(fixture.Actor).Operations.Count,
                    "Acknowledged operation remained an admission hold.");
                Equal(DurableCompositeResultCode.Replay,
                    fixture.First.AcknowledgeReconciliation(
                        intent.Reference, acknowledgement).Code,
                    "Exact acknowledgement did not replay.");
                Equal(DurableCompositeResultCode.ReplayConflict,
                    fixture.First.AcknowledgeReconciliation(
                        intent.Reference, Hash("different-proof")).Code,
                    "Conflicting acknowledgement was accepted.");
            }
        }

        internal static void OwnerStartupEnumerationRecoversWithoutActorReconnect()
        {
            using (var fixture = new Fixture())
            {
                string issuedRequest = Hash("owner-startup-issued");
                DurableCompositeOperationToken issued = fixture.Issue(
                    fixture.First, issuedRequest);
                DurableCompositeOwnerOutstandingQueryResult ownerView =
                    fixture.Factory.QueryOwnerOutstanding(fixture.OwnerOne);
                Equal(DurableCompositeOutstandingQueryState.Ready, ownerView.State,
                    "Owner startup enumeration was unavailable.");
                Equal(1, ownerView.IssuedOperations.Count,
                    "Explicit issued-only token was not visible to its owner.");
                Equal(issued.CanonicalValue,
                    ownerView.IssuedOperations[0].OperationToken.CanonicalValue,
                    "Owner issued token changed during enumeration.");
                Equal(0, fixture.Factory.QueryOwnerOutstanding(
                    fixture.OwnerTwo).IssuedOperations.Count,
                    "Issued token leaked to a different owner module.");
                Equal(DurableCompositeTokenCancellationCode.Cancelled,
                    fixture.First.CancelIssuedOperationToken(
                        fixture.Actor, issued, issuedRequest).Code,
                    "Issued-only cleanup was not explicit.");

                EndpointId endpoint = fixture.Store.Add("owner-startup", X, "x.v1");
                string request = Hash("owner-startup-committing");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                DurableCompositeOperationIntent intent = fixture.Intent(
                    fixture.OwnerOne,
                    token,
                    request,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("startup-recover")));
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "Prepare failed.");
                fixture.Store.FailCommitSequenceOnce = 1;
                Equal(DurableCompositeResultCode.EvidenceConflict,
                    fixture.First.Commit(intent.Reference).Code,
                    "Injected committing cut did not stop before terminalization.");

                ownerView = fixture.Factory.QueryOwnerOutstanding(fixture.OwnerOne);
                Equal(DurableCompositeOutstandingQueryState.Ready, ownerView.State,
                    "Restart owner view failed closed unexpectedly.");
                Equal(1, ownerView.JournaledOperations.Count,
                    "Committing root was not enumerated before actor reconnect.");
                DurableCompositeJournaledOutstandingOperation pending =
                    ownerView.JournaledOperations[0];
                Equal(DurableCompositeOperationPhase.Committing, pending.Phase,
                    "Owner view lost the exact durable phase.");
                DurableCompositeOutstandingReadResult detail =
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.OwnerOne,
                        pending.ActorIdentity,
                        pending.OperationToken.CanonicalValue);
                Equal(DurableCompositeOutstandingReadState.Journaled, detail.State,
                    "Owner could not reopen its exact startup root.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Recover(detail.Operation.Reference).Code,
                    "Owner could not recover before an actor reconnect.");
                fixture.Store.Assert(endpoint, Y, "y.v1", token.WorldEpoch, 1);
            }
        }

        internal static void TokenDispositionSurvivesAcknowledgedCheckpointCompaction()
        {
            using (var fixture = new Fixture())
            {
                string cancelledRequest = Hash("disposition-cancelled");
                DurableCompositeOperationToken cancelled = fixture.Issue(
                    fixture.Second, cancelledRequest);
                Equal(DurableCompositeTokenDispositionState.Active,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerOne, cancelled.CanonicalValue).State,
                    "A live issued token was not held active.");
                Equal(DurableCompositeTokenCancellationCode.Cancelled,
                    fixture.Second.CancelIssuedOperationToken(
                        fixture.Actor, cancelled, cancelledRequest).Code,
                    "Token cancellation failed.");
                Equal(DurableCompositeTokenDispositionState.Cancelled,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerOne, cancelled.CanonicalValue).State,
                    "An explicit cancellation was not authenticated as inert.");

                EndpointId endpoint = fixture.Store.Add("disposition", X, "x.v1");
                string request = Hash("disposition-reconciliation");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                var requirement = new DurableCompositeReconciliationRequirement(
                    fixture.OwnerOne.Descriptor.ModuleId,
                    fixture.OwnerOne.Descriptor.ProtocolVersion.Major,
                    RunicCapabilityIds.InventoryDurableOperations);
                var intent = new DurableCompositeOperationIntent(
                    fixture.OwnerOne,
                    token,
                    fixture.Actor,
                    Fixture.Domain,
                    request,
                    Bytes("custody-disposition-v1"),
                    new[]
                    {
                        new DurableCompositeEndpointIntent(
                            endpoint, X, Y, "x.v1", "y.v1", Bytes("disposition"))
                    },
                    requirement);
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "Prepare failed.");
                Equal(DurableCompositeTokenDispositionState.Active,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue).State,
                    "Prepared token was not held active.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Commit(intent.Reference).Code, "Commit failed.");
                Equal(DurableCompositeTokenDispositionState.CommittedReconciliationHold,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue).State,
                    "Committed custody token did not retain its reconciliation hold.");

                string tampered = token.CanonicalValue.Substring(
                    0, token.CanonicalValue.Length - 1) +
                    (token.CanonicalValue[token.CanonicalValue.Length - 1] == '0' ? "1" : "0");
                Equal(DurableCompositeTokenDispositionState.Invalid,
                    fixture.Factory.QueryTokenDisposition(fixture.OwnerTwo, tampered).State,
                    "A tampered marker token was authenticated.");
                Equal(DurableCompositeResultCode.Acknowledged,
                    fixture.First.AcknowledgeReconciliation(
                        intent.Reference, Hash("disposition-proof")).Code,
                    "Reconciliation acknowledgement failed.");
                DurableCompositeTokenDispositionResult acknowledged =
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue);
                Equal(DurableCompositeTokenDispositionState.Acknowledged,
                    acknowledged.State,
                    "Acknowledged token was not durably inert.");
                True(acknowledged.IsAuthenticatedInert &&
                     !acknowledged.RequiresCustodyHold,
                    "Acknowledged token still required a custody hold.");

                string database = Path.Combine(fixture.Root, "disposition-world.db");
                File.WriteAllBytes(database, Bytes("disposition-generation-one"));
                string cycle = Guid.NewGuid().ToString("N");
                True(fixture.Journal.TryStageWorldCheckpoint(
                        1, database, cycle, Guid.NewGuid().ToString("N"), 77, 88,
                        out _, out _),
                    "Disposition checkpoint did not stage.");
                Equal(DurableCompositeCheckpointState.Unchanged,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        1, database, cycle, out _),
                    "First checkpoint generation compacted the disposition.");
                File.WriteAllBytes(database + ".old", Bytes("disposition-generation-one"));
                File.WriteAllBytes(database, Bytes("disposition-generation-two"));
                Equal(DurableCompositeCheckpointState.Advanced,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        1, database, cycle, out _),
                    "Second checkpoint generation did not compact the acknowledged root.");
                Equal(DurableCompositeTokenDispositionState.Retired,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue).State,
                    "Compaction forgot an authenticated inert token disposition.");

                fixture.RestartDurableSurface();
                DurableCompositeTokenDispositionResult restarted =
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue);
                Equal(DurableCompositeTokenDispositionState.Retired, restarted.State,
                    "Restart lost the compacted disposition frontier.");
                True(restarted.IsAuthenticatedInert,
                    "A rollback-restored marker would remain permanently locked.");
                fixture.OwnerTwo.Dispose();
                Equal(DurableCompositeTokenDispositionState.Unavailable,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue).State,
                    "An inactive requester could query the server authority catalog.");
            }
        }

        internal static void AbortedReconciliationHoldSurvivesRestartAndCheckpoint()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("abort-hold", X, "x.v1");
                string request = Hash("abort-hold-request");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                var requirement = new DurableCompositeReconciliationRequirement(
                    fixture.OwnerOne.Descriptor.ModuleId,
                    fixture.OwnerOne.Descriptor.ProtocolVersion.Major,
                    RunicCapabilityIds.InventoryDurableOperations);
                byte[] manifest = Bytes("prepared-client-debit-and-grave-custody-v1");
                var intent = new DurableCompositeOperationIntent(
                    fixture.OwnerOne,
                    token,
                    fixture.Actor,
                    Fixture.Domain,
                    request,
                    manifest,
                    new[]
                    {
                        new DurableCompositeEndpointIntent(
                            endpoint, X, Y, "x.v1", "y.v1", Bytes("abort-hold"))
                    },
                    requirement);
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "Prepare failed.");
                DurableCompositeOperationResult aborted = fixture.First.Abort(intent.Reference);
                Equal(DurableCompositeResultCode.Aborted, aborted.Code, "Abort failed.");
                Equal(DurableCompositeOperationPhase.Aborted, aborted.Snapshot.Phase,
                    "Abort did not persist a terminal receipt.");

                DurableCompositeOutstandingQueryResult outstanding =
                    fixture.Factory.QueryOutstanding(fixture.Actor);
                Equal(1, outstanding.Operations.Count,
                    "Prepared client evidence became inert at server abort.");
                Equal(DurableCompositeOperationPhase.Aborted,
                    outstanding.Operations[0].TerminalPhase,
                    "Outstanding abort hold lost its terminal disposition.");
                Equal(0L, outstanding.Operations[0].CommitSequence,
                    "An aborted root invented a commit sequence.");
                Equal(token.CanonicalValue,
                    outstanding.Operations[0].OperationToken.CanonicalValue,
                    "Abort hold lost its authenticated token.");
                Equal(0, fixture.Factory.QueryOutstanding(
                    fixture.OtherActor).Operations.Count,
                    "Abort hold leaked to a different account.");
                Equal(0, fixture.Factory.QueryOwnerOutstanding(
                    fixture.OwnerTwo).Operations.Count,
                    "Abort hold leaked to a different owner module.");
                DurableCompositeTokenDispositionResult held =
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue);
                Equal(DurableCompositeTokenDispositionState.AbortedReconciliationHold,
                    held.State, "Aborted prepared custody was classified inert.");
                True(!held.IsAuthenticatedInert && held.RequiresCustodyHold,
                    "Aborted prepared custody did not retain its lock.");

                string tampered = token.CanonicalValue.Substring(
                    0, token.CanonicalValue.Length - 1) +
                    (token.CanonicalValue[token.CanonicalValue.Length - 1] == '0' ? "1" : "0");
                Equal(DurableCompositeTokenDispositionState.Invalid,
                    fixture.Factory.QueryTokenDisposition(fixture.OwnerTwo, tampered).State,
                    "A tampered abort token was authenticated.");

                // Establish a real commit frontier and prove the unacknowledged abort root is not
                // discarded by the two-generation world checkpoint.
                EndpointId anchorEndpoint = fixture.Store.Add("abort-hold-anchor", X, "x.v1");
                string anchorRequest = Hash("abort-hold-anchor-one");
                DurableCompositeOperationToken anchorToken = fixture.Issue(
                    fixture.First, anchorRequest);
                DurableCompositeOperationIntent anchor = fixture.Intent(
                    fixture.OwnerOne,
                    anchorToken,
                    anchorRequest,
                    new DurableCompositeEndpointIntent(
                        anchorEndpoint, X, Y, "x.v1", "y.v1", Bytes("anchor-one")));
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(anchor).Code, "Anchor prepare failed.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Commit(anchor.Reference).Code, "Anchor commit failed.");
                string database = Path.Combine(fixture.Root, "abort-hold-world.db");
                File.WriteAllBytes(database, Bytes("abort-hold-generation-one"));
                string firstCycle = Guid.NewGuid().ToString("N");
                True(fixture.Journal.TryStageWorldCheckpoint(
                        1, database, firstCycle, Guid.NewGuid().ToString("N"), 77, 88,
                        out _, out _),
                    "Abort-hold checkpoint did not stage.");
                Equal(DurableCompositeCheckpointState.Unchanged,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        1, database, firstCycle, out _),
                    "First generation advanced unexpectedly.");
                File.WriteAllBytes(database + ".old", Bytes("abort-hold-generation-one"));
                File.WriteAllBytes(database, Bytes("abort-hold-generation-two"));
                Equal(DurableCompositeCheckpointState.Advanced,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        1, database, firstCycle, out _),
                    "Second generation did not advance.");
                Equal(DurableCompositeJournalReadState.Present,
                    fixture.Journal.Read(token.OperationIdText, out _, out _),
                    "Checkpoint erased an unacknowledged abort hold.");

                fixture.RestartDurableSurface();
                outstanding = fixture.Factory.QueryOutstanding(fixture.Actor);
                Equal(1, outstanding.Operations.Count,
                    "Restart lost the abort reconciliation hold.");
                DurableCompositeOutstandingReadResult detail =
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.OwnerOne,
                        fixture.Actor,
                        outstanding.Operations[0].OperationToken.CanonicalValue);
                Equal(DurableCompositeOutstandingReadState.Journaled, detail.State,
                    "Fresh session could not read the retained abort root.");
                True(detail.Operation.ExactIntent.SequenceEqual(manifest),
                    "Abort hold lost its compensation manifest.");
                Equal(aborted.Snapshot.Receipt.ReceiptHash,
                    detail.Operation.Snapshot.Receipt.ReceiptHash,
                    "Abort receipt changed across restart.");
                Equal(DurableCompositeResultCode.FailedClosed,
                    fixture.Second.AcknowledgeReconciliation(
                        detail.Operation.Reference, Hash("wrong-owner-proof")).Code,
                    "A different owner acknowledged the abort hold.");
                string proof = Hash("exact-compensation-and-custody-proof");
                Equal(DurableCompositeResultCode.Acknowledged,
                    fixture.First.AcknowledgeReconciliation(
                        detail.Operation.Reference, proof).Code,
                    "Exact abort compensation acknowledgement failed.");
                Equal(0, fixture.Factory.QueryOutstanding(fixture.Actor).Operations.Count,
                    "Acknowledged abort remained an admission hold.");
                Equal(DurableCompositeTokenDispositionState.Aborted,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue).State,
                    "Acknowledged abort was not authenticated inert.");
                Equal(DurableCompositeResultCode.Replay,
                    fixture.First.AcknowledgeReconciliation(
                        detail.Operation.Reference, proof).Code,
                    "Exact abort acknowledgement did not replay.");
                Equal(DurableCompositeResultCode.ReplayConflict,
                    fixture.First.AcknowledgeReconciliation(
                        detail.Operation.Reference, Hash("conflicting-proof")).Code,
                    "Conflicting abort acknowledgement was accepted.");

                // A later verified generation may compact the acknowledged root while preserving
                // the authenticated retirement frontier for rollback-restored grave markers.
                EndpointId secondEndpoint = fixture.Store.Add("abort-hold-anchor-two", X, "x.v1");
                string secondRequest = Hash("abort-hold-anchor-two");
                DurableCompositeOperationToken secondToken = fixture.Issue(
                    fixture.First, secondRequest);
                DurableCompositeOperationIntent second = fixture.Intent(
                    fixture.OwnerOne,
                    secondToken,
                    secondRequest,
                    new DurableCompositeEndpointIntent(
                        secondEndpoint, X, Y, "x.v1", "y.v1", Bytes("anchor-two")));
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(second).Code, "Second anchor prepare failed.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Commit(second.Reference).Code, "Second anchor commit failed.");
                File.WriteAllBytes(database, Bytes("abort-hold-generation-three"));
                string secondCycle = Guid.NewGuid().ToString("N");
                True(fixture.Journal.TryStageWorldCheckpoint(
                        2, database, secondCycle, Guid.NewGuid().ToString("N"), 77, 88,
                        out _, out _),
                    "Second abort-hold checkpoint did not stage.");
                Equal(DurableCompositeCheckpointState.Unchanged,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        2, database, secondCycle, out _),
                    "Second checkpoint first generation advanced unexpectedly.");
                File.WriteAllBytes(database + ".old", Bytes("abort-hold-generation-three"));
                File.WriteAllBytes(database, Bytes("abort-hold-generation-four"));
                Equal(DurableCompositeCheckpointState.Advanced,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        2, database, secondCycle, out _),
                    "Second checkpoint did not compact acknowledged abort.");
                Equal(DurableCompositeJournalReadState.Absent,
                    fixture.Journal.Read(token.OperationIdText, out _, out _),
                    "Acknowledged abort root did not compact.");
                Equal(DurableCompositeTokenDispositionState.Retired,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue).State,
                    "Compaction forgot the authenticated abort disposition.");
                fixture.RestartDurableSurface();
                Equal(DurableCompositeTokenDispositionState.Retired,
                    fixture.Factory.QueryTokenDisposition(
                        fixture.OwnerTwo, token.CanonicalValue).State,
                    "Restart lost the abort retirement frontier.");
            }
        }

        internal static void CheckpointNeedsTwoGenerationsAndRetainsIssuedTokens()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("checkpoint", X, "x.v1");
                string request = Hash("checkpoint-commit");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                DurableCompositeOperationIntent intent = fixture.Intent(
                    fixture.OwnerOne,
                    token,
                    request,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("checkpoint")));
                Equal(DurableCompositeResultCode.Prepared, fixture.First.Prepare(intent).Code,
                    "Prepare failed.");
                Equal(DurableCompositeResultCode.Committed, fixture.First.Commit(intent.Reference).Code,
                    "Commit failed.");

                string unconsumedRequest = Hash("client-tag-not-yet-journaled");
                DurableCompositeOperationToken unconsumed =
                    fixture.Issue(fixture.Second, unconsumedRequest);
                string database = Path.Combine(fixture.Root, "world.db");
                File.WriteAllBytes(database, Bytes("generation-one"));
                string cycle = Guid.NewGuid().ToString("N");
                True(fixture.Journal.TryStageWorldCheckpoint(
                        1, database, cycle, Guid.NewGuid().ToString("N"), 77, 88,
                        out _, out _),
                    "Checkpoint did not stage.");
                Equal(DurableCompositeCheckpointState.Unchanged,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        1, database, cycle, out _),
                    "The first database replace compacted the WAL.");
                Equal(DurableCompositeJournalReadState.Present,
                    fixture.Journal.Read(intent.OperationIdText, out _, out _),
                    "First generation removed replay history.");

                File.WriteAllBytes(database + ".old", Bytes("untrusted-backup"));
                File.WriteAllBytes(database, Bytes("generation-two"));
                Equal(DurableCompositeCheckpointState.Rejected,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        1, database, cycle, out _),
                    "A mismatched fallback generation advanced the checkpoint.");
                Equal(DurableCompositeJournalReadState.Present,
                    fixture.Journal.Read(intent.OperationIdText, out _, out _),
                    "A mismatched fallback removed required replay history.");

                File.WriteAllBytes(database + ".old", Bytes("generation-one"));
                File.WriteAllBytes(database, Bytes("generation-two"));
                Equal(DurableCompositeCheckpointState.Advanced,
                    fixture.Journal.TryAdvanceVerifiedWorldCheckpoint(
                        1, database, cycle, out _),
                    "The exact second generation did not advance the checkpoint.");
                Equal(DurableCompositeJournalReadState.Absent,
                    fixture.Journal.Read(intent.OperationIdText, out _, out _),
                    "Checkpointed terminal root did not compact.");
                DurableCompositeOutstandingReadResult issued =
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.OwnerTwo,
                        fixture.Actor,
                        unconsumed.CanonicalValue);
                Equal(DurableCompositeOutstandingReadState.Issued, issued.State,
                    "Checkpoint guessed that an unconsumed client tag was abandoned.");
            }
        }

        internal static void StagedCheckpointRecoveryRebindsRemappedMarkerIdentity()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("checkpoint-rebind", X, "x.v1");
                string request = Hash("checkpoint-rebind");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                DurableCompositeOperationIntent intent = fixture.Intent(
                    fixture.OwnerOne,
                    token,
                    request,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("checkpoint-rebind")));
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "Prepare failed.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Commit(intent.Reference).Code, "Commit failed.");
                string database = Path.Combine(fixture.Root, "checkpoint-rebind.db");
                File.WriteAllBytes(database, Bytes("checkpoint-rebind-generation"));
                string cycle = Guid.NewGuid().ToString("N");
                True(fixture.Journal.TryStageWorldCheckpoint(
                        1, database, cycle, Guid.NewGuid().ToString("N"), 77, 88,
                        out DurableCompositeWorldCheckpointStage original, out _),
                    "Checkpoint did not stage with its pre-restart marker hint.");
                True(fixture.Journal.TryRebindStagedWorldCheckpointMarker(
                        77, 88, 1, 99,
                        out DurableCompositeWorldCheckpointStage rebound, out _),
                    "The remapped current marker did not rebind.");
                Equal(1L, rebound.MarkerUserId, "Rebound marker user ID was not current.");
                Equal((uint)99, rebound.MarkerObjectId,
                    "Rebound marker object ID was not current.");
                Equal(original.CanonicalMarkerValue, rebound.CanonicalMarkerValue,
                    "Numeric rebinding changed logical checkpoint identity.");
                fixture.RestartDurableSurface();
                Equal(DurableCompositeHistoryReadState.Ready,
                    fixture.Journal.ReadStagedWorldCheckpoint(
                        out DurableCompositeWorldCheckpointStage restarted, out _),
                    "Restart could not read the rebound checkpoint.");
                Equal(1L, restarted.MarkerUserId,
                    "Restart restored the stale marker user ID.");
                Equal((uint)99, restarted.MarkerObjectId,
                    "Restart restored the stale marker object ID.");
                True(!fixture.Journal.TryRebindStagedWorldCheckpointMarker(
                        77, 88, 2, 100, out _, out _),
                    "A stale pre-restart marker hint rebound the checkpoint twice.");
            }
        }

        internal static void CheckpointCaptureIgnoresLaterWorldStateAndHoldsMutationBarrier()
        {
            using (var fixture = new Fixture())
            {
                EndpointId endpoint = fixture.Store.Add("checkpoint-liveness", X, "x.v1");
                string request = Hash("checkpoint-liveness");
                DurableCompositeOperationToken token = fixture.Issue(fixture.First, request);
                DurableCompositeOperationIntent intent = fixture.Intent(
                    fixture.OwnerOne,
                    token,
                    request,
                    new DurableCompositeEndpointIntent(
                        endpoint, X, Y, "x.v1", "y.v1", Bytes("checkpoint-liveness")));
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.First.Prepare(intent).Code, "Prepare failed.");
                Equal(DurableCompositeResultCode.Committed,
                    fixture.First.Commit(intent.Reference).Code, "Commit failed.");

                // The snapshot follows the terminal WAL record. A later vanilla edit, owner
                // migration, or destruction must not require the historical after bytes to remain
                // current forever.
                fixture.Store.SetDomainState(endpoint, Z, "later.vanilla", string.Empty, 0);
                fixture.Store.SetAuthority(endpoint, false, true);
                True(fixture.Factory.TryBeginWorldCheckpointCapture(
                        out long through,
                        out IDurableCompositeJournalStore journal,
                        out IDisposable barrier),
                    "Later world state permanently blocked checkpoint eligibility.");
                Equal(1L, through, "Checkpoint did not cover the terminal sequence.");
                True(journal != null && barrier != null,
                    "Checkpoint capture did not return its exact journal/barrier lease.");

                bool competitorEntered = true;
                Exception threadFailure = null;
                var competitor = new Thread(() =>
                {
                    try
                    {
                        competitorEntered = RunicMutationGate.TryEnter(
                            "test.checkpoint-competitor", out IDisposable lease);
                        lease?.Dispose();
                    }
                    catch (Exception exception) { threadFailure = exception; }
                });
                competitor.Start();
                competitor.Join();
                if (threadFailure != null) throw threadFailure;
                True(!competitorEntered,
                    "A mutation entered between checkpoint eligibility and snapshot capture.");
                barrier.Dispose();
                True(RunicMutationGate.TryEnter(
                        "test.checkpoint-after-snapshot", out IDisposable after),
                    "Checkpoint barrier did not release after snapshot capture.");
                after.Dispose();
            }
        }

        internal static void ForensicStagedFileAndReparsePathFailClosed()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "runic-composite-forensic-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var store = new FileDurableCompositeJournalStore(root, "world.test")) { }
                string world = Path.Combine(root, "world.test");
                File.WriteAllBytes(
                    Path.Combine(world, "runic-composite.catalog.new"),
                    Bytes("torn"));
                Throws<InvalidDataException>(() =>
                {
                    using (var ignored = new FileDurableCompositeJournalStore(root, "world.test")) { }
                });
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch { }
            }

            string linkRoot = Path.Combine(
                Path.GetTempPath(), "runic-composite-link-" + Guid.NewGuid().ToString("N"));
            string outside = Path.Combine(
                Path.GetTempPath(), "runic-composite-outside-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(linkRoot);
                Directory.CreateDirectory(outside);
                try
                {
                    Directory.CreateSymbolicLink(Path.Combine(linkRoot, "world.test"), outside);
                }
                catch (UnauthorizedAccessException) { return; }
                catch (PlatformNotSupportedException) { return; }
                Throws<InvalidDataException>(() =>
                {
                    using (var ignored = new FileDurableCompositeJournalStore(
                        linkRoot, "world.test")) { }
                });
            }
            finally
            {
                try { Directory.Delete(linkRoot, true); }
                catch { }
                try { Directory.Delete(outside, true); }
                catch { }
            }
        }

        internal static void WorldObjectCreateCommitsAndPreparedAbortCompensates()
        {
            using (var fixture = new WorldFixture())
            {
                string request = Hash("world-create-commit");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableWorldObjectCreateIntent create = fixture.Create(token, 0, "sapling");
                DurableCompositeOperationIntent operation = fixture.Operation(
                    token, request, create);
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Create-from-absence did not prepare before native creation.");
                fixture.Provider.StageDesired(create);
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Commit(operation.Reference).Code,
                    "Exact prepared native creation did not commit.");
                fixture.Provider.AssertDesired(create, token.WorldEpoch, 1);

                string abortRequest = Hash("world-create-abort");
                DurableCompositeOperationToken abortToken = fixture.Issue(abortRequest);
                DurableWorldObjectCreateIntent abortCreate =
                    fixture.Create(abortToken, 0, "turnip");
                DurableCompositeOperationIntent abortOperation = fixture.Operation(
                    abortToken, abortRequest, abortCreate);
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(abortOperation).Code,
                    "Compensated create did not prepare.");
                fixture.Provider.StageDesired(abortCreate);
                Equal(DurableCompositeResultCode.Aborted,
                    fixture.Coordinator.Abort(abortOperation.Reference).Code,
                    "Prepared native creation was not durably compensated.");
                fixture.Provider.AssertBefore(abortCreate);
                Equal(1, fixture.Provider.CompensationCount,
                    "Prepared create compensation did not run exactly once.");
                Equal(DurableCompositeResultCode.Replay,
                    fixture.Coordinator.Abort(abortOperation.Reference).Code,
                    "Durable abort was not idempotent.");
                Equal(1, fixture.Provider.CompensationCount,
                    "Abort replay compensated twice.");
            }
        }

        internal static void MixedRemovalAndCreationReplaysAsOneOrderedRoot()
        {
            using (var fixture = new WorldFixture())
            {
                string request = Hash("mixed-replant");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableWorldObjectRemoveIntent remove = fixture.Remove(
                    token,
                    0,
                    Guid.NewGuid().ToString("N"),
                    "old-crop",
                    Hash("old-crop-present"),
                    "crop.present.v1");
                DurableWorldObjectCreateIntent create = fixture.Create(
                    token, 1, "new-crop");
                fixture.Provider.SeedBefore(remove);
                DurableCompositeOperationIntent operation = fixture.Operation(
                    token, request, remove, create);
                Equal(2, operation.Endpoints.Count, "Mixed operation lost an endpoint.");
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Mixed replant did not prepare.");
                fixture.Provider.StageDesired(remove);
                fixture.Provider.StageDesired(create);
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Commit(operation.Reference).Code,
                    "Mixed removal/create did not commit as one root.");
                fixture.Provider.AssertDesired(remove, token.WorldEpoch, 1);
                fixture.Provider.AssertDesired(create, token.WorldEpoch, 1);

                // A world snapshot from before the root must deterministically redo both desired
                // states; a half-operation is never accepted as the committed receipt.
                fixture.Provider.ResetToBefore(remove);
                fixture.Provider.ResetToBefore(create);
                int before = fixture.Provider.ApplyCount;
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Recover(operation.Reference).Code,
                    "Mixed root recovery failed.");
                Equal(before + 2, fixture.Provider.ApplyCount,
                    "Mixed recovery did not apply both endpoints exactly once.");
                fixture.Provider.AssertDesired(remove, token.WorldEpoch, 1);
                fixture.Provider.AssertDesired(create, token.WorldEpoch, 1);
            }
        }

        internal static void WorldObjectDuplicateTokenQuarantinesBeforePrepare()
        {
            using (var fixture = new WorldFixture())
            {
                string request = Hash("duplicate-world-object");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableWorldObjectCreateIntent create = fixture.Create(token, 0, "carrot");
                fixture.Provider.MarkDuplicate(create.ObjectId);
                DurableCompositeOperationIntent operation = fixture.Operation(
                    token, request, create);
                Equal(DurableCompositeResultCode.FailedClosed,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Duplicate Runic creation tokens were not quarantined.");
                Equal(0, fixture.Provider.ApplyCount,
                    "Duplicate evidence caused a world mutation.");
            }
        }

        internal static void WorldObjectUpdateCommitsRecoversAndCompensates()
        {
            using (var fixture = new WorldFixture())
            {
                string request = Hash("world-update-commit");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableWorldObjectUpdateIntent update = fixture.Update(
                    token,
                    0,
                    Guid.NewGuid().ToString("N"),
                    "repair-piece",
                    Hash("health:0.25"),
                    "piece.health.25.v1",
                    Hash("health:1.00"),
                    "piece.health.100.v1");
                fixture.Provider.SeedBefore(update);
                DurableCompositeOperationIntent operation = fixture.Operation(
                    token, request, update);

                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Present-to-Present repair did not prepare.");
                fixture.Provider.StageDesired(update);
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Commit(operation.Reference).Code,
                    "Exact prepared Present-to-Present repair did not commit.");
                fixture.Provider.AssertDesired(update, token.WorldEpoch, 1);

                fixture.Provider.ResetToBefore(update);
                int beforeRecovery = fixture.Provider.ApplyCount;
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Recover(operation.Reference).Code,
                    "Committed Present-to-Present repair did not recover from before state.");
                Equal(beforeRecovery + 1, fixture.Provider.ApplyCount,
                    "Repair recovery did not reapply exactly once.");
                fixture.Provider.AssertDesired(update, token.WorldEpoch, 1);

                string abortRequest = Hash("world-update-abort");
                DurableCompositeOperationToken abortToken = fixture.Issue(abortRequest);
                DurableWorldObjectUpdateIntent abortUpdate = fixture.Update(
                    abortToken,
                    0,
                    Guid.NewGuid().ToString("N"),
                    "repair-abort-piece",
                    Hash("health:0.50"),
                    "piece.health.50.v1",
                    Hash("health:1.00:abort"),
                    "piece.health.100.v2");
                fixture.Provider.SeedBefore(abortUpdate);
                DurableCompositeOperationIntent abortOperation = fixture.Operation(
                    abortToken, abortRequest, abortUpdate);
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(abortOperation).Code,
                    "Compensated Present-to-Present repair did not prepare.");
                fixture.Provider.StageDesired(abortUpdate);
                int beforeCompensation = fixture.Provider.CompensationCount;
                Equal(DurableCompositeResultCode.Aborted,
                    fixture.Coordinator.Abort(abortOperation.Reference).Code,
                    "Prepared Present-to-Present repair was not compensated.");
                Equal(beforeCompensation + 1, fixture.Provider.CompensationCount,
                    "Repair compensation did not execute exactly once.");
                fixture.Provider.AssertBefore(abortUpdate);
            }
        }

        internal static void WorldObjectDomainAllowsExactlySixtyFourEndpoints()
        {
            using (var fixture = new WorldFixture())
            {
                string request = Hash("world-sixty-four");
                DurableCompositeOperationToken token = fixture.Issue(request);
                var updates = new List<IDurableWorldObjectEndpointIntent>();
                for (int ordinal = 0; ordinal < DurableCompositeLimits.MaximumEndpoints; ordinal++)
                {
                    DurableWorldObjectUpdateIntent update = fixture.EnrollUpdate(
                        token,
                        ordinal,
                        "repair-piece-" + ordinal,
                        Hash("before:" + ordinal),
                        "piece.before." + ordinal,
                        Hash("after:" + ordinal),
                        "piece.after." + ordinal);
                    fixture.Provider.SeedUnenrolled(update);
                    updates.Add(update);
                }

                DurableCompositeOperationIntent operation = fixture.Service.CreateOperationIntent(
                    fixture.Owner,
                    token,
                    fixture.Actor,
                    request,
                    Bytes("world-operation:" + request),
                    updates);
                Equal(DurableCompositeLimits.MaximumEndpoints, operation.Endpoints.Count,
                    "The world-object domain did not retain all 64 endpoints.");
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(operation).Code,
                    "A 64-endpoint world-object operation did not prepare.");
                Equal(DurableCompositeLimits.MaximumEndpoints,
                    fixture.Provider.EnrollmentPublishCount,
                    "A 64-endpoint vanilla-object operation did not enroll every stable identity.");
                foreach (IDurableWorldObjectEndpointIntent update in updates)
                    fixture.Provider.StageDesired(update);
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Commit(operation.Reference).Code,
                    "A 64-endpoint world-object operation did not commit as one root.");
                Equal(DurableCompositeLimits.MaximumEndpoints, fixture.Provider.ApplyCount,
                    "The 64-endpoint root did not publish every endpoint exactly once.");

                Throws<ArgumentOutOfRangeException>(() => fixture.Update(
                    token,
                    DurableCompositeLimits.MaximumEndpoints,
                    Guid.NewGuid().ToString("N"),
                    "overflow-piece",
                    Hash("overflow-before"),
                    "piece.before.overflow",
                    Hash("overflow-after"),
                    "piece.after.overflow"));
            }
        }

        internal static void DeferredOwnerUpdateAndRemovalResumeAfterDisconnect()
        {
            using (var fixture = new WorldFixture())
            {
                fixture.Provider.DeferredMode = true;
                fixture.Provider.OwnerSessionCurrent = true;
                string request = Hash("deferred-owner-mixed");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableWorldObjectUpdateIntent update = fixture.Update(
                    token,
                    0,
                    Guid.NewGuid().ToString("N"),
                    "remote-repair-piece",
                    Hash("remote-health-before"),
                    "piece.health.before",
                    Hash("remote-health-after"),
                    "piece.health.after");
                DurableWorldObjectRemoveIntent remove = fixture.Remove(
                    token,
                    1,
                    Guid.NewGuid().ToString("N"),
                    "remote-undo-piece",
                    Hash("remote-piece-present"),
                    "piece.present.before-remove");
                fixture.Provider.SeedBefore(update);
                fixture.Provider.SeedBefore(remove);
                DurableCompositeOperationIntent operation = fixture.Operation(
                    token, request, update, remove);

                DurableCompositeOperationResult prepared =
                    fixture.Coordinator.Prepare(operation);
                Equal(DurableCompositeResultCode.Prepared,
                    prepared.Code,
                    "Delegated owner operation did not prepare under exact current authority. " +
                    prepared.ReasonCode + " / " + fixture.Provider.LastAuthorityReason);

                DurableCompositeOperationResult result =
                    fixture.Coordinator.Commit(operation.Reference);
                Equal(DurableCompositeResultCode.NotReady, result.Code,
                    "The first owner command was not reported as pending.");
                Equal(DurableCompositeOperationPhase.Committing, result.Snapshot.Phase,
                    "Owner dispatch occurred before the root became durably Committing.");
                Equal(1L, result.Snapshot.CommitSequence,
                    "Deferred owner command did not receive the durable commit sequence.");

                // Each server observation resumes the same root and dispatches the next stable
                // endpoint. No second WAL or client-selected sequence participates.
                fixture.Provider.StageAllPendingDesired();
                result = fixture.Coordinator.Commit(operation.Reference);
                Equal(DurableCompositeResultCode.NotReady, result.Code,
                    "The second stable endpoint was not dispatched under the same root.");
                fixture.Provider.StageAllPendingDesired();

                // Both native effects and exact markers are now replicated. The execution peer
                // may disconnect before the final callback without stranding the terminal write.
                fixture.Provider.OwnerSessionCurrent = false;
                result = fixture.Coordinator.Commit(operation.Reference);
                Equal(DurableCompositeResultCode.Committed, result.Code,
                    "Exact owner observations did not terminalize the mixed deferred root.");
                Equal(2, fixture.Provider.DeferredDispatchCount,
                    "Each delegated endpoint was not dispatched exactly once.");

                // Terminalization depends on replicated exact state+marker, not on the execution
                // peer remaining connected after its native action.
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.ReadStatus(operation.Reference).Code,
                    "Owner disconnect invalidated an immutable committed receipt.");
                fixture.Provider.AssertDesired(update, token.WorldEpoch, 1);
                fixture.Provider.AssertDesired(remove, token.WorldEpoch, 1);
            }
        }

        internal static void ExistingObjectEnrollmentIsJournalFirstAndMutationLast()
        {
            using (var fixture = new WorldFixture())
            {
                string request = Hash("existing-enrollment-order");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableWorldObjectUpdateIntent update = fixture.EnrollUpdate(
                    token,
                    0,
                    "vanilla-repair-piece",
                    Hash("vanilla-repair-before"),
                    "piece.health.25.enroll",
                    Hash("vanilla-repair-after"),
                    "piece.health.100.enroll");
                DurableWorldObjectRemoveIntent remove = fixture.EnrollRemove(
                    token,
                    1,
                    "vanilla-undo-piece",
                    Hash("vanilla-remove-before"),
                    "piece.present.enroll");
                True(update.RequiresIdentityEnrollment && remove.RequiresIdentityEnrollment,
                    "Existing-object factories did not mark deterministic enrollment.");
                Equal(
                    DurableWorldObjectCreationId.Derive(
                        token, fixture.Descriptor.MutationKind, 0).ValueText,
                    update.ObjectId.ValueText,
                    "Update enrollment identity was not token/kind/ordinal derived.");
                True(DurableWorldObjectCodec.TryDecode(
                        update.ToCompositeEndpointIntent().ExactMutation,
                        out IDurableWorldObjectEndpointIntent decoded) &&
                    decoded.RequiresIdentityEnrollment &&
                    decoded.ObjectId.ValueText == update.ObjectId.ValueText,
                    "Enrollment identity did not survive the canonical codec.");
                fixture.Provider.SeedUnenrolled(update);
                fixture.Provider.SeedUnenrolled(remove);
                DurableCompositeOperationIntent operation = fixture.Operation(
                    token, request, update, remove);

                int observedClaimingRoots = 0;
                fixture.Provider.EnrollmentStarting = _ =>
                {
                    Equal(DurableCompositeJournalReadState.Present,
                        fixture.Journal.Read(
                            token.OperationIdText, out byte[] bytes, out string reason),
                        "Enrollment ran before a durable root existed: " + reason);
                    True(DurableCompositeCodec.TryDecode(
                            bytes, out CompositeRootRecord root, out reason),
                        "Enrollment could not reread the canonical root: " + reason);
                    Equal(DurableCompositeOperationPhase.Claiming, root.Phase,
                        "Enrollment ran after Prepared publication.");
                    Equal(0, fixture.Provider.ApplyCount,
                        "Gameplay mutation ran during identity enrollment.");
                    observedClaimingRoots++;
                };

                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Exact ordinary-object enrollment did not prepare.");
                Equal(2, observedClaimingRoots,
                    "Each exact existing endpoint was not enrolled under Claiming.");
                Equal(2, fixture.Provider.EnrollmentPublishCount,
                    "Stable identity publication did not occur exactly once per endpoint.");
                Equal(0, fixture.Provider.ApplyCount,
                    "Prepare mutated gameplay state.");
                fixture.Provider.AssertBefore(update);
                fixture.Provider.AssertBefore(remove);

                fixture.Provider.StageDesired(update);
                fixture.Provider.StageDesired(remove);
                DurableCompositeOperationResult committed =
                    fixture.Coordinator.Commit(operation.Reference);
                Equal(DurableCompositeResultCode.Committed,
                    committed.Code,
                    "Enrolled mixed update/removal did not commit: " +
                    committed.ReasonCode);
                Equal(2, fixture.Provider.ApplyCount,
                    "The exact desired states were not published after Prepared.");
                int publications = fixture.Provider.EnrollmentPublishCount;
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Recover(operation.Reference).Code,
                    "Committed enrolled root did not replay exactly.");
                Equal(publications, fixture.Provider.EnrollmentPublishCount,
                    "Replay rewrote stable object identity.");
            }
        }

        internal static void ExistingObjectEnrollmentResumesAcrossEveryClaimingCrashCut()
        {
            using (var fixture = new WorldFixture())
            {
                fixture.Provider.DeferredMode = true;
                fixture.Provider.OwnerSessionCurrent = true;
                fixture.Provider.EnrollmentPendingMode = true;
                string request = Hash("existing-enrollment-before-tag-crash");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableWorldObjectUpdateIntent update = fixture.EnrollUpdate(
                    token,
                    0,
                    "remote-vanilla-piece",
                    Hash("remote-vanilla-before"),
                    "piece.remote.before",
                    Hash("remote-vanilla-after"),
                    "piece.remote.after");
                fixture.Provider.SeedUnenrolled(update);
                DurableCompositeOperationIntent operation = fixture.Operation(
                    token, request, update);

                DurableCompositeOperationResult result = fixture.Coordinator.Prepare(operation);
                Equal(DurableCompositeResultCode.NotReady, result.Code,
                    "Owner-dispatched enrollment did not remain Claiming while pending.");
                Equal(DurableCompositeOperationPhase.Claiming, result.Snapshot.Phase,
                    "Pending enrollment published Prepared.");
                Equal(0, fixture.Provider.EnrollmentPublishCount,
                    "Pending enrollment published a tag early.");
                Equal(0, fixture.Provider.ApplyCount,
                    "Pending enrollment mutated gameplay state.");

                fixture.Provider.SimulateServerRestart();
                fixture.RestartRuntime();
                result = fixture.Coordinator.Prepare(operation);
                Equal(DurableCompositeResultCode.NotReady, result.Code,
                    "Crash-before-tag did not resume the exact Claiming root.");
                Equal(2, fixture.Provider.EnrollmentDispatchCount,
                    "Restart did not idempotently redispatch the exact enrollment.");
                fixture.Provider.CompletePendingEnrollments();
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Replicated owner tag did not complete Prepared after restart.");
                Equal(1, fixture.Provider.EnrollmentPublishCount,
                    "Owner tag was not exact missing-or-same publication.");
                fixture.Provider.DeferredMode = false;
                fixture.Provider.StageDesired(update);
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Commit(operation.Reference).Code,
                    "The first crash-cut root did not reach a terminal receipt.");
                int gameplayMutationsBeforeSecondCut = fixture.Provider.ApplyCount;

                string secondRequest = Hash("existing-enrollment-after-tag-crash");
                DurableCompositeOperationToken secondToken = fixture.Issue(secondRequest);
                DurableWorldObjectRemoveIntent remove = fixture.EnrollRemove(
                    secondToken,
                    0,
                    "remote-vanilla-remove",
                    Hash("remote-remove-before"),
                    "piece.remote.remove.before");
                fixture.Provider.SeedUnenrolled(remove);
                fixture.Provider.EnrollmentPendingMode = false;
                fixture.Provider.ThrowAfterEnrollmentPublication = true;
                DurableCompositeOperationIntent second = fixture.Operation(
                    secondToken, secondRequest, remove);
                result = fixture.Coordinator.Prepare(second);
                Equal(DurableCompositeResultCode.EvidenceConflict, result.Code,
                    "Injected crash-after-tag did not fail the current callback closed.");
                Equal(2, fixture.Provider.EnrollmentPublishCount,
                    "Crash cut did not occur after exact tag publication.");
                fixture.Provider.ThrowAfterEnrollmentPublication = false;
                fixture.Provider.SimulateServerRestart();
                fixture.RestartRuntime();
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(second).Code,
                    "Crash-after-tag/before-Prepared did not recover from root plus readback.");
                Equal(2, fixture.Provider.EnrollmentPublishCount,
                    "Recovery rewrote an already exact tag.");
                Equal(gameplayMutationsBeforeSecondCut, fixture.Provider.ApplyCount,
                    "Either Claiming crash cut performed gameplay mutation.");
            }
        }


        internal static void ExistingObjectEnrollmentConflictsAndOwnerLossFailClosed()
        {
            using (var stale = new WorldFixture())
            {
                string request = Hash("existing-enrollment-stale");
                DurableCompositeOperationToken token = stale.Issue(request);
                DurableWorldObjectUpdateIntent update = stale.EnrollUpdate(
                    token, 0, "reused-zdo", Hash("reused-before"), "reused.before",
                    Hash("reused-after"), "reused.after");
                stale.Provider.SeedUnenrolled(update);
                stale.Provider.CorruptUnenrolledTarget(update, Hash("zdo-reused-third-state"));
                DurableCompositeOperationIntent operation = stale.Operation(
                    token, request, update);
                Equal(DurableCompositeResultCode.FailedClosed,
                    stale.Coordinator.Prepare(operation).Code,
                    "Reused/stale exact target evidence did not fail closed.");
                Equal(0, stale.Provider.EnrollmentPublishCount,
                    "Stale evidence published an identity tag.");
                Equal(0, stale.Provider.ApplyCount,
                    "Stale evidence mutated gameplay state.");
            }

            using (var duplicate = new WorldFixture())
            {
                string request = Hash("existing-enrollment-duplicate");
                DurableCompositeOperationToken token = duplicate.Issue(request);
                DurableWorldObjectRemoveIntent remove = duplicate.EnrollRemove(
                    token, 0, "duplicate-zdo", Hash("duplicate-before"), "duplicate.before");
                duplicate.Provider.SeedUnenrolled(remove);
                duplicate.Provider.MarkDuplicate(remove.ObjectId);
                DurableCompositeOperationIntent operation = duplicate.Operation(
                    token, request, remove);
                Equal(DurableCompositeResultCode.FailedClosed,
                    duplicate.Coordinator.Prepare(operation).Code,
                    "Duplicate stable tag resolution was not quarantined.");
                Equal(0, duplicate.Provider.EnrollmentPublishCount,
                    "Duplicate resolution rewrote identity.");
            }

            using (var owner = new WorldFixture())
            {
                owner.Provider.DeferredMode = true;
                owner.Provider.OwnerSessionCurrent = false;
                string request = Hash("existing-enrollment-owner-loss");
                DurableCompositeOperationToken token = owner.Issue(request);
                DurableWorldObjectUpdateIntent update = owner.EnrollUpdate(
                    token, 0, "owner-lost-zdo", Hash("owner-lost-before"),
                    "owner.lost.before", Hash("owner-lost-after"), "owner.lost.after");
                owner.Provider.SeedUnenrolled(update);
                DurableCompositeOperationIntent operation = owner.Operation(
                    token, request, update);
                Equal(DurableCompositeResultCode.NotReady,
                    owner.Coordinator.Prepare(operation).Code,
                    "Disconnected exact owner was not held safely pending.");
                Equal(0, owner.Provider.EnrollmentPublishCount,
                    "Disconnected owner was treated as mutation authority.");
                owner.Provider.OwnerSessionCurrent = true;
                Equal(DurableCompositeResultCode.Prepared,
                    owner.Coordinator.Prepare(operation).Code,
                    "Exact reconnected owner could not resume identity enrollment.");
            }
        }

        internal static void CustodyExactIntentLifecycleIsExplicitAndNeverTimeoutInferred()
        {
            using (var fixture = new WorldFixture())
            {
                fixture.Provider.DeferredMode = true;
                fixture.Provider.OwnerSessionCurrent = true;
                fixture.Provider.EnrollmentPendingMode = true;
                string request = Hash("custody-lifecycle");
                byte[] custodyClause = Bytes(
                    "custody.v1;manifest=" + Hash("exact-conservation") +
                    ";prefab=TombStone;origin=session-a");
                DurableCompositeOperationToken token = fixture.Issue(request);
                DurableCompositeOutstandingReadResult issued =
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.Owner, fixture.Actor, token.CanonicalValue);
                Equal(DurableCompositeOutstandingReadState.Issued, issued.State,
                    "Server token reservation was not discoverable.");
                True(!issued.Operation.HasDurableRoot && issued.Operation.ExactIntent.Length == 0,
                    "Issued-only state was mislabeled as a custody root.");

                DurableWorldObjectUpdateIntent update = fixture.EnrollUpdate(
                    token, 0, "custody-guard-piece", Hash("custody-before"),
                    "custody.before", Hash("custody-after"), "custody.after");
                fixture.Provider.SeedUnenrolled(update);
                var requirement = new DurableCompositeReconciliationRequirement(
                    fixture.Owner.Descriptor.ModuleId,
                    fixture.Owner.Descriptor.ProtocolVersion.Major,
                    RunicCapabilityIds.InventoryDurableOperations);
                DurableCompositeOperationIntent operation = fixture.Service.CreateOperationIntent(
                    fixture.Owner,
                    token,
                    fixture.Actor,
                    request,
                    custodyClause,
                    new[] { update },
                    requirement);

                Equal(DurableCompositeResultCode.NotReady,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Custody root did not remain Claiming while identity was pending.");
                AssertOutstandingPhase(
                    fixture, token, custodyClause, DurableCompositeOperationPhase.Claiming);
                fixture.Provider.CompletePendingEnrollments();
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(operation).Code,
                    "Custody root did not reach Prepared after exact tag readback.");
                AssertOutstandingPhase(
                    fixture, token, custodyClause, DurableCompositeOperationPhase.Prepared);

                fixture.Provider.EnrollmentPendingMode = false;
                Equal(DurableCompositeResultCode.NotReady,
                    fixture.Coordinator.Commit(operation.Reference).Code,
                    "Custody root did not expose its durable Committing phase.");
                AssertOutstandingPhase(
                    fixture, token, custodyClause, DurableCompositeOperationPhase.Committing);
                fixture.Provider.StageAllPendingDesired();
                fixture.Provider.OwnerSessionCurrent = false;
                Equal(DurableCompositeResultCode.Committed,
                    fixture.Coordinator.Commit(operation.Reference).Code,
                    "Observed custody operation did not reach an immutable receipt.");
                AssertOutstandingPhase(
                    fixture, token, custodyClause, DurableCompositeOperationPhase.Committed);

                string acknowledgement = Hash("custody-next-session-proof");
                Equal(DurableCompositeResultCode.Acknowledged,
                    fixture.Coordinator.AcknowledgeReconciliation(
                        operation.Reference, acknowledgement).Code,
                    "Explicit custody reconciliation was not acknowledged.");
                DurableCompositeOutstandingQueryResult retired =
                    fixture.Factory.QueryOutstanding(fixture.Actor);
                Equal(0, retired.Operations.Count + retired.IssuedOperations.Count +
                         retired.JournaledOperations.Count,
                    "Acknowledged custody remained an admission/outstanding hold.");
                DurableCompositeOutstandingReadResult retiredDetail =
                    fixture.Factory.ReadOutstandingOperation(
                        fixture.Owner, fixture.Actor, token.CanonicalValue);
                True(retiredDetail.Success &&
                     retiredDetail.Operation.Snapshot.ReconciliationAcknowledged,
                    "Custody retirement was inferred instead of durably recorded.");

                string abortRequest = Hash("custody-explicit-abort");
                DurableCompositeOperationToken abortToken = fixture.Issue(abortRequest);
                DurableWorldObjectRemoveIntent remove = fixture.EnrollRemove(
                    abortToken, 0, "unused-custody", Hash("unused-before"), "unused.before");
                fixture.Provider.DeferredMode = false;
                fixture.Provider.OwnerSessionCurrent = true;
                fixture.Provider.SeedUnenrolled(remove);
                DurableCompositeOperationIntent abortOperation = fixture.Operation(
                    abortToken, abortRequest, remove);
                Equal(DurableCompositeResultCode.Prepared,
                    fixture.Coordinator.Prepare(abortOperation).Code,
                    "Unused custody operation did not prepare for explicit abort.");
                Equal(DurableCompositeResultCode.Aborted,
                    fixture.Coordinator.Abort(abortOperation.Reference).Code,
                    "Unused custody operation was not explicitly aborted.");
                retired = fixture.Factory.QueryOutstanding(fixture.Actor);
                Equal(0, retired.Operations.Count + retired.IssuedOperations.Count +
                         retired.JournaledOperations.Count,
                    "Aborted custody relied on timeout inference.");
            }
        }

        private static void AssertOutstandingPhase(
            WorldFixture fixture,
            DurableCompositeOperationToken token,
            byte[] exactIntent,
            DurableCompositeOperationPhase expectedPhase)
        {
            DurableCompositeOutstandingReadResult detail =
                fixture.Factory.ReadOutstandingOperation(
                    fixture.Owner, fixture.Actor, token.CanonicalValue);
            Equal(DurableCompositeOutstandingReadState.Journaled, detail.State,
                "Durable custody detail was unavailable in " + expectedPhase + ".");
            True(detail.Operation.ExactIntent.SequenceEqual(exactIntent),
                "Custody exactIntent changed in " + expectedPhase + ".");
            Equal(expectedPhase, detail.Operation.Snapshot.Phase,
                "Custody phase readback mismatch.");
        }

        internal static void PeerAdmissionIsTransportAccountScopedAndProfileExact()
        {
            using (var fixture = new Fixture())
            {
                string request = Hash("peer-admission-issued");
                fixture.Issue(fixture.First, request);

                ProtocolHello emptySpoofedHello = new ProtocolHello(
                    Hash("remote-nonce-empty"),
                    Array.Empty<ModuleProtocolState>(),
                    new[]
                    {
                        new RpcHandshakeClaim(
                            "transactions.account",
                            fixture.Actor.CanonicalKey)
                    });
                RpcHandshakeClaimEvaluation ownMissing =
                    DurableCompositePeerAdmissionPolicy.Evaluate(
                        fixture.Factory,
                        PeerContext(fixture.Actor, emptySpoofedHello));
                True(!ownMissing.Accepted && string.Equals(
                        ownMissing.ReasonCode,
                        "transactions-issued-consumer-missing",
                        StringComparison.Ordinal),
                    "The pending account was admitted without its exact consumer profile.");

                // A claim that names the pending account cannot select its ledger partition. The
                // exact direct-session identity belongs to OtherActor, which has no pending work.
                RpcHandshakeClaimEvaluation other =
                    DurableCompositePeerAdmissionPolicy.Evaluate(
                        fixture.Factory,
                        PeerContext(fixture.OtherActor, emptySpoofedHello));
                True(other.Accepted,
                    "A spoofed account claim denied an unrelated transport-bound account.");

                ModuleDescriptor owner = fixture.OwnerOne.Descriptor;
                ProtocolHello wrongVersion = Hello(new ModuleProtocolState(
                    owner.ModuleId,
                    "2.0.0",
                    owner.ProtocolVersion.Major,
                    new[] { RunicCapabilityIds.DurableCompositeOperations }));
                Equal("transactions-issued-consumer-mismatch",
                    DurableCompositePeerAdmissionPolicy.Evaluate(
                        fixture.Factory, PeerContext(fixture.Actor, wrongVersion)).ReasonCode,
                    "A wrong consumer version was accepted for an issued operation.");

                ProtocolHello missingProvider = Hello(new ModuleProtocolState(
                    owner.ModuleId,
                    owner.SemanticVersion.ToString(),
                    owner.ProtocolVersion.Major,
                    Array.Empty<string>()));
                Equal("transactions-issued-provider-missing",
                    DurableCompositePeerAdmissionPolicy.Evaluate(
                        fixture.Factory, PeerContext(fixture.Actor, missingProvider)).ReasonCode,
                    "A profile without the durable provider was accepted.");

                ProtocolHello exact = Hello(new ModuleProtocolState(
                    owner.ModuleId,
                    owner.SemanticVersion.ToString(),
                    owner.ProtocolVersion.Major,
                    new[] { RunicCapabilityIds.DurableCompositeOperations }));
                True(DurableCompositePeerAdmissionPolicy.Evaluate(
                        fixture.Factory, PeerContext(fixture.Actor, exact)).Accepted,
                    "The exact pending-account reconciliation profile was denied.");

                var connectionOnly = new RpcPeerIdentity(
                    "valheim.transport",
                    "session-test",
                    RpcIdentityAssurance.ConnectionBound);
                Equal("transactions-reconciliation-identity-required",
                    DurableCompositePeerAdmissionPolicy.Evaluate(
                        fixture.Factory, PeerContext(connectionOnly, exact)).ReasonCode,
                    "A connection-only identity selected an account WAL partition.");
                Equal("transactions-reconciliation-evaluator-failed",
                    DurableCompositePeerAdmissionPolicy.Evaluate(
                        new ThrowingOutstandingSource(),
                        PeerContext(fixture.Actor, exact)).ReasonCode,
                    "An unreadable/corrupt outstanding source did not deny admission.");

                RpcHandshakePeerContext clientReceiver = new RpcHandshakePeerContext(
                    new RpcPeerIdentity(
                        "valheim.server", "server", RpcIdentityAssurance.ConnectionBound),
                    "session-client-receiver",
                    Hash("client-local-nonce"),
                    Hash("server-remote-nonce"),
                    exact,
                    false,
                    true);
                True(DurableCompositePeerAdmissionPolicy.Evaluate(
                        new ThrowingOutstandingSource(), clientReceiver).Accepted,
                    "The client incorrectly evaluated its ConnectionBound server as a joining account.");
            }
        }

        private static ProtocolHello Hello(params ModuleProtocolState[] modules) =>
            new ProtocolHello(Hash("remote-nonce"), modules);

        private static RpcHandshakePeerContext PeerContext(
            RpcPeerIdentity identity,
            ProtocolHello hello) =>
            new RpcHandshakePeerContext(
                identity,
                "session-test",
                Hash("local-nonce"),
                Hash("peer-nonce"),
                hello,
                true,
                true);

        private static byte[] Bytes(string value) => Encoding.UTF8.GetBytes(value);

        internal static string Hash(string value)
        {
            using (SHA256 sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Bytes(value)).Select(
                    current => current.ToString("x2")));
        }

        private static string CatalogPath(Fixture fixture) => Path.Combine(
            fixture.Root, "world.test", "runic-composite.catalog");

        private static int ReadCatalogSchema(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                int bodyLength = reader.ReadInt32();
                if (bodyLength < 8) throw new InvalidDataException("Catalog body is too small.");
                byte[] body = reader.ReadBytes(bodyLength);
                if (body.Length != bodyLength) throw new EndOfStreamException();
                return BitConverter.ToInt32(body, 4);
            }
        }

        private static void Exact(byte[] expected, byte[] actual, string message)
        {
            if (expected == null || actual == null || !expected.SequenceEqual(actual))
                throw new InvalidOperationException(message);
        }

        private static void True(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
                throw new InvalidOperationException(
                    message + " Expected " + expected + "; actual " + actual + ".");
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }

        private sealed class Fixture : IDisposable
        {
            internal const string Domain = "test.composite-domain";
            internal const string Prefix = "test.endpoint:";

            private IDisposable _domainLease;

            internal Fixture()
            {
                Root = Path.Combine(
                    Path.GetTempPath(), "runic-composite-" + Guid.NewGuid().ToString("N"));
                Registry = new RunicRegistry();
                Provider = Register("runic.test-provider");
                OwnerOne = Register("runic.test-owner-one");
                OwnerTwo = Register("runic.test-owner-two");
                Actor = new RpcPeerIdentity(
                    "steam", "76561198000000001", RpcIdentityAssurance.BackendAccount);
                OtherActor = new RpcPeerIdentity(
                    "steam", "76561198000000002", RpcIdentityAssurance.BackendAccount);
                Journal = new FileDurableCompositeJournalStore(Root, "world.test");
                Factory = new DurableCompositeOperationCoordinatorFactory(Registry, Journal);
                Store = new MemoryEndpointStore();
                _domainLease = Factory.RegisterEndpointDomain(
                    Provider,
                    new DurableCompositeEndpointDomainDescriptor(Domain, 1, Prefix),
                    Store);
                First = Factory.Create(OwnerOne, Domain);
                Second = Factory.Create(OwnerTwo, Domain);
            }

            internal string Root { get; }
            internal RunicRegistry Registry { get; }
            internal FileDurableCompositeJournalStore Journal { get; private set; }
            internal DurableCompositeOperationCoordinatorFactory Factory { get; private set; }
            internal MemoryEndpointStore Store { get; }
            internal ModuleRegistration Provider { get; }
            internal ModuleRegistration OwnerOne { get; }
            internal ModuleRegistration OwnerTwo { get; }
            internal RpcPeerIdentity Actor { get; }
            internal RpcPeerIdentity OtherActor { get; }
            internal IDurableCompositeOperationCoordinator First { get; private set; }
            internal IDurableCompositeOperationCoordinator Second { get; private set; }

            internal void RestartDurableSurface()
            {
                _domainLease.Dispose();
                Factory.Dispose();
                Journal = new FileDurableCompositeJournalStore(Root, "world.test");
                Factory = new DurableCompositeOperationCoordinatorFactory(Registry, Journal);
                _domainLease = Factory.RegisterEndpointDomain(
                    Provider,
                    new DurableCompositeEndpointDomainDescriptor(Domain, 1, Prefix),
                    Store);
                First = Factory.Create(OwnerOne, Domain);
                Second = Factory.Create(OwnerTwo, Domain);
            }

            internal DurableCompositeOperationToken Issue(
                IDurableCompositeOperationCoordinator coordinator,
                string request)
            {
                DurableCompositeTokenIssueResult result =
                    coordinator.IssueOperationToken(Actor, request);
                True(result.Success, "Token issue failed: " + result.ReasonCode);
                return result.Token;
            }

            internal DurableCompositeOperationIntent Intent(
                ModuleRegistration owner,
                DurableCompositeOperationToken token,
                string request,
                params DurableCompositeEndpointIntent[] endpoints) =>
                new DurableCompositeOperationIntent(
                    owner,
                    token,
                    Actor,
                    Domain,
                    request,
                    Bytes("intent:" + request),
                    endpoints);

            public void Dispose()
            {
                try { _domainLease.Dispose(); }
                catch { }
                try { Factory.Dispose(); }
                catch { }
                try { OwnerTwo.Dispose(); }
                catch { }
                try { OwnerOne.Dispose(); }
                catch { }
                try { Provider.Dispose(); }
                catch { }
                try { Directory.Delete(Root, true); }
                catch { }
            }

            private ModuleRegistration Register(string id) => Registry.RegisterModule(
                new ModuleDescriptor(id, id, "1.0.0", "1.0", Array.Empty<string>()));
        }

        private sealed class MemoryEndpointStore :
            IDurableCompositeEndpointStore
        {
            private readonly Dictionary<EndpointId, State> _states =
                new Dictionary<EndpointId, State>();

            internal IDurableCompositeJournalStore Journal { get; set; }
            internal bool ClaimSawDurableRoot { get; private set; }
            internal bool ApplySawCommittingRoot { get; private set; }
            internal int ApplyCount { get; private set; }
            internal int RawMetadataRevision { get; private set; }
            internal long FailCommitSequenceOnce { get; set; }
            internal bool AllowAbandonAttestation { get; set; } = true;
            internal bool ThrowAbandonAttestation { get; set; }
            internal Action AfterReleaseBeforeReadback { get; set; }

            private readonly Queue<ReadBehavior> _readBehaviors = new Queue<ReadBehavior>();
            private readonly Dictionary<EndpointId, int> _releaseFailures =
                new Dictionary<EndpointId, int>();
            private readonly Dictionary<EndpointId, int> _releaseThrows =
                new Dictionary<EndpointId, int>();
            private bool _releasedSinceRead;

            internal enum ReadBehavior
            {
                Ready,
                Unavailable,
                Corrupt,
                ReadyNull,
                Throw
            }

            internal void EnqueueReadBehavior(params ReadBehavior[] behaviors)
            {
                foreach (ReadBehavior behavior in behaviors) _readBehaviors.Enqueue(behavior);
            }

            internal void FailRelease(EndpointId endpoint, int count = 1) =>
                _releaseFailures[endpoint] = count;

            internal void ThrowRelease(EndpointId endpoint, int count = 1) =>
                _releaseThrows[endpoint] = count;

            internal EndpointId Add(string suffix, string fingerprint, string semantic)
            {
                var endpoint = new EndpointId(Fixture.Prefix + suffix);
                _states.Add(endpoint, new State(fingerprint, semantic));
                return endpoint;
            }

            internal void SetDomainState(
                EndpointId endpoint,
                string fingerprint,
                string semantic,
                string epoch,
                long sequence)
            {
                State state = _states[endpoint];
                state.Fingerprint = fingerprint;
                state.Semantic = semantic;
                state.WorldEpoch = epoch;
                state.Sequence = sequence;
                state.Claim = null;
            }

            internal void SetExternalStatePreservingClaim(
                EndpointId endpoint,
                string fingerprint,
                string semantic)
            {
                State state = _states[endpoint];
                state.Fingerprint = fingerprint;
                state.Semantic = semantic;
                state.WorldEpoch = string.Empty;
                state.Sequence = 0L;
            }

            internal bool HasClaim(EndpointId endpoint) =>
                _states.TryGetValue(endpoint, out State state) && state.Claim != null;

            internal void SetClaim(
                EndpointId endpoint,
                DurableCompositeEndpointClaim claim) =>
                _states[endpoint].Claim = claim;

            internal void SetAuthority(
                EndpointId endpoint,
                bool serverOwned,
                bool destroyPending)
            {
                State state = _states[endpoint];
                state.ServerOwned = serverOwned;
                state.DestroyPending = destroyPending;
            }

            internal void DropClaims()
            {
                foreach (State state in _states.Values) state.Claim = null;
            }

            internal void Assert(
                EndpointId endpoint,
                string fingerprint,
                string semantic,
                string epoch,
                long sequence)
            {
                State state = _states[endpoint];
                Equal(fingerprint, state.Fingerprint, "Endpoint fingerprint mismatch.");
                Equal(semantic, state.Semantic, "Endpoint semantic revision mismatch.");
                Equal(epoch, state.WorldEpoch, "Endpoint epoch mismatch.");
                Equal(sequence, state.Sequence, "Endpoint sequence mismatch.");
            }

            public DurableCompositeEndpointReadState Read(
                EndpointId stableEndpointId,
                out DurableCompositeEndpointSnapshot snapshot,
                out string failureCode)
            {
                if (_releasedSinceRead && AfterReleaseBeforeReadback != null)
                {
                    Action callback = AfterReleaseBeforeReadback;
                    AfterReleaseBeforeReadback = null;
                    callback();
                }
                _releasedSinceRead = false;
                if (_readBehaviors.Count != 0)
                {
                    ReadBehavior behavior = _readBehaviors.Dequeue();
                    if (behavior == ReadBehavior.Throw)
                        throw new InvalidOperationException("test.endpoint.read-throw");
                    if (behavior == ReadBehavior.Unavailable || behavior == ReadBehavior.Corrupt)
                    {
                        snapshot = null;
                        failureCode = behavior == ReadBehavior.Unavailable
                            ? "test.endpoint.unavailable"
                            : "test.endpoint.corrupt";
                        return behavior == ReadBehavior.Unavailable
                            ? DurableCompositeEndpointReadState.Unavailable
                            : DurableCompositeEndpointReadState.Corrupt;
                    }
                    if (behavior == ReadBehavior.ReadyNull)
                    {
                        snapshot = null;
                        failureCode = "test.endpoint.ready-null";
                        return DurableCompositeEndpointReadState.Ready;
                    }
                }
                if (!_states.TryGetValue(stableEndpointId, out State state))
                {
                    snapshot = null;
                    failureCode = "test.endpoint.missing";
                    return DurableCompositeEndpointReadState.Missing;
                }
                snapshot = new DurableCompositeEndpointSnapshot(
                    stableEndpointId,
                    state.Fingerprint,
                    state.Semantic,
                    true,
                    true,
                    state.ServerOwned,
                    state.DestroyPending,
                    state.Claim,
                    state.WorldEpoch,
                    state.Sequence);
                failureCode = "test.endpoint.ready";
                return DurableCompositeEndpointReadState.Ready;
            }

            public bool TryAcquireClaim(
                DurableCompositeEndpointClaim claim,
                out string failureCode)
            {
                if (!_states.TryGetValue(claim.StableEndpointId, out State state))
                {
                    failureCode = "test.endpoint.missing";
                    return false;
                }
                if (Journal != null &&
                    Journal.Read(
                        claim.OperationIdText,
                        out byte[] rootBytes,
                        out _) == DurableCompositeJournalReadState.Present &&
                    DurableCompositeCodec.TryDecode(
                        rootBytes, out CompositeRootRecord root, out _) &&
                    (root.Phase == DurableCompositeOperationPhase.Claiming ||
                     root.Phase == DurableCompositeOperationPhase.Prepared ||
                     root.Phase == DurableCompositeOperationPhase.Committing))
                    ClaimSawDurableRoot = true;
                if (state.Claim != null)
                {
                    bool replay = claim.MatchesExact(state.Claim);
                    failureCode = replay ? "test.claim.replay" : "test.claim.conflict";
                    return replay;
                }
                state.Claim = claim;
                RawMetadataRevision++;
                failureCode = "test.claim.acquired";
                return true;
            }

            public bool TryApplyAfter(
                DurableCompositeMutationContext operation,
                DurableCompositeEndpointIntent endpoint,
                DurableCompositeEndpointClaim exactClaim,
                out string failureCode)
            {
                if (FailCommitSequenceOnce == operation.CommitSequence)
                {
                    FailCommitSequenceOnce = 0;
                    failureCode = "test.apply.injected-crash-cut";
                    return false;
                }
                State state = _states[endpoint.StableEndpointId];
                if (state.Claim == null || !exactClaim.MatchesExact(state.Claim) ||
                    (!string.Equals(
                         state.Fingerprint, endpoint.BeforeFingerprint, StringComparison.Ordinal) ||
                     !string.Equals(
                         state.Semantic, endpoint.PreparedSemanticRevision, StringComparison.Ordinal)))
                {
                    failureCode = "test.apply.conflict";
                    return false;
                }
                if (Journal != null &&
                    Journal.Read(
                        operation.OperationId.ToString("N"),
                        out byte[] rootBytes,
                        out _) == DurableCompositeJournalReadState.Present &&
                    DurableCompositeCodec.TryDecode(
                        rootBytes, out CompositeRootRecord root, out _) &&
                    root.Phase == DurableCompositeOperationPhase.Committing)
                    ApplySawCommittingRoot = true;
                state.Fingerprint = endpoint.AfterFingerprint;
                state.Semantic = endpoint.AfterSemanticRevision;
                state.WorldEpoch = operation.WorldEpoch;
                state.Sequence = operation.CommitSequence;
                ApplyCount++;
                failureCode = "test.applied";
                return true;
            }

            public bool TryReleaseClaim(
                DurableCompositeEndpointClaim exactClaim,
                out string failureCode)
            {
                State state = _states[exactClaim.StableEndpointId];
                if (_releaseThrows.TryGetValue(exactClaim.StableEndpointId, out int throws) &&
                    throws > 0)
                {
                    _releaseThrows[exactClaim.StableEndpointId] = throws - 1;
                    throw new InvalidOperationException("test.release.throw");
                }
                if (_releaseFailures.TryGetValue(exactClaim.StableEndpointId, out int failures) &&
                    failures > 0)
                {
                    _releaseFailures[exactClaim.StableEndpointId] = failures - 1;
                    failureCode = "test.release.injected-failure";
                    return false;
                }
                if (state.Claim == null)
                {
                    failureCode = "test.release.replay";
                    _releasedSinceRead = true;
                    return true;
                }
                if (!exactClaim.MatchesExact(state.Claim))
                {
                    failureCode = "test.release.conflict";
                    return false;
                }
                state.Claim = null;
                RawMetadataRevision++;
                _releasedSinceRead = true;
                failureCode = "test.released";
                return true;
            }


            private sealed class State
            {
                internal State(string fingerprint, string semantic)
                {
                    Fingerprint = fingerprint;
                    Semantic = semantic;
                    WorldEpoch = string.Empty;
                }

                internal string Fingerprint;
                internal string Semantic;
                internal string WorldEpoch;
                internal long Sequence;
                internal DurableCompositeEndpointClaim Claim;
                internal bool ServerOwned = true;
                internal bool DestroyPending;
            }
        }


        private sealed class WorldFixture : IDisposable
        {
            private IDisposable _providerLease;

            internal WorldFixture()
            {
                Root = Path.Combine(
                    Path.GetTempPath(), "runic-world-object-" + Guid.NewGuid().ToString("N"));
                Registry = new RunicRegistry();
                TransactionsModule = Register("runic.transactions-test");
                Owner = Register("runic.agriculture-test");
                Journal = new FileDurableCompositeJournalStore(Root, "world.test");
                Factory = new DurableCompositeOperationCoordinatorFactory(Registry, Journal);
                Service = new DurableWorldObjectCompositeService(
                    Registry, Factory, TransactionsModule);
                Provider = new MemoryWorldObjectProvider();
                Descriptor = new DurableWorldObjectMutationProviderDescriptor(
                    "agriculture.plant", 1);
                _providerLease = Service.RegisterMutationProvider(
                    Owner, Descriptor, Provider);
                Coordinator = Service.CreateCoordinator(Owner);
                Actor = new RpcPeerIdentity(
                    "steam", "76561198000000003", RpcIdentityAssurance.BackendAccount);
            }

            internal string Root { get; }
            internal RunicRegistry Registry { get; }
            internal ModuleRegistration TransactionsModule { get; }
            internal ModuleRegistration Owner { get; }
            internal FileDurableCompositeJournalStore Journal { get; private set; }
            internal DurableCompositeOperationCoordinatorFactory Factory { get; private set; }
            internal DurableWorldObjectCompositeService Service { get; private set; }
            internal MemoryWorldObjectProvider Provider { get; }
            internal DurableWorldObjectMutationProviderDescriptor Descriptor { get; }
            internal IDurableCompositeOperationCoordinator Coordinator { get; private set; }
            internal RpcPeerIdentity Actor { get; }

            internal DurableCompositeOperationToken Issue(string request)
            {
                DurableCompositeTokenIssueResult result =
                    Coordinator.IssueOperationToken(Actor, request);
                True(result.Success, "World-object token issue failed: " + result.ReasonCode);
                return result.Token;
            }

            internal DurableWorldObjectCreateIntent Create(
                DurableCompositeOperationToken token,
                int ordinal,
                string prefab) => new DurableWorldObjectCreateIntent(
                    token,
                    ordinal,
                    Descriptor,
                    prefab,
                    new DurableWorldObjectPose(1f + ordinal, 2f, 3f, 0f, 0f, 0f, 1f),
                    Actor,
                    Hash("ward:" + prefab),
                    Hash("terrain:" + prefab),
                    Hash("cost:" + prefab),
                    Hash("inventory:" + prefab),
                    token.CanonicalValue,
                    Bytes("provider:" + prefab));

            internal DurableWorldObjectRemoveIntent Remove(
                DurableCompositeOperationToken token,
                int ordinal,
                string existingToken,
                string prefab,
                string beforeFingerprint,
                string beforeSemantic) => new DurableWorldObjectRemoveIntent(
                    token,
                    ordinal,
                    Descriptor,
                    existingToken,
                    prefab,
                    new DurableWorldObjectPose(4f, 5f, 6f, 0f, 0f, 0f, 1f),
                    Actor,
                    beforeFingerprint,
                    beforeSemantic,
                    Hash("ward:" + prefab),
                    Hash("terrain:" + prefab),
                    Hash("cost:" + prefab),
                    Hash("inventory:" + prefab),
                    token.CanonicalValue,
                    Bytes("provider:" + prefab));

            internal DurableWorldObjectUpdateIntent Update(
                DurableCompositeOperationToken token,
                int ordinal,
                string existingToken,
                string prefab,
                string beforeFingerprint,
                string beforeSemantic,
                string afterFingerprint,
                string afterSemantic) => new DurableWorldObjectUpdateIntent(
                    token,
                    ordinal,
                    Descriptor,
                    existingToken,
                    prefab,
                    new DurableWorldObjectPose(7f + ordinal, 8f, 9f, 0f, 0f, 0f, 1f),
                    Actor,
                    beforeFingerprint,
                    beforeSemantic,
                    afterFingerprint,
                    afterSemantic,
                    Hash("ward:" + prefab),
                    Hash("terrain:" + prefab),
                    Hash("cost:" + prefab),
                    Hash("inventory:" + prefab),
                    token.CanonicalValue,
                    Bytes("provider:" + prefab));

            internal DurableWorldObjectUpdateIntent EnrollUpdate(
                DurableCompositeOperationToken token,
                int ordinal,
                string prefab,
                string beforeFingerprint,
                string beforeSemantic,
                string afterFingerprint,
                string afterSemantic) => DurableWorldObjectUpdateIntent.EnrollExisting(
                    token,
                    ordinal,
                    Descriptor,
                    prefab,
                    new DurableWorldObjectPose(7f + ordinal, 8f, 9f, 0f, 0f, 0f, 1f),
                    Actor,
                    beforeFingerprint,
                    beforeSemantic,
                    afterFingerprint,
                    afterSemantic,
                    Hash("ward:" + prefab),
                    Hash("terrain:" + prefab),
                    Hash("cost:" + prefab),
                    Hash("inventory:" + prefab),
                    token.CanonicalValue,
                    Bytes("provider:" + prefab));

            internal DurableWorldObjectRemoveIntent EnrollRemove(
                DurableCompositeOperationToken token,
                int ordinal,
                string prefab,
                string beforeFingerprint,
                string beforeSemantic) => DurableWorldObjectRemoveIntent.EnrollExisting(
                    token,
                    ordinal,
                    Descriptor,
                    prefab,
                    new DurableWorldObjectPose(4f, 5f, 6f, 0f, 0f, 0f, 1f),
                    Actor,
                    beforeFingerprint,
                    beforeSemantic,
                    Hash("ward:" + prefab),
                    Hash("terrain:" + prefab),
                    Hash("cost:" + prefab),
                    Hash("inventory:" + prefab),
                    token.CanonicalValue,
                    Bytes("provider:" + prefab));

            internal void RestartRuntime()
            {
                _providerLease.Dispose();
                Service.Dispose();
                Factory.Dispose();
                Journal = new FileDurableCompositeJournalStore(Root, "world.test");
                Factory = new DurableCompositeOperationCoordinatorFactory(Registry, Journal);
                Service = new DurableWorldObjectCompositeService(
                    Registry, Factory, TransactionsModule);
                _providerLease = Service.RegisterMutationProvider(
                    Owner, Descriptor, Provider);
                Coordinator = Service.CreateCoordinator(Owner);
            }

            internal DurableCompositeOperationIntent Operation(
                DurableCompositeOperationToken token,
                string request,
                params IDurableWorldObjectEndpointIntent[] endpoints) =>
                Service.CreateOperationIntent(
                    Owner,
                    token,
                    Actor,
                    request,
                    Bytes("world-operation:" + request),
                    endpoints);

            public void Dispose()
            {
                try { _providerLease.Dispose(); }
                catch { }
                try { Service.Dispose(); }
                catch { }
                try { Factory.Dispose(); }
                catch { }
                try { Owner.Dispose(); }
                catch { }
                try { TransactionsModule.Dispose(); }
                catch { }
                try { Directory.Delete(Root, true); }
                catch { }
            }

            private ModuleRegistration Register(string id) => Registry.RegisterModule(
                new ModuleDescriptor(id, id, "1.0.0", "1.0", Array.Empty<string>()));
        }

        private sealed class MemoryWorldObjectProvider :
            IDurableWorldObjectMutationProvider,
            IDurableWorldObjectDeferredMutationProvider,
            IDurableWorldObjectIdentityEnrollmentProvider
        {
            private readonly Dictionary<EndpointId, WorldState> _states =
                new Dictionary<EndpointId, WorldState>();
            private readonly HashSet<EndpointId> _duplicates = new HashSet<EndpointId>();
            private readonly Dictionary<EndpointId, DeferredDispatch> _pending =
                new Dictionary<EndpointId, DeferredDispatch>();
            private readonly Dictionary<EndpointId, WorldState> _unenrolled =
                new Dictionary<EndpointId, WorldState>();
            private readonly HashSet<EndpointId> _pendingEnrollment =
                new HashSet<EndpointId>();

            internal int ApplyCount { get; private set; }
            internal int CompensationCount { get; private set; }
            internal int DeferredDispatchCount { get; private set; }
            internal int EnrollmentDispatchCount { get; private set; }
            internal int EnrollmentPublishCount { get; private set; }
            internal bool DeferredMode { get; set; }
            internal bool OwnerSessionCurrent { get; set; }
            internal bool EnrollmentPendingMode { get; set; }
            internal bool ThrowAfterEnrollmentPublication { get; set; }
            internal Action<IDurableWorldObjectEndpointIntent> EnrollmentStarting { get; set; }
            internal string LastAuthorityReason { get; private set; } = "not-called";

            public DurableWorldObjectReadState Read(
                DurableWorldObjectCreationId creationId,
                out DurableWorldObjectAuthorityEvidence evidence,
                out string failureCode)
            {
                EndpointId endpoint = creationId.EndpointId;
                if (_duplicates.Contains(endpoint))
                {
                    evidence = null;
                    failureCode = "test.world-object.duplicate";
                    return DurableWorldObjectReadState.Duplicate;
                }
                if (!_states.TryGetValue(endpoint, out WorldState state))
                {
                    state = WorldState.Absent();
                    _states.Add(endpoint, state);
                }
                evidence = new DurableWorldObjectAuthorityEvidence(
                    state.ReadState,
                    state.Fingerprint,
                    state.Semantic,
                    true,
                    true,
                    !DeferredMode,
                    false,
                    state.WorldEpoch,
                    state.Sequence);
                failureCode = "test.world-object.ready";
                return state.ReadState;
            }

            public DurableWorldObjectReadState ReadExactUnenrolledTarget(
                DurableCompositeRollbackContext operation,
                IDurableWorldObjectEndpointIntent intent,
                out DurableWorldObjectAuthorityEvidence evidence,
                out string failureCode)
            {
                evidence = null;
                if (operation == null || intent == null ||
                    operation.OperationId != intent.OperationToken.OperationId ||
                    !intent.RequiresIdentityEnrollment ||
                    !_unenrolled.TryGetValue(intent.ObjectId.EndpointId, out WorldState state))
                {
                    failureCode = "test.world-object.unenrolled-missing";
                    return DurableWorldObjectReadState.Absent;
                }
                evidence = Evidence(state);
                failureCode = "test.world-object.unenrolled-ready";
                return state.ReadState;
            }

            public DurableWorldObjectIdentityEnrollmentState
                TryBeginOrResumeIdentityEnrollment(
                    DurableCompositeRollbackContext operation,
                    IDurableWorldObjectEndpointIntent intent,
                    DurableWorldObjectAuthorityEvidence exactUnenrolledEvidence,
                    out string failureCode)
            {
                if (operation == null || intent == null || exactUnenrolledEvidence == null ||
                    operation.OperationId != intent.OperationToken.OperationId ||
                    !intent.RequiresIdentityEnrollment ||
                    !_unenrolled.TryGetValue(intent.ObjectId.EndpointId, out WorldState target) ||
                    !string.Equals(
                        target.Fingerprint,
                        exactUnenrolledEvidence.Fingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        target.Semantic,
                        exactUnenrolledEvidence.SemanticRevision,
                        StringComparison.Ordinal))
                {
                    failureCode = "test.world-object.enrollment-conflict";
                    return DurableWorldObjectIdentityEnrollmentState.FailedClosed;
                }
                EnrollmentStarting?.Invoke(intent);
                if (_pendingEnrollment.Add(intent.ObjectId.EndpointId))
                    EnrollmentDispatchCount++;
                if (EnrollmentPendingMode || !OwnerSessionCurrent && DeferredMode)
                {
                    failureCode = "test.world-object.enrollment-pending";
                    return DurableWorldObjectIdentityEnrollmentState.Pending;
                }
                PublishEnrollment(intent.ObjectId.EndpointId);
                if (ThrowAfterEnrollmentPublication)
                    throw new InvalidDataException("test-crash-after-enrollment-publication");
                failureCode = "test.world-object.enrolled";
                return DurableWorldObjectIdentityEnrollmentState.Enrolled;
            }


            public bool TryApplyDesiredState(
                DurableCompositeMutationContext operation,
                IDurableWorldObjectEndpointIntent intent,
                out string failureCode)
            {
                WorldState state = Get(intent.ObjectId.EndpointId);
                bool exactBefore = string.Equals(
                        state.Fingerprint, intent.BeforeFingerprint, StringComparison.Ordinal) &&
                    string.Equals(
                        state.Semantic, intent.BeforeSemanticRevision, StringComparison.Ordinal);
                bool exactStagedAfter = string.Equals(
                        state.Fingerprint, intent.AfterFingerprint, StringComparison.Ordinal) &&
                    string.Equals(
                        state.Semantic, intent.AfterSemanticRevision, StringComparison.Ordinal);
                if (!exactBefore && !exactStagedAfter)
                {
                    failureCode = "test.world-object.third-state";
                    return false;
                }
                SetDesired(state, intent);
                state.WorldEpoch = operation.WorldEpoch;
                state.Sequence = operation.CommitSequence;
                ApplyCount++;
                failureCode = "test.world-object.applied";
                return true;
            }

            public bool IsExactCurrentOwnerMutationAuthority(
                DurableCompositeRollbackContext operation,
                long expectedCommitSequence,
                IDurableWorldObjectEndpointIntent intent,
                DurableWorldObjectAuthorityEvidence evidence,
                out string failureCode)
            {
                bool current = DeferredMode && OwnerSessionCurrent && operation != null &&
                    intent != null && evidence != null &&
                    operation.OperationId == intent.OperationToken.OperationId &&
                    expectedCommitSequence >= 0;
                failureCode = current
                    ? "test.world-object.owner-current"
                    : "test.world-object.owner-stale";
                LastAuthorityReason = failureCode;
                return current;
            }

            public DurableCompositeDeferredApplyState TryBeginOrResumeDesiredState(
                DurableCompositeMutationContext operation,
                IDurableWorldObjectEndpointIntent intent,
                out string failureCode)
            {
                if (!DeferredMode)
                    return TryApplyDesiredState(operation, intent, out failureCode)
                        ? DurableCompositeDeferredApplyState.Applied
                        : DurableCompositeDeferredApplyState.FailedClosed;
                if (!OwnerSessionCurrent)
                {
                    failureCode = "test.world-object.owner-disconnected";
                    return DurableCompositeDeferredApplyState.Pending;
                }
                WorldState state = Get(intent.ObjectId.EndpointId);
                if (string.Equals(
                        state.Fingerprint, intent.AfterFingerprint, StringComparison.Ordinal) &&
                    string.Equals(
                        state.Semantic, intent.AfterSemanticRevision, StringComparison.Ordinal) &&
                    string.Equals(
                        state.WorldEpoch, operation.WorldEpoch, StringComparison.Ordinal) &&
                    state.Sequence == operation.CommitSequence)
                {
                    failureCode = "test.world-object.owner-observed";
                    return DurableCompositeDeferredApplyState.Applied;
                }
                if (!_pending.TryGetValue(intent.ObjectId.EndpointId, out DeferredDispatch current))
                {
                    _pending.Add(
                        intent.ObjectId.EndpointId,
                        new DeferredDispatch(operation, intent));
                    DeferredDispatchCount++;
                }
                else if (current.Operation.OperationId != operation.OperationId ||
                         current.Operation.CommitSequence != operation.CommitSequence)
                {
                    failureCode = "test.world-object.owner-dispatch-conflict";
                    return DurableCompositeDeferredApplyState.FailedClosed;
                }
                failureCode = "test.world-object.owner-pending";
                return DurableCompositeDeferredApplyState.Pending;
            }

            public bool TryCompensateToBefore(
                DurableCompositeRollbackContext operation,
                IDurableWorldObjectEndpointIntent intent,
                out string failureCode)
            {
                WorldState state = Get(intent.ObjectId.EndpointId);
                if (!string.Equals(
                        state.Fingerprint, intent.AfterFingerprint, StringComparison.Ordinal) ||
                    !string.Equals(
                        state.Semantic, intent.AfterSemanticRevision, StringComparison.Ordinal))
                {
                    failureCode = "test.world-object.compensation-third-state";
                    return false;
                }
                SetBefore(state, intent);
                CompensationCount++;
                failureCode = "test.world-object.compensated";
                return true;
            }

            internal void SeedBefore(IDurableWorldObjectEndpointIntent intent) =>
                SetBefore(Get(intent.ObjectId.EndpointId), intent);

            internal void SeedUnenrolled(IDurableWorldObjectEndpointIntent intent)
            {
                var state = WorldState.Absent();
                SetBefore(state, intent);
                _unenrolled[intent.ObjectId.EndpointId] = state;
                _states.Remove(intent.ObjectId.EndpointId);
            }

            internal void CompletePendingEnrollments()
            {
                foreach (EndpointId endpoint in _pendingEnrollment.ToArray())
                    PublishEnrollment(endpoint);
            }

            internal void SimulateServerRestart() => _pendingEnrollment.Clear();

            internal void CorruptUnenrolledTarget(
                IDurableWorldObjectEndpointIntent intent,
                string fingerprint)
            {
                WorldState state = _unenrolled[intent.ObjectId.EndpointId];
                state.Fingerprint = fingerprint;
            }

            internal void SetUnenrolledThirdState(
                IDurableWorldObjectEndpointIntent intent,
                string fingerprint,
                string semantic)
            {
                WorldState state = _unenrolled[intent.ObjectId.EndpointId];
                state.Fingerprint = fingerprint;
                state.Semantic = semantic;
            }

            internal void AssertUnenrolled(
                IDurableWorldObjectEndpointIntent intent,
                string fingerprint,
                string semantic)
            {
                WorldState state = _unenrolled[intent.ObjectId.EndpointId];
                Equal(fingerprint, state.Fingerprint,
                    "Unenrolled endpoint fingerprint changed.");
                Equal(semantic, state.Semantic,
                    "Unenrolled endpoint semantic changed.");
            }

            internal void StageDesired(IDurableWorldObjectEndpointIntent intent)
            {
                WorldState state = Get(intent.ObjectId.EndpointId);
                SetDesired(state, intent);
                // A native prepared mutation does not invent a server commit marker. Existing
                // predecessor markers are retained until Transactions publishes the commit.
            }

            internal void StageAllPendingDesired()
            {
                foreach (DeferredDispatch dispatch in _pending.Values.ToArray())
                {
                    WorldState state = Get(dispatch.Intent.ObjectId.EndpointId);
                    SetDesired(state, dispatch.Intent);
                    state.WorldEpoch = dispatch.Operation.WorldEpoch;
                    state.Sequence = dispatch.Operation.CommitSequence;
                    _pending.Remove(dispatch.Intent.ObjectId.EndpointId);
                }
            }

            internal void ResetToBefore(IDurableWorldObjectEndpointIntent intent) =>
                SetBefore(Get(intent.ObjectId.EndpointId), intent);

            internal void MarkDuplicate(DurableWorldObjectCreationId objectId) =>
                _duplicates.Add(objectId.EndpointId);

            internal void AssertDesired(
                IDurableWorldObjectEndpointIntent intent,
                string epoch,
                long sequence)
            {
                WorldState state = Get(intent.ObjectId.EndpointId);
                Equal(intent.AfterFingerprint, state.Fingerprint,
                    "World-object after fingerprint mismatch.");
                Equal(intent.AfterSemanticRevision, state.Semantic,
                    "World-object after semantic mismatch.");
                Equal(epoch, state.WorldEpoch, "World-object epoch mismatch.");
                Equal(sequence, state.Sequence, "World-object sequence mismatch.");
            }

            internal void AssertBefore(IDurableWorldObjectEndpointIntent intent)
            {
                WorldState state = Get(intent.ObjectId.EndpointId);
                Equal(intent.BeforeFingerprint, state.Fingerprint,
                    "World-object before fingerprint mismatch.");
                Equal(intent.BeforeSemanticRevision, state.Semantic,
                    "World-object before semantic mismatch.");
            }

            private WorldState Get(EndpointId endpoint)
            {
                if (!_states.TryGetValue(endpoint, out WorldState state))
                {
                    state = WorldState.Absent();
                    _states.Add(endpoint, state);
                }
                return state;
            }

            private void PublishEnrollment(EndpointId endpoint)
            {
                if (!_unenrolled.TryGetValue(endpoint, out WorldState target)) return;
                _unenrolled.Remove(endpoint);
                _states[endpoint] = target;
                _pendingEnrollment.Remove(endpoint);
                EnrollmentPublishCount++;
            }

            private DurableWorldObjectAuthorityEvidence Evidence(WorldState state) =>
                new DurableWorldObjectAuthorityEvidence(
                    state.ReadState,
                    state.Fingerprint,
                    state.Semantic,
                    true,
                    true,
                    !DeferredMode,
                    false,
                    state.WorldEpoch,
                    state.Sequence);

            private static void SetDesired(
                WorldState state,
                IDurableWorldObjectEndpointIntent intent)
            {
                state.ReadState = intent.TransitionKind ==
                        DurableWorldObjectTransitionKind.RemoveToAbsence
                    ? DurableWorldObjectReadState.Absent
                    : DurableWorldObjectReadState.Present;
                state.Fingerprint = intent.AfterFingerprint;
                state.Semantic = intent.AfterSemanticRevision;
            }

            private static void SetBefore(
                WorldState state,
                IDurableWorldObjectEndpointIntent intent)
            {
                state.ReadState = intent.TransitionKind ==
                        DurableWorldObjectTransitionKind.CreateFromAbsence
                    ? DurableWorldObjectReadState.Absent
                    : DurableWorldObjectReadState.Present;
                state.Fingerprint = intent.BeforeFingerprint;
                state.Semantic = intent.BeforeSemanticRevision;
                state.WorldEpoch = string.Empty;
                state.Sequence = 0;
            }

            private sealed class WorldState
            {
                internal static WorldState Absent() => new WorldState
                {
                    ReadState = DurableWorldObjectReadState.Absent,
                    Fingerprint = DurableWorldObjectDomain.AbsentFingerprint,
                    Semantic = DurableWorldObjectDomain.AbsentSemanticRevision,
                    WorldEpoch = string.Empty
                };

                internal DurableWorldObjectReadState ReadState;
                internal string Fingerprint;
                internal string Semantic;
                internal string WorldEpoch;
                internal long Sequence;
            }

            private sealed class DeferredDispatch
            {
                internal DeferredDispatch(
                    DurableCompositeMutationContext operation,
                    IDurableWorldObjectEndpointIntent intent)
                {
                    Operation = operation;
                    Intent = intent;
                }

                internal DurableCompositeMutationContext Operation { get; }
                internal IDurableWorldObjectEndpointIntent Intent { get; }
            }
        }

        private sealed class ThrowingOutstandingSource :
            IDurableCompositeOutstandingOperationSource
        {
#pragma warning disable CS0067
            public event EventHandler<DurableCompositeOutstandingChangedEventArgs>
                OutstandingChanged;
#pragma warning restore CS0067

            public DurableCompositeOutstandingQueryResult QueryOutstanding(
                RpcPeerIdentity actorIdentity) =>
                throw new InvalidDataException("test-corrupt-wal");

            public DurableCompositeOwnerOutstandingQueryResult QueryOwnerOutstanding(
                ModuleRegistration ownerModule) =>
                throw new InvalidDataException("test-corrupt-wal");

            public DurableCompositeOutstandingReadResult ReadOutstandingOperation(
                ModuleRegistration ownerModule,
                RpcPeerIdentity actorIdentity,
                string canonicalOperationToken) =>
                throw new InvalidDataException("test-corrupt-wal");

            public DurableCompositeRequestLookupResult LookupRequest(
                ModuleRegistration ownerModule,
                RpcPeerIdentity actorIdentity,
                string durableRequestKeyHash,
                string requestHash) =>
                throw new InvalidDataException("test-corrupt-wal");
        }
    }
}
