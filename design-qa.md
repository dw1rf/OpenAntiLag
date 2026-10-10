# Design QA — Open AntiLag 0.5

Visual target: user-supplied palette image with #02060E and #C50337 opposing vertical gradients. Existing native desktop application, not a website or pixel clone of the palette board. No decorative image assets were needed: the user explicitly requested a native UI gradient.

Evidence: docs/ready.png, active.png, small.png, scale150.png, scale200.png. Client-area renders at 800x930, 630x560, and synthetic 150/200% fonts and geometry. Reference and rendered ready/small images were opened in one comparison input. Full-size text is readable; no focused crop needed. User's private reference is not redistributed.

Findings and iteration: initial gradient used an oblique angle and did not follow the reference's vertical transition. Replaced with vertical crimson-to-black on the status card and dark-to-crimson on the primary button; regenerated all captures and compared again. Layout intentionally retains the familiar options and fixed action/footer region. Small windows scroll the settings; all action and update controls stay visible.

Required surfaces:
- Typography: Segoe UI retained for Windows readability; white primary and muted secondary text, measured wrapping. Disabled cards remain readable. Layout assertions passed.
- Spacing: 24 px frame inset, 12 px card gaps, rounded 20 px status panel. Default height expanded to fit update controls. Small-window settings scroll intentionally.
- Color: exact #02060E background and #C50337 accent; vertical transition matches the supplied swatches. Near-black neutral cards and subdued selected-card tint preserve hierarchy.
- Assets: standard native checkmarks/application icon; no raster illustrations or custom logo requested. Gradient is UI styling explicitly requested by the user.
- Content: working profile actions, explicit experimental-profile text, visible auto-update preference, manual check and update status. System feature behavior is unchanged.

Validation: 63 tests and actual form-button simulation passed. DWM dark-mode readback 1, caption-color HRESULT 0. Screenshots omit the OS titlebar. Cross-monitor DPI, screen-reader and high-contrast testing remain follow-up gaps; synthetic scale renders do not substitute for physical DPI testing.

No outstanding P0/P1/P2 visual findings. P3: native dialogs follow the Windows system theme.

final result: passed
