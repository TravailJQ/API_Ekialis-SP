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

        [HttpPost("synchroniser-vers-ekialis")]
        public async Task<IActionResult> SynchroniserVersEkialis()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification Ekialis");

                // 1. Récupération des logiciels Ekialis (noms uniquement)
                var rawJson = await ekialisService.GetComponentsRawJsonAsync();
                var jArray = JArray.Parse(rawJson);

                var nomsEkialis = new HashSet<string>();
                foreach (var item in jArray)
                {
                    var componentClassId = "";
                    if (item["componentClass"]?["id"] != null)
                    {
                        componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                    }

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
                            // Convertir en dictionnaire pour faciliter le traitement
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

                Console.WriteLine($"🔍 Logiciels manquants dans Ekialis: {logicielsManquants.Count}");

                // 4. Ajout des logiciels manquants dans Ekialis
                var ajoutsReussis = 0;
                var ajoutsEchecs = 0;

                foreach (var logicielManquant in logicielsManquants)
                {
                    var nomLogiciel = logicielManquant["Title"].ToString() ?? "";
                    Console.WriteLine($"🔄 Ajout de '{nomLogiciel}' dans Ekialis...");

                    var success = await ekialisService.AddItemToEkialisAsync(nomLogiciel, logicielManquant);

                    if (success)
                        ajoutsReussis++;
                    else
                        ajoutsEchecs++;
                }

                var response = new
                {
                    totalSharePoint = logicielsSharePoint.Count,
                    totalEkialis = nomsEkialis.Count,
                    logicielsManquants = logicielsManquants.Count,
                    ajoutsReussis,
                    ajoutsEchecs,
                    logicielsAjoutes = logicielsManquants.Select(logiciel => new
                    {
                        nom = logiciel["Title"].ToString(),
                        champs = logiciel.Where(kvp => kvp.Key != "Title")
                                      .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de la synchronisation vers Ekialis: {ex.Message}");
                return StatusCode(500, $"Erreur lors de la synchronisation vers Ekialis: {ex.Message}");
            }
        }

        [HttpPost("synchronisation-bidirectionnelle")]
        public async Task<IActionResult> SynchronisationBidirectionnelle()
        {
            try
            {
                Console.WriteLine("🚀 Démarrage de la synchronisation bidirectionnelle...");

                // 1. SharePoint vers Ekialis (SharePoint = source de vérité)
                Console.WriteLine("\n📥 Phase 1: Ajout des logiciels manquants dans Ekialis");
                var toEkialisResult = await SynchroniserVersEkialis();

                // 2. Ekialis vers SharePoint (nouveaux logiciels créés dans Ekialis)
                Console.WriteLine("\n📤 Phase 2: Ajout des nouveaux logiciels d'Ekialis dans SharePoint");
                var toSharePointResult = await SynchroniserVersSharePoint();

                var response = new
                {
                    message = "Synchronisation bidirectionnelle terminée",
                    versEkialis = toEkialisResult,
                    versSharePoint = toSharePointResult
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de la synchronisation bidirectionnelle: {ex.Message}");
                return StatusCode(500, $"Erreur lors de la synchronisation bidirectionnelle: {ex.Message}");
            }
        }

        [HttpPost("synchroniser-caracteristiques")]
        public async Task<IActionResult> SynchroniserCaracteristiques()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification Ekialis");

                Console.WriteLine("🔄 Début de la synchronisation des caractéristiques...");

                // 1. Récupération des logiciels Ekialis avec leurs caractéristiques
                var rawJson = await ekialisService.GetComponentsRawJsonAsync();
                var jArray = JArray.Parse(rawJson);

                var logicielsEkialis = new Dictionary<string, (int id, Dictionary<string, (int valueId, string currentValue, int characteristicId)> characteristics)>();

                foreach (var item in jArray)
                {
                    var componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                    if (componentClassId != "1") continue;

                    var nomAppli = item["name"]?.ToString()?.Trim() ?? "";
                    var componentId = item["id"]?.ToObject<int>() ?? 0;

                    if (string.IsNullOrEmpty(nomAppli) || componentId == 0) continue;

                    var caracteristiques = new Dictionary<string, (int valueId, string currentValue, int characteristicId)>();

                    if (item["characteristics"] is JArray caractList)
                    {
                        foreach (var caract in caractList)
                        {
                            var nomCarac = caract["name"]?.ToString() ?? "";
                            var valeur = caract["characteristicValue"]?["value"]?.ToString() ?? "";
                            var valueId = caract["characteristicValue"]?["id"]?.ToObject<int>() ?? 0;
                            var characteristicId = caract["id"]?.ToObject<int>() ?? 0;

                            if (FieldMapping.IsCharacteristicMapped(nomCarac) && valueId > 0 && characteristicId > 0)
                            {
                                caracteristiques[nomCarac] = (valueId, valeur, characteristicId);
                            }
                        }
                    }

                    logicielsEkialis[nomAppli.ToLower()] = (componentId, caracteristiques);
                }

                // 2. Récupération des logiciels SharePoint
                var itemsSharePoint = await _sharePointService.GetSelectedFieldsAsync();
                var logicielsSharePoint = new Dictionary<string, Dictionary<string, string>>();

                foreach (var item in itemsSharePoint)
                {
                    if (!item.ContainsKey("Title")) continue;

                    var title = item["Title"]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(title)) continue;

                    var champs = new Dictionary<string, string>();
                    foreach (var field in item)
                    {
                        if (field.Key != "Title" && FieldMapping.IsFieldMapped(field.Key))
                        {
                            var valeur = field.Value?.ToString()?.Trim() ?? "";
                            if (!string.IsNullOrEmpty(valeur))
                            {
                                var caracteristique = FieldMapping.GetEkialisCharacteristic(field.Key);
                                if (!string.IsNullOrEmpty(caracteristique))
                                {
                                    champs[caracteristique] = valeur;
                                }
                            }
                        }
                    }

                    logicielsSharePoint[title.ToLower()] = champs;
                }

                // 3. Synchronisation des logiciels communs
                var logicielsCommuns = logicielsEkialis.Keys.Intersect(logicielsSharePoint.Keys).ToList();
                Console.WriteLine($"🔍 Logiciels communs trouvés: {logicielsCommuns.Count}");

                var caracteristiquesModifiees = 0;
                var caracteristiquesAjoutees = 0;
                var erreurs = 0;

                foreach (var nomLogiciel in logicielsCommuns)
                {
                    Console.WriteLine($"\n📋 Traitement de '{nomLogiciel}':");

                    var (componentId, caracteristiquesEkialis) = logicielsEkialis[nomLogiciel];
                    var champsSharePoint = logicielsSharePoint[nomLogiciel];

                    foreach (var champSharePoint in champsSharePoint)
                    {
                        var nomCaracteristique = champSharePoint.Key;
                        var valeurSharePoint = champSharePoint.Value;

                        Console.WriteLine($"  🔍 Vérification: {nomCaracteristique} = '{valeurSharePoint}'");

                        if (caracteristiquesEkialis.ContainsKey(nomCaracteristique))
                        {
                            // Caractéristique existe dans Ekialis - vérifier si mise à jour nécessaire
                            var (valueId, valeurEkialis, characteristicId) = caracteristiquesEkialis[nomCaracteristique];

                            if (valeurEkialis != valeurSharePoint)
                            {
                                Console.WriteLine($"    📝 Mise à jour: '{valeurEkialis}' → '{valeurSharePoint}'");
                                var success = await ekialisService.UpdateExistingCharacteristicValueAsync(valueId, valeurSharePoint, componentId, characteristicId);
                                if (success)
                                    caracteristiquesModifiees++;
                                else
                                    erreurs++;
                            }
                            else
                            {
                                Console.WriteLine($"    ✅ Valeur identique, pas de mise à jour nécessaire");
                            }
                        }
                        else
                        {
                            // Caractéristique n'existe pas dans Ekialis - la créer
                            Console.WriteLine($"    ➕ Création de nouvelle valeur de caractéristique");
                            var success = await ekialisService.AddCharacteristicToComponentAsync(componentId, nomCaracteristique, valeurSharePoint);
                            if (success)
                                caracteristiquesAjoutees++;
                            else
                                erreurs++;
                        }
                    }
                }

                var response = new
                {
                    logicielsCommuns = logicielsCommuns.Count,
                    caracteristiquesModifiees,
                    caracteristiquesAjoutees,
                    erreurs,
                    details = logicielsCommuns.Select(nom => new
                    {
                        nom,
                        componentId = logicielsEkialis[nom].id,
                        caracteristiquesEkialis = logicielsEkialis[nom].characteristics.Count,
                        champsSharePoint = logicielsSharePoint[nom].Count
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de la synchronisation des caractéristiques: {ex.Message}");
                return StatusCode(500, $"Erreur lors de la synchronisation: {ex.Message}");
            }
        }


        [HttpPost("marquer-obsoletes-rouge")]
        public async Task<IActionResult> MarquerObsoletesRouge()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification Ekialis");

                Console.WriteLine("🔴 Début du marquage des logiciels obsolètes en rouge...");

                // 1. Récupération des logiciels SharePoint (source de vérité)
                var itemsSharePoint = await _sharePointService.GetSelectedFieldsAsync();
                var nomsSharePoint = itemsSharePoint
                    .Where(i => i.ContainsKey("Title"))
                    .Select(i => i["Title"]?.ToString()?.Trim().ToLower())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet();

                Console.WriteLine($"📋 Logiciels dans SharePoint: {nomsSharePoint.Count}");

                // 2. Récupération des logiciels Ekialis
                var rawJson = await ekialisService.GetComponentsRawJsonAsync();
                var jArray = JArray.Parse(rawJson);

                var logicielsEkialis = new List<(int id, string name, string currentColor)>();

                foreach (var item in jArray)
                {
                    var componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                    if (componentClassId != "1") continue;

                    var id = item["id"]?.ToObject<int>() ?? 0;
                    var name = item["name"]?.ToString()?.Trim() ?? "";
                    var color = item["color"]?.ToString() ?? "";

                    if (id > 0 && !string.IsNullOrEmpty(name))
                    {
                        logicielsEkialis.Add((id, name, color));
                    }
                }

                Console.WriteLine($"📋 Logiciels dans Ekialis: {logicielsEkialis.Count}");

                // 3. Identification des logiciels obsolètes (dans Ekialis mais pas dans SharePoint)
                var logicielsObsoletes = logicielsEkialis
                    .Where(logiciel => !nomsSharePoint.Contains(logiciel.name.ToLower()))
                    .ToList();

                Console.WriteLine($"🔍 Logiciels obsolètes trouvés: {logicielsObsoletes.Count}");

                // 4. Marquage en rouge des logiciels obsolètes
                var marquagesReussis = 0;
                var marquagesEchecs = 0;

                foreach (var logicielObsolete in logicielsObsoletes)
                {
                    Console.WriteLine($"🔴 Marquage de '{logicielObsolete.name}' (ID: {logicielObsolete.id})");

                    // Vérifier si déjà rouge pour éviter les appels inutiles
                    if (logicielObsolete.currentColor.ToUpper() == "FF0000")
                    {
                        Console.WriteLine($"  ✅ Déjà marqué en rouge, ignoré");
                        marquagesReussis++;
                        continue;
                    }

                    var success = await ekialisService.UpdateComponentColorAsync(logicielObsolete.id, "FF0000");

                    if (success)
                    {
                        marquagesReussis++;
                        Console.WriteLine($"  ✅ Marqué en rouge avec succès");
                    }
                    else
                    {
                        marquagesEchecs++;
                        Console.WriteLine($"  ❌ Échec du marquage");
                    }
                }

                var response = new
                {
                    totalEkialis = logicielsEkialis.Count,
                    totalSharePoint = nomsSharePoint.Count,
                    logicielsObsoletes = logicielsObsoletes.Count,
                    marquagesReussis,
                    marquagesEchecs,
                    logicielsMarques = logicielsObsoletes.Select(l => new
                    {
                        id = l.id,
                        nom = l.name,
                        ancienneCouleur = l.currentColor,
                        nouvelleCouleur = "FF0000"
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors du marquage: {ex.Message}");
                return StatusCode(500, $"Erreur lors du marquage: {ex.Message}");
            }
        }

        [HttpPost("demarrer-synchronisation-automatique")]
        public IActionResult DemarrerSynchronisationAutomatique()
        {
            // Le service background se démarre automatiquement avec l'application
            return Ok(new { message = "La synchronisation automatique est active et s'exécute toutes les heures" });
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

                // 1. SharePoint → Ekialis
                Console.WriteLine("\n📥 Phase 1: Ajout des logiciels SharePoint manquants dans Ekialis");
                var toEkialisResult = await SynchroniserVersEkialis();

                // 2. Ekialis → SharePoint  
                Console.WriteLine("\n📤 Phase 2: Ajout des logiciels Ekialis manquants dans SharePoint");
                var toSharePointResult = await SynchroniserVersSharePoint();

                // 3. Mise à jour des caractéristiques
                Console.WriteLine("\n🔄 Phase 3: Mise à jour des caractéristiques");
                var caracteristiquesResult = await SynchroniserCaracteristiques();

                // 4. Marquage des obsolètes
                Console.WriteLine("\n🔴 Phase 4: Marquage des logiciels obsolètes en rouge");
                var marquageResult = await MarquerObsoletesRouge();

                var response = new
                {
                    message = "Synchronisation manuelle complète terminée",
                    timestamp = DateTime.Now,
                    phases = new
                    {
                        versEkialis = toEkialisResult,
                        versSharePoint = toSharePointResult,
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
            "POST /api/Comparaison/synchronisation-manuelle-complete - Lance une synchronisation complète immédiate",
            "POST /api/Comparaison/synchroniser-vers-sharepoint - Ajoute les logiciels Ekialis manquants dans SharePoint",
            "POST /api/Comparaison/synchroniser-vers-ekialis - Ajoute les logiciels SharePoint manquants dans Ekialis",
            "POST /api/Comparaison/synchroniser-caracteristiques - Met à jour les caractéristiques",
            "POST /api/Comparaison/marquer-obsoletes-rouge - Marque les logiciels obsolètes en rouge",
            "GET /api/Comparaison/logiciels - Compare les deux listes"
        }
            };

            return Ok(status);
        }
    }
}


