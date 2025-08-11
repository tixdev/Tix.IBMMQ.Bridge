using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Helpers
{
    internal class ContainerImage
    {
        static string name;
        public string Name { get { return name; } }

        static ContainerImage()
        {
            var isArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            name = isArm
                ? "ibm-mqadvanced-server-dev:9.4.3.0-arm64"
                : "ibmcom/mq:latest";

            if (isArm && !ImageExists(name))
            {
                RunScript("./build-arm-mq-image.sh");
            }
        }

        private static bool ImageExists(string image)
        {
            var psi = new ProcessStartInfo("docker", $"image inspect {image}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }

        private static void RunScript(string script)
        {
            var psi = new ProcessStartInfo("bash", script)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc.WaitForExit();
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException($"Script {script} failed.");
            }
        }
    }

    static class ContainerImageExtensions
    {
        public static MqContainer BuildMqContainer(this ContainerImage image,
            string mqStartupScriptPath = null,
            bool exposeWebConsole = false)
        {
            return new MqContainer(image).Build(mqStartupScriptPath, exposeWebConsole);
        }
    }
}
