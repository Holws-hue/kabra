using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
namespace Kabra.Android;
[Activity(Label = "Kabra Online 2D", MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
                           ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public sealed class MainActivity : AndroidGameActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Blocker: the IntersectGame lifecycle and platform runner need porting.
        // Never present a shell-only APK as playable.
        throw new NotSupportedException("KABRA_ANDROID_INTERSECT_RUNNER_NOT_PORTED");
    }
}