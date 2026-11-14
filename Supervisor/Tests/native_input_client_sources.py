"""Single source inventory for native input Client compilation (never executes it)."""
from pathlib import Path

DESKTOP_SUPPORT = ('NativeHostLifecycle.cs', 'OwnedInputInjector.cs', 'OwnedHelperPause.cs',
                   'OwnedThreadSampler.cs', 'OwnedThreadEvidence.cs')
COMMON_FOLDERS = ('Supervisor/Core', 'Supervisor/Broker',
                  'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities', 'Supervisor/Interactive')


def native_input_client_sources(root, *, desktop=True):
    root = Path(root)
    tests = root/'Supervisor/Tests'
    if desktop:
        # New partial fixture components must reach all three compilation entrances.
        files = [tests/'NativeDesktopInputHostChecks.cs']
        files += sorted(p for p in tests.glob('NativeDesktopInput*Checks.cs')
                        if p.name != 'NativeDesktopInputHostChecks.cs')
        files += [tests/name for name in DESKTOP_SUPPORT]
    else:
        files = [tests/'NativeInputHostChecks.cs']
    for folder in COMMON_FOLDERS:
        entries = sorted((root/folder).glob('*.cs'))
        if not entries:
            raise FileNotFoundError('native Client source folder empty: '+folder)
        files += entries
    files += [root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in
              ('WorkerFrameCodec.cs', 'WorkerExitCodes.cs', 'RuntimeLimits.Generated.cs')]
    files += [root/'Supervisor/Windows'/name for name in
              ('Native.cs', 'RestrictedProcess.cs', 'TrustedAudioProcess.cs')]
    files += [root/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/WindowsPreviewInput.cs']
    files += [root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PreviewInputChannel.cs']
    if len(files) != len(set(files)):
        raise ValueError('duplicate native Client source')
    for path in files:
        if not path.is_file():
            raise FileNotFoundError('native Client source missing: '+str(path.relative_to(root)))
    return files
