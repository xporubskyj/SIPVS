using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml.Xsl;

namespace SipvsApp.Services;

public class XmlService(string rootPath)
{
    private readonly string _xsdPath = Path.Combine(rootPath, "forms", "purchase-order.xsd");
    private readonly string _xslPath = Path.Combine(rootPath, "forms", "purchase-order.xsl");

    public string XmlPath { get; } = Path.Combine(rootPath, "output", "purchase-order.xml");
    public string HtmlPath { get; } = Path.Combine(rootPath, "output", "purchase-order.html");
    public string AsicePath { get; } = Path.Combine(rootPath, "output", "purchase-order.asice");
    public string AsiceTPath { get; } = Path.Combine(rootPath, "output", "purchase-order-xades-t.asice");

    public void Save(string xml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(XmlPath)!);
        XDocument.Parse(xml).Save(XmlPath);
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.Schemas.Add(null, _xsdPath);
        settings.ValidationEventHandler += (_, e) =>
            errors.Add($"Riadok {e.Exception.LineNumber}, stĺpec {e.Exception.LinePosition}: {e.Message}");

        try
        {
            using var reader = XmlReader.Create(XmlPath, settings);
            while (reader.Read()) { }
        }
        catch (XmlException e)
        {
            errors.Add($"Riadok {e.LineNumber}, stĺpec {e.LinePosition}: {e.Message}");
        }

        return errors;
    }

    public void Transform()
    {
        var transform = new XslCompiledTransform();
        transform.Load(_xslPath);
        transform.Transform(XmlPath, HtmlPath);
    }

    public string ReadXsd() => File.ReadAllText(_xsdPath);

    public string ReadXsl() => File.ReadAllText(_xslPath);

    public void SaveAsice(string base64)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(AsicePath)!);
        File.WriteAllBytes(AsicePath, Convert.FromBase64String(base64.Trim()));
    }
}