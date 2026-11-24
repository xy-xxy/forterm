using System;
using System.Text;

using Renci.SshNet.Sftp.Responses;

namespace Renci.SshNet.Sftp.Requests
{
    internal sealed class SftpOpenRequest : SftpRequest
    {
        private readonly Action<SftpHandleResponse> _handleAction;
        private byte[] _fileName;

        public override SftpMessageTypes SftpMessageType
        {
            get { return SftpMessageTypes.Open; }
        }

        public string Filename
        {
            get { return Encoding.GetString(_fileName, 0, _fileName.Length); }
            private set { _fileName = Encoding.GetBytes(value); }
        }

        public Flags Flags { get; }

        public Encoding Encoding { get; }

        /// <summary>
        /// Gets the size of the message in bytes.
        /// </summary>
        /// <value>
        /// The size of the messages in bytes.
        /// </value>
        protected override int BufferCapacity
        {
            get
            {
                var capacity = base.BufferCapacity;
                capacity += 4; // FileName length
                capacity += _fileName.Length; // FileName
                capacity += 4; // Flags
                capacity += 4; // Attributes
                if (((uint)Flags & 0x28u)==0x28u) capacity += 4; // Forterm private exclusive create mode
                return capacity;
            }
        }

        public SftpOpenRequest(uint protocolVersion, uint requestId, string fileName, Encoding encoding, Flags flags, Action<SftpHandleResponse> handleAction, Action<SftpStatusResponse> statusAction)
            : base(protocolVersion, requestId, statusAction)
        {
            Encoding = encoding;
            Filename = fileName;
            Flags = flags;

            _handleAction = handleAction;
        }

        protected override void LoadData()
        {
            base.LoadData();
            throw new NotSupportedException();
        }

        protected override void SaveData()
        {
            base.SaveData();

            WriteBinaryString(_fileName);
            Write((uint)Flags);
            if (((uint)Flags & 0x28u)==0x28u)
            {
                Write(4u); // SSH_FILEXFER_ATTR_PERMISSIONS
                Write(384u); // 0600, supplied atomically at exclusive creation
            }
            else Write(0u); // empty attributes
        }

        public override void Complete(SftpResponse response)
        {
            if (response is SftpHandleResponse handleResponse)
            {
                _handleAction(handleResponse);
            }
            else
            {
                base.Complete(response);
            }
        }
    }
}
