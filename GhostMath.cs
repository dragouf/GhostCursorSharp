using System;
using System.Collections.Generic;
using System.Geometry;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GhostCursorSharp
{
    /*public class Vector2
    {
        public double X { get; set; }
        public double Y { get; set; }
    }*/

    public class GhostMath
    {
        public static Vector2 Origin = new Vector2 { X = 0, Y = 0 };

        public static Bezier BezierCurve(Vector2 start, Vector2 finish, double? overrideSpread)
        {
            // could be played around with
            var min = 2;
            var max = 200;
            var vec = Direction(start, finish);
            var length = Magnitude(vec);
            var spread = Clamp(length, min, max);
            var anchors = GenerateBezierAnchors(start, finish, overrideSpread ?? spread);

            return new Bezier(start, anchors.Left, anchors.Right, finish);
        }

        public static Pair<Vector2> GenerateBezierAnchors(Vector2 a, Vector2 b, double spread)
        {
            var randomizer = new Random();

            var side = Math.Round(randomizer.NextDouble()) == 1 ? 1 : -1;
            Func<Vector2> calc = () =>
            {
                var result = RandomNormalLine(a, b, spread); // [randMid, normalV]
                var randMid = result.Left;
                var normalV = result.Right;

                var choice = Mult(normalV, side);

                return RandomVector2OnLine(randMid, Add(randMid, choice));
            };

            var sortedRandomVector2s = new List<Vector2> { calc(), calc() }.OrderBy(a => a.X);

            return new Pair<Vector2>(sortedRandomVector2s.First(), sortedRandomVector2s.Last());
        }


        public static Vector2 Direction(Vector2 a, Vector2 b)
        {
            return Sub(b, a);
        }

        public static Vector2 Sub(Vector2 a, Vector2 b)
        {
            return new Vector2 { X = a.X - b.X, Y = a.Y - b.Y };
        }

        public static Vector2 Div(Vector2 a, double b)
        {
            return new Vector2 { X = Convert.ToSingle(a.X / b), Y = Convert.ToSingle(a.Y / b) };
        }

        public static Vector2 Mult(Vector2 a, double b)
        {
            return new Vector2 { X = Convert.ToSingle(a.X * b), Y = Convert.ToSingle(a.Y * b) };
        }

        public static Vector2 Add(Vector2 a, Vector2 b)
        {
            return new Vector2 { X = a.X + b.X, Y = a.Y + b.Y };
        }

        public static Vector2 Perpendicular(Vector2 a)
        {
            return new Vector2 { X = a.Y, Y = -1 * a.X };
        }

        public static double Magnitude(Vector2 a)
        {
            return Math.Sqrt(Math.Pow(a.X, 2) + Math.Pow(a.Y, 2));
        }

        public static Vector2 Unit(Vector2 a)
        {
            return Div(a, Magnitude(a));
        }

        public static Vector2 setMagnitude(Vector2 a, double amount)
        {
            return Mult(Unit(a), amount);
        }

        public static double RandomNumberRange(int min, int max)
        {
            var randomizer = new Random();

            return randomizer.NextDouble() * (max - min) + min;
        }

        public static Vector2 RandomVector2OnLine(Vector2 a, Vector2 b)
        {
            var randomizer = new Random();
            var vec = Direction(a, b);
            var multiplier = randomizer.NextDouble();

            return Add(a, Mult(vec, multiplier));
        }

        public static Pair<Vector2> RandomNormalLine(Vector2 a, Vector2 b, double range)
        {
            var randMid = RandomVector2OnLine(a, b);
            var normalV = setMagnitude(Perpendicular(Direction(a, randMid)), range);

            return new Pair<Vector2>(randMid, normalV);
        }

        public static double Clamp(double target, double min, double max)
        {
            return Math.Min(max, Math.Max(min, target));
        }

        public static Vector2 Overshoot(Vector2 coordinate, double radius)
        {
            var randomizer = new Random();
            var a = randomizer.NextDouble() * 2 * Math.PI;
            var rad = radius * Math.Sqrt(randomizer.NextDouble());
            var vector = new Vector2 { X = Convert.ToSingle(rad * Math.Cos(a)), Y = Convert.ToSingle(rad * Math.Sin(a)) };

            return Add(coordinate, vector);
        }
    }
}
