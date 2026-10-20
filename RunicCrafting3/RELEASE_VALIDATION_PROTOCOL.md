# RUNIC PRACTICAL RELEASE VALIDATION PROTOCOL

## Objective

Before declaring a Runic issue resolved, prove three things:

1. The reported problem is actually fixed.
2. The fix did not break anything reasonably connected to it.
3. The change does not introduce a serious multiplayer, persistence, duplication, loss, or compatibility risk.

Do NOT attempt exhaustive testing of every Runic feature for every change.

Testing must be proportional to the risk and blast radius of the change.

---

# STEP 1 - PROVE THE BUG

Before changing code:

- Reproduce the reported problem if possible.
- Identify the actual root cause.
- Record the failing code path.
- Determine what systems touch that code.

Do not fix symptoms unless the root cause cannot reasonably be determined.

---

# STEP 2 - MAP THE BLAST RADIUS

After determining the fix, answer:

### What did I change?

### What directly calls or depends on what I changed?

### What shared systems does this touch?

Pay special attention to:

- inventory
- containers
- crafting
- production
- ZDO state
- RPCs
- ownership
- server authority
- persistence
- configuration
- Harmony patches
- player lifecycle
- Runic cross-module integration

From this analysis, create a SHORT list of functionality that could plausibly regress.

Do not test unrelated functionality.

---

# STEP 3 - ASSIGN A RISK LEVEL

Every change gets one of three levels.

## LEVEL 1 - LOW RISK

Examples:

- UI text
- logging
- tooltip
- cosmetic behavior
- isolated calculation
- null guard
- clearly local code with no persistent/networked state

Required:

- build
- original bug test
- immediate neighboring functionality
- error/log check

Then stop.

---

## LEVEL 2 - MODERATE RISK

Examples:

- crafting logic
- inventory manipulation
- storage search
- workstation interaction
- configuration behavior
- build interaction
- feature logic used by several methods

Required:

- everything from Level 1
- original reproduction
- positive case
- obvious negative case
- 2-5 highest-risk neighboring scenarios
- save/reload if state is persisted
- host/client test if multiplayer state is involved

Then stop unless something suspicious appears.

---

## LEVEL 3 - HIGH RISK

Use this level when touching:

- server authority
- RPCs
- ZDOs
- ownership
- shared containers
- simultaneous inventory mutation
- item consumption
- item creation
- persistent world state
- character data
- save data
- cross-module shared infrastructure

Required:

- everything from Level 2
- host performs operation
- joined client performs operation
- second participant observes correct result where applicable
- reconnect/reload
- concurrency test where shared resources are involved
- upgrade from current public version if persistence changed
- relevant Runic integration test

Do NOT automatically test the entire Runic suite.

Only test modules sharing the affected subsystem.

---

# STEP 4 - TEST BUDGET AND SCOPE CONTROL

Testing must be aggressive but bounded.

The default target is:

- **Level 1 / Low Risk:** approximately 2-5 meaningful checks
- **Level 2 / Moderate Risk:** approximately 5-12 meaningful checks
- **Level 3 / High Risk:** approximately 10-25 meaningful checks

These are not rigid quotas. They are guardrails.

Do not create large numbers of redundant tests simply because more scenarios can be imagined.

Prefer a smaller number of high-information tests that exercise:

- the original defect,
- the actual root cause,
- the highest-risk blast-radius dependencies,
- the most likely regression,
- and the applicable Runic invariants.

Testing beyond these ranges should occur only when evidence justifies escalation, such as:

- a regression is discovered,
- results are inconsistent,
- synchronization behaves unexpectedly,
- ownership is unclear,
- concurrency exposes instability,
- the root cause is broader than initially believed,
- or an applicable invariant fails.

## Mandatory reasoning before testing

Before executing tests, state:

1. **Risk level**
2. **Blast radius**
3. **Most likely regression paths**
4. **Planned number of meaningful checks**
5. **Why those checks provide sufficient coverage**

Then execute those tests.

Do not begin with a massive generic regression suite.

## Core rule

Codex must not think:

> "I changed Foo(). Foo() passes. Done."

It must think:

