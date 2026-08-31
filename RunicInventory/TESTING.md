# Runic Inventory testing

Target: Valheim 0.221.12.

Automated tests cover the exact topology, persistence codec, lossless position rollback, pickup
policy, controller bindings, optional reflection API, installed client/dedicated contracts, and the
absence of Foundation DLL references.

Manual acceptance should verify:

1. With Foundation DLLs absent, install Inventory by itself and confirm quick slots, equip swaps,
   locks, sort, pickup filtering, and configuration reload. In the open inventory, verify the exact
   bottom cells say Helmet, Chest, Legs, Cape, Utility, and Quick 1-3, each with a yellow border.
   Toggle a general slot with Left Alt + right-click, then repeat to unlock it.
2. Exercise death, tombstone recovery, logout/login, disable/re-enable, and uninstall with every
   special role occupied; require no item loss or duplication.
3. Connect one client to a dedicated server. Confirm only that client's native inventory changes and
   the headless server remains inert.
4. Test each independently installed Interaction, Crafting, or Storage mod, then test them together.
   Confirm they discover the
   optional reflection API only when Inventory is present and keep working when it is absent.
5. Inspect the release DLL references and Thunderstore manifest; Core, Persistence, Permissions, and
   Transactions must be absent.
6. Remove the utility belt from its role, move it through another slot, and equip it again. The
   bottom-row labels/borders and role behavior must recover immediately, and Storage protection
   queries for carried items must again return a proven result.

The pass criterion is exact native ownership, bounded local work, vanilla persistence, and no item
loss across supported lifecycle paths.
