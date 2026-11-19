---
title: AiMeter Privacy Policy
permalink: /privacy/
---

# AiMeter Privacy Policy

_Last updated: November 19, 2026_

AiMeter is a Windows desktop widget that shows your usage limits for AI services
(Claude and OpenCode). It is published by VeLab. This policy explains what data the app
handles and where that data goes.

## Summary

- AiMeter has **no server, no accounts, no analytics and no telemetry**.
- The developer **does not collect, receive or store** any of your data.
- Everything AiMeter stores stays **on your computer**.
- AiMeter talks only to the services you sign in to (`claude.ai` and `opencode.ai`) and
  to GitHub, to check for app updates.

## Data the app handles

| Data | Purpose | Where it is kept |
|---|---|---|
| Sign-in session (cookies) for claude.ai / opencode.ai | Lets the app fetch your usage after you sign in through the built-in browser window | Local WebView2 profile on your PC |
| Usage figures (percent used, reset times, balances) | Shown in the widget and used for quota alerts | Memory, and the local settings and cache files |
| Last successful usage snapshot (numbers, reset times, timestamp) | Lets the widget show the last known values while offline | Local `%AppData%\AiMeter\cache.json` |
| Settings (layout, colors, selected metrics, startup option) | Remembering your preferences | Local `settings.json` |
| Update state (last check time, skipped version, notification times) | Avoids repeated update checks and notifications | Local `%AppData%\AiMeter\update-state.json` |
| Diagnostic logs (errors, fetch failures, session expiry) | Troubleshooting | Local log files, kept on your PC only |

AiMeter never sees or stores your password. You type it into the provider's own sign-in
page, which runs inside the app's embedded Microsoft Edge WebView2 browser.

### Storage location

All of the above lives in `%AppData%\AiMeter\`.

## Network connections

AiMeter connects only to:

- `https://claude.ai`: sign in and read your Claude usage limits.
- `https://opencode.ai`: sign in and read your OpenCode usage and balance.
- `https://api.github.com/repos/LanternOfDarkness/AiMeter/releases/latest`: check
  for a new version. The request carries the app version in the `User-Agent` and
  nothing else: no identifiers, cookies, settings or usage data. Release
  downloads come from GitHub, and nothing is downloaded until you approve the
  update.

These requests go straight from your computer to those services. They are covered by
the services' own privacy policies:
[Anthropic](https://www.anthropic.com/legal/privacy), [OpenCode](https://opencode.ai) and
[GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
AiMeter is not affiliated with or endorsed by Anthropic or OpenCode.

## Sharing

AiMeter does not sell, share or send your data to the developer or to any third party.

## Deleting your data

- Sign out of an account in AiMeter's settings to remove its session.
- Delete `AiMeter.exe` (or uninstall it, if you used the installer), then delete the
  `%AppData%\AiMeter` folder to remove settings, logs and sessions.

## Children

AiMeter is not directed at children and does not knowingly handle children's data.

## Changes

If this policy changes, the new version will be published at this address with a new
"Last updated" date.

## Contact

Questions or concerns: open an issue at
<https://github.com/LanternOfDarkness/AiMeter/issues>.
