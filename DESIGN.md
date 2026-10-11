# Better SS desktop design system

## Direction

Apple-inspired precision for a native Windows productivity utility: compact typography, quiet neutral surfaces, grouped controls, rounded settings cards, vivid blue actions and optical glass on temporary overlays. The native application remains code-first WPF on .NET 8. Preserve capture coordinates, image pixels, commands, keyboard shortcuts, OCR, recording, drag/drop and settings.

Stitch project: `15132568099306375314`, **Better SS — Apple-inspired desktop & Liquid Glass**. Shared design system: `assets/160dcdf4c8694b73bde36b70557b2a35`, Fluent Cupertino Precision. Stitch generated the screenshot editor, preferences, floating overlays, video editor and companion utility sheets. Exported references live in `docs/stitch-ui/`. These are design references; native WPF code implements the product. Generated concepts containing unsupported controls or capability badges were omitted.

## Shared components

`BetterSS/UI.cs` owns tokens, typography and templates. `Motion.cs` owns finite WPF animation clocks. `GlassSurface.cs`, `LiquidLens.cs` and `LiveGlassBackdrop.cs` own material composition and sampling.

| Token | Light | Dark |
|---|---|---|
| Canvas | #F5F5F7 | #1C1C1E |
| Surface | #FFFFFF | #2C2C2E |
| Sidebar | #EEEEF2 | #232326 |
| Text | #1D1D1F | #F5F5F7 |
| Secondary text | #68686F | #AEAEB5 |
| Divider | #E0E0E6 | #404045 |
| Primary action | #0071E3, white text | #0071E3, white text |
| Selected tool/navigation | #0071E3, white text | #0071E3, white text |
| Selected segment | white, dark text | raised neutral, light text |

Use Segoe UI Variable with Segoe UI fallback, and Windows Segoe MDL2 Assets icons. Apple proprietary fonts are not bundled. Titles are 20–22 DIPs, body 12–13, captions 10–11. Buttons use 9 DIP corners, cards 14, segmented groups 11, preview 18 and capture controls 22. Window margins are 24–28 DIPs. Native Windows caption buttons and rounded window corners remain.

The screenshot editor has a solid navigation sidebar, grouped brush presets, compact round swatches, contextual options and a large image workspace. Preferences use grouped cards, switches and a shared sidebar. Utility sheets share page headers, accurate source metadata, compact format segments and consistent action placement. Video editing has a framed player, grouped transport, timeline and explicit trim fields.

Selected states combine weight, color and a structural marker. Icon-only controls keep tooltips and automation names. Focus has a visible outline; disabled controls retain a distinct state. High contrast uses OS colors and native checkbox/slider/text-field behavior.

## Direct screen tools

The capture toolbar is a compact icon-only strip centered at the top of the active display's working area. Its 40 DIP controls use vector symbols for area, window, one display, all displays, screenshot, OCR, eyedropper, video, cancel and confirm. Tooltips, automation names, keyboard focus and selected indicators keep the actions discoverable. It hides during a region drag.

Screenshot remains the default action. Live OCR reuses the frozen screen selection for a dragged region, selected display, all displays or a chosen window. Windows OCR runs on this PC and copies recognized text silently, whether launched by a direct shortcut or through the capture toolbar. Successful reads produce no text popup, screenshot preview, file or capture animation. Only failure or empty recognition shows a brief message, preserving the previous clipboard when there is no text.

Pick color previews the sampled pixel and hex value under the pointer. Clicking copies `#RRGGBB` and opens a compact Color picker inspector with selectable HEX, RGB, HSL and CMYK values, individual copy actions and up to 24 unique recent picks persisted in the current settings profile. Picking an existing color moves it to the front; clicking a history swatch shows its values and copies its HEX. Pick new hides the inspector while sampling; cancellation restores it. CMYK is a generic RGB conversion estimate, not an ICC-profiled print conversion (explained on its tooltip). The annotation color dialog remains a separate tool for choosing drawing colors.

## Video editor reference revision

The user's selected Stitch screenshot is the direct composition reference for Better SS Split. Its window uses an integrated 44 DIP Windows caption/header with source metadata, undo/redo, shortcuts and export; a large charcoal viewer with a centered fit preview and accurate metadata/timecode HUDs; a 42 DIP transport strip; one compact editing row; and a thumbnail timeline with ruler, red playhead and yellow trim edges. Both light and dark layouts fit the 900 × 600 minimum window.

