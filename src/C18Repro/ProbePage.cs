using Android.Provider;
using Android.Util;

namespace C18Repro;

// Runs one scenario, chosen by `am start --es scenario <name>`, then logs a DONE line. Every line carries the "C18" tag.
// Scenarios: scalex0, scaley0 (the bug), scale0, scalex250 (controls), scalex0-workaround (the fix).
public class ProbePage : ContentPage
{
	const string Tag = "C18";
	const int Iterations = 20;
	readonly BoxView box = new() { WidthRequest = 100, HeightRequest = 100, Color = Colors.Red };

	public ProbePage()
	{
		Content = new VerticalStackLayout { Children = { box } };
		Loaded += async (_, _) => await RunScenarioAsync();
	}

	async Task RunScenarioAsync()
	{
		var scenario = Scenario.Name ?? "none";
		var animatorScale = Settings.Global.GetFloat(Platform.AppContext.ContentResolver, Settings.Global.AnimatorDurationScale, 1f);
		Log.Info(Tag, $"C18 START scenario={scenario} api={(int)Android.OS.Build.VERSION.SdkInt} animatorScale={animatorScale.ToString(System.Globalization.CultureInfo.InvariantCulture)} runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

		AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Log.Error(Tag, $"C18 UNHANDLED {e.Exception.GetType().FullName}: {e.Exception.Message}");

		var thrown = 0;
		var lastTarget = 1.0;
		for (var i = 0; i < Iterations; i++)
		{
			lastTarget = 0.5 + i * 0.01;
			try
			{
				await AnimateAsync(scenario, lastTarget);
			}
			catch (Exception ex)
			{
				thrown++;
				Log.Error(Tag, $"C18 ITER {i} threw {ex.GetType().FullName}: {ex.Message}");
			}

			// Lets the ticker go idle, so the next animation starts it again and its first tick arrives inside Start
			await Task.Delay(150);
		}

		var finalScaleX = box.ScaleX;
		var finalScaleY = box.ScaleY;
		var finalScale = box.Scale;
		var completed = Math.Abs(scenario.StartsWith("scaley", StringComparison.Ordinal) ? finalScaleY - lastTarget : scenario == "scale0" ? finalScale - lastTarget : finalScaleX - lastTarget) < 0.0001;
		Log.Info(Tag, $"C18 DONE scenario={scenario} thrown={thrown}/{Iterations} reachedLastTarget={completed}");
	}

	Task<bool> AnimateAsync(string scenario, double target) => scenario switch
	{
		"scalex0" => box.ScaleXToAsync(target, 0),
		"scaley0" => box.ScaleYToAsync(target, 0),
		"scale0" => box.ScaleToAsync(target, 0),
		"scalex250" => box.ScaleXToAsync(target, 250),
		"scalex0-workaround" => ScaleXToAsyncWorkaround(target, 0),
		_ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
	};

	// Workaround: a length of 0 never goes through the animation, so the Tweener's 0/0 never happens
	Task<bool> ScaleXToAsyncWorkaround(double target, uint length)
	{
		if (length == 0)
		{
			box.ScaleX = target;
			return Task.FromResult(true);
		}

		return box.ScaleXToAsync(target, length);
	}
}
