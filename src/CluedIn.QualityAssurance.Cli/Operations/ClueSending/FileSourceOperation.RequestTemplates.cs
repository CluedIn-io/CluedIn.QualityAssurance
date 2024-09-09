using CluedIn.Core.Data.Vocabularies;

using Newtonsoft.Json;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal abstract partial class FileSourceOperation<TOptions>
{
    private static class RequestTemplates
    {
        public static string AddEntityCodeViaAnnotationCode(
            int annotationId,
            string vocabularyKeyFullName,
            string entityCodeOrigin,
            string vocabularyKeyAnnotationKey,
            bool useAsSourceCode)
        {
            #region Request
            var requestString = $$"""
            mutation createAnnotationCode($annotationCode: InputAnnotationCode) {              preparation {                id                createAnnotationCode(annotationCode: $annotationCode) {                  id                  __typename                }                __typename              }            }
            """;
            #endregion
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
              "query": {{JsonConvert.SerializeObject(requestString)}}
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
            #region Request
            var requestString = $$"""
            mutation modifyBatchVocabularyClueMappingConfiguration($annotationId: ID!, $batchPropertyMappings: InputBatchPropertyMapping) {              management {                modifyBatchVocabularyClueMappingConfiguration(                  annotationId: $annotationId                  batchPropertyMappings: $batchPropertyMappings                )                __typename              }            }
            """;
            #endregion

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
              "query": {{JsonConvert.SerializeObject(requestString)}}
            }
            """;
        }

        public static string GetAnnotationById(
            int annotationId)
        {
            #region Request
            var requestString = $$"""
            query getAnnotationById($id: ID) {
              preparation {
                id
                annotation(id: $id) {
                  id
                  annotationCodeSetup
                  isDynamicVocab
                  name
                  entityType
                  previewImageKey
                  nameKey
                  descriptionKey
                  originEntityCodeKey
                  createdDateMap
                  modifiedDateMap
                  cultureKey
                  origin
                  versionKey
                  beforeCreatingClue
                  beforeSendingClue
                  useStrictEdgeCode
                  useDefaultSourceCode
                  vocabularyId
                  vocabulary {
                    vocabularyName
                    vocabularyId
                    providerId
                    keyPrefix
                    __typename
                  }
                  entityTypeConfiguration {
                    icon
                    displayName
                    entityType
                    __typename
                  }
                  annotationProperties {
                    key
                    vocabKey
                    coreVocab
                    useAsEntityCode
                    useAsAlias
                    useSourceCode
                    entityCodeOrigin
                    vocabularyKeyId
                    type
                    annotationEdges {
                      id
                      key
                      edgeType
                      entityTypeConfiguration {
                        icon
                        displayName
                        entityType
                        __typename
                      }
                      origin
                      dataSourceGroupId
                      dataSourceId
                      dataSetId
                      direction
                      edgeProperties {
                        id
                        annotationEdgeId
                        originalField
                        vocabularyKey {
                          displayName
                          vocabularyKeyId
                          isCluedInCore
                          isDynamic
                          isObsolete
                          isProvider
                          vocabularyId
                          name
                          isVisible
                          key
                          mappedKey
                          groupName
                          dataClassificationCode
                          dataType
                          description
                          providerId
                          mapsToOtherKeyId
                          __typename
                        }
                        __typename
                      }
                      __typename
                    }
                    vocabularyKey {
                      displayName
                      vocabularyKeyId
                      isCluedInCore
                      isDynamic
                      isObsolete
                      isProvider
                      vocabularyId
                      name
                      isVisible
                      key
                      mappedKey
                      groupName
                      dataClassificationCode
                      dataType
                      description
                      providerId
                      mapsToOtherKeyId
                      __typename
                    }
                    validations {
                      id
                      displayName
                      inverse
                      parameters {
                        key
                        value
                        __typename
                      }
                      __typename
                    }
                    transformations {
                      filters {
                        parameters {
                          key
                          value
                          __typename
                        }
                        id
                        displayName
                        inverse
                        __typename
                      }
                      operations {
                        inverse
                        parameters {
                          key
                          value
                          __typename
                        }
                        id
                        displayName
                        __typename
                      }
                      __typename
                    }
                    __typename
                  }
                  __typename
                }
                __typename
              }
            }
            """;
            #endregion
            return $$"""
            {
              "operationName": "getAnnotationById",
              "variables": {
                "id": "{{annotationId}}"
              },
              "query": {{JsonConvert.SerializeObject(requestString)}}
            }
            """;
        }

        public static string AddEntityEdgeAsync(
            int annotationId,
            string entityType,
            string edgeType,
            string origin,
            string edgeDirection,
            string vocabularyKeyFullName)
        {
            #region Request
            var requestString = $$"""
            mutation addEdgeMapping($annotationId: ID!, $key: String!, $edgeConfiguration: InputEdgeConfiguration) {              management {                addEdgeMapping(                  annotationId: $annotationId                  key: $key                  edgeConfiguration: $edgeConfiguration                )                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "addEdgeMapping",
              "variables": {
                "annotationId": "{{annotationId}}",
                "edgeConfiguration": {
                  "edgeProperties": [],
                  "entityTypeConfiguration": {
                    "new": false,
                    "icon": "Twitter",
                    "entityType": "/{{entityType}}",
                    "displayName": "{{entityType}}"
                  },
                  "edgeType": "/{{edgeType}}",
                  "origin": "{{origin}}",
                  "direction": "{{edgeDirection}}"
                },
                "key": "{{vocabularyKeyFullName}}"
              },
              "query": {{JsonConvert.SerializeObject(requestString)}}
            }
            """;
        }

        public static string AddPropertyMappingAsync(
            Guid dataSetId,
            string originalField,
            bool useAsAlias,
            bool useAsEntityCode,
            Guid vocabularyId,
            Guid vocabularyKeyId)
        {
            #region Request
            var requestString = $$"""
            mutation addPropertyMappingToCluedMappingConfiguration($dataSetId: ID!, $propertyMappingConfiguration: InputPropertyMappingConfiguration, $extra: Boolean) {              management {                addPropertyMappingToCluedMappingConfiguration(                  dataSetId: $dataSetId                  propertyMappingConfiguration: $propertyMappingConfiguration                  extra: $extra                )                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "addPropertyMappingToCluedMappingConfiguration",
              "variables": {
                "dataSetId": "{{dataSetId}}",
                "propertyMappingConfiguration": {
                  "originalField": "{{originalField}}",
                  "useAsAlias": {{useAsAlias.ToString().ToLowerInvariant()}},
                  "useAsEntityCode": {{useAsEntityCode.ToString().ToLowerInvariant()}},
                  "vocabularyKeyConfiguration": {
                    "vocabularyId": "{{vocabularyId}}",
                    "new": false,
                    "vocabularyKeyId": "{{vocabularyKeyId}}"
                  }
                }
              },
              "query": {{JsonConvert.SerializeObject(requestString)}}
            }
            """;
        }

        public static string CreateAutoAnnotationAsync(
            Guid dataSetId,
            string entityType,
            string vocabularyName,
            Guid vocabularyId)
        {
            #region Request
            var requestString = $$"""
            mutation createAutoAnnotation($dataSetId: ID!, $type: String!, $mappingConfiguration: InputMappingConfiguration, $isDynamicVocab: Boolean) {              management {                createAutoAnnotation(                  dataSetId: $dataSetId                  type: $type                  mappingConfiguration: $mappingConfiguration                  isDynamicVocab: $isDynamicVocab                ) {                  id                  __typename                }                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "createAutoAnnotation",
              "variables": {
                "dataSetId": "{{dataSetId}}",
                "type": "file",
                "mappingConfiguration": {
                  "entityTypeConfiguration": {
                    "icon": "Twitter",
                    "new": false,
                    "displayName": "{{entityType}}",
                    "entityType": "/{{entityType}}"
                  },
                  "ignoredFields": [],
                  "vocabularyConfiguration": {
                    "new": false,
                    "keyPrefix": "{{vocabularyName}}",
                    "vocabularyName": "{{vocabularyName}}",
                    "vocabularyId": "{{vocabularyId}}"
                  }
                },
                "isDynamicVocab": true
              },
              "query": {{JsonConvert.SerializeObject(requestString)}}
            }
            """;
        }

        public static string CreateDataSourceSetAsync(
            string dataSourceSetName,
            Guid userId)
        {
            #region Request
            var requestString = $$"""
            mutation createDataSourceSet($dataSourceSet: InputDataSourceSet) {              inbound {                createDataSourceSet(dataSourceSet: $dataSourceSet)                __typename              }            }            
            """;
            #endregion

            return $$"""
            {
              "operationName": "createDataSourceSet",
              "variables": {
                "dataSourceSet": {
                  "name": "{{dataSourceSetName}}",
                  "author": "{{userId}}"
                }
              },
              "query": {{JsonConvert.SerializeObject(requestString)}}
            }
            """;
        }
    }    
}

