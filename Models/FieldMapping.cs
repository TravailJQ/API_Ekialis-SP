namespace API_Ekialis_Excel.Models
{
    public static class FieldMapping
    {
        // Mapping SharePoint vers Ekialis
        public static readonly Dictionary<string, string> SharePointToEkialis = new Dictionary<string, string>
        {
            { "field_1", "GEN-Domaine métier [Pôle NGE]" },
            { "field_2", "ACTEUR-Resp.Appli [Référent]" },
            { "field_3", "GEN-Description [Rôle]" },
            { "field_6", "GEN-Editeur" }
            // field_9 n'a pas de mapping (aucun)
        };

        // Mapping inverse Ekialis vers SharePoint
        public static readonly Dictionary<string, string> EkialisToSharePoint = new Dictionary<string, string>
        {
            { "GEN-Domaine métier [Pôle NGE]", "field_1" },
            { "ACTEUR-Resp.Appli [Référent]", "field_2" },
            { "GEN-Description [Rôle]", "field_3" },
            { "GEN-Editeur", "field_6" }
        };

        // Champs SharePoint mappés
        public static readonly List<string> MappedSharePointFields = new List<string>
        {
            "Title", "field_1", "field_2", "field_3", "field_6", "field_9"
        };

        // Caractéristiques Ekialis mappées
        public static readonly List<string> MappedEkialisCharacteristics = new List<string>
        {
            "GEN-Domaine métier [Pôle NGE]",
            "ACTEUR-Resp.Appli [Référent]",
            "GEN-Description [Rôle]",
            "GEN-Editeur"
        };

        // Méthodes utilitaires
        public static string? GetEkialisCharacteristic(string sharePointField)
        {
            return SharePointToEkialis.TryGetValue(sharePointField, out var characteristic) ? characteristic : null;
        }

        public static string? GetSharePointField(string ekialisCharacteristic)
        {
            return EkialisToSharePoint.TryGetValue(ekialisCharacteristic, out var field) ? field : null;
        }

        public static bool IsFieldMapped(string sharePointField)
        {
            return SharePointToEkialis.ContainsKey(sharePointField);
        }

        public static bool IsCharacteristicMapped(string ekialisCharacteristic)
        {
            return EkialisToSharePoint.ContainsKey(ekialisCharacteristic);
        }
    }
}