using Android.Views;
using Microsoft.Xna.Framework;
namespace Intersect.Client.Core;

/// <summary>Android bootstrap for the actual Intersect .339 runner, with traceable stages.</summary>
public static class AndroidBridge
{
    private static AndroidGameActivity? _activity;
    private static readonly object Sync = new();
    private static Action<string>? _status;
    private static string? _logPath;

    // Matches the diagnostic button in MainActivity. Persist BEFORE entering Intersect.
    public static void BeginDiagnostics(string writableGameRoot)
    {
        Directory.CreateDirectory(writableGameRoot);
        lock (Sync)
        {
            _logPath = Path.Combine(writableGameRoot, "kabra_android_boot.log");
            File.WriteAllText(_logPath, DateTime.UtcNow.ToString("O") +
                " | KABRA_ANDROID_LOG_STARTED" + Environment.NewLine);
        }
        global::Android.Util.Log.Info("KabraBootstrap", "KABRA_ANDROID_LOG_STARTED");
    }

    private static void Persist(string stage)
    {
        global::Android.Util.Log.Info("KabraBootstrap", stage);
        lock (Sync)
        {
            if (_logPath == null) return;
            try
            {
                File.AppendAllText(_logPath, DateTime.UtcNow.ToString("O") +
                    " | " + stage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("KabraBootstrap", "BOOT_LOG_WRITE_FAILED: " + ex);
            }
        }
    }
    public static Action<string>? StageChanged { get; set; }
    public static bool ViewAttached { get; private set; }
    private static Game? _game;

    public static Task StartAsync(AndroidGameActivity activity, string writableGameRoot, Action<string> status)
    {
        if (activity == null) throw new ArgumentNullException(nameof(activity));
        if (status == null) throw new ArgumentNullException(nameof(status));
        lock (Sync) { _activity = activity; _status = status; ViewAttached = false; _game = null; }
        // Ensure diagnostics exist even when AndroidBridge is started directly.
        if (_logPath == null) BeginDiagnostics(writableGameRoot);
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
                Persist("ANDROID_BOOTSTRAP_EXCEPTION: " + ex);
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
        Persist(stage);
        try { callback?.Invoke(stage); }
        catch (Exception ex) { Persist("BOOT_STATUS_CALLBACK_FAILED: " + ex.Message); }
        try { StageChanged?.Invoke(stage); }
        catch (Exception ex) { Persist("BOOT_STAGE_CALLBACK_FAILED: " + ex.Message); }
    }

    private static void Report(Action<string> status, string message)
    {
        Persist(message);
        try { status(message); }
        catch (Exception ex) { Persist("BOOT_STATUS_CALLBACK_FAILED: " + ex.Message); }
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
        Persist("ANDROID_BRIDGE_STOP");
        try { game?.Exit(); }
        catch (Exception ex) { global::Android.Util.Log.Warn("KabraBootstrap", "GAME_EXIT_FAILED: " + ex.Message); }
    }

    internal static void AttachGame(Game game)
    {
        AndroidGameActivity activity;
        lock (Sync) activity = _activity ?? throw new InvalidOperationException("ANDROID_ACTIVITY_NOT_READY");
        Persist("ANDROID_GAME_ATTACH_BEGIN");
        // Android MonoGame may not register its View until initialized. This is
        // an explicit diagnostic, not a fallback dummy view/menu.
        var view = game.Services.GetService(typeof(View)) as View;
        if (view == null)
        {
            Persist("KABRA_ANDROID_GAME_VIEW_MISSING: MonoGame View service not initialized");
            throw new InvalidOperationException("KABRA_ANDROID_GAME_VIEW_MISSING: MonoGame view was not initialized");
        }
        using var done = new ManualResetEventSlim(false);
        Exception? attachError = null;
        activity.RunOnUiThread(() =>
        {
            try { activity.SetContentView(view); }
            catch (Exception ex) { attachError = ex; }
            finally { done.Set(); }
        });
        if (!done.Wait(TimeSpan.FromSeconds(10)))
        {
            Persist("KABRA_ANDROID_ATTACH_UI_TIMEOUT");
            throw new TimeoutException("KABRA_ANDROID_ATTACH_UI_TIMEOUT");
        }
        if (attachError != null)
        {
            Persist("KABRA_ANDROID_ATTACH_VIEW_FAILED: " + attachError);
            throw new InvalidOperationException("KABRA_ANDROID_ATTACH_VIEW_FAILED", attachError);
        }
        lock (Sync) { _game = game; ViewAttached = true; }
        Report("ANDROID_GAME_ATTACH_DONE");
    }
}