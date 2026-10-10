# NVIDIA implementation notes

Public ABI and setting constants: NVIDIA/nvapi commit 70d337db9186e968eab622f7e786de7e437faf3d (nvapi.h, nvapi_interface.h, NvApiDriverSettings.h).
https://github.com/NVIDIA/nvapi/tree/70d337db9186e968eab622f7e786de7e437faf3d

Extended DRS entry points and Low Latency IDs: NVIDIA Profile Inspector commit 2f50c388b3a4d661cade66b32746bec096d1eee1 (Native/NVAPI/NvapiDrsWrapper.cs and CustomSettingNames.xml).
https://github.com/Orbmu2k/nvidiaProfileInspector/tree/2f50c388b3a4d661cade66b32746bec096d1eee1

Our interop code is implemented locally. No downloaded binaries are used. The NVAPI DLL is loaded from System32 only. Extended Set/Get signatures are distinct from the public signatures (two extra DWORDs on Set, one by-reference DWORD on Get). Driver 617.42 rejected the Low Latency hidden IDs with the public entry points; extended calls accepted them. These entry points are undocumented and may change.

NVDRS_SETTING_V1 uses pack 4, 12320 bytes: version=0; setting ID=4100; type=4104; location=4108; isCurrentPredefined=4112; current DWORD=8220. Public DWORD settings only, except two vetted hidden Low Latency DWORDs. Global/base profile handles must match; custom workstation global profile is rejected.

Journal is durable before writes. Save happens once per operation; apply is verified in a fresh loaded session. Failed/partial commit keeps the journal. Recovery restores only values matching what this app applied; other external values remain. A driver default remains inherited on restoration (new driver defaults may differ from old). Concurrent modification by a separate NVIDIA utility during a transaction is outside the guarantee. No startup GPU reapply. Original values are not replaced by repeated Apply.

Validation on 2026-10-10:
- build.ps1 -OutputDirectory dist-nvidia: 76 tests.
- --ui-check: real button event path with simulated NVIDIA driver; system actions and narrow layout.
- --nvidia-read: native 617.42, RTX 4070 Ti.
- --nvidia-stage-check: all 25 settings Set/Get and Reset in disposable DRS session, deliberately no SaveSettings; fresh session matches original values and inheritance.
- Native persistent SaveSettings and performance effects remain unverified.
