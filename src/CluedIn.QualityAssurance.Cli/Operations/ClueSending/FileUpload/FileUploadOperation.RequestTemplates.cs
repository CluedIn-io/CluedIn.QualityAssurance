using CluedIn.Core.FileTypes;

using Newtonsoft.Json;

using Serilog.Sinks.File;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.FileUpload;

internal partial class FileUploadOperation
{
    private static class RequestTemplates
    {
        public static string CommitDataSetAsync(Guid dataSetId)
        {
            #region Request
            var requestString = $$"""
            mutation commitDataSet($dataSetId: ID) {
              inbound {
                commitDataSet(dataSetId: $dataSetId)
                __typename
              }
            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "commitDataSet",
              "variables": {
                "dataSetId": "{{dataSetId}}"
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string ResumeUploadRequestAsync(
            string fileName,
            long fileSize,
            string mimeType,
            string dataSourceName,
            int dataSourceGroupId)
        {
            return $$"""
            {
              "fileName": "{{fileName}}",
              "fileSize": {{fileSize}},
              "mimeType": "{{mimeType}}",
              "noHeaders": false,
              "dataSourceName": "{{dataSourceName}}",
              "dataSourceGroupId": "{{dataSourceGroupId}}",
              "dataSourceGroupName": ""
            }
            """;
        }
    }
}
