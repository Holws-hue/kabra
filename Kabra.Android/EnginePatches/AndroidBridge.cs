using Android.Views;
using Microsoft.Xna.Framework;
namespace Intersect.Client.Core;

/// <summary>Android bootstrap for the actual Intersect .339 runner, with traceable stages.</summary>
public static class AndroidBridge
{
    private static AndroidGameActivity? _activity;
    private static readonly object Sync = new();
    private static Action<string>? _status;
    public static Action<string>? StageChanged { get; set; }
    public static bool ViewAttached { get; private set; }
    private static Game? _game;

    public static Task StartAsync(AndroidGameActivity activity, string writableGameRoot, Action<string> status)
    {
        if (activity == null) throw new ArgumentNullException(nameof(activity));
        if (status == null) throw new ArgumentNullException(nameof(status));
        lock (Sync) { _activity = activity; _status = status; ViewAttached = false; _game = null; }
        // The original Bootstrapper.Start() blocks the Activity's UI event handler.
        // Keep Android's UI available to surface exceptions and show startup status.
        return Task.Run(() =>
        {
            try
            {
                Report(status, "ANDROID_BOOTSTRAP_BEGIN");
                Environment.CurrentDirectory = writableGameRoot;
                Report(status, "ANDROID_BOOTSTRAP_WORKDIR_OK");
                Bootstrapper.Start();
                Report(status, "ANDROID_BOOTSTRAP_RETURNED");
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("KabraBootstrap", ex.ToString());
                Report(status, "ANDROID_BOOTSTRAP_FAILED: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        });
    }

    // Called by the Android-only diagnostic hooks patched into Intersect.
    public static void Report(string stage)
    {
        Action<string>? callback;
        lock (Sync) callback = _status;
        global::Android.Util.Log.Info("KabraBootstrap", stage);
        callback?.Invoke(stage);
        StageChanged?.Invoke(stage);
    }

    private static void Report(Action<string> status, string message)
    {
        global::Android.Util.Log.Info("KabraBootstrap", message);
        status(message);
    }

    public static void Stop()
    {
        Game? game;
        lock (Sync)
        {
            game = _game;
            _game = null;
            _activity = null;
            _status = null;
            ViewAttached = false;
        }
        try { game?.Exit(); }
        catch (Exception ex) { global::Android.Util.Log.Warn("KabraBootstrap", "GAME_EXIT_FAILED: " + ex.Message); }
    }

    internal static void AttachGame(Game game)
    {
        AndroidGameActivity activity;
        lock (Sync) activity = _activity ?? throw new InvalidOperationException("ANDROID_ACTIVITY_NOT_READY");
        global::Android.Util.Log.Info("KabraBootstrap", "ANDROID_GAME_ATTACH_BEGIN");
        // Android MonoGame may not register its View until initialized. This is
        // an explicit diagnostic, not a fallback dummy view/menu.
        var view = game.Services.GetService(typeof(View)) as View;
        if (view == null)
            throw new InvalidOperationException("KABRA_ANDROID_GAME_VIEW_MISSING: MonoGame view was not initialized");
        using var done = new ManualResetEventSlim(false);
        Exception? attachError = null;
        activity.RunOnUiThread(() =>
        {
            try { activity.SetContentView(view); }
            catch (Exception ex) { attachError = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("KABRA_ANDROID_ATTACH_UI_TIMEOUT");
        if (attachError != null)
            throw new InvalidOperationException("KABRA_ANDROID_ATTACH_VIEW_FAILED", attachError);
        lock (Sync) { _game = game; ViewAttached = true; }
        Report("ANDROID_GAME_ATTACH_DONE");
    }
}