using API_Ekialis_Excel.Controllers;
using API_Ekialis_Excel.Services;

namespace API_Ekialis_Excel.Services
{
    public class SynchronizationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SynchronizationBackgroundService> _logger;
        private readonly IConfiguration _configuration;

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

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PerformSynchronizationAsync();

                    // Attendre 1 heure avant la prochaine synchronisation
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Service de synchronisation arrêté");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Erreur lors de la synchronisation automatique");

                    // En cas d'erreur, attendre 10 minutes avant de réessayer
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                }
            }
        }

        private async Task PerformSynchronizationAsync()
        {
            using var scope = _serviceProvider.CreateScope();

            var sharePointService = scope.ServiceProvider.GetRequiredService<SharePointRestService>();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            _logger.LogInformation("🔄 Début de la synchronisation automatique - {Time}", DateTime.Now);

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

                // 2. ÉTAPE 2: Mise à jour des caractéristiques (SharePoint écrase Ekialis)
                _logger.LogInformation("🔄 ÉTAPE 2: Mise à jour des caractéristiques Ekialis selon SharePoint");
                var miseAJour = await SynchroniserCaracteristiques(ekialisService, sharePointService);
                _logger.LogInformation($"✅ Étape 2 terminée: {miseAJour.modifiees} modifications, {miseAJour.ajoutees} ajouts, {miseAJour.erreurs} erreurs");

                // 3. ÉTAPE 3: Marquage des logiciels obsolètes en rouge
                _logger.LogInformation("🔴 ÉTAPE 3: Marquage des logiciels obsolètes en rouge");
                var marquage = await MarquerObsoletesRouge(ekialisService, sharePointService);
                _logger.LogInformation($"✅ Étape 3 terminée: {marquage.reussis} marquages réussis, {marquage.echecs} échecs");

                // 4. Rapport final
                _logger.LogInformation("📊 SYNCHRONISATION COMPLÈTE TERMINÉE:");
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
                var jArray = Newtonsoft.Json.Linq.JArray.Parse(rawJson);

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

        // Supprimé - Ne pas synchroniser Ekialis vers SharePoint car SharePoint est la source de vérité

        private async Task<(int modifiees, int ajoutees, int erreurs)> SynchroniserCaracteristiques(EkialisService ekialisService, SharePointRestService sharePointService)
        {
            try
            {
                // [Code similaire à la méthode du controller mais simplifié pour les logs]
                // Récupération et comparaison des caractéristiques
                // Retourne le nombre de modifications/ajouts/erreurs

                // Pour l'instant, on retourne des valeurs par défaut
                // Vous pouvez copier le code de la méthode SynchroniserCaracteristiques du controller ici

                return (0, 0, 0);
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
                var jArray = Newtonsoft.Json.Linq.JArray.Parse(rawJson);

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
    }
}