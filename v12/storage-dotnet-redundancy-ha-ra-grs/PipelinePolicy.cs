using Azure.Core;
using Azure.Core.Pipeline;

namespace StorageRedundancy
{
    internal class ShowRequestInfoPolicy : HttpPipelinePolicy
    {
        public override void Process(
            HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            ProcessMessage(message);
            ProcessNext(message, pipeline);
        }
        public override async ValueTask ProcessAsync(
            HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
        {
            ProcessMessage(message);
            await ProcessNextAsync(message, pipeline);
        }

        private void ProcessMessage(HttpMessage message)
        {
            string? host = message.Request.Uri.Host;
            if (host is not null)
            {
                string region = host.Contains("-secondary") ? "secondary" : "primary";
                Console.WriteLine($"Request sent to {region} region endpoint");
            }
        }
    }
}
