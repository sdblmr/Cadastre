using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace CadastrePrototype
{
    /// <summary>Une parcelle cadastrale simplifiée, prête à être dessinée.</summary>
    public class Parcelle
    {
        public string Id { get; set; }
        public string Section { get; set; }
        public string Numero { get; set; }
        public double SurfaceM2 { get; set; }
        /// <summary>Anneaux du contour (polygone), en Lambert93 (X, Y).</summary>
        public List<List<(double X, double Y)>> AnneauxLambert93 { get; set; } = new List<List<(double, double)>>();
    }

    /// <summary>Emprise d'un batiment (bati cadastral), pret a etre dessine en hachure.</summary>
    public class Batiment
    {
        public string Id { get; set; }
        /// <summary>Anneaux du contour, en Lambert93 (X, Y).</summary>
        public List<List<(double X, double Y)>> AnneauxLambert93 { get; set; } = new List<List<(double, double)>>();
    }

    /// <summary>Interface pour la deduplication par ID dans les methodes WFS generiques.</summary>
    public interface IHasId { string Id { get; } }

    /// <summary>Point d'adresse (localisant PCI Express).</summary>
    public class Localisant : IHasId
    {
        public string Id { get; set; }
        public string Numero { get; set; }
        public string Section { get; set; }
        public string NomCommune { get; set; }
        /// <summary>Position en Lambert93 (X, Y).</summary>
        public (double X, double Y) PositionLambert93 { get; set; }
    }

    /// <summary>Subdivision fiscale (sous-parcelle PCI Express).</summary>
    public class SubdivisionFiscale : IHasId
    {
        public string Id { get; set; }
        public string IdParcelle { get; set; }
        public string Lettre { get; set; }
        /// <summary>Anneaux du contour, en Lambert93 (X, Y).</summary>
        public List<List<(double X, double Y)>> AnneauxLambert93 { get; set; } = new List<List<(double, double)>>();
    }

    public static class ApiCartoClient
    {
        private const string ParcelleUrl = "https://apicarto.ign.fr/api/cadastre/parcelle";

        // Endpoint bati : WFS BD TOPO (data.geopf.fr), couche BDTOPO_V3:batiment.
        // Accessible sans cle API. Limite de 1000 objets par requete.
        public static string BatiBaseUrl { get; set; } = "https://data.geopf.fr/wfs/ows";

        private const int ApiLimit = 1000;  // limite IGN par requete
        private static readonly HttpClient Http = new HttpClient();

        /// <summary>Callback de log. Assigné par CadastreCommand pour écrire dans la console ZWCAD.</summary>
        public static Action<string> Logger { get; set; }

        static ApiCartoClient()
        {
            // .NET Framework 4.7.2 ne force pas toujours TLS 1.2 par défaut selon la config
            // Windows du poste ; sans ça, apicarto.ign.fr (HTTPS) peut échouer silencieusement
            // au niveau du handshake, avec une HttpRequestException générique côté HttpClient.
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
        }

        private static void Log(string msg)
        {
            Logger?.Invoke(msg);
        }

        /// <summary>
        /// Interroge l'API Carto pour les parcelles intersectant une emprise
        /// (rectangle) donnée en Lambert93, et retourne des parcelles en Lambert93.
        /// Si la limite API (1000 résultats) est atteinte, l'emprise est automatiquement
        /// subdivisée en 4 quadrants et les requêtes sont relancées récursivement.
        /// </summary>
        public static async Task<List<Parcelle>> GetParcellesAsync(double xMin, double yMin, double xMax, double yMax)
        {
            return await GetParcellesWithSubdivisionAsync(xMin, yMin, xMax, yMax, maxDepth: 2);
        }

        /// <summary>
        /// Requête récursive avec subdivision automatique si la limite API est atteinte.
        /// </summary>
        private static async Task<List<Parcelle>> GetParcellesWithSubdivisionAsync(
            double xMin, double yMin, double xMax, double yMax, int maxDepth)
        {
            var parcelles = await QueryParcellesAsync(xMin, yMin, xMax, yMax);

            // Si on n'a pas atteint la limite, ou si on est au fond de la récursion → terminé
            if (parcelles.Count < ApiLimit || maxDepth <= 0)
                return parcelles;

            // Limite atteinte → on découpe l'emprise en 4 quadrants (traités séquentiellement
            // pour éviter de saturer l'API ou le client HTTP avec des appels parallèles)
            Log($"Subdivision (profondeur restante={maxDepth}) : {parcelles.Count} parcelles = limite API, découpe en 4 (séquentiel)...");

            double xMid = (xMin + xMax) / 2.0;
            double yMid = (yMin + yMax) / 2.0;

            var quadrants = new (double xMin, double yMin, double xMax, double yMax)[]
            {
                (xMin, yMin, xMid, yMid),
                (xMid, yMin, xMax, yMid),
                (xMin, yMid, xMid, yMax),
                (xMid, yMid, xMax, yMax),
            };

            var results = new List<List<Parcelle>>();
            foreach (var q in quadrants)
            {
                var subResult = await GetParcellesWithSubdivisionAsync(q.xMin, q.yMin, q.xMax, q.yMax, maxDepth - 1);
                results.Add(subResult);
            }

            // Fusion avec déduplication par ID
            var seen = new HashSet<string>();
            var merged = new List<Parcelle>();
            int totalBeforeDedup = 0;
            foreach (var list in results)
            {
                foreach (var p in list)
                {
                    totalBeforeDedup++;
                    if (seen.Add(p.Id ?? ""))
                        merged.Add(p);
                }
            }

            Log($"Subdivision terminée : {merged.Count} parcelles uniques après fusion ({totalBeforeDedup} avant déduplication, {totalBeforeDedup - merged.Count} doublons)");
            return merged;
        }

        /// <summary>
        /// Requête unitaire à l'API Carto (une emprise → un appel HTTP).
        /// </summary>
        private static async Task<List<Parcelle>> QueryParcellesAsync(double xMin, double yMin, double xMax, double yMax)
        {
            // 1. Reprojection de l'emprise Lambert93 -> WGS84 (l'API attend du WGS84)
            var (lonMin, latMin) = Lambert93.ToWgs84(xMin, yMin);
            var (lonMax, latMax) = Lambert93.ToWgs84(xMax, yMax);

            // Construction via des types .NET simples + FromObject : évite le piège de
            // JArray(JArray) qui "aplatit" les tableaux imbriqués au lieu de les nicher.
            var anneau = new List<double[]>
            {
                new[] { lonMin, latMin },
                new[] { lonMax, latMin },
                new[] { lonMax, latMax },
                new[] { lonMin, latMax },
                new[] { lonMin, latMin }
            };
            var coordinates = new List<List<double[]>> { anneau };

            var geom = new JObject
            {
                ["type"] = "Polygon",
                ["coordinates"] = JToken.FromObject(coordinates)
            };

            // Formatting.None : le GeoJSON envoyé doit rester compact, sans retours à la ligne,
            // sinon l'URL gonfle inutilement et certains proxies/serveurs le digèrent mal.
            string geomJson = geom.ToString(Newtonsoft.Json.Formatting.None);

            // URL complète (sans le geom pour éviter de saturer les logs)
            string shortGeom = geomJson.Length > 120 ? geomJson.Substring(0, 120) + "..." : geomJson;
            Log($"API call | bbox L93: [{xMin:F0},{yMin:F0} / {xMax:F0},{yMax:F0}] → WGS84: [{lonMin:F6},{latMin:F6} / {lonMax:F6},{latMax:F6}]");

            string url = $"{ParcelleUrl}?geom={Uri.EscapeDataString(geomJson)}";

            // Utiliser HttpRequestMessage au lieu de modifier DefaultRequestHeaders
            // (sinon les appels concurrents de la subdivision s'écrasent les headers)
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Accept.Clear();
                request.Headers.Add("accept", "application/json");

                var response = await Http.SendAsync(request);
                string json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Erreur API Carto ({(int)response.StatusCode}) : {json}");
                }

                var result = ParseFeatureCollection(json);
                Log($"API response : {result.Count} parcelles");
                return result;
            }
        }

        private static List<Parcelle> ParseFeatureCollection(string json)
        {
            var result = new List<Parcelle>();
            var root = JObject.Parse(json);
            var features = root["features"] as JArray;
            if (features == null) return result;

            foreach (var feature in features)
            {
                var props = feature["properties"];
                var parcelle = new Parcelle
                {
                    Id = (string)props?["id"],
                    Section = (string)props?["section"],
                    Numero = (string)props?["numero"],
                    SurfaceM2 = props?["contenance"]?.ToObject<double?>() ?? 0
                };

                // Fallback si l'API ne renvoie pas "id" directement :
                // on reconstruit l'ID à partir de la commune + section + numéro.
                // L'API carto peut utiliser "commune" + "section" + "numero" au lieu d'un "id" unique.
                if (string.IsNullOrEmpty(parcelle.Id))
                {
                    string commune = (string)props?["commune"];
                    if (!string.IsNullOrEmpty(commune) && !string.IsNullOrEmpty(parcelle.Section) && !string.IsNullOrEmpty(parcelle.Numero))
                    {
                        parcelle.Id = $"{commune}{parcelle.Section}{parcelle.Numero}";
                    }
                    else if (!string.IsNullOrEmpty(parcelle.Section) && !string.IsNullOrEmpty(parcelle.Numero))
                    {
                        parcelle.Id = $"{parcelle.Section}{parcelle.Numero}";
                    }
                }

                // Dernier recours : si toujours pas d'ID, on en génère un unique
                // pour éviter que la déduplication ne collapse toutes les parcelles.
                if (string.IsNullOrEmpty(parcelle.Id))
                {
                    parcelle.Id = Guid.NewGuid().ToString();
                    Log($"⚠ Pas d'ID pour une parcelle (section={parcelle.Section}, numero={parcelle.Numero}) → GUID généré");
                }

                var geometry = feature["geometry"];
                string geomType = (string)geometry?["type"];
                var coordinates = geometry?["coordinates"] as JArray;

                if (coordinates != null)
                {
                    // Un Polygon a une liste d'anneaux ; on ne traite ici que le cas simple.
                    // Pour un MultiPolygon il faudrait boucler un niveau plus haut.
                    IEnumerable<JToken> rings = geomType == "MultiPolygon" ? coordinates[0] : coordinates;

                    foreach (var ring in rings)
                    {
                        var anneau = new List<(double, double)>();
                        foreach (var point in ring)
                        {
                            double lon = (double)point[0];
                            double lat = (double)point[1];
                            anneau.Add(Lambert93.FromWgs84(lon, lat));
                        }
                        parcelle.AnneauxLambert93.Add(anneau);
                    }
                }

                result.Add(parcelle);
            }

            return result;
        }

        // ============================================================
        // Bati (batiments)
        // ============================================================

        /// <summary>
        /// Interroge l'API pour les batiments (bati cadastral) dans l'emprise donnee.
        /// Retourne une liste vide si BatiBaseUrl est null ou si l'API echoue.
        /// </summary>
        public static async Task<List<Batiment>> GetBatiAsync(double xMin, double yMin, double xMax, double yMax)
        {
            if (string.IsNullOrEmpty(BatiBaseUrl))
                return new List<Batiment>();

            return await GetBatiWithSubdivisionAsync(xMin, yMin, xMax, yMax, maxDepth: 2);
        }

        private static async Task<List<Batiment>> GetBatiWithSubdivisionAsync(
            double xMin, double yMin, double xMax, double yMax, int maxDepth)
        {
            var batiments = await QueryBatiAsync(xMin, yMin, xMax, yMax);
            if (batiments == null) return new List<Batiment>();

            if (batiments.Count < ApiLimit || maxDepth <= 0)
                return batiments;

            Log($"Subdivision bati (profondeur restante={maxDepth}) : {batiments.Count} batiments = limite API, decoupe en 4...");

            double xMid = (xMin + xMax) / 2.0;
            double yMid = (yMin + yMax) / 2.0;

            var quadrants = new (double xMin, double yMin, double xMax, double yMax)[]
            {
                (xMin, yMin, xMid, yMid),
                (xMid, yMin, xMax, yMid),
                (xMin, yMid, xMid, yMax),
                (xMid, yMid, xMax, yMax),
            };

            var results = new List<List<Batiment>>();
            foreach (var q in quadrants)
            {
                var subResult = await GetBatiWithSubdivisionAsync(q.xMin, q.yMin, q.xMax, q.yMax, maxDepth - 1);
                results.Add(subResult);
            }

            var seen = new HashSet<string>();
            var merged = new List<Batiment>();
            int totalBeforeDedup = 0;
            foreach (var list in results)
            {
                foreach (var b in list)
                {
                    totalBeforeDedup++;
                    if (seen.Add(b.Id ?? ""))
                        merged.Add(b);
                }
            }

            Log($"Subdivision bati terminee : {merged.Count} batiments uniques ({totalBeforeDedup} avant dedup, {totalBeforeDedup - merged.Count} doublons)");
            return merged;
        }

        private static async Task<List<Batiment>> QueryBatiAsync(double xMin, double yMin, double xMax, double yMax)
        {
            if (string.IsNullOrEmpty(BatiBaseUrl))
                return new List<Batiment>();

            try
            {
                // Emprise en WGS84 pour le WFS
                var (lonMin, latMin) = Lambert93.ToWgs84(xMin, yMin);
                var (lonMax, latMax) = Lambert93.ToWgs84(xMax, yMax);

                // WFS GetFeature avec bbox + outputFormat=GeoJSON
                // ATTENTION : WFS 2.0 utilise l'ordre lat,lon pour EPSG:4326 (pas lon,lat !)
                string bboxVal = $"{latMin.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 $"{lonMin.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 $"{latMax.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 $"{lonMax.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 "urn:ogc:def:crs:EPSG::4326";

                string url = $"{BatiBaseUrl}?service=WFS&version=2.0.0&request=GetFeature" +
                             $"&typeNames=CADASTRALPARCELS.PARCELLAIRE_EXPRESS:batiment" +
                             $"&outputFormat=application/json" +
                             $"&srsName=urn:ogc:def:crs:EPSG::4326" +
                             $"&bbox={Uri.EscapeDataString(bboxVal)}" +
                             $"&count={ApiLimit}";

                Log($"API bati WFS | bbox L93: [{xMin:F0},{yMin:F0} / {xMax:F0},{yMax:F0}]");

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Accept.Clear();
                    request.Headers.Add("accept", "application/json");

                    var response = await Http.SendAsync(request);
                    string json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"API bati erreur ({(int)response.StatusCode})");
                        return new List<Batiment>();
                    }

                    var result = ParseBatiFeatureCollection(json);
                    Log($"API bati response : {result.Count} batiments");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Log($"API bati exception : {ex.Message}");
                return new List<Batiment>();
            }
        }

        private static List<Batiment> ParseBatiFeatureCollection(string json)
        {
            var result = new List<Batiment>();
            JObject root;
            try { root = JObject.Parse(json); }
            catch { return result; }

            var features = root["features"] as JArray;
            if (features == null) return result;

            foreach (var feature in features)
            {
                var props = feature["properties"];
                var geometry = feature["geometry"];
                string geomType = (string)geometry?["type"];
                var coordinates = geometry?["coordinates"] as JArray;
                if (coordinates == null) continue;

                var batiment = new Batiment
                {
                    Id = (string)props?["id"]
                };

                // Normaliser en liste d'anneaux
                List<JArray> allRings = new List<JArray>();
                if (geomType == "MultiPolygon")
                {
                    foreach (JToken polygon in coordinates)
                    {
                        if (polygon is JArray polyArr)
                            foreach (JToken ring in polyArr)
                                if (ring is JArray ringArr)
                                    allRings.Add(ringArr);
                    }
                }
                else // Polygon
                {
                    foreach (JToken ring in coordinates)
                        if (ring is JArray ringArr)
                            allRings.Add(ringArr);
                }

                foreach (var ring in allRings)
                {
                    var anneau = new List<(double, double)>();
                    foreach (var point in ring)
                    {
                        // WFS BD TOPO renvoie [lon, lat, altitude] — on ignore l'altitude
                        double lon = (double)point[0];
                        double lat = (double)point[1];
                        anneau.Add(Lambert93.FromWgs84(lon, lat));
                    }
                    if (anneau.Count >= 3)
                        batiment.AnneauxLambert93.Add(anneau);
                }

                if (string.IsNullOrEmpty(batiment.Id))
                    batiment.Id = Guid.NewGuid().ToString();

                result.Add(batiment);
            }

            return result;
        }

        // ============================================================
        // Localisants (points d'adresse)
        // ============================================================

        public static async Task<List<Localisant>> GetLocalisantsAsync(double xMin, double yMin, double xMax, double yMax)
        {
            return await QueryWfsAsync("CADASTRALPARCELS.PARCELLAIRE_EXPRESS:localisant",
                xMin, yMin, xMax, yMax, ParseLocalisantsFeatureCollection, "localisants");
        }

        private static List<Localisant> ParseLocalisantsFeatureCollection(string json)
        {
            var result = new List<Localisant>();
            JObject root;
            try { root = JObject.Parse(json); } catch { return result; }
            var features = root["features"] as JArray;
            if (features == null) return result;

            foreach (var feature in features)
            {
                var props = feature["properties"];
                var geometry = feature["geometry"];
                var coordinates = geometry?["coordinates"] as JArray;
                if (coordinates == null || coordinates.Count == 0) continue;

                // MultiPoint : coordinates[0] = [lon, lat]
                var pt = coordinates[0] as JArray;
                if (pt == null || pt.Count < 2) continue;

                double lon = (double)pt[0];
                double lat = (double)pt[1];
                var (x, y) = Lambert93.FromWgs84(lon, lat);

                result.Add(new Localisant
                {
                    Id = (string)feature["id"],
                    Numero = (string)props?["numero"] ?? "",
                    Section = (string)props?["section"] ?? "",
                    NomCommune = (string)props?["nom_com"] ?? "",
                    PositionLambert93 = (x, y)
                });
            }
            return result;
        }

        // ============================================================
        // Subdivisions fiscales
        // ============================================================

        public static async Task<List<SubdivisionFiscale>> GetSubdivisionsAsync(double xMin, double yMin, double xMax, double yMax)
        {
            return await QueryWfsAsync("CADASTRALPARCELS.PARCELLAIRE_EXPRESS:subdivision_fiscale",
                xMin, yMin, xMax, yMax, ParseSubdivisionsFeatureCollection, "subdivisions");
        }

        private static List<SubdivisionFiscale> ParseSubdivisionsFeatureCollection(string json)
        {
            var result = new List<SubdivisionFiscale>();
            JObject root;
            try { root = JObject.Parse(json); } catch { return result; }
            var features = root["features"] as JArray;
            if (features == null) return result;

            foreach (var feature in features)
            {
                var props = feature["properties"];
                var geometry = feature["geometry"];
                string geomType = (string)geometry?["type"];
                var coordinates = geometry?["coordinates"] as JArray;
                if (coordinates == null) continue;

                List<JArray> allRings = new List<JArray>();
                if (geomType == "MultiPolygon")
                {
                    foreach (JToken polygon in coordinates)
                        if (polygon is JArray polyArr)
                            foreach (JToken ring in polyArr)
                                if (ring is JArray ringArr) allRings.Add(ringArr);
                }
                else
                {
                    foreach (JToken ring in coordinates)
                        if (ring is JArray ringArr) allRings.Add(ringArr);
                }

                var sd = new SubdivisionFiscale
                {
                    Id = (string)feature["id"],
                    IdParcelle = (string)props?["idu_parcel"] ?? "",
                    Lettre = (string)props?["lettre"] ?? ""
                };

                foreach (var ring in allRings)
                {
                    var anneau = new List<(double, double)>();
                    foreach (var point in ring)
                    {
                        double lon = (double)point[0];
                        double lat = (double)point[1];
                        anneau.Add(Lambert93.FromWgs84(lon, lat));
                    }
                    if (anneau.Count >= 3) sd.AnneauxLambert93.Add(anneau);
                }

                if (string.IsNullOrEmpty(sd.Id))
                    sd.Id = Guid.NewGuid().ToString();

                result.Add(sd);
            }
            return result;
        }

        // ============================================================
        // WFS generique (utilise par bati, localisants, subdivisions)
        // ============================================================

        private static async Task<List<T>> QueryWfsAsync<T>(string typeName,
            double xMin, double yMin, double xMax, double yMax,
            Func<string, List<T>> parser, string logLabel) where T : IHasId
        {
            var result = new List<T>();
            try
            {
                var (lonMin, latMin) = Lambert93.ToWgs84(xMin, yMin);
                var (lonMax, latMax) = Lambert93.ToWgs84(xMax, yMax);

                string bboxVal = $"{latMin.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 $"{lonMin.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 $"{latMax.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 $"{lonMax.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                                 "urn:ogc:def:crs:EPSG::4326";

                string url = $"https://data.geopf.fr/wfs/ows?service=WFS&version=2.0.0&request=GetFeature" +
                             $"&typeNames={typeName}" +
                             $"&outputFormat=application/json" +
                             $"&srsName=urn:ogc:def:crs:EPSG::4326" +
                             $"&bbox={Uri.EscapeDataString(bboxVal)}" +
                             $"&count={ApiLimit}";

                Log($"API {logLabel} | bbox L93: [{xMin:F0},{yMin:F0} / {xMax:F0},{yMax:F0}]");

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.Accept.Clear();
                    request.Headers.Add("accept", "application/json");

                    var response = await Http.SendAsync(request);
                    string json = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"API {logLabel} erreur ({(int)response.StatusCode})");
                        return result;
                    }

                    result = parser(json);
                    Log($"API {logLabel} response : {result.Count}");

                    // Subdivision recursive si on atteint la limite
                    if (result.Count >= ApiLimit)
                    {
                        Log($"Subdivision {logLabel} : {result.Count} = limite API, decoupe...");
                        double xMid = (xMin + xMax) / 2.0;
                        double yMid = (yMin + yMax) / 2.0;

                        var quadrants = new (double, double, double, double)[]
                        {
                            (xMin, yMin, xMid, yMid), (xMid, yMin, xMax, yMid),
                            (xMin, yMid, xMid, yMax), (xMid, yMid, xMax, yMax)
                        };

                        var seen = new HashSet<string>();
                        foreach (var q in quadrants)
                        {
                            var subResult = await QueryWfsAsync(typeName, q.Item1, q.Item2, q.Item3, q.Item4, parser, logLabel);
                            foreach (var item in subResult)
                            {
                                string id = item?.Id ?? "";
                                if (seen.Add(id)) result.Add(item);
                            }
                        }
                        Log($"Subdivision {logLabel} terminee : {result.Count} uniques");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"API {logLabel} exception : {ex.Message}");
            }
            return result;
        }
    }
}
