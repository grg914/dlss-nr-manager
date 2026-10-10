# Microsoft Visual C++ Redistributable x64 — local operator input

Place the **genuine Microsoft-downloaded** installer here, if you choose to keep a local copy:

```text
third_party-local/microsoft/vc_redist.x64.exe
```

Official source: https://aka.ms/vc14/vc_redist.x64.exe

The executable is **not part of this repository**, is not included in Manager release assets, and is not installed or executed automatically. The root `.gitignore` excludes all of `third_party-local/`; do not force-add the executable to Git or upload it as a public release asset without independently confirming Microsoft redistribution rights.

In the Manager's **Downloads** page, an independent, offline, read-only Windows Registry probe shows whether the host has a Microsoft Visual C++ **v14 Redistributable x64** installation and its registered version. This status does **not** certify that every native DLL, OpenMP library (`vcomp140.dll`), or specific binary is supported. It does not affect NVIDIA GPU/driver detection and does not trigger downloads.

For Real-ESRGAN/NCNN in V4, the native build disables OpenMP and does not bundle `vcomp140.dll`. The local Redistributable installer is neither required for nor installed by that new Real-ESRGAN packaging route. Existing third-party and historical release binaries may have independent prerequisites.
