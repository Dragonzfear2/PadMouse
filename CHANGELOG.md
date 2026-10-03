# Changelog

## 1.2.0

- **More controllers:** PlayStation (DualShock 4, DualSense, DualShock 3), Nintendo Switch Pro, Joy-Cons and most other USB or Bluetooth controllers now work, as well as Xbox. This uses the bundled SDL 2 library.
- **Follows the controller you're using:** with several controllers connected, PadMouse uses whichever one you last pressed a button on.
- **Button names match your controller:** the Settings window shows Cross/Circle/Square/Triangle, L1/R1/L2/R2, Share/Options on PlayStation, and the Nintendo equivalents.
- **Rumble and battery level** now work on PlayStation and Switch controllers too, where the controller supports them.
- **New Controllers page in Settings:** lists everything that's connected and shows which controller is in use.
- **Set-up wizard for unknown controllers:** press each button when asked, and the controller is remembered from then on. PadMouse offers this automatically when it finds a controller it doesn't recognise.

## 1.1.0

- **New Settings window:** dark theme with a sidebar.
  - Clickable controller picture.
  - Live stick visualiser.
  - Cursor target practice area.
  - Live preview of changes.
- **Profiles:** Desktop, Browser and Media are included. They switch automatically by app, or with View + RB / LB.
- **Automatic pausing:** PadMouse pauses while a fullscreen game is in front. You can also keep "never pause" and "always pause" app lists.
- **On-screen pop-ups:** shown when you switch PadMouse on or off, change profile, connect a controller, or the battery runs low.
- **Battery:** battery level now shows in the tray tooltip, with a low-battery warning.
- **Welcome guide:** a first-run guide labels every button.
- **Built-in installer:**
  - Installs per user, with no admin rights needed.
  - Adds a Start menu shortcut, plus an optional desktop shortcut.
  - Adds an entry in Settings → Apps, with an uninstaller.
- **In-app update check and one-click update:** uses GitHub releases.
- **Start with Windows:** can now start as administrator, using Task Scheduler. There's also Restart as administrator.
- **About page.**
- **Tray icon fixes:**
  - The icon is set before the tray entry is created, so it shows reliably.
  - Left-click opens Settings.
  - Running the exe again opens Settings.
- **Fixed:** pressing the on/off combo no longer briefly triggers the buttons it's made of.

## 1.0.0

- First version:
  - the controller works as a mouse
  - an on-screen keyboard
  - remappable buttons in a `.ini` file
  - a tray icon and an on/off combo
