using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Intersect.Client.Core;
using Microsoft.Xna.Framework;
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
        try
        {
            var root = Path.Combine(FilesDir?.AbsolutePath ??
                throw new InvalidOperationException("Android FilesDir unavailable"), "kabra");
            Directory.CreateDirectory(root);
            CopyAssets("resources", root);
            // Bootstrapper -> ClientContext -> MonoGameRunner -> IntersectGame.
            // Not a standalone placeholder menu. Native renderer/login must work.
            AndroidBridge.Start(this, root);
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Error("KabraAndroid", ex.ToString());
            throw;
        }
    }
    private void CopyAssets(string folder, string destination)
    {
        var names = Assets?.List(folder) ??
            throw new InvalidOperationException("Android AssetManager not available");
        if (names.Length == 0)
            throw new InvalidOperationException("KABRA_ASSETS_MISSING: " + folder);
        var target = Path.Combine(destination, folder);
        Directory.CreateDirectory(target);
        foreach (var name in names)
        {
            var child = folder + "/" + name;
            var children = Assets!.List(child);
            if (children is { Length: > 0 })
            {
                CopyAssets(child, destination);
                continue;
            }
            var targetFile = Path.Combine(destination, child);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            if (File.Exists(targetFile)) continue;
            using var src = Assets.Open(child);
            using var dst = File.Create(targetFile);
            src.CopyTo(dst);
        }
    }
}