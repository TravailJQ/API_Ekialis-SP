using API_Ekialis_Excel.Models;
using API_Ekialis_Excel.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace API_Ekialis_Excel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ComparaisonController : ControllerBase
    {
        private readonly SharePointRestService _sharePointService;
        private readonly IConfiguration _configuration;

        public ComparaisonController(SharePointRestService sharePointService, IConfiguration configuration)
        {
            _sharePointService = sharePointService;
            _configuration = configuration;
        }

        [HttpGet("logiciels")]
        public async Task<IActionResult> ComparerLogiciels()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification Ekialis");

                // Liste des logiciels Ekialis
                var logicielsEkialis = await ekialisService.GetSoftwareComponentsFlatAsync();
                var nomsEkialis = logicielsEkialis
                    .Select(l => l.Name?.Trim().ToLower())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                // Liste SharePoint (on suppose que SharePoint:Fields = Title dans appsettings)
                var itemsSharePoint = await _sharePointService.GetSelectedFieldsAsync();
                var nomsSharePoint = itemsSharePoint
                    .Where(i => i.ContainsKey("Title"))
                    .Select(i => i["Title"]?.ToString()?.Trim().ToLower())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n)
                    .ToList();

                // Comparaison
                var manquantsDansSharePoint = nomsEkialis.Except(nomsSharePoint).ToList();
                var manquantsDansEkialis = nomsSharePoint.Except(nomsEkialis).ToList();

                var response = new
                {
                    totalEkialis = nomsEkialis.Count,
                    totalSharePoint = nomsSharePoint.Count,
                    identiques = nomsEkialis.Intersect(nomsSharePoint).Count(),
                    differences = new
                    {
                        dansEkialisPasDansSharePoint = manquantsDansSharePoint,
                        dansSharePointPasDansEkialis = manquantsDansEkialis
                    }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erreur lors de la comparaison : {ex.Message}");
            }
        }

        [HttpPost("synchroniser-vers-sharepoint")]
        public async Task<IActionResult> SynchroniserVersSharePoint()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification Ekialis");

                // 1. Récupération des logiciels Ekialis avec caractéristiques
                var characteristics = await ekialisService.GetCharacteristicsAsync();
                var characDict = characteristics.ToDictionary(c => c.Id, c => c.Name);

                var rawJson = await ekialisService.GetComponentsRawJsonAsync();
                var jArray = JArray.Parse(rawJson);

                var logicielsEkialis = new Dictionary<string, Dictionary<string, string>>();

                foreach (var item in jArray)
                {
                    var componentClassId = "";
                    if (item["componentClass"]?["id"] != null)
                    {
                        componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                    }

                    if (componentClassId != "1") continue;

                    var nomAppli = item["name"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(nomAppli)) continue;

                    var caracteristiques = new Dictionary<string, string>();

                    if (item["characteristics"] is JArray caractList)
                    {
                        foreach (var caract in caractList)
                        {
                            var valeur = caract["characteristicValue"]?["value"]?.ToString();
                            var nomCaracFromJson = caract["name"]?.ToString() ?? "";

                            if (!string.IsNullOrWhiteSpace(valeur) && !string.IsNullOrWhiteSpace(nomCaracFromJson))
                            {
                                // Vérifier si cette caractéristique est mappée
                                if (FieldMapping.IsCharacteristicMapped(nomCaracFromJson))
                                {
                                    caracteristiques[nomCaracFromJson] = valeur;
                                }
                            }
                        }
                    }

                    logicielsEkialis[nomAppli.ToLower()] = caracteristiques;
                }

                // 2. Récupération des logiciels SharePoint
                var itemsSharePoint = await _sharePointService.GetSelectedFieldsAsync();
                var nomsSharePoint = itemsSharePoint
                    .Where(i => i.ContainsKey("Title"))
                    .Select(i => i["Title"]?.ToString()?.Trim().ToLower())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet();

                // 3. Identification des logiciels manquants dans SharePoint
                var logicielsManquants = logicielsEkialis
                    .Where(kvp => !nomsSharePoint.Contains(kvp.Key))
                    .ToList();

                Console.WriteLine($"🔍 Logiciels manquants dans SharePoint: {logicielsManquants.Count}");

                // 4. Ajout des logiciels manquants
                var ajoutsReussis = 0;
                var ajoutsEchecs = 0;

                foreach (var logicielManquant in logicielsManquants)
                {
                    var nomOriginal = logicielsEkialis.FirstOrDefault(kvp => kvp.Key == logicielManquant.Key).Key;
                    var success = await _sharePointService.AddItemToSharePointAsync(nomOriginal, logicielManquant.Value);

                    if (success)
                        ajoutsReussis++;
                    else
                        ajoutsEchecs++;
                }

                var response = new
                {
                    totalEkialis = logicielsEkialis.Count,
                    totalSharePoint = nomsSharePoint.Count,
                    logicielsManquants = logicielsManquants.Count,
                    ajoutsReussis,
                    ajoutsEchecs,
                    logicielsAjoutes = logicielsManquants.Select(kvp => new
                    {
                        nom = kvp.Key,
                        caracteristiques = kvp.Value
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de la synchronisation: {ex.Message}");
                return StatusCode(500, $"Erreur lors de la synchronisation: {ex.Message}");
            }
        }
    }
}


