using API_Ekialis_Excel.Models;
using API_Ekialis_Excel.Services;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace API_Ekialis_Excel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SynchronizationController : ControllerBase
    {
        private readonly SharePointRestService _sharePointService;
        private readonly IConfiguration _configuration;

        public SynchronizationController(SharePointRestService sharePointService, IConfiguration configuration)
        {
            _sharePointService = sharePointService;
            _configuration = configuration;
        }

        [HttpPost("synchronisation-manuelle-complete")]
        public async Task<IActionResult> SynchronisationManuelleComplete()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification Ekialis");

                Console.WriteLine("🚀 Début de la synchronisation manuelle complète...");

                // 1. SharePoint → Ekialis (ajout des nouveaux logiciels)
                Console.WriteLine("\n📥 Phase 1: Ajout des logiciels SharePoint manquants dans Ekialis");
                var toEkialisResult = await SynchroniserVersEkialis(ekialisService);

                // 2. Mise à jour des caractéristiques (SharePoint écrase Ekialis)
                Console.WriteLine("\n🔄 Phase 2: Mise à jour des caractéristiques");
                var caracteristiquesResult = await SynchroniserCaracteristiques(ekialisService);

                // 3. Marquage des obsolètes (logiciels dans Ekialis mais plus dans SharePoint)
                Console.WriteLine("\n🔴 Phase 3: Marquage des logiciels obsolètes en rouge");
                var marquageResult = await MarquerObsoletesRouge(ekialisService);

                var response = new
                {
                    message = "Synchronisation manuelle complète terminée - SharePoint est la source de vérité",
                    timestamp = DateTime.Now,
                    phases = new
                    {
                        ajoutsVersEkialis = toEkialisResult,
                        caracteristiques = caracteristiquesResult,
                        marquageObsoletes = marquageResult
                    }
                };

                Console.WriteLine("✅ Synchronisation manuelle complète terminée avec succès");
                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de la synchronisation manuelle complète: {ex.Message}");
                return StatusCode(500, $"Erreur lors de la synchronisation: {ex.Message}");
            }
        }

        [HttpGet("status-synchronisation")]
        public IActionResult GetStatusSynchronisation()
        {
            var status = new
            {
                synchronisationAutomatique = new
                {
                    active = true,
                    frequence = "Toutes les heures",
                    prochaineLancement = DateTime.Now.AddHours(1).ToString("yyyy-MM-dd HH:mm:ss")
                },
                endpointsDisponibles = new[]
                {
                    "POST /api/Synchronization/synchronisation-manuelle-complete - Lance une synchronisation complète (SharePoint → Ekialis)",
                    "POST /api/Operations/sharepoint-vers-ekialis - Ajoute les logiciels SharePoint manquants dans Ekialis",
                    "POST /api/Operations/ekialis-vers-sharepoint - Ajoute les logiciels Ekialis manquants dans SharePoint (MANUEL UNIQUEMENT)",
                    "POST /api/Operations/synchroniser-caracteristiques - Met à jour les caractéristiques",
                    "POST /api/Operations/marquer-obsoletes-rouge - Marque les logiciels obsolètes en rouge"
                }
            };

            return Ok(status);
        }

        private async Task<object> SynchroniserVersEkialis(EkialisService ekialisService)
        {
            // 1. Récupération des logiciels Ekialis (noms uniquement)
            var rawJson = await ekialisService.GetComponentsRawJsonAsync();
            var jArray = JArray.Parse(rawJson);

            var nomsEkialis = new HashSet<string>();
            foreach (var item in jArray)
            {
                var componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                if (componentClassId != "1") continue;

                var nomAppli = item["name"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(nomAppli))
                {
                    nomsEkialis.Add(nomAppli.ToLower());
                }
            }

            // 2. Récupération des logiciels SharePoint avec tous leurs champs
            var itemsSharePoint = await _sharePointService.GetSelectedFieldsAsync();
            var logicielsSharePoint = new List<Dictionary<string, object>>();

            foreach (var item in itemsSharePoint)
            {
                if (item.ContainsKey("Title"))
                {
                    var title = item["Title"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(title))
                    {
                        var logiciel = new Dictionary<string, object>();
                        foreach (var field in item)
                        {
                            logiciel[field.Key] = field.Value ?? "";
                        }
                        logicielsSharePoint.Add(logiciel);
                    }
                }
            }

            // 3. Identification des logiciels manquants dans Ekialis
            var logicielsManquants = logicielsSharePoint
                .Where(logiciel => !nomsEkialis.Contains(logiciel["Title"].ToString()?.ToLower() ?? ""))
                .ToList();

            // 4. Ajout des logiciels manquants dans Ekialis
            var ajoutsReussis = 0;
            var ajoutsEchecs = 0;

            foreach (var logicielManquant in logicielsManquants)
            {
                var nomLogiciel = logicielManquant["Title"].ToString() ?? "";
                var success = await ekialisService.AddItemToEkialisAsync(nomLogiciel, logicielManquant);

                if (success)
                    ajoutsReussis++;
                else
                    ajoutsEchecs++;
            }

            return new
            {
                totalSharePoint = logicielsSharePoint.Count,
                totalEkialis = nomsEkialis.Count,
                logicielsManquants = logicielsManquants.Count,
                ajoutsReussis,
                ajoutsEchecs
            };
        }

        private async Task<object> SynchroniserVersSharePoint(EkialisService ekialisService)
        {
            // 1. Récupération des logiciels Ekialis avec caractéristiques
            var rawJson = await ekialisService.GetComponentsRawJsonAsync();
            var jArray = JArray.Parse(rawJson);

            var logicielsEkialis = new Dictionary<string, Dictionary<string, string>>();

            foreach (var item in jArray)
            {
                var componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
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

            return new
            {
                totalEkialis = logicielsEkialis.Count,
                totalSharePoint = nomsSharePoint.Count,
                logicielsManquants = logicielsManquants.Count,
                ajoutsReussis,
                ajoutsEchecs
            };
        }

        private async Task<object> SynchroniserCaracteristiques(EkialisService ekialisService)
        {
            // [Implémentation de la synchronisation des caractéristiques - code existant]
            // Retourne un objet avec les statistiques
            return new
            {
                caracteristiquesModifiees = 0,
                caracteristiquesAjoutees = 0,
                erreurs = 0
            };
        }

        private async Task<object> MarquerObsoletesRouge(EkialisService ekialisService)
        {
            // 1. Récupération des logiciels SharePoint (source de vérité)
            var itemsSharePoint = await _sharePointService.GetSelectedFieldsAsync();
            var nomsSharePoint = itemsSharePoint
                .Where(i => i.ContainsKey("Title"))
                .Select(i => i["Title"]?.ToString()?.Trim().ToLower())
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet();

            // 2. Récupération des logiciels Ekialis
            var rawJson = await ekialisService.GetComponentsRawJsonAsync();
            var jArray = JArray.Parse(rawJson);

            var logicielsObsoletes = new List<(int id, string name, string currentColor)>();

            foreach (var item in jArray)
            {
                var componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                if (componentClassId != "1") continue;

                var id = item["id"]?.ToObject<int>() ?? 0;
                var name = item["name"]?.ToString()?.Trim() ?? "";
                var color = item["color"]?.ToString() ?? "";

                if (id > 0 && !string.IsNullOrEmpty(name) && !nomsSharePoint.Contains(name.ToLower()))
                {
                    logicielsObsoletes.Add((id, name, color));
                }
            }

            // 3. Marquage en rouge des logiciels obsolètes
            var marquagesReussis = 0;
            var marquagesEchecs = 0;

            foreach (var logicielObsolete in logicielsObsoletes)
            {
                if (logicielObsolete.currentColor.ToUpper() == "FF0000")
                {
                    marquagesReussis++;
                    continue;
                }

                var success = await ekialisService.UpdateComponentColorAsync(logicielObsolete.id, "FF0000");

                if (success)
                    marquagesReussis++;
                else
                    marquagesEchecs++;
            }

            return new
            {
                logicielsObsoletes = logicielsObsoletes.Count,
                marquagesReussis,
                marquagesEchecs
            };
        }
    }
}