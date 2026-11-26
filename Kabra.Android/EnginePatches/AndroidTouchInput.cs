using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;

namespace Intersect.Client.MonoGame.Input;

/// <summary>Adapter only: feeds the existing Intersect key and Gwen mouse paths.</summary>
internal static class AndroidTouchInput
{
    internal static KeyboardState ApplyMovement(KeyboardState keyboard, TouchCollection touches, int width, int height)
    {
        var keys = keyboard.GetPressedKeys().ToList();
        var center = new Vector2(width * .18f, height * .77f);
        var deadZone = height * .04f;
        foreach (var touch in touches)
        {
            if (touch.State is not (TouchLocationState.Pressed or TouchLocationState.Moved)) continue;
            if (touch.Position.X >= width * .45f || touch.Position.Y <= height * .35f) continue;
            var delta = touch.Position - center;
            if (delta.LengthSquared() <= deadZone * deadZone) break;
            if (Math.Abs(delta.X) > deadZone) keys.Add(delta.X > 0 ? Keys.Right : Keys.Left);
            if (Math.Abs(delta.Y) > deadZone) keys.Add(delta.Y > 0 ? Keys.Down : Keys.Up);
            break;
        }
        return new KeyboardState(keys.Distinct().ToArray(), keyboard.CapsLock, keyboard.NumLock);
    }

    internal static MouseState ApplyPointer(MouseState mouse, TouchCollection touches, int width, int height)
    {
        foreach (var touch in touches)
        {
            if (touch.State is not (TouchLocationState.Pressed or TouchLocationState.Moved)) continue;
            // The left-bottom area is the movement joystick; the rest of the display
            // acts as a primary pointer for login and character selection.
            if (touch.Position.X < width * .45f && touch.Position.Y > height * .35f) continue;
            return new MouseState(
                (int)touch.Position.X, (int)touch.Position.Y, mouse.ScrollWheelValue,
                ButtonState.Pressed, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released, mouse.HorizontalScrollWheelValue
            );
        }
        return new MouseState(
            mouse.X, mouse.Y, mouse.ScrollWheelValue, ButtonState.Released,
            ButtonState.Released, ButtonState.Released, ButtonState.Released,
            ButtonState.Released, mouse.HorizontalScrollWheelValue
        );
    }
}