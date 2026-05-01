# Chunk 18 — Sample-app native interop: threat model

**Status:** Phase 2 deep-review (initial pass)
**Reviewer:** Security review, Phase 2
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3` (`main`)
**Companion:** [000-chunking-and-threat-model.md](./000-chunking-and-threat-model.md) §8 (Tier-5)

---

## 1. Scope

Files under `samples/apps/**` that contain `unsafe`, `[DllImport]`, `[LibraryImport]`, `Marshal.*`, `StructLayout`, or `fixed` blocks. Glob found exactly the following review-relevant files:

| File | LOC | Role |
|---|---:|---|
| `samples/apps/netpulse/Native/IpHelper.cs` | 183 | P/Invoke wrappers around `iphlpapi.dll` (`GetExtendedTcpTable`, `GetExtendedUdpTable`, `GetIfTable2`, `FreeMibTable`); raw pointer walks over MIB tables. |
| `samples/apps/netpulse/NetworkMonitor.cs` | 183 | Caller; four background timers polling the above every 50 ms. (Pure managed code — no `unsafe`. Listed for context only.) |
| `samples/apps/reactorfiles/Native/NativeFs.cs` | 33 | `[LibraryImport]` declarations + `[StructLayout(Sequential)]` for `FsEntry` / `FsResult` against a Rust `cdylib`. |
| `samples/apps/reactorfiles/Services/FileSystemService.cs` | 175 | C# caller — `unsafe` blocks, `fixed (char*)` pinning of path string, raw pointer walks of `FsEntry[]`. |
| `samples/apps/reactorfiles/Native/reactorfs/src/ffi.rs` | 62 | Rust FFI surface (entry / free). |
| `samples/apps/reactorfiles/Native/reactorfs/src/enumerate.rs` | 123 | Rust enumerator using `jwalk`; allocates UTF-16 name buffers and returns them via raw pointers + `mem::forget`. |
| `samples/apps/reactorfiles/Native/reactorfs/src/types.rs` | 25 | `#[repr(C)]` definitions for `FsEntry` / `FsResult`. |
| `samples/apps/reactorfiles/Native/reactorfs/src/lib.rs` | 4 | Module wiring only. |
| `samples/apps/chat/App/Notifications.cs` | 96 | Single `[DllImport("user32.dll")] SetForegroundWindow`. Declared but **not called** from anywhere in the sample (verified with Grep; only the extern declaration matched). Listed because it is in scope per the chunk definition. |

Total review-relevant native-interop surface: ~575 LOC.

**Out of chunk scope (referred elsewhere):**
- All non-native sample-app code (TodoApp, monaco-editor JS bundles, regedit registry P/Invoke is in `RegistryService.cs` but uses managed `Microsoft.Win32.Registry` — no P/Invoke, confirmed). The chunking doc explicitly excludes sample-app application logic except the native interop.
- `monaco-editor`’s bundled `*.worker-*.js` files matched the regex but are minified third-party JavaScript, not C# native-interop. Excluded.

---

## 2. Data-flow diagram

```
                     ┌──────────────────────────────────────────────┐
                     │ NetPulse process (managed)                   │
                     │                                              │
NetworkMonitor ──50 ms──► IpHelper.GetTcpConnections() ─┐           │
                          IpHelper.GetUdpEndpoints()    │           │
                          IpHelper.GetInterfaceSnapshots()           │
                                                        │           │
                                                        ▼           │
                                ┌────────────────────────────┐      │
                                │ Marshal.AllocHGlobal(size) │      │
                                └────────────┬───────────────┘      │
                                             │ IntPtr               │
                                             ▼                      │
                          ┌──────────────────────────────────┐      │
                          │  iphlpapi.dll                    │      │
                          │  GetExtendedTcpTable / Udp / If2 │      │
                          └────────────┬─────────────────────┘      │
                                       │ writes MIB rows            │
                                       ▼                            │
                          raw byte* walk @ fixed offsets ────►      │
                                       │                            │
                                       ▼                            │
                          managed TcpConn[] / UdpEndpoint[] /       │
                          List<InterfaceSnapshot>                   │
                                                                    │
                          finally: Marshal.FreeHGlobal(buf)         │
                                   FreeMibTable(table)              │
                     └──────────────────────────────────────────────┘

                     ┌──────────────────────────────────────────────┐
                     │ ReactorFiles process (managed + unsafe)      │
                     │                                              │
DirectoryTree ──► FileSystemService.EnumerateNative(path)           │
                            │                                       │
                            │ fixed (char* pathPtr = path)          │
                            ▼                                       │
                            reactorfs_enumerate(pathPtr, len)       │
                                       │                            │
                                       ▼                            │
                          ┌────────────────────────────┐            │
                          │  reactorfs.dll  (Rust)     │            │
                          │  jwalk::WalkDir            │            │
                          │  → Vec<FsEntry>            │            │
                          │  → mem::forget on Vec and  │            │
                          │    on every UTF-16 name    │            │
                          └────────────┬───────────────┘            │
                                       │ FsResult { *mut, count }   │
                                       ▼                            │
                            for i in 0..count:                      │
                              new string((char*)NamePtr, 0, NameLen)│
                              Path.Combine(parent, name)            │
                              DateTime.FromFileTimeUtc(ticks)       │
                                       │                            │
                            finally: reactorfs_free_result(result)  │
                     └──────────────────────────────────────────────┘
```

Inputs:
- For NetPulse: nothing user-controlled (the system kernel is the upstream of TCP/UDP/IF tables). The strings the framework returns to the user (interface aliases) are configured by the local administrator.
- For ReactorFiles: `path` comes from the UI / breadcrumb / file-tree state, all driven by the desktop user. (The threat model assumes the desktop user is trusted.)

---

## 3. Trust boundaries crossed

| Boundary | Direction | Trust assumption being made |
|---|---|---|
| Managed C# ↔ `iphlpapi.dll` (Windows) | call out / data in | The OS DLL is fully trusted; struct layouts match the documented `MIB_*` shapes; field offsets used by `IpHelper` match the running Windows version. |
| Managed C# ↔ `reactorfs.dll` (Rust `cdylib` shipped in the same drop) | call out / data in | The DLL is part of the sample build; trusted *as a binary* but the marshaling boundary still has to be safe even if it returns garbage (which it can if the path is invalid UTF-16). |
| Filesystem ↔ `reactorfs` | side effect | jwalk reads metadata of arbitrary directories the desktop user can already read. No new privilege. |
| Sample → developer who copy-pastes | semantic only | The whole reason this chunk exists: a security-incorrect idiom in a sample becomes a memory bug in a real app. |

The chunking doc explicitly carves out: "Native FFI (Rust `reactorfs`, `iphlpapi`) — Trust the binary; review the marshaling boundary." That is exactly what is reviewed below.

---

## 4. Asset inventory

| Asset | Why it matters |
|---|---|
| Process integrity of any app that copy-pastes from these samples | A bad `MarshalAs`, a missed `GC.KeepAlive`, an off-by-one on an `IntPtr` walk, or a missed `Free*` becomes an arbitrary read / arbitrary write / heap corruption in **the copy-paster's** product. The threat is reputational (Reactor's samples are the canonical "this is how you do it" reference) and direct (real users of those products). |
| Heap of NetPulse / ReactorFiles processes | The samples themselves are not security-critical, but a heap overflow / UAF in a Reactor sample is still a Reactor incident. |
| OS handles allocated by `iphlpapi` (`MIB_IF_TABLE2` returned via `GetIfTable2` and freed via `FreeMibTable`) | If we lose track on an error path, the process leaks until exit — bounded but a real liveness asset for a long-running poll loop. |
| Rust-side allocations (`Vec<FsEntry>` and per-name `Vec<u16>`) | Each `enumerate_*` call leaks one outer Vec and N inner Vecs if the C# `finally` is bypassed. |

Capabilities:
- These samples do not gain new privilege; everything they expose is already accessible to the user with managed APIs (`System.Net.NetworkInformation.IPGlobalProperties`, `Directory.EnumerateFileSystemEntries`). The samples reach for native APIs purely for performance / stress-testing the reconciler.

---

## 5. STRIDE table

| # | Cat | Threat | Attacker model | Impact | Likelihood | Current mitigation | Finding |
|---|---|---|---|---|---|---|---|
| 1 | T (Tampering) | `MIB_IF_ROW2_SIZE = 1352` is a hardcoded constant; `iphlpapi` may grow the row in a future Windows update or pad it differently on ARM64. | Reading a copy-pasted snippet on a future OS | Misaligned reads → garbage interface stats; in the worst case reading past the table buffer if the kernel returns rows of size ≠ 1352 → out-of-bounds read of the AllocHGlobal block. | Medium for sample stability; Low for an exploit (read of process-private memory only) | None — the value is a literal with a comment. | **F1 (Medium).** |
| 2 | T | `byte* basePtr = (byte*)table + 8` skips the `NumEntries` header by an assumed 8-byte padding that is correct on x64 but is **not documented** in the public `MIB_IF_TABLE2` struct (the struct is declared as `ULONG NumEntries; MIB_IF_ROW2 Table[ANY_SIZE];` and the alignment of the row array depends on its largest member). | OS update / different arch | Same as F1. | Low | None — comment claims "padding to 8" without checking `MIB_IF_ROW2`'s natural alignment requirement. | **F2 (Medium).** |
| 3 | I (Info-disclosure) | `new string((char*)(row + IF_ROW2_OFFSET_ALIAS), 0, 256)` reads exactly 256 chars and only then trims `'\0'`. The Win32 `MIB_IF_ROW2.Alias` field is `WCHAR[IF_MAX_STRING_SIZE + 1]` = 257 chars but the OS guarantees null-termination only within those 257. If a future row layout omits the trailing null, the string returned will contain whatever bytes follow in the buffer (still inside the AllocHGlobal block, but it could be alias data of *another* row → low-tier info disclosure between interfaces in the same process). | Pathological / future OS layout | Information leak inside the same trust domain; not cross-process. | Low | None | **F3 (Low).** |
| 4 | T | `result[i] = ... row[5]` — `MIB_TCPROW_OWNER_PID` is documented as 6 DWORDs but the *managed equivalent* (`MIB_TCPROW_OWNER_PID` in `iphlpapi.h`) is unchanged for >15 years. Risk of size drift is low but nothing validates the buffer size against `count * 24 + 4`. | OS drift | Out-of-bounds read inside the AllocHGlobal block → garbage state values, possibly process-internal data. | Low | None | **F4 (Low).** |
| 5 | D (DoS) | `Marshal.AllocHGlobal(size)` uses an `int size` returned from the kernel. On a system with very many TCP connections, `int size` is a signed 32-bit value used as a byte count; will overflow well before any realistic attack but if `size` is negative the AllocHGlobal will throw, which is then caught upstream by `NetworkMonitor`'s blanket `catch { }` (NetworkMonitor.cs:71/84/122/167). | OS misbehavior | Silent loss of telemetry rather than a crash. | Low | Blanket `catch` swallows but allocates again next tick. | **F5 (Info).** |
| 6 | T | `if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR) return [];` (IpHelper.cs:58) — but on the *probe* call the kernel may also return `NO_ERROR` and have written nothing to `size`. The code then `AllocHGlobal(0)` and proceeds. `AllocHGlobal(0)` returns a non-null pointer in CRT semantics and `Marshal.ReadInt32` of it is undefined. | OS | Process crash on the probe path. | Low | Inside try/finally → buffer freed; but `Marshal.ReadInt32(buf)` of a non-zero zero-size block reads adjacent heap. | **F6 (Low).** |
| 7 | T (memory-safety) | `fixed (char* pathPtr = path)` in `FileSystemService.EnumerateNative` pins for the duration of the P/Invoke call, which is correct. But the returned `FsResult` references native-allocated memory; the comment in the Rust side says the C# caller owns and must free it. **The C# code does the right thing** — `try { ConvertResult } finally { reactorfs_free_result }`. No `GC.KeepAlive(path)` is needed because the `fixed` block ends before `ConvertResult` runs (the path is no longer needed). Confirmed safe. | — | — | — | Pattern is correct. | **No finding** (positive). |
| 8 | T (memory-safety) | `new string((char*)src.NamePtr, 0, (int)src.NameLen)` (FileSystemService.cs:90). `NameLen` is a `uint` cast to `int`; for `NameLen` ≥ 2^31 this becomes negative and `new string(...)` throws `ArgumentOutOfRangeException`. A hostile DLL could trigger this; an honest one cannot (filename UTF-16 lengths are bounded by `MAX_PATH`-ish). Within the chunk's "trust the binary" assumption this is fine, but a copy-paster might point this code at a *different* native producer. | Hostile DLL | Crash. | Low (within trust model) | None — straight cast. | **F7 (Low).** |
| 9 | T (memory-safety) | `Marshal.ReadInt32(buf)` reads `count` from the AllocHGlobal block before the bounds-checked walk. There is no upper-bound check on `count` against `(size - 4) / 24`. If the kernel returns inconsistent `count` and `size` (or if the pointer is later passed to a snippet that doesn't have the kernel as the producer), the loop reads past `buf + size`. | OS misbehavior or copy-paste with a different producer | OOB read of the AllocHGlobal block tail. | Low (kernel won't lie); Medium for copy-paste hazard. | None | **F8 (Low–Medium).** |
| 10 | D | NetPulse's `NetworkMonitor` swallows *all* exceptions with bare `catch { }` (NetworkMonitor.cs:71, 84, 122, 167). If any of the unsafe walks corrupts state silently (e.g. negative size, OS mismatch on a future build), the user sees blank charts and no log entry. | Defensive | Repudiation / debuggability. | Medium | None | **F9 (Low).** |
| 11 | T (memory-safety, **FFI cross-allocator**) | `enumerate.rs:54-68`: `name_utf16.as_ptr(); std::mem::forget(name_utf16);` then later `Vec::from_raw_parts(...)` in `ffi.rs:51-57`. The `Vec<u16>` was allocated on **Rust's global allocator** (`std::alloc`); reconstructing with `from_raw_parts` and dropping it is correct *as long as the free runs in the same DLL that allocated*. The C# side calls `reactorfs_free_result` from the same DLL → safe. **However**, this is a fragile sample idiom: a developer who copies the C# `ConvertResult` shape into a context where they `Marshal.FreeHGlobal(NamePtr)` (because "it's a name pointer") will free Rust's allocator pool with the CRT — undefined behavior. The boundary is correct here but is a copy-paste landmine. | Copy-paster | UB / heap corruption in their app. | High (this is exactly the threat the chunking doc highlights). | The Rust `# Safety` comments mention "must be a value previously returned by one of the enumerate functions"; the C# side has no comment at all. | **F10 (Medium).** |
| 12 | T (memory-safety) | `enumerate.rs:67`: `let ptr = entries.as_mut_ptr(); std::mem::forget(entries);` — Rust's `Vec` layout exposes `(ptr, len, cap)`. The C# struct only carries `(ptr, count)` — no `cap`. The `from_raw_parts(...)` call in `ffi.rs:51-57` uses `count` for both `len` and `cap`. If `Vec::with_capacity` ever over-allocates (which it can: `Vec::push` doubles capacity), `cap > len` and `Vec::from_raw_parts(ptr, len, len)` is **undefined behavior** — `Vec::from_raw_parts` requires the capacity argument to equal the actual allocation size used at construction. | Rust language semantics | UB on every `enumerate_*` call where the `Vec` ended up with extra capacity (i.e. essentially every call). | High — happens on the normal path, not an error path. | None — this is a real Rust-FFI bug. | **F11 (High).** |
| 13 | T (memory-safety) | Same shape as F11 for the per-entry `Vec<u16>` name buffers (`enumerate.rs:32`: `name_utf16: Vec<u16> = ... .collect()`). `collect::<Vec<u16>>()` from an `Iterator` of unknown size uses `Vec::extend`, which over-allocates. The free path (`ffi.rs:53-56`) reconstructs with `(name_len, name_len)` — same UB as F11 but more frequent (one per file in every directory listing). | Rust language semantics | UB; in practice the global allocator may tolerate `cap == len` reconstruction if the allocator's de-allocator only inspects the start pointer and the bucket, but this is allocator-dependent and not part of Rust's contract. | High | None | **F12 (High).** |
| 14 | T | `to_filetime_ticks` (enumerate.rs:6-13): `d.as_nanos() as u64 / 100 + EPOCH_OFFSET`. `as_nanos()` returns `u128`; the cast `as u64` truncates. Pre-Windows-epoch dates (`Err(_)`) return 0; that's fine. But for any date past year 2554-ish (`u64` nanoseconds wrap), the cast silently wraps and produces garbage `FILETIME` ticks. Unrealistic today; flag as a future bug. | Time | None | Trivial | None | **F13 (Info).** |
| 15 | T | `FsEntry` (`types.rs`) is `#[repr(C)]` with field order `name_ptr, name_len, size, modified_ticks, is_directory, has_children`. The C# `FsEntry` (`NativeFs.cs:5-14`) declares matching field order and `[StructLayout(LayoutKind.Sequential)]`. **Critically, the C# version omits explicit padding/alignment.** The Rust struct has natural size 32 (8 + 4 + 4-pad + 8 + 8 + 1 + 1 + 6-pad) = 40 bytes on x64. The C# version with `Sequential` and default `Pack=8` is also 40 bytes. They agree on x64. On ARM64 with default Pack=8 they also agree. **No finding** but the test of agreement is implicit — if anyone ever changes the order or adds a field, the layouts can diverge silently. | Future-edit | Layout drift → reads wrong fields. | Low | None | **F14 (Info).** |
| 16 | T | `FsResult` (types.rs:21-24): `entries: *mut FsEntry, count: u32`. C# (`NativeFs.cs:16-21`): `nint Entries; uint Count`. On x64: 8 + 4 + 4 padding = 16 bytes both sides. Matches. | — | — | — | — | **No finding** (positive). |
| 17 | I | `chat/App/Notifications.cs:91-96` declares `[DllImport("user32.dll")] SetForegroundWindow` but Grep confirms it has **zero callers** in the repo. Dead P/Invoke is a small attack-surface increase: a future contributor wires it up unsafely; meanwhile it is one more declaration that AOT trim analysis must keep. | Maintenance | Low | Trivial | None | **F15 (Info).** |
| 18 | E (EoP) | None of the samples expose a P/Invoke surface to remote callers; no devtools tool routes to `IpHelper` or `reactorfs`. EoP is not in scope for this chunk. | — | — | — | — | — |
| 19 | R (Repudiation) | The Rust side has no logging at all; the C# side has only the bare `catch { }`. A struct-layout mismatch on a future Windows build will silently produce garbage in the UI rather than logged. | Diagnosability | Low | None — flagged as F9. | — |

---

## 6. Findings

### F1 — `MIB_IF_ROW2` size & field offsets are hardcoded constants
**Severity:** Medium (correctness + future-OS landmine)
**Location:** `samples/apps/netpulse/Native/IpHelper.cs:19-29` and the dereferences at lines 143-157.

```csharp
const int MIB_IF_ROW2_SIZE = 1352;
const int IF_ROW2_OFFSET_INDEX = 8;
const int IF_ROW2_OFFSET_ALIAS = 28;       // WCHAR[257]
const int IF_ROW2_OFFSET_TYPE = 1128;
…
byte* row = basePtr + (long)i * MIB_IF_ROW2_SIZE;
uint type = *(uint*)(row + IF_ROW2_OFFSET_TYPE);
```

The structure is computed by the kernel; the offsets are a snapshot of `iphlpapi.h` at one moment in time. They are correct on x64 Windows 10/11 *today*. Any of the following will desynchronize:
- A future OS build adds a field anywhere before `OutOctets` (offsets shift).
- ARM64 padding differences if Microsoft ever changes the struct.
- A user running on a Windows server SKU with different ifheader macros.

When the offsets desync, the code reads adjacent rows' bytes and produces wrong telemetry without error.

**Recommendation:** Either (a) declare the actual `[StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct MIB_IF_ROW2` and let the marshaler compute offsets, or (b) at minimum, sanity-check `((MIB_IF_TABLE2*)table)->NumEntries * MIB_IF_ROW2_SIZE + 8 ≤ <some upper bound>` before the loop, and add a comment that the offsets came from `iphlpapi.h` on `_WIN32_WINNT` ≥ N. This is a sample — copy-pasters will inherit the constants.

### F2 — 8-byte header skip on `MIB_IF_TABLE2` is alignment-implicit
**Severity:** Medium
**Location:** `samples/apps/netpulse/Native/IpHelper.cs:139` (`byte* basePtr = (byte*)table + 8;`).

The comment says "ULONG NumEntries (4 bytes) + padding to 8 = 8 bytes header." That works because `MIB_IF_ROW2` contains `ULONG64`/`UINT64` fields whose natural alignment is 8. It is implicit; if anyone touches the row layout this skip is wrong. Recommend computing as `IntPtr.Size`-aware or replacing with a real struct layout.

### F3 — Alias buffer read can spill past the WCHAR[257] field if a future row omits null-termination
**Severity:** Low
**Location:** `samples/apps/netpulse/Native/IpHelper.cs:157`.

```csharp
string alias = new string((char*)(row + IF_ROW2_OFFSET_ALIAS), 0, 256).TrimEnd('\0');
```

Reads exactly 256 chars then trims. If the kernel ever populates without a terminator, the next 256 chars after the field will be returned to the UI (still inside the table buffer, but not the intended field). Use `Marshal.PtrToStringUni((IntPtr)(row + IF_ROW2_OFFSET_ALIAS))` (which respects the null) or read up to the first `'\0'` manually with a 257-bound.

### F4 — TCP/UDP row strides are magic numbers
**Severity:** Low
**Location:** `samples/apps/netpulse/Native/IpHelper.cs:72,112` (`i * 24` and `i * 12`).

`MIB_TCPROW_OWNER_PID` and `MIB_UDPROW_OWNER_PID` are well-baked structures that won't change, but again, a real `[StructLayout(LayoutKind.Sequential)] struct MIB_TCPROW_OWNER_PID` would let the compiler size them and would be a much better thing for a developer to copy.

### F5 — `int size` from kernel is not validated before `AllocHGlobal`
**Severity:** Info
**Location:** `samples/apps/netpulse/Native/IpHelper.cs:60,100` (`Marshal.AllocHGlobal(size)`).

If the kernel ever returns a negative or zero `size` on the probe call, AllocHGlobal will throw or return a useless pointer that `Marshal.ReadInt32` will then dereference. Validate `size >= 4`.

### F6 — Probe-call `NO_ERROR` is treated identically to `ERROR_INSUFFICIENT_BUFFER`
**Severity:** Low
**Location:** `samples/apps/netpulse/Native/IpHelper.cs:58,98`.

The probe is supposed to set `size` to the required buffer size and return `ERROR_INSUFFICIENT_BUFFER`. The code accepts `NO_ERROR` from the probe too. If `NO_ERROR` is returned but the table happens to contain zero rows, `size` may not be reliably set and the subsequent `AllocHGlobal(size)` is undefined.

### F7 — `(int)src.NameLen` cast is unchecked
**Severity:** Low
**Location:** `samples/apps/reactorfiles/Services/FileSystemService.cs:90`.

```csharp
var name = new string((char*)src.NamePtr, 0, (int)src.NameLen);
```

For `NameLen ≥ 2^31` this becomes negative and throws. Within the trust model the producing DLL will never emit such a length, but a copy-paster who points this loop at a different native producer inherits the unchecked cast. Add `if (src.NameLen > short.MaxValue) continue;` or similar.

### F8 — No upper-bound check on `count` against the AllocHGlobal block size
**Severity:** Low (current); Medium (copy-paste hazard)
**Location:** `samples/apps/netpulse/Native/IpHelper.cs:66-80, 106-117`.

```csharp
int count = Marshal.ReadInt32(buf);
…
for (int i = 0; i < count; i++) { uint* row = (uint*)(ptr + i * 24); … }
```

`count` is sourced from the same buffer the kernel filled. There is no `4 + count * 24 ≤ size` check. The kernel will not lie, but a developer copying this loop into a context where the producer is *not* `iphlpapi` (say, an IPC channel or a deserialized snapshot) inherits an OOB read.

### F9 — All polling failures are silently swallowed
**Severity:** Low
**Location:** `samples/apps/netpulse/NetworkMonitor.cs:71, 84, 122, 167`.

`catch { /* swallow polling failures */ }`. If a struct-layout mismatch (F1/F2) ever produces a `NullReferenceException` from a bad pointer, or a `Marshal.AllocHGlobal` throws because of F5, the user sees a blank chart with no log. Recommend at minimum logging via `System.Diagnostics.Trace.WriteLine` once per kind of failure.

### F10 — FFI cross-allocator hazard is undocumented on the C# side
**Severity:** Medium (copy-paste hazard)
**Location:** `samples/apps/reactorfiles/Native/NativeFs.cs:23-33`, `samples/apps/reactorfiles/Services/FileSystemService.cs:43-77`, `samples/apps/reactorfiles/Native/reactorfs/src/ffi.rs:33-62`.

The Rust side has `# Safety` comments saying "must be a value previously returned by one of the enumerate functions." The C# side has no comment at all explaining that:
- `result.Entries` and `FsEntry.NamePtr` are **Rust global-allocator** memory.
- They must be freed only by `reactorfs_free_result`.
- Calling `Marshal.FreeHGlobal(result.Entries)` would corrupt heaps.
- The struct is *not* a tagged owner — there is no way to call `Free` twice safely.