The timeline creates up to 24 real source thumbnails with the bundled FFmpeg. Image pixels are detached from their worker-owned decoders into frozen BitmapSource buffers before WPF drawings consume them. Thumbnail timestamps map back into each clip's source range after cuts. Clicking/dragging scrubs the edited timeline, yellow edges preview source In/Out and commit one undoable trim, and the fields accept timecodes or seconds. Existing split/delete/marks/undo/redo/export remain connected; S joins C/Ctrl+K as a split shortcut. Speed and loop affect preview playback only. Media metadata and file-size estimates are computed from the actual source/settings.

Use `--design-preview --video-only --output <folder>` to render this editor with a generated sample video in both themes and at minimum size. This mode is a visual review export and does not run interaction regression suites.

## Liquid glass

Both the capture toolbar and post-capture screenshot preview use the optical material, including the preview's hover action strip. The screenshot stays sharp: effects are applied to background layers, separate from the image and foreground controls.

`LiquidLens` caches rounded lens geometry. It resamples background pixels with mild interior magnification and stronger curved displacement near the bevel. Color channels receive a small dispersion offset along the lens normal. The center uses a 7 DIP blur beneath a readable neutral tint; the rim uses sharp refracted pixels. An inset highlight, gradient lighting, soft separation shadow and pointer-position specular reflection complete the material. Reduced motion disables the moving highlight.

The capture toolbar samples the existing frozen capture image once at the toolbar's physical position. It appears immediately, with no entrance animation or drag hint, and hides during region selection.

The preview samples one small desktop rectangle shared by its two materials at a 180 ms interval, only while visible. Sampling starts after capture exclusion is established so it cannot recursively sample its own pixels. Preparation runs on a worker using frozen bitmaps; WPF brushes are updated on the dispatcher. Invisible action strips are skipped. Hidden/closed windows stop sampling; closing removes event subscriptions. Samples are transient and are not written to disk or uploaded. The sampler bounds its physical rectangle to 1.8 million pixels, lens buffers to 700 × 450, and its geometry cache to 24 entries. This is not a claim of 60 fps or GPU profiling.

Windows transparency and High Contrast select glass or solid surfaces. WPF tier 0/1 and software-only rendering keep glass using a compatibility path: lens buffers preserve their aspect ratio within 350 × 225 pixels, CPU box blur supplies the frost, and WPF blur/shadow effects and pointer-following highlights are omitted. Compatibility previews sample every 500 ms; tier 2 keeps the full material at 180 ms. Unavailable capture exclusion uses a solid fallback, with its original error preserved in diagnostics. Three sampling failures stop sampling and retain the last good glass frame when available; without a good frame they use solid surfaces. Main settings, editing controls and dense timelines remain solid for readability.

Apple's Liquid Glass APIs are part of SwiftUI and AppKit. This original Windows implementation reproduces refraction and lighting behavior; it is not Apple's native renderer. No dependency or copied reference implementation was added.

## Motion

Immediate hover/press feedback, button compression to .98, restrained release springs and short dialog/menu entrances remain. Retargeting carries presentation position and velocity. Completed clocks are removed. Native menu/dialog dismissal is immediate. Capture controls have no entrance animation; the editing canvas is not entrance-scaled. Screenshot flash/shrink and drag behavior retain existing settings. Reduced motion skips spatial transitions.

## Native design previews

Build with `dotnet build BetterSS/BetterSS.csproj -c Release`. Run the built executable with `--design-preview --output <folder>` to export actual WPF views using generated sample content. This mode does not register hotkeys, create a tray icon or save user preferences. It produces light/dark screens and glass sampler diagnostics. It is a visual review mode, not an interaction regression suite.

Run `--glass-test` for focused compatibility checks, including simulated tier 0/1, software-rendered materials, accessibility settings, capture-exclusion failure and frozen-frame recovery. Run `--design-preview --glass-only --software-glass --output <folder>` to exercise the real desktop sampler with software-rendered WPF; omit `--software-glass` to diagnose the machine's normal configuration. The glass text reports include Windows transparency, High Contrast, material quality, sampling interval and sampler status. Software-mode testing on one PC does not replace testing inside Sandbox or on another device.

`Run-BetterSS-Dev.ps1 -Background` starts the existing installation-free development workflow with separate `dev-settings.json`. The redesign does not publish a release.

## References

- [Apple: Adopting Liquid Glass](https://developer.apple.com/documentation/TechnologyOverviews/adopting-liquid-glass)
- [Apple: Applying Liquid Glass to custom views](https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views)
- [Microsoft: WPF technology regions](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/technology-regions-overview)
- [Microsoft: Window capture exclusion](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity)

Earlier Apple Design, UI Animation and MIT Liquid Glass references informed the existing design/motion foundation; no implementation code or proprietary fonts were copied. Stitch-generated HTML is reference material only.
