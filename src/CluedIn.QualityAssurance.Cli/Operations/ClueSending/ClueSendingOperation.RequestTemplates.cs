namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract partial class ClueSendingOperation<TOptions> where TOptions : IClueSendingOperationOptions
{
    private static class RequestTemplates
    {
        public static string SubmitSampleClueAsync(
            Guid organizationId,
            Guid currentClueUuid,
            int currentClueCount)
        {
            return $$"""
            {
              "clue": {
                "attribute-organization": "{{organizationId}}",
                "attribute-origin": "/Dummy#CluedIn:{{currentClueUuid}}",
                "attribute-appVersion": "1.8.0.0",
                "clueDetails": {
                  "data": {
                    "attribute-origin": "/Dummy#CluedIn:{{currentClueUuid}}",
                    "attribute-appVersion": "1.8.0.0",
                    "attribute-inputSource": "cluedin",
                    "entityData": {
                      "attribute-origin": "/Dummy#CluedIn:{{currentClueUuid}}",
                      "attribute-appVersion": "1.8.0.0",
                      "attribute-source": "rest",
                      "entityType": "/Dummy",
                      "properties": {
                        "property-organization.address": "address test {{currentClueCount}}",
                        "property-organization.industry": "industry test {{currentClueCount}}"
                      },
                      "name": "Test1-1 iteration-{{currentClueCount}}"
                    }
                  }
                }
              }
            }
            
            """;
        }
    }
}
