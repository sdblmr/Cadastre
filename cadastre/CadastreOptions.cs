namespace cadastre
{
    /// <summary>
    /// Options globales partagees entre le ruban et la commande CADASTRE.
    /// Statique car les boutons du ruban et la commande tournent dans
    /// des contextes d'execution differents (SendStringToExecute est asynchrone).
    /// </summary>
    public static class CadastreOptions
    {
        // ---- Options d'import ----
        public static bool ImporterParcelles { get; set; } = true;
        public static bool ImporterBati { get; set; } = false;
        public static bool ImporterLibelles { get; set; } = false;
        public static bool ImporterLocalisants { get; set; } = false;
        public static bool ImporterSubdivisions { get; set; } = false;

        // ---- Calques ----
        public static bool CreerCalques { get; set; } = false;
        public static string CalqueParcelles { get; set; } = "CAD_PARCELLES";
        public static string CalqueBati { get; set; } = "CAD_BATI";
        public static string CalqueLibelles { get; set; } = "CAD_LIBELLES";
        public static string CalqueLocalisants { get; set; } = "CAD_LOCALISANTS";
        public static string CalqueSubdivisions { get; set; } = "CAD_SUBDIVISIONS";

        // Couleurs par defaut (index ACI 0-255)
        public static short CouleurParcelles { get; set; } = 7;
        public static short CouleurBati { get; set; } = 40;
        public static short CouleurLibelles { get; set; } = 4;
        public static short CouleurLocalisants { get; set; } = 2;
        public static short CouleurSubdivisions { get; set; } = 6;
    }
}
