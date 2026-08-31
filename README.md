# Chazman Mods

Official source, media, and releases for Valheim mods created by **Chazman**.

## Runic Valheim Vanilla Plus suite

The Runic gameplay mods are independently installable and independently versioned. BepInExPack
Valheim 5.4.2333 is their only shared runtime requirement; there is no Runic Core, Persistence,
Permissions, Transactions, or replacement shared-library package to install.

| Design module | Release | Purpose |
|---|---:|---|
| [Storage](./RunicStorage/) | 1.0.0 | Bounded chest actions, authorized queries, and chest-content hover |
| [Crafting](./RunicCrafting/) | 1.0.0 | Nearby material plans and vanilla-preserving craft/build/repair integration |
| [Agriculture](./RunicAgriculture/) | 1.0.0 | Bounded planting, harvest, and replant tools |
| [Production](./RunicProduction/) | 1.0.0 | Loaded-station replenishment and truthful station hover detail |
| [Building](./RunicPrecisionBuildTool/) | 2.0.1 | Precision transforms, snap matching, repeat history, catalog tools, repair, and conservative undo |
| [Inventory](./RunicInventory/) | 1.0.0 | Lossless native-row equipment/quick roles, item protection, sorting, and pickup planning |
| [Portals](./RunicPortals/) | 1.0.0 | Public/private/group exact-network routing, temporary authorized pins, and session Return |
| [Exploration](./RunicExploration/) | 1.0.0 | Privacy-preserving search and navigation over already-known map pins |
| [Awareness](./RunicAwareness/) | 1.0.0 | Bounded display-only food, effect, comfort, item, and context explanations |
| [Interaction](./RunicInteraction/) | 1.0.0 | Guarded interaction conveniences, session-only door close, text validation, and pickup filtering |
| [Safety](./RunicSafety/) | 1.0.0 | Loss prevention and feature-owned compatibility/admission checks |
| [Velocity](./RunicVelocity/) | 1.0.0 | Bounded startup measurement and validated assembly-manifest caching |
| [Sentinel](./RunicSentinel/) | 1.0.0 | Signed-policy verification and direct admission evidence |
| [World Engine](./RunicWorldEngine/) | 1.0.0 | Rate-limited, read-only aggregate ZDO observation |

The suite does not use durable composite gameplay journals, suite-wide inventory locks, account
quarantine, join-time recovery enforcement, or a global capability registry. Networked features own
their small bounded request state and use authenticated Valheim peers and native ownership paths.
Failed feature requests release only their own short-lived state.

Runic Portals retains its feature-owned portal and group world-data formats. Runic Crafting and
Runic Storage can use Runic Inventory when present, but Inventory is optional and its absence never
prevents either mod from loading.

[Runic Build Camera](./RunicBuildCamera/) and [Runic Integrity](./RunicIntegrity/) remain independent
companions and are unchanged by the Foundation removal. The retired Foundation packages are
documented in [RUNIC_FOUNDATION.md](./RUNIC_FOUNDATION.md), and the Wave 1 behavior/install guide is
in [RUNIC_GAMEPLAY_WAVE1.md](./RUNIC_GAMEPLAY_WAVE1.md).

## Community and support

Join the **Chazman Mods Discord** for support, bug reports, screenshots, and mod discussion:

https://discord.gg/7HKHTCdFqY
