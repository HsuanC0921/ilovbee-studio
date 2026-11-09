# Settings workbench
Mode: Operate. Code-led refinement of the established B87 workbench, not a new brand.

User requirement: preserve every C# feature before UI overhaul. Sidebar labels must remain readable. Follow Windows theme with persistent manual light/dark selection. Unit, stress and security gates must pass before packaging the next release. No camera or real-device stress tests.

First viewport: B87 identity and labeled eight-destination navigation; connection and battery state; editable complete keyboard layout; current/proposed action editing; operation feedback. Blue selection remains the incumbent product language. All surfaces have opaque backgrounds so native compositing cannot destroy text contrast. Native caption area is separate from content, and narrow/zoomed windows retain text navigation.

Acceptance: sidebar text contrast >= 4.5:1 in both themes; no hidden labels or horizontal overflow at 1440, 1030 and 600 logical pixels; no document-height overflow from native caption offset; theme preference persists; all old workflows remain operable. Native WebView rendering and caption controls are verified separately from the browser's mocked Tauri shell.

No comp or raster assets. No font/brand invention. Windows UI sans typography with explicit Traditional Chinese fallbacks. The user's attached failed sidebar screenshot is the regression reference.
