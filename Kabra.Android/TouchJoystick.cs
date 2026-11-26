using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;
namespace Kabra.Android;
// Android touch input groundwork. Must be connected to Intersect MonoInput.
internal static class TouchJoystick
{
    public static Point ReadDirection(TouchCollection touches, int screenWidth, int screenHeight)
    {
        foreach (var touch in touches)
        {
            if (touch.State is not (TouchLocationState.Moved or TouchLocationState.Pressed)) continue;
            if (touch.Position.X >= screenWidth * 0.45f || touch.Position.Y <= screenHeight * 0.35f) continue;
            var center = new Vector2(screenWidth * 0.18f, screenHeight * 0.77f);
            var delta = touch.Position - center;
            if (delta.LengthSquared() < (screenHeight * 0.04f) * (screenHeight * 0.04f)) return Point.Zero;
            return new Point(Math.Sign(delta.X), Math.Sign(delta.Y));
        }
        return Point.Zero;
    }
}