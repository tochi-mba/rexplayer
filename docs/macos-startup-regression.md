# macOS startup regression

The reported 0.30.7 Intel build aborts on macOS 15.8.1 in
`xamarin_vm_initialize`, before managed application startup. CoreCLR is loaded.
The crash report does not include the runtime assertion message, so it alone
does not establish the root cause.

Investigation and validation must cover the final distributed bundle, including
its managed assemblies, runtime configuration, native architectures and signing.
A successful build and matching Mach-O architectures do not prove that the
packaged app can initialize its managed runtime on both supported CPUs.
