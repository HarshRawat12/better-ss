# Third-party notices

Better SS itself is available under the [MIT License](LICENSE). The following
software is separate from Better SS and remains under its own license.

## FFmpeg

Screen recording and video editing use `ffmpeg.exe` and `ffprobe.exe` from the
[FFmpeg project](https://ffmpeg.org/). The setup script downloads the Windows
"release essentials" build provided by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/).
That build is licensed under GNU GPL version 3 or later. A copy of its license is
kept at `BetterSS/Tools/FFmpeg-LICENSE.txt` and distributed beside packaged builds.

The FFmpeg binaries are intentionally not stored in this repository. Running
`scripts/setup-ffmpeg.ps1` downloads them from the provider and verifies the
provider-published SHA-256 checksum before installation.

FFmpeg source code and corresponding-source information are available from the
[FFmpeg download page](https://ffmpeg.org/download.html) and the build provider.

## User-provided audio

Capture sounds are optional. Audio files used during development are excluded
from the repository because no redistribution license has been established for
them. Contributors and users are responsible for ensuring that any audio they
add or distribute is properly licensed.
