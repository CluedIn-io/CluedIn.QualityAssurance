using Newtonsoft.Json;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending.IngestionEndpoint;

internal partial class IngestionEndpointOperation
{
    private static class RequestTemplates
    {
        public static string CreateDataSetAsync(
            int dataSourceId,
            Guid userId,
            string entityType)
        {
            #region Request
            var requestString = $$"""
            mutation createDataSets($dataSourceId: ID, $dataSets: [InputDataSet]) {
              inbound {
                createDataSets(dataSourceId: $dataSourceId, dataSets: $dataSets) {
                  id
                  __typename
                }
                __typename
              }
            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "createDataSets",
              "variables": {
                "dataSourceId": "{{dataSourceId}}",
                "dataSets": [
                  {
                    "author": "{{userId}}",
                    "store": true,
                    "name": "MyIngestEndpointName",
                    "type": "endpoint",
                    "configuration": {
                      "object": {
                        "endPointName": "MyIngestEndpointName",
                        "autoSubmit": false,
                        "entityType": "/{{entityType}}"
                      },
                      "entityTypeConfiguration": {
                        "icon": "MdHourglassTop",
                        "new": true,
                        "displayName": "{{entityType}}",
                        "entityType": "/{{entityType}}"
                      }
                    }
                  }
                ]
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string CreateDataSourceAsync(
            int dataSourceSetId,
            Guid userId)
        {
            #region Request
            var requestString = $$"""
            mutation createDataSource($dataSourceSetId: ID, $dataSource: InputDataSource) {
              inbound {
                createDataSource(dataSourceSetId: $dataSourceSetId, dataSource: $dataSource) {
                  id
                  __typename
                }
                __typename
              }
            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "createDataSource",
              "variables": {
                "dataSourceSetId": "{{dataSourceSetId}}",
                "dataSource": {
                  "author": "{{userId}}",
                  "type": "endpoint",
                  "name": "MyIngest"
                }
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string ModifyDataSetAutoSubmitAsync(Guid dataSetId)
        {
            #region Request
            var requestString = $$"""
            mutation modifyDataSetAutoSubmit($dataSetId: ID!, $autoSubmit: Boolean) {
              inbound {
                modifyDataSetAutoSubmit(dataSetId: $dataSetId, autoSubmit: $autoSubmit)
                __typename
              }
            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "modifyDataSetAutoSubmit",
              "variables": {
                "dataSetId": "{{dataSetId}}",
                "autoSubmit": true
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }
    }
}