A developer copying the C# pattern into a project where they think `IntPtr` means "I own this with the CRT" is exactly the threat the chunking doc warns about.

**Recommendation:** Add a doc comment on `FsResult` and on `reactorfs_free_result` explaining the allocator boundary, and ideally introduce a `SafeHandle`-derived wrapper around `FsResult` that calls `reactorfs_free_result` in `ReleaseHandle`. That would also make the `try/finally` cleanup automatic.

### F11 — `Vec::from_raw_parts(entries, count, count)` is UB unless `cap == count`
**Severity:** **High** (memory-safety, normal-path)
**Location:** Allocator side: `samples/apps/reactorfiles/Native/reactorfs/src/enumerate.rs:21,66-68,82,114-116`. Free side: `samples/apps/reactorfiles/Native/reactorfs/src/ffi.rs:44-46`.

```rust
// allocator (enumerate.rs:66-68)
let count = entries.len() as u32;
let ptr = entries.as_mut_ptr();
std::mem::forget(entries);

// freer (ffi.rs:44-46)
let entries = unsafe {
    Vec::from_raw_parts(result.entries, result.count as usize, result.count as usize)
};
```

`Vec::from_raw_parts(ptr, length, capacity)` requires `capacity` to be the **actual capacity used at allocation time**. `Vec::push`-built vectors generally have `cap > len` (the doubling growth strategy). Calling `from_raw_parts(p, n, n)` when the original `cap` was, say, 16 and `len` was 11, and then dropping the reconstructed Vec, tells the allocator to free `n * sizeof(FsEntry)` bytes when it actually allocated `cap * sizeof(FsEntry)` bytes. This is **undefined behavior** per the documented contract of `Vec::from_raw_parts`: *"capacity needs to be the capacity that the pointer was allocated with."*

