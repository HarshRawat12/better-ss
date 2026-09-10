# Privacy

Better SS is designed to work without an account or online service.

## Data the app handles

- Screenshots and screen recordings explicitly started by the user
- Text extracted locally from a selected image
- Preferences such as hotkey, output folder, theme, and startup choice
- Temporary image files used for drag-and-drop
- Temporary video segments used while recording or exporting

## Where data goes

Captured media is copied to the Windows clipboard and/or written to folders on
the local computer. OCR runs through the Windows OCR APIs. Settings are stored in
`%LOCALAPPDATA%\Better SS\settings.json`.

Better SS does not implement telemetry, analytics, advertising, cloud upload,
remote OCR, user accounts, or background synchronization. FFmpeg is downloaded
only when a developer explicitly runs `scripts/setup-ffmpeg.ps1`; the app itself
does not download it.

## User controls

Users choose the screenshot save folder, can disable automatic saving, and can
manage the local drag cache from Preferences. Disabling automatic saving still
creates a local cache file when necessary to support file drag-and-drop.

Windows, destination applications, clipboard managers, backup tools, or folders
configured for cloud synchronization may independently process data after Better
SS places it on the clipboard or filesystem. Those systems are outside the app's
control.
