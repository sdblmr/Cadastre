using System;
using System.Threading.Tasks;

using ZwSoft.ZwCAD.Runtime;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.Geometry;
using ZwSoft.ZwCAD.Colors;
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;

using CadastrePrototype;

namespace cadastre
{
    public class CadastreCommand
    {
        [CommandMethod("CADASTRE")]
        public void ImporterParcelles()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            // ---- Options actives ----
            bool doParcelles     = CadastreOptions.ImporterParcelles;
            bool doBati          = CadastreOptions.ImporterBati;
            bool doLibelles      = CadastreOptions.ImporterLibelles;
            bool doLocalisants   = CadastreOptions.ImporterLocalisants;
            bool doSubdivisions  = CadastreOptions.ImporterSubdivisions;
            bool doCalques       = CadastreOptions.CreerCalques;

            ed.WriteMessage($"\n[Cadastre] Options : Parcelles={doParcelles}, Bati={doBati}, Libelles={doLibelles}, Localisants={doLocalisants}, Subdivisions={doSubdivisions}, Calques={doCalques}");
            if (!doParcelles && !doBati && !doLibelles && !doLocalisants && !doSubdivisions)
            {
                ed.WriteMessage("\n[Cadastre] Aucune option activee, rien a importer.");
                return;
            }

            // ---- 1. Emprise ----
            PromptPointResult ppr1 = ed.GetPoint("\nPoint bas-gauche de l'emprise : ");
            if (ppr1.Status != PromptStatus.OK) return;

            PromptCornerOptions cornerOpts = new PromptCornerOptions("\nPoint haut-droit de l'emprise : ", ppr1.Value);
            PromptPointResult ppr2 = ed.GetCorner(cornerOpts);
            if (ppr2.Status != PromptStatus.OK) return;

            double xMinDwg = Math.Min(ppr1.Value.X, ppr2.Value.X);
            double yMinDwg = Math.Min(ppr1.Value.Y, ppr2.Value.Y);
            double xMaxDwg = Math.Max(ppr1.Value.X, ppr2.Value.X);
            double yMaxDwg = Math.Max(ppr1.Value.Y, ppr2.Value.Y);

            // ---- 2. Echelle ----
            double xCentre = (xMinDwg + xMaxDwg) / 2.0;
            double yCentre = (yMinDwg + yMaxDwg) / 2.0;
            double scaleToMeters = GetScaleToMeters(db, xCentre, yCentre, ed);

            double xMin = xMinDwg * scaleToMeters;
            double yMin = yMinDwg * scaleToMeters;
            double xMax = xMaxDwg * scaleToMeters;
            double yMax = yMaxDwg * scaleToMeters;

            ed.WriteMessage($"\n[Debug] Facteur d'echelle detecte : x{scaleToMeters}");
            ed.WriteMessage($"\n[Debug] Emprise Lambert93 (m) : X [{xMin:F1} , {xMax:F1}]  Y [{yMin:F1} , {yMax:F1}]");

            ApiCartoClient.Logger = msg => ed.WriteMessage($"\n  [API] {msg}");

            // ---- 3. API Parcelles ----
            System.Collections.Generic.List<Parcelle> parcelles = null;
            if (doParcelles || doLibelles)
            {
                try
                {
                    ed.WriteMessage("\nRequete en cours vers l'API Carto... (subdivision auto si >1000 parcelles)");
                    parcelles = Task.Run(() => ApiCartoClient.GetParcellesAsync(xMin, yMin, xMax, yMax)).GetAwaiter().GetResult();
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nErreur API Parcelles : {ex.Message}");
                    if (ex.InnerException != null)
                        ed.WriteMessage($"\n  --> {ex.InnerException.Message}");
                }
            }

            // ---- 4. API Bati ----
            System.Collections.Generic.List<Batiment> batiments = null;
            if (doBati)
            {
                try
                {
                    ed.WriteMessage("\nRequete en cours pour le bati...");
                    batiments = Task.Run(() => ApiCartoClient.GetBatiAsync(xMin, yMin, xMax, yMax)).GetAwaiter().GetResult();
                    if (batiments != null && batiments.Count > 0)
                        ed.WriteMessage($"\n{batiments.Count} batiment(s) trouve(s).");
                    else
                        ed.WriteMessage("\nAucun batiment trouve (API bati non disponible ou zone sans batiments).");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nErreur API Bati (les parcelles seront importees sans le bati) : {ex.Message}");
                    batiments = null;
                }
            }

