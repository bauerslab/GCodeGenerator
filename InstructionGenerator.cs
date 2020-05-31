using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Vector2 = GCodeGenerator.Vector2;

namespace GCodeGenerator
{
    public class Character
    {
        public double Width { get; set; }

        /// <summary>
        /// Generates instructions to draw the character
        /// </summary>
        public Func<string> Instructions;
    }

    public class Arc
    {
        public Vector2 Start { get; set; }
        public Vector2 End { get; set; }
        public double Radius { get; set; }
        public bool Clockwise { get; set; }

        public static Arc RoundCorner(Vector2 before, Vector2 rounded, Vector2 after, double desiredRadius)
        {
            Arc arc = new Arc();

            Vector2 from = before - rounded;
            Vector2 to = after - rounded;

            //Half of angle between "from" and "to" vectors
            double halfDifAngle = (Math.Atan2(from.Y, from.X) - Math.Atan2(to.Y, to.X)) / 2;

            //Distance between rounded angle point and center of circle
            double tan = Math.Abs(Math.Tan(halfDifAngle));
            double segmentLength = desiredRadius / tan;

            //Check for room
            double length = Math.Min(from.Length(), to.Length());
            arc.Radius = desiredRadius;
            //If not enough room, tighten radius
            if (segmentLength > length)
            {
                //throw new Exception("Line is too short to be rounded by this radius");
                segmentLength = length;
                arc.Radius = length * tan;
            }

            //get start and end points by ratio of segment length to total length
            arc.Start = rounded + from * (segmentLength / from.Length());
            arc.End = rounded + to * (segmentLength / to.Length());

            // Calculation of the coordinates of the circle center by the addition of angular vectors.
            Vector2 d = rounded * 2 - arc.Start - arc.End;
            Vector2 r = new Vector2((float)segmentLength, (float)arc.Radius);
            Vector2 circlePoint = rounded + d * (r.Length() / d.Length());

            //Calculate sweep angle to see if we are going clockwise or counterclockwise
            var startAngle = Math.Atan2(arc.Start.Y - circlePoint.Y, arc.Start.X - circlePoint.X);
            var endAngle = Math.Atan2(arc.End.Y - circlePoint.Y, arc.End.X - circlePoint.X);
            var sweepAngle = endAngle - startAngle;
            arc.Clockwise = sweepAngle > 0;

            return arc;
        }
    }

    public static class InstructionGenerator
    {
        /// <summary>Generates instructions for Initialization</summary>
        public static Func<string> Initialize { get; set; } = () => $@"G90
G17
G21
G1 F{Speed}";
        /// <summary>Generates instructions for drawing a character and advances the drawing position for the next character.</summary>
        /// <param name="c">The character to draw</param>
        public static string Execute(char c)
        {
            if (!Characters.ContainsKey(c))
                throw new ArgumentException("Character not available in font", nameof(c));

            var instructions = Characters[c].Instructions();

            //advance to next position
            baseLeft += Vector2.UnitX * ((float)Characters[c].Width * fontSize + fontSpacing);

            return instructions;
        }
        /// <summary>Generates instruction to return to home position</summary>
        public static Func<string> GoHome { get; set; } = () => $"\nG0 X0 Y0";

        /// <summary>Traverse speed while writing.</summary>
        public static double Speed { get; set; } = 500;
        ///<summary>Current writing location.</summary>
        public static Vector2 TopLeft
        {
            get => baseLeft + Vector2.UnitY * fontSize;
            set => baseLeft = value - Vector2.UnitY * fontSize;
        }
        /// <summary>Font size in gcode machine units. Some characters extend 50% below baseline, so total height needed is 1.5 times this value.</summary>
        public static double FontSize
        {
            get => fontSize;
            set
            {
                //get topLeft and radiusRatio since it will be lost when fontSize changes
                var topLeft = TopLeft;
                var spacingRatio = FontSpacing;
                fontSize = value;
                baseLeft = topLeft - Vector2.UnitY * fontSize;
                fontSpacing = fontSize * spacingRatio;
            }
        }
        /// <summary>Curve radius for rounding hard corners in machine units</summary>
        public static double FontRadius { get; set; } = 2;
        /// <summary>The distance between points when interpolating an arc as lines</summary>
        public static double ArcInterpolationDistance { get; set; } = 1;
        /// <summary>Horizontal spacing between characters relative to font size (default: 0.1)</summary>
        public static double FontSpacing { get => fontSpacing / FontSize; set => fontSpacing = value * FontSize; }
        /// <summary>The radius of circle used to create a dot relative to font size. (default: 0.025)</summary>
        public static double DotSize { get; set; } = 0.025f;

        private static Vector2 baseLeft = Vector2.Zero;
        private static double fontSize = 60;
        private static double fontRadius => FontRadius / fontSize;
        private static double fontSpacing = 6;

