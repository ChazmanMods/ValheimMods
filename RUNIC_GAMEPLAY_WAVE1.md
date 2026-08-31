# Runic Gameplay Wave 1 — Foundation-free releases

Storage, Crafting, Agriculture, Inventory, and Production are independently installable for Valheim
0.221.12. Each package requires only BepInExPack Valheim 5.4.2333. Install any subset and do not
install Runic Core, Persistence, Permissions, or Transactions.

Runic Inventory is an optional enhancement for Storage and Crafting. When its small public API is
present, those mods honor its item-protection result. When it is absent, they retain their own safe
standalone behavior. An incompatible installed optional API denies only the enhanced action; it does
not prevent either plugin from loading.

## Runtime boundaries

- Storage uses bounded feature-owned requests for remote chest actions and native container
  ownership/mutation paths. A timeout or rejection clears only that request.
- Crafting plans and applies one native craft/build/repair action at a time. It creates no persistent
  gameplay journal and never holds a suite-wide inventory lock.
- Agriculture uses bounded planting/harvest/replant requests with current ward, range, terrain,
  ownership, and material checks. Rejected work cannot leave a durable reservation.
- Inventory owns only its item/equipment features. It exposes no global mutation lock, quarantine,
  reconciliation loop, or join-time recovery gate.
- Production observes loaded stations and performs bounded replenishment only after the exact input,
  capacity, access, and native ownership checks pass. It persists no operation journal.

All five preserve vanilla recipes, progression, timing, capacity, access checks, and item costs.
Unsupported or indeterminate enhanced actions fail closed while the original vanilla path remains
available. Removing any package requires no gameplay-journal cleanup or migration.

Replace older DLLs rather than leaving multiple versions of the same mod installed together. The
release versions remain Inventory 1.0.0, Storage 1.0.0, Crafting 1.0.0, Agriculture 1.0.0, and
Production 1.0.0.
