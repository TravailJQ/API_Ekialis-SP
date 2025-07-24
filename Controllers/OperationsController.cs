using API_Ekialis_Excel.Models;
using API_Ekialis_Excel.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models;
using Newtonsoft.Json.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using System.IO;

namespace API_Ekialis_Excel.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OperationsController : ControllerBase
    {
        private readonly SharePointRestService _sharePointService;
        private readonly IConfiguration _configuration;

        public OperationsController(SharePointRestService sharePointService, IConfiguration configuration)
        {
            _sharePointService = sharePointService;
            _configuration = configuration;
        }

        [HttpPost("sharepoint-vers-ekialis")]
        public async Task<IActionResult> SharePointVersEkialis()
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

        [HttpPost("ekialis-vers-sharepoint")]
        public async Task<IActionResult> EkialisVersSharePoint()
        {
            try
            {
                using var httpClient = new HttpClient();
                var ekialisService = new EkialisService(httpClient, _configuration);

                var authSuccess = await ekialisService.AuthenticateAsync();
                if (!authSuccess)
                    return Unauthorized("Échec de l'authentification Ekialis");

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

                var response = new
                {
                    logicielsCommuns = logicielsCommuns.Count,
                    caracteristiquesModifiees,
                    caracteristiquesAjoutees,
                    erreurs
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

        [HttpPost("import-excel-vers-sharepoint")]
        public async Task<IActionResult> ImportExcelVersSharePoint(IFormFile excelFile)
        {
            try
            {
                if (excelFile == null || excelFile.Length == 0)
                    return BadRequest("Aucun fichier Excel fourni");

                Console.WriteLine($"📁 Traitement du fichier: {excelFile.FileName} ({excelFile.Length} bytes)");

                // Lecture du fichier Excel
                using var stream = new MemoryStream();
                await excelFile.CopyToAsync(stream);
                var fileBytes = stream.ToArray();

                // Analyse avec la méthode de parsing
                var logiciels = await ParseExcelFile(fileBytes);

                if (!logiciels.Any())
                    return BadRequest("Aucune donnée valide trouvée dans le fichier Excel");

                Console.WriteLine($"📋 {logiciels.Count} logiciels trouvés dans le fichier Excel");

                // Ajout vers SharePoint
                var ajoutsReussis = 0;
                var ajoutsEchecs = 0;
                var erreursDetaillees = new List<string>();

                foreach (var logiciel in logiciels)
                {
                    var nomLogiciel = logiciel.ContainsKey("Title") ? logiciel["Title"]?.ToString() ?? "" : "";

                    if (string.IsNullOrEmpty(nomLogiciel))
                    {
                        ajoutsEchecs++;
                        erreursDetaillees.Add("Ligne ignorée: APPLICATION vide");
                        continue;
                    }

                    Console.WriteLine($"📤 Ajout de '{nomLogiciel}' dans SharePoint...");

                    // Conversion en format attendu par SharePoint
                    var champsSharePoint = new Dictionary<string, string>();
                    foreach (var champ in logiciel)
                    {
                        if (champ.Key != "Title" && champ.Value != null && !string.IsNullOrEmpty(champ.Value.ToString()))
                        {
                            champsSharePoint[champ.Key] = champ.Value.ToString();
                            Console.WriteLine($"  📝 Champ mappé: {champ.Key} = '{champ.Value}'");
                        }
                    }

                    Console.WriteLine($"  📊 {champsSharePoint.Count} champs à synchroniser pour '{nomLogiciel}'");

                    var success = await _sharePointService.AddItemToSharePointAsync(nomLogiciel, champsSharePoint);

                    if (success)
                    {
                        ajoutsReussis++;
                        Console.WriteLine($"  ✅ '{nomLogiciel}' ajouté avec succès");
                    }
                    else
                    {
                        ajoutsEchecs++;
                        erreursDetaillees.Add($"Échec de l'ajout: {nomLogiciel}");
                        Console.WriteLine($"  ❌ Échec de l'ajout de '{nomLogiciel}'");
                    }
                }

                var response = new
                {
                    message = "Import Excel vers SharePoint terminé",
                    fichier = excelFile.FileName,
                    totalLignes = logiciels.Count,
                    ajoutsReussis,
                    ajoutsEchecs,
                    erreursDetaillees
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur lors de l'import Excel: {ex.Message}");
                return StatusCode(500, $"Erreur lors de l'import Excel: {ex.Message}");
            }
        }

        private async Task<List<Dictionary<string, object>>> ParseExcelFile(byte[] fileBytes)
        {
            var logiciels = new List<Dictionary<string, object>>();

            try
            {
                // Mapping des colonnes Excel vers les champs SharePoint
                var columnMapping = new Dictionary<string, string>
        {
            { "APPLICATION", "Title" },
            { "APPLICATION ", "Title" }, // Avec espace
            { "FOURNISSEUR", "field_6" },
            { "SERVICE/ENTITE", "field_1" },
            { "ROLE", "field_3" },
            { "PRIX", "field_8" },
            { "Référent NGE", "field_2" },
            { "Contact Commercial - Nom, Prénom", "field_13" },
            { "Contact Commercial - Téléphone", "field_15" },
            { "Contact Commercial - Mail", "field_14" },
            { "LIEN EDITEUR (Présentation Solution)", "field_25" },
            { "Pérénité Solution", "field_27" }
        };

                using (var stream = new MemoryStream(fileBytes))
                using (var document = SpreadsheetDocument.Open(stream, false))
                {
                    var workbookPart = document.WorkbookPart;
                    var worksheetPart = workbookPart.WorksheetParts.First();
                    var worksheet = worksheetPart.Worksheet;
                    var sheetData = worksheet.GetFirstChild<SheetData>();
                    var stringTable = workbookPart.GetPartsOfType<SharedStringTablePart>().FirstOrDefault();

                    var rows = sheetData.Descendants<Row>().ToList();
                    if (!rows.Any()) return logiciels;

                    // Récupérer les en-têtes (première ligne)
                    var headerRow = rows[0];
                    var headers = new Dictionary<int, string>();
                    int colIndex = 0;

                    foreach (Cell cell in headerRow.Descendants<Cell>())
                    {
                        var cellValue = GetCellValue(cell, stringTable);
                        if (!string.IsNullOrEmpty(cellValue))
                        {
                            headers[colIndex] = cellValue.Trim();
                        }
                        colIndex++;
                    }

                    // Traiter les lignes de données (à partir de la ligne 2)
                    for (int i = 1; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var logiciel = new Dictionary<string, object>();
                        bool hasData = false;
                        colIndex = 0;

                        foreach (Cell cell in row.Descendants<Cell>())
                        {
                            var cellValue = GetCellValue(cell, stringTable);

                            if (headers.ContainsKey(colIndex) && columnMapping.ContainsKey(headers[colIndex]))
                            {
                                var sharePointField = columnMapping[headers[colIndex]];
                                logiciel[sharePointField] = cellValue?.Trim() ?? "";

                                if (!string.IsNullOrEmpty(cellValue))
                                    hasData = true;
                            }
                            colIndex++;
                        }

                        if (hasData && logiciel.ContainsKey("Title") && !string.IsNullOrEmpty(logiciel["Title"]?.ToString()))
                        {
                            logiciels.Add(logiciel);
                        }
                    }
                }

                Console.WriteLine($"📊 Parsing terminé: {logiciels.Count} logiciels extraits");
                return logiciels;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erreur de parsing Excel: {ex.Message}");
                throw;
            }
        }

        private string GetCellValue(Cell cell, SharedStringTablePart stringTable)
        {
            if (cell.CellValue == null) return "";

            var value = cell.CellValue.InnerXml;

            if (cell.DataType != null && cell.DataType.Value == CellValues.SharedString)
            {
                if (stringTable != null && int.TryParse(value, out int index))
                {
                    return stringTable.SharedStringTable.ChildElements[index].InnerText;
                }
            }

            return value;
        }
    }
}
