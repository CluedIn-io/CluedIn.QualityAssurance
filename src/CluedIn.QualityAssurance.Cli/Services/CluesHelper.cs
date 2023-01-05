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

        clue.Attributes["organization"].Value = organizationId.ToString();

        foreach (var node in xmlDoc.SelectNodes("//codes/value").OfType<XmlNode>())
        {
            node.InnerText += idSuffix;
        }

        foreach (var node in xmlDoc.SelectNodes("//edge").OfType<XmlNode>())
        {
            node.Attributes["from"].Value += idSuffix;
            node.Attributes["to"].Value += idSuffix;
        }

        foreach (var node in xmlDoc.SelectNodes("//*[@origin]").OfType<XmlNode>())
        {
            node.Attributes["origin"].Value += idSuffix;
        }

        xml = xmlDoc.OuterXml;
        return xml;
    }

    public static string GetTestRunSuffix(Guid organizationId)
    {
        return "-testrun-" + organizationId;
    }
}
