using Newtonsoft.Json;

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
              "query": {{JsonConvert.SerializeObject(requestString)}}
            }
            """;
        }
    }
}
