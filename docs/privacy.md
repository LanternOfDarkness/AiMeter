---
title: AiMeter Privacy Policy
permalink: /privacy/
---

# AiMeter Privacy Policy

_Last updated: November 3, 2026_

AiMeter is a Windows desktop widget that shows your usage limits for AI services
(Claude and OpenCode). It is published by VeLab. This policy explains what data the app
handles and where that data goes.

## Summary

- AiMeter has **no server, no accounts, no analytics and no telemetry**.
- The developer **does not collect, receive or store** any of your data.
- Everything AiMeter stores stays **on your computer**.
- AiMeter talks only to the services you sign in to: `claude.ai` and `opencode.ai`.

## Data the app handles

| Data | Purpose | Where it is kept |
|---|---|---|
| Sign-in session (cookies) for claude.ai / opencode.ai | Lets the app fetch your usage after you sign in through the built-in browser window | Local WebView2 profile on your PC |
| Usage figures (percent used, reset times, balances) | Shown in the widget and used for quota alerts | Memory, and the local settings file |
| Settings (layout, colors, selected metrics, startup option) | Remembering your preferences | Local `settings.json` |
| Diagnostic logs (errors, fetch failures, session expiry) | Troubleshooting | Local log files, kept on your PC only |

AiMeter never sees or stores your password. You type it into the provider's own sign-in
page, which runs inside the app's embedded Microsoft Edge WebView2 browser.

### Storage location

- Installer version: `%AppData%\AiMeter\`
- Microsoft Store version: the app's private package data folder
  (`%LocalAppData%\Packages\<AiMeter package>\`), which Windows removes when you uninstall
  the app.

## Network connections

AiMeter connects only to:

- `https://claude.ai`: sign in and read your Claude usage limits.
- `https://opencode.ai`: sign in and read your OpenCode usage and balance.

These requests go straight from your computer to those services. They are covered by
the services' own privacy policies:
[Anthropic](https://www.anthropic.com/legal/privacy) and [OpenCode](https://opencode.ai).
AiMeter is not affiliated with or endorsed by Anthropic or OpenCode.

## Sharing

AiMeter does not sell, share or send your data to the developer or to any third party.

## Deleting your data

- Sign out of an account in AiMeter's settings to remove its session.
- Uninstall the app. For the installer version, also delete the `%AppData%\AiMeter`
  folder to remove settings, logs and sessions.

## Children

AiMeter is not directed at children and does not knowingly handle children's data.

## Changes

If this policy changes, the new version will be published at this address with a new
"Last updated" date.

## Contact

Questions or concerns: open an issue at
<https://github.com/LanternOfDarkness/AiMeter/issues>.
