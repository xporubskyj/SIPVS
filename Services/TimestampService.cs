using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace SipvsApp.Services;

/// <summary>
/// Rozšíri podpis XAdES-BES v ASiC-E kontajneri na XAdES-T pridaním časovej pečiatky
/// (xades:SignatureTimeStamp) nad elementom ds:SignatureValue.
/// </summary>
public class TimestampService(HttpClient httpClient, string tsaUrl)
{
    private const string DsNs = "http://www.w3.org/2000/09/xmldsig#";
    private const string XadesNs = "http://uri.etsi.org/01903/v1.3.2#";
    private const string C14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    private const string SignaturesEntry = "META-INF/signatures.xml";

    public string TsaUrl => tsaUrl;

    public async Task<TimestampResult> ExtendToXadesTAsync(string sourceAsicePath, string targetAsicePath)
    {
        if (!File.Exists(sourceAsicePath))
            throw new InvalidOperationException("Podpísaný kontajner (XAdES-BES) neexistuje. Najprv dokument podpíš.");

        var entries = ReadZip(sourceAsicePath);
        if (!entries.TryGetValue(SignaturesEntry, out var signaturesBytes))
            throw new InvalidOperationException($"Kontajner neobsahuje {SignaturesEntry}.");

        var doc = new XmlDocument { PreserveWhitespace = true };
        // XmlReader.Create (na rozdiel od doc.Load(Stream)) normalizuje CRLF -> LF podľa XML špecifikácie,
        // inak by sa CR v base64 hodnotách kanonikalizoval ako "&#xD;" a odtlačok by nesedel.
        using (var stream = new MemoryStream(signaturesBytes))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
            doc.Load(reader);

        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ds", DsNs);
        ns.AddNamespace("xades", XadesNs);

        var signatureValue = doc.SelectSingleNode("//ds:Signature/ds:SignatureValue", ns) as XmlElement
            ?? throw new InvalidOperationException("Podpis neobsahuje ds:SignatureValue.");
        var qualifyingProperties = doc.SelectSingleNode("//ds:Signature/ds:Object/xades:QualifyingProperties", ns) as XmlElement
            ?? throw new InvalidOperationException("Podpis neobsahuje xades:QualifyingProperties.");
        if (qualifyingProperties.SelectSingleNode("xades:UnsignedProperties/xades:UnsignedSignatureProperties/xades:SignatureTimeStamp", ns) != null)
            throw new InvalidOperationException("Podpis už obsahuje časovú pečiatku (XAdES-T).");

        // 1. Kanonikalizovaný ds:SignatureValue -> SHA-256 odtlačok
        var digest = SHA256.HashData(Canonicalize(signatureValue));

        // 2. RFC 3161 žiadosť na TSA
        var token = await RequestTimestampAsync(digest);
        var tokenBase64 = Convert.ToBase64String(token.AsSignedCms().Encode());

        // 3. Vloženie xades:UnsignedProperties/UnsignedSignatureProperties/SignatureTimeStamp
        var signatureId = signatureValue.ParentNode is XmlElement sig ? sig.GetAttribute("Id") : "signature";
        var unsignedProperties = qualifyingProperties.SelectSingleNode("xades:UnsignedProperties", ns) as XmlElement
            ?? (XmlElement)qualifyingProperties.AppendChild(doc.CreateElement("xades", "UnsignedProperties", XadesNs))!;
        var unsignedSignatureProperties = unsignedProperties.SelectSingleNode("xades:UnsignedSignatureProperties", ns) as XmlElement
            ?? (XmlElement)unsignedProperties.AppendChild(doc.CreateElement("xades", "UnsignedSignatureProperties", XadesNs))!;

        var signatureTimeStamp = doc.CreateElement("xades", "SignatureTimeStamp", XadesNs);
        signatureTimeStamp.SetAttribute("Id", $"{signatureId}SignatureTimeStamp");
        var c14nMethod = doc.CreateElement("ds", "CanonicalizationMethod", DsNs);
        c14nMethod.SetAttribute("Algorithm", C14N);
        var encapsulated = doc.CreateElement("xades", "EncapsulatedTimeStamp", XadesNs);
        encapsulated.InnerText = tokenBase64;
        signatureTimeStamp.AppendChild(c14nMethod);
        signatureTimeStamp.AppendChild(encapsulated);
        unsignedSignatureProperties.AppendChild(signatureTimeStamp);

        entries[SignaturesEntry] = Serialize(doc);
        WriteAsice(targetAsicePath, entries);

        return new TimestampResult(
            token.TokenInfo.Timestamp,
            token.AsSignedCms().SignerInfos[0].Certificate?.Subject ?? "neznámy",
            Convert.ToHexString(token.TokenInfo.GetSerialNumber().ToArray()));
    }

    private static byte[] Canonicalize(XmlElement element)
    {
        // Node-set: element + všetci potomkovia + atribúty (menné priestory predkov doplní C14N)
        var nodes = element.SelectNodes("descendant-or-self::node() | descendant-or-self::*/@*")!;
        var transform = new XmlDsigC14NTransform(false);
        transform.LoadInput(nodes);
        using var output = (Stream)transform.GetOutput(typeof(Stream));
        using var memory = new MemoryStream();
        output.CopyTo(memory);
        return memory.ToArray();
    }

    private async Task<Rfc3161TimestampToken> RequestTimestampAsync(byte[] digest)
    {
        var request = Rfc3161TimestampRequest.CreateFromHash(
            digest, HashAlgorithmName.SHA256,
            nonce: RandomNumberGenerator.GetBytes(8),
            requestSignerCertificates: true);

        using var content = new ByteArrayContent(request.Encode());
        content.Headers.ContentType = new("application/timestamp-query");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsync(tsaUrl, content);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"TSA ({tsaUrl}) nie je dostupná: {e.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"TSA ({tsaUrl}) vrátila HTTP {(int)response.StatusCode}.");
            var body = await response.Content.ReadAsByteArrayAsync();
            try
            {
                // Overí status, messageImprint, nonce a podpis TSA nad tokenom
                return request.ProcessResponse(body, out _);
            }
            catch (CryptographicException e)
            {
                throw new InvalidOperationException($"Neplatná odpoveď TSA: {e.Message}");
            }
        }
    }

    private static byte[] Serialize(XmlDocument doc)
    {
        using var memory = new MemoryStream();
        using (var writer = XmlWriter.Create(memory, new XmlWriterSettings
               {
                   Encoding = new UTF8Encoding(false),
                   // D.Signer zapisuje base64 so zalomením CRLF – zachováme pôvodný tvar súboru
                   NewLineHandling = NewLineHandling.Replace,
                   NewLineChars = "\r\n"
               }))
            doc.Save(writer);
        return memory.ToArray();
    }

    private static Dictionary<string, byte[]> ReadZip(string path)
    {
        var result = new Dictionary<string, byte[]>();
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            result[entry.FullName] = memory.ToArray();
        }
        return result;
    }

    private static void WriteAsice(string path, Dictionary<string, byte[]> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        // ASiC: "mimetype" musí byť prvý a nekomprimovaný
        foreach (var (name, data) in entries.OrderBy(e => e.Key == "mimetype" ? 0 : 1))
        {
            var entry = zip.CreateEntry(name, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(data);
        }
    }
}

public record TimestampResult(DateTimeOffset Time, string TsaSubject, string SerialNumber);