            // ---- 4b. API Localisants ----
            System.Collections.Generic.List<Localisant> localisants = null;
            if (doLocalisants)
            {
                try
                {
                    ed.WriteMessage("\nRequete en cours pour les localisants...");
                    localisants = Task.Run(() => ApiCartoClient.GetLocalisantsAsync(xMin, yMin, xMax, yMax)).GetAwaiter().GetResult();
                    ed.WriteMessage(localisants != null && localisants.Count > 0
                        ? $"\n{localisants.Count} localisant(s) trouve(s)."
                        : "\nAucun localisant trouve.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nErreur API Localisants : {ex.Message}");
                    localisants = null;
                }
            }

            // ---- 4c. API Subdivisions fiscales ----
            System.Collections.Generic.List<SubdivisionFiscale> subdivisions = null;
            if (doSubdivisions)
            {
                try
                {
                    ed.WriteMessage("\nRequete en cours pour les subdivisions fiscales...");
                    subdivisions = Task.Run(() => ApiCartoClient.GetSubdivisionsAsync(xMin, yMin, xMax, yMax)).GetAwaiter().GetResult();
                    ed.WriteMessage(subdivisions != null && subdivisions.Count > 0
                        ? $"\n{subdivisions.Count} subdivision(s) trouvee(s)."
                        : "\nAucune subdivision trouvee.");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nErreur API Subdivisions : {ex.Message}");
                    subdivisions = null;
                }
            }

            if ((parcelles == null || parcelles.Count == 0) && (batiments == null || batiments.Count == 0)
                && (localisants == null || localisants.Count == 0) && (subdivisions == null || subdivisions.Count == 0))
            {
                ed.WriteMessage("\nAucune donnee trouvee dans cette emprise.");
                return;
            }

            // ---- 5. Dessin ----
            double scaleFromMeters = 1.0 / scaleToMeters;
            int nbPolylignes = 0, nbTextes = 0, nbHachures = 0, nbLocalisants = 0, nbSubdivisions = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Calques
                ObjectId layerParcellesId = ObjectId.Null;
                ObjectId layerBatiId = ObjectId.Null;
                ObjectId layerLibellesId = ObjectId.Null;
                ObjectId layerLocalisantsId = ObjectId.Null;
                ObjectId layerSubdivisionsId = ObjectId.Null;
                if (doCalques)
                {
                    layerParcellesId    = GetOrCreateLayer(db, tr, CadastreOptions.CalqueParcelles, CadastreOptions.CouleurParcelles);
                    layerBatiId         = GetOrCreateLayer(db, tr, CadastreOptions.CalqueBati, CadastreOptions.CouleurBati);
                    layerLibellesId     = GetOrCreateLayer(db, tr, CadastreOptions.CalqueLibelles, CadastreOptions.CouleurLibelles);
                    layerLocalisantsId  = GetOrCreateLayer(db, tr, CadastreOptions.CalqueLocalisants, CadastreOptions.CouleurLocalisants);
                    layerSubdivisionsId = GetOrCreateLayer(db, tr, CadastreOptions.CalqueSubdivisions, CadastreOptions.CouleurSubdivisions);
                }

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // ---- 5a. Bati (hachures) — en premier = toujours en dessous ----
                if (doBati && batiments != null)
                {
                    foreach (Batiment bati in batiments)
                    {
                        foreach (var anneau in bati.AnneauxLambert93)
                        {
                            if (anneau.Count < 3) continue;

                            try
                            {
                                Hatch hatch = new Hatch();
                                hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                                hatch.Associative = false;

                                btr.AppendEntity(hatch);
                                tr.AddNewlyCreatedDBObject(hatch, true);

                                Point2dCollection vertices = new Point2dCollection();
                                DoubleCollection bulges = new DoubleCollection();
                                for (int i = 0; i < anneau.Count; i++)
                                {
                                    vertices.Add(new Point2d(anneau[i].X * scaleFromMeters, anneau[i].Y * scaleFromMeters));
                                    bulges.Add(0.0);
                                }
                                var first = anneau[0];
                                var last = anneau[anneau.Count - 1];
                                if (Math.Abs(first.X - last.X) > 1e-9 || Math.Abs(first.Y - last.Y) > 1e-9)
                                {
                                    vertices.Add(new Point2d(first.X * scaleFromMeters, first.Y * scaleFromMeters));
                                    bulges.Add(0.0);
                                }

                                hatch.AppendLoop(HatchLoopTypes.Default, vertices, bulges);
                                hatch.EvaluateHatch(true);
                                if (doCalques) hatch.Layer = CadastreOptions.CalqueBati;

                                nbHachures++;
                            }
                            catch (System.Exception ex)
                            {
                                ed.WriteMessage($"\n  [Warning] Echec hachure batiment {bati.Id} : {ex.Message}");
                            }
                        }
                    }
                }

