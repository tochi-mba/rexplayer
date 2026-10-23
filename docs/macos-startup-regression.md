# macOS startup regression

The reported 0.30.7 Intel build aborts on macOS 15.8.1 in
`xamarin_vm_initialize`, before managed application startup. CoreCLR is loaded.
The crash report does not include the runtime assertion message, so it alone
does not establish the root cause.

Investigation and validation must cover the final distributed bundle, including
its managed assemblies, runtime configuration, native architectures and signing.
A successful build and matching Mach-O architectures do not prove that the
packaged app can initialize its managed runtime on both supported CPUs.

## Packaging defect and fix

Inspection of the published `v0.30.7/rexplayer-mac-universal.zip` found
`Contents/MonoBundle/rexplayer.dll` with PE machine `0xaa64` (ARM64), and
`System.Private.CoreLib.dll` with machine `0xec20` (the ARM64 macOS runtime).
The retired merge script copied all non-Mach-O files from the ARM bundle, so
the Intel native launcher received the ARM managed payload. This explains why
merely checking the native executable with `lipo` did not catch the defect.

Build the universal app through the macOS SDK using the project's plural
`RuntimeIdentifiers`, without a single `-r` override. The SDK generates the
launcher and arranges architecture-specific managed assets together. See
[Microsoft's universal-app instructions](https://github.com/dotnet/macios/wiki/.NET-release-notes-Xcode-13.3#how-to-create-a-universal-app-with-support-for-multiple-architectures).

CI extracts the resulting ZIP, verifies its checksum and signature without
re-signing it, then launches it on both Apple Silicon and Intel macOS 15.
The smoke test must start, play a fixture and quit cleanly. It captures native
stdout and stderr so an early runtime assertion remains visible even when
no managed log has been written. These native checks require Mac CI; Windows
cannot establish that the fixed app launches on the affected machine.
