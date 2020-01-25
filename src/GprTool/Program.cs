﻿using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using RestSharp;
using RestSharp.Authenticators;

namespace GprTool
{
    [Command("gpr")]
    [Subcommand(typeof(ListCommand), typeof(PushCommand), typeof(DetailsCommand))]
    public class Program : GprCommandBase
    {
        public static Task Main(string[] args) =>
            CommandLineApplication.ExecuteAsync<Program>(args);

        protected override Task OnExecute(CommandLineApplication app)
        {
            // this shows help even if the --help option isn't specified
            app.ShowHelp();
            return Task.CompletedTask;
        }
    }

    [Command(Description = "List my packages")]
    public class ListCommand : GprCommandBase
    {
        protected override Task OnExecute(CommandLineApplication app)
        {
            var user = "GprTool";
            var token = AccessToken;
            var client = new RestClient("https://api.github.com/graphql");
            client.Authenticator = new HttpBasicAuthenticator(user, token);
            var request = new RestRequest(Method.POST);
            request.AddHeader("accept", "application/vnd.github.packages-preview+json,application/vnd.github.package-deletes-preview+json");
            var graphql = @"{""query"":""query { viewer { registryPackages(first: 10) { nodes { name packageType nameWithOwner versions(first: 10) { nodes { id version readme deleted files(first: 10) { nodes { name updatedAt } } } } } } } }"",""variables"":{}}";
            request.AddParameter("undefined", graphql, ParameterType.RequestBody);
            IRestResponse response = client.Execute(request);

            var doc = JsonDocument.Parse(response.Content);

            var registryPackages = doc.RootElement
                .GetProperty("data")
                .GetProperty("viewer")
                .GetProperty("registryPackages")
                .GetProperty("nodes")
                .EnumerateArray()
                .Select(rp => (name: rp.GetProperty("name"),
                    packageType: rp.GetProperty("packageType"),
                    nameWithOwner: rp.GetProperty("nameWithOwner"),
                    versions: rp.GetProperty("versions").GetProperty("nodes").EnumerateArray()))
                .GroupBy(p => p.nameWithOwner);

            foreach (var packagesByRepo in registryPackages)
            {
                Console.WriteLine(packagesByRepo.Key);
                foreach (var package in packagesByRepo)
                {
                    var versions = package.versions.Select(v => v.GetProperty("version"));
                    Console.WriteLine($"    {package.name} ({package.packageType}) [{string.Join(", ", versions)}]");
                }
            }

            return Task.CompletedTask;
        }
    }

    [Command(Description = "Publish a package")]
    public class PushCommand : GprCommandBase
    {
        protected override Task OnExecute(CommandLineApplication app)
        {
            var user = "GprTool";
            var token = AccessToken;
            var client = new RestClient($"https://nuget.pkg.github.com/{Owner}/");
            client.Authenticator = new HttpBasicAuthenticator(user, token);
            var request = new RestRequest(Method.PUT);
            request.AddFile("package", PackageFile);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                Console.WriteLine(response.Content);
                return Task.CompletedTask;
            }

            var nugetWarning = response.Headers.FirstOrDefault(h =>
                h.Name.Equals("X-Nuget-Warning", StringComparison.OrdinalIgnoreCase));
            if (nugetWarning != null)
            {
                Console.WriteLine(nugetWarning.Value);
                return Task.CompletedTask;
            }

            Console.WriteLine(response.StatusDescription);
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"{header.Name}: {header.Value}");
            }
            return Task.CompletedTask;
        }

        [Argument(0, Description = "Path to the package file")]
        public string PackageFile { get; set; }

        [Option("--owner", Description = "The owner if repository URL wasn't specified in nupkg/nuspec")]
        public string Owner { get; } = "GPR-TOOL-DEFAULT-OWNER";
    }

    [Command(Description = "View package details")]
    public class DetailsCommand : GprCommandBase
    {
        protected override Task OnExecute(CommandLineApplication app)
        {
            var user = "GprTool";
            var token = AccessToken;
            var client = new RestClient($"https://nuget.pkg.github.com/{Owner}/{Name}/{Version}.json");
            client.Authenticator = new HttpBasicAuthenticator(user, token);
            var request = new RestRequest(Method.GET);
            var response = client.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var doc = JsonDocument.Parse(response.Content);
                var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
                return Task.CompletedTask;
            }

            var nugetWarning = response.Headers.FirstOrDefault(h =>
                h.Name.Equals("X-Nuget-Warning", StringComparison.OrdinalIgnoreCase));
            if (nugetWarning != null)
            {
                Console.WriteLine(nugetWarning.Value);
                return Task.CompletedTask;
            }

            Console.WriteLine(response.StatusDescription);
            foreach (var header in response.Headers)
            {
                Console.WriteLine($"{header.Name}: {header.Value}");
            }
            return Task.CompletedTask;
        }

        [Argument(0, Description = "Package owner")]
        public string Owner { get; set; }

        [Argument(1, Description = "Package name")]
        public string Name { get; set; }

        [Argument(2, Description = "Package version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// This base type provides shared functionality.
    /// Also, declaring <see cref="HelpOptionAttribute"/> on this type means all types that inherit from it
    /// will automatically support '--help'
    /// </summary>
    [HelpOption("--help")]
    public abstract class GprCommandBase
    {
        protected abstract Task OnExecute(CommandLineApplication app);

        [Option("-k|--api-key", Description = "The access token to use")]
        public string AccessToken { get; }
    }
}
