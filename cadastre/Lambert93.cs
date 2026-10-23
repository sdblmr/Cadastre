using System;

namespace CadastrePrototype
{
    /// <summary>
    /// Conversion Lambert93 (RGF93 / EPSG:2154) &lt;-&gt; WGS84 (EPSG:4326).
    /// Basé sur les constantes officielles IGN pour la projection conique
    /// conforme de Lambert à 2 parallèles standards (ellipsoïde GRS80).
    /// Ce sont des constantes cartographiques publiques (pas de matériel
    /// sous droit d'auteur), reprises ici pour un usage interne.
    /// </summary>
    public static class Lambert93
    {
        // Ellipsoïde GRS80
        private const double A = 6378137.0;
        private const double E = 0.0818191912350225; // excentricité GRS80

        // Constantes de projection Lambert-93
        private const double N = 0.7256077650531266;
        private const double C = 11754255.4261933;
        private const double Xs = 700000.0;
        private const double Ys = 12655612.049876;
        private const double Lambda0Deg = 3.0; // méridien de référence (Greenwich)

        private static double Deg2Rad(double deg) => deg * Math.PI / 180.0;
        private static double Rad2Deg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>Latitude isométrique.</summary>
        private static double IsometricLatitude(double phi)
        {
            double esinphi = E * Math.Sin(phi);
            return Math.Log(Math.Tan(Math.PI / 4.0 + phi / 2.0) *
                             Math.Pow((1.0 - esinphi) / (1.0 + esinphi), E / 2.0));
        }

        /// <summary>WGS84 (lon, lat en degrés décimaux) -> Lambert93 (X, Y en mètres).</summary>
        public static (double X, double Y) FromWgs84(double lonDeg, double latDeg)
        {
            double lambda = Deg2Rad(lonDeg);
            double phi = Deg2Rad(latDeg);
            double lambda0 = Deg2Rad(Lambda0Deg);

            double L = IsometricLatitude(phi);
            double r = C * Math.Exp(-N * L);
            double gamma = N * (lambda - lambda0);

            double x = Xs + r * Math.Sin(gamma);
            double y = Ys - r * Math.Cos(gamma);
            return (x, y);
        }

        /// <summary>Lambert93 (X, Y en mètres) -> WGS84 (lon, lat en degrés décimaux).</summary>
        public static (double LonDeg, double LatDeg) ToWgs84(double x, double y)
        {
            double lambda0 = Deg2Rad(Lambda0Deg);

            double r = Math.Sqrt(Math.Pow(x - Xs, 2) + Math.Pow(y - Ys, 2));
            double gamma = Math.Atan((x - Xs) / (Ys - y));
            double lambda = lambda0 + gamma / N;

            double L = -1.0 / N * Math.Log(Math.Abs(r / C));

            // Inversion itérative de la latitude isométrique (Newton), converge en ~5 itérations
            double phi = 2.0 * Math.Atan(Math.Exp(L)) - Math.PI / 2.0;
            for (int i = 0; i < 6; i++)
            {
                double esinphi = E * Math.Sin(phi);
                phi = 2.0 * Math.Atan(
                    Math.Pow((1.0 + esinphi) / (1.0 - esinphi), E / 2.0) * Math.Exp(L)
                ) - Math.PI / 2.0;
            }

            return (Rad2Deg(lambda), Rad2Deg(phi));
        }
    }
}