                // ---- 5b. Parcelles (polylignes) — au milieu ----
                if (doParcelles && parcelles != null)
                {
                    foreach (Parcelle parcelle in parcelles)
                    {
                        foreach (var anneau in parcelle.AnneauxLambert93)
                        {
                            if (anneau.Count < 2) continue;

                            Polyline poly = new Polyline();
                            for (int i = 0; i < anneau.Count; i++)
                                poly.AddVertexAt(i,
                                    new Point2d(anneau[i].X * scaleFromMeters, anneau[i].Y * scaleFromMeters),
                                    0, 0, 0);
                            poly.Closed = true;
                            if (doCalques) poly.Layer = CadastreOptions.CalqueParcelles;

                            btr.AppendEntity(poly);
                            tr.AddNewlyCreatedDBObject(poly, true);
                            nbPolylignes++;
                        }
                    }
                }

                // ---- 5c. Libelles (textes aux centroides) — en dernier = toujours au-dessus ----
                if (doLibelles && parcelles != null)
                {
                    foreach (Parcelle parcelle in parcelles)
                    {
                        if (parcelle.AnneauxLambert93.Count == 0) continue;
                        var premierAnneau = parcelle.AnneauxLambert93[0];
                        if (premierAnneau.Count < 3) continue;

                        var (cx, cy) = ComputePolygonCentroid(premierAnneau);
                        double tx = cx * scaleFromMeters;
                        double ty = cy * scaleFromMeters;

                        DBText text = new DBText();
                        text.Position = new Point3d(tx, ty, 0);
                        text.TextString = $"{parcelle.Section} {parcelle.Numero}";
                        text.Height = db.Textsize;
                        text.HorizontalMode = TextHorizontalMode.TextCenter;
                        text.VerticalMode = TextVerticalMode.TextVerticalMid;
                        text.AlignmentPoint = new Point3d(tx, ty, 0);
                        if (doCalques) text.Layer = CadastreOptions.CalqueLibelles;

                        btr.AppendEntity(text);
                        tr.AddNewlyCreatedDBObject(text, true);
                        nbTextes++;
                    }
                }

                // ---- 5d. Localisants (points d'adresse) ----
                if (doLocalisants && localisants != null)
                {
                    foreach (Localisant loc in localisants)
                    {
                        double lx = loc.PositionLambert93.X * scaleFromMeters;
                        double ly = loc.PositionLambert93.Y * scaleFromMeters;

                        string label = loc.Numero;
                        if (!string.IsNullOrEmpty(loc.NomCommune) && string.IsNullOrEmpty(label))
                            label = loc.NomCommune;

                        if (string.IsNullOrEmpty(label)) continue;

                        DBText text = new DBText();
                        text.Position = new Point3d(lx, ly, 0);
                        text.TextString = label;
                        text.Height = db.Textsize;
                        text.HorizontalMode = TextHorizontalMode.TextCenter;
                        text.VerticalMode = TextVerticalMode.TextVerticalMid;
                        text.AlignmentPoint = new Point3d(lx, ly, 0);
                        if (doCalques) text.Layer = CadastreOptions.CalqueLocalisants;

                        btr.AppendEntity(text);
                        tr.AddNewlyCreatedDBObject(text, true);
                        nbLocalisants++;
                    }
                }

                // ---- 5e. Subdivisions fiscales (polylignes) ----
                if (doSubdivisions && subdivisions != null)
                {
                    foreach (SubdivisionFiscale sd in subdivisions)
                    {
                        foreach (var anneau in sd.AnneauxLambert93)
                        {
                            if (anneau.Count < 2) continue;

                            Polyline poly = new Polyline();
                            for (int i = 0; i < anneau.Count; i++)
                                poly.AddVertexAt(i,
                                    new Point2d(anneau[i].X * scaleFromMeters, anneau[i].Y * scaleFromMeters),
                                    0, 0, 0);
                            poly.Closed = true;
                            if (doCalques) poly.Layer = CadastreOptions.CalqueSubdivisions;

                            btr.AppendEntity(poly);
                            tr.AddNewlyCreatedDBObject(poly, true);
                            nbSubdivisions++;
                        }
                    }
                }

                tr.Commit();

