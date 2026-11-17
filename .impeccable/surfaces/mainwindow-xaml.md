---
version: 1
slug: "mainwindow-xaml"
primary_target: "MainWindow.xaml"
related_targets: ["App.xaml","Views/AgendaWindow.xaml","Views/EditorWindow.xaml","Views/DocumentExplorerWindow.xaml","Views/QuickNotesWindow.xaml","Views/SettingsWindow.xaml"]
---

## Scope and mode

The SchoolDesk application shell and its integrated pages. Operate: students should move quickly between school workflows.

## Audience, job, and action

Students checking today's timetable and next lesson, organizing dated tasks, and creating or finding school documents. The primary action is to read the current school state, then navigate to the relevant workflow.

## Content and constraints

The dashboard uses the actual local timetable, week, task counts, and next lesson. The app is a Windows 11 WPF application; user data remains local, and all existing agenda, DOCX, notes, explorer, and settings actions must remain available.

## Direction and memorable moment

User-selected light, native Windows styling: opaque matte neutral surfaces, a restrained blue for current navigation and primary actions, compact rounded corners, and a persistent left navigation rail. On opening, the dashboard puts the date, school week, task counts, current event, and next lesson in one scan.

## Unresolved decisions

None for this redesign.