> "I changed Foo(). What else relies on Foo(), the state Foo changes, or the assumptions Foo makes?"

Test those paths.

Then stop once the original defect, meaningful blast radius, adversarial checks, and applicable invariants all pass.

The goal is maximum confidence per test, not maximum test count.

---

# STEP 5 - TEST THE ORIGINAL BUG

Run the exact reproduction that previously failed.

It must now pass.

Then run at least one meaningful variation designed to determine whether the fix merely patched the reproduction rather than fixing the underlying problem.

Example:

If opening a chest while crafting failed:

Do not test only:

"Open chest → craft."

Also test something like:

"Second player opens chest → first player crafts."

The variation should attack the root cause.

---

# STEP 6 - REGRESSION TEST THE BLAST RADIUS

For every item identified in Step 2, run the smallest useful test proving it still works.

Do not generate dozens of redundant cases.

Prefer high-information tests.

Example:

If storage withdrawal changed, a good sequence might be:

1. withdraw normally
2. withdraw last available item
3. two clients attempt related operations
4. verify final item count

That may provide more useful evidence than 30 tiny tests.

---

# STEP 7 - CHECK RUNIC INVARIANTS

For affected systems, verify the applicable invariant.

## ITEM CONSERVATION

Starting quantity  
+ legitimate production  
- legitimate consumption  
= ending quantity

No unexplained duplication.

No unexplained loss.

---

## MULTIPLAYER CONSISTENCY

All participants eventually agree on the authoritative state.

---

## PERSISTENCE

State before save/reload remains semantically correct afterward.

---

## AUTHORITY

Only the intended authoritative actor performs persistent mutations.

---

## CRAFTING

One craft consumes exactly the required resources and creates exactly the intended output.

These invariants are more important than generating huge numbers of individual tests.

---

# STEP 8 - ADVERSARIAL CHECK

Before declaring success, ask exactly these questions:

### 1. What is the most likely thing this fix accidentally broke?

Test it.

### 2. What is the easiest way the original bug could still exist in a slightly different form?

Test it.

### 3. Could this duplicate, destroy, desynchronize, or corrupt player data?

If yes, test the most likely scenario.

### 4. Could host and client behave differently?

If yes, test both.

### 5. Could this fail after reconnect or reload?

If yes, test one reconnect/reload cycle.

This is mandatory.

---

# STEP 9 - ESCALATE ONLY WHEN WARRANTED

Do additional testing if:

- a regression appears,
- logs show unexpected errors,
- test results are inconsistent,
- concurrency is involved,
- ownership behaves unexpectedly,
- the root cause turns out to affect more code than expected,
- another similar implementation is discovered.

Do NOT expand testing merely because more tests are theoretically possible.

Testing should follow evidence and risk.

---

# STEP 10 - STOP CONDITION

Testing is complete when:

- the original defect passes,
- the root cause has been addressed,
- the blast-radius tests pass,
- applicable invariants hold,
- the adversarial checks pass,
- no new relevant errors appear.

At that point STOP TESTING.

Do not continue inventing low-probability cases unless the change is specifically high risk.

---

# STEP 11 - FINAL REPORT

Return this concise report:

## FIX
What was wrong and what changed.

## ROOT CAUSE
Why it was happening.

## RISK LEVEL
1 / 2 / 3 and why.

## TEST BUDGET
How many meaningful checks were planned and how many were actually executed.

## TESTS RUN
List the actual meaningful tests.

## REGRESSION CHECK
What neighboring functionality was checked.

## INVARIANTS
Which applicable invariants were verified.

## ISSUES FOUND
Anything discovered during regression testing.

## UNTESTED RISK
Anything important that could not actually be validated.

## VERDICT

Use exactly one:

**PASS - Ready for release**

**FAIL - More work required**

**INCOMPLETE - Important validation could not be performed**

Never claim PASS merely because the original reproduction works.

---

# CORE RULE

The goal is not:

**"Test everything."**

The goal is:

**"Understand what this change could break, attack those failure modes intelligently, and stop once the meaningful risk has been covered."**

Use the smallest number of tests that provides strong evidence.

Spend compute where the risk is, not everywhere.