        //Geometric calculation functions
        /// <summary>
        /// Finds a point on circle defined by center and radius such that the line to it from point will be tangent
        /// </summary>
        /// <param name="point">the starting point</param>
        /// <param name="center">the center of the arc</param>
        /// <param name="radius">the radius of the arc</param>
        /// <param name="clockwise">which side of the circle </param>
        private static Vector2 FindTangentPoint(Vector2 point, Vector2 center, double radius, bool clockwise)
        {
            var Beta = center - point;
            var distance = Beta.Length();

            var alpha = Math.Asin(radius / distance);
            var beta = Math.Atan2(Beta.Y, Beta.X);

            var theta = beta + (clockwise ? alpha : -alpha);
            var tangentLength = (float)Math.Sqrt(distance * distance - radius * radius);
            Vector2 tangentPoint = new Vector2((float)Math.Cos(theta), (float)Math.Sin(theta));
            tangentPoint *= tangentLength;
            tangentPoint += point;

            return tangentPoint;
        }
        /// <summary>
        /// Finds points on two circles to create a line tangent to both
        /// </summary>
        /// <param name="oCenter">Center of "from" circle</param>
        /// <param name="oRadius">Radius of "from" circle</param>
        /// <param name="qCenter">Center of "to" circle</param>
        /// <param name="qRadius">Radius of "to" circle</param>
        /// <param name="oClockwise">Whether to exit "from" circle on the clockwise side</param>
        /// <param name="qClockwise">Whether to enter "to" circle from the clockwise side</param>
        /// <returns></returns>
        private static (Vector2 O, Vector2 Q) FindTangentPoints(Vector2 oCenter, double oRadius, Vector2 qCenter, double qRadius, bool oClockwise, bool qClockwise)
        {
            var circleVector = qCenter - oCenter;
            var distance = circleVector.Length();

            if (distance <= Math.Abs(oRadius - qRadius))
                throw new Exception("Can't draw tangent line connecting circles that are inside each other.");

            Vector2 oTangent, qTangent;
            Vector2 tangentIntersect;
            bool oInverse, qInverse;

            if (oClockwise == qClockwise) //outside
            {
                if (oRadius == qRadius)
                {
                    Vector2 offset =
                        new Vector2(-circleVector.Y, circleVector.X).Normalize()
                        * (oClockwise ? oRadius : -oRadius);

                    oTangent = oCenter + offset;
                    qTangent = qCenter + offset;
                    return (oTangent, qTangent);
                }
                else
                {
                    tangentIntersect = new Vector2(
                        (qCenter.X * oRadius - oCenter.X * qRadius) / (oRadius - qRadius),
                        (qCenter.Y * oRadius - oCenter.Y * qRadius) / (oRadius - qRadius));

                    //is tangent intersect closer to O or Q?
                    oInverse = qInverse = (tangentIntersect - oCenter).Length() > (tangentIntersect - qCenter).Length();

                }
            }
            else //inside
            {
                if (distance <= oRadius + qRadius)
                    throw new Exception("Can't draw crossing tangent line between circles that overlap.");

                if (oRadius == qRadius)
                    tangentIntersect = (oCenter + qCenter) * 0.5f;
                else
                    tangentIntersect = new Vector2(
                        (qCenter.X * oRadius + oCenter.X * qRadius) / (oRadius + qRadius),
                        (qCenter.Y * oRadius + oCenter.Y * qRadius) / (oRadius + qRadius));

                oInverse = true;
                qInverse = false;
            }
            oTangent = FindTangentPoint(tangentIntersect, oCenter, oRadius, oClockwise ^ oInverse);
            qTangent = FindTangentPoint(tangentIntersect, qCenter, qRadius, qClockwise ^ qInverse);
            return (oTangent, qTangent);
        }

        //Code functions Generate g-code (Input is in fontspace coordinates)
        private const string CodeEngage = "\nM3";
        private const string CodeDisengage = "\nM5";
        private static string CodeGoto(Vector2 pos) => $"\nG0 X{AbsoluteX(pos.X)} Y{AbsoluteY(pos.Y)}";
        private static string CodeLine(Vector2 to) => $"\nG1 X{AbsoluteX(to.X)} Y{AbsoluteY(to.Y)}";
        private static string CodeXLine(double to) => $"\nG1 X{AbsoluteX(to)}";
        private static string CodeYLine(double to) => $"\nG1 Y{AbsoluteY(to)}";
        //private static string CodeArc((double X, double Y) to, double radius, bool clockwise = true) => CodeLine(to);// $"\nG{(clockwise ? 2 : 3)} X{AbsoluteX(to.X)} Y{AbsoluteY(to.Y)} R{Scale(radius)}";
        //private static string CodeArc((double X, double Y) to, double i, double j, bool clockwise = true) => CodeLine(to);// $"\nG{(clockwise ? 2 : 3)} X{AbsoluteX(to.X)} Y{AbsoluteY(to.Y)}{(i != 0 ? $" I{Scale(i)}" : "")}{(j != 0 ? $" J{Scale(j)}" : "")}";
        //private static string CodeArc(Vector2 to, double radius, bool clockwise = true) => CodeArc((to.X, to.Y), radius, clockwise);
        //private static string CodeCircle(double i = 0, double j = 0, bool clockwise = true) => string.Empty; //$"\nG{(clockwise ? 2 : 3)}{(i != 0 ? $" I{Scale(i)}" : "")}{(j != 0 ? $" J{Scale(j)}" : "")}";

        /// <summary>
        /// Transforms a fontspace X value to an absolute X value using baseLeft offset.
        /// </summary>
        /// <param name="pos">X position relative to baseLeft in fontspace ratio coordinates</param>
        private static double AbsoluteX(double pos) => baseLeft.X + pos * FontSize;
        /// <summary>
        /// Transforms a fontspace Y value to an absolute Y value using baseLeft offset.
        /// </summary>
        /// <param name="pos">Y position relative to baseLeft in fontspace ratio coordinates</param>
        private static double AbsoluteY(double pos) => baseLeft.Y + pos * FontSize;
        /// <summary>
        /// Scales a value in fontspace size to machine units
        /// </summary>
        private static double Scale(double length) => length * FontSize;

