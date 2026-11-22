# Look parity

rexplayer looks the same on Windows and on a Mac because both shells draw from one description of
the look, and the [capability matrix](capability-matrix.md) has a row for each screen (ADR-0020).

## The one description

All three live in `src/Rex.Media.AppCore/Design`, are pure and are fully tested.

| File | What it says |
|---|---|
| `DesignTokens.cs` | The numbers: ink and signal colours, corner radii, spacing, type sizes, motion timings |
| `DesignRoles.cs` | What each colour is for (canvas, surface, raised, hairline, text) in the dark and the light theme, and the stage's colours |
| `ShellScreens.cs` | Each screen's structure: the controls it always shows, in reading order, each with its Windows automation id and its kind (`icon`, `icon-toggle`, `accent-icon`, `text`, `accent-text`, `overlay-text`, `label`, `slider`) |

## Where each shell turns it into controls

- Windows: `src/Rex.Media.App/App.xaml` spells the roles as brushes and `src/Rex.Media.App/RexTheme.cs`
  projects the tokens; `MainWindow.xaml` gives every control its automation id.
- Mac: `src/Rex.Media.App.Mac/MacTheme.cs` reads the same tokens and roles and styles the AppKit
  controls; each control carries the same identifier as on Windows.

Neither shell writes a colour or a size of its own; a test holds `App.xaml` to `DesignRoles`.

## The Look rows

The matrix area "Look" has one row per screen: `LOOK-01` the welcome screen, `LOOK-02` the transport
and seek bar, `LOOK-03` the music stage and visualisations, `LOOK-04` the library, `LOOK-05` Up Next,
`LOOK-06` the settings pages, `LOOK-07` the tool windows. A behaviour row being verified says
nothing about how its screen looks; only a Look row does.

## How a Look row becomes verified

A Look row is `verified` on a platform only while a `[Capability("LOOK-…", platform)]` test there
proves both of these:

1. **Structure.** The running shell answers the automation command `ui.describe` with the screen's
   controls as `Id:kind;Id:kind;...`, and that text equals `ShellScreens.Describe(screen)`. A
   control added to, dropped from or moved in one shell fails the other until both agree.
2. **Picture.** CI saves a screenshot of the screen under the same name on both platforms, so a
   person can put the two side by side.

Until both hold, and until the screen has been looked at on a real Mac, the row stays `built`. A
test method may claim only a few rows, so one launch-and-quit test cannot verify a platform.

## Adding a screen

1. Describe it in `ShellScreens` (a property, added to `All`) with a new Look row in
   `docs/capability-matrix.json`, `built` on both platforms.
2. Give the controls the same automation ids on both shells, and make both answer `ui.describe` for
   it.
3. Add the structure test and the screenshot on each platform; then the row may say `verified`.

A screen changed on one shell is changed on the other in the same pull request, or its Look row
says `gap` with a note and an issue.
