---
name: iLovbee Studio
description: A precise Windows workbench for configuring the B87 keyboard.
colors:
  ink: "#192231"
  muted: "#536174"
  blue: "#1754B5"
  primary: "#215CC5"
  surface: "#F4F6F9"
  side: "#E9EEF5"
  card: "#FFFFFF"
  field: "#FFFFFF"
  white: "#FFFFFF"
  line: "#CBD4E1"
  hover: "#DDE6F1"
  selected: "#DCE9FF"
  selected-ink: "#124998"
  bed: "#DCE3EC"
  danger: "#A51E29"
  dark-ink: "#EDF2F8"
  dark-muted: "#B4C0D1"
  dark-blue: "#8DBAFF"
  dark-surface: "#111823"
  dark-side: "#17212E"
  dark-card: "#1B2635"
  dark-field: "#131D2A"
  dark-line: "#45546A"
  dark-hover: "#2B3B50"
  dark-selected: "#243D60"
  dark-selected-ink: "#C9DFFF"
  dark-bed: "#0D1520"
  dark-danger: "#FFB0B8"
typography:
  body:
    fontFamily: "Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft JhengHei, sans-serif"
    fontSize: "15px"
    fontWeight: 400
    lineHeight: 1.6
  page-title:
    fontFamily: "Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft JhengHei, sans-serif"
    fontSize: "28px"
    fontWeight: 650
    lineHeight: 1.35
  compact-label:
    fontFamily: "Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft JhengHei, sans-serif"
    fontSize: "12px"
  key-legend:
    fontFamily: "Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Microsoft JhengHei, sans-serif"
    fontSize: "11px"
  test-log:
    fontFamily: "Cascadia Mono, Consolas, monospace"
    fontSize: "14px"
rounded:
  key: "5px"
  chip: "5px"
  field: "7px"
  button: "7px"
  navigation: "9px"
  tile: "10px"
  card: "12px"
  keyboard: "12px"
  knob: "50%"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  section: "24px"
  page: "32px"
components:
  navigation:
    backgroundColor: "{colors.side}"
    textColor: "{colors.ink}"
    rounded: "{rounded.navigation}"
    padding: "10px 14px"
    height: "44px"
  navigation-selected:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.selected-ink}"
    rounded: "{rounded.navigation}"
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
    rounded: "{rounded.button}"
    padding: "8px 15px"
    height: "38px"
  keyboard-key-selected:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
    rounded: "{rounded.key}"
  keyboard-key-pending:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.selected-ink}"
    rounded: "{rounded.key}"
  card:
    backgroundColor: "{colors.card}"
    rounded: "{rounded.card}"
    padding: "22px"
  field:
    backgroundColor: "{colors.field}"
    textColor: "{colors.ink}"
    rounded: "{rounded.field}"
    padding: "7px 10px"
    height: "38px"
---

# Design System: iLovbee Studio

## Overview

**Creative North Star: “The Precise Keyboard Workbench”**

This is a quiet, neutral Windows workbench for configuring a physical B87 keyboard. Graphite text and opaque tonal surfaces keep the device map and controls legible. Blue distinguishes selection and action states. Traditional Chinese labels are concise and operational.

The native shell keeps its own caption controls in a separate 36px strip. The work area follows the selected Windows theme by default, with a persisted local choice of system, light, or dark. The same component roles and spacing apply in both palettes.

**Key Characteristics:**
- Opaque, neutral work surfaces with blue state color
- Eight always-labeled navigation destinations
- Windows UI sans typography with Traditional Chinese fallbacks
- Physical keyboard map and adjacent editing controls remain central

## Colors

The palette uses cool blue-gray neutrals in light mode and deep blue-charcoal neutrals in dark mode. The blue accent remains reserved for selection, focus, and primary actions.

### Primary
- **Workbench Blue** (`{colors.blue}`): Key hover and active editing accents in the light palette. Use the brighter dark counterpart for the same state roles.
- **Action Blue** (`{colors.primary}`): Primary action controls and checks.

### Neutral
- **Graphite Ink** (`{colors.ink}`): Main text in light mode.
- **Slate Muted** (`{colors.muted}`): Supporting text and subdued interface labels.
- **Porcelain Canvas** (`{colors.surface}`): Main page background.
- **Sidebar Mist** (`{colors.side}`): Navigation and native caption strip background.
- **White Surface** (`{colors.card}`): Header, cards, and feedback surface.
- **Field White** (`{colors.field}`): Inputs and key faces.
- **Structural Line** (`{colors.line}`): Borders and dividers.
- **Hover Wash** (`{colors.hover}`): Hover surfaces.
- **Selected Wash** (`{colors.selected}`) with **Selected Ink** (`{colors.selected-ink}`): Selected navigation and staged mapping state.
- **Keyboard Bed** (`{colors.bed}`): Recessed field under the keyboard map.
- Dark mode uses the matching `dark-*` tokens for ink, surfaces, borders, selected state, keyboard bed, and danger status.