        //Draw functions generate move instructions assuming that the marker is already engaged
        private static string DrawLinesRounded(params Vector2[] points)
        {
            if (points.Length < 3)
                throw new ArgumentException("Not enough points for multiple lines.", nameof(points));

            //Go to first point and engage
            StringBuilder instructions = new StringBuilder();

            for (int i = 2; i < points.Length; i++)
            {
                //Arc arc = Arc.RoundCorner(points[i - 2], points[i - 1], points[i], fontRadius*fontSize);

                //draw line to start of curve
                //instructions.Append(CodeLine(arc.Start));
                //draw corner arc
                //instructions.Append(DrawArc(arc.Start, arc.End, arc.Radius, arc.Clockwise));

                //just draw hard angles instead
                instructions.Append(CodeLine(points[i-1]));
            }
            //draw line to the last point
            instructions.Append(CodeLine(points.Last()));
            return instructions.ToString();
        }
        private static string DrawLineIntoArc(Vector2 start, Vector2 center, Vector2 end, bool clockwise = true)
        {
            var radius = (end - center).Length();
            var tangent = FindTangentPoint(start, center, radius, clockwise);

            return
                CodeLine(tangent) +
                DrawArc(tangent, end, radius, clockwise);
        }
        private static string DrawArcIntoArc(Vector2 start, Vector2 startCenter, Vector2 end, Vector2 endCenter, bool startClockwise = true, bool endClockwise = true)
        {
            var startRadius = (start - startCenter).Length();
            var endRadius = (end - endCenter).Length();

            (Vector2 startTangent, Vector2 endTangent) = FindTangentPoints(startCenter, startRadius, endCenter, endRadius, startClockwise, endClockwise);

            return
                DrawArc(start, startTangent, startRadius, startClockwise) +
                CodeLine(endTangent) +
                DrawArc(endTangent, end, endRadius, endClockwise);
        }
        private static string DrawArcs(Vector2 start, Vector2 end, params (Vector2 center, double radius, bool clockwise)[] curves )
        {
            StringBuilder instructions = new StringBuilder();

            if (curves.Length < 2)
                throw new Exception("Not enough curves");

            List<Vector2> tangentPoints = new List<Vector2> { start };

            for(int i = 1;  i < curves.Length; i++)
            {
                (Vector2 startTangent, Vector2 endTangent) = FindTangentPoints(curves[i-1].center, curves[i - 1].radius, curves[i].center, curves[i].radius, curves[i - 1].clockwise, curves[i].clockwise);
                instructions.Append(DrawArc(tangentPoints[i - 1], startTangent, curves[i - 1].radius, curves[i - 1].clockwise));
                instructions.Append(CodeLine(endTangent));
                tangentPoints.Add(endTangent);
            }
            instructions.Append(DrawArc(tangentPoints.Last(), end, curves.Last().radius, curves.Last().clockwise));

            return instructions.ToString();
        }
        private static string DrawArc(Vector2 from, Vector2 to, double radius, bool clockwise = true)
        {
            StringBuilder instructions = new StringBuilder();
            Vector2 center;
            Vector2 difference = (to - from);
            Vector2 middle = (to + from) * 0.5;
            Vector2 perp = new Vector2((clockwise ? 1 : -1) * difference.Y, (clockwise ? -1 : 1) * difference.X)*0.5;
            double distance = difference.Length();

            if (distance >= radius * 2)
            {
                radius = distance * 0.5;
                //divide 180 degree arc into two 90 degree arcs to avoid going the wrong direction
                instructions.Append(DrawArc(from, middle - perp, radius, clockwise));
                instructions.Append(DrawArc(middle - perp, to, radius, clockwise));
            }
            else
            {
                //find center
                double h = Math.Sqrt(radius * radius - distance * distance * 0.25);
                center = middle + perp.Normalize() * h;

                //calculate the number of steps
                double arcDistance = radius * Math.Asin(distance * 0.5 / radius);
                int numOfSteps = (int)Math.Ceiling(FontSize * arcDistance / ArcInterpolationDistance);

                Vector2 fromUnit = (from - center).Normalize();
                Vector2 toUnit = (to - center).Normalize();

                for (int i = 1; i < numOfSteps; i++)
                {
                    Vector2 unitVector = fromUnit.Clerp(toUnit, (float)(i / (double)numOfSteps), clockwise);
                    Vector2 pos = unitVector * radius + center;
                    instructions.Append(CodeLine(pos));
                }

                instructions.Append(CodeLine(to));
            }

            return instructions.ToString();
        }
        private static string DrawCircle(Vector2 from, Vector2 center, bool clockwise = true)
        {
            StringBuilder instructions = new StringBuilder();

            var fromVector = from - center;
            int numOfSteps = (int)Math.Ceiling(FontSize * (2 * fromVector.Length() * Math.PI) / ArcInterpolationDistance);
            List<Quaternion> Quarters = new List<Quaternion>
            {
                fromVector.Normalize(),
                (clockwise ? new Vector2(-fromVector.Y, -fromVector.X) : new Vector2(fromVector.Y, fromVector.X)).Normalize(),
                fromVector.Invert().Normalize(),
                (clockwise ? new Vector2(fromVector.Y, fromVector.X) : new Vector2(-fromVector.Y, -fromVector.X)).Normalize()
            };

            //At minimum draw a square
            if (numOfSteps <= 4)
                for (int i = 1; i < 4; i++)
                    instructions.Append(CodeLine((Vector2)Quarters[i] * fromVector.Length() + center));
            else
                for(int i = 1; i < numOfSteps; i++)
                {
                    int quadrant = 4 * i / numOfSteps;
                    instructions.Append(CodeLine(
                        (Vector2)Quaternion.Slerp(
                            Quarters[quadrant],
                            Quarters[(quadrant + 1) < 4 ? (quadrant + 1) : 0],
                            (float)((i / (double)numOfSteps - quadrant * 0.25) / 0.25)
                        ) * fromVector.Length() + center));
                }

            instructions.Append(CodeLine(from));

            return instructions.ToString();
        }

