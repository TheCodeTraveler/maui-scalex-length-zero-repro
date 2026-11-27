# maui-scalex-length-zero-repro

[Android] `ScaleXToAsync(x, 0)` and `ScaleYToAsync(x, 0)` throw `Java.Lang.IllegalArgumentException: Cannot set 'scaleX' to Float.NaN`, Uncaught, it crashes the app; caught, it leaves `ScaleX` stuck at `NaN`. The animation `Tweener` computes `0 / 0 = NaN` for a length of 0, and Android's `UpdateScaleX`/`UpdateScaleY` only guard a NaN `Scale`, not `ScaleX`/`ScaleY`.

CI: https://github.com/TheCodeTraveler/maui-scalex-length-zero-repro/actions/runs/45237670453 (Android: 19 of 20 `ScaleXToAsync` and 18 of 20 `ScaleYToAsync` calls threw; uncaught, the app crashed)
Upstream issue: (to be filed)
Related: dotnet/maui#40535 (`rate: 0` crashes in the same `Tweener.Step`), dotnet/maui#40683 (a throwing property write wedges the property)

## Reproduce locally

Managed proof, no device (needs the .NET 11.0.100 SDK):

```
dotnet run -c Release --project probe -- bug      # exit 0: NaN is written first in 20/20 runs
dotnet run -c Release --project probe -- control  # exit 0: no NaN
dotnet run -c Release --project probe -- workaround      # exit 0: no NaN
dotnet run -c Release --project probe -- stuck    # exit 0: after one throw, ScaleX stays NaN
```

On Android (needs the `maui-android` workload and an emulator with animations on):

```
dotnet build src/C18Repro/C18Repro.csproj -c Release
adb install -r src/C18Repro/bin/Release/net11.0-android/android-x64/com.example.c18repro-Signed.apk
adb shell am start -W -n com.example.c18repro/com.example.c18repro.MainActivity --es scenario scalex0
adb logcat -d | grep -i "Float.NaN"
```

## Observed vs expected

- Observed: `view.ScaleXToAsync(x, 0)` throws `Cannot set 'scaleX' to Float.NaN`, as does `ScaleYToAsync` (`scaleY`).
- Observed after the throw: `ScaleX` stays `NaN` and every later `ScaleX = 0.3` (Android cell; `ScaleX = 1` in the probe) or `ScaleXToAsync(0.8, 250)` on that element is silently dropped (native `scaleX` stays 1). The `scalex0-stuck` cell and the `stuck` probe leg log the MAUI and native values after each step.
- Uncaught: in the `scalex0-uncaught` cell, the first `ScaleXToAsync(0.5, 0)` after a warm-up, awaited in `Loaded` without `try/catch`, killed the process with `FATAL EXCEPTION: main` / `IllegalArgumentException: Cannot set 'scaleX' to Float.NaN`.
- About 19 of 20: the very first length-0 calls in a cold process sometimes do not throw, because JIT and the first JNI callback let the millisecond clock tick over before the first step. Later calls throw almost always, so the stuck and uncaught cells retry on fresh boxes.
- Expected: a length of 0 sets the target value without throwing.
- `ScaleToAsync`, `RotateToAsync`, `FadeToAsync` and `TranslateToAsync` with length 0 write the same NaN frame. Nothing throws: MAUI skips a NaN `Scale`, and Android accepts a NaN rotation, alpha or translation.

## Versions

- `Microsoft.Maui.Controls` 11.0.10, SDK 11.0.100 (`global.json`), `net11.0-android`, `android-x64`
- Emulator: API 34 x86_64 `google_apis` (input `api-level` on `workflow_dispatch`), animator duration scale 1
- Runtime: CoreCLR, the only Android runtime in .NET 11

## Legs

[`.github/workflows/repro.yml`](.github/workflows/repro.yml) is green only when every bug leg shows the bug and every control and workaround leg is clean. Each leg writes an expected-vs-observed table to the job summary, and a leg whose app did not run fails.

