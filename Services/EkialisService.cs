using API_Ekialis_Excel.Models;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace API_Ekialis_Excel.Services
{
    public class EkialisService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private string _jwtToken = string.Empty;

        public EkialisService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;

            var baseUrl = _configuration["EkialisApi:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                _httpClient.BaseAddress = new Uri(baseUrl);
            }
        }

        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                var authRequest = new AuthenticationRequest
                {
                    auth_key = _configuration["EkialisApi:AuthKey"] ?? string.Empty
                };

                var json = JsonConvert.SerializeObject(authRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/auth", content);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    var tokenResponse = JsonConvert.DeserializeObject<dynamic>(result);

                    if (tokenResponse?.token != null)
                    {
                        _jwtToken = tokenResponse.token;
                        _httpClient.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", _jwtToken);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur d'authentification: {ex.Message}");
                return false;
            }
        }

        // VERSION SIMPLIFIÉE : Récupère les données en JSON brut
        public async Task<string> GetComponentsRawJsonAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/explore/components");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"JSON reçu, taille: {json.Length} caractères");
                    return json;
                }

                return "[]";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la récupération: {ex.Message}");
                return "[]";
            }
        }

        // VERSION SIMPLIFIÉE : Parse le JSON avec JArray
        public async Task<List<dynamic>> GetComponentsDynamicAsync()
        {
            try
            {
                var json = await GetComponentsRawJsonAsync();

                if (!string.IsNullOrEmpty(json) && json != "[]")
                {
                    var jArray = JArray.Parse(json);
                    var components = jArray.ToObject<List<dynamic>>();

                    Console.WriteLine($"Composants parsés: {components?.Count ?? 0}");

                    return components ?? new List<dynamic>();
                }

                return new List<dynamic>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur de parsing: {ex.Message}");
                return new List<dynamic>();
            }
        }

        // VERSION SIMPLIFIÉE : Filtre les logiciels en mode dynamique
        public async Task<List<ComponentFlat>> GetSoftwareComponentsFlatAsync()
        {
            try
            {
                var json = await GetComponentsRawJsonAsync();

                if (string.IsNullOrEmpty(json) || json == "[]")
                {
                    Console.WriteLine("❌ Aucune donnée reçue");
                    return new List<ComponentFlat>();
                }

                var jArray = JArray.Parse(json);
                var result = new List<ComponentFlat>();

                Console.WriteLine($"🔍 Nombre total d'éléments à traiter: {jArray.Count}");

                int logicielsFiltrés = 0;
                int autresClasses = 0;

                foreach (var item in jArray)
                {
                    try
                    {
                        // Extraction de componentClass.id de manière sécurisée
                        var componentClassId = 0;
                        var componentClassName = "";

                        if (item["componentClass"] != null)
                        {
                            componentClassId = GetSafeInt(item["componentClass"], "id");
                            componentClassName = GetSafeString(item["componentClass"], "name");
                        }

                        // DIAGNOSTIC : Compter toutes les classes
                        if (componentClassId == 1)
                        {
                            logicielsFiltrés++;

                            var id = GetSafeInt(item, "id");
                            var name = GetSafeString(item, "name");
                            var icon = GetSafeString(item, "icon");
                            var color = GetSafeString(item, "color");

                            var componentFlat = new ComponentFlat
                            {
                                Id = id,
                                Name = name,
                                Icon = icon,
                                Color = color,
                                ComponentClassId = componentClassId,
                                ComponentClassName = componentClassName,
                                ComponentStatusId = 0,
                                ComponentStatusName = "",
                                Company = 0,
                                CharacteristicsCount = GetArrayLength(item, "characteristics"),
                                SourceRelationsCount = GetArrayLength(item, "sourceRelations")
                            };

                            result.Add(componentFlat);
                        }
                        else
                        {
                            autresClasses++;
                        }
                    }
                    catch (Exception itemEx)
                    {
                        Console.WriteLine($"❌ Erreur sur un élément: {itemEx.Message}");
                    }
                }

                Console.WriteLine($"📊 RÉSULTATS DU FILTRAGE:");
                Console.WriteLine($"   - Total éléments traités: {jArray.Count}");
                Console.WriteLine($"   - Logiciels trouvés (classe 1): {logicielsFiltrés}");
                Console.WriteLine($"   - Autres classes: {autresClasses}");
                Console.WriteLine($"   - Objets ComponentFlat créés: {result.Count}");

                if (result.Any())
                {
                    Console.WriteLine($"   - Premiers logiciels: {string.Join(", ", result.Take(5).Select(r => r.Name))}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur générale: {ex.Message}");
                return new List<ComponentFlat>();
            }
        }

        // Méthodes utilitaires pour extraction sécurisée
        private int GetSafeInt(JToken token, string property)
        {
            try
            {
                return token[property]?.Value<int>() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private string GetSafeString(JToken token, string property)
        {
            try
            {
                return token[property]?.Value<string>() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private int GetArrayLength(JToken token, string property)
        {
            try
            {
                var array = token[property] as JArray;
                return array?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        // Méthode de fallback pour les autres endpoints
        public async Task<List<CharacteristicValue>> GetCharacteristicValuesAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/explore/characteristic_values");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<List<CharacteristicValue>>(json);
                    return result ?? new List<CharacteristicValue>();
                }

                return new List<CharacteristicValue>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la récupération des valeurs: {ex.Message}");
                return new List<CharacteristicValue>();
            }
        }

        public async Task<List<Characteristic>> GetCharacteristicsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/explore/characteristics");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonConvert.DeserializeObject<List<Characteristic>>(json);
                    return result ?? new List<Characteristic>();
                }

                return new List<Characteristic>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de la récupération des caractéristiques: {ex.Message}");
                return new List<Characteristic>();
            }
        }

        // Méthode simplifiée pour conversion - OBSOLÈTE, utiliser GetSoftwareComponentsFlatAsync
        public List<ComponentFlat> ConvertToFlat(List<Component> components)
        {
            // Cette méthode n'est plus utilisée avec l'approche dynamique
            return new List<ComponentFlat>();
        }
    }
}