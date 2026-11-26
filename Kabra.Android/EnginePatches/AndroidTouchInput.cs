using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace Intersect.Client.MonoGame.Input;

/// <summary>
/// Android gamepad: existing Intersect keyboard and Gwen pointer paths remain
/// authoritative; hardware input and native on-screen controls are merged.
/// </summary>
internal static class AndroidTouchInput
{
    private static readonly object KeySync = new();
    private static readonly HashSet<Keys> VirtualKeys = new();

    internal static void SetVirtualKey(Keys key, bool pressed)
    {
        lock (KeySync)
        {
            if (pressed) VirtualKeys.Add(key);
            else VirtualKeys.Remove(key);
        }
    }

    internal static void ReleaseAll()
    {
        lock (KeySync) VirtualKeys.Clear();
    }

    internal static KeyboardState ApplyMovement(
        KeyboardState keyboard, TouchCollection touches, int width, int height, bool isInGame)
    {
        var keys = keyboard.GetPressedKeys().ToList();
        lock (KeySync) keys.AddRange(VirtualKeys);

        // Preserve the gesture joystick for player movement only, not the login
        // screen. D-pad native overlay also works independently of TouchPanel.
        if (isInGame)
        {
            var center = new Vector2(width * .18f, height * .77f);
            var deadZone = Math.Max(9f, height * .04f);
            foreach (var touch in touches)
            {
                if (touch.State is not (TouchLocationState.Pressed or TouchLocationState.Moved))
                    continue;
                if (touch.Position.X >= width * .45f || touch.Position.Y <= height * .35f)
                    continue;
                var delta = touch.Position - center;
                if (delta.LengthSquared() <= deadZone * deadZone) break;
                if (Math.Abs(delta.X) > deadZone)
                    keys.Add(delta.X > 0 ? Keys.Right : Keys.Left);
                if (Math.Abs(delta.Y) > deadZone)
                    keys.Add(delta.Y > 0 ? Keys.Down : Keys.Up);
                break;
            }
        }
        return new KeyboardState(keys.Distinct().ToArray(), keyboard.CapsLock, keyboard.NumLock);
    }

    internal static MouseState ApplyPointer(
        MouseState mouse, TouchCollection touches, int width, int height, bool isInGame)
    {
        foreach (var touch in touches)
        {
            if (touch.State is not (TouchLocationState.Pressed or TouchLocationState.Moved))
                continue;
            if (isInGame &&
                touch.Position.X < width * .45f && touch.Position.Y > height * .35f)
                continue; // left lower joystick zone while playing
            return new MouseState(
                (int)touch.Position.X, (int)touch.Position.Y, mouse.ScrollWheelValue,
                ButtonState.Pressed, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released, mouse.HorizontalScrollWheelValue);
        }
        return new MouseState(
            mouse.X, mouse.Y, mouse.ScrollWheelValue, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, mouse.HorizontalScrollWheelValue);
    }
}
