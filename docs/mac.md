# rexplayer on a Mac

rexplayer runs on macOS 12 Monterey and later, on Apple Silicon and on Intel Macs. It is the same
player as on Windows: the same menus, shortcuts (Cmd where Windows says Ctrl, Option where it says
Alt), settings and library. What is still Windows-only is listed in the [parity ledger](parity.md).

## Which download

Open the Apple menu, then **About This Mac**.

| It says | Download |
|---|---|
| Chip: Apple M1, M2, M3, M4 or later | `rexplayer-mac-osx-arm64.zip` |
| Processor: Intel | `rexplayer-mac-osx-x64.zip` |

It also shows the macOS version. Below macOS 12, see [an older Mac](#an-older-mac).

## Installing

1. Open the zip (double-click). You get **rexplayer.app**.
2. Drag rexplayer.app into **Applications**.

## Opening it the first time

rexplayer is not signed with an Apple Developer ID yet, so the Mac asks once before it opens it.
After that it opens like any other app.

**macOS 12 Monterey, 13 Ventura and 14 Sonoma**

1. In Applications, hold **Control** and click rexplayer, then choose **Open**.
2. Choose **Open** again in the box that appears.

**macOS 15 Sequoia and later**

1. Double-click rexplayer. The Mac says it cannot check it; choose **Done**.
2. Open **System Settings**, then **Privacy & Security**.
3. Scroll down to the message that rexplayer was blocked, and choose **Open Anyway**.
4. Confirm with your password or Touch ID, then choose **Open**.

If the Mac says the app "is damaged and can't be opened", it was downloaded in a way that marked
it quarantined. Open **Terminal** and run, once:

```sh
xattr -dr com.apple.quarantine /Applications/rexplayer.app
```

## Where rexplayer keeps its files

Settings, the library, bookmarks, history and logs are in
`~/Library/Application Support/REX/rexplayer` (the same files Windows keeps under
`%LocalAppData%\REX\rexplayer`). **Help, Show the log** opens the log folder.

## An older Mac

rexplayer needs macOS 12 or later, which is what .NET, the platform it is written in, supports.
Many Macs from 2012 to 2015 stop at macOS 10.15 or 11 officially, but can run macOS 12 with
OpenCore Legacy Patcher, a free community tool:

1. Check the Mac's model is on the patcher's
   [supported models list](https://dortania.github.io/OpenCore-Legacy-Patcher/MODELS.html).
2. Back up the Mac with Time Machine first.
3. Follow the patcher's own guide to install macOS 12 Monterey (newer versions are possible on
   many models, but Monterey is the lightest that rexplayer supports).

Macs from before 2012 cannot run macOS 12 at all, so rexplayer cannot run on them.
