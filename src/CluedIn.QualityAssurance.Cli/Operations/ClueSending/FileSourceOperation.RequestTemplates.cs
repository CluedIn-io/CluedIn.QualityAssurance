namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract partial class FileSourceOperation<TOptions>
{
    private static class Requests
    {
        public static string AddEntityCodeViaAnnotationCode(
        int annotationId,
        string vocabularyKeyFullName,
        string entityCodeOrigin,
        string vocabularyKeyAnnotationKey,
        bool useAsSourceCode)
        {
            return $$"""
        {
          "operationName": "createAnnotationCode",
          "variables": {
            "annotationCode": {
              "vocabKey": "{{vocabularyKeyFullName}}",
              "entityCodeOrigin": "{{entityCodeOrigin}}",
              "key": "{{vocabularyKeyAnnotationKey}}",
              "type": "String",
              "annotationId": "{{annotationId}}",
              "sourceCode": {{useAsSourceCode.ToString().ToLowerInvariant()}}
            }
          },
          "query": "mutation createAnnotationCode($annotationCode: InputAnnotationCode) {\n  preparation {\n    id\n    createAnnotationCode(annotationCode: $annotationCode) {\n      id\n      __typename\n    }\n    __typename\n  }\n}\n"
        }
        """;
        }

        public static string AddEntityCode(
            int annotationId,
            string vocabularyKeyFullName,
            string entityCodeOrigin,
            bool useAsEntityCode,
            bool useAsSourceCode)
        {
            return $$"""
            {
              "operationName": "modifyBatchVocabularyClueMappingConfiguration",
              "variables": {
                "annotationId": "{{annotationId}}",
                "batchPropertyMappings": {
                  "propertyMappingSettings": [
                    {
                      "vocabKey": "{{vocabularyKeyFullName}}",
                      "entityCodeOrigin": "{{entityCodeOrigin}}",
                      "useAsEntityCode": {{useAsEntityCode.ToString().ToLowerInvariant()}},
                      "useSourceCode": {{useAsSourceCode.ToString().ToLowerInvariant()}}
                    }
                  ]
                }
              },
              "query": "mutation modifyBatchVocabularyClueMappingConfiguration($annotationId: ID!, $batchPropertyMappings: InputBatchPropertyMapping) {\n  management {\n    modifyBatchVocabularyClueMappingConfiguration(\n      annotationId: $annotationId\n      batchPropertyMappings: $batchPropertyMappings\n    )\n    __typename\n  }\n}\n"
            }
            """;
        }

        public static string GetAnnotationById(
            int annotationId)
        {
            return $$"""
            {
              "operationName": "getAnnotationById",
              "variables": {
                "id": "{{annotationId}}"
              },
              "query": "query getAnnotationById($id: ID) {\n  preparation {\n    id\n    annotation(id: $id) {\n      id\n      annotationCodeSetup\n      isDynamicVocab\n      name\n      entityType\n      previewImageKey\n      nameKey\n      descriptionKey\n      originEntityCodeKey\n      createdDateMap\n      modifiedDateMap\n      cultureKey\n      origin\n      versionKey\n      beforeCreatingClue\n      beforeSendingClue\n      useStrictEdgeCode\n      useDefaultSourceCode\n      vocabularyId\n      vocabulary {\n        vocabularyName\n        vocabularyId\n        providerId\n        keyPrefix\n        __typename\n      }\n      entityTypeConfiguration {\n        icon\n        displayName\n        entityType\n        __typename\n      }\n      annotationProperties {\n        displayName\n        key\n        vocabKey\n        coreVocab\n        useAsEntityCode\n        useAsAlias\n        useSourceCode\n        entityCodeOrigin\n        vocabularyKeyId\n        type\n        annotationEdges {\n          id\n          key\n          edgeType\n          entityTypeConfiguration {\n            icon\n            displayName\n            entityType\n            __typename\n          }\n          origin\n          dataSourceGroupId\n          dataSourceId\n          dataSetId\n          direction\n          edgeProperties {\n            id\n            annotationEdgeId\n            originalField\n            vocabularyKey {\n              displayName\n              vocabularyKeyId\n              isCluedInCore\n              isDynamic\n              isObsolete\n              isProvider\n              vocabularyId\n              name\n              isVisible\n              key\n              mappedKey\n              groupName\n              dataClassificationCode\n              dataType\n              description\n              providerId\n              mapsToOtherKeyId\n              __typename\n            }\n            __typename\n          }\n          __typename\n        }\n        vocabularyKey {\n          displayName\n          vocabularyKeyId\n          isCluedInCore\n          isDynamic\n          isObsolete\n          isProvider\n          vocabularyId\n          name\n          isVisible\n          key\n          mappedKey\n          groupName\n          dataClassificationCode\n          dataType\n          description\n          providerId\n          mapsToOtherKeyId\n          __typename\n        }\n        validations {\n          id\n          displayName\n          inverse\n          parameters {\n            key\n            value\n            __typename\n          }\n          __typename\n        }\n        transformations {\n          filters {\n            parameters {\n              key\n              value\n              __typename\n            }\n            id\n            displayName\n            inverse\n            __typename\n          }\n          operations {\n            inverse\n            parameters {\n              key\n              value\n              __typename\n            }\n            id\n            displayName\n            __typename\n          }\n          __typename\n        }\n        __typename\n      }\n      __typename\n    }\n    __typename\n  }\n}\n"
            }
            """;
        }
    }
    
}

