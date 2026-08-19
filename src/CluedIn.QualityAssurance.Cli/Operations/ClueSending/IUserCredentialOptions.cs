using CluedIn.QualityAssurance.Cli.Environments;

using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal interface IUserCredentialOptions
{
    [Option("use-existing-organization", HelpText = "Whether to use an existing organization.")]
    public bool UseExistingOrganization { get; set; }

    [Option('o', Default = "foobar", Required = false, HelpText = "Organization name.")]
    string OrganizationName { get; set; }

    [Option('u', Default = "", Required = false, HelpText = "User name.")]
    string UserName { get; set; }

    [Option('p', Default = "", Required = false, HelpText = "User password.")]
    string UserPassword { get; set; }
}
