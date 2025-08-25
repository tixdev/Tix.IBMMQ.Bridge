using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Tix.IBMMQ.Bridge.IntegrationTests.Helpers
{
    internal class TestHelper
    {
        /// <summary>
        /// </summary>
        /// <param name="test"></param>
        /// <param name="timeDependentTest">
        /// If the test is time-dependent, two delayed successes will be required to pass it
        /// </param>
        /// <param name="timoutSeconds"></param>
        /// <returns></returns>
        public static async Task<bool> Evaluate(
            ITestOutputHelper logger,
            Func<bool> test,
            bool timeDependentTest = true,
            int timeoutSeconds = 5)
        {
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var interval = TimeSpan.FromMilliseconds(500);
            var startTime = DateTime.UtcNow;

            do
            {
                if (test())
                {
                    if (!timeDependentTest)
                        return true;

                    // Repeat the test once more as it may change, depending by time
                    timeDependentTest = false;
                }

                logger.WriteLine($"Retest in {interval}");
                await Task.Delay(interval);
            }
            while (DateTime.UtcNow - startTime < timeout);

            logger.WriteLine($"Test failed for timeout after {timeout}");
            return false;
        }
    }
}
