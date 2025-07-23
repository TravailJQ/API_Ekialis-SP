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

                // 2. Données brutes des composants
                var rawJson = await ekialisService.GetComponentsRawJsonAsync();
                var jArray = JArray.Parse(rawJson);

                var logiciels = new List<object>();

                foreach (var item in jArray)
                {
                    // On ne garde que les logiciels (componentClassId = 1)
                    if (item["componentClass"]?["id"]?.ToString() != "1")
                        continue;

                    var nomAppli = item["name"]?.ToString() ?? "";

                    var caracteristiques = new List<string>();

                    if (item["characteristics"] is JArray caractList)
                    {
                        foreach (var caract in caractList)
                        {
                            var valeur = caract["value"]?.ToString();
                            var characId = caract["characteristic"]?["id"]?.ToObject<int>() ?? 0;

                            if (!string.IsNullOrWhiteSpace(valeur))
                            {
                                var nomCarac = characDict.TryGetValue(characId, out var nom)
                                    ? nom
                                    : $"Inconnu ({characId})";

                                caracteristiques.Add($"{nomCarac} ({valeur})");
                            }
                        }
                    }

                    logiciels.Add(new
                    {
                        NOM_APPLI = nomAppli,
                        caracteristiques
                    });
                }

                var sorted = logiciels.OrderBy(l => ((string)((dynamic)l).NOM_APPLI)).ToList();

                var json = System.Text.Json.JsonSerializer.Serialize(sorted, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                return Content(json, "application/json");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erreur: {ex.Message}");
            }
        }
    }
}