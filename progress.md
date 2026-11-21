# Labster rebrand progress

Branch `update/Labster-rebrand` · Base commit `2dd129b` · November 21, 2026

## 1. Brand assets

- [x] Used the official Labster SVG geometry for the web mark, favicon, desktop icon, and splash screen.
- [x] Applied the existing primary color `#8B1538` to the web mark and agent asset backgrounds.
- [x] Verified that the desktop foreground and splash preserve the web mark's geometry.

## 2. Web and logging terminal

- [x] Updated desktop and mobile navigation, login, terminal header, monitoring copy, and browser titles that displayed CLMS.
- [x] Kept the existing web theme and USeP institutional identification. The login retains the system descriptor.
- [x] Restored the existing terminal tab markup and state wiring so the three forms compile with their original submission handlers.

## 3. Desktop app

- [x] Set the display name to Labster in the project, settings window, enrollment page, and Windows and macOS status labels.
- [x] Matched the web's primary, primary dark, accent, foreground, surface, muted, and error colors in the agent resources. Preserved its OS dark-mode support.
- [x] Built the Windows agent and inspected the generated icon and splash artwork.
- [ ] Visually verify the installed icon and splash on Windows and macOS. Upgrade an enrolled workstation and verify reconnection with the existing registration.

## 4. Emails and documentation

- [x] Updated booking confirmation and update subjects and signatures. The email sender display name defaults to Labster. Deployments with an existing `Brevo:SenderName` override should set it to Labster.
- [x] Updated product-facing agent and operator documentation.
- [ ] Preview delivered booking, sign-in, and password-reset emails in a configured test deployment.

## 5. Release verification

- [x] Passed 23 JavaScript tests, SVG and XAML XML parsing, logo geometry checks, seven palette mappings, and `git diff --check`.
- [x] Repaired pre-existing build blockers in the logbook tuple assignment, three Razor authorization tags, and terminal markup and tab bindings. The existing authorization policies and form handlers are preserved.
- [x] Built the web app with zero errors. The build manifest includes the official SVG.
- [ ] Run the .NET suite in an environment that allows the test runner to connect to its test host. The test assembly compiled, but VSTest aborted after a 90-second test-host connection timeout.
- [ ] Smoke-test desktop and mobile layouts, sign-in, booking, student and visitor check-in and check-out, and agent connectivity. A loopback HTTP check was blocked by the workspace socket permissions (`WinError 10013`).

The compatibility scope preserves project names, namespaces, database and API identifiers, auth cookies and token settings, and agent package and enrollment identifiers. The branch's pre-existing untracked test artifacts were left untouched.

## Sidebar restyle

- Applied the maroon and white palette to the expanded desktop sidebar, collapsed rail, and mobile drawer. Active links use a solid white highlight, with translucent hover fills and white keyboard focus outlines.
- Added icons so Account settings and Application Registry remain identifiable in the collapsed rail.
- The web build passed with zero errors.
- Calculated contrast ratios of 9.28:1 for white and maroon, 6.39:1 for secondary text, and 6.89:1 for white text on the hover fill. Live browser verification remains open.

## Unit Monitoring navigation

- Removed the empty Instructors page and sidebar link.
- Moved Application Registry and Unit Registry into the Unit Monitoring tab bar. Each tab uses its existing permission, and the sidebar opens the first permitted tab.
- Added permission-protected redirects from the old registry URLs and updated the operator docs.
- The web build passed with zero errors. Live checks for tab wrapping, bookmark redirects, and accounts with different permissions remain open.
