﻿using CommandLine;

namespace CluedIn.QualityAssurance.Cli.Operations.ClueSending;

internal interface IClueSendingOperationOptions : IMultiIterationOptions
{
    [Option('c', "client-id-prefix", Default = "foobar", Required = false, HelpText = "Client id prefix to be used when creating organizations.")]
    string ClientIdPrefix { get; set; }

    [Option('u', "username", Default = "admin", Required = false, HelpText = "Username when creating organization.")]
    string UserName { get; set; }

    [Option('p', "password", Default = "Foobar23!", Required = false, HelpText = "Password when creating organization.")]
    string Password { get; set; }


    [Option("is-reingestion", Default = false, Required = false, HelpText = "Whether this is a reingestion test.")]
    public bool IsReingestion { get; set; }

    [Option("output-directory", Required = true, HelpText = "Result output directory.")]
    public string OutputDirectory { get; set; }
}