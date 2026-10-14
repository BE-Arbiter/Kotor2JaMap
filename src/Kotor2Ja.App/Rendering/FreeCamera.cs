using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Kotor2Ja.App.Rendering;

/// <summary>
/// Camera libre, repere JA (Z vers le haut, unites Quake). Yaw en degres autour de Z (0 = +X), pitch en degres (positif = vers le haut).
/// Clic droit maintenu pour regarder ; ZQSD / WASD pour se deplacer ; Espace/E monter, Ctrl/C descendre ; Maj accelere ; molette = vitesse.
/// </summary>
public sealed class FreeCamera
{
    private const float BaseSpeed = 600f;   // unites JA par seconde
    private const float Sensitivity = 0.15f; // degres par pixel
    private float _speedFactor = 1f;
    private bool _looking;

    public Vector3 Position { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float FieldOfView { get; set; } = 70f;
    public float Near { get; set; } = 4f;
    public float Far { get; set; } = 200000f;

    public Vector3 Forward
    {
        get
        {
            float y = MathHelper.ToRadians(Yaw), p = MathHelper.ToRadians(Pitch);
            return new Vector3(MathF.Cos(y) * MathF.Cos(p), MathF.Sin(y) * MathF.Cos(p), MathF.Sin(p));
        }
    }

    public Matrix View => Matrix.CreateLookAt(Position, Position + Forward, Vector3.UnitZ);

    public Matrix Projection(float aspect) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(FieldOfView), aspect, Near, Far);

    /// <summary>Place la camera pour voir une boite : en retrait sur la diagonale, regardant le centre.</summary>
    public void Frame(Vector3 min, Vector3 max)
    {
        Vector3 center = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 64f);
        Yaw = 45f;
        Pitch = -25f;
        Position = center - Forward * (radius * 1.6f);
        _speedFactor = 1f;
    }

    public void PlaceAt(Vector3 position, float yaw)
    {
        Position = position;
        Yaw = yaw;
        Pitch = 0f;
        _speedFactor = 1f;
    }

    /// <summary>
    /// Met a jour la camera. <paramref name="captured"/> : ImGui capture le clavier/la souris (entrees ignorees).
    /// <paramref name="center"/> : centre de la zone 3D en pixels fenetre, pour recentrer la souris pendant la rotation.
    /// </summary>
    public void Update(float dt, KeyboardState kb, MouseState mouse, int lastWheel, bool mouseCaptured, bool keyboardCaptured,
        bool windowActive, Point center, out bool lookingNow)
    {
        bool rmb = mouse.RightButton == ButtonState.Pressed;
        if (!rmb || !windowActive) _looking = false;
        else if (!_looking && !mouseCaptured) { _looking = true; Mouse.SetPosition(center.X, center.Y); lookingNow = true; return; }

        if (_looking)
        {
            Yaw -= (mouse.X - center.X) * Sensitivity;
            Pitch = MathHelper.Clamp(Pitch - (mouse.Y - center.Y) * Sensitivity, -89f, 89f);
            Mouse.SetPosition(center.X, center.Y);
        }
        lookingNow = _looking;

        if (!mouseCaptured && mouse.ScrollWheelValue != lastWheel)
            _speedFactor = MathHelper.Clamp(_speedFactor * MathF.Pow(1.2f, (mouse.ScrollWheelValue - lastWheel) / 120f), 0.02f, 200f);

        if (keyboardCaptured || !windowActive) return;
        Vector3 fwd = Forward;
        Vector3 flat = new(MathF.Cos(MathHelper.ToRadians(Yaw)), MathF.Sin(MathHelper.ToRadians(Yaw)), 0f);
        Vector3 right = new(flat.Y, -flat.X, 0f);
        Vector3 move = Vector3.Zero;
        if (kb.IsKeyDown(Keys.Z) || kb.IsKeyDown(Keys.W)) move += fwd;
        if (kb.IsKeyDown(Keys.S)) move -= fwd;
        if (kb.IsKeyDown(Keys.D)) move += right;
        if (kb.IsKeyDown(Keys.Q) || kb.IsKeyDown(Keys.A)) move -= right;
        if (kb.IsKeyDown(Keys.Space) || kb.IsKeyDown(Keys.E)) move += Vector3.UnitZ;
        if (kb.IsKeyDown(Keys.LeftControl) || kb.IsKeyDown(Keys.RightControl) || kb.IsKeyDown(Keys.C)) move -= Vector3.UnitZ;
        if (move == Vector3.Zero) return;
        float speed = BaseSpeed * _speedFactor * (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift) ? 4f : 1f);
        Position += Vector3.Normalize(move) * speed * dt;
    }
}
