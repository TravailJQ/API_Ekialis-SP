using API_Ekialis_Excel.Services;
using Microsoft.AspNetCore.Mvc;

namespace API_Ekialis_Excel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SharePointController : ControllerBase
    {
        private readonly SharePointRestService _sharePointService;
        private readonly IConfiguration _configuration;

        public SharePointController(SharePointRestService sharePointService, IConfiguration configuration)
        {
            _sharePointService = sharePointService;
            _configuration = configuration;
        }

        /// <summary>
        /// Export JSON brut uniquement avec les champs demandés
        /// </summary>
        [HttpGet("fields")]
        public async Task<IActionResult> GetSelectedFieldsOnly()
        {
            try
            {
                var result = await _sharePointService.GetSelectedFieldsAsync();

                var champsDemandes = new[] { "Title", "field_1", "field_2", "field_3", "field_6", "field_9" };

                var lignes = result
                    .Where(item => item.ContainsKey("Title"))
                    .Select(item =>
                    {
                        var valeurs = champsDemandes.Select(champ =>
                        {
                            if (champ == "Title")
                                return item.ContainsKey("Title") ? item["Title"]?.ToString()?.Trim() ?? "" : "";
                            else
                                return item.ContainsKey(champ) ? item[champ]?.ToString()?.Trim() ?? "" : "";
                        });

                        return string.Join(", ", valeurs);
                    })
                    .OrderBy(ligne => ligne) // Tri alphabétique par application
                    .ToList();

                var contenuFinal = string.Join(Environment.NewLine, lignes);

                return Content(contenuFinal, "text/plain; charset=utf-8");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Erreur: {ex.Message}");
            }
        }
    }
}
