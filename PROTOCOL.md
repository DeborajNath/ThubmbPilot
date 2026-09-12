# ThumbPilot protocol v8

TCP 45832 carries TLS 1.2/1.3 with newline-delimited UTF-8 JSON. Frames are bounded to 4096 bytes including newline; server read/write deadlines are five seconds. The Android client sends heartbeats while idle. The PC certificate fingerprint is pinned on the phone; initial discovery/pairing is trust-on-first-use on a trusted LAN.

## Discovery and pairing

- Phone finds PC: UDP 45833 query `LOCALMOUSE8:` followed by a 32-character nonce. Reply: `type:localmouse`, `version:8`, PC name, certificate fingerprint in `id`, and matching nonce.
- PC finds phone: UDP 45834 query `LOCALMOUSEPHONE8:` plus nonce. Foreground phone replies with `type:localmouse-phone`, name, public signing key and matching nonce.
- PC invitation: `type:localmouse-invite`, `version:8`, name, PC fingerprint and random invitation. The invitation expires and can be consumed only once by the matching phone key.
- Existing connection hello: `type:hello`, `version:8`, 64-hex credential `token`, phone `name`.
- New pairing also includes `pair:request`, `publicKey`, `proof`, and `invitation` (empty when phone-initiated). The ECDSA/SHA-256 proof covers `LocalMouse8\n{fingerprint}\n{invitation}\n{token}`. Phone-initiated pairing asks for PC approval; a PC-initiated invitation already represents local initiation and requires approval on the phone.
- Approval lasts at most 60 seconds, saves the credential and enables input. Welcome includes version, PC name, fingerprint `id` and `inputEnabled`. Saved phones may reconnect without another approval.

Internal wire prefixes and storage identifiers keep the old LocalMouse name for compatibility.

## Authorized requests

| Type | Fields / effect |
| --- | --- |
| `ping` | Returns `pong` and `inputEnabled`. |
| `move` | Integer `dx`, `dy`, each -512..512; relative mouse movement. |
| `scroll` | Integer `dx`, `dy`, each -512..512; horizontal/vertical wheel units. |
| `click` | `button:left` or `button:right`. |
| `button` | Boolean `down`; connection-owned left-button drag. |
| `text` | Valid Unicode `text`, at most 256 UTF-16 units per frame. |
| `key` | Valid key name and array of unique supported modifier names. Includes system media/volume keys. |
| `action` | Allowed `action`: brave, calculator, notepad, explorer, lock, sleep, restart, shutdown. Sleep/restart/shutdown require `confirmed:true`. |

Input is authorized and locally gated before execution. Commands return acknowledgements (including input status; action failures may include a message). Pausing, revocation, timeout and disconnection release held mouse buttons. Arbitrary shell commands and raw virtual-key values are not exposed.

The bounded Android input queue coalesces adjacent motion while preserving click/key barriers. Old motion expires, drag releases are protected from expiry, and typing batches fail together rather than partially enqueue. The client does not replay uncertain typing after reconnect. It allows one request in flight at a time, favouring simple ordered behaviour over maximum throughput.

Implementation authority: desktop `ConnectionServer.cs`, `ActionProtocol.cs`, `KeyboardInput.cs`, `PairingApproval.cs`, and mobile `LanConnection.kt`, `InputQueue.kt`, `PhoneIdentity.kt`. Smoke tests cover the main protocol invariants; this document is a summary, not a generated schema.
