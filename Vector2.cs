using System;
using System.Numerics;

namespace GCodeGenerator
{
    public class Vector2
    {
        public static Vector2 Zero = new Vector2(0, 0);
        public static Vector2 UnitY = new Vector2(0, 1);
        public static Vector2 UnitX = new Vector2(1, 0);

        public double X { get; set; }
        public double Y { get; set; }

        public Vector2(double x, double y)
        {
            X = x;
            Y = y;
        }
        public double Length() => Math.Sqrt(X * X + Y * Y);
        public Vector2 Normalize() => new Vector2(X / Length(), Y / Length());
        public Vector2 Invert() => new Vector2(-X, -Y);
        /// <summary>Only use with unit vectors</summary>
        public Vector2 Clerp(Vector2 to, double amount, bool clockwise)
        {
            if (amount >= 1)
                return new Vector2(to.X, to.Y);
            if (amount <= 0)
                return new Vector2(X,Y);

            double opposite = (to - this).Length() * 0.5;
            double angle = (clockwise ? -2 : 2) * Math.Asin(opposite);

            Vector2 rotateBy = new Vector2(Math.Cos(angle*amount), Math.Sin(angle*amount));
            Vector2 result = this * rotateBy;
            return result;
        }

        public static Vector2 operator *(Vector2 A, Vector2 B) => new Vector2(A.X*B.X - A.Y*B.Y, A.X*B.Y + A.Y*B.X).Normalize();
        public static Vector2 operator *(Vector2 Multiplicand, double Multiplier) => new Vector2(Multiplicand.X * Multiplier, Multiplicand.Y * Multiplier);
        public static Vector2 operator +(Vector2 A, Vector2 B) => new Vector2(A.X + B.X, A.Y + B.Y);
        public static Vector2 operator -(Vector2 A, Vector2 B) => new Vector2(A.X - B.X, A.Y - B.Y);
        public static implicit operator Vector2((double X, double Y) tuple) => new Vector2(tuple.X, tuple.Y );
        public static implicit operator Quaternion(Vector2 vector) => new Quaternion((float)vector.X, (float)vector.Y, 0, 0);
        public static explicit operator Vector2(Quaternion quaternion) => new Vector2(quaternion.X, quaternion.Y);
    }
}
