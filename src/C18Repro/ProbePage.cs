using System.Globalization;
using System.Reflection;
using Android.Provider;
using Android.Runtime;
using Android.Util;

namespace C18Repro;

// One scenario per process, so a crash in one cell cannot hide another cell's result.
public class ProbePage : ContentPage
{
	const string Tag = "C18";
	const int Iterations = 20;
	readonly VerticalStackLayout host = new();

	public ProbePage()
	{
		Content = host;
		Loaded += async (_, _) => await RunScenarioAsync();
	}

	static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

	async Task RunScenarioAsync()
	{
		var scenario = Scenario.Name ?? "none";
		var animatorScale = Settings.Global.GetFloat(Platform.AppContext.ContentResolver, Settings.Global.AnimatorDurationScale, 1f);
		var mauiVersion = typeof(VisualElement).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		var targetSdk = Platform.AppContext.ApplicationInfo?.TargetSdkVersion;
		Log.Info(Tag, $"C18 START scenario={scenario} api={(int)Android.OS.Build.VERSION.SdkInt} animatorScale={Format(animatorScale)} targetSdk={targetSdk} maui={mauiVersion} runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

		AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Log.Error(Tag, $"C18 UNHANDLED {e.Exception.GetType().FullName}: {e.Exception.Message}");

		switch (scenario)
		{
			case "scalex0-stuck":
				await RunStuckAsync();
				break;
			case "scalex0-uncaught":
				await RunUncaughtAsync();
				break;
			default:
				await RunIterationsAsync(scenario);
				break;
		}
	}

	async Task<BoxView> AddFreshBoxAsync()
	{
		var box = new BoxView { WidthRequest = 100, HeightRequest = 100, Color = Colors.Red };
		var loaded = new TaskCompletionSource();
		box.Loaded += (_, _) => loaded.TrySetResult();
		host.Children.Clear();
		host.Children.Add(box);

		var finished = await Task.WhenAny(loaded.Task, Task.Delay(5000));
		if (finished != loaded.Task)
		{
			throw new TimeoutException("The BoxView never raised Loaded.");
		}

		// Lets the ticker go idle, so the next animation starts it again and its first tick arrives inside Start
		await Task.Delay(150);
		return box;
	}

	static double? ReadNativeScaleX(BoxView box) => (box.Handler?.PlatformView as Android.Views.View)?.ScaleX;

	async Task RunIterationsAsync(string scenario)
	{
		var thrown = 0;
		var reached = 0;
		var reachedLastTarget = false;
		var loggedStackTrace = false;
		for (var i = 0; i < Iterations; i++)
		{
			var box = await AddFreshBoxAsync();
			var target = 0.5 + i * 0.01;
			var didThrow = false;
			try
			{
				await AnimateAsync(box, scenario, target);
			}
			catch (Exception ex)
			{
				didThrow = true;
				thrown++;
				if (!loggedStackTrace)
				{
					loggedStackTrace = true;
					Log.Error(Tag, $"C18 STACK {ex}");
				}
			}

			var reachedTarget = Math.Abs(ReadMauiValue(box, scenario) - target) < 0.0001;
			reached += reachedTarget ? 1 : 0;
			reachedLastTarget = reachedTarget;
			Log.Info(Tag, $"C18 ITER {i} threw={didThrow} reachedTarget={reachedTarget} maui={Format(ReadMauiValue(box, scenario))} native={ReadNativeScaleX(box)}");
		}

		Log.Info(Tag, $"C18 DONE scenario={scenario} thrown={thrown}/{Iterations} reachedLastTarget={reachedLastTarget} reachedTarget={reached}/{Iterations}");
	}

	static double ReadMauiValue(BoxView box, string scenario) => scenario switch
	{
		"scaley0" => box.ScaleY,
		"scale0" => box.Scale,
		_ => box.ScaleX,
	};

	async Task RunStuckAsync()
	{
		var box = await AddFreshBoxAsync();
		var thrown = 0;
		try
		{
			await box.ScaleXToAsync(0.5, 0);
		}
		catch (Exception ex)
		{
			thrown++;
			Log.Error(Tag, $"C18 STACK {ex}");
		}

		LogStuckStep("after the throw", box);
		box.ScaleX = 1;
		LogStuckStep("after ScaleX = 1", box);
		await box.ScaleXToAsync(0.8, 250);
		LogStuckStep("after ScaleXToAsync(0.8, 250)", box);

		var reached = Math.Abs(box.ScaleX - 0.8) < 0.0001;
		Log.Info(Tag, $"C18 DONE scenario=scalex0-stuck thrown={thrown}/1 reachedLastTarget={reached} finalMaui={Format(box.ScaleX)} finalNative={ReadNativeScaleX(box)}");
	}

	static void LogStuckStep(string step, BoxView box) =>
		Log.Info(Tag, $"C18 STUCK step=\"{step}\" maui={Format(box.ScaleX)} native={ReadNativeScaleX(box)}");

	// No try/catch on purpose: the process must die before it can log DONE.
	async Task RunUncaughtAsync()
	{
		var box = await AddFreshBoxAsync();
		Log.Info(Tag, "C18 UNCAUGHT awaiting ScaleXToAsync(0.5, 0) without try/catch");
		await box.ScaleXToAsync(0.5, 0);
		Log.Info(Tag, "C18 DONE scenario=scalex0-uncaught thrown=0/1 reachedLastTarget=True");
	}

	Task<bool> AnimateAsync(BoxView box, string scenario, double target) => scenario switch
	{
		"scalex0" => box.ScaleXToAsync(target, 0),
		"scaley0" => box.ScaleYToAsync(target, 0),
		"scale0" => box.ScaleToAsync(target, 0),
		"scalex250" => box.ScaleXToAsync(target, 250),
		"scalex0-workaround" => ScaleXToAsyncWorkaround(box, target, 0),
		_ => throw new InvalidOperationException($"Unknown scenario '{scenario}'."),
	};

	// Workaround: a length of 0 never goes through the animation, so the Tweener's 0/0 never happens
	static Task<bool> ScaleXToAsyncWorkaround(BoxView box, double target, uint length)
	{
		if (length == 0)
		{
			box.ScaleX = target;
			return Task.FromResult(true);
		}

		return box.ScaleXToAsync(target, length);
	}
}
