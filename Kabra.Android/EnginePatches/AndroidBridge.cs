using Android.Views;
using Microsoft.Xna.Framework;
namespace Intersect.Client.Core;

/// <summary>
/// Android entry into the actual Intersect 0.8.0.339 client lifecycle.
/// Uses Bootstrapper -> ClientContext -> IPlatformRunner -> IntersectGame.
/// </summary>
public static class AndroidBridge
{
    private static AndroidGameActivity? _activity;

    public static void Start(AndroidGameActivity activity, string writableGameRoot)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        Environment.CurrentDirectory = writableGameRoot;
        Bootstrapper.Start();
    }

    internal static void AttachGame(Game game)
    {
        var activity = _activity ?? throw new InvalidOperationException("Android activity not set");
        var view = game.Services.GetService(typeof(View)) as View ??
            throw new InvalidOperationException("KABRA_ANDROID_GAME_VIEW_MISSING");
        activity.SetContentView(view);
    }
}