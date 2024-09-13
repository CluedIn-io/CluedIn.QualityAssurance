using System.Text.Json;

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
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
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
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
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
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
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
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
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
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
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
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
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
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string CreateManualAnnotationAsync(
            Guid dataSetId,
            string entityType,
            string vocabularyName,
            Guid vocabularyId)
        {
            var keysConfig = JsonSerializer.Serialize(new string[] { }); // TODO: Add Keysconfig
            #region Request
            var requestString = $$"""
            mutation createManualAnnotation($dataSetId: ID!, $type: String!, $mappingConfiguration: InputMappingConfiguration, $isDynamicVocab: Boolean) {              management {                createManualAnnotation(                  dataSetId: $dataSetId                  type: $type                  mappingConfiguration: $mappingConfiguration                  isDynamicVocab: $isDynamicVocab                ) {                  id                  __typename                }                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "createManualAnnotation",
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
                  },
                  "keysConfig": {{keysConfig}}
                },
                "isDynamicVocab": true
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string CreateEntityTypeAsync(
            string entityType,
            string entityTypeRoute)
        {
            #region Request

            var variable = $$"""
            {
              "type": "/{{entityType}}",
              "active": true,
              "displayName": "{{entityType}}",
              "icon": "Twitter",
              "route": "{{entityTypeRoute}}",
              "pageTemplateId": ""
            }
            """;
            var requestString = $$"""
            mutation createEntityTypeConfigurationV2($entityTypeConfiguration: String!) {              management {                createEntityTypeConfigurationV2(                  entityTypeConfiguration: $entityTypeConfiguration                )                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "createEntityTypeConfigurationV2",
              "variables": {
                "entityTypeConfiguration": {{GraphqlQueryHelper.Serialize(variable)}}
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string SetNameKeyAsync(
            int annotationId,
            string nameKey)
        {
            #region Request
            var requestString = $$"""
            mutation modifyAnnotation($annotation: InputEntityAnnotation) {              preparation {                modifyAnnotation(annotation: $annotation)                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "modifyAnnotation",
              "variables": {
                "annotation": {
                  "id": "{{annotationId}}",
                  "nameKey": "{{nameKey}}"
                }
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string SetOriginAsync(
            int annotationId,
            string origin)
        {
            #region Request
            var requestString = $$"""
            mutation saveCustomOriginClueMappingConfiguration($annotationId: ID!, $customOrigin: String) {              management {                saveCustomOriginClueMappingConfiguration(                  annotationId: $annotationId                  customOrigin: $customOrigin                )                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "saveCustomOriginClueMappingConfiguration",
              "variables": {
                "annotationId": "{{annotationId}}",
                "customOrigin": "{{origin}}"
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string SetOriginEntityCodeKeyAsync(
            int annotationId,
            string originEntityCodeKey)
        {
            #region Request
            var requestString = $$"""
            mutation modifyAnnotation($annotation: InputEntityAnnotation) {              preparation {                modifyAnnotation(annotation: $annotation)                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "modifyAnnotation",
              "variables": {
                "annotation": {
                  "id": "{{annotationId}}",
                  "originEntityCodeKey": "{{originEntityCodeKey}}"
                }
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string GetDataSourceByIdAsync(
            int dataSourceId)
        {
            #region Request
            var requestString = $$"""
            query getDataSourceById($id: ID!) {              inbound {                dataSource(id: $id) {                  id                  canBeDeleted                  name                  hasError                  latestErrorMessage                  errorType                  author {                    id                    username                    __typename                  }                  fileMetadata {                    fileName                    processing
                    uploading
                    uploadedPercentage
                    mimeType                    __typename                  }                  createdAt                  type                  dataSourceSet {                    id                    name                    __typename                  }                  sql                  connectionStatus {                    connected                    errorMessage                    __typename                  }                  dataSets {                    id                    name                    annotationId                    elasticTotal                    expectedTotal                    annotation {                      originEntityCodeKey                      annotationProperties {                        key                        __typename                      }                      __typename                    }                    stats {                      total                      successful                      failed                      __typename                    }                    author {                      id                      username                      __typename                    }                    createdAt                    updatedAt                    dataSource {                      id                      __typename                    }                    __typename                  }                  connectorConfigurationId                  connectorConfiguration {                    id                    name                    accountDisplay                    accountId                    active                    autoSync                    codeName                    configuration                    connector {                      id                      icon                      name                      authMethods                      properties                      streamModes                      __typename                    }                    createdDate                    entityId                    failingAuthentication                    guide                    helperConfiguration                    providerId                    reAuthEndpoint                    source                    sourceQuality                    stats                    status                    supportsAutomaticWebhookCreation                    supportsConfiguration                    supportsWebhooks                    userId                    userName                    users {                      id                      username                      roles                      __typename                    }                    webhookManagementEndpoints                    webhooks                    __typename                  }                  __typename                }                __typename              }            }            
            """;
            #endregion

            return $$"""
            {
              "operationName": "getDataSourceById",
              "variables": {
                "id": "{{dataSourceId}}"
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string GetAllVocabulariesAsync(
            string vocabularyName)
        {
            #region Request
            var requestString = $$"""
            query getAllVocabularies($searchName: String, $isActive: Boolean, $pageNumber: Int, $pageSize: Int, $sortBy: String, $sortDirection: String, $entityType: String, $connectorId: ID, $filterTypes: Int, $filterHasNoSource: Boolean) {              management {                id                vocabularies(                  searchName: $searchName                  isActive: $isActive                  pageNumber: $pageNumber                  pageSize: $pageSize                  sortBy: $sortBy                  sortDirection: $sortDirection                  entityType: $entityType                  connectorId: $connectorId                  filterTypes: $filterTypes                  filterHasNoSource: $filterHasNoSource                ) {                  total                  data {                    vocabularyId                    vocabularyName                    keyPrefix                    isCluedInCore                    isDynamic                    isProvider                    isActive                    grouping                    createdAt                    connector {                      id                      name                      about                      icon                      __typename                    }                    __typename                  }                  __typename                }                __typename              }            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "getAllVocabularies",
              "variables": {
                "searchName": "{{vocabularyName}}",
                "pageNumber": 1,
                "pageSize": 100,
                "entityType": null,
                "connectorId": null,
                "isActive": null,
                "filterTypes": null,
                "filterHasNoSource": null
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string CreateVocabularyAsync(
            string vocabularyName,
            string entityType)
        {
            #region Request
            var requestString = $$"""
            mutation createVocabulary($vocabulary: InputVocabulary) {              management {                id                createVocabulary(vocabulary: $vocabulary) {                  ...Vocabulary                  __typename                }                __typename              }            }            fragment Vocabulary on Vocabulary {              vocabularyId              vocabularyName              keyPrefix              isCluedInCore              entityTypeConfiguration {                icon                entityType                displayName                __typename              }              isDynamic              isProvider              isActive              grouping              createdAt              providerId              description              connector {                id                name                about                icon                __typename              }              __typename            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "createVocabulary",
              "variables": {
                "vocabulary": {
                  "vocabularyName": "{{vocabularyName}}",
                  "entityTypeConfiguration": {
                    "icon": "Twitter",
                    "new": false,
                    "displayName": "{{entityType}}",
                    "entityType": "/{{entityType}}"
                  },
                  "providerId": "",
                  "keyPrefix": "{{vocabularyName}}",
                  "description": ""
                }
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string CreateVocabularyKeyAsync(
            Guid vocabularyId,
            string vocabularyKeyName,
            string vocabularyKeyGroup,
            string vocabularyKeyType)
        {
            #region Request
            var requestString = $$"""
            mutation createVocabulary($vocabularyKey: InputVocabularyKey) {              management {                id                createVocabularyKey(vocabularyKey: $vocabularyKey) {                  ...VocabularyKey                  __typename                }                __typename              }            }            fragment VocabularyKey on VocabularyKey {              displayName              vocabularyKeyId              vocabularyId              name              isVisible              isCluedInCore              isDynamic              isProvider              isObsolete              groupName              key              storage              dataClassificationCode              dataType              description              dataAnnotationsIsPrimaryKey              dataAnnotationsIsEditable              dataAnnotationsIsNullable              dataAnnotationsIsRequired              dataAnnotationsMinimumLength              dataAnnotationsMaximumLength              providerId              compositeVocabularyId              compositeVocabulary {                name                displayName                dataType                __typename              }              mapsToOtherKeyId              glossaryTermId              createdAt              createdBy              mappedKey              isValueChangeInsignificant              connector {                id                name                about                icon                type                __typename              }              vocabulary {                vocabularyId                vocabularyName                connector {                  id                  name                  about                  icon                  __typename                }                __typename              }              author {                id                username                __typename              }              __typename            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "createVocabulary",
              "variables": {
                "vocabularyKey": {
                  "vocabularyId": "{{vocabularyId}}",
                  "displayName": "{{vocabularyKeyName}}",
                  "name": "{{vocabularyKeyName}}",
                  "groupName": "{{vocabularyKeyGroup}}",
                  "isVisible": true,
                  "dataType": "{{vocabularyKeyType}}",
                  "description": ""
                }
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string GetEntityTypeInfoAsync(
            string entityType,
            bool withPageTemplate)
        {
            #region Request
            var requestStringWithoutPageTemplate = $$"""
            query getEntityTypeInfo($type: String!) {              management {                getEntityTypeInfo(type: $type) {                  id                  icon                  displayName                  type                  route                  path                  active                  layoutConfiguration                  pageTemplateId                  __typename                }                __typename              }            }
            """;
            var requestStringWithPageTemplate = $$"""
            query getEntityTypeInfo($type: String!) {              management {                getEntityTypeInfo(type: $type, includePageTemplate: false) {                  id                  icon                  displayName                  type                  route                  path                  active                  layoutConfiguration                  pageTemplateId                  __typename                }                __typename              }            }
            """;
            var requestString = withPageTemplate ? requestStringWithPageTemplate : requestStringWithoutPageTemplate;
            #endregion

            return $$"""
            {
              "operationName": "getEntityTypeInfo",
              "variables": {
                "type": "/{{entityType}}"
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }

        public static string GetVocabularyKeysFromVocabularyIdAsync(Guid vocabularyId)
        {
            #region Request
            var requestString = $$"""
            query getVocabularyKeysFromVocabularyId($id: ID!, $searchName: String, $dataType: String, $classification: String, $filterIsObsolete: String) {              management {                id                vocabularyKeysFromVocabularyId(                  id: $id                  searchName: $searchName                  dataType: $dataType                  classification: $classification                  filterIsObsolete: $filterIsObsolete                ) {                  total                  data {                    ...VocabularyKey                    __typename                  }                  __typename                }                __typename              }            }            fragment VocabularyKey on VocabularyKey {              displayName              vocabularyKeyId              vocabularyId              name              isVisible              isCluedInCore              isDynamic              isProvider              isObsolete              groupName              key              storage              dataClassificationCode              dataType              description              dataAnnotationsIsPrimaryKey              dataAnnotationsIsEditable              dataAnnotationsIsNullable              dataAnnotationsIsRequired              dataAnnotationsMinimumLength              dataAnnotationsMaximumLength              providerId              compositeVocabularyId              compositeVocabulary {                name                displayName                dataType                __typename              }              mapsToOtherKeyId              glossaryTermId              createdAt              createdBy              mappedKey              isValueChangeInsignificant              connector {                id                name                about                icon                type                __typename              }              vocabulary {                vocabularyId                vocabularyName                connector {                  id                  name                  about                  icon                  __typename                }                __typename              }              author {                id                username                __typename              }              __typename            }
            """;
            #endregion

            return $$"""
            {
              "operationName": "getVocabularyKeysFromVocabularyId",
              "variables": {
                "id": "{{vocabularyId}}",
                "filterIsObsolete": "All"
              },
              "query": {{GraphqlQueryHelper.Serialize(requestString)}}
            }
            """;
        }
    }
}

