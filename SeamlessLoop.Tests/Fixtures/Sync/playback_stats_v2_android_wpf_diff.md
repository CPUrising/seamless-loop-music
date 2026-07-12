# Playback Statistics Android/WPF Interop

## Inputs
- Golden: E:\codeproject\SeamlessLoopMobileNative\app\src\test\resources\sync\playback_stats_v2_android_golden.json
  - SHA-256: 3bfca90e2444c5d495c738367ae158ffba41c1dee2de9967250de4a238e2b9a5
- Tombstone collision: E:\codeproject\SeamlessLoopMobileNative\app\src\test\resources\sync\playback_stats_v2_tombstone_collision.json
  - SHA-256: 730fbf7396481398f1d79e58d051e2fab6996dd2ea36a442005f1bb67e386f1b

## Merge
- Production merge order: golden + collision and collision + golden
- Canonical playbackStatistics equality: PASS
- Top-level provenance differences before normalization: deviceId: "android-pixel-8" vs "desktop-wpf-1"
- WPF provenance: deviceId=`desktop-wpf-1`, exportedAt=max(input exportedAt)

## Assertions
- Wire: schema2=`PASS`, playbackStatistics=`PASS`, sourceLocal=`PASS`, datedListenMs=`PASS`
- Forbidden aliases absent: playbackStats=`PASS`, dailyListenMs=`PASS`, source-local=`PASS`
- android-pixel-8 generation 1 contribution suppressed: PASS
- android-pixel-8 generation 2 dated buckets survive: PASS
- android-pixel-8 generation 1 tombstone survives: PASS
- Unresolved Mix.FLAC identity and desktop generation 0 contribution survive: PASS
- Unresolved optional metadata remains absent: PASS

## Round Trip
- WPF fixture strict deserialize + canonical serialize byte identity: PASS
- WPF fixture SHA-256: d84d3b53aaf49dbe69389f6b1caf7eb59d458601735a20db771ab3fceb72a8bb
