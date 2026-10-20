# Authority and outcome contract: prerequisite status

This is an evidence-backed design boundary, not a declaration that 3.0 is built.

## Ordinary native chests

The actual unmodified owner responds to the ordinary native RequestOpen protocol.
It denies requests while the vanilla player is viewing the chest. After the panel
closes it transfers ownership and sends a response. The diagnostic client waits
for the server's ownership/data packet, materializes the Inventory before marking
it in-use, rechecks access/range, performs a bounded native mutation, and releases
only its own access. It never forces the old owner back or displays a chest panel.

Observed live evidence: busy denial; idle transfer with seven current Wood;
one-Wood diagnostic consumption; user independently observes six and can take all;
native client reconnect. Save/reload confirmation is recorded in VALIDATION-REPORT.

These observations support this *candidate* native chest path. They do not yet
prove the complete ownership-churn/message-fault/concurrent-crafter matrix or
production crafting. Public/native chest identity must be explicitly checked;
custom inventories and authority-managed storage need adapters, never display-copy
debits. Tombstones and mobile/character inventories are not covered by this proof.

## Required debit authorization

An operation must bind world, session, action, source and immutable costs. A preview
does not authorize mutation. At the game-thread execution boundary require actual
native authorization, current native writer exclusion, authoritative payload
receipt and fresh materialized inventory, current permission/range, exact item
identity, and valid action/cancellation state. A replicated owner flag alone or a
client-side mutex is not the contract. Every spent slice needs an exact outcome.

## Manual cooking: unresolved required capability

Installed CookingStation.CookItem removes a carried ingredient before sending
RPC_AddItem(string itemName, bool cheated). The receiver checks its free slot at
execution time and may silently accept nothing. The native visual RPC contains a
slot and item name, not the requesting actor's action ID. The accepted/refused
quantity is not returned to the caller. Fermenter has the same consume-before-
remote-empty-check pattern. Fuel messages also need an independent outcome audit.

The actual installed-method diagnostic, with an injected delayed request and
competing final-slot input, observed:

- Native caller returned true and removed one RawMeat.
- Before delivery a competing input filled the final slot.
- Delivery changed no target state and returned no correlated acceptance.
- The user's ingredient remained removed.
- The uncontended local control consumed one and filled one slot correctly.

This diagnostic is single-process, with injected competition. It is not genuine
two-client cooking evidence. Logs preserve the first failed helper interception
attempt and the corrected run separately.

### Why a later slot count is insufficient

Two competing inputs of the same item can produce identical observed target state
whether this client's request was accepted or the other request was accepted.
The native messages observed here do not identify which action filled the slot.
A timeout is not rejection evidence. Refunding or resending would risk duplicate
value/effects; consuming irrevocably on send can lose a rejected ingredient.

### What may and may not proceed

An action-specific native execution path with independently proven exclusion and
acceptance, or cooperation at the actual target authority, is required. Optional
server support cannot be silently promoted into a client-only dependency. Forcing
ownership of an active remote target or inferring acceptance from a stock change
does not resolve the proof obligation.

No general safe client-only remote-target path has been established in this work.
Such targets must remain explicitly unsupported until proven. This is a 3.0
release blocker under the supplied contract, not authorization to drop manual
input, change its player promise, or ship the native loss behavior.

## Unknown outcomes and returns

Resolve the original operation only. Never retry a potentially applied effect
under a new identity. Isolate the affected source/obligation; unrelated sources
remain usable. Return only exact confirmed unused slices, retaining metadata and
explicit custody when capacity prevents return. Never restore an inventory image
over unrelated writes. No cross-inventory crash-atomicity claim is made.

## Optional server

Not implemented. Capability negotiation must authenticate the actual peer/session
and advertise only independently tested source/action/write mediation. A queue
alone cannot grant authority. The 2.1.2 baseline's mandatory matching-module
handshake (including rejecting missing peers after 45 seconds) is incompatible
with the 3.0 optional/vanilla-player installation contract and is not reused as a
pretend optional server package. No authority migration was attempted.
