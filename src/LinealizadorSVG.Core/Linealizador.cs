using System.Text;

namespace LinealizadorSVG.Core;

/// <summary>Path data rewritten with straight segments only, and how many of them it took.</summary>
public sealed record TrazadoLineal(string D, int Segmentos);

/// <summary>
/// Turns SVG path data into absolute M/L/Z commands: lines stay as they are and curves (C, S, Q, T, A) become
/// straight segments about <c>precision</c> units long.
/// </summary>
public static class Linealizador
{
    public const int SegmentosMaximosPorCurva = 10000;

    public static TrazadoLineal Linealizar(string d, double precision)
    {
        if (!(precision > 0)) throw new ArgumentOutOfRangeException(nameof(precision), "La precisión tiene que ser mayor que cero.");
        return new Lector(d, precision).Leer();
    }

    private sealed class Lector(string d, double paso)
    {
        private readonly string[] _a = [.. Numeros.Token().Matches(d).Select(m => m.Value)];
        private readonly List<string> _salida = [];
        private int _i, _segmentos;
        private double _x, _y, _sx, _sy;

        private static bool EsComando(string t) => t.Length == 1 && char.IsAsciiLetter(t[0]);

        private double Num()
        {
            if (_i >= _a.Length || EsComando(_a[_i])) throw new SvgInvalidoException("Faltan coordenadas en un trazado.");
            return Numeros.Leer(_a[_i++]);
        }

        private (double X, double Y) Par(bool rel)
        {
            double x = Num(), y = Num();
            return rel ? (_x + x, _y + y) : (x, y);
        }

        private void Linea(double x, double y)
        {
            _salida.Add("L" + Numeros.Formatear(x) + "," + Numeros.Formatear(y));
            _x = x;
            _y = y;
            _segmentos++;
        }

        private void Curva(Func<double, (double X, double Y)> punto, int n)
        {
            n = Math.Clamp(n, 2, SegmentosMaximosPorCurva);
            for (var k = 1; k <= n; k++)
            {
                var p = punto(k / (double)n);
                Linea(p.X, p.Y);
            }
        }

        private int Pasos(double largo) => (int)Math.Min(SegmentosMaximosPorCurva, Math.Ceiling(largo / paso));

        private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

        private void Cuadratica((double X, double Y) p0, (double X, double Y) q, (double X, double Y) p) =>
            Curva(t =>
            {
                var u = 1 - t;
                return (u * u * p0.X + 2 * u * t * q.X + t * t * p.X, u * u * p0.Y + 2 * u * t * q.Y + t * t * p.Y);
            }, Pasos(Dist(p0, q) + Dist(q, p)));

