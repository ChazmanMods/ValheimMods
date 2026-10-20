# Compatibility evidence at the core checkpoint

| Environment / capability | Evidence | Current claim |
|---|---|---|
| .NET Standard 2.1 core / .NET 9 checker | Clean build; 12 deterministic groups | Core library compiles and specified model checks pass |
| Valheim 1.0.16, network 40, vanilla dedicated server | Prior separate diagnostic client and human-operated vanilla client | Only the recorded native chest handoff/debit/observer/save-reload sequence |
| Sole client plugin installation | No production bootstrap or gameplay hooks | Incomplete |
| Listen server, host and joined crafter | No new-core gameplay run | Incomplete |
| Two crafters competing for last resources | Pure accounting checks; no new-core live run | Incomplete |
| Optional dedicated-server coordinator with vanilla writers | No implementation of native write mediation | Incomplete; no server capabilities advertised |
| RunicStorage / RunicProduction | Existing sources preserved; no new live adapter | Incomplete; no authority or protection integration claimed |
| Expanded inventories / custom costs / custom storage | Generic exact model only | Native matching/capacity/authority adapters and live evidence required |
| Remote cooking/input with native silent rejection | Single-process installed-method diagnostic | Unresolved acceptance correlation; not enabled |
| Process crash / independent save restoration | No durable cross-inventory journal | Unsupported claim; never described as crash-proof |

The core has no Unity, BepInEx, Harmony, Storage or Production dependencies.
Source capability contracts in code are not discovered or advertised services.
