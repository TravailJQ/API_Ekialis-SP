using API_Ekialis_Excel.Models;
using API_Ekialis_Excel.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace API_Ekialis_Excel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EkialisController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        public EkialisController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Export brut uniquement avec les noms et les caractéristiques formatées
        /// </summary>
        [HttpGet("logiciels-caracteristiques")]
        public async Task<IActionResult> GetLogicielsAvecCaracteristiques()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification avec Ekialis");

                // 1. Récupération de toutes les caractéristiques connues
                var characteristics = await ekialisService.GetCharacteristicsAsync();
                var characDict = characteristics.ToDictionary(c => c.Id, c => c.Name);

                Console.WriteLine($"🔍 Nombre de caractéristiques dans le dictionnaire: {characDict.Count}");

                // 2. Données brutes des composants
                var rawJson = await ekialisService.GetComponentsRawJsonAsync();
                var jArray = JArray.Parse(rawJson);

                Console.WriteLine($"🔍 Nombre total d'éléments dans le JSON: {jArray.Count}");

                var logiciels = new List<object>();
                int logicielsTraites = 0;
                int logicielsAvecCaracteristiques = 0;

                foreach (var item in jArray)
                {
                    Console.WriteLine($"\n--- ITEM {logicielsTraites + 1} ---");
                    Console.WriteLine($"Item complet: {item.ToString().Substring(0, Math.Min(500, item.ToString().Length))}...");

                    // Vérification de la structure componentClass
                    var componentClassId = "";
                    if (item["componentClass"] != null)
                    {
                        if (item["componentClass"]?["id"] != null)
                        {
                            componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                            Console.WriteLine($"ComponentClass ID (objet): {componentClassId}");
                        }
                        else if (item["componentClass"]?.Type == JTokenType.Integer)
                        {
                            componentClassId = item["componentClass"]?.ToString() ?? "";
                            Console.WriteLine($"ComponentClass ID (direct): {componentClassId}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ ComponentClass est null");
                    }

                    // On ne garde que les logiciels (componentClassId = 1)
                    if (componentClassId != "1")
                    {
                        Console.WriteLine($"❌ Pas un logiciel (componentClassId = {componentClassId})");
                        continue;
                    }

                    logicielsTraites++;
                    var nomAppli = item["name"]?.ToString() ?? "";
                    var icon = item["icon"]?.ToString() ?? "";
                    var color = item["color"]?.ToString() ?? "";

                    Console.WriteLine($"✅ Logiciel trouvé: '{nomAppli}' (icon: {icon}, color: {color})");

                    var caracteristiques = new List<string>();

                    if (item["characteristics"] is JArray caractList)
                    {
                        Console.WriteLine($"📋 Nombre de caractéristiques: {caractList.Count}");

                        if (caractList.Count > 0)
                        {
                            logicielsAvecCaracteristiques++;
                            Console.WriteLine($"Première caractéristique: {caractList[0].ToString()}");
                        }

                        foreach (var caract in caractList)
                        {
                            // Structure corrigée selon le JSON observé
                            var valeur = caract["characteristicValue"]?["value"]?.ToString();
                            var characId = caract["id"]?.ToObject<int>() ?? 0;
                            var nomCaracFromJson = caract["name"]?.ToString() ?? "";

                            Console.WriteLine($"  - Caractéristique ID: {characId}, Nom: '{nomCaracFromJson}', Valeur: '{valeur}'");

                            if (!string.IsNullOrWhiteSpace(valeur))
                            {
                                // Utilise le nom depuis le JSON directement ou depuis le dictionnaire
                                var nomCarac = !string.IsNullOrWhiteSpace(nomCaracFromJson)
                                    ? nomCaracFromJson
                                    : (characDict.TryGetValue(characId, out var nom) ? nom : $"Inconnu ({characId})");

                                caracteristiques.Add($"{nomCarac} ({valeur})");
                                Console.WriteLine($"    ➜ Ajouté: {nomCarac} ({valeur})");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("❌ Pas de caractéristiques trouvées ou format incorrect");
                        Console.WriteLine($"Type de characteristics: {item["characteristics"]?.Type}");
                        Console.WriteLine($"Valeur de characteristics: {item["characteristics"]?.ToString()}");
                    }

                    logiciels.Add(new
                    {
                        NOM_APPLI = nomAppli,
                        icon = icon,
                        color = color,
                        caracteristiques
                    });

                    Console.WriteLine($"✅ Logiciel ajouté avec {caracteristiques.Count} caractéristiques");
                }

                Console.WriteLine($"\n📊 RÉSUMÉ:");
                Console.WriteLine($"- Logiciels traités: {logicielsTraites}");
                Console.WriteLine($"- Logiciels avec caractéristiques: {logicielsAvecCaracteristiques}");
                Console.WriteLine($"- Total logiciels dans la réponse: {logiciels.Count}");

                var sorted = logiciels.OrderBy(l => ((string)((dynamic)l).NOM_APPLI)).ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(sorted, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ERREUR: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return StatusCode(500, $"Erreur: {ex.Message}");
            }
        }
    }
}