# Validation — 2026-11-14

Windows GUI executable compiled with the Mono C# compiler against the .NET Framework 4.x API.
Storage and GUI checks were run with Mono and Xvfb on Linux. All 21 checks passed.

Verified: Japanese/multiline/emoji data round trip, atomic saves, previous-version backup, corrupt-primary recovery, preservation of unreadable input, invalid import version rejection, duplicate ID separation, monitor placement recovery, bundled fonts, automatic saves, window geometry and pin state persistence, close-to-hide, hidden-note restoration, and reload.

The preview is rendered by the actual app controls in that compatibility environment. Its first window uses a simulated activation event because the test display has no window manager. Windows font rasterization may differ.

Not verified on a Windows 11 machine: native IME conversion, tray and multi-instance behavior, sign-in autorun, mixed-DPI monitor behavior, and SmartScreen reputation. The executable is unsigned. This is the initial portable release.
