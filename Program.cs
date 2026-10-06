using SipvsApp.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(new XmlService(builder.Environment.ContentRootPath));

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/api/save", async (HttpRequest request, XmlService xmlService) =>
{
    using var reader = new StreamReader(request.Body);
    xmlService.Save(await reader.ReadToEndAsync());
    return Results.Ok(new { message = $"XML bolo uložené: {xmlService.XmlPath}" });
});

app.MapPost("/api/validate", (XmlService xmlService) =>
{
    var errors = xmlService.Validate();
    return Results.Ok(new { isValid = errors.Count == 0, errors });
});

app.MapPost("/api/transform", (XmlService xmlService) =>
{
    xmlService.Transform();
    return Results.Ok(new { message = $"HTML bolo uložené: {xmlService.HtmlPath}" });
});

app.MapGet("/api/output/html", (XmlService xmlService) =>
    Results.File(xmlService.HtmlPath, "text/html; charset=utf-8"));

app.Run();
