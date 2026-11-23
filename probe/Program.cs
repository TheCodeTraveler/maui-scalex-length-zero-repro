using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Animations;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Handlers;

var leg = args.SingleOrDefault() ?? throw new ArgumentException("Pass one leg: bug, control or workaround.");
DispatcherProvider.SetCurrent(new InlineDispatcherProvider());

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
