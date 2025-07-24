using API_Ekialis_Excel.Models;
using API_Ekialis_Excel.Services;
using Newtonsoft.Json.Linq;

namespace API_Ekialis_Excel.Services
{
    public class SynchronizationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SynchronizationBackgroundService> _logger;
        private readonly IConfiguration _configuration;
        private Timer? _timer;

        public SynchronizationBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<SynchronizationBackgroundService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Service de synchronisation automatique démarré");

            // Configuration du timer pour s'exécuter toutes les heures
            _timer = new Timer(ExecuteSynchronization, null, TimeSpan.Zero, TimeSpan.FromHours(1));

            // Maintenir le service en vie
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async void ExecuteSynchronization(object? state)
        {
            try
            {
                _logger.LogInformation("🔄 Début de la synchronisation automatique - {Time}", DateTime.Now);
                await PerformSynchronizationAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la synchronisation automatique");
            }
        }

        private async Task PerformSynchronizationAsync()
        {
            using var scope = _serviceProvider.CreateScope();

            var sharePointService = scope.ServiceProvider.GetRequiredService<SharePointRestService>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, configuration);

                // 1. Authentification Ekialis
                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                {
                    _logger.LogError("❌ Échec de l'authentification Ekialis");
                    return;
                }

                _logger.LogInformation("✅ Authentification Ekialis réussie");

                // 2. ÉTAPE 1: Synchroniser SharePoint → Ekialis (nouveaux logiciels SharePoint)
                _logger.LogInformation("📥 ÉTAPE 1: Ajout des logiciels SharePoint manquants dans Ekialis");
                var ajoutsEkialis = await SynchroniserVersEkialis(ekialisService, sharePointService);
                _logger.LogInformation($"✅ Étape 1 terminée: {ajoutsEkialis.reussis} ajouts réussis, {ajoutsEkialis.echecs} échecs");

                // 3. ÉTAPE 2: Mise à jour des caractéristiques (SharePoint écrase Ekialis)
                _logger.LogInformation("🔄 ÉTAPE 2: Mise à jour des caractéristiques Ekialis selon SharePoint");
                var miseAJour = await SynchroniserCaracteristiques(ekialisService, sharePointService);
                _logger.LogInformation($"✅ Étape 2 terminée: {miseAJour.modifiees} modifications, {miseAJour.ajoutees} ajouts, {miseAJour.erreurs} erreurs");

                // 4. ÉTAPE 3: Marquage des logiciels obsolètes en rouge
                _logger.LogInformation("🔴 ÉTAPE 3: Marquage des logiciels obsolètes en rouge");
                var marquage = await MarquerObsoletesRouge(ekialisService, sharePointService);
                _logger.LogInformation($"✅ Étape 3 terminée: {marquage.reussis} marquages réussis, {marquage.echecs} échecs");

                // 5. Rapport final
                _logger.LogInformation("📊 SYNCHRONISATION COMPLÈTE TERMINÉE (SharePoint → Ekialis):");
                _logger.LogInformation($"   - Ajouts Ekialis: {ajoutsEkialis.reussis} réussis, {ajoutsEkialis.echecs} échecs");
                _logger.LogInformation($"   - Caractéristiques: {miseAJour.modifiees} modifiées, {miseAJour.ajoutees} ajoutées");
                _logger.LogInformation($"   - Marquages rouge: {marquage.reussis} réussis");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur lors de la synchronisation");
            }
        }

        private async Task<(int reussis, int echecs)> SynchroniserVersEkialis(EkialisService ekialisService, SharePointRestService sharePointService)
        {
            try
            {
                // Récupération des logiciels Ekialis (noms uniquement)
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

                // Récupération des logiciels SharePoint
                var itemsSharePoint = await sharePointService.GetSelectedFieldsAsync();
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

                // Identification des logiciels manquants dans Ekialis
                var logicielsManquants = logicielsSharePoint
                    .Where(logiciel => !nomsEkialis.Contains(logiciel["Title"].ToString()?.ToLower() ?? ""))
                    .ToList();

                // Ajout des logiciels manquants
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

                return (ajoutsReussis, ajoutsEchecs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation vers Ekialis");
                return (0, 1);
            }
        }

        private async Task<(int modifiees, int ajoutees, int erreurs)> SynchroniserCaracteristiques(EkialisService ekialisService, SharePointRestService sharePointService)
        {
            try
            {
                // Récupération des logiciels Ekialis avec leurs caractéristiques
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

                // Récupération des logiciels SharePoint
                var itemsSharePoint = await sharePointService.GetSelectedFieldsAsync();
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

                // Synchronisation des logiciels communs
                var logicielsCommuns = logicielsEkialis.Keys.Intersect(logicielsSharePoint.Keys).ToList();

                var caracteristiquesModifiees = 0;
                var caracteristiquesAjoutees = 0;
                var erreurs = 0;

                foreach (var nomLogiciel in logicielsCommuns)
                {
                    var (componentId, caracteristiquesEkialis) = logicielsEkialis[nomLogiciel];
                    var champsSharePoint = logicielsSharePoint[nomLogiciel];

                    foreach (var champSharePoint in champsSharePoint)
                    {
                        var nomCaracteristique = champSharePoint.Key;
                        var valeurSharePoint = champSharePoint.Value;

                        if (caracteristiquesEkialis.ContainsKey(nomCaracteristique))
                        {
                            var (valueId, valeurEkialis, characteristicId) = caracteristiquesEkialis[nomCaracteristique];

                            if (valeurEkialis != valeurSharePoint)
                            {
                                var success = await ekialisService.UpdateExistingCharacteristicValueAsync(valueId, valeurSharePoint, componentId, characteristicId);
                                if (success)
                                    caracteristiquesModifiees++;
                                else
                                    erreurs++;
                            }
                        }
                        else
                        {
                            var success = await ekialisService.AddCharacteristicToComponentAsync(componentId, nomCaracteristique, valeurSharePoint);
                            if (success)
                                caracteristiquesAjoutees++;
                            else
                                erreurs++;
                        }
                    }
                }

                return (caracteristiquesModifiees, caracteristiquesAjoutees, erreurs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation des caractéristiques");
                return (0, 0, 1);
            }
        }

        private async Task<(int reussis, int echecs)> MarquerObsoletesRouge(EkialisService ekialisService, SharePointRestService sharePointService)
        {
            try
            {
                // Récupération des logiciels SharePoint
                var itemsSharePoint = await sharePointService.GetSelectedFieldsAsync();
                var nomsSharePoint = itemsSharePoint
                    .Where(i => i.ContainsKey("Title"))
                    .Select(i => i["Title"]?.ToString()?.Trim().ToLower())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToHashSet();

                // Récupération des logiciels Ekialis
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

                // Marquage en rouge
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

                return (marquagesReussis, marquagesEchecs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors du marquage des logiciels obsolètes");
                return (0, 1);
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🛑 Arrêt du service de synchronisation automatique");
            _timer?.Change(Timeout.Infinite, 0);
            await base.StopAsync(stoppingToken);
        }

        public override void Dispose()
        {
            _timer?.Dispose();
            base.Dispose();
        }
    }
}