In practice, on Rust's default global allocator (`std::alloc::System` → MSVC CRT on Windows), the deallocator inspects the pointer and uses the bucket metadata, which **usually** tolerates cap-mismatch silently — but this is allocator-specific behavior, not a Rust contract. Switching to `mimalloc`, `jemalloc`, or a future `std::alloc::System` revision can turn this into a heap corruption immediately.

**Recommendation:** Use `Vec::into_raw_parts` (nightly) **or** use `Box<[FsEntry]>` (which has no capacity field): call `let boxed: Box<[FsEntry]> = entries.into_boxed_slice();` and pass `Box::into_raw(boxed)`; reconstruct with `Box::from_raw(slice_from_raw_parts_mut(ptr, count))`. Pair with the C# `count`. This produces a tight allocation of exactly `count * sizeof(FsEntry)` and the round-trip is well-defined.

### F12 — Same UB on every per-entry name `Vec<u16>`
**Severity:** **High** (memory-safety, normal-path)
**Location:** `samples/apps/reactorfiles/Native/reactorfs/src/enumerate.rs:32, 52-54, 95-102`. Free side: `samples/apps/reactorfiles/Native/reactorfs/src/ffi.rs:50-58`.

```rust
let name_utf16: Vec<u16> = file_name.encode_utf16().collect();
…
let name_ptr = name_utf16.as_ptr();
let name_len = name_utf16.len() as u32;
std::mem::forget(name_utf16);
```

