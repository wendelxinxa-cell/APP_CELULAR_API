using Android.App;
using Android.Content.PM;
using Android.OS;

namespace APP_CELULAR
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            Window?.SetStatusBarColor(Android.Graphics.Color.ParseColor("#111111"));
            Window?.SetNavigationBarColor(Android.Graphics.Color.ParseColor("#111111"));
        }
    }
}