### Named Rules
**The Opaque Surface Rule.** Every app surface declares a solid theme color so native window composition cannot wash out interface text.

**The State Color Rule.** Blue marks active selection, focus, and primary action. The pale selected wash distinguishes staged mapping changes.

## Typography

**Display Font:** Segoe UI Variable Text (fallback: Segoe UI)
**Body Font:** Segoe UI Variable Text, with Segoe UI and Microsoft JhengHei UI/Microsoft JhengHei fallbacks
**Label/Mono Font:** Windows UI sans for labels and Cascadia Mono (fallback: Consolas) for test logs.

**Character:** Familiar Windows sans forms keep this dense utility readable. Traditional Chinese fallbacks are explicit. Tabular numerals support device readings.

### Hierarchy
- **Page title** (semibold, 28px, 1.35): Main screen heading.
- **Section title** (semibold, 16–22px): Cards and secondary sections.
- **Body** (regular, 15px, 1.6): Primary interface text.
- **Compact label** (12px): Navigation context, supporting labels, and device details.
- **Key legend** (11px): Captions within the physical key map.
- **Test log** (Cascadia Mono, 14px): Keyboard test events.

## Layout

The desktop shell uses a 232px sidebar, a flexible work area, and a separate 36px native caption strip. At widths up to 1100px the sidebar becomes 200px and split content stacks. At 760px and below navigation becomes a four-column, two-row labeled grid above the content. Keep all eight destinations visible and labels readable. The main area has a status header, independently scrolling page body, and persistent operation feedback footer.

Use the 4/8/12/16/20/24/32px spacing rhythm. The keyboard map uses a 800×300 design coordinate space and scales down to fit its available width. Preserve opaque surfaces and avoid horizontal overflow at narrow widths.

## Elevation & Depth

The interface is flat and tonal rather than shadow-led. Surface contrast, thin borders, and the recessed keyboard bed establish grouping and depth. No custom shadow vocabulary is used.

## Shapes

Controls use compact rounded rectangles, with slightly more generous corners for cards and the keyboard bed. Keycaps remain small and distinct. The rotary marker is circular. Borders carry structure without ornamental outlines. Preserve the visible 3px focus outline and offset for keyboard interaction.

## Components

### Buttons
- **Character:** Compact Windows workbench controls with direct Traditional Chinese labels.
- **Primary:** Blue surface with contrasting text, 38px minimum height and 8px/15px padding.
- **Standard:** Opaque field surface, structural border, and 7px corners.
- **Hover / Focus:** Hover uses the tonal hover surface. Keyboard focus uses a 3px theme-colored outline with 3px offset.

### Inputs / Fields
- **Style:** Opaque field surface with structural border, 7px corners, and 38px minimum height.
- **Focus:** Shared visible 3px theme-colored outline.
- **Disabled:** Reduced opacity with default cursor for disabled action buttons.

### Navigation
- **Style:** Eight vertically stacked, icon-plus-text destinations in the desktop sidebar. Labels stay visible at all widths. Compact layout hides icons while retaining labels.
- **State:** Active destination uses selected wash and selected ink. Hover uses the hover surface.

### Cards / Containers
- **Corner Style:** 12px cards and 10px tiles.
- **Background:** Opaque card token with a thin structural border.
- **Shadow Strategy:** No custom shadows. Tonal layering defines the workbench.
- **Internal Padding:** 22px desktop cards, reduced to 16px on compact screens.

### Keyboard Map
- **Style:** Complete clickable physical layout on a 12px rounded keyboard bed. Keycaps use compact legends and 5px corners.
- **State:** Selected key uses primary blue. A staged edit uses selected wash, selected ink, and a stronger blue outline.
- **Behavior:** The selected key's current and proposed assignments remain available in the editing flow. The physical knob opens key testing. Its physical location does not establish a mapping.

### Theme Control
- **Behavior:** A labeled selector offers system, light, and dark modes. The choice is stored locally. System mode follows the Windows color preference.

## Do's and Don'ts

### Do:
- **Do** keep all app regions opaque and use the matching light or dark token set.
- **Do** keep all eight navigation labels visible, including in the compact grid.
- **Do** distinguish selected keys from staged edits with the established state colors.
- **Do** preserve the separate native caption strip and visible keyboard focus.

### Don't:
- **Don't** imply keyboard mapping from the knob's location. It opens key testing.
- **Don't** introduce translucent app surfaces that compromise text legibility.
- **Don't** replace the neutral workbench with decorative marketing panels.
