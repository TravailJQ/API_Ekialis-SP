using API_Ekialis_Excel.Services;
using Microsoft.AspNetCore.Mvc;

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
    }
}