| Job | Cell | Role | Proves |
|---|---|---|---|
| probe | `bug` | bug | MAUI's own `AnimationManager`, with a ticker that fires inside `Start` like Android's `ValueAnimator`, writes `NaN` first for length 0 |
| probe | `control` | control | length 1 and 250 write no `NaN` |
| probe | `workaround` | workaround | `Math.Max(1, length)` writes no `NaN` |
| probe | `stuck` | bug | a handler that throws on NaN leaves `ScaleX` at `NaN`; later sets and animations are dropped |
| android (coreclr) | `scalex0`, `scaley0` | bug | 20 iterations, each on a fresh `BoxView`, of `ScaleXToAsync`/`ScaleYToAsync(x, 0)`; logcat has `Cannot set 'scaleX'` (or `'scaleY'`) `to Float.NaN` and at least 10 of 20 iterations throw |
| android (coreclr) | `scalex0-stuck` | bug | retries fresh boxes until one throw is caught, then `ScaleX = 0.3` and `ScaleXToAsync(0.8, 250)`; MAUI `ScaleX` stays `NaN`, native stays 1 (so the set was dropped) |
| android (coreclr) | `scalex0-uncaught` | bug | warm-up, then up to 20 fresh boxes with no `try/catch`; passes only if a throw kills the process (`FATAL EXCEPTION`, `Fatal signal` or `has died`) |
| android (coreclr) | `scale0`, `scalex250` | control | `ScaleToAsync(x, 0)` (has the NaN guard) and `ScaleXToAsync(x, 250)` throw 0 of 20 and reach the target |
| android (coreclr) | `scalex0-workaround` | workaround | assigning `ScaleX` directly when the length is 0 throws 0 of 20 |

The app (`src/C18Repro`) runs one scenario per process (`--es scenario <name>`), logs `C18 START` (MAUI version, target SDK) and `C18 DONE` lines, and uses a fresh `BoxView` per iteration (a throw wedges the old one, see below), waiting for `Loaded` plus 150 ms so the ticker is idle and the first tick arrives inside `Start`. Animations must stay on: with an animator duration scale of 0, MAUI skips every animation, so the Android job fails if the scale is 0.

## Root cause

1. `Tweener.Step` computes `Value = Math.Min(1.0, ms / (double)Length)`. With `Length == 0` and a first step of 0 ms, that is `0 / 0 = NaN`.
2. Android's ticker starts a `ValueAnimator`, which calls its update listener before `start()` returns (AOSP `ValueAnimator.start` -> `setCurrentPlayTime(0)`). The first tick therefore almost always has 0 ms, unless the millisecond clock ticks over between `AnimationManager.Start` and the listener.
3. `UpdateScaleX`/`UpdateScaleY` skip only a NaN `Scale` (the Xamarin.Forms fix for `ScaleTo` with length 0, xamarin/Xamarin.Forms#14606). `ScaleX`/`ScaleY` reach `View.setScaleX`, which throws on NaN.
4. `BindableObject.SetValueCore` (L695-711 in 11.0.10 and main) sets `IsBeingSet` with no `try/finally`. The throw from the `ScaleX` handler leaves the flag set, so every later `ScaleX` set on that element is queued in `DelayedSetters` forever and `ScaleX` stays `NaN`. This affects any property whose handler throws: dotnet/maui#40683.
5. dotnet/maui#30167 fixed the same `Tweener` NaN for Windows `RotateXTo`/`RotateYTo` only, by guarding that platform.

## Recommended fix

- In `Tweener.Step`, treat `Length == 0` as complete: `Value = Length == 0 ? 1.0 : Math.Min(1.0, ms / (double)Length)`. This fixes every platform and every property.
- In `BindableObject.SetValueCore`, clear `IsBeingSet` in a `finally`, so a throwing handler cannot wedge the property.
- Also, in Android `UpdateScaleX`/`UpdateScaleY`, skip a value that is not finite (`float.IsFinite`), next to the existing NaN check.

The fix needs a change inside MAUI. From user code, the workaround is to avoid a length of 0 (assign the property directly, or use a length of 1 or more), which the workaround legs show.
