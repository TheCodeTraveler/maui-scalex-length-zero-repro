namespace C18Repro;

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState) => new(new ProbePage());
}
