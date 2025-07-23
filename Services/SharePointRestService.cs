using System.Net;
using System.Net.Http.Headers;
using API_Ekialis_Excel.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace API_Ekialis_Excel.Services
{
    public class SharePointRestService
    {
        private readonly IConfiguration _configuration;

        public SharePointRestService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private HttpClient CreateAuthenticatedHttpClient()
        {
            var siteUri = new Uri(_configuration["SharePoint:SiteUrl"]);
            var fedAuth = _configuration["SharePoint:FedAuth"];
            var rtFa = _configuration["SharePoint:RtFa"];

            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };

            handler.CookieContainer.Add(siteUri, new Cookie("FedAuth", fedAuth));
            handler.CookieContainer.Add(siteUri, new Cookie("rtFa", rtFa));

            var client = new HttpClient(handler);

            client.DefaultRequestHeaders.Accept.Clear();

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept", "application/json;odata=verbose");

            return client;
        }



        public async Task<List<Dictionary<string, object>>> GetListItemsAsync()
        {
            var siteUrl = _configuration["SharePoint:SiteUrl"];
            var listName = _configuration["SharePoint:ListName"];
            var apiUrl = $"{siteUrl}/_api/web/lists/getbytitle('{listName}')/items";

            var client = CreateAuthenticatedHttpClient();
            var response = await client.GetAsync(apiUrl);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Erreur SharePoint: {response.StatusCode}");
                return new List<Dictionary<string, object>>();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            var jsonObject = JObject.Parse(jsonContent);
            var items = new List<Dictionary<string, object>>();

            if (jsonObject["d"]?["results"] != null)
            {
                foreach (var item in jsonObject["d"]["results"])
                {
                    var itemDict = new Dictionary<string, object>();
                    foreach (var prop in item.Children<JProperty>())
                    {
                        if (!prop.Name.StartsWith("__") && prop.Name != "odata.type")
                        {
                            itemDict[prop.Name] = prop.Value?.ToString() ?? "";
                        }
                    }
                    items.Add(itemDict);
                }
            }

            return items;
        }

        public async Task<string> GetListItemsRawJsonAsync()
        {
            var siteUrl = _configuration["SharePoint:SiteUrl"];
            var listName = _configuration["SharePoint:ListName"];
            var apiUrl = $"{siteUrl}/_api/web/lists/getbytitle('{listName}')/items";

            var client = CreateAuthenticatedHttpClient();
            var response = await client.GetAsync(apiUrl);

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<List<Dictionary<string, object>>> GetSelectedFieldsAsync()
        {
            var siteUrl = _configuration["SharePoint:SiteUrl"];
            var listName = _configuration["SharePoint:ListName"];
            var fieldList = _configuration["SharePoint:Fields"]; // Champs séparés par virgule

            if (string.IsNullOrWhiteSpace(fieldList))
                throw new Exception("Aucun champ spécifié dans appsettings.json (SharePoint:Fields)");

            var selectedFields = fieldList.Split(',').Select(f => f.Trim()).ToList();
            var selectQuery = string.Join(",", selectedFields);

            var apiUrl = $"{siteUrl}/_api/web/lists/getbytitle('{listName}')/items?$select={selectQuery}&$top=5000";

            var client = CreateAuthenticatedHttpClient();
            var response = await client.GetAsync(apiUrl);

            var results = new List<Dictionary<string, object>>();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var json = JObject.Parse(content);

                if (json["d"]?["results"] != null)
                {
                    foreach (var item in json["d"]["results"])
                    {
                        var dict = new Dictionary<string, object>();

                        foreach (var field in selectedFields)
                        {
                            dict[field] = item[field]?.ToString() ?? "";
                        }

                        results.Add(dict);
                    }
                }
            }

            return results;
        }

        public async Task<bool> AddItemToSharePointAsync(string nomLogiciel, Dictionary<string, string> caracteristiques)
        {
            try
            {
                var siteUrl = _configuration["SharePoint:SiteUrl"];
                var listName = _configuration["SharePoint:ListName"];
                var apiUrl = $"{siteUrl}/_api/web/lists/getbytitle('{listName}')/items";

                var client = CreateAuthenticatedHttpClient();

                // Récupérer le bon type de la liste
                var listItemType = await GetListItemTypeAsync();
                if (string.IsNullOrEmpty(listItemType))
                {
                    Console.WriteLine("❌ Impossible de récupérer le type de la liste");
                    return false;
                }

                // Créer un objet dynamique pour ajouter les champs
                var expandoObject = new System.Dynamic.ExpandoObject() as IDictionary<string, object>;
                expandoObject["__metadata"] = new { type = listItemType };
                expandoObject["Title"] = nomLogiciel;

                // Ajout des caractéristiques mappées
                foreach (var caract in caracteristiques)
                {
                    var sharePointField = FieldMapping.GetSharePointField(caract.Key);
                    if (!string.IsNullOrEmpty(sharePointField))
                    {
                        expandoObject[sharePointField] = caract.Value;
                        Console.WriteLine($"  Mapping: {caract.Key} -> {sharePointField} = {caract.Value}");
                    }
                }

                // Sérialisation en JSON
                var jsonContent = JsonConvert.SerializeObject(expandoObject);
                Console.WriteLine($"JSON envoyé: {jsonContent}");

                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                // Headers requis pour SharePoint REST API
                var requestDigest = await GetRequestDigestAsync();
                if (string.IsNullOrEmpty(requestDigest))
                {
                    Console.WriteLine("❌ Impossible d'obtenir le RequestDigest");
                    return false;
                }

                client.DefaultRequestHeaders.Remove("X-RequestDigest");
                client.DefaultRequestHeaders.Add("X-RequestDigest", requestDigest);

                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json")
                {
                    Parameters = { new System.Net.Http.Headers.NameValueHeaderValue("odata", "verbose") }
                };

                var response = await client.PostAsync(apiUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✅ Logiciel '{nomLogiciel}' ajouté à SharePoint avec succès");
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Erreur lors de l'ajout de '{nomLogiciel}': {response.StatusCode}");
                    Console.WriteLine($"Détail erreur: {errorContent}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception lors de l'ajout de '{nomLogiciel}': {ex.Message}");
                return false;
            }
        }

        private async Task<string> GetListItemTypeAsync()
        {
            try
            {
                var siteUrl = _configuration["SharePoint:SiteUrl"];
                var listName = _configuration["SharePoint:ListName"];
                var apiUrl = $"{siteUrl}/_api/web/lists/getbytitle('{listName}')?$select=ListItemEntityTypeFullName";

                var client = CreateAuthenticatedHttpClient();
                var response = await client.GetAsync(apiUrl);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var jsonObject = JObject.Parse(jsonResponse);
                    var itemType = jsonObject["d"]?["ListItemEntityTypeFullName"]?.ToString() ?? "";

                    Console.WriteLine($"Type de liste récupéré: {itemType}");
                    return itemType;
                }
                else
                {
                    Console.WriteLine($"❌ Erreur récupération type liste: {response.StatusCode}");
                    return "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception récupération type liste: {ex.Message}");
                return "";
            }
        }

        private async Task<string> GetRequestDigestAsync()
        {
            try
            {
                var siteUrl = _configuration["SharePoint:SiteUrl"];
                var digestUrl = $"{siteUrl}/_api/contextinfo";

                var client = CreateAuthenticatedHttpClient();

                // Headers pour contextinfo
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json;odata=verbose");

                var content = new StringContent("", System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(digestUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"RequestDigest response: {jsonResponse.Substring(0, Math.Min(200, jsonResponse.Length))}...");

                    var jsonObject = JObject.Parse(jsonResponse);
                    var digest = jsonObject["d"]?["GetContextWebInformation"]?["FormDigestValue"]?.ToString() ?? "";

                    Console.WriteLine($"RequestDigest obtenu: {digest.Substring(0, Math.Min(50, digest.Length))}...");
                    return digest;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"❌ Erreur RequestDigest: {response.StatusCode} - {errorContent}");
                }

                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception RequestDigest: {ex.Message}");
                return "";
            }
        }
    }
}
