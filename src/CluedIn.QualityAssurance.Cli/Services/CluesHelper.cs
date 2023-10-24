using System.Xml;

namespace CluedIn.QualityAssurance.Cli.Services;

internal class CluesHelper
{
    public static string AppendTestRunSuffixToClueXml(string xml, Guid organizationId)
    {
        var idSuffix = GetTestRunSuffix(organizationId);
        var xmlDoc = new XmlDocument();
        xmlDoc.LoadXml(xml);

        var clue = xmlDoc.SelectSingleNode("/clue");


        var organizationAttribute = clue?.Attributes?["organization"];
        if (organizationAttribute == null)
        {
            throw new InvalidOperationException("Organization attribute is null.");
        }

        organizationAttribute.Value = organizationId.ToString();

        var valueNodes = xmlDoc.SelectNodes("//codes/value")?.OfType<XmlNode>() ?? Array.Empty<XmlNode>();
        foreach (var node in valueNodes)
        {
            node.InnerText += idSuffix;
        }

        var edgeNodes = xmlDoc.SelectNodes("//edge")?.OfType<XmlNode>() ?? Array.Empty<XmlNode>();
        foreach (var node in edgeNodes)
        {
            var fromAttribute = node?.Attributes?["from"] ?? throw new InvalidOperationException("From Attribute is null.");
            var toAttribute = node?.Attributes?["to"] ?? throw new InvalidOperationException("To Attribute is null.");
            fromAttribute.Value += idSuffix;
            toAttribute.Value += idSuffix;
        }

        var originNodes = xmlDoc.SelectNodes("//*[@origin]")?.OfType<XmlNode>() ?? Array.Empty<XmlNode>();
        foreach (var node in originNodes)
        {
            var originAttribute = node?.Attributes?["origin"] ?? throw new InvalidOperationException("Origin Attribute is null.");
            originAttribute.Value += idSuffix;
        }

        xml = xmlDoc.OuterXml;
        return xml;
    }

    public static string GetTestRunSuffix(Guid organizationId)
    {
        return "-testrun-" + organizationId;
    }
}
