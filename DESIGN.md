---
name: iLovbee Studio
description: Precise, native Windows workbench for configuring the B87 keyboard.
colors:
  ink: "#20242B"
  muted: "#55606D"
  primary: "#185ABD"
  surface: "#F7F8FA"
  border: "#D5DBE3"
  keyboard-bed: "#E3E7ED"
  pending: "#DCEAFF"
  knob: "#465365"
  white: "#FFFFFF"
typography:
  body:
    fontFamily: "Windows system UI"
    fontSize: "14px"
    lineHeight: 1.6
  page-title:
    fontFamily: "Windows system UI"
    fontSize: "28px"
    fontWeight: 600
  compact-label:
    fontFamily: "Windows system UI"
    fontSize: "12px"
  key-legend:
    fontFamily: "Windows system UI"
    fontSize: "12px"
  test-log:
    fontFamily: "Cascadia Mono"
    fontSize: "14px"
rounded:
  key: "5px"
  card: "8px"
  keyboard: "12px"
  knob: "18px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "20px"
  page: "32px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
    padding: "9px 16px"
  button-standard:
    textColor: "{colors.ink}"
    padding: "9px 16px"
  keyboard-key-selected:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
    rounded: "{rounded.key}"
  keyboard-key-pending:
    backgroundColor: "{colors.pending}"
    textColor: "{colors.ink}"
    rounded: "{rounded.key}"
  assignment-strip:
    backgroundColor: "{colors.white}"
    rounded: "{rounded.card}"
    padding: "16px"
  navigation:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.ink}"
---

# Design System: iLovbee Studio

## Overview

**Creative North Star: “The Precise Keyboard Workbench”**

This Windows-native interface treats the physical keyboard as the work surface. A quiet porcelain canvas and graphite text keep attention on the key map; restrained blue marks selection and the next committed action. Traditional Chinese labels are direct and compact, with status and controls carrying the workflow instead of promotional copy.

Fluent controls retain their familiar keyboard focus and interaction states. The integrated title bar keeps native caption controls, while the large keyboard, current-versus-proposed assignment strip, and staged changes make editing legible at a glance.

**Key Characteristics:**
- Professional Traditional Chinese desktop UI
- Native WinUI 3 controls and Fluent focus affordances
- Blue means selected; pale blue means pending
- Large physical keyboard map with an explicit knob test affordance

## Colors

Porcelain neutrals frame a single clear blue accent; color communicates state rather than decoration.

### Primary
- **Workbench Blue** (`{colors.primary}`): Selects the active physical key and accents the apply action.

### Neutral
- **Graphite Ink** (`{colors.ink}`): Main labels and key legends.
- **Slate Muted** (`{colors.muted}`): Supporting labels and secondary status.
- **Porcelain Surface** (`{colors.surface}`): Main application and title-bar surface.
- **Divider Gray** (`{colors.border}`): Fine structural borders.
- **Keyboard Bed** (`{colors.keyboard-bed}`): Recessed field behind the key map.
- **Pending Blue Wash** (`{colors.pending}`): Background of a key with a staged edit.
- **Knob Graphite** (`{colors.knob}`): Physical rotary-control marker.
- **White** (`{colors.white}`): Key faces and assignment surface.

### Named Rules
**The State Color Rule.** Blue identifies the selected key; pale blue identifies a pending mapping. Keep those meanings distinct.

## Typography

**Display Font:** Windows system UI (Segoe UI)
**Body Font:** Windows system UI (Segoe UI)
**Label/Mono Font:** Windows system UI for labels; Cascadia Mono for the key-test log.

**Character:** Familiar Windows typography keeps the workbench native and readable. Compact labels support a dense device editor, while page titles establish a clear hierarchy.

### Hierarchy
- **Page title** (semibold, 28px): Screen heading.
- **Body** (system default, 14px, 1.6 line height): Supporting text and device status.
- **Key action** (system default, 16px): Current assignment in the selected-key strip.
- **Label** (system default, 12px): Field labels, helper text, and key legends.
- **Test log** (Cascadia Mono, 14px): Keyboard test events.

## Layout

The integrated 48px title bar sits above a responsive Fluent NavigationView. Its pane adapts between expanded and compact widths; the content area keeps device status at the top, scrollable page content in the middle, and operation feedback at the bottom. Use a 32px page inset and the observed 4/8/12/16/20px spacing steps. On the remapping page, keep the complete keyboard map prominent and the full-width assignment surface immediately below it; scale the map down as needed rather than cropping it.

## Elevation & Depth

Depth comes from tonal surfaces and borders: the keyboard sits on a cool gray bed, key faces and the assignment strip use white, and a thin divider separates device status. No custom shadow tokens are defined in the implementation. Native controls provide their own focus and interaction treatments.

## Shapes

Use crisp, gently rounded geometry: 5px key corners, 8px assignment surface corners, a 12px keyboard bed, and a circular 18px knob. Thin gray borders separate unselected keys; selected or pending keys use the blue accent border, with a stronger 2px border for pending changes.

## Components

### Buttons
- **Character:** Familiar, tactile WinUI controls with concise Traditional Chinese action labels.
- **Primary:** The apply action uses the native accent button style; ordinary buttons use 16px horizontal and 9px vertical padding.
- **Hover / Focus:** Preserve native Fluent hover, pressed, and keyboard-focus states.

### Inputs / Fields
- **Style:** Use native WinUI text, combo, and suggestion controls for search, layer choice, and assignment.
- **Focus:** Keep Fluent keyboard focus visible and preserve native interaction behavior.

### Navigation
- **Style:** Native NavigationView with five labeled destinations and familiar iconography. Expanded width is 210px and compact width is 48px; its pane switches with available width.

### Keyboard Map
- **Style:** A complete, clickable physical layout on a rounded gray bed. White key faces carry compact legends; the selected key is blue, while a changed key is pale blue with a blue outline.
- **Behavior:** Selecting a key updates the adjacent current/proposed assignment strip. The rotary knob opens key testing and does not imply an undocumented mapping.

## Do's and Don'ts

### Do:
- **Do** retain native WinUI controls and their Fluent focus behavior.
- **Do** distinguish the selected key from staged edits using the established blue and pale blue states.
- **Do** keep current assignment and proposed action visibly adjacent in the assignment strip.

### Don't:
- **Don't** assign a key meaning to the knob from its physical position; it opens key testing.
- **Don't** replace the neutral workbench with decorative dashboard cards or marketing copy.
