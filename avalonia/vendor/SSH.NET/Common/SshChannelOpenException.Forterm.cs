#nullable enable
namespace Renci.SshNet.Common
{
    // Forterm: what the server said when it refused a session channel (RFC 4254 5.1). Upstream throws an SshException
    // with the same message and keeps neither the reason nor the description.
    public sealed class SshChannelOpenException : SshException
    {
        public SshChannelOpenException(string message, uint reasonCode, string description)
            : base(message)
        {
            ReasonCode = reasonCode;
            Description = description;
        }

        public uint ReasonCode { get; }

        // Text from the server.
        public string Description { get; }
    }
}
