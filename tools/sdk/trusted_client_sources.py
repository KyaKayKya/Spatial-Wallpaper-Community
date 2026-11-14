"""One compile inventory for the trusted native preview and presentation checks."""
def client_sources(root):
 files=list((root/'Supervisor/Presentation').glob('*.cs'))+list((root/'Supervisor/Preview').glob('*.cs'))
 files += [root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['RuntimeSession.cs','RuntimePackageInspection.cs','WorkerFrameCodec.cs','RuntimeLimits.Generated.cs']]
 files += [root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows'/name for name in ['PinnedProcessOwner.cs','NativeScriptControl.cs','TrustedAudioSource.cs','PreviewStatusFile.cs','TrustedRuntimeDistribution.cs','HostExitContract.cs','SupervisorManagerConnection.cs','SupervisorSessionClient.cs','ContentSurfaceConnection.cs','PreviewHostLease.cs']]
 files += [root/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities'/name for name in ['InputBroker.cs','InputGate.cs','InputSnapshot.cs','WindowsMachineClock.cs','SoundMix.cs','AsyncPreviewInputTarget.cs']]
 files += [root/'Supervisor/Broker/DesktopInputChannel.cs',root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PreviewInputChannel.cs',root/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/WindowsPreviewInput.cs']
 files += list((root/'Packages/com.spatialwallpaper.sdk/Runtime/Data').glob('*.cs'))+[path for path in (root/'Prototype/Assets/ContentRuntime/Core').glob('*.cs') if path.name not in {'GitHubMarket.cs','GitHubMarketClient.cs','ContentStore.Native.cs'}]
 files += list((root/'Supervisor/Interactive').glob('*.cs'))
 files += [root/'Supervisor/Windows/Native.cs']
 return list(dict.fromkeys(files))
