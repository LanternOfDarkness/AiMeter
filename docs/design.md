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
to support additional providers such as OpenAI, Gemini, Grok,
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

Each metric is displayed as an independent circular indicator.

``` text
┌───────────────────────────────┐

      ○          ○          ○
     74%        100%       42%

      5h         Week       2d

 Claude Msg   Claude Max   Claude Fable

───────────────────────────────

      ○          ○

     63%        91%

      4h         1d

 GPT-5       Gemini

└───────────────────────────────┘
```

Each indicator contains:

-   Circular progress ring showing **remaining quota**
-   Remaining percentage
-   Reset timer
-   Metric name
-   Optional provider icon

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
-   Choose grid, row, or column layout
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

## Design Principles

-   Minimalistic
-   Fast
-   Extensible
-   Provider-independent
-   Fully customizable