`encode_utf16().collect::<Vec<u16>>()` does not guarantee `cap == len`. The reconstruction in `ffi.rs:51-57` again uses `(name_len, name_len)`. Same UB as F11, multiplied by entry count.

**Recommendation:** Same — use `Box<[u16]>` for names (`name_utf16.into_boxed_slice()`), or store both `len` and `cap` in `FsEntry` (the latter would also need C# struct changes). Box-of-slice is the cleanest.

### F13 — `to_filetime_ticks` truncates `u128` nanoseconds to `u64`
**Severity:** Info (year-2554 problem)
**Location:** `samples/apps/reactorfiles/Native/reactorfs/src/enumerate.rs:6-13`.

`d.as_nanos() as u64` silently wraps in ~year 2554. Use `u128` arithmetic or `checked_div`/`saturating_*`.

### F14 — Struct layouts agree by happy coincidence; nothing enforces it
**Severity:** Info
**Location:** `samples/apps/reactorfiles/Native/NativeFs.cs:5-21` paired with `samples/apps/reactorfiles/Native/reactorfs/src/types.rs`.

C# `[StructLayout(LayoutKind.Sequential)]` and Rust `#[repr(C)]` agree on x64 today. There is no compile-time check. Recommend one of:
- A unit test on the Rust side that prints `std::mem::size_of::<FsEntry>()` and `offset_of!`s, plus a C# unit test asserting `Marshal.SizeOf<FsEntry>()` equals 40, run as part of CI.
- Or a tiny `reactorfs_layout_check()` FFI that returns `(size_of::<FsEntry>(), offset_of!(FsEntry, name_len), …)` for the C# side to assert at startup.

### F15 — Dead `SetForegroundWindow` P/Invoke in chat sample
**Severity:** Info
**Location:** `samples/apps/chat/App/Notifications.cs:91-96`.

Declared but Grep finds zero callers. Either wire it up or remove it. (As a sample, leaving an unused P/Invoke is a small but real teaching liability — readers will think it's load-bearing.)

---

## 7. Open questions

1. **Are these samples shipped in the documentation set / "copy this" gallery, or are they purely internal stress tests?** The severity of F10–F12 hinges on this. If the sample-app code is pointed-to from public docs as reference material, F11 and F12 graduate from "samples have UB" to "we are teaching UB." If they are stress-test apps only used by the Reactor team, the urgency is lower (but they are still live UB).
2. **Is there an integration test that runs `reactorfs` on a directory with thousands of entries under a non-default Rust allocator (e.g., Valgrind/Miri)?** Miri would catch F11/F12 on the first run. Worth wiring `cargo +nightly miri test` into CI.
3. **For NetPulse's hardcoded `MIB_IF_ROW2` offsets, is there a Reactor team policy for "samples may pin to a specific OS struct layout"?** If yes, document at the top of `IpHelper.cs`. If no, replace with a `[StructLayout]` declaration.
4. **Is `Notifications.PInvoke.SetForegroundWindow` left over from an earlier branch?** If so, delete in the same commit that removes its caller.

---

## 8. Out-of-scope referrals

- **`src/Reactor.Interop.WinForms/**`** — covered in Chunk 19 (next chunk). HWND lifetime, COM apartment crossings — not duplicated here.
- **`samples/apps/regedit/Services/RegistryService.cs`** — uses managed `Microsoft.Win32.Registry`, no P/Invoke. Out of this chunk; if a separate "regedit privilege model" review is desired it would be a one-off, not part of the chunked plan.
- **The `monaco-editor` worker JavaScript bundles** (`*.worker-*.js`) matched the regex but are minified third-party JS. They are part of the WebView2 surface, which is a separate consideration (sample-app-level `WebView2` review is not in the chunking doc; flag for a future "WebView in samples" review if desired).
- **NetPulse polling cadence and reconciler-stress concerns** (50 ms timers on background threads calling setState) — that is a Chunk 14 (reconciler) concern about thread-safety / re-entrancy on hot paths, not a native-interop concern. Listed here only because `IpHelper` is the data source.

---

## 9. Summary of severities

| Severity | Count | IDs |
|---|---:|---|
| High | 2 | F11, F12 |
| Medium | 3 | F1, F2, F10 |
| Low | 5 | F3, F4, F6, F7, F8 |
| Info | 5 | F5, F9, F13, F14, F15 |

The two High findings are both Rust-side and both about cross-allocator FFI lifetime: the `Vec::from_raw_parts(p, n, n)` reconstruction violates `Vec`'s documented capacity contract. Fixing them is mechanical (`Box<[T]>` instead of `Vec<T>` across the boundary) and removes the most plausible class of bug a copy-paster would inherit unchanged.
