using Android.App;
using Android.Content.PM;
using Android.OS;

namespace C18Repro;

[Activity(Name = "com.example.c18repro.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		Scenario.Name = Intent?.GetStringExtra("scenario");
		base.OnCreate(savedInstanceState);
	}
}
