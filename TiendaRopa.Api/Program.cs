using TiendaRopa.Api;

var builder = WebApplication.CreateBuilder(args);

// Toda la configuración de la API vive en ApiIntegration, para poder
// hospedarla sola (este proyecto) o fusionada con el frontend (TiendaRopa.Web).
builder.Services.AddVelvetRoseApi(builder.Configuration, builder.Environment);

var app = builder.Build();

await app.UseVelvetRoseApiAsync();

app.Run();
