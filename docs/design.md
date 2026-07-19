# AI Usage Monitor

## Design Document

**Version:** 1.0

## Overview

AI Usage Monitor is a lightweight Windows desktop application that
provides a real-time overview of AI service usage limits.

The application is designed to stay unobtrusive while remaining
instantly accessible. It consists of a small floating widget and a
system tray application that continuously monitors usage limits across
multiple AI providers.

The initial release focuses on Claude, but the architecture is designed
to support additional providers such as ChatGPT, OpenAI (API), Gemini, Grok,
OpenRouter, DeepSeek, Kimi, Cursor, GitHub Copilot, ZenMux, NeuralWatt,
and others.

## Goals

-   Lightweight background application
-   Minimal resource usage
-   Modern Windows UI
-   Always-visible usage information
-   Extensible provider architecture
-   Highly customizable dashboard
-   Automatic updates without user interaction

## High-Level Architecture

``` text
+----------------------------+
|        Floating Widget     |
+----------------------------+

+----------------------------+
|       System Tray App      |
+----------------------------+

             │

+----------------------------+
|      Provider Manager      |
+----------------------------+

             │

   +---------+---------+
   |         |         |
 Claude   OpenAI   Gemini
 Provider Provider Provider

             │

      Usage Information

             │

+----------------------------+
|     Local Cache / Config   |
+----------------------------+
```

## Main Components

### 1. Tray Application

Responsibilities:

-   Start with Windows
-   Refresh providers periodically
-   Store cached data
-   Display notifications
-   Open Settings
-   Show/Hide widget
-   Manual Refresh

Context menu:

``` text
Refresh Now
Show Widget
Settings
About
Exit
```

### 2. Floating Widget

Properties:

-   Always On Top (optional)
-   Draggable
-   Snap to screen edges
-   Click-through mode (optional)
-   Acrylic / Mica background
-   Adjustable opacity
-   Remember position
-   Auto-hide support (future)

## Widget Layout

Two layout modes, selected in Settings (or via the widget's own toggle button/context menu):

**Compact** — a free-floating, draggable strip of thin bars, one row per metric:

``` text
┌───────────────────────────────────┐
│ Claude Session   ████████░░  74%  5h  │
│ Claude Weekly    ██████████  100% Week│
│ OpenCode Rolling ████░░░░░░  42%  2d  │
└───────────────────────────────────┘
```

Each row shows the metric name, a quota-fill bar (colored by remaining %), the remaining
percentage, and a short reset countdown (`5h`, `2d 3h`).

**Taskbar** — docks to the bottom of the work area at taskbar height, one narrow
vertical-fill column per metric, growing horizontally as metrics are added:

``` text
┌────┬────┬────┬────┐
│CSE │CWK │OCR │OCW │
│▓▓▓▓│▓▓▓▓│░░▓▓│▓▓▓▓│
│   ▏│   ▏│   ▏│   ▏│
└────┴────┴────┴────┘
```

Each column shows a 3-letter metric code, a bottom-up quota-fill bar, and a thin
time-until-reset sliver hugging its right edge; percentage and reset time appear on hover via
tooltip. `Left` is user-draggable horizontally; the dock's `Top` and height are recomputed
from the taskbar's detected thickness.

When no provider is logged in at all, both modes replace the metrics with a single
"Log in required" surface (a lock glyph, plus text in Compact) that opens Settings on click.

### Color States

    Remaining Color
  ----------- ----------
     75--100% Green
      50--75% Blue
      25--50% Orange
      10--25% Red
       0--10% Dark Red
      Unknown Gray

## Claude Metrics

-   Claude Messages
-   Claude Max Usage
-   Claude Fable Usage

## Customization

Users can:

-   Select which metrics are visible
-   Reorder metrics
-   Choose Compact or Taskbar layout
-   Resize widget
-   Adjust opacity
-   Toggle labels, timers, icons, and percentages
-   Choose theme and accent color

Example:

``` text
✓ Claude Messages
✓ Claude Max
✓ Claude Fable
☐ GPT-4
✓ GPT-5
☐ Gemini Flash
✓ Cursor Fast
```

## Provider Architecture

``` csharp
public interface IProvider
{
    string Name { get; }

    Task<IReadOnlyList<UsageMetric>> GetMetricsAsync();
}
```

## Refresh

-   Automatic refresh every 60 seconds
-   Manual refresh
-   Cached values displayed while refreshing

## Notifications

Configurable notifications for:

-   Low remaining quota
-   Quota exhausted
-   Quota reset

## Technology Stack

-   .NET 9
-   WPF
-   MVVM
-   Dependency Injection
-   JSON configuration
-   Local encrypted credentials

## Future Enhancements

-   Multiple widgets
-   Historical charts
-   Usage trends
-   Multiple accounts
-   Cloud sync
-   Plugin marketplace
-   Compact tray popup
-   **ChatGPT provider** — track ChatGPT Plus/Pro usage limits alongside Claude and OpenCode.
-   **Reorder metrics** — user-set ordering of metrics in the widget (up/down or drag).
-   **Self-contained installer** — see `docs/plans/2026-07-15-taskbar-mode-docking-installer-design.md`
    for details (bundles the .NET runtime so no network access is needed at install time).

### Delivered

-   **Compact multi-column bars** — configurable bars-per-column that wraps overflow into
    additional columns; plus per-widget bar-width and cell-gap sliders.
-   **Font size setting** — customizable widget text size (Compact rows, Taskbar column
    labels/numbers, the latter clamped to the docked column height).
-   **Hide numeric values toggle** — drops the always-visible percentage/reset text in both
    layouts, leaving just the name/code and the quota-colored bar; full numbers remain on hover.
-   **Per-metric custom label** — override a metric's displayed name (Compact mode).
-   **Per-metric color customization** — user-chosen bar colors, falling back to the
    quota-threshold palette when set to "Auto".

## Design Principles

-   Minimalistic
-   Fast
-   Extensible
-   Provider-independent
-   Fully customizable
