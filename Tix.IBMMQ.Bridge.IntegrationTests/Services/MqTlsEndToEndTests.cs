using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using IBM.WMQ;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Services;

public class MqTlsEndToEndTests(ITestOutputHelper logger)
{
    [Fact]
    public void Should_forward_message_between_queues_with_tls()
    {
        Environment.SetEnvironmentVariable("MQDOTNET_TRACE_ON", "2");
        
        TrustCaOnHost();
        
        var props = new Hashtable
        {
            { MQC.HOST_NAME_PROPERTY, "2.tcp.eu.ngrok.io" },
            { MQC.PORT_PROPERTY, 17083 },
            { MQC.CHANNEL_PROPERTY, "APP.TLS.SVRCONN" },
            { MQC.TRANSPORT_PROPERTY, MQC.TRANSPORT_MQSERIES_MANAGED },
            { MQC.SSL_CIPHER_SPEC_PROPERTY, "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384" },
            { MQC.USER_ID_PROPERTY, "app" },
            { MQC.PASSWORD_PROPERTY, "passw0rd" }
        };
        
        for (int i = 0; i < 10; i++)
        {
            var timer = Stopwatch.StartNew();

            using var qmgr = new MQQueueManager("", props);
            
            using var queue = qmgr.AccessQueue("DEV.QUEUE.1", MQC.MQOO_INPUT_AS_Q_DEF | MQC.MQOO_OUTPUT);

            var putMsg = new MQMessage();
            putMsg.WriteString("hello tls");
            queue.Put(putMsg, new MQPutMessageOptions());

            var got = new MQMessage();
            var gmo = new MQGetMessageOptions { Options = MQC.MQGMO_WAIT, WaitInterval = 5000 };
            queue.Get(got, gmo);
            var body = got.ReadString(got.MessageLength);
            
            Assert.Equal("hello tls", body);
            
            timer.Stop();
            
            logger.WriteLine(timer.ElapsedMilliseconds.ToString());
            
            timer.ElapsedMilliseconds.ShouldBeLessThanOrEqualTo(1000);
        }
    }
    
    private static void TrustCaOnHost()
    {
        var caCertPath = Path.Combine(AppContext.BaseDirectory, "Services", "ca.crt");
        
        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            store.Open(OpenFlags.ReadWrite);
            var certPem = File.ReadAllText(caCertPath);
            var cert = X509Certificate2.CreateFromPem(certPem);
            store.Add(cert);
        }
        
        if (OperatingSystem.IsMacOS())
        {
            var loginKeychain = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Keychains/login.keychain-db");

            RunCli("security", $"add-trusted-cert -d -r trustRoot -k \"{loginKeychain}\" \"{caCertPath}\"");
        }
    }
    
    private static void RunCli(string fileName, string args, bool ignoreErrors = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        string stdout = p.StandardOutput.ReadToEnd();
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (p.ExitCode != 0 && !ignoreErrors)
            throw new InvalidOperationException(
                $"{fileName} {args}\nExitCode={p.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
}