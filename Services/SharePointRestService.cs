using System.Net;
using System.Net.Http.Headers;
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
    }
}
