# Native DM capture (debug only — not in release)

v1.0.0 ships **Send DM** via IMVU `__makeRequest` / conversation messages API.

During development, IMVU Next's WebView header had a **Capture DM click** button. It hooked:

- `addMessagesWindow` / `addWindow` / `createChildContext` / `__selectConversation`
- `__request` / `__makeRequest` / `__queueRequest` / `__pumpRequest`
- `fetch` / `XMLHttpRequest` / `IMVU.XMLHttpRequest`

JS lived in `scripts/Imvu/open-native-dm.js` as `__imvuStartDmCapture`, `__imvuPollDmCapture`, `__imvuStopDmCapture` (removed before v1.0.0). C# was `CaptureDmBtn` on the IMVU header in `MainWindow.xaml` and handlers in `MainWindow.WebView.cs`.

Restore from git history **before tag v1.0.0** if you need to capture another IMVU client call the same way. Do not put the button back in a public installer.
