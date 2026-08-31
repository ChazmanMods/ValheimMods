# Changelog

## 1.0.0

- Removed the mandatory Runic Core, Persistence, Permissions, Transactions, and Inventory runtime
  dependencies without changing the published version.
- Deleted capability registration, protocol coupling, the durable remote crafting saga, composite
  container claims, player journals, recovery enforcement, and the suite-wide mutation gate.
- Kept exact carried-first material allocation and rollback as a Crafting-owned process-local lease.
- Enabled the same native local-player and container-owner path for solo, listen-server, and
  dedicated-client processes; non-owned containers are excluded from the attempt.
- Preserved Workshop Access ZDO keys and added optional group membership lookup through Runic
  Portals without making Portals a load requirement.
- Kept combined requirement displays, stationless build rules, placement rollback, and Repair All.
