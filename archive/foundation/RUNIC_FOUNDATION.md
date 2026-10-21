# Retired Runic Foundation packages

Runic Core 1.0.0, Runic Persistence 1.0.0, Runic Permissions 1.0.0, and Runic Transactions 1.0.0 are
retired runtime packages. They are not dependencies of the current gameplay mods and are not part of
the Foundation-free release inventory. Do not install or publish them with the current suite.

The repository may retain selected source files under the old project directories as build-time
source inputs or historical implementation evidence. Those files are compiled directly into the few
gameplay DLLs that need the relevant bounded helper; they do not produce or require a shared runtime
DLL. The Core module/service registry, cross-mod capability discovery, protocol-version coupling,
durable composite transaction system, global inventory locks, quarantine, and join-time recovery
enforcement are not used by the gameplay release.

No user world, character, catalog, or old release-evidence data is deleted by this retirement. Runic
Portals continues to own and read its existing portal/group feature data formats. Superseded package
archives remain historical evidence only after the replacement release passes its acceptance and
coexistence gates.

Recoverable superseded Foundation packages are archived under
`artifacts/RunicFoundation/Obsolete/`; name collisions receive a new archive path rather than
overwriting prior evidence. A validation-only run does not run the
test suites, create or change the Foundation artifact directory, or promote a package.
