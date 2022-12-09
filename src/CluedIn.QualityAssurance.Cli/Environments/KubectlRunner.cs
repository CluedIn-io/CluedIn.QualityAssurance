using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CluedIn.QualityAssurance.Cli.Environments;

internal class RunStatus
{
    public string Output { get; }
    public string Errors { get; }
    public int ExitCode { get; }

    public bool IsSuccess => ExitCode == 0;

    public RunStatus(string output, string errors, int exitCode)
    {
        Output = output;
        Errors = errors;
        ExitCode = exitCode;
    }

}

internal class PortForwardResult
{
    public Process Process { get; set; }

    public string Uri { get; set; }
}

internal class KubectlRunner
{
    private const string KubectlExecutablePath = "kubectl";

    public async Task PortForwardAsync(string workingDirectory, string[] arguments, TaskCompletionSource<PortForwardResult> taskCompletionSource, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(KubectlExecutablePath, string.Join(" ", arguments))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = new Process();
        try
        {
            process.StartInfo = psi;
            process.Start();

            var errors = new StringBuilder();
            var outputTask = WaitForUri(process.StandardOutput, process, taskCompletionSource);
            var errorTask = ThrowWithErrorOutput(process.StandardError, errors, taskCompletionSource);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task WaitForUri(StreamReader reader, Process process, TaskCompletionSource<PortForwardResult> taskCompletionSource)
    {
        await Task.Yield();

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            if (line.StartsWith("Forwarding from "))
            {
                var regex = new Regex(@"([0-9\.]+:[0-9]+) -> ([0-9]+)");
                var match = regex.Match(line);
                if (match.Success)
                {
                    taskCompletionSource.SetResult(new PortForwardResult
                    {
                        Process = process,
                        Uri = match.Groups[1].Value,
                    });
                    return;
                }

            }
        }

        throw new InvalidOperationException("Failed to port forward.");
    }

    private static async Task ThrowWithErrorOutput(StreamReader reader, StringBuilder lines, TaskCompletionSource<PortForwardResult> taskCompletionSource)
    {
        await Task.Yield();

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            lines.AppendLine(line);
        }
        taskCompletionSource.SetException(new InvalidOperationException($"Error occurred with command & error output: '{lines}'"));
    }

    public async Task<RunStatus> RunAsync(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(KubectlExecutablePath, string.Join(" ", arguments))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var p = new Process();
        try
        {
            p.StartInfo = psi;
            p.Start();

            var output = new StringBuilder();
            var errors = new StringBuilder();
            var outputTask = ConsumeStreamReaderAsync(p.StandardOutput, output);
            var errorTask = ConsumeStreamReaderAsync(p.StandardError, errors);

            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);

            return new RunStatus(output.ToString(), errors.ToString(), p.ExitCode);
        }
        finally
        {
            p.Dispose();
        }
    }

    private static async Task ConsumeStreamReaderAsync(StreamReader reader, StringBuilder lines)
    {
        await Task.Yield();

        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
        {
            lines.AppendLine(line);
        }
    }
}
