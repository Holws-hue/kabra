using Android.Views;
using Microsoft.Xna.Framework;

namespace Intersect.Client.Core;

/// <summary>
/// Android-specific bridge for the real Intersect client. Non-UI bootstrap work
/// may run in a worker, but MonoGame Game creation, view attachment and Run()
/// MUST execute on the Android UI thread.
/// </summary>
public static class AndroidBridge
{
    private static readonly object Sync = new();
    private static AndroidGameActivity? _activity;
    private static Game? _game;
    private static string? _logPath;
    private static Action<string>? _status;

    public static Action<string>? StageChanged { get; set; }
    public static bool ViewAttached { get; private set; }

    public static void BeginDiagnostics(string writableGameRoot)
    {
        Directory.CreateDirectory(writableGameRoot);
        lock (Sync)
        {
            _logPath = Path.Combine(writableGameRoot, "kabra_android_boot.log");
            File.WriteAllText(_logPath,
                DateTime.UtcNow.ToString("O") + " | KABRA_ANDROID_LOG_STARTED" + Environment.NewLine);
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
                File.AppendAllText(_logPath,
                    DateTime.UtcNow.ToString("O") + " | " + stage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("KabraBootstrap", "BOOT_LOG_WRITE_FAILED: " + ex);
            }
        }
    }

    public static void Report(string stage)
    {
        Persist(stage);
        Action<string>? callback;
        lock (Sync) callback = _status;
        try { callback?.Invoke(stage); }
        catch (Exception ex) { Persist("BOOT_STATUS_CALLBACK_FAILED: " + ex.Message); }
        try { StageChanged?.Invoke(stage); }
        catch (Exception ex) { Persist("BOOT_STAGE_CALLBACK_FAILED: " + ex.Message); }
    }

    public static Task StartAsync(AndroidGameActivity activity, string writableGameRoot, Action<string> status)
    {
        if (activity == null) throw new ArgumentNullException(nameof(activity));
        if (status == null) throw new ArgumentNullException(nameof(status));
        lock (Sync)
        {
            _activity = activity;
            _status = status;
            _game = null;
            ViewAttached = false;
        }
        if (_logPath == null) BeginDiagnostics(writableGameRoot);
        return Task.Run(() =>
        {
            try
            {
                Report("ANDROID_BOOTSTRAP_BEGIN");
                Environment.CurrentDirectory = writableGameRoot;
                Report("ANDROID_BOOTSTRAP_WORKDIR_OK");
                Bootstrapper.Start();
                Report("ANDROID_BOOTSTRAP_RETURNED");
            }
            catch (Exception ex)
            {
                Persist("ANDROID_BOOTSTRAP_EXCEPTION: " + ex);
                Report("ANDROID_BOOTSTRAP_FAILED: " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }
        });
    }

    /// <summary>
    /// Called by the Android-only patch inside IntersectGame.MonoGameRunner.Start.
    /// Do not create the Game in Task.Run: Android's MonoGame GamePlatform and
    /// GL view depend on the Activity's main looper.
    /// </summary>
    public static void RunGameOnUiThread(Action createAttachAndRun)
    {
        if (createAttachAndRun == null) throw new ArgumentNullException(nameof(createAttachAndRun));
        AndroidGameActivity activity;
        lock (Sync)
            activity = _activity ?? throw new InvalidOperationException("ANDROID_ACTIVITY_NOT_READY");

        if (global::Android.OS.Looper.MyLooper() == global::Android.OS.Looper.MainLooper)
        {
            Report("ANDROID_GAME_UI_THREAD_CONFIRMED");
            createAttachAndRun();
            return;
        }

        Report("ANDROID_GAME_UI_DISPATCH_QUEUED");
        using var completed = new ManualResetEventSlim(false);
        Exception? failure = null;
        activity.RunOnUiThread(() =>
        {
            try
            {
                Report("ANDROID_GAME_UI_THREAD_CONFIRMED");
                createAttachAndRun();
            }
            catch (Exception ex)
            {
                failure = ex;
                Persist("ANDROID_GAME_UI_EXCEPTION: " + ex);
            }
            finally
            {
                completed.Set();
            }
        });

        if (!completed.Wait(TimeSpan.FromSeconds(45)))
        {
            Persist("ANDROID_GAME_UI_DISPATCH_TIMEOUT");
            throw new TimeoutException("ANDROID_GAME_UI_DISPATCH_TIMEOUT");
        }
        if (failure != null)
            throw new InvalidOperationException("ANDROID_GAME_UI_INIT_FAILED: " + failure.Message, failure);
    }

    internal static void AttachGame(Game game)
    {
        AndroidGameActivity activity;
        lock (Sync)
            activity = _activity ?? throw new InvalidOperationException("ANDROID_ACTIVITY_NOT_READY");

        Report("ANDROID_GAME_ATTACH_BEGIN");
        if (global::Android.OS.Looper.MyLooper() != global::Android.OS.Looper.MainLooper)
            throw new InvalidOperationException("ANDROID_GAME_ATTACH_WRONG_THREAD");

        var view = game.Services.GetService(typeof(View)) as View;
        if (view == null)
            throw new InvalidOperationException("KABRA_ANDROID_GAME_VIEW_MISSING");

        activity.SetContentView(view);
        lock (Sync)
        {
            _game = game; // Retain while Android runs the asynchronous game loop.
            ViewAttached = true;
        }
        Report("ANDROID_GAME_ATTACH_DONE");
    }

    public static void Stop()
    {
        Game? game;
        AndroidGameActivity? activity;
        lock (Sync)
        {
            game = _game;
            activity = _activity;
            _game = null;
            _activity = null;
            _status = null;
            ViewAttached = false;
        }
        Report("ANDROID_BRIDGE_STOP");
        if (game == null || activity == null) return;
        activity.RunOnUiThread(() =>
        {
            try { game.Exit(); }
            catch (Exception ex) { Persist("ANDROID_GAME_EXIT_FAILED: " + ex); }
        });
    }
}
