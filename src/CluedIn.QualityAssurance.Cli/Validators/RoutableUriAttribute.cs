using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;

namespace CluedIn.QualityAssurance.Cli.Validators;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
internal class RoutableUriAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value != null && Uri.TryCreate(value.ToString(), UriKind.Absolute, out var uri))
        {
            using var tcpClient = new TcpClient();
            try
            {
                tcpClient.Connect(uri.Host, uri.Port);
            }
            catch (Exception)
            {
                return new ValidationResult($"Unable to connect to host {value}");
            }
        }

        return ValidationResult.Success;
    }
}