# AMD ADLX implementation

SDK reference: GPUOpen-LibrariesAndSDKs/ADLX commit 32b5a740d42295c5dfe9026b9f52683da0f3af91.
https://github.com/GPUOpen-LibrariesAndSDKs/ADLX/tree/32b5a740d42295c5dfe9026b9f52683da0f3af91

Independent C# C-ABI interop; driver DLL amdadlx64.dll loaded from System32 only. No SDK binaries downloaded or redistributed. C exports use cdecl; object vtables stdcall; adlx_bool marshalled as a byte. System singleton is not reference-counted. GPU list, GPU objects, graphics service and feature interfaces are released before terminating owned ADLX initialization.

Calls are serialized by the shared GPU/system operation guard. ADLX GPU PNP string is persisted for restoration rather than an unstable list index. Six feature interfaces are negotiated by IsSupported. Unsupported/unknown interfaces are skipped, all other errors abort. Driver absence disables apply/restore while keeping guidance visible. Settings apply immediately, unlike NVIDIA DRS sessions: journal is saved before the first write and partial operations remain recoverable. No cross-utility transaction isolation is claimed.

88 unit/integration tests in current build. AMD tests exercise group dependencies, disk failure, partial writes, restore retry, external change preservation, missing capabilities, silent rejection, wrong GPU, repeated apply and journal validation. UI tests exercise Apply/Restore on FakeAmd. Native absence path checked on NVIDIA hardware. A C translation unit compiled against the pinned AMD SDK validates 16 vtable offsets and sizeof(adlx_bool)==1. Radeon hardware testing remains outstanding; UI and release notes mark support experimental.
