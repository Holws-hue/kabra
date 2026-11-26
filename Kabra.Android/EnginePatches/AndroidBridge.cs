using Android.Views;
using Microsoft.Xna.Framework;

namespace Intersect.Client.Core;

/// <summary>
/// Android-only entry/lifecycle into the REAL Intersect client.
/// Game.Run() is asynchronous on Android: never wrap the Game in a method-scoped using.
/// </summary>
public static class AndroidBridge
{
    private static AndroidGameActivity? _activity;
    private static Game? _runningGame;

    public static Action<string>? StageChanged { get; set; }
    public static string LastStage { get; private set; } = "NOT_STARTED";
    public static bool ViewAttached { get; private set; }

    public static void Report(string stage)
    {
        LastStage = stage;
        global::Android.Util.Log.Info("KabraBoot", stage);
        try
        {
            // No credentials, private keys or IPs in this diagnostic log.
            File.AppendAllText(
                Path.Combine(Environment.CurrentDirectory, "kabra_android_boot.log"),
                DateTime.UtcNow.ToString("O") + " " + stage + Environment.NewLine
            );
        }
        catch (Exception error)
        {
            global::Android.Util.Log.Warn("KabraBoot", "Unable to write stage log: " + error.GetType().Name);
        }
        try { StageChanged?.Invoke(stage); }
        catch (Exception error)
        {
            global::Android.Util.Log.Warn("KabraBoot", "Status callback: " + error.GetType().Name);
        }
    }

    public static void Start(AndroidGameActivity activity, string writableGameRoot)
    {
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        Environment.CurrentDirectory = writableGameRoot;
        ViewAttached = false;
        Report("BOOTSTRAPPER_START");
        try
        {
            Bootstrapper.Start();
            Report("BOOTSTRAPPER_RETURNED");
        }
        catch (Exception exception)
        {
            Report("BOOTSTRAPPER_ERROR_" + exception.GetType().Name);
            global::Android.Util.Log.Error("KabraBoot", exception.ToString());
            throw;
        }
    }

    internal static void AttachGame(Game game)
    {
        var activity = _activity ?? throw new InvalidOperationException("KABRA_ANDROID_ACTIVITY_NOT_SET");
        Report("GETTING_GAME_VIEW");
        var view = game.Services.GetService(typeof(View)) as View
            ?? throw new InvalidOperationException("KABRA_ANDROID_GAME_VIEW_MISSING");
        // Essential for Android's asynchronous Run() loop. Do not dispose it after Run() returns.
        _runningGame = game;
        activity.SetContentView(view);
        ViewAttached = true;
        Report("GAME_VIEW_ATTACHED");
    }

    public static void Stop()
    {
        try
        {
            var game = _runningGame;
            _runningGame = null;
            game?.Dispose();
        }
        catch (Exception ex)
        {
            global::Android.Util.Log.Warn("KabraBoot", "OnDestroy cleanup: " + ex.GetType().Name);
        }
        ViewAttached = false;
    }
}
