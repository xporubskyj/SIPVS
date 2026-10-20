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

app.MapGet("/api/forms/xsd", (XmlService s) =>
    Results.Text(s.ReadXsd(), "application/xml; charset=utf-8"));

app.MapGet("/api/forms/xsl", (XmlService s) =>
    Results.Text(s.ReadXsl(), "application/xml; charset=utf-8"));

app.MapPost("/api/save-asice", async (HttpRequest request, XmlService s) =>
{
    using var reader = new StreamReader(request.Body);
    s.SaveAsice(await reader.ReadToEndAsync());
    return Results.Ok(new { message = $"ASiC-E bolo uložené: {s.AsicePath}" });
});

app.MapGet("/api/output/asice", (XmlService s) =>
    Results.File(s.AsicePath, "application/vnd.etsi.asic-e+zip", "purchase-order.asice"));

app.Run();
