// Offline doubles for the small Unity surface used by the linked production
// files. These test transition logic; they do not simulate an XR runtime.
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
    }
    public struct Vector3
    {
        internal NVector value;
        public Vector3(float x, float y, float z) { value = new(x, y, z); }
        internal Vector3(NVector v) { value = v; }
        public static Vector3 up => new(0, 1, 0);
        public static Vector3 forward => new(0, 0, 1);
        public static Vector3 right => new(1, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.value + b.value);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.value - b.value);
        public static Vector3 operator -(Vector3 a) => new(-a.value);
        public static Vector3 operator *(Vector3 a, float b) => new(a.value * b);
        public static float Distance(Vector3 a, Vector3 b) => NVector.Distance(a.value, b.value);
        public override string ToString() => value.ToString();
    }
    public struct Quaternion
    {
        internal NQuaternion value;
        private Quaternion(NQuaternion v) { value = v; }
        public static Quaternion identity => new(NQuaternion.Identity);
        public static Quaternion AngleAxis(float degrees, Vector3 axis) => new(NQuaternion.CreateFromAxisAngle(axis.value, degrees * MathF.PI / 180));
        public static Quaternion Inverse(Quaternion q) => new(NQuaternion.Inverse(q.value));
        public static Quaternion operator *(Quaternion a, Quaternion b) => new(a.value * b.value);
        public static Vector3 operator *(Quaternion a, Vector3 b) => new(NVector.Transform(b.value, a.value));
        public static float Angle(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Clamp(MathF.Abs(NQuaternion.Dot(NQuaternion.Normalize(a.value), NQuaternion.Normalize(b.value))), 0, 1)) * 180 / MathF.PI;
    }
    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation = Quaternion.identity;
    }
    public static class Mathf
    {
        public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
        public static float DeltaAngle(float current, float target)
        {
            float d = ((target - current) % 360 + 360) % 360;
            return d > 180 ? d - 360 : d;
        }
    }
}
