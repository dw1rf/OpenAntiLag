# Design QA — Open AntiLag 0.4 beta 1

Source: user-provided ready/active screenshots of 0.2 and blue-grey palette image (private references, not redistributed). This is a requested redesign, not a pixel clone of the old layout.

Implementation evidence: docs/ready.png, docs/active.png, docs/small.png, docs/scale150.png, docs/scale200.png. Native WinForms client area captures, no CSS/browser viewport. Baseline 800 × 890 px; small 630 × 560; synthetic 150% and 200% controls AND fonts. Captures omit the OS-owned titlebar. DWM dark-mode readback = 1, caption-color setter HRESULT = 0 on the validation host. Actual monitor-to-monitor DPI transitions were not tested.

## Comparison and iterations

- Original screenshots: disabled checkbox labels turned black; descriptions were cropped. New owner-drawn CheckBox controls retain the same high-contrast text in locked states. Description heights are measured and rows grow to fit.
- First capture: a light scrollbar and overly short default content area. Fixed with dark scrollbar styling and enough default vertical space. Small windows intentionally scroll settings while the action/footer rows remain visible.
- Final ready/active captures were opened alongside the supplied screenshots; palette reference and implementation were compared in the same visual review. All six active cards retain readable titles/descriptions. Their text regions are readable at full capture size; additional crops are unnecessary.

## Five fidelity surfaces

- Typography: Segoe UI, clear hierarchy, measured wrapping, no fixed one-line clipping. Layout assertions pass for labels and cards in all captured sizes.
- Spacing: 24 px outer inset, 12 px card gaps, separate state/settings/actions/footer. Resizing uses TableLayoutPanel and a scrollable settings region.
- Color: palette sampled from source swatches: #0B1423, #1F3952, #4D728F, #90B0C7, #CADCEA. Applied as background, selection, accents and text; foreground shades adjusted for contrast. No mint accent remains.
- Assets: no decorative raster assets required by this settings screen. Standard native application icon is retained. Source palette image not redistributed.
- Content: existing actions preserved; English/Russian duplicated button labels simplified to Russian. Experimental system profile, administrator rights and manual restart are stated explicitly.

## Interaction validation

49 engine tests passed. Simulated UI test invokes actual Enable/Disable buttons and checks enabled/locked/restored states; narrow-window layout assertions passed. No real power or registry settings were changed. Theme APIs returned successful dark-mode/caption results. Keyboard semantics/accessibility roles inherited from native Button/CheckBox; a screen-reader audit was not performed.

No outstanding P0/P1/P2 findings in the tested states. P3: native message boxes still use the system dialog theme; cross-monitor DPI and high-contrast mode need wider hardware testing.

Visual result: passed. System application and post-reboot behavior remain unverified; see README.


0.4 adds an explicit experimental subtitle, BCD/HAGS/priority scope, a pending-restart state and 40 px more default height to keep the warning visible. All five captures were regenerated; ready/active/small captures visually reviewed.
