# Windows installer and Explorer acceptance checks

Build with `installer/build-installer.ps1`. The result is
`Lantoss-Setup-1.9.0-x64.msi`. These steps require an ordinary Windows desktop;
an offscreen UI export and an extracted MSI are not a substitute for them.

1. Exit the older app from the Windows tray. Install as the intended Windows
   account, select the Explorer feature and optionally Desktop and Startup.
   Confirm the per-user location, Start menu entries and Installed apps entry.
2. Open Lantoss and approve its scoped Private-network firewall setup if asked.
   Pair another PC on the same trusted Wi-Fi. Both apps need to be open.
3. In Windows 11 Explorer, right-click a file and choose **Share with Lantoss →
   the other PC** directly in the main menu. Verify it queues a transfer to that PC
   and waits for receiver approval. Check Unicode, spaces, ampersands and long
   paths. Also test an existing unsent draft: direct Explorer sends must keep it.
4. Test **Choose a PC…** with no recipient yet. It must prepare the file and ask
   for a recipient, without silently sending. Test an unpaired or blocked target
   and verify it is removed from the submenu after the app updates its menu.
5. Test a mixed selection of files and folders in one transfer, including Unicode.
   Windows 11 supports up to 256 items within the 96 KiB request budget. Check
   larger requests fail clearly rather than silently dropping items. Windows 10
   retains the classic 15-selection fallback. Test a running app and a cold launch.
6. Close the window while receiving/syncing. With the tray option enabled, work
   continues. Double-click the icon to reopen; **Exit Lantoss** must stop sharing.
   Turn the tray option off and confirm closing the window exits instead.
7. Enable launch at sign-in and automatic server startup in Settings, save, then
   sign out/in. Confirm the tray icon, receiving listener and browser page.
   Test startup disabled and Windows Task Manager's Startup-app disable control.
8. Stop sharing intentionally and verify it stays stopped during that app run.
   Reopen or choose **Start server** to start it again. No background service runs
   before sign-in or after the user signs out.
9. Exit Lantoss, uninstall from Installed apps, and confirm its shortcuts,
   current-user Explorer verbs and Run value are gone. Confirm that received
   files, metadata, linked folders, conflict copies and retained versions remain.
10. Reinstall/repair and a future major upgrade must preserve data and avoid
    duplicate menus. Test MSI cancellation and rollback in a disposable VM.

11. On a fresh Windows 11 VM, test the development-publisher consent, declining,
    UAC cancellation and successful trust. Confirm only the embedded certificate
    enters Local Machine Trusted People; no root trust is added. The signed sparse
    package must register for the installing user, not SYSTEM or another account.
    Repeat repair, feature removal/re-addition and 1.8-to-1.9 upgrade. Refresh the
    Explorer session if its old menu remains cached; no global menu hacks are used.
12. Inspect Get-AppxPackage -Name Lantoss.ExplorerIntegration and its installed
    manifest. Full uninstall must unregister it before deleting its external DLL.
    Trust persists intentionally for other Lantoss installations. After all these
    development builds are removed, the matching certificate can be removed by its
    exact thumbprint in certlm.msc / Trusted People / Certificates.

The package installs a native DLL through a COM surrogate and no machine-wide
Windows service. The private IPC channel remains current-user-only. Do not relax
its ACL to make a restricted host pass. The native DLL and sparse package are
development-signed; the MSI is unsigned. Physical two-PC behavior, actual sign-in,
publisher consent and live Explorer registration require a normal target desktop.

## 1.9 local address, notes and sync acceptance

Use disposable sample folders on two updated PCs and a phone on trusted home
Wi-Fi. These steps have not been completed on physical devices by the build host.

1. Leave discovery/browser sharing on and open http://share-wollvo.local on the
   phone and both PCs without adding a port. Confirm the correct local page and
   Other PCs picker. Open each named PC and confirm its distinct files/notes.
2. Start both apps together repeatedly. Only one may claim the common name;
   both unique device URLs must remain usable. Exit its owner, then confirm
   another PC takes over after goodbye or lease expiry. Repeat an abrupt exit
   and Wi-Fi reconnect. Check Windows's own browser can resolve its local name.
3. Reserve port 80 with another server before starting Lantoss. Confirm the
   visible fallback status and direct QR work; Lantoss must not stop the other
   server or advertise a no-port URL it cannot serve. Repeat with discovery off,
   blocked multicast and guest isolation; never claim these networks passed.
4. Browse a shared subfolder on phone and PC. Add/remove a sample file on its
   host, press Refresh and confirm the listing changes without a page reload.
   Refresh during an in-flight load, navigate while a request is pending, and
   check selected upload files/drafts survive. Try an unavailable/removed space.
5. Share Unicode/multiline text and a URL from the phone, This PC and the other
   PC. Confirm correct receiving boards and persistence after restart. Incoming
   notes must preserve a draft; only HTTP/HTTPS gets an explicit Open link action.
   Repeat with optional code protection on and after session revocation.
6. Link sample folders. Verify additions, modifications, conflicts and offline
   recovery in both directions. Leave a large unchanged folder idle and inspect
   CPU/disk in Task Manager, then edit a few files and confirm timely convergence.
   Use Sync now to force a full checksum pass. Locked files, watcher overflow,
   unavailable roots and sleep/resume must recover without losing content. Keep
   the existing no-deletion propagation and retained-version behavior.
