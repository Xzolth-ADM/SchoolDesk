---
name: SchoolDesk
description: A calm, local workspace for the school day.
colors:
  primary: "#315A9B"
  primary-hover: "#264B88"
  primary-subtle: "#E7EDF7"
  neutral-bg: "#F4F6F8"
  surface: "#FFFFFF"
  subtle-surface: "#F0F2F5"
  text: "#20242A"
  muted: "#5F6875"
  border: "#D8DDE5"
typography:
  display:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "34px"
    fontWeight: 600
  title:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "17px"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI, sans-serif"
    fontSize: "13px"
    fontWeight: 400
rounded:
  row: "4px"
  control: "5px"
spacing:
  xs: "5px"
  sm: "8px"
  md: "16px"
  lg: "24px"
  xl: "32px"
components:
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "#FFFFFF"
    rounded: "{rounded.control}"
    padding: "12px 8px"
    height: "36px"
  button-secondary:
    backgroundColor: "{colors.subtle-surface}"
    textColor: "{colors.text}"
    rounded: "{rounded.control}"
    padding: "12px 8px"
    height: "36px"
  input-text:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.text}"
    padding: "9px 7px"
    height: "36px"
  navigation-item:
    backgroundColor: "transparent"
    textColor: "{colors.text}"
    rounded: "{rounded.control}"
    padding: "14px 10px"
    height: "42px"
  content-card:
    backgroundColor: "{colors.surface}"
    rounded: "{rounded.control}"
---

# Design System: SchoolDesk

## Overview

**Creative North Star: "The Clear Study Desk"**

SchoolDesk should feel like a clear, dependable place to check the school day and get work done. Its light Windows-native surfaces and French interface keep the application familiar and easy to scan, while a restrained blue marks the current location and the actions that matter.

The interface uses opaque, matte surfaces and thin borders instead of decorative effects. Liquid Glass, translucency, and blur are rejected. The dashboard leads with today's real schedule and next lesson; the agenda and document tools remain integrated in the same application window.

**Key Characteristics:**
- Light, matte Windows surfaces.
- A restrained blue accent for active navigation and primary actions.
- Clear hierarchy, compact controls, and modest corner rounding.
- Local-first school information with no invented timetable entries.

## Colors

Cool, quiet neutrals carry most of the interface; blue is reserved for navigation and primary actions.

### Primary
- **School Blue** (#315A9B): Primary buttons, selected navigation, and time-sensitive schedule details.
- **Deeper School Blue** (#264B88): Hover color for primary emphasis.
- **Blue Wash** (#E7EDF7): Selected navigation and other subtle active surfaces.

### Neutral
- **Window Mist** (#F4F6F8): Main application background.
- **Paper White** (#FFFFFF): Content surfaces, inputs, and menus.
- **Soft Slate** (#F0F2F5): Sidebar and secondary button surfaces.
- **Ink** (#20242A): Main text and control labels.
- **Slate Text** (#5F6875): Supporting text; maintains readable contrast on white.
- **Cool Border** (#D8DDE5): Surface and control outlines.

**The One Accent Rule.** Keep blue to active navigation, primary actions, and a small amount of schedule information; let neutral surfaces do the structural work.

## Typography

**Display Font:** Segoe UI (with sans-serif fallback)

**Body Font:** Segoe UI (with sans-serif fallback)

**Character:** Familiar Windows typography, using weight and size rather than decorative treatments to establish hierarchy.

### Hierarchy
- **Display** (600, 34px): Dashboard lead and primary page heading.
- **Title** (600, 17px): Panel titles and document titles.
- **Body** (400, 13px): Navigation, supporting information, and form labels.
- **Timer** (300, 54px): The remaining-time readout on the dashboard.

## Layout

The maximized application uses a persistent 238px left navigation rail and a flexible content area. The dashboard's main content is capped at 1180px and arranged in two columns; integrated pages use a single working surface with clear gutters. Common spacing steps are 8px, 16px, 24px, and 32px, with 5px gaps for tight control groups.

## Elevation & Depth

The system is flat by default and uses no shadows. Depth comes from opaque tonal surfaces, thin neutral borders, and spacing between working areas. Focus is communicated by a stronger accent border rather than elevation.

**The Flat Surface Rule.** Do not add a shadow, blur, or translucent layer to simulate depth.

## Shapes

Keep the geometry restrained: 5px corners for buttons and main surfaces, 4px for nested agenda rows, and square-edged text fields. Use thin borders to define surfaces; avoid pill-shaped controls and oversized rounding.

## Components

### Buttons
- **Shape:** Modestly rounded, 5px.
- **Primary:** School Blue with white text, 12px horizontal and 8px vertical padding, 36px minimum height.
- **Secondary:** Soft Slate with Ink text and a Cool Border outline.
- **Hover / Focus:** Hover reduces opacity and slightly strengthens the outline; keyboard focus strengthens the outline, using blue for the focus border.

### Cards / Containers
- **Corner Style:** 5px for main surfaces; 4px for nested agenda rows.
- **Background:** Paper White on the Window Mist background.
- **Shadow Strategy:** None; separate surfaces with a 1px border and spacing.
- **Internal Padding:** 14–18px for working panels; larger dashboard panels use 30–38px.

### Inputs / Fields
- **Style:** Paper White, Cool Border, square corners, and 9px by 7px internal padding.
- **Focus:** 2px School Blue border.
- **Disabled:** Reduced opacity; retain legible text and the control's layout.

### Navigation
- **Style:** Fixed left rail, 238px wide, on Soft Slate.
- **Default:** Transparent button surface with left-aligned text.
- **Active:** Blue text on Blue Wash.
- **Selection:** The current page is represented persistently; navigation stays inside the main application window.

## Do's and Don'ts

### Do:
- **Do** use opaque matte surfaces and thin neutral borders to define working areas.
- **Do** reserve School Blue for active navigation, primary actions, and restrained schedule emphasis.
- **Do** keep page workflows integrated in the main window.
- **Do** preserve real local timetable data and clearly present an empty schedule when none is configured.

### Don't:
- **Don't** use Liquid Glass, decorative translucency, blur, or shadow effects.
- **Don't** use low-contrast text on colored or selected surfaces.
- **Don't** add oversized rounded cards or pill-shaped controls.
- **Don't** invent timetable entries to fill empty states.
