using Newtonsoft.Json;

namespace API_Ekialis_Excel.Models
{
    public class AuthenticationRequest
    {
        public string auth_key { get; set; } = string.Empty;
    }

    public class AuthenticationResponse
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    // Modèles pour les caractéristiques (anciennes API)
    public class CharacteristicValue
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public DateTime? CreatedDate { get; set; }
    }

    public class Characteristic
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<CharacteristicValue> Values { get; set; } = new List<CharacteristicValue>();
    }

    // Modèle Component simplifié avec gestion dynamique
    public class Component
    {
        public int id { get; set; }
        public string name { get; set; } = string.Empty;
        public string icon { get; set; } = string.Empty;
        public string color { get; set; } = string.Empty;

        // Utilisation de dynamic pour gérer les objets complexes
        [JsonProperty("componentClass")]
        public dynamic componentClass { get; set; } = new { id = 0, name = "" };

        [JsonProperty("componentStatus")]
        public dynamic componentStatus { get; set; } = new { id = 0, name = "" };

        public int company { get; set; }

        public List<dynamic> characteristics { get; set; } = new List<dynamic>();
        public List<dynamic> sourceRelations { get; set; } = new List<dynamic>();

        public int componentClassRelation { get; set; }

        // Propriétés helper pour accéder facilement aux valeurs
        [JsonIgnore]
        public int ComponentClassId => GetDynamicId(componentClass);

        [JsonIgnore]
        public string ComponentClassName => GetDynamicName(componentClass);

        [JsonIgnore]
        public int ComponentStatusId => GetDynamicId(componentStatus);

        [JsonIgnore]
        public string ComponentStatusName => GetDynamicName(componentStatus);

        private int GetDynamicId(dynamic obj)
        {
            try
            {
                if (obj == null) return 0;
                return obj.id ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private string GetDynamicName(dynamic obj)
        {
            try
            {
                if (obj == null) return "";
                return obj.name ?? "";
            }
            catch
            {
                return "";
            }
        }
    }

    // Modèle simplifié pour l'export Excel
    public class ComponentFlat
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int ComponentClassId { get; set; }
        public string ComponentClassName { get; set; } = string.Empty;
        public int ComponentStatusId { get; set; }
        public string ComponentStatusName { get; set; } = string.Empty;
        public int Company { get; set; }
        public int CharacteristicsCount { get; set; }
        public int SourceRelationsCount { get; set; }
    }
}