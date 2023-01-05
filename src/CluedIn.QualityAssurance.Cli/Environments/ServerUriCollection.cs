namespace CluedIn.QualityAssurance.Cli.Environments;

internal record ServerUriCollection(
    Uri AuthApiUri,
    Uri PublicApiUri,
    Uri WebApiUri,
    Uri UiGraphqlUri,
    Uri UploadApiUri);