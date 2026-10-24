# Windows preferences and onboarding source audit for follow-up #488

**Scope:** Source task #485.9, as a bounded inspection of the **existing native Windows** preferences, hotkeys, first run, About, What's New, updater, dialogs and validation. The separate execution-quality gates #485.12 and #485.10 remain necessary. This document records exactly what the source and tests claim; it does not turn unrun tests into successful execution or CI fixtures into approved images.

## The 15 native preference sections

`MainWindow.Preferences.ShowPreferencesAsync` constructs and routes all of these existing sections; `AppWindowTests.Shell.EveryPreferencesSectionShowsAloneAndTheThemeChosenIsKept` enumerates the picker, verifies one heading per page and captures a native screenshot per section.

| Section(s) | Implementation contract and existing checks | What is not certified by this review |
| --- | --- | --- |
| Playback; Intros and credits | Bounded numeric seek/jump inputs; saved audio/playback decisions, intro/credits toggles. `AppWindowTests.Preferences.PreferencesChangeTheSettingsAndAreKept` and `AppWindowTests.Transport` exercise key choices. | Full native keyboard and controller focus order |
| Video and pictures; Music and artwork | Separate image-effect/visualizer editors; localized defaults, generated-art controls and artwork invalidation. `AppWindowTests.Preferences.EveryBasicVisualizerSettingHasARealAutomationNameAndResetHelp` verifies labels and reset scope. | High-contrast preview pixels and low-end GPU performance |
| Performance | Manual and Energy Saver-aware modes with explanation of temporary Automatic choice. `DeviceProfileTests` and `EnergySaverModeTests` cover policy. | Physical-device power transition |
| Languages; Subtitles | User language preferences and bounded subtitle appearance, code pages and placement. `SubtitleLookTests` and `AppWindowTests.Preferences` cover core contracts. | Actual dual-track screen-reader/contrast review |
| Transcription | Model and download options live in a separate sub-editor. `TranscriptionPolicyTests` and transcription UI tests cover cancellation and gated behavior. | Every download, offline and storage-failure path on device |
| Window | Theme, always-on-top, shell-media and title timing controls. `AppWindowTests.Shell.EveryPreferencesSectionShowsAloneAndTheThemeChosenIsKept` proves the theme selection route. | Native Snap, DPI and title-bar behavior covered separately under #485.10 |
| Library; Metadata | Media opening/scan preferences; optional provider settings in `MainWindow.MetadataPreferences`. `MetadataPolicyTests` verifies opt-in and excluded-folder gates. | Full native network-consent and disconnected-drive interaction review |
| History and resume | History retention and resume preferences; `PlayerControllerTests`, `AppWindowTests` and library-history tests check underlying behavior. | Persistence/recovery on interrupted shutdown |
| Keyboard and mouse | The global/app hotkey split, reset/clear, conflicts and import/export are handled by `MainWindow.Shortcuts`; `ShortcutEditingTests`, `ShortcutRoutingTests`, `AppWindowTests.Shell` and `AppWindowTests.Preferences` cover editor and Save/Cancel hand-off. | Hardware keyboards with alternate layouts and assistive input |
| Opening files; Updates | New-window/queue behavior, update cadence/mode and disclosure of network-dependent operations. `AppWindowTests.Dialogs`, `UpdateFlowTests` and `UpdateViewTests` cover core states. | Native updater restart, metered network and installer hand-off |

The wide screen uses a searchable category rail and compact widths use a single section picker. `AppWindowTests.Preferences.PreferencesResizeKeepsTheCompactPickerAndRestoresTheWideCategoryRail`, `PreferencesSearchFindsWrappedLabelsChoicesAndClosedExpanderOptions`, and `PreferencesNavigationKeepsUnsavedValuesAndCancelDiscardsThem` check those transitions. The page's explicit Save/Cancel hint documents that **actions** such as downloads and update checks run when selected; ordinary settings changes remain deferred until Save. `PlayerSettings.Normalize` bounds stored settings.

## Other audited surfaces and corrections

| Surface | Existing source / regression contract | Action in #488 |
| --- | --- | --- |
| First run | `MainWindow.Preferences.GreetAsync`; `AppEndToEndTests` for persistent first-run choice | **Corrected:** background updates now default to **off**, are enabled only when selected, and the welcome text distinguishes offline playback from optional network lookups. `WelcomePrivacy` is available to UI Automation. Added default-off and deliberate-opt-in tests and a native test-fixture capture. |
| About | `MainWindow.About` offers version, local data/log paths, source attributions, What's New and update checks; `AppWindowTests.About` exercises the UIA surface | **Corrected:** removed the unconditional claim that nothing is sent. `AboutPrivacy` identifies the network disclosure for native automation. |
| What's New | `MainWindow.Preferences.ShowWhatsNewAsync` reads the bundled changelog for the installed version; `AppWindowTests.Dialogs.TheFirstStartOfANewVersionOffersWhatsNew` tests the upgrade notice and reopening from Preferences | No fabricated notes or stale version claims added |
| Updater | `MainWindow.UpdateFlow`, `MainWindow.UpdateView`, pure `UpdateFlowTests` / `UpdateViewTests`, and native `CheckingForUpdatesOpensTheUpdatePanel` cover idle/checking/available/downloading/paused/offline/metered/error/retry/ready/installer hand-off models | Optional network path remains explicit; final native failure, accessibility and deployment validation belongs to #485.12 |
| Dialogs and validation | `AppWindowTests.Dialogs` exercises concurrent-dialog rejection, cancel/invalid seek, keyboard sheet and named selectors. `AppWindowTests.Preferences` checks editor cancel/save and responsive navigation; `DetailOverrideTests` / `PendingDetailWritesTests` cover numeric/date validation and deferred writes | All destructive, unusual IME, screen-reader and keyboard focus paths still need the native gate |

## Residual quality requirements

- Native current-head Windows build and UI Automation, plus visual inspection of screenshot artifacts, high-DPI, high-contrast and keyboard/screen-reader review remain **open under #485.12**.
- Native shell and file-association behavior belongs to **#485.10**.
- CI captures are labeled as regression fixtures; no image is promoted to final-release website evidence under #468.
- Do not elevate the capability matrix or claim macOS parity from this Windows source review.

This completes the **source inventory and known-risk audit** of #485.9, not execution of its separate native quality or release gates. Every corrected statement has a native UI test authored in the same branch; results must be rechecked on the latest commit.
