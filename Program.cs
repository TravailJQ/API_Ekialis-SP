using API_Ekialis_Excel.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Enregistrement des services personnalisés
builder.Services.AddHttpClient<EkialisService>();
builder.Services.AddScoped<ExportService>();

// Enregistrement du service SharePoint
builder.Services.AddScoped<SharePointRestService>(); // Utilise HttpClient dans le service, pas via DI directe

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
