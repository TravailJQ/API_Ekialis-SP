using Newtonsoft.Json;
using System.Linq;
using System.Text;
using System.Reflection;
using API_Ekialis_Excel.Models;

namespace API_Ekialis_Excel.Services
{
    public class ExportService
    {
        public ExportService()
        {
            // Plus besoin de configuration EPPlus
        }

        /// <summary>
        /// Exporte une liste d'objets en format CSV
        /// </summary>
        /// <typeparam name="T">Type des objets à exporter</typeparam>
        /// <param name="data">Liste des données à exporter</param>
        /// <param name="separator">Séparateur CSV (par défaut point-virgule)</param>
        /// <returns>Données CSV en bytes</returns>
        public byte[] ExportToCsv<T>(List<T> data, string separator = ";")
        {
            if (!data.Any())
                return System.Text.Encoding.UTF8.GetBytes("");

            var csv = new StringBuilder();

            // En-têtes (noms des propriétés)
            var properties = typeof(T).GetProperties();
            var headers = string.Join(separator, properties.Select(p => p.Name));
            csv.AppendLine(headers);

            // Données
            foreach (var item in data)
            {
                var values = properties.Select(p =>
                {
                    var value = p.GetValue(item)?.ToString() ?? "";
                    // Échapper les guillemets et séparateurs
                    if (value.Contains(separator) || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                    {
                        value = "\"" + value.Replace("\"", "\"\"") + "\"";
                    }
                    return value;
                });

                csv.AppendLine(string.Join(separator, values));
            }

            return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        }

        /// <summary>
        /// Exporte une liste d'objets en format CSV avec en-têtes personnalisés
        /// </summary>
        /// <typeparam name="T">Type des objets à exporter</typeparam>
        /// <param name="data">Liste des données à exporter</param>
        /// <param name="customHeaders">En-têtes personnalisés</param>
        /// <param name="separator">Séparateur CSV</param>
        /// <returns>Données CSV en bytes</returns>
        public byte[] ExportToCsvWithHeaders<T>(List<T> data, Dictionary<string, string> customHeaders, string separator = ";")
        {
            if (!data.Any())
                return System.Text.Encoding.UTF8.GetBytes("");

            var csv = new StringBuilder();
            var properties = typeof(T).GetProperties();

            // En-têtes personnalisés
            var headers = properties.Select(p =>
                customHeaders.ContainsKey(p.Name) ? customHeaders[p.Name] : p.Name
            );
            csv.AppendLine(string.Join(separator, headers));

            // Données
            foreach (var item in data)
            {
                var values = properties.Select(p =>
                {
                    var value = p.GetValue(item)?.ToString() ?? "";
                    if (value.Contains(separator) || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                    {
                        value = "\"" + value.Replace("\"", "\"\"") + "\"";
                    }
                    return value;
                });

                csv.AppendLine(string.Join(separator, values));
            }

            return System.Text.Encoding.UTF8.GetBytes(csv.ToString());
        }

        /// <summary>
        /// Exporte en JSON (conservé pour compatibilité)
        /// </summary>
        /// <typeparam name="T">Type des objets à exporter</typeparam>
        /// <param name="data">Liste des données à exporter</param>
        /// <returns>JSON formaté</returns>
        public string ExportToJson<T>(List<T> data)
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }

        /// <summary>
        /// Crée un CSV spécialement formaté pour les logiciels
        /// </summary>
        /// <param name="logiciels">Liste des logiciels</param>
        /// <returns>CSV avec en-têtes en français</returns>
        public byte[] ExportLogicielsToCsv(List<ComponentFlat> logiciels)
        {
            var headers = new Dictionary<string, string>
            {
                { "Id", "ID" },
                { "Name", "Nom du logiciel" },
                { "Icon", "Icône" },
                { "Color", "Couleur" },
                { "ComponentClassId", "ID Classe" },
                { "ComponentClassName", "Type" },
                { "ComponentStatusId", "ID Statut" },
                { "ComponentStatusName", "Statut" },
                { "Company", "Entreprise" },
                { "CharacteristicsCount", "Nb Caractéristiques" },
                { "SourceRelationsCount", "Nb Relations" }
            };

            return ExportToCsvWithHeaders(logiciels, headers, ";");
        }
    }
}