                // ---- Resume ----
                string resume = $"\nImport termine : ";
                if (doParcelles && parcelles != null)
                    resume += $"{parcelles.Count} parcelle(s) → {nbPolylignes} polyligne(s). ";
                if (doLibelles)
                    resume += $"{nbTextes} libelle(s). ";
                if (doBati)
                    resume += $"{nbHachures} batiment(s). ";
                if (doLocalisants)
                    resume += $"{nbLocalisants} localisant(s). ";
                if (doSubdivisions)
                    resume += $"{nbSubdivisions} subdivision(s). ";
                if (doCalques)
                    resume += $"\n  Calques : {CadastreOptions.CalqueParcelles}, {CadastreOptions.CalqueBati}, {CadastreOptions.CalqueLibelles}, {CadastreOptions.CalqueLocalisants}, {CadastreOptions.CalqueSubdivisions}";
                ed.WriteMessage(resume);
            }
        }

        // Bornes approximatives de la Lambert93 (EPSG:2154) pour la France métropolitaine
        // + marge. Sert uniquement de fallback quand INSUNITS ne donne pas d'unité métrique.
        private const double LambertXMin = 0.0;
        private const double LambertXMax = 1_300_000.0;
        private const double LambertYMin = 6_000_000.0;
        private const double LambertYMax = 7_200_000.0;

        /// <summary>
        /// Retourne le facteur de conversion "unité dessin → mètres".
        /// Priorité 1 : db.Insunits (le sélecteur UNITS de ZWCAD) pour les unités métriques,
        ///             MAIS validé contre les bornes Lambert93 : si le facteur INSUNITS donne
        ///             des coordonnées aberrantes (ex: dessin en mm mais UNITS réglé sur m),
        ///             on détecte l'incohérence et on passe en auto-détection.
        /// Priorité 2 : auto-détection par magnitude dans les bornes Lambert93 (fallback).
        /// </summary>
        private static double GetScaleToMeters(Database db, double xRaw, double yRaw, Editor ed)
        {
            // 1. Lecture de l'unité du dessin (INSUNITS)
            int insunits = ReadInsunits(db);

            // 2. Si c'est une unité métrique connue → essayer ce facteur, mais VALIDER
            double? metricFactor = InsunitsToScaleFactor(insunits);
            if (metricFactor.HasValue)
            {
                double xTest = xRaw * metricFactor.Value;
                double yTest = yRaw * metricFactor.Value;
                if (xTest >= LambertXMin && xTest <= LambertXMax &&
                    yTest >= LambertYMin && yTest <= LambertYMax)
                {
                    // Le facteur INSUNITS est cohérent avec les coordonnées réelles
                    string unitName = GetUnitName(insunits);
                    ed.WriteMessage($"\n[Debug] Unité dessin : {unitName} (INSUNITS={insunits}, facteur x{metricFactor.Value})");
                    return metricFactor.Value;
                }
                else
                {
                    // Incohérence : l'unité déclarée ne correspond pas aux coordonnées brutes.
                    // Exemple classique : dessin créé en mm, puis UNITS passé sur Meters
                    // → INSUNITS=6 mais les coordonnées sont toujours en mm.
                    string unitName = GetUnitName(insunits);
                    ed.WriteMessage($"\n[Debug] Unité dessin = {unitName} (INSUNITS={insunits}) mais facteur x{metricFactor.Value} " +
                                   $"donne X={xTest:F0} Y={yTest:F0} → hors bornes Lambert93");
                    ed.WriteMessage($"\n[Debug] → incohérence détectée, fallback sur auto-détection...");
                }
            }
            else
            {
                ed.WriteMessage($"\n[Debug] INSUNITS={insunits} ({GetUnitName(insunits)}) → unité non métrique, fallback auto-détection");
            }

            // 3. Fallback : auto-détection (unité non métrique, ou incohérence détectée)
            return AutoDetectScaleToMeters(xRaw, yRaw, ed);
        }

        /// <summary>
        /// Lit db.Insunits de manière robuste (propriété de type UnitsValue enum ou int selon version ZWCAD).
        /// </summary>
        private static int ReadInsunits(Database db)
        {
            try
            {
                // db.Insunits est de type UnitsValue (enum) dans l'API ZWCAD/AutoCAD.
                // Un cast direct en int fonctionne sur toutes les versions.
                return (int)db.Insunits;
            }
            catch
            {
                return 0; // 0 = Unitless/Undefined → fallback auto-détection
            }
        }

        /// <summary>
        /// Convertit une valeur INSUNITS en facteur vers les mètres.
        /// Retourne null si l'unité n'est pas une unité métrique gérée → fallback nécessaire.
        /// </summary>
        private static double? InsunitsToScaleFactor(int insunits)
        {
            switch (insunits)
            {
                // Unités métriques (valeurs standard AutoCAD/ZWCAD UnitsValue)
                case 4:  return 0.001;   // Millimeters  → ×0.001 pour avoir des mètres
                case 5:  return 0.01;    // Centimeters  → ×0.01
                case 14: return 0.1;     // Decimeters   → ×0.1
                case 6:  return 1.0;     // Meters       → déjà en mètres
                case 15: return 10.0;    // Decameters   → ×10
                case 16: return 100.0;   // Hectometers  → ×100
                case 7:  return 1000.0;  // Kilometers   → ×1000
                default: return null;    // Unitless, Impérial, etc. → fallback
            }
        }

        /// <summary>
        /// Nom lisible de l'unité INSUNITS (pour logs de debug).
        /// </summary>
        private static string GetUnitName(int insunits)
        {
            switch (insunits)
            {
                case 0:  return "Unitless";
                case 1:  return "Inches";
                case 2:  return "Feet";
                case 3:  return "Miles";
                case 4:  return "Millimeters";
                case 5:  return "Centimeters";
                case 6:  return "Meters";
                case 7:  return "Kilometers";
                case 8:  return "Microinches";
                case 9:  return "Mils";
                case 10: return "Yards";
                case 11: return "Angstroms";
                case 12: return "Nanometers";
                case 13: return "Microns";
                case 14: return "Decimeters";
                case 15: return "Decameters";
                case 16: return "Hectometers";
                case 17: return "Gigameters";
                default: return $"Unknown({insunits})";
            }
        }

        /// <summary>
        /// Fallback : auto-détection du facteur en testant une série de facteurs « ronds »
        /// contre les bornes réelles de la Lambert93 (France métropolitaine).
        /// N'est appelé que si INSUNITS ne donne pas d'unité métrique exploitable.
        /// </summary>
        private static double AutoDetectScaleToMeters(double xRaw, double yRaw, Editor ed)
        {
            double[] candidats =
            {
                1.0,        // dessin déjà en mètres
                0.001,      // dessin en millimètres
                0.01,       // dessin en centimètres
                0.1,        // dessin en décimètres
                1000.0,     // dessin en kilomètres
                0.0254,     // dessin en pouces
                0.3048,     // dessin en pieds
            };

            foreach (double f in candidats)
            {
                double x = xRaw * f;
                double y = yRaw * f;
                if (x >= LambertXMin && x <= LambertXMax && y >= LambertYMin && y <= LambertYMax)
                {
                    ed.WriteMessage($"facteur retenu : x{f}");
                    return f;
                }
            }

            ed.WriteMessage("\n[Cadastre] Attention : impossible de detecter automatiquement l'echelle du dessin " +
                             "(le point clique ne correspond, a aucun facteur usuel teste, a une position en France metropolitaine). " +
                             "Aucune conversion n'a ete appliquee (facteur = 1). Verifie que ton dessin est bien georeference en Lambert93.");
            return 1.0;
        }

        // ============================================================
        // Helpers geometrie et calques
        // ============================================================

        /// <summary>
        /// Calcule le centroide (centre de masse) d'un polygone 2D via la formule du shoelace.
        /// Plus robuste que la moyenne simple pour les polygones irreguliers (parcelles).
        /// </summary>
        private static (double X, double Y) ComputePolygonCentroid(System.Collections.Generic.List<(double X, double Y)> vertices)
        {
            int n = vertices.Count;
            if (n < 3) return (0, 0);

            double area = 0.0, cx = 0.0, cy = 0.0;

            for (int i = 0; i < n; i++)
            {
                var p0 = vertices[i];
                var p1 = vertices[(i + 1) % n];
                double cross = p0.X * p1.Y - p1.X * p0.Y;
                area += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
            }

            area *= 0.5;
            if (Math.Abs(area) < 1e-12)
            {
                // Polygone degenere → moyenne simple
                double sx = 0, sy = 0;
                for (int i = 0; i < n; i++) { sx += vertices[i].X; sy += vertices[i].Y; }
                return (sx / n, sy / n);
            }

            cx /= (6.0 * area);
            cy /= (6.0 * area);
            return (cx, cy);
        }

        /// <summary>
        /// Recupere ou cree un calque dans le dessin courant.
        /// </summary>
        private static ObjectId GetOrCreateLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(layerName))
                return lt[layerName];

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord();
            ltr.Name = layerName;
            // ZWCAD : ColorIndex n'existe pas sur LayerTableRecord, on utilise Color
            // avec l'enum ColorMethod.ByAci (valeur 1 = ACI).
            ltr.Color = ZwSoft.ZwCAD.Colors.Color.FromColorIndex(
                ZwSoft.ZwCAD.Colors.ColorMethod.ByAci, colorIndex);

            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }
    }
}
