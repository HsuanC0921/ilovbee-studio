# iLovbee Studio desktop

Mode: Operate. Code-led redesign; one shipped device class: Windows desktop.

## THESIS
A precise keyboard workbench: the actual B87 occupies the main workspace and selecting a physical key reveals its current and proposed action.

## OWN-WORLD
Neutral porcelain workspace, graphite navigation, restrained blue selection. Native Fluent form controls, crisp keycap geometry and Traditional Chinese labels. No marketing slogans or decorative dashboard cards.

## STORY
Connect USB, see the device and current map, select keys, search actions or compose shortcuts, stage changes across layers, undo/redo, inspect differences and apply with a complete backup and verified readback. Other pages expose lighting, Windows-reported Bluetooth battery, testing and profiles. Firmware research belongs only in developer documentation.

## FIRST VIEWPORT
Persistent device status and selected navigation. A large complete keyboard diagram with the knob at the physical Pause position. A full-width assignment strip below the keyboard makes the selected key, current mapping and next action readable at 1030px minimum width. The knob opens testing; its unknown mapping is never silently written as Pause or Mute.

## FORM
Windows-native WinUI 3/C#/.NET 10; custom integrated title bar and native caption controls. 8px spacing rhythm, 14px body, 28px page titles, 10–12px unscaled key legends. Native keyboard focus and selected states. Blue means selected; pale blue outlines pending changes. Resizing preserves the whole keyboard and allows page scrolling. Separate NativeAOT system-tray widget reads Windows battery without loading WinUI.

## FINISH
Verify build and 21 protocol/profile/editor tests, USB read-only probe, actual Bluetooth battery read and widget memory measurement. User requests avoiding repeated Computer Use. Use in-app RenderTargetBitmap captures and layout checks at 1320 and 1030 logical pixels, and all five page navigation. Fresh finishing review against these captures. No web detector: this is native WinUI. Document finished tokens after corrections.
