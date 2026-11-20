# Compatibility and Synchronization

## Baseline

Upstream: `UniversalRobots/Universal_Robots_Client_Library`, branch `master`, range **757d512..d00cd65**, exactly **70 commits**. The upstream reflog records the June 30 checkout at `757d512`, just before initial C# commit `31cdc52`, and the September 30 fast-forward. C# started this update at `f1d403c`. The baseline is supported by local history, not an arbitrary last-70 cutoff. `upstream-sync.json` records this evidence.

## Applied Changes

| Upstream intent | Managed implementation |
| --- | --- |
| Parser bounds and safe unaligned length reads | BinaryParser slice/count validation; strict primary lengths; bounded packet allocations |
| Interruptible connection teardown | UrTcpClient cancellation closes connecting sockets; RTDE/primary close streams before joining reads |
| Script include hardening | Root containment, cycle/depth checks, nested-relative paths, explicit missing-file failures |
| Streaming trajectories | StreamStart=2 / StreamEnd=3; matching upstream control script shipped with package |
| Spline precision | 1e8 derivative multiplier, range checks, away-from-zero rounding, legacy script detection |
| MotionTarget formatting | Managed ToString helper; existing joint/pose formatting preserved |
| Robot API 10.14 | Correct HTTP root/routes/verbs, OpenAPI version discovery, gated popups/log/version/system/support-file commands |
| Nullable program metadata | Nullable fields on ProgramInformation; nulls no longer break list parsing |
| Primary hardware/configuration | Type-18 HardwareInfoMessage, optional control-box/tool-flange configuration tail, corrected message routing |
| G-series | Signed 32-bit RobotType, IDs 12/13/14, version-aware series helpers |
| Error descriptions | Embedded 1,364-entry catalog v42.0.0 with exact/code-only fallback and C100 dynamic text |
| target_base_wrench | Negotiated VECTOR6D support without hard-coded field catalogs |

## Deliberate Differences

- No native Linux FIFO scheduling, POSIX descriptor wrappers, `librt`, ROS industrial CI, or native ABI shims. Managed async cancellation is used; no hard-real-time guarantee.
- .NET Standard 2.0 cannot portably canonicalize all symlinks. Script include paths containing links/reparse points are rejected, rather than followed. Templates must be trusted and not concurrently modified by another process.
- Unknown template variables remain placeholders through the existing ScriptReader API for compatibility; `StrictVariables` is enabled by UrDriver. Invalid include paths now throw.
- Fresh background RTDE reads may coalesce samples, return null on timeout, and propagate cancellation. Returned data is no longer mutated by the next parser cycle.
- `RobotType` changes backing type from sbyte to int to match the primary wire protocol. Recompile consumers that depend on enum layout.
- PolyScope X defaults to HTTP port 80, not G5's port 29999. Explicit custom HTTP ports remain available.
- HTTP metadata is typed where useful and raw response dictionaries remain available. Cancellation propagates; network failures and unsupported versions remain distinguishable.

## Remaining Risks

| Severity | Gap | Status / Required Follow-Up |
| --- | --- | --- |
| High | Complete motion and safety behavior | Offline wire tests are not validation of robot motion. Verify finite/streamed trajectories, cancellation, protective stops, and script deployment in URSim/hardware before use. |
| High | Automatic reconnect and lifecycle overlap | The C# API lacks the full C++ reconnect state machine. Do not concurrently call lifecycle methods; recreate a client after a failed/partial packet read. |
| High | Primary legacy models | Safety, joint/masterboard and runtime-exception parsers outside this change range remain partial and require additional upstream conformance fixtures. |
| High | Blocking script execution | SendScriptBlockingAsync predates this sync and does not match the complete C++ start/finish/error workflow. Do not use it as proof that motion completed. |
| Medium | Control feedback and waiter lifecycle | TCP feedback fragmentation and result-arrival-before-wait scenarios require further control-layer work. |
| Medium | RTDE v1 and reconnect coverage | Packet layout changes are implemented, but CB3 hardware and protocol-v1 end-to-end behavior are not certified. |
| Medium | Robot API 10.14/G-series | Verified against upstream source and offline fixtures, not a live 10.14/10.15 controller. Operational-mode setters remain unsupported in upstream too. |
| Medium | High-level writer batches | Input snapshots avoid shared-packet mutation; full lossless queueing and every combined mask/value helper need broader upstream parity work. |
| Release Gate | Identity, SourceLink, Pages and NuGet | Configure actual origin, package ownership, protected environments and secrets; hosted execution is not verifiable from this local checkout. |

This is a preview synchronization, not a blanket declaration that the older native rewrite already had full C++ parity. No new physical-robot testing or motion was performed during this update.

## Local Validation (2026-10-01)

Restore, Release build, all 183 tests, NuGet/symbol packing, DocFX generation, actionlint, package-content checks and a credential-pattern scan passed. Library coverage is 49.5% of lines and 36.7% of branches. Compiler warnings remain in older code; SourceLink warns because no origin remote is configured. No GitHub deployment, NuGet push, or robot motion was executed.