using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Tix.IBMMQ.Bridge.Options;
using Tix.IBMMQ.Bridge.Services;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

FixLinuxCertificates();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MQBridgeOptions>(builder.Configuration.GetSection("MQBridge"));
builder.Services.AddHostedService<MQBridgeService>();

var app = builder.Build();

app.Run();

static void FixLinuxCertificates()
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
    {
        using (var x509Store2 = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            x509Store2.Open(OpenFlags.ReadWrite);

            foreach (var cert in ParseCertificatesFromPem("/usr/local/share/ca-certificates/ca-certificates.crt"))
            //foreach (var cert in ParseCertificatesFromPem("/etc/ssl/certs/ca-certificates.crt"))
            {
                if (!x509Store2.Certificates.Contains(cert) &&
                    cert.Subject.ToLowerInvariant().Contains("corner"))
                {
                    Console.WriteLine($"Add certificate: {cert.Thumbprint}, {cert.Subject}");
                    x509Store2.Add(cert);

                    break;
                }
            }
        }
    }
}

static IEnumerable<X509Certificate2> ParseCertificatesFromPem(string certPath)
{
    var certList = File.ReadAllText(certPath)
        .Split(
            new[] { "-----BEGIN CERTIFICATE-----", "-----END CERTIFICATE-----" },
            StringSplitOptions.RemoveEmptyEntries)
        .Where(x => !string.IsNullOrWhiteSpace(x.Replace('\r', ' ').Replace('\n', ' ')))
        .Select(x => x.Trim());

    foreach (string cert in certList)
    {
        byte[] certBytes = Convert.FromBase64String(cert);
        yield return new X509Certificate2(certBytes);
    }
}