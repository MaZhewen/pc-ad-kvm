# Independent wireless KVM implementation plan

**Goal:** Keep KVM operational across wireless-debugging shutdown and recover after LAN interruptions without ADB.

**Architecture:** Independent pinned TLS gateway forwards two authenticated channels to existing loopback protocol endpoints. A detached Android daemon owns successive sessions, input cleanup, clipboard and geometry reports. The PC coordinator separates bootstrap from network recovery.

**Tech Stack:** C# 5 / .NET Framework, Java 8 / Android app_process, TLS 1.2, PowerShell builds.

## Work items

- [ ] Add DirectNetworkGateway.cs and PinnedTls.java with localhost TLS interop tests. Assert valid bytes pass, wrong pin/token fail, unauthorized candidates do not interrupt an established channel, disposal closes all sockets.
- [ ] Add DirectSessionConfig.java, DirectDaemon.java, DeviceGeometry.java and adapt Injector/ClipboardSession. Test config validation, reconnect cleanup, owner replacement, deadlines, geometry wire format. Preserve USB constructors and session behavior.
- [ ] Add DirectBootstrap.cs for device Wi-Fi address, PC route selection, unique push/config/launch, ownership-checked cleanup. Test serialized files and command arguments with fake ADB.
- [ ] Integrate gateway ownership and recovery in ConnectionCoordinator; permit USB bootstrap in wireless mode, avoid ADB polling after deployment, refresh generation on input reconnect, invalidate clipboard publication state.
- [ ] Add display-report and daemon-stop messages to Protocol and Watchers; continue existing Program geometry handling and preserve the preexisting keyboard fixes.
- [ ] Update connection UI and README to distinguish bootstrap from runtime, retry limits and firewall requirements.
- [ ] Run focused tests, relevant regression suites and matched build; inspect diff and perform independent review.
- [ ] Run live startup, ADB-independent readiness, disconnect/reconnect, and cleanup checks on the tablet. Record exactly which scenarios were exercised.

## Progress / decisions

- Initial device unavailable, subsequently connected as transport 1; Android 16, wlan0 IPv4 10.222.134.72, wireless debugging disabled, setsid/nohup present.
- Existing baseline tests first denied loopback socket access; rerun with host network permission.
- Subtasks: TLS gateway/client and Android daemon are delegated with non-overlapping ownership. Coordinator, launcher, docs and integration remain with primary agent.
