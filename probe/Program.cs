using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;

var leg = args.SingleOrDefault() ?? throw new ArgumentException("Pass one leg: bug, control, workaround or stuck.");
DispatcherProvider.SetCurrent(new InlineDispatcherProvider());
Console.WriteLine($"C18 PROBE controls={typeof(VisualElement).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion} runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

if (leg == "stuck")
{
	return RunStuckLeg();
}

var checks = leg switch
{
	"bug" => new (string Name, bool ExpectNaN, Func<List<double>> Measure)[]
	{
		("ScaleXToAsync(2, length: 0)", true, () => Measure(v => v.ScaleXToAsync(2, 0), nameof(VisualElement.ScaleX), v => v.ScaleX)),
		("ScaleYToAsync(2, length: 0)", true, () => Measure(v => v.ScaleYToAsync(2, 0), nameof(VisualElement.ScaleY), v => v.ScaleY)),
	},
	"control" => new (string, bool, Func<List<double>>)[]
	{
		("ScaleXToAsync(2, length: 1)", false, () => Measure(v => v.ScaleXToAsync(2, 1), nameof(VisualElement.ScaleX), v => v.ScaleX)),
		("ScaleXToAsync(2, length: 250)", false, () => Measure(v => v.ScaleXToAsync(2, 250), nameof(VisualElement.ScaleX), v => v.ScaleX)),
	},
	"workaround" => new (string, bool, Func<List<double>>)[]
	{
		("ScaleXToAsync(2, length: Math.Max(1, 0))", false, () => Measure(v => v.ScaleXToAsync(2, Math.Max(1u, 0u)), nameof(VisualElement.ScaleX), v => v.ScaleX)),
		("ScaleYToAsync(2, length: Math.Max(1, 0))", false, () => Measure(v => v.ScaleYToAsync(2, Math.Max(1u, 0u)), nameof(VisualElement.ScaleY), v => v.ScaleY)),
	},
	_ => throw new ArgumentException($"Unknown leg '{leg}'."),
};

const int Runs = 20;
// Unmeasured: the first animation JIT-compiles AnimationManager.OnFire, which could tick the millisecond clock over mid-animation.
Measure(v => v.ScaleXToAsync(2, 250), nameof(VisualElement.ScaleX), v => v.ScaleX);

var rows = new List<object>();
var allAsExpected = true;
foreach (var (name, expectNaN, measure) in checks)
{
	var nanFirst = 0;
	var lastWrites = new List<double>();
	for (var run = 0; run < Runs; run++)
	{
		lastWrites = measure();
		if (lastWrites.Count > 0 && double.IsNaN(lastWrites[0]))
		{
			nanFirst++;
		}
	}

	var ok = expectNaN ? nanFirst >= 1 : nanFirst == 0;
	allAsExpected &= ok;
	rows.Add(new { check = name, expected = expectNaN ? "NaN first in at least 1 of 20 (expect 20/20)" : "NaN first in 0 of 20", observed = $"nanFirst={nanFirst}/{Runs}", lastWrites = lastWrites.Select(w => w.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(), ok });
	Console.WriteLine($"C18 PROBE leg={leg} check=\"{name}\" nanFirst={nanFirst}/{Runs} writes=[{string.Join(", ", lastWrites)}] expectNaN={expectNaN} ok={ok}");
}

Directory.CreateDirectory("results");
File.WriteAllText($"results/probe-{leg}.json", JsonSerializer.Serialize(new { leg, ok = allAsExpected, rows }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"C18 PROBE RESULT leg={leg} ok={allAsExpected}");
return allAsExpected ? 0 : 1;

// A handler that throws makes BindableObject.SetValueCore leave IsBeingSet set (no try/finally), so every later set of that property is queued forever.
static int RunStuckLeg()
{
	var ticker = new FiresInsideStartTicker();
	var services = new ServiceCollection()
		.AddSingleton<IAnimationManager>(new AnimationManager(ticker))
		.AddSingleton<IDispatcher>(new InlineDispatcher())
		.BuildServiceProvider();
	var handler = new ThrowsOnNaNScaleXHandler();
	handler.SetMauiContext(new MauiContext(services));
	var box = new BoxView { Handler = handler };

	var threw = false;
	try
	{
		var task = box.ScaleXToAsync(0.5, 0);
		for (var frame = 0; frame < 5 && !task.IsCompleted; frame++)
		{
			Thread.Sleep(16);
			ticker.Fire?.Invoke();
		}
	}
	catch (ArgumentException ex)
	{
		threw = true;
		Console.WriteLine($"C18 PROBE stuck step=throw message=\"{ex.Message}\"");
	}

	Console.WriteLine(Describe("after the throw", box, handler));
	box.ScaleX = 1;
	Console.WriteLine(Describe("after ScaleX = 1", box, handler));

	var animation = box.ScaleXToAsync(0.8, 250);
	for (var frame = 0; frame < 40 && !animation.IsCompleted; frame++)
	{
		Thread.Sleep(16);
		ticker.Fire?.Invoke();
	}

	Console.WriteLine(Describe($"after ScaleXToAsync(0.8, 250) completed={animation.IsCompleted}", box, handler));
	var finalScaleX = box.ScaleX;
	var ok = threw && double.IsNaN(finalScaleX) && handler.PlatformScaleX == 1;
	Console.WriteLine(FormattableString.Invariant($"C18 PROBE stuck finalScaleX={finalScaleX} finalPlatformScaleX={handler.PlatformScaleX}"));

	Directory.CreateDirectory("results");
	File.WriteAllText("results/probe-stuck.json", JsonSerializer.Serialize(new { leg = "stuck", ok, threw, finalScaleX = finalScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture), finalPlatformScaleX = handler.PlatformScaleX }, new JsonSerializerOptions { WriteIndented = true }));
	Console.WriteLine($"C18 PROBE RESULT leg=stuck ok={ok}");
	return ok ? 0 : 1;
}

static string Describe(string step, BoxView box, ThrowsOnNaNScaleXHandler handler) =>
	FormattableString.Invariant($"C18 PROBE stuck step=\"{step}\" maui={box.ScaleX} platform={handler.PlatformScaleX}");

static List<double> Measure(Func<VisualElement, Task<bool>> animate, string property, Func<VisualElement, double> read)
{
	var ticker = new FiresInsideStartTicker();
	var services = new ServiceCollection()
		.AddSingleton<IAnimationManager>(new AnimationManager(ticker))
		.AddSingleton<IDispatcher>(new InlineDispatcher())
		.BuildServiceProvider();
	var handler = new ObjectViewHandler();
	handler.SetMauiContext(new MauiContext(services));
	var view = new BoxView();
	view.Handler = handler;

	var written = new List<double>();
	view.PropertyChanged += (_, e) => { if (e.PropertyName == property) written.Add(read(view)); };

	var task = animate(view);
	var frames = 0;
	while (!task.IsCompleted && frames++ < 100)
	{
		Thread.Sleep(16);
		ticker.Fire?.Invoke();
	}

	return task.IsCompleted ? written : throw new InvalidOperationException("The animation never completed, so this leg proves nothing.");
}

// Mirrors Android's ValueAnimator.start(), which calls the update listener before it returns (AOSP ValueAnimator.java L1145-1158).
sealed class FiresInsideStartTicker : ITicker
{
	public bool IsRunning { get; private set; }
	public bool SystemEnabled => true;
	public int MaxFps { get; set; } = 60;
	public Action? Fire { get; set; }
	public void Start() { IsRunning = true; Fire?.Invoke(); }
	public void Stop() => IsRunning = false;
}

sealed class ObjectViewHandler() : ViewHandler<IView, object>(ViewMapper)
{
	protected override object CreatePlatformView() => new object();
}

sealed class InlineDispatcherProvider : IDispatcherProvider
{
	public IDispatcher GetForCurrentThread() => new InlineDispatcher();
}

sealed class InlineDispatcher : IDispatcher
{
	public bool IsDispatchRequired => false;
	public bool Dispatch(Action action) { action(); return true; }
	public bool DispatchDelayed(TimeSpan delay, Action action) { action(); return true; }
	public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
}

// Stands in for Android's View.setScaleX, which throws on NaN.
sealed class ThrowsOnNaNScaleXHandler() : ViewHandler<IView, object>(new PropertyMapper<IView, ThrowsOnNaNScaleXHandler>(ViewMapper)
{
	[nameof(IView.ScaleX)] = (handler, view) =>
	{
		if (double.IsNaN(view.ScaleX))
		{
			throw new ArgumentException("Cannot set 'scaleX' to Float.NaN");
		}

		handler.PlatformScaleX = view.ScaleX;
	},
})
{
	public double PlatformScaleX { get; set; } = 1;
	protected override object CreatePlatformView() => new object();
}
