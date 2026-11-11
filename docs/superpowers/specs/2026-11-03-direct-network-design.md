# Independent wireless KVM

Approved direction: use ADB to bootstrap, then run keyboard, pointer, clipboard and display reporting over an independent authenticated encrypted LAN connection. AP roaming must not require re-enabling wireless debugging while the daemon survives.

## Architecture

Keep existing USB loopback transport and clipboard protocols. Add a TLS 1.2 gateway bound to the PC IPv4 interface selected by routing to the device's Wi-Fi address. A random per-bootstrap token authenticates each channel before it can reach the local input or clipboard listener. The Android client pins the ephemeral server certificate obtained in a configuration file over the already authorized ADB channel. No plaintext fallback, certificate-store changes, APK, root or Shizuku.

ADB deploys a unique jar and mode-0600 configuration. `nohup setsid` with detached standard streams launches the daemon. A unique owner marker allows a newer launch to retire the prior daemon without process-name kills. An authenticated stop message stops a reachable daemon; ownership-checked ADB cleanup is a fallback. An unreachable daemon releases input and retires after 30 minutes without a session. Restarting the PC or Android requires bootstrapping again.

Each network session creates fresh input and clipboard state. Losing heartbeat closes the session and releases all input, but the outer daemon remains and reconnects. Reconnection never automatically takes control or replays input. Each accepted input connection has a new PC generation. Display geometry is reported by the daemon without shell polling. USB remains unchanged.

## UI and recovery

The wireless choice becomes independent Wi-Fi, with ADB described as the startup mechanism. An online USB target may bootstrap Wi-Fi too; subsequent input still uses the LAN. After successful bootstrap, ordinary network loss waits for the daemon rather than polling ADB or pushing another jar. Explicit retry reboots the connection workflow. Listener errors, routing errors, and startup timeouts must explain their stage. The PC interface address must remain reachable; PC address changes require reconnecting through ADB.

## Validation

Test real C#/Java TLS interoperability, bad pin/token rejection, concurrent-client bounds, gateway disposal, daemon reconnect/ownership/expiration, session generation separation, display reports, no ADB dependency after bootstrap, and USB regressions. Build matched exe/jar. On the target Android 16 tablet verify daemon survival after bootstrap ADB ends and wireless debugging is off, direct session recovery after a controlled socket break, and input readiness with geometry. AP roaming and prolonged physical use require explicit live evidence; do not infer them from loopback tests.

## Working state

Existing uncommitted Program.cs keyboard-state reconciliation belongs to the user and is preserved. Work uses codex/direct-network in the current checkout. Network tests need host network permissions; initial restricted baseline failed on denied loopback sockets, not assertions.
