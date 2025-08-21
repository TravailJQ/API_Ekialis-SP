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

        /// <summary>
        /// Lance une synchronisation complète : SharePoint → Ekialis
        /// </summary>
        /// <remarks>
        /// Effectue une synchronisation complète en 3 phases :
        /// - Phase 1 : Ajoute les logiciels de SharePoint manquants dans Ekialis
        /// - Phase 2 : Met à jour les caractéristiques Ekialis selon SharePoint  
        /// - Phase 3 : Marque en rouge les logiciels obsolètes dans Ekialis
        /// 
        /// SharePoint est considéré comme la source de vérité.
        /// </remarks>
        /// <response code="200">Synchronisation terminée avec succès</response>
        /// <response code="401">Échec de l'authentification Ekialis</response>
        [HttpPost("sharepoint-vers-ekialis-complet")]
        public async Task<IActionResult> SynchronisationSharePointVersEkialisComplete()
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

        /// <summary>
        /// Teste l'authentification Basic Auth
        /// </summary>
        /// <remarks>
        /// Endpoint simple pour vérifier que l'authentification Basic Auth fonctionne correctement.
        /// Utilisez vos identifiants configurés dans BasicAuthMiddleware.
        /// </remarks>
        /// <response code="200">Authentification réussie</response>
        /// <response code="401">Identifiants invalides</response>
        [HttpGet("test-authentification")]
        public IActionResult TestAuthentification()
        {
            return Ok(new
            {
                message = "Authentification réussie",
                timestamp = DateTime.Now,
                user = "Accès autorisé"
            });
        }

        /// <summary>
        /// Affiche le statut de la synchronisation automatique
        /// </summary>
        /// <remarks>
        /// Retourne les informations sur la synchronisation automatique :
        /// - Statut (actif/inactif)
        /// - Fréquence d'exécution
        /// - Prochaine exécution prévue
        /// - Liste des endpoints disponibles
        /// </remarks>
        /// <response code="200">Statut récupéré avec succès</response>
        [HttpGet("statut-synchronisation-automatique")]
        public IActionResult GetStatutSynchronisationAutomatique()
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
                    "POST /api/Synchronization/sharepoint-vers-ekialis-complet - Synchronisation complète SharePoint → Ekialis",
                    "POST /api/Operations/ajouter-sharepoint-vers-ekialis - Ajoute uniquement les logiciels manquants dans Ekialis",
                    "POST /api/Operations/ajouter-ekialis-vers-sharepoint - Ajoute uniquement les logiciels manquants dans SharePoint",
                    "POST /api/Operations/mettre-a-jour-caracteristiques - Met à jour les caractéristiques selon SharePoint",
                    "POST /api/Operations/marquer-obsoletes-rouge - Marque les logiciels obsolètes en rouge",
                    "POST /api/Operations/importer-excel-vers-sharepoint - Importe un fichier Excel vers SharePoint"
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

        // Méthode supprimée - SharePoint est la source de vérité
        // La synchronisation Ekialis → SharePoint n'est disponible qu'en manuel via OperationsController

        private async Task<object> SynchroniserCaracteristiques(EkialisService ekialisService)
        {
            try
            {
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
                            Console.WriteLine($"    ➕ Création de nouvelle valeur de caractéristique");
                            var success = await ekialisService.AddCharacteristicToComponentAsync(componentId, nomCaracteristique, valeurSharePoint);
                            if (success)
                                caracteristiquesAjoutees++;
                            else
                                erreurs++;
                        }
                    }
                }

                return new
                {
                    logicielsCommuns = logicielsCommuns.Count,
                    caracteristiquesModifiees,
                    caracteristiquesAjoutees,
                    erreurs
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de la synchronisation des caractéristiques: {ex.Message}");
                return new
                {
                    logicielsCommuns = 0,
                    caracteristiquesModifiees = 0,
                    caracteristiquesAjoutees = 0,
                    erreurs = 1
                };
            }
        }

        private async Task<object> MarquerObsoletesRouge(EkialisService ekialisService)
        {
            try
            {
                Console.WriteLine("🎨 Début de la synchronisation des couleurs - TOUS EN VERT sauf obsolètes...");

                // 1. Récupération des logiciels SharePoint (noms Title uniquement)
                var itemsSharePoint = await _sharePointService.GetSelectedFieldsAsync();
                var nomsSharePoint = new HashSet<string>();

                foreach (var item in itemsSharePoint)
                {
                    if (item.ContainsKey("Title"))
                    {
                        var title = item["Title"]?.ToString()?.Trim() ?? "";
                        if (!string.IsNullOrEmpty(title))
                        {
                            nomsSharePoint.Add(title.ToLower());
                        }
                    }
                }

                Console.WriteLine($"📋 Noms SharePoint récupérés: {nomsSharePoint.Count}");

                // 2. Récupération des logiciels Ekialis
                var rawJson = await ekialisService.GetComponentsRawJsonAsync();
                var jArray = JArray.Parse(rawJson);

                var logicielsEkialis = new List<(int id, string name, string currentColor)>();

                foreach (var item in jArray)
                {
                    var componentClassId = item["componentClass"]?["id"]?.ToString() ?? "";
                    if (componentClassId != "1") continue; // Uniquement les logiciels

                    var id = item["id"]?.ToObject<int>() ?? 0;
                    var name = item["name"]?.ToString()?.Trim() ?? "";
                    var color = item["color"]?.ToString() ?? "";

                    if (id > 0 && !string.IsNullOrEmpty(name))
                    {
                        logicielsEkialis.Add((id, name, color));
                    }
                }

                Console.WriteLine($"📋 Logiciels Ekialis récupérés: {logicielsEkialis.Count}");

                // 3. LOGIQUE SIMPLE: 
                // - Si logiciel Ekialis ABSENT de SharePoint → ROUGE (obsolète)
                // - Sinon → VERT (valide)

                var aMarquerRouge = new List<(int id, string name, string currentColor)>();
                var aMarquerVert = new List<(int id, string name, string currentColor)>();

                foreach (var logicielEkialis in logicielsEkialis)
                {
                    var nomEkialisNormalise = logicielEkialis.name.ToLower();
                    var couleurActuelle = NormaliserCouleur(logicielEkialis.currentColor);

                    if (nomsSharePoint.Contains(nomEkialisNormalise))
                    {
                        // PRÉSENT dans SharePoint → VERT
                        if (couleurActuelle != "22B14C")
                        {
                            aMarquerVert.Add(logicielEkialis);
                        }
                    }
                    else
                    {
                        // ABSENT de SharePoint → ROUGE (obsolète)
                        if (couleurActuelle != "FF0000")
                        {
                            aMarquerRouge.Add(logicielEkialis);
                        }
                    }
                }

                Console.WriteLine($"\n📊 RÉSUMÉ:");
                Console.WriteLine($"   - Logiciels à marquer VERT: {aMarquerVert.Count}");
                Console.WriteLine($"   - Logiciels à marquer ROUGE (obsolètes): {aMarquerRouge.Count}");

                // TEST: Vérifier quelques logiciels pour debug
                Console.WriteLine($"\n🔍 TEST sur les 10 premiers logiciels Ekialis:");
                foreach (var test in logicielsEkialis.Take(10))
                {
                    var testNorm = test.name.ToLower();
                    var present = nomsSharePoint.Contains(testNorm);
                    Console.WriteLine($"  - '{test.name}' → {(present ? "✅ PRÉSENT" : "❌ ABSENT")} SharePoint");
                }

                // Si TOUS sont marqués rouge, il y a un problème de comparaison
                if (aMarquerRouge.Count == logicielsEkialis.Count && aMarquerVert.Count == 0)
                {
                    Console.WriteLine($"⚠️ ATTENTION: TOUS les logiciels seraient marqués rouge!");
                    Console.WriteLine($"⚠️ Cela indique un problème de comparaison des noms.");
                    Console.WriteLine($"⚠️ Premiers noms SharePoint: {string.Join(", ", nomsSharePoint.Take(5))}");
                    Console.WriteLine($"⚠️ Premiers noms Ekialis: {string.Join(", ", logicielsEkialis.Take(5).Select(l => l.name.ToLower()))}");

                    // SÉCURITÉ: Ne pas exécuter si tous seraient marqués rouge
                    return new
                    {
                        erreur = "Tous les logiciels seraient marqués rouge - comparaison échouée",
                        totalEkialis = logicielsEkialis.Count,
                        totalSharePoint = nomsSharePoint.Count,
                        premierNomsSharePoint = nomsSharePoint.Take(10).ToList(),
                        premierNomsEkialis = logicielsEkialis.Take(10).Select(l => l.name.ToLower()).ToList()
                    };
                }

                // 4. Application des couleurs VERT
                var marquagesVertReussis = 0;
                var marquagesVertEchecs = 0;

                foreach (var logiciel in aMarquerVert)
                {
                    Console.WriteLine($"🟢 Marquage VERT: '{logiciel.name}' (ID: {logiciel.id})");
                    var success = await ekialisService.UpdateComponentColorAsync(logiciel.id, "22B14C");

                    if (success)
                        marquagesVertReussis++;
                    else
                        marquagesVertEchecs++;
                }

                // 5. Application des couleurs ROUGE (seulement les vrais obsolètes)
                var marquagesRougeReussis = 0;
                var marquagesRougeEchecs = 0;

                foreach (var logiciel in aMarquerRouge)
                {
                    Console.WriteLine($"🔴 Marquage ROUGE: '{logiciel.name}' (ID: {logiciel.id}) - OBSOLÈTE");
                    var success = await ekialisService.UpdateComponentColorAsync(logiciel.id, "FF0000");

                    if (success)
                        marquagesRougeReussis++;
                    else
                        marquagesRougeEchecs++;
                }

                return new
                {
                    totalEkialis = logicielsEkialis.Count,
                    totalSharePoint = nomsSharePoint.Count,
                    marquagesVert = new
                    {
                        total = aMarquerVert.Count,
                        reussis = marquagesVertReussis,
                        echecs = marquagesVertEchecs
                    },
                    marquagesRouge = new
                    {
                        total = aMarquerRouge.Count,
                        reussis = marquagesRougeReussis,
                        echecs = marquagesRougeEchecs
                    },
                    logicielsVerts = aMarquerVert.Select(l => l.name).Take(10).ToList(),
                    logicielsRouges = aMarquerRouge.Select(l => l.name).ToList()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors du marquage: {ex.Message}");
                return new
                {
                    erreur = ex.Message
                };
            }
        }

        // Méthode utilitaire pour normaliser les couleurs
        private string NormaliserCouleur(string couleur)
        {
            if (string.IsNullOrEmpty(couleur)) return "";
            return couleur.Replace("#", "").ToUpperInvariant();
        }

        // Méthode utilitaire pour normaliser les couleurs
        private string NormaliserCouleur(string couleur)
        {
            if (string.IsNullOrEmpty(couleur)) return "";

            // Supprimer le # si présent et convertir en majuscules
            return couleur.Replace("#", "").ToUpperInvariant();
        }

        // Méthodes utilitaires à ajouter dans vos controllers
        private string NormaliserNomPourComparaison(string nom)
        {
            if (string.IsNullOrEmpty(nom)) return "";

            return nom.Trim()
                      .ToLowerInvariant()
                      .Replace("  ", " ") // Double espaces -> simple
                      .Replace("_", " ")  // Underscores -> espaces
                      .Replace("-", " ")  // Tirets -> espaces
                      .Normalize(); // Normalisation Unicode
        }

        private double CalculerSimilitude(string nom1, string nom2)
        {
            if (string.IsNullOrEmpty(nom1) || string.IsNullOrEmpty(nom2)) return 0;

            nom1 = NormaliserNomPourComparaison(nom1);
            nom2 = NormaliserNomPourComparaison(nom2);

            if (nom1 == nom2) return 1.0;

            // Calcul simple de similarité basé sur la distance de Levenshtein
            var longueurMax = Math.Max(nom1.Length, nom2.Length);
            if (longueurMax == 0) return 1.0;

            var distance = CalculerDistanceLevenshtein(nom1, nom2);
            return 1.0 - (double)distance / longueurMax;
        }

        private int CalculerDistanceLevenshtein(string s1, string s2)
        {
            var matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(Math.Min(
                        matrix[i - 1, j] + 1,
                        matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[s1.Length, s2.Length];
        }
    }
}