        public TrazadoLineal Leer()
        {
            var cmd = "";
            (double X, double Y)? ultimoC = null, ultimoQ = null;
            var previo = "";

            while (_i < _a.Length)
            {
                if (EsComando(_a[_i])) cmd = _a[_i++];
                else if (cmd == "") throw new SvgInvalidoException("Comando SVG inválido en un trazado.");

                var c = cmd.ToUpperInvariant();
                var rel = cmd != c;

                switch (c)
                {
                    case "Z":
                        _salida.Add("Z");
                        _x = _sx;
                        _y = _sy;
                        ultimoC = ultimoQ = null;
                        previo = "Z";
                        cmd = "";
                        continue;

                    case "M":
                    {
                        var p = Par(rel);
                        _x = _sx = p.X;
                        _y = _sy = p.Y;
                        _salida.Add("M" + Numeros.Formatear(_x) + "," + Numeros.Formatear(_y));
                        cmd = rel ? "l" : "L"; // extra pairs after a moveto are linetos
                        ultimoC = ultimoQ = null;
                        previo = c;
                        continue;
                    }

                    case "L":
                    {
                        var p = Par(rel);
                        Linea(p.X, p.Y);
                        ultimoC = ultimoQ = null;
                        previo = c;
                        continue;
                    }

                    case "H":
                    case "V":
                    {
                        var v = Num();
                        if (c == "H") Linea(rel ? _x + v : v, _y);
                        else Linea(_x, rel ? _y + v : v);
                        ultimoC = ultimoQ = null;
                        previo = c;
                        continue;
                    }

                    case "C":
                    case "S":
                    {
                        (double X, double Y) p0 = (_x, _y);
                        (double X, double Y) p1;
                        if (c == "C") p1 = Par(rel);
                        else p1 = ultimoC is { } uc && previo is "C" or "S" ? (2 * _x - uc.X, 2 * _y - uc.Y) : (_x, _y);
                        var p2 = Par(rel);
                        var p3 = Par(rel);
                        Curva(t =>
                        {
                            var u = 1 - t;
                            double a = u * u * u, b = 3 * u * u * t, cc = 3 * u * t * t, e = t * t * t;
                            return (a * p0.X + b * p1.X + cc * p2.X + e * p3.X, a * p0.Y + b * p1.Y + cc * p2.Y + e * p3.Y);
                        }, Pasos(Dist(p0, p1) + Dist(p1, p2) + Dist(p2, p3)));
                        ultimoC = p2;
                        ultimoQ = null;
                        previo = c;
                        continue;
                    }

                    case "Q":
                    {
                        (double X, double Y) p0 = (_x, _y);
                        var q = Par(rel);
                        var p = Par(rel);
                        Cuadratica(p0, q, p);
                        ultimoQ = q;
                        ultimoC = null;
                        previo = c;
                        continue;
                    }

                    case "T":
                    {
                        var p = Par(rel);
                        var q = ultimoQ is { } uq && previo is "Q" or "T" ? (2 * _x - uq.X, 2 * _y - uq.Y) : (_x, _y);
                        Cuadratica((_x, _y), q, p);
                        ultimoQ = q;
                        ultimoC = null;
                        previo = c;
                        continue;
                    }

                    case "A":
                        Arco(rel);
                        ultimoC = ultimoQ = null;
                        previo = c;
                        continue;

                    default:
                        throw new SvgInvalidoException($"Comando no compatible en un trazado: {cmd}.");
                }
            }

            var sb = new StringBuilder();
            foreach (var s in _salida) sb.Append(sb.Length > 0 ? " " : "").Append(s);
            return new TrazadoLineal(sb.ToString(), _segmentos);
        }

        // Endpoint to center parameterization, SVG 1.1 implementation notes F.6.5.
        private void Arco(bool rel)
        {
            double rx = Math.Abs(Num()), ry = Math.Abs(Num()), phi = Num() * Math.PI / 180;
            bool grande = Num() != 0, barrido = Num() != 0;
            var p = Par(rel);
            double x1 = _x, y1 = _y, x2 = p.X, y2 = p.Y;
            if (rx == 0 || ry == 0 || (x1 == x2 && y1 == y2))
            {
                Linea(x2, y2);
                return;
            }

            double cos = Math.Cos(phi), sin = Math.Sin(phi), dx = (x1 - x2) / 2, dy = (y1 - y2) / 2;
            double xp = cos * dx + sin * dy, yp = -sin * dx + cos * dy;
            var lambda = xp * xp / (rx * rx) + yp * yp / (ry * ry);
            if (lambda > 1)
            {
                var s = Math.Sqrt(lambda);
                rx *= s;
                ry *= s;
            }

            var signo = grande == barrido ? -1 : 1;
            var den = rx * rx * yp * yp + ry * ry * xp * xp;
            var coef = den == 0 ? 0 : signo * Math.Sqrt(Math.Max(0, (rx * rx * ry * ry - rx * rx * yp * yp - ry * ry * xp * xp) / den));
            double cxp = coef * (rx * yp / ry), cyp = coef * (-ry * xp / rx);
            double cx = cos * cxp - sin * cyp + (x1 + x2) / 2, cy = sin * cxp + cos * cyp + (y1 + y2) / 2;

            double ux = (xp - cxp) / rx, uy = (yp - cyp) / ry, vx = (-xp - cxp) / rx, vy = (-yp - cyp) / ry;
            var theta = Math.Atan2(uy, ux);
            var delta = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
            if (!barrido && delta > 0) delta -= 2 * Math.PI;
            if (barrido && delta < 0) delta += 2 * Math.PI;

            var largo = Math.Abs(delta) * Math.Max(rx, ry);
            Curva(t =>
            {
                var th = theta + delta * t;
                return (cx + rx * cos * Math.Cos(th) - ry * sin * Math.Sin(th), cy + rx * sin * Math.Cos(th) + ry * cos * Math.Sin(th));
            }, Pasos(largo));
            // Land exactly on the endpoint the file asked for (the last sample can drift by rounding).
            _x = x2;
            _y = y2;
        }
    }
}
