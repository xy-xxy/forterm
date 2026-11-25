#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

using Renci.SshNet.Common;

namespace Renci.SshNet
{
    /// <inheritdoc cref="ISshClient" />
    public class SshClient : BaseClient, ISshClient
    {
        /// <summary>
        /// Holds the list of forwarded ports.
        /// </summary>
        private readonly List<ForwardedPort> _forwardedPorts;

        /// <summary>
        /// Holds a value indicating whether the current instance is disposed.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if the current instance is disposed; otherwise, <see langword="false"/>.
        /// </value>
        private bool _isDisposed;

        private MemoryStream? _inputStream;

        /// <inheritdoc />
        public IEnumerable<ForwardedPort> ForwardedPorts
        {
            get
            {
                return _forwardedPorts.AsReadOnly();
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshClient" /> class.
        /// </summary>
        /// <param name="connectionInfo">The connection info.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connectionInfo"/> is <see langword="null"/>.</exception>
        public SshClient(ConnectionInfo connectionInfo)
            : this(connectionInfo, ownsConnectionInfo: false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshClient"/> class.
        /// </summary>
        /// <param name="host">Connection host.</param>
        /// <param name="port">Connection port.</param>
        /// <param name="username">Authentication username.</param>
        /// <param name="password">Authentication password.</param>
        /// <exception cref="ArgumentNullException"><paramref name="password"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="host"/> is invalid, or <paramref name="username"/> is <see langword="null"/> or contains only whitespace characters.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is not within <see cref="IPEndPoint.MinPort"/> and <see cref="IPEndPoint.MaxPort"/>.</exception>
        public SshClient(string host, int port, string username, string password)
            : this(new PasswordConnectionInfo(host, port, username, password), ownsConnectionInfo: true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshClient"/> class.
        /// </summary>
        /// <param name="host">Connection host.</param>
        /// <param name="username">Authentication username.</param>
        /// <param name="password">Authentication password.</param>
        /// <exception cref="ArgumentNullException"><paramref name="password"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="host"/> is invalid, or <paramref name="username"/> is <see langword="null"/> or contains only whitespace characters.</exception>
        public SshClient(string host, string username, string password)
            : this(host, ConnectionInfo.DefaultPort, username, password)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshClient"/> class.
        /// </summary>
        /// <param name="host">Connection host.</param>
        /// <param name="port">Connection port.</param>
        /// <param name="username">Authentication username.</param>
        /// <param name="keyFiles">Authentication private key file(s) .</param>
        /// <exception cref="ArgumentNullException"><paramref name="keyFiles"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="host"/> is invalid, -or- <paramref name="username"/> is <see langword="null"/> or contains only whitespace characters.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is not within <see cref="IPEndPoint.MinPort"/> and <see cref="IPEndPoint.MaxPort"/>.</exception>
        public SshClient(string host, int port, string username, params IPrivateKeySource[] keyFiles)
            : this(new PrivateKeyConnectionInfo(host, port, username, keyFiles), ownsConnectionInfo: true)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshClient"/> class.
        /// </summary>
        /// <param name="host">Connection host.</param>
        /// <param name="username">Authentication username.</param>
        /// <param name="keyFiles">Authentication private key file(s) .</param>
        /// <exception cref="ArgumentNullException"><paramref name="keyFiles"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="host"/> is invalid, -or- <paramref name="username"/> is <see langword="null"/> or contains only whitespace characters.</exception>
        public SshClient(string host, string username, params IPrivateKeySource[] keyFiles)
            : this(host, ConnectionInfo.DefaultPort, username, keyFiles)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshClient"/> class.
        /// </summary>
        /// <param name="connectionInfo">The connection info.</param>
        /// <param name="ownsConnectionInfo">Specified whether this instance owns the connection info.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connectionInfo"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// If <paramref name="ownsConnectionInfo"/> is <see langword="true"/>, then the
        /// connection info will be disposed when this instance is disposed.
        /// </remarks>
        private SshClient(ConnectionInfo connectionInfo, bool ownsConnectionInfo)
            : this(connectionInfo, ownsConnectionInfo, new ServiceFactory())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SshClient"/> class.
        /// </summary>
        /// <param name="connectionInfo">The connection info.</param>
        /// <param name="ownsConnectionInfo">Specified whether this instance owns the connection info.</param>
        /// <param name="serviceFactory">The factory to use for creating new services.</param>
        /// <exception cref="ArgumentNullException"><paramref name="connectionInfo"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="serviceFactory"/> is <see langword="null"/>.</exception>
        /// <remarks>
        /// If <paramref name="ownsConnectionInfo"/> is <see langword="true"/>, then the
        /// connection info will be disposed when this instance is disposed.
        /// </remarks>
        internal SshClient(ConnectionInfo connectionInfo, bool ownsConnectionInfo, IServiceFactory serviceFactory)
            : base(connectionInfo, ownsConnectionInfo, serviceFactory)
        {
            _forwardedPorts = new List<ForwardedPort>();
        }

        /// <summary>
        /// Called when client is disconnecting from the server.
        /// </summary>
        protected override void OnDisconnecting()
        {
            base.OnDisconnecting();

            foreach (var port in _forwardedPorts)
            {
                port.Stop();
            }
        }

        /// <inheritdoc />
        public void AddForwardedPort(ForwardedPort port)
        {
            ArgumentNullException.ThrowIfNull(port);

            EnsureSessionIsOpen();

            AttachForwardedPort(port);
            _forwardedPorts.Add(port);
        }

        /// <inheritdoc />
        public void RemoveForwardedPort(ForwardedPort port)
        {
            ArgumentNullException.ThrowIfNull(port);

            // Stop port forwarding before removing it
            port.Stop();

            DetachForwardedPort(port);
            _ = _forwardedPorts.Remove(port);
        }

        private void AttachForwardedPort(ForwardedPort port)
        {
            if (port.Session != null && port.Session != Session)
            {
                throw new InvalidOperationException("Forwarded port is already added to a different client.");
            }

            port.Session = Session;
        }

        private static void DetachForwardedPort(ForwardedPort port)
        {
            port.Session = null;
        }

        /// <inheritdoc />
        public SshCommand CreateCommand(string commandText)
        {
            return CreateCommand(commandText, ConnectionInfo.Encoding);
        }

        /// <inheritdoc />
        public SshCommand CreateCommand(string commandText, Encoding encoding)
        {
            EnsureSessionIsOpen();

            ConnectionInfo.Encoding = encoding;
            return new SshCommand(Session!, commandText, encoding);
        }

        /// <inheritdoc />
        public SshCommand RunCommand(string commandText)
        {
            var cmd = CreateCommand(commandText);
            _ = cmd.Execute();
            return cmd;
        }

        /// <inheritdoc />
        public Shell CreateShell(Stream input, Stream output, Stream extendedOutput, string terminalName, uint columns, uint rows, uint width, uint height, IDictionary<TerminalModes, uint>? terminalModes, int bufferSize)
        {
            EnsureSessionIsOpen();

            return new Shell(Session, input, output, extendedOutput, terminalName, columns, rows, width, height, terminalModes, bufferSize);
        }

        /// <inheritdoc />
        public Shell CreateShell(Stream input, Stream output, Stream extendedOutput, string terminalName, uint columns, uint rows, uint width, uint height, IDictionary<TerminalModes, uint> terminalModes)
        {
            return CreateShell(input, output, extendedOutput, terminalName, columns, rows, width, height, terminalModes, 1024);
        }

        /// <inheritdoc />
        public Shell CreateShell(Stream input, Stream output, Stream extendedOutput)
        {
            return CreateShell(input, output, extendedOutput, string.Empty, 0, 0, 0, 0, terminalModes: null, 1024);
        }

        /// <inheritdoc />
        public Shell CreateShell(Encoding encoding, string input, Stream output, Stream extendedOutput, string terminalName, uint columns, uint rows, uint width, uint height, IDictionary<TerminalModes, uint>? terminalModes, int bufferSize)
        {
            /*
             * TODO Issue #1224: let shell dispose of input stream when we own the stream!
             */

            _inputStream = new MemoryStream();

            using (var writer = new StreamWriter(_inputStream, encoding, bufferSize: 1024, leaveOpen: true))
            {
                writer.Write(input);
                writer.Flush();
            }

            _ = _inputStream.Seek(0, SeekOrigin.Begin);

            return CreateShell(_inputStream, output, extendedOutput, terminalName, columns, rows, width, height, terminalModes, bufferSize);
        }

        /// <inheritdoc />
        public Shell CreateShell(Encoding encoding, string input, Stream output, Stream extendedOutput, string terminalName, uint columns, uint rows, uint width, uint height, IDictionary<TerminalModes, uint> terminalModes)
        {
            return CreateShell(encoding, input, output, extendedOutput, terminalName, columns, rows, width, height, terminalModes, 1024);
        }

        /// <inheritdoc />
        public Shell CreateShell(Encoding encoding, string input, Stream output, Stream extendedOutput)
        {
            return CreateShell(encoding, input, output, extendedOutput, string.Empty, 0, 0, 0, 0, terminalModes: null, 1024);
        }

        /// <inheritdoc />
        public Shell CreateShellNoTerminal(Stream input, Stream output, Stream extendedOutput, int bufferSize = -1)
        {
            EnsureSessionIsOpen();

            return new Shell(Session, input, output, extendedOutput, bufferSize);
        }

        /// <inheritdoc />
        // Forterm extension; owner lifetime is retained by the application's
        // session lease. A subsystem rejection does not dispose the owner.
        public SftpClient OpenSharedSftpClient() => new SftpClient(this);

        // Forterm: a caller-authenticated, single-target private relay owns
        // the socket. No unauthenticated local forwarding listener is created.
        public void BridgeOwnedSocket(string host, uint port, IForwardedPort lifetime,
            System.Net.Sockets.Socket socket, Action opened)
        {
            CheckDisposed();
            var session = Session ?? throw new SshConnectionException("SSH transport is closed.");
            using (var channel = session.CreateChannelDirectTcpip())
            {
                Exception failure = null;
                channel.Exception += (_, args) => { failure = args.Exception; socket.Dispose(); };
                channel.Open(host, port, lifetime, socket);
                if (!channel.IsOpen) throw new SshException("Jump server rejected direct-tcpip.");
                opened();
                channel.Bind();
                if (failure != null) throw failure;
            }
        }

        private readonly System.Threading.SemaphoreSlim _fortermKeepAliveGate = new System.Threading.SemaphoreSlim(1, 1);
        private bool _fortermProbeUnconfirmed;
        // Forterm exposes no remote-forward global requests. Only one global
        // keepalive may be outstanding; failure replies also prove liveness.
        public async System.Threading.Tasks.Task ProbeTransportAsync(TimeSpan responseWindow, int missedWindows, System.Threading.CancellationToken ct)
        {
            var session = Session ?? throw new SshConnectionException("SSH transport is closed.");
            await _fortermKeepAliveGate.WaitAsync(ct).ConfigureAwait(false);
            var reply = new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<MessageEventArgs<Renci.SshNet.Messages.Connection.RequestSuccessMessage>> success = (_, _) => reply.TrySetResult(true);
            EventHandler<MessageEventArgs<Renci.SshNet.Messages.Connection.RequestFailureMessage>> failure = (_, _) => reply.TrySetResult(true);
            try
            {
                if (_fortermProbeUnconfirmed) throw new SshException("An unconfirmed global request is still outstanding.");
                session.RequestSuccessReceived += success;
                session.RequestFailureReceived += failure;
                session.SendMessage(new Renci.SshNet.Messages.Connection.GlobalRequestMessage(System.Text.Encoding.ASCII.GetBytes("keepalive@openssh.com"), true));
                for (int n = 0; n < missedWindows; n++)
                {
                    try { await reply.Task.WaitAsync(responseWindow, ct).ConfigureAwait(false); return; }
                    catch (TimeoutException) when (n + 1 < missedWindows) { }
                }
            }
            catch (TimeoutException) { _fortermProbeUnconfirmed = true; throw; }
            catch (System.OperationCanceledException) { if (!reply.Task.IsCompleted) _fortermProbeUnconfirmed = true; throw; }
            finally { session.RequestSuccessReceived -= success; session.RequestFailureReceived -= failure; _fortermKeepAliveGate.Release(); }
        }

        public ShellStream CreateShellStream(string terminalName, uint columns, uint rows, uint width, uint height, int bufferSize)
        {
            return CreateShellStream(terminalName, columns, rows, width, height, bufferSize, terminalModeValues: null);
        }

        /// <inheritdoc />
        public ShellStream CreateShellStream(string terminalName, uint columns, uint rows, uint width, uint height, int bufferSize, IDictionary<TerminalModes, uint>? terminalModeValues)
        {
            EnsureSessionIsOpen();

            return ServiceFactory.CreateShellStream(Session, terminalName, columns, rows, width, height, terminalModeValues, bufferSize);
        }

        // Forterm: a shell whose channel sends "env" requests before the shell request. Names the
        // server refused are returned; the shell still opens. A window above 0 turns on flow control
        // (see ShellStream.ReleaseWindow).
        public ShellStream CreateShellStream(string terminalName, uint columns, uint rows, uint width, uint height, int bufferSize,
            IReadOnlyList<KeyValuePair<string, string>> environment, out IReadOnlyList<string> rejected, uint window = 0, IDictionary<TerminalModes, uint>? modes = null)
        {
            EnsureSessionIsOpen();
            var refused = new List<string>();
            var stream = new ShellStream(Session!, terminalName, columns, rows, width, height, bufferSize, environment, refused, window, modes);
            rejected = refused;
            return stream;
        }

        /// <inheritdoc />
        public ShellStream CreateShellStreamNoTerminal(int bufferSize = -1)
        {
            EnsureSessionIsOpen();

            return ServiceFactory.CreateShellStreamNoTerminal(Session, bufferSize);
        }

        /// <summary>
        /// Stops forwarded ports.
        /// </summary>
        protected override void OnDisconnected()
        {
            base.OnDisconnected();

            for (var i = _forwardedPorts.Count - 1; i >= 0; i--)
            {
                var port = _forwardedPorts[i];
                DetachForwardedPort(port);
                _forwardedPorts.RemoveAt(i);
            }
        }

        /// <summary>
        /// Releases unmanaged and - optionally - managed resources.
        /// </summary>
        /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _inputStream?.Dispose();
                _inputStream = null;

                _isDisposed = true;
            }
        }

        private void EnsureSessionIsOpen()
        {
            if (Session is null)
            {
                throw new SshConnectionException("Client not connected.");
            }
        }
    }
}
