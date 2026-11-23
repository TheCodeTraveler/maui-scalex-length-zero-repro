# maui-scalex-length-zero-repro

[Android] `ScaleXToAsync(x, 0)` and `ScaleYToAsync(x, 0)` throw `Java.Lang.IllegalArgumentException: Cannot set 'scaleX' to Float.NaN`. The animation `Tweener` computes `0 / 0 = NaN` for a length of 0, and Android's `UpdateScaleX`/`UpdateScaleY` only guard a NaN `Scale`, not `ScaleX`/`ScaleY`.

CI: (run link added after the first run)
Upstream issue: (to be filed)

## Reproduce locally

Managed proof, no device (needs the .NET 11.0.100 SDK):

```
dotnet run -c Release --project probe -- bug      # exit 0: ScaleX writes [NaN, 2]
dotnet run -c Release --project probe -- control  # exit 0: no NaN
dotnet run -c Release --project probe -- fix      # exit 0: no NaN
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
- Expected: a length of 0 completes immediately at the target value, as `ScaleToAsync(x, 0)` already does.
- The same NaN frame is also written by `RotateToAsync`, `FadeToAsync` and `TranslateToAsync` with length 0. Android accepts it for those properties, so nothing throws.

## Versions

- `Microsoft.Maui.Controls` 11.0.10, SDK 11.0.100 (`global.json`), `net11.0-android`, `android-x64`
- Emulator: API 34 x86_64 `google_apis` (input `api-level` on `workflow_dispatch`), animator duration scale 1
- Runtimes: CoreCLR and Mono (`-p:ReproRuntime=coreclr|mono`)

## Legs

[`.github/workflows/repro.yml`](.github/workflows/repro.yml) is green only when every bug leg shows the bug and every control and fix leg is clean. Each leg writes an expected-vs-observed table to the job summary, and a leg whose app did not run fails.

| Job | Cell | Role | Proves |
|---|---|---|---|
| probe | `bug` | bug | MAUI's own `AnimationManager`, with a ticker that fires inside `Start` like Android's `ValueAnimator`, writes `NaN` first for length 0 |
| probe | `control` | control | length 1 and 250 write no `NaN` |
| probe | `fix` | fix | `Math.Max(1, length)` writes no `NaN` |
| android (coreclr, mono) | `scalex0`, `scaley0` | bug | 20 iterations of `ScaleXToAsync`/`ScaleYToAsync(x, 0)`; logcat has `Cannot set 'scaleX'` (or `'scaleY'`) `to Float.NaN` |
| android (coreclr, mono) | `scale0`, `scalex250` | control | `ScaleToAsync(x, 0)` (has the NaN guard) and `ScaleXToAsync(x, 250)` throw 0 of 20 and reach the target |
| android (coreclr, mono) | `scalex0-workaround` | fix | assigning `ScaleX` directly when the length is 0 throws 0 of 20 |

The app (`src/C18Repro`) runs one scenario per process (`--es scenario <name>`), logs `C18 START` and `C18 DONE` lines, and waits 150 ms between iterations so the ticker is idle and the first tick arrives inside `Start`. Animations must stay on: with an animator duration scale of 0, MAUI skips every animation, so the Android job fails if the scale is 0.

## Root cause

1. `Tweener.Step` computes `Value = Math.Min(1.0, ms / (double)Length)`. With `Length == 0` and a first step of 0 ms, that is `0 / 0 = NaN`.
2. Android's ticker starts a `ValueAnimator`, which calls its update listener before `start()` returns (AOSP `ValueAnimator.start` -> `setCurrentPlayTime(0)`). The first tick therefore always has 0 ms.
3. `UpdateScaleX`/`UpdateScaleY` skip only a NaN `Scale` (the Xamarin.Forms fix for `ScaleTo` with length 0, xamarin/Xamarin.Forms#14606). `ScaleX`/`ScaleY` reach `View.setScaleX`, which throws on NaN.
4. dotnet/maui#30167 fixed the same `Tweener` NaN for Windows `RotateXTo`/`RotateYTo` only, by guarding that platform.

## Recommended fix

- In `Tweener.Step`, treat `Length == 0` as complete: `Value = Length == 0 ? 1.0 : Math.Min(1.0, ms / (double)Length)`. This fixes every platform and every property.
- Also, in Android `UpdateScaleX`/`UpdateScaleY`, skip a value that is not finite (`float.IsFinite`), next to the existing NaN check.

The fix needs a change inside MAUI. From user code, the workaround is to avoid a length of 0 (assign the property directly, or use a length of 1 or more), which the fix legs show.