        //Make functions perform a whole operation including engage and disengage
        private static string MakeLine(Vector2 from, Vector2 to) =>
            CodeGoto(from) +
            CodeEngage +
            CodeLine(to) +
            CodeDisengage;
        private static string MakeXLine(Vector2 from, double to) =>
            CodeGoto(from) +
            CodeEngage +
            CodeXLine(to) +
            CodeDisengage;
        private static string MakeYLine(Vector2 from, double to) =>
            CodeGoto(from) +
            CodeEngage +
            CodeYLine(to) +
            CodeDisengage;
        private static string MakeLinesRounded(params Vector2[] points)
        {
            if (points.Count() < 3)
                throw new ArgumentException("Not enough points for multiple lines.", nameof(points));

            //Go to first point and engage
            StringBuilder instructions = new StringBuilder();
            instructions.Append(CodeGoto(points[0]));
            instructions.Append(CodeEngage);
            instructions.Append(DrawLinesRounded(points));
            instructions.Append(CodeDisengage);
            return instructions.ToString();
        }
        private static string MakeDot(Vector2 pos) => MakeCircle(pos + Vector2.UnitX * DotSize, pos);

        private static string MakeArc(Vector2 from, Vector2 to, double radius, bool clockwise = true) =>
            CodeGoto(from) +
            CodeEngage +
            DrawArc(from, to, radius, clockwise) +
            CodeDisengage;
        private static string MakeCircle(Vector2 from, Vector2 center, bool clockwise = true) =>
            CodeGoto(from) +
            CodeEngage +
            DrawCircle(from, center, clockwise) +
            CodeDisengage;

