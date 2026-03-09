using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SpecimenFX17.Imaging
{
    public class SessionData
    {
        public string SessionDate { get; set; } = DateTime.Now.ToString("s");
        public List<ShapeData> Selections { get; set; } = new();
    }

    public class ShapeData
    {
        public string Type { get; set; } = "";
        public int ColorArgb { get; set; }
        public int[] PointsX { get; set; } = Array.Empty<int>();
        public int[] PointsY { get; set; } = Array.Empty<int>();
        public int Radius { get; set; }

        public string Variety { get; set; } = "";
        public string Date { get; set; } = "";
        public float? MeasuredBrix { get; set; }
        public string Notes { get; set; } = "";
    }

    public static class SessionManager
    {
        public static void SaveSession(string savePath, IEnumerable<SelectionShape> shapes)
        {
            var session = new SessionData();

            foreach (var sh in shapes)
            {
                if (sh is MaskShape) continue; // Saltamos MaskShape por ahora para evitar JSON masivos

                var data = new ShapeData
                {
                    ColorArgb = sh.Color.ToArgb(),
                    Variety = sh.Variety,
                    Date = sh.Date,
                    MeasuredBrix = sh.MeasuredBrix,
                    Notes = sh.Notes
                };

                if (sh is PixelShape px)
                {
                    data.Type = nameof(PixelShape);
                    data.PointsX = new[] { px.Pt.X };
                    data.PointsY = new[] { px.Pt.Y };
                }
                else if (sh is RectShape r)
                {
                    data.Type = nameof(RectShape);
                    data.PointsX = new[] { r.Rect.Left, r.Rect.Width };
                    data.PointsY = new[] { r.Rect.Top, r.Rect.Height };
                }
                else if (sh is CircleShape c)
                {
                    data.Type = nameof(CircleShape);
                    data.PointsX = new[] { c.Center.X };
                    data.PointsY = new[] { c.Center.Y };
                    data.Radius = c.Radius;
                }
                else if (sh is PolygonShape p)
                {
                    data.Type = nameof(PolygonShape);
                    data.PointsX = p.Points.Select(pt => pt.X).ToArray();
                    data.PointsY = p.Points.Select(pt => pt.Y).ToArray();
                }
                else if (sh is FreehandShape f)
                {
                    data.Type = nameof(FreehandShape);
                    data.PointsX = f.Points.Select(pt => pt.X).ToArray();
                    data.PointsY = f.Points.Select(pt => pt.Y).ToArray();
                }

                session.Selections.Add(data);
            }

            string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(savePath, json);
        }

        public static List<SelectionShape> LoadSession(string loadPath)
        {
            var shapes = new List<SelectionShape>();
            if (!File.Exists(loadPath)) return shapes;

            string json = File.ReadAllText(loadPath);
            var session = JsonSerializer.Deserialize<SessionData>(json);
            if (session == null || session.Selections == null) return shapes;

            foreach (var data in session.Selections)
            {
                var c = Color.FromArgb(data.ColorArgb);
                SelectionShape? shape = null;

                if (data.Type == nameof(PixelShape) && data.PointsX.Length > 0)
                    shape = new PixelShape(new Point(data.PointsX[0], data.PointsY[0]), c);
                else if (data.Type == nameof(RectShape) && data.PointsX.Length > 1)
                    shape = new RectShape(new Rectangle(data.PointsX[0], data.PointsY[0], data.PointsX[1], data.PointsY[1]), c);
                else if (data.Type == nameof(CircleShape) && data.PointsX.Length > 0)
                    shape = new CircleShape(new Point(data.PointsX[0], data.PointsY[0]), data.Radius, c);
                else if (data.Type == nameof(PolygonShape) && data.PointsX.Length > 0)
                {
                    var pts = data.PointsX.Zip(data.PointsY, (x, y) => new Point(x, y)).ToList();
                    shape = new PolygonShape(pts, c);
                }
                else if (data.Type == nameof(FreehandShape) && data.PointsX.Length > 0)
                {
                    var pts = data.PointsX.Zip(data.PointsY, (x, y) => new Point(x, y)).ToList();
                    shape = new FreehandShape(pts, c);
                }

                if (shape != null)
                {
                    shape.Variety = data.Variety;
                    shape.Date = data.Date;
                    shape.MeasuredBrix = data.MeasuredBrix;
                    shape.Notes = data.Notes;
                    shapes.Add(shape);
                }
            }

            return shapes;
        }
    }
}