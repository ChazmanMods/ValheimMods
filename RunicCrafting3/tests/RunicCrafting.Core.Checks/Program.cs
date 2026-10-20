using RunicCrafting.Core;
using Baseline = RunicCrafting.Domain;
using System.Text.Json;

var results = new List<object>();
var groups = new (string Name, Action Test)[]
{
    ("01 baseline limit and wider search", WiderSearch),
    ("02 restrictive alternatives and source preference", Alternatives),
    ("03 exact matching, protection and metadata", ProtectionAndIdentity),
    ("04 truthful preview and bounded source discovery", PreviewAndDiscovery),
    ("05 arithmetic, immutability and identity validation", InputBoundaries),
    ("06 fresh commit validation", FreshValidation),
    ("07 single effect and uncertain outcomes", Lifecycle),
    ("08 exact custody and partial/blocked/unknown returns", Custody),
    ("09 replay, changed payload and evidence bounds", Journal),
    ("10 cancellation, upgrade identity and live build pose", IntentIdentity),
    ("11 sequential batches and achievable quantity", Batches),
    ("12 fallback, recovery bounds and session isolation", Recovery)
};
int failed = 0;
string? evidencePath = null, groupFilter = null;
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 == args.Length) throw new ArgumentException("Options require a value.");
    if (args[i] == "--evidence") evidencePath = args[i + 1];
    else if (args[i] == "--group") groupFilter = args[i + 1];
    else throw new ArgumentException("Unknown option: " + args[i]);
}
var selectedGroups = groups.Where(g => groupFilter == null || g.Name.StartsWith(groupFilter, StringComparison.Ordinal)).ToArray();
if (selectedGroups.Length == 0) throw new ArgumentException("No matching check group.");
foreach (var group in selectedGroups)
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        group.Test(); Console.WriteLine("PASS " + group.Name);
        results.Add(new { group = group.Name, passed = true, elapsedMs = timer.Elapsed.TotalMilliseconds, error = (string?)null });
    }
    catch (Exception error)
    {
        failed++; Console.WriteLine("FAIL " + group.Name + "\n" + error);
        results.Add(new { group = group.Name, passed = false, elapsedMs = timer.Elapsed.TotalMilliseconds, error = error.ToString() });
    }
}
if (evidencePath != null)
    File.WriteAllText(evidencePath, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind = "deterministic core checks; no Unity or network peers", planned = 12, executed = selectedGroups.Length, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{selectedGroups.Length - failed}/{selectedGroups.Length} meaningful groups passed. This is not game acceptance evidence.");
Environment.ExitCode = failed == 0 ? 0 : 1;

static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static ItemStack Stack(string id = "wood", int amount = 10, ItemIdentity? identity = null, Protection protection = Protection.None) =>
    new(id, identity ?? new ItemIdentity("Wood"), amount, protection);
static MaterialSource Source(string id = "chest", IEnumerable<ItemStack>? items = null, SourceKind kind = SourceKind.Nearby,
    SourceAvailability availability = SourceAvailability.Usable, string revision = "1", double distance = 1, int preference = 0) =>
    new(id, revision, kind, availability, items ?? new[] { Stack() }, distance, preference);
static MaterialCost Cost(string id = "wood-cost", int quantity = 2, params string[] alternatives) =>
    new(id, quantity, (alternatives.Length == 0 ? new[] { "Wood" } : alternatives).Select(x => new ItemMatch(x)));
static MaterialPlan Plan(int quantity = 2) => new MaterialPlanner().Plan(new[] { Cost(quantity: quantity) }, new[] { Source() }, true);
static ActionIntent Intent(ActionKind kind = ActionKind.Craft, string id = "action", string tool = "", string upgrade = "", int units = 1) =>
    new("world", "session", id, kind, "recipe-variant-0", tool, upgradeInstance: upgrade, units: units);
static ActionObservation Observe(ActionIntent intent, bool valid = true, PlacementPose? pose = null) =>
    new(intent.World, intent.SessionNonce, intent.Selection, valid, intent.Tool, intent.Target, intent.UpgradeInstance, pose);
static ActionCoordinator Ready(ActionIntent? intent = null, MaterialPlan? plan = null)
{
    var a = new ActionCoordinator(intent ?? Intent(), 0);
    Check(a.Acquire("native", Observe(a.Intent))); Check(a.MarkReady(plan ?? Plan(), Observe(a.Intent))); return a;
}
static string Spend(ActionCoordinator a, MaterialPlan? plan = null)
{
    plan ??= Plan(); var operation = a.BeginSpend(plan.Fingerprint, Observe(a.Intent)); Check(operation != null);
    Check(a.ResolveSpend("native", operation!, OperationOutcome.Confirmed,
        plan.Slices.Select((x, i) => new DebitEvidence("debit-" + i, i, x.Quantity)))); return operation!;
}
static void Complete(ActionCoordinator a, MaterialPlan? plan = null)
{
    plan ??= Plan(); if (a.State == ActionState.Planned) { Check(a.Acquire("native", Observe(a.Intent))); Check(a.MarkReady(plan, Observe(a.Intent))); }
    Spend(a, plan); var effect = a.BeginEffect(Observe(a.Intent)); Check(effect != null);
    Check(a.ResolveEffect("native", effect!.Operation, OperationOutcome.Confirmed, "effect-receipt", 1,
        a.Custody.Slices.ToDictionary(x => x.Id, x => x.Debited))); Equal(ActionState.Completed, a.State);
}

static void WiderSearch()
{
    var oldSources = Enumerable.Range(0, 130).Select(i => new Baseline.MaterialSourceSnapshot("chest-" + i,
        Baseline.MaterialSourceKind.NearbyContainer, i, new Dictionary<string, int> { ["Wood"] = i == 129 ? 10 : 0 })).ToArray();
    Check(!new Baseline.ExactMaterialPlanner().TryPlan(new[] { new Baseline.MaterialRequirement("Wood", 1) }, oldSources, out _, out var reason));
    Equal("source-limit", reason);
    var sources = Enumerable.Range(0, 130).Select(i => Source("chest-" + i, i == 129 ? new[] { Stack() } : Array.Empty<ItemStack>(), distance: i)).ToArray();
    var planner = new MaterialPlanner();
    var partial = planner.Plan(new[] { Cost() }, sources.Take(128), false);
    Equal(PlanStatus.SearchIncomplete, partial.Status); Check(!partial.DefinitiveDeficit); Equal(0, partial.Slices.Count);
    var full = planner.Plan(new[] { Cost() }, sources, true);
    Equal(PlanStatus.Ready, full.Status); Equal("chest-129", full.Slices.Single().SourceId);
    var limited = planner.Plan(new[] { Cost() }, sources, true, workBudget: 1);
    Equal(PlanStatus.SearchIncomplete, limited.Status); Check(!limited.DefinitiveDeficit);
}

static void Alternatives()
{
    var planner = new MaterialPlanner();
    var costs = new[] { Cost("a", 1, "Wood", "Stone"), Cost("b", 1, "Wood", "Resin"), Cost("c", 1, "Wood", "Resin") };
    var source = Source(items: new[] { Stack("0", 1), Stack("1", 1, new ItemIdentity("Stone")), Stack("2", 1, new ItemIdentity("Resin")) });
    var plan = planner.Plan(costs, new[] { source }, true); Equal(PlanStatus.Ready, plan.Status);
    Equal("Stone", plan.Slices.Single(x => x.CostId == "a").Identity.Prefab); Equal(3, plan.Slices.Sum(x => x.Quantity));
    var exact = new MaterialCost("exact", 1, new[] { new ItemMatch("Wood", quality: 2) });
    var carried = Source("carried", new[] { Stack("quality", 1, new ItemIdentity("Wood", quality: 2)), Stack("ordinary", 1) }, SourceKind.Carried, distance: 100);
    var near = Source("preferred", new[] { Stack("ordinary", 20) }, preference: -10);
    var strict = planner.Plan(new[] { Cost("general", 1), exact }, new[] { near, carried }, true);
    Equal(PlanStatus.Ready, strict.Status); Check(strict.Slices.All(x => x.SourceId == "carried"));
    Equal(2, strict.Slices.Single(x => x.CostId == "exact").Identity.Quality);
    Equal(strict.Fingerprint, planner.Plan(new[] { exact, Cost("general", 1) }, new[] { carried, near }, true).Fingerprint);
    Equal("far", planner.Plan(new[] { Cost() }, new[] { Source("close", distance: 1), Source("far", distance: 100, preference: -1) }, true).Slices.Single().SourceId);
    Equal("a", planner.Plan(new[] { Cost() }, new[] { Source("z"), Source("a") }, true).Slices.Single().SourceId);
}

static void ProtectionAndIdentity()
{
    var custom = new Dictionary<string, string> { ["rune"] = "saved" };
    var identity = new ItemIdentity("Wood", 2, 3, 4, 51.25f, 7, "Crafter", custom, new Dictionary<string, string> { ["adapter"] = "v1:metadata" });
    var protectedItems = new[] { Stack("equipped", 1, protection: Protection.Equipped), Stack("locked", 1, protection: Protection.Locked),
        Stack("favorite", 1, protection: Protection.Favorite), Stack("unknown", 1, protection: Protection.Unknown),
        Stack("nonfungible", 1, protection: Protection.Nonfungible), Stack("custom", 1, identity), Stack("plain", 1) };
    var source = Source(items: protectedItems); var planner = new MaterialPlanner();
    var plain = planner.Plan(new[] { Cost(quantity: 1) }, new[] { source }, true);
    Equal("plain", plain.Slices.Single().InstanceId); Equal(1L, plain.Counts.Single().Total);
    Equal(PlanStatus.Missing, planner.Plan(new[] { Cost(quantity: 2) }, new[] { source }, true).Status);
    var selector = new ExactSelection("chest", "custom", identity.Fingerprint, Protection.None, permitCustomData: true);
    var exact = new MaterialCost("selected", 1, new[] { new ItemMatch("Wood", 2, 3, 4) }, selector);
    var selected = planner.Plan(new[] { exact }, new[] { source }, true);
    Equal(PlanStatus.Ready, selected.Status); Equal(identity.Fingerprint, selected.Slices.Single().Identity.Fingerprint);
    Equal(51.25f, selected.Slices.Single().Identity.Durability); Equal("saved", selected.Slices.Single().Identity.CustomData["rune"]);
    Equal(PlanStatus.Missing, planner.Plan(new[] { new MaterialCost("wrong", 1, new[] { new ItemMatch("Wood", 2, 2, 4) }, selector) }, new[] { source }, true).Status);
    Check(identity.Fingerprint != new ItemIdentity("Wood", 2, 3, 4, 51.26f, 7, "Crafter", custom).Fingerprint);
    Check(identity.Fingerprint != new ItemIdentity("Wood", 2, 3, 4, 51.25f, 8, "Crafter", custom).Fingerprint);
    Equal(PlanStatus.Missing, planner.Plan(new[] { exact }, new[] { Source(items: new[] { Stack("replacement", 1, identity) }) }, true).Status);
}

static void PreviewAndDiscovery()
{
    var planner = new MaterialPlanner(); var costs = new[] { Cost(quantity: 5) };
    var denied = Source("private", availability: SourceAvailability.Denied); Equal(0, denied.Items.Count);
    var sources = new[] { Source("carried", new[] { Stack(amount: 1) }, SourceKind.Carried), Source("near", new[] { Stack(amount: 2) }),
        Source("busy", new[] { Stack(amount: 10) }, availability: SourceAvailability.Busy), denied };
    var preview = planner.Plan(costs, sources, true);
    Equal(PlanStatus.WaitingForSources, preview.Status); Check(!preview.DefinitiveDeficit); Equal(0, preview.Slices.Count);
    var counts = preview.Counts.Single(); Equal(1L, counts.Carried); Equal(2L, counts.Nearby); Equal(10L, counts.Busy); Equal(2, counts.Missing);
    var deficit = planner.Plan(costs, sources.Where(x => x.Availability != SourceAvailability.Busy), true);
    Check(deficit.DefinitiveDeficit); Equal(2, deficit.Counts.Single().Missing);
    var index = new SourceIndex();
    for (int i = 0; i < 130; i++) index.Publish(Source("c" + i, i == 129 ? new[] { Stack() } : Array.Empty<ItemStack>()), i * .1, 0, 0);
    index.Publish(Source("above"), 0, 21, 0); index.Publish(Source("outside"), 21, 0, 0);
    var search = index.BeginSearch(0, 0, 0); Check(search.Expand()); Equal(128, search.Sources.Count); Check(!search.IsComplete);
    Equal(PlanStatus.SearchIncomplete, planner.Plan(costs, search.Sources, search.IsComplete).Status);
    Check(search.Expand()); Check(search.IsComplete); Equal(130, search.Sources.Count);
    Equal(PlanStatus.Ready, planner.Plan(costs, search.Sources, search.IsComplete).Status);
    Check(ReferenceEquals(search.Sources, search.Sources));
    index.Invalidate(Invalidation.Access); Check(!search.IsValid); Check(!search.IsComplete); Equal(0, search.Sources.Count); Check(!search.Expand());
    index.Clear(); Check(index.BeginSearch(0, 0, 0).IsComplete);
}

static void InputBoundaries()
{
    Throws<OverflowException>(() => Cost(quantity: int.MaxValue).Multiply(2));
    var huge = new MaterialPlanner().Plan(new[] { Cost(quantity: int.MaxValue) },
        new[] { Source("a", new[] { Stack(amount: int.MaxValue) }), Source("b", new[] { Stack(amount: int.MaxValue) }) }, true);
    Equal(PlanStatus.Ready, huge.Status); Equal(4294967294L, huge.Counts.Single().Total);
    var metadata = new Dictionary<string, string> { ["a"] = "before" };
    var identity = new ItemIdentity("Wood", customData: metadata); var digest = identity.Fingerprint; metadata["a"] = "after";
    Equal("before", identity.CustomData["a"]); Equal(digest, identity.Fingerprint);
    var list = new List<ItemStack> { Stack() }; var source = Source(items: list); list.Clear(); Equal(1, source.Items.Count);
    Throws<ArgumentException>(() => Source(items: new[] { Stack(), Stack() }));
    Throws<ArgumentException>(() => new MaterialPlanner().Plan(new[] { Cost() }, new[] { Source(), Source() }, true));
    Throws<ArgumentException>(() => new MaterialPlanner().Plan(new[] { Cost(), Cost() }, new[] { Source() }, true));
    Throws<ArgumentOutOfRangeException>(() => Source(distance: double.NaN));
    Throws<ArgumentOutOfRangeException>(() => Stack(amount: -1));
    Throws<ArgumentOutOfRangeException>(() => new ItemIdentity("Wood", durability: float.NaN));
}

static void FreshValidation()
{
    var planner = new MaterialPlanner(); var plan = Plan(); var costs = new[] { Cost() };
    Check(planner.IsFresh(plan, costs, new[] { Source() }));
    Check(!planner.IsFresh(plan, costs, new[] { Source(revision: "2") }));
    Check(!planner.IsFresh(plan, costs, new[] { Source(items: new[] { Stack(amount: 9) }) }));
    Check(!planner.IsFresh(plan, costs, new[] { Source(items: new[] { Stack("different") }) }));
    Check(!planner.IsFresh(plan, costs, new[] { Source(items: new[] { Stack(identity: new ItemIdentity("Wood", variant: 1)) }) }));
    Check(!planner.IsFresh(plan, costs, new[] { Source(items: new[] { Stack(protection: Protection.Locked) }) }));
    Check(!planner.IsFresh(plan, costs, new[] { Source(availability: SourceAvailability.Denied) }));
    Check(!planner.IsFresh(plan, costs, new[] { Source(availability: SourceAvailability.Busy) }));
    Check(!planner.IsFresh(plan, new[] { Cost(quantity: 3) }, new[] { Source() }));
    Check(!planner.IsFresh(plan, costs, Array.Empty<MaterialSource>()));
}

static void Lifecycle()
{
    var plan = Plan(); var a = Ready();
    var spend = a.BeginSpend(plan.Fingerprint, Observe(a.Intent))!; Check(spend != null);
    Check(a.BeginSpend(plan.Fingerprint, Observe(a.Intent)) == null);
    Check(a.ResolveSpend("native", spend!, OperationOutcome.Unknown, Array.Empty<DebitEvidence>()));
    Equal(ActionState.Uncertain, a.State); a.Cancel(); Equal(ActionState.Uncertain, a.State);
    Check(a.BeginEffect(Observe(a.Intent)) == null); Check(!a.Replan("server", 100, Observe(a.Intent)));
    Check(!a.ResolveSpend("server", spend!, OperationOutcome.Confirmed, new[] { new DebitEvidence("slice", 0, 2) }));
    Check(a.ResolveSpend("native", spend!, OperationOutcome.Confirmed, new[] { new DebitEvidence("slice", 0, 2) }));
    Equal(ActionState.Returning, a.State); Equal(2L, a.Custody.Held); Check(a.BeginEffect(Observe(a.Intent)) == null);
    var b = Ready(); var debit = Spend(b); var effect = b.BeginEffect(Observe(b.Intent))!;
    Check(effect != null); Check(b.BeginEffect(Observe(b.Intent)) == null);
    Check(b.ResolveEffect("native", effect!.Operation, OperationOutcome.Unknown, "timeout", 0, new Dictionary<string, int>()));
    b.Cancel(); Check(!b.Custody.BeginReturn("debit-0", "speculative", 2)); Check(b.BeginEffect(Observe(b.Intent)) == null);
    var consumed = new Dictionary<string, int> { ["debit-0"] = 2 };
    Check(b.ResolveEffect("native", effect.Operation, OperationOutcome.Confirmed, "receipt", 1, consumed));
    Equal(ActionState.Completed, b.State); Equal(0L, b.Custody.Held);
    Check(b.ResolveEffect("native", effect.Operation, OperationOutcome.Confirmed, "receipt", 1, consumed));
    Check(!b.ResolveEffect("native", effect.Operation, OperationOutcome.Confirmed, "changed", 1, consumed));
    Check(b.ResolveSpend("native", debit, OperationOutcome.Confirmed, new[] { new DebitEvidence("debit-0", 0, 2) }));
    Equal(1, b.Custody.Count); Equal(2, b.Custody.Slices.Single().Consumed);
    var invalid = Ready(); Spend(invalid); var invalidEffect = invalid.BeginEffect(Observe(invalid.Intent))!;
    Check(!invalid.ResolveEffect("native", invalidEffect.Operation, OperationOutcome.Confirmed, "bad", 1, new Dictionary<string, int> { ["debit-0"] = 1 }));
    Equal(ActionState.Applying, invalid.State);
}

static void Custody()
{
    var plan = Plan(4); var a = Ready(Intent(ActionKind.ManualInput, units: 4), plan); Spend(a, plan);
    var effect = a.BeginEffect(Observe(a.Intent))!;
    Check(a.ResolveEffect("native", effect.Operation, OperationOutcome.ConfirmedPartial, "accepted-one", 1, new Dictionary<string, int> { ["debit-0"] = 1 }));
    Equal(ActionState.Returning, a.State); var slice = a.Custody.Slices.Single(); Equal(3, slice.Held);
    Check(a.Custody.BeginReturn(slice.Id, "return-1", 3));
    Check(a.Custody.ResolveReturn(slice.Id, "return-1", OperationOutcome.Denied, 0)); Equal(3, slice.Held);
    Check(!a.FinishReturns()); Check(!a.Custody.BeginReturn(slice.Id, "return-1", 3));
    Check(a.Custody.BeginReturn(slice.Id, "return-2", 3));
    Check(a.Custody.ResolveReturn(slice.Id, "return-2", OperationOutcome.Unknown, 0));
    Check(!a.Custody.BeginReturn(slice.Id, "return-3", 3)); Equal(3, slice.Held);
    Check(a.Custody.ResolveReturn(slice.Id, "return-2", OperationOutcome.ConfirmedPartial, 1)); Equal(2, slice.Held);
    Check(a.Custody.BeginReturn(slice.Id, "return-3", 2));
    Check(a.Custody.ResolveReturn(slice.Id, "return-3", OperationOutcome.Confirmed, 2));
    Check(a.Custody.ResolveReturn(slice.Id, "return-2", OperationOutcome.ConfirmedPartial, 1));
    Check(!a.Custody.BeginReturn(slice.Id, "return-4", 1)); Check(a.FinishReturns()); Equal(ActionState.Completed, a.State);
    Equal(slice.Debited, slice.Consumed + slice.Returned + slice.Held); Equal(3, slice.Returned);
    Throws<InvalidOperationException>(() => a.Custody.ConfirmDebit("unexpected", plan.Slices[0], 1));
    var partial = Ready(); var operation = partial.BeginSpend(Plan().Fingerprint, Observe(partial.Intent))!;
    Check(partial.ResolveSpend("native", operation, OperationOutcome.Unknown, new[] { new DebitEvidence("known", 0, 1) }));
    Equal(1L, partial.Custody.Held); Check(!partial.Custody.BeginReturn("known", "blind-return", 1));
    Check(partial.ResolveSpend("native", operation, OperationOutcome.ConfirmedPartial, new[] { new DebitEvidence("known", 0, 1) }));
    Equal(ActionState.Returning, partial.State); Check(partial.BeginEffect(Observe(partial.Intent)) == null);
}

static void Journal()
{
    var journal = new OperationJournal("world", "session", 2);
    var id = new OperationId("world", "session", "1", "chest", OperationPhase.Debit, 1);
    Equal(Registration.New, journal.Register(id, "payload", out var record));
    Equal(Registration.Replay, journal.Register(id, "payload", out var duplicate)); Check(ReferenceEquals(record, duplicate));
    Equal(Registration.PayloadMismatch, journal.Register(id, "changed", out _));
    Check(journal.Resolve(id, "payload", OperationOutcome.Unknown, "no-reply")); Check(journal.Resolve(id, "payload", OperationOutcome.Confirmed, "receipt"));
    Check(journal.Resolve(id, "payload", OperationOutcome.Confirmed, "receipt"));
    Check(!journal.Resolve(id, "payload", OperationOutcome.Confirmed, "different-receipt"));
    Check(!journal.Resolve(id, "payload", OperationOutcome.StaleBeforeSpend, "zero-spend"));
    var unknown = new OperationId("world", "session", "2", "other", OperationPhase.Debit, 2);
    Equal(Registration.New, journal.Register(unknown, "b", out _)); Check(journal.Resolve(unknown, "b", OperationOutcome.Unknown, "unknown"));
    var third = new OperationId("world", "session", "3", "third", OperationPhase.Debit, 3);
    Equal(Registration.CapacityReached, journal.Register(third, "c", out _));
    Equal(Registration.Replay, journal.Register(unknown, "b", out _)); Equal(2, journal.Count);
    journal.RetireAcknowledged(2); Equal(1, journal.Count);
    Equal(Registration.Retired, journal.Register(id, "payload", out _));
    Equal(Registration.InvalidSequence, journal.Register(new OperationId("world", "session", "1", "chest", OperationPhase.Debit, 4), "payload", out _));
    Equal(Registration.Replay, journal.Register(unknown, "b", out _));
    Equal(Registration.New, journal.Register(third, "c", out _));
    Equal(Registration.InvalidSequence, journal.Register(new OperationId("world", "session", "different", "third", OperationPhase.Debit, 3), "c", out _));
    journal.Close(); Equal(Registration.WrongSession, journal.Register(id, "payload", out _)); Equal(OperationOutcome.Unknown, journal.Query(unknown)!.Outcome);
    Check(journal.Resolve(unknown, "b", OperationOutcome.Confirmed, "late-confirmation"));
    Equal(Registration.WrongSession, new OperationJournal("world", "new-session").Register(id, "payload", out _));
}

static void IntentIdentity()
{
    var pose1 = new PlacementPose(1, 0, 0, 0, 0, 0, 1, "snap-a"); var pose2 = new PlacementPose(2, 3, 4, 0, 1, 0, 0, "snap-b");
    var build = Ready(Intent(ActionKind.Build, tool: "hammer-instance")); Spend(build);
    var command = build.BeginEffect(Observe(build.Intent, pose: pose2))!; Check(ReferenceEquals(pose2, command.Pose)); Check(!ReferenceEquals(pose1, command.Pose));
    Check(build.BeginEffect(Observe(build.Intent, pose: pose1)) == null);
    var changedTool = Ready(Intent(ActionKind.Build, tool: "hammer-instance"));
    Check(changedTool.BeginSpend(Plan().Fingerprint, new ActionObservation("world", "session", "recipe-variant-0", true, "replacement-hammer", pose: pose2)) == null);
    Equal(ActionState.Cancelled, changedTool.State);
    var upgrade = Ready(Intent(ActionKind.Upgrade, upgrade: "item-instance/location-v1")); Spend(upgrade);
    Check(upgrade.BeginEffect(new ActionObservation("world", "session", "recipe-variant-0", true, upgradeInstance: "item-instance/location-v2")) == null);
    Equal(ActionState.Returning, upgrade.State);
    var cancelled = Ready(); cancelled.Cancel(); Equal(ActionState.Cancelled, cancelled.State); Check(cancelled.BeginSpend(Plan().Fingerprint, Observe(cancelled.Intent)) == null);
    var dead = Ready(); Spend(dead); Check(dead.BeginEffect(Observe(dead.Intent, false)) == null); Equal(ActionState.Returning, dead.State);
    Check(!Intent().Matches(new ActionObservation("world", "new-session", "recipe-variant-0", true)));
}

static void Batches()
{
    var template = Intent(id: "batch"); var batch = new BatchCoordinator(template, 3, 4);
    Equal(12, batch.RequestedOutput); var first = batch.Next(Observe(template), 0)!; Check(first != null);
    Check(batch.Next(Observe(template), 1) == null); Equal(0, first!.Custody.Count);
    Complete(first); Check(batch.Observe(first)); Check(!batch.Observe(first)); Equal(1, batch.CompletedCrafts); Equal(4, batch.CompletedOutput);
    var second = batch.Next(Observe(template), 10)!; Equal(0, second.Custody.Count); batch.Stop(); Check(batch.Observe(second));
    Check(batch.Finished); Equal(1, batch.CompletedCrafts); Check(batch.Next(Observe(template), 20) == null);
    var changed = new BatchCoordinator(template, 3, 4); Check(changed.Next(new ActionObservation("world", "session", "other-recipe", true), 0) == null); Check(changed.Stopped);
    var invalid = new BatchCoordinator(template, 3, 4); Check(invalid.Next(Observe(template, false), 0) == null); Check(invalid.Stopped);
    var planner = new MaterialPlanner();
    var maximum = BatchPreview.Maximum(planner, new[] { Cost(quantity: 2) }, new[] { Source() }, true, 4, 100, output => output <= 12);
    Equal(3, maximum.Crafts); Equal(12, maximum.Output); Check(maximum.Complete);
    var limited = BatchPreview.Maximum(planner, new[] { Cost() }, new[] { Source() }, false, 4, 100, _ => true); Equal(5, limited.Crafts); Check(!limited.Complete);
    Equal(1000, BatchPreview.Maximum(planner, new[] { Cost(quantity: 1) }, new[] { Source(items: new[] { Stack(amount: 10000) }) }, true, 1, 1000, _ => true).Crafts);
    Throws<ArgumentOutOfRangeException>(() => new BatchCoordinator(template, 1, 1, 1001));
    Throws<OverflowException>(() => new BatchCoordinator(template, 100, int.MaxValue));
}

static void Recovery()
{
    var a = new ActionCoordinator(Intent(), 0); Check(a.Acquire("server", Observe(a.Intent))); Check(!a.ShowPending(249)); Check(a.ShowPending(250));
    Check(a.Replan("native", 100, Observe(a.Intent))); Equal("native", a.AuthorityPath);
    Check(a.Replan("native", 1200, Observe(a.Intent))); Check(!a.Replan("native", 1200, Observe(a.Intent))); Equal(2, a.Replans);
    Check(a.MarkReady(Plan(), Observe(a.Intent))); var spend = a.BeginSpend(Plan().Fingerprint, Observe(a.Intent))!;
    Check(!a.Replan("server", 1200, Observe(a.Intent))); Check(a.ResolveSpend("native", spend, OperationOutcome.Unknown, Array.Empty<DebitEvidence>()));
    var registry = new ActionRegistry("world", "session");
    Check(registry.TryStart("craft", ActionKind.Craft, "recipe-variant-0", 0, out var pending));
    Check(!registry.TryStart("craft", ActionKind.Craft, "recipe-variant-0", 0, out _));
    Check(pending!.Acquire("native", Observe(pending.Intent))); Check(registry.MarkReady(pending, Plan(), Observe(pending.Intent)));
    var pendingSpend = registry.BeginSpend(pending, Plan().Fingerprint, Observe(pending.Intent))!;
    Check(pending.ResolveSpend("native", pendingSpend, OperationOutcome.Unknown, Array.Empty<DebitEvidence>()));
    Check(registry.TryStart("craft", ActionKind.Craft, "recipe-variant-0", 10, out var unrelated));
    Check(unrelated!.Acquire("native", Observe(unrelated.Intent)));
    Check(!registry.MarkReady(unrelated, Plan(), Observe(unrelated.Intent))); Check(registry.IsSourceIsolated("chest"));
    var alternatePlan = new MaterialPlanner().Plan(new[] { Cost() }, new[] { Source("other-chest") }, true);
    Check(registry.MarkReady(unrelated, alternatePlan, Observe(unrelated.Intent))); Complete(unrelated, alternatePlan);
    Equal(ActionState.Completed, unrelated.State); Equal(ActionState.Uncertain, pending.State);
    registry.Close(); Check(pending.CancellationRequested); Check(!registry.TryStart("craft", ActionKind.Craft, "recipe-variant-0", 20, out _));
    var expired = new ActionCoordinator(Intent(), 0); Check(expired.Acquire("native", Observe(expired.Intent))); Check(!expired.Replan("native", 1201, Observe(expired.Intent)));
    var guard = new GameThreadGuard(); guard.AssertCurrent(); Task.Run(() => Throws<InvalidOperationException>(() => guard.AssertCurrent())).GetAwaiter().GetResult();
    Check(!new SourceCapability("unproven", "chest", AccessPath.Native, true, false, true, true, true, true).CanSupply);
    Throws<ArgumentException>(() => new AccessResult(OperationOutcome.Confirmed));
}