        //Character font instruction data
        public static readonly Dictionary<char, Character> Characters = new Dictionary<char, Character>
        {
            { ' ', new Character{
                Width = 0.3,
                Instructions = () => string.Empty
            }},
            { '!', new Character{
                Width = 0,
                Instructions = () =>
                    MakeYLine((0,1),0.25) +
                    MakeDot((0,0))
            }},
            { '"', new Character{
                Width = 0.1,
                Instructions = () =>
                    MakeYLine((0,1), 0.75) +
                    MakeYLine((0.1,1), 0.75)
            }},
            { '#', new Character{
                Width = 0.6,
                Instructions = () =>
                    MakeYLine((0.2,0), 0.6) +
                    MakeYLine((0.4,0.6), 0) +
                    MakeXLine((0.6, 0.2), 0) +
                    MakeXLine((0, 0.4), 0.6)
            }},
            { '$', new Character{
                Width = 0.4,
                Instructions = () =>
                    MakeYLine((0.2, 1), 0) +
                    CodeGoto((0, 0.3)) +
                    CodeEngage +
                    DrawArc((0, 0.3),(0.4, 0.3), 0.2, clockwise: false) +
                    DrawArc((0.4, 0.3),(0.2, 0.5), 0.2, clockwise: false) +
                    DrawArc((0.2, 0.5),(0, 0.7), 0.2) +
                    DrawArc((0, 0.7),(0.4, 0.7), 0.2) +
                    CodeDisengage
            }},
            { '%', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeCircle((0.25, 0.875), (0.125, 0.875)) +
                    MakeCircle((0.25, 0.125),  (0.375, 0.125)) +
                    MakeLine((0,0), (0.5,1))
            }},
            { '&', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0)) +
                    CodeEngage +
                    DrawLineIntoArc((0.5,0), (0.25,0.85), (0.25, 1)) +
                    DrawArcIntoArc((0.25, 1), (0.25,0.85), (0.25,0.0), (0.25,0.25), endClockwise: false) +
                    DrawArc((0.25,0.0), (0.5, 0.25), 0.25, clockwise: false) +
                    CodeDisengage
            }},
            { '\'', new Character{// WARNING: not '\'
                Width = 0,
                Instructions = () =>
                    MakeYLine((0,1), 0.75)
            }},
            { '(', new Character{
                Width = 1 - Math.Sin(Math.PI / 3),
                Instructions = () =>
                    MakeArc((1 - Math.Sin(Math.PI / 3), 1), (1 - Math.Sin(Math.PI / 3), 0), 1, clockwise: false)
            }},
            { ')', new Character{
                Width = 1 - Math.Sin(Math.PI / 3),
                Instructions = () =>
                    MakeArc((0, 1), (0, 0), 1)
            }},
            { '*', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0.75)) +
                    CodeEngage +
                    DrawLinesRounded((0.5,0.75), (0.25,0.75), (0.375,0.5335)) +
                    DrawLinesRounded((0.375,0.5335), (0.25,0.75), (0.125,0.5335)) +
                    DrawLinesRounded((0.125,0.5335), (0.25,0.75), (0,0.75)) +
                    DrawLinesRounded((0,0.75), (0.25,0.75), (0.125,0.9665)) +
                    DrawLinesRounded((0.125,0.9665), (0.25,0.75), (0.375,0.9665)) +
                    DrawLinesRounded((0.375,0.9665), (0.25,0.75), (0.5,0.75)) +
                    CodeDisengage
            }},
            { '+', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0.5)) +
                    CodeEngage +
                    DrawLinesRounded((0.5,0.5), (0.25,0.5), (0.25,0.25)) +
                    DrawLinesRounded((0.25,0.25), (0.25,0.5), (0,0.5)) +
                    DrawLinesRounded((0,0.5), (0.25,0.5), (0.25,0.75)) +
                    DrawLinesRounded((0.25,0.75), (0.25,0.5), (0.5,0.5)) +
                    CodeDisengage
            }},
            { ',', new Character{
                Width = 0.125,
                Instructions = () =>
                    CodeGoto((0, -0.125)) +
                    CodeEngage +
                    DrawArc((0, -0.125), (0.125, 0.0), 0.125, clockwise: false) +
                    DrawCircle((0.125, 0.0), (0.125 - DotSize, 0.0), clockwise: false) +
                    CodeDisengage
            }},
            { '-', new Character{
                Width = 0.3,
                Instructions = () =>
                    MakeLine((0,0.5),(0.3,0.5))
            }},
            { '.', new Character{
                Width = 0,
                Instructions = () =>
                    MakeDot((0,0))
            }},
            { '/', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLine((0,0),(0.5,1))
            }},
            { '0', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0.75)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawArc((0.5, 0.25),(0, 0.25), 0.25) +
                    CodeYLine(0.75) +
                    DrawArc((0, 0.75),(0.5, 0.75), 0.25) +
                    CodeDisengage
            }},
            { '1', new Character{
                Width = 0.2,
                Instructions = () =>
                    MakeLinesRounded((0,0.9),(0.1,1),(0.1,0)) +
                    MakeXLine((0,0),0.2)
            }},
            { '2', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.75)) +
                    CodeEngage +
                    DrawArc((0, 0.75),(0.5, 0.75), 0.25) +
                    DrawArc((0.5, 0.75),(0.45, 0.6), 0.25) +
                    DrawLinesRounded((0.45,0.6),(0,0),(0.5,0)) +
                    CodeDisengage
            }},
            { '3', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 1)) +
                    CodeEngage +
                    DrawLinesRounded((0,1),(0.5,1),(0.25,0.5)) +
                    DrawArc((0.25,0.5),(0.25, 0), 0.25) +
                    DrawArc((0.25, 0),(0, 0.25), 0.25) +
                    CodeDisengage
            }},
            { '4', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((1/3.0,0), (1/3.0,1), (0,1/3.0), (0.5,1/3.0))
            }},
            { '5', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,0)) +
                    CodeEngage +
                    CodeXLine(0.25) +
                    DrawArc((0.25,0), (0.25,0.5), 0.25, clockwise: false) +
                    DrawLinesRounded((0.25,0.5), (0,0.5), (0,1), (0.5,1)) +
                    CodeDisengage
            }},
            { '6', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,0.25)) +
                    CodeEngage +
                    DrawCircle((0,0.25), (0.25,0.25)) +
                    DrawArc((0,0.25), (0.5,1), 13/16.0) +
                    CodeDisengage
            }},
            { '7', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,0),(0.5,1),(0,1))
            }},
            { '8', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.25, 0.5)) +
                    CodeEngage +
                    DrawCircle((0.25, 0.5), (0.25, 0.75)) +
                    DrawCircle((0.25, 0.5), (0.25, 0.25), clockwise: false) +
                    CodeDisengage
            }},
            { '9', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0.75)) +
                    CodeEngage +
                    DrawCircle((0.5, 0.75), (0.25, 0.75)) +
                    CodeYLine(0) +
                    CodeDisengage
            }},
            { ':', new Character{
                Width = 0.05,
                Instructions = () =>
                    MakeDot((0,0.5)) +
                    MakeDot((0,0))
            }},
            { ';', new Character{
                Width = 0.125,
                Instructions = () =>
                    CodeGoto((0, -0.125)) +
                    CodeEngage +
                    DrawArc((0, -0.125), (0.125,0.0), 0.125, clockwise: false) +
                    DrawCircle((0.125,0.0), (0.125-DotSize,0.0), clockwise: false) +
                    CodeDisengage +
                    MakeDot((0.125 - DotSize,0.5))
            }},
            { '<', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0.5, 0.75), (0,0.5), (0.5,0.25))
            }},
            { '=', new Character{
                Width = 0.3,
                Instructions = () =>
                    MakeLine((0,0.6),(0.3,0.6)) +
                    MakeLine((0.3,0.4),(0,0.4))
            }},
            { '>', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0, 0.25), (0.5,0.5), (0,0.75))
            }},
            { '?', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,1)) +
                    CodeEngage +
                    DrawArc((0,1), (0,0.5), 0.25) +
                    CodeLine((0,0.25)) +
                    CodeDisengage +
                    MakeDot((0, 0))
            }},
            { '@', new Character{
                Width = 1,
                Instructions = () =>
                    CodeGoto((0.75,0.75)) +
                    CodeEngage +
                    CodeYLine(0.5) +
                    DrawCircle((0.75,0.5), (0.5,0.5)) +
                    DrawArc((0.5,0.5), (1,0.5), 0.125, clockwise:false) +
                    DrawArc((1,0.5), (0,0.5), 0.5, clockwise:false) +
                    DrawArc((0,0.5), (0.5, 0), 0.5, clockwise:false) +
                    CodeDisengage
            }},
            { 'A', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeXLine((0.375, 0.5), 0.125) +
                    MakeLinesRounded((0,0),(0.25,1),(0.5,0))
            }},
            { 'B', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.25, 0)) +
                    CodeEngage +
                    DrawLinesRounded((0.25,0), (0,0), (0,0.5), (0.25,0.5)) +
                    DrawLinesRounded((0.25,0.5), (0,0.5), (0,1), (0.25,1)) +
                    DrawArc((0.25,1), (0.25, 0.5), 0.25) +
                    DrawArc((0.25, 0.5), (0.25, 0), 0.25) +
                    CodeDisengage
            }},
            { 'C', new Character{
                Width = 1,
                Instructions = () =>
                    CodeGoto((1, 1)) +
                    CodeEngage +
                    CodeXLine(0.5) +
                    DrawArc((0.5, 1), (0.5, 0), 0.5, clockwise: false) +
                    CodeXLine(1) +
                    CodeDisengage
            }},
            { 'D', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0)) +
                    CodeEngage +
                    CodeYLine(1) +
                    DrawArc((0, 1), (0,0), 0.5) +
                    CodeDisengage
            }},
            { 'E', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0)) +
                    CodeEngage +
                    DrawLinesRounded((0.5,0), (0,0), (0,0.5), (0.5,0.5)) +
                    DrawLinesRounded((0.5,0.5), (0,0.5), (0,1), (0.5,1)) +
                    CodeDisengage
            }},
            { 'F', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0)) +
                    CodeEngage +
                    DrawLinesRounded((0,0), (0,0.5), (0.5,0.5)) +
                    DrawLinesRounded((0.5,0.5), (0,0.5), (0,1), (0.5,1)) +
                    CodeDisengage
            }},
            { 'G', new Character{
                Width = 0.75,
                Instructions = () =>
                    CodeGoto((0.75, 1)) +
                    CodeEngage +
                    CodeXLine(0.5) +
                    DrawArc((0.5, 1), (0, 0.5), 0.5, clockwise: false) +
                    DrawArc((0, 0.5), (0.5, 0), 0.5, clockwise: false) +
                    DrawLinesRounded((0.5,0),(0.75,0),(0.75,0.5),(0.375,0.5)) +
                    CodeDisengage
            }},
            { 'H', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeYLine((0, 1), 0) +
                    MakeXLine((0, 0.5), 0.5) +
                    MakeYLine((0.5, 1), 0)
            }},
            { 'I', new Character{
                Width = 0.20,
                Instructions = () =>
                    MakeXLine((0, 1), 0.2) +
                    MakeYLine((0.1, 1), 0) +
                    MakeXLine((0, 0), 0.2)
            }},
            { 'J', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.25)) +
                    CodeEngage +
                    DrawArc((0, 0.25), (0.5, 0.25), 0.25, clockwise: false) +
                    CodeYLine(1) +
                    CodeDisengage
            }},
            { 'K', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeYLine((0, 1), 0) +
                    MakeLinesRounded((0.5,0),(0,0.5),(0.5,1))
            }},
            { 'L', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,1),(0,0),(0.5,0))
            }},
            { 'M', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,0),(0,1),(0.25,0.5),(0.5,1),(0.5,0))
            }},
            { 'N', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,0),(0,1),(0.5,0),(0.5,1))
            }},
            { 'O', new Character{
                Width = 1,
                Instructions = () =>
                    MakeCircle((1, 0.5), (0.5, 0.5), clockwise: false)
            }},
            { 'P', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0)) +
                    CodeEngage +
                    DrawLinesRounded((0,0), (0,1), (0.25,1)) +
                    DrawArc((0.25,1), (0.25, 0.5), 0.25) +
                    CodeXLine(0) +
                    CodeDisengage
            }},
            { 'Q', new Character{
                Width = 1,
                Instructions = () =>
                    CodeGoto((0.5, 0)) +
                    CodeEngage +
                    DrawCircle((0.5, 0), (0.5, 0.5)) +
                    DrawArc((0.5, 0), (0.75, -0.25), 0.25, clockwise: false) +
                    CodeDisengage
            }},
            { 'R', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0)) +
                    CodeEngage +
                    DrawLinesRounded((0,0), (0,1), (0.25,1)) +
                    DrawArc((0.25,1), (0.25, 0.5), 0.25) +
                    DrawLinesRounded((0.25, 0.5),(0,0.5), (0.5,0)) +
                    CodeDisengage
            }},
            { 'S', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.25)) +
                    CodeEngage +
                    DrawArc((0, 0.25), (0.5, 0.25), 0.25, clockwise: false) +
                    DrawArc((0.5, 0.25), (0.25, 0.5), 0.25, clockwise: false) +
                    DrawArc((0.25, 0.5), (0, 0.75), 0.25) +
                    DrawArc((0, 0.75), (0.5, 0.75), 0.25) +
                    CodeDisengage
            }},
            { 'T', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeYLine((0.25,0), 1) +
                    MakeXLine((0,1), 0.5)
            }},
            { 'U', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 1)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawArc((0, 0.25), (0.5, 0.25), 0.25, clockwise: false) +
                    CodeYLine(1) +
                    CodeDisengage
            }},
            { 'V', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,1),(0.25,0),(0.5,1))
            }},
            { 'W', new Character{
                Width = 1,
                Instructions = () =>
                    MakeLinesRounded((0,1),(0.25,0),(0.5,0.5),(0.75,0),(1,1))
            }},
            { 'X', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLine((0, 1), (0.5, 0)) +
                    MakeLine((0, 0), (0.5, 1))
            }},
            { 'Y', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,1)) +
                    CodeEngage +
                    DrawLinesRounded((0,1),(0.25,0.5),(0.25,0)) +
                    DrawLinesRounded((0.25,0),(0.25,0.5),(0.5,1)) +
                    CodeDisengage
            }},
            { 'Z', new Character{
                Width = 1,
                Instructions = () =>
                    MakeLinesRounded((0,1),(1,1),(0,0),(1,0))
            }},
            { '[', new Character{
                Width = 0.2,
                Instructions = () =>
                    MakeLinesRounded((0.2,1),(0,1),(0,0),(0.2,0))
            }},
            { '\\', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLine((0,1),(0.5,0))
            }},
            { ']', new Character{
                Width = 0.2,
                Instructions = () =>
                    MakeLinesRounded((0,1),(0.2,1),(0.2,0),(0,0))
            }},
            { '^', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,0.75),(0.25,1),(0.5,0.75))
            }},
            { '_', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLine((0,0),(0.5,0))
            }},
            { '`', new Character{
                Width = 0.3,
                Instructions = () =>
                    MakeLine((0,1),(0.3,0.7))
            }},
            { 'a', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5,0.5)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawCircle((0.5,0.25), (0.25,0.25)) +
                    CodeYLine(0) +
                    CodeDisengage
            }},
            { 'b', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 1)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawCircle((0, 0.25), (0.25, 0.25), clockwise: false) +
                    CodeDisengage
            }},
            { 'c', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0.5)) +
                    CodeEngage +
                    CodeXLine(0.25) +
                    DrawArc((0.25, 0.5), (0.25, 0), 0.25, clockwise: false) +
                    CodeXLine(0.5) +
                    CodeDisengage
            }},
            { 'd', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawCircle((0.5, 0.25), (0.25, 0.25), clockwise: false) +
                    CodeYLine(1) +
                    CodeDisengage
            }},
            { 'e', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.25)) +
                    CodeEngage +
                    CodeXLine(0.5) +
                    DrawArc((0.5, 0.25), (0, 0.25), 0.25, clockwise: false) +
                    DrawArc((0, 0.25), (0.25, 0), 0.25, clockwise: false) +
                    CodeXLine(0.5) +
                    CodeDisengage
            }},
            { 'f', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeXLine((0,0.5), 0.5) +
                    CodeGoto((0.25, 0)) +
                    CodeEngage +
                    CodeYLine(0.75) +
                    DrawArc((0.25, 0.75), (0.5,1), 0.25) +
                    CodeDisengage
            }},
            { 'g', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0)) +
                    CodeEngage +
                    DrawArc((0, 0), (0.5, 0), 0.25, clockwise: false) +
                    CodeYLine(0.25) +
                    DrawCircle((0.5, 0.25), (0.25, 0.25), clockwise: false) +
                    CodeDisengage
            }},
            { 'h', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,1)) +
                    CodeEngage +
                    CodeYLine(0) +
                    CodeYLine(0.25) +
                    DrawArc((0,0.25), (0.5, 0.25), 0.25) +
                    CodeYLine(0) +
                    CodeDisengage
            }},
            { 'i', new Character{
                Width = 0,
                Instructions = () =>
                    MakeYLine((0,0), 0.5) +
                    MakeDot((0, 0.75))
            }},
            { 'j', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, -0.25)) +
                    CodeEngage +
                    DrawArc((0, -0.25), (0.5, -0.25), 0.25, clockwise: false) +
                    CodeYLine(0.5) +
                    CodeDisengage +
                    MakeDot((0.5, 0.75))
            }},
            { 'k', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeYLine((0, 1), 0) +
                    MakeLinesRounded((0.5,0),(0,0.25),(0.5,0.5))
            }},
            { 'l', new Character{
                Width = 0.2,
                Instructions = () =>
                    MakeLinesRounded((0,1),(0,0),(0.2,0))
            }},
            { 'm', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,0)) +
                    CodeEngage +
                    CodeYLine(0.375) +
                    DrawArc((0,0.375), (0.25,0.375), 0.125) +
                    CodeYLine(0) +
                    CodeYLine(0.375) +
                    DrawArc((0.25,0.375), (0.5,0.375), 0.125) +
                    CodeYLine(0) +
                    CodeDisengage
            }},
            { 'n', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,0)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawArc((0,0.25), (0.5, 0.25), 0.25) +
                    CodeYLine(0) +
                    CodeDisengage
            }},
            { 'o', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeCircle((0.5, 0.25), (0.25, 0.25))
            }},
            { 'p', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.5)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawCircle((0, 0.25), (0.25, 0.25), clockwise: false) +
                    CodeYLine(-0.5) +
                    CodeDisengage
            }},
            { 'q', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5, 0.25)) +
                    CodeEngage +
                    DrawCircle((0.5, 0.25), (0.25, 0.25)) +
                    CodeYLine(-0.5) +
                    CodeDisengage
            }},
            { 'r', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.5)) +
                    CodeEngage +
                    CodeYLine(0) +
                    CodeYLine(0.25) +
                    DrawArc((0, 0.25), (0.5, 0.25), 0.25) +
                    CodeDisengage
            }},
            { 's', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.125)) +
                    CodeEngage +
                    DrawArc((0, 0.125), (0.125, 0), 0.125, clockwise: false) +
                    CodeXLine(0.375) +
                    DrawArc((0.375, 0), (0.375, 0.25), 0.125, clockwise: false) +
                    CodeXLine(0.125) +
                    DrawArc((0.125, 0.25), (0.125, 0.5), 0.125) +
                    CodeXLine(0.375) +
                    DrawArc((0.375, 0.5), (0.5, 0.375), 0.125) +
                    CodeDisengage
            }},
            { 't', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeYLine((0.25,0), 1) +
                    MakeXLine((0,0.75), 0.5)
            }},
            { 'u', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0, 0.5)) +
                    CodeEngage +
                    CodeYLine(0.25) +
                    DrawArc((0, 0.25), (0.5, 0.25), 0.25, clockwise: false) +
                    CodeYLine(0.5) +
                    CodeDisengage
            }},
            { 'v', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,0.5),(0.25,0),(0.5,0.5))
            }},
            { 'w', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,0.5)) +
                    CodeEngage +
                    CodeYLine(0.125) +
                    DrawArc((0,0.125), (0.25,0.125), 0.125, clockwise: false) +
                    CodeYLine(0.5) +
                    CodeYLine(0.125) +
                    DrawArc((0.25,0.125), (0.5, 0.125), 0.125, clockwise: false) +
                    CodeYLine(0.5) +
                    CodeDisengage
            }},
            { 'x', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLine((0, 0.5), (0.5, 0)) +
                    MakeLine((0, 0), (0.5, 0.5))
            }},
            { 'y', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLine((0,0.5),(0.25,0)) +
                    MakeLine((0,-0.5),(0.5,0.5))
            }},
            { 'z', new Character{
                Width = 0.5,
                Instructions = () =>
                    MakeLinesRounded((0,0.5),(0.5,0.5),(0,0),(0.5,0))
            }},
            { '{', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0.5,1)) +
                    CodeEngage +
                    CodeXLine(0.375) +
                    DrawArc((0.375, 1), (0.25, 0.875), 0.125, clockwise: false) +
                    CodeYLine(0.625) +
                    DrawArc((0.25, 0.625), (0.125, 0.5), 0.125) +
                    CodeXLine(0) +
                    CodeXLine(0.125) +
                    DrawArc((0.125, 0.5), (0.25,0.375), 0.125) +
                    CodeYLine(0.125) +
                    DrawArc((0.25,0.125), (0.375, 0), 0.125, clockwise: false) +
                    CodeXLine(0.5) +
                    CodeDisengage
            }},
            { '|', new Character{
                Width = 0,
                Instructions = () =>
                    MakeLine((0,0),(0,1))
            }},
            { '}', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,1)) +
                    CodeEngage +
                    CodeXLine(0.125) +
                    DrawArc((0.125, 1), (0.25, 0.875), 0.125) +
                    CodeYLine(0.625) +
                    DrawArc((0.25, 0.625), (0.375, 0.5), 0.125, clockwise: false) +
                    CodeXLine(0.5) +
                    CodeXLine(0.375) +
                    DrawArc((0.375, 0.5), (0.25,0.375), 0.125, clockwise: false) +
                    CodeYLine(0.125) +
                    DrawArc((0.25,0.125), (0.125, 0), 0.125) +
                    CodeXLine(0) +
                    CodeDisengage
            }},
            { '~', new Character{
                Width = 0.5,
                Instructions = () =>
                    CodeGoto((0,0.5)) +
                    CodeEngage +
                    DrawArc((0, 0.5), (0.25, 0.5), 0.223606797749979) +
                    DrawArc((0.25, 0.5), (0.5,0.5), 0.223606797749979, clockwise: false) +
                    CodeDisengage
            }},
            { 'あ', new Character{
                Width = 1,
                Instructions = () =>
                    MakeArc((0.175+0.037, 1-0.272), (0.760-0.037,1-0.221), 3, clockwise: false) +
                    MakeArc((0.404, 1 - 0.051 - 0.037), (0.475, 1 - 0.815), 1, clockwise: false) +
                    CodeGoto((0.624,1-0.381)) +
                    CodeEngage +
                    DrawArc((0.624,1-0.381), (0.287,1-0.873), 0.6) +
                    DrawArc((0.287,1-0.873), (0.176,1-0.785), 0.083) +
                    DrawArc((0.176,1-0.785), (0.555,1-0.444), 0.4) +
                    DrawArc((0.555,1-0.444), (0.770,1-0.480), 0.667) +
                    DrawArc((0.770,1-0.480), (0.765,1-0.866), 0.205) +
                    DrawArc((0.765,1-0.866), (0.639,1-0.901), 0.8) +
                    CodeDisengage
            }},
            { 'は', new Character{
                Width = 1,
                Instructions = () =>
                    MakeArc((0.2, 1-0.15), (0.17,1-0.855), 3, clockwise: false) +
                    MakeLine((0.369, 1-0.344), (0.900, 1-0.314)) +
                    CodeGoto((0.659,1-0.116)) +
                    CodeEngage +
                    DrawLineIntoArc((0.659,1-0.116), (0.582,1-0.763), (0.617,1-0.849)) +
                    DrawArc((0.617,1-0.849), (0.527,1-0.863), 0.195) +
                    DrawArc((0.527,1-0.863), (0.404,1-0.755), 0.12) +
                    DrawArc((0.404,1-0.755), (0.458,1-0.671), 0.088) +
                    DrawArc((0.458,1-0.671), (0.585,1-0.666), 0.265) +
                    DrawArc((0.585,1-0.666), (0.904,1-0.802), 0.765) +
                    CodeDisengage
            }},
            { '私', new Character{
                Width = 1,
                Instructions = () =>
                    MakeArc((0.425, 1-0.120), (0.115,1-0.168), 3) +
                    MakeLine((0.067+0.037, 1 - 0.321 - 0.037), (0.492 - 0.037, 1 - 0.321 - 0.037)) +
                    MakeLine((0.246+0.037, 1 - 0.129 - 0.037), (0.246 + 0.037, 1 - 0.942 + 0.037)) +
                    MakeArc((0.246+0.037, 1-0.395), (0.088, 1-0.715), 0.75) +
                    MakeArc((0.3, 0.5), (0.446, 1-0.6), 0.75) +
                    MakeLinesRounded((0.674, 1-0.161), (0.517, 1-0.846), (0.845, 1-0.781)) +
                    MakeArc((0.75, 1-0.537), (0.893, 1-0.873), 1)
            }},
            { '俺', new Character{
                Width = 1,
                Instructions = () =>
                    MakeArc((0.262, 1-0.088), (0.070,1-0.502), 1) +
                    MakeLine((0.168+0.039, 1 - 0.35), (0.168 + 0.039, 1 - 0.938 + 0.039)) +
                    MakeLine((0.317+0.037, 1 - 0.177 - (0.073*0.5)), (0.918 - 0.037, 1 - 0.177 - (0.073*0.5))) +
                    MakeArc((0.588, 1-0.088), (0.288, 0.5), 0.75) +
                    MakeArc((0.675, 1-0.218), (0.930, 1-0.484), 0.75, clockwise:false) +
                    MakeLine((0.379+0.033, 1 - 0.442 -0.031), (0.379+0.033, 1 - 0.814)) +
                    MakeLinesRounded((0.379+0.033, 1-0.442-0.031), (0.774+0.033, 1-0.442-0.031), (0.774+0.033, 1-0.700-0.031)) +
                    MakeLine((0.379+0.033, 1-0.571-0.031), (0.774+0.033, 1-0.571-0.031)) +
                    MakeLine((0.379+0.033, 1-0.700-0.031), (0.774+0.033, 1-0.700-0.031)) +
                    CodeGoto((0.567+0.0385,1-0.375)) +
                    CodeEngage +
                    DrawLineIntoArc((0.567+0.0385,1-0.375), (0.666, 1-0.8275), (0.666,1-0.856-0.032), clockwise:false) +
                    DrawLineIntoArc((0.666,1-0.856-0.032), (0.841, 1-0.83), (0.897,1-0.848), clockwise:false) +
                    CodeLine((0.908,1-0.808)) +
                    CodeDisengage
            }},
        };
    }
}
