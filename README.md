# Spatial Wallpaper source snapshot

Public repository: https://github.com/KyaKayKya/Spatial-Wallpaper-Community

Frozen source commit: `f64cc14bfb2c5559c298a7ea579b7a5bdf26be36`. This directory contains no private Git history.

For normal use, obtain the complete Windows distribution from this repository's Releases. This source-only snapshot is for code review and development; it is not an installer or a complete Unity artwork project.

SDK and authoring sources are under `Packages/com.spatialwallpaper.sdk`; shared project sources, tools and tests keep their original paths. Start with the SDK `Documentation~` directory. Creators need Unity 2022.3 and the required development toolchain. Runtime users do not need Unity.

Original scenes, models, materials, textures, audio, licensed external packages and binary caches are intentionally omitted. See `OMITTED_INPUTS.json`; import each required dependency only after obtaining its license. Missing project assets and references must be restored before building the full artwork library. The exporter does not download LFS objects or install dependencies.

Verify `SHA256SUMS` and `SOURCE_MANIFEST.json` before reviewing or publishing. Some source documentation links refer to private development evidence intentionally absent from this snapshot.

See `SOURCE_LICENSE_STATUS.md` and `THIRD_PARTY_NOTICES.md` for the license boundary. The snapshot does not grant a new license for project-owned code.
