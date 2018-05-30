#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (ImapInboxIdler.cs) is part of MailHandler.
// 
// MailHandler is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// MailHandler is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with MailHandler. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.Threading;
using System.Threading.Tasks;
using MailHandler.Events;
using MailKit;
using MailKit.Net.Imap;

namespace MailHandler {
    internal class ImapInboxIdler : IDisposable {
        private const int IncreaseRetryTimingUntil = 10;
        private const int InitialRetryTimingInMinutes = 1;

        private int _retryTimingInMinutes = InitialRetryTimingInMinutes;
        private int _retryCount;
        private ImapClient _imapClient;
        private bool _imapClientCanIdle;
        private CancellationTokenSource _doneToken;
        private readonly IClientFactory _factory;
        private readonly IMailHandlerConfiguration _config;
        private bool _anthenticatedSuccessfully;
        private IMailFolder _currentFolder;

        /// <summary>
        /// Event published when the mail count in the watched folder changes (the sender is the IMailFolder)
        /// </summary>
        public event EventHandler<FolderEventArgs> CountChanged;

        /// <summary>
        /// Event published when a mail is moving out of the watched folder,
        /// the eventargs contains the index of the moved mail
        /// </summary>
        public event EventHandler<MessageEventArgs> MessageExpunged;

        /// <summary>
        /// Event published when the Uid validity of mail in the watched folder changes
        /// </summary>
        public event EventHandler<EventArgs> UidValidityChanged;

        protected IMailFolder CurrentFolder => _currentFolder ?? (_currentFolder = _imapClient?.Inbox);

        protected string MailBoxName => _config.MailBoxId;

        public ImapInboxIdler(IMailHandlerConfiguration config) {
            _config = config;
            _factory = _config.ClientFactory ?? new ClientFactory(_config);
        }

        public void Dispose() {
            _config.Tracer?.TraceVerbose($"{nameof(ImapInboxIdler)} is disposing", $"{this}");
            _doneToken?.Dispose();
            DisconnectImapIfNeeded();
        }

        private void InboxOnMessageExpunged(object sender, MessageEventArgs e) {
            _config.Tracer?.TraceVerbose($"<MessageExpunged>", $"{this}");
            MessageExpunged?.Invoke(this, e);
        }

        private void InboxOnCountChanged(object sender, EventArgs e) {
            _config.Tracer?.TraceVerbose($"<CountChanged>", $"{this}");
            if (sender is IMailFolder folder) {
                CountChanged?.Invoke(this, new FolderEventArgs(folder));
            }
        }

        private void InboxOnUidValidityChanged(object sender, EventArgs e) {
            _config.Tracer?.TraceVerbose($"<UidValidityChanged>", $"{this}");
            UidValidityChanged?.Invoke(this, e);
        }

        private void DisconnectImapIfNeeded() {
            try {
                if (_imapClient != null) {
                    _imapClient.Disconnected -= ImapClientOnDisconnected;
                }

                if (_currentFolder != null) {
                    _currentFolder.MessageExpunged -= InboxOnMessageExpunged;
                    _currentFolder.CountChanged -= InboxOnCountChanged;
                    _currentFolder.UidValidityChanged -= InboxOnUidValidityChanged;
                }

                _currentFolder = null;

                if (_imapClient != null && _imapClient.IsConnected) {
                    _imapClient.Disconnect(true);
                    _imapClient.Dispose();
                }
            } catch (Exception e) {
                _config.Tracer?.TraceError($"An error has occured while disconnecting - {e}", $"{this}");
            }
        }

        private void ImapClientOnDisconnected(object o, EventArgs eventArgs) {
            _config.Tracer?.TraceVerbose($"Client disconnected", $"{this}");
        }

        /// <summary>
        /// Stop idling, cancel the StartIdling method
        /// </summary>
        public void StopIdling() {
            _config.Tracer?.TraceInformation($"Client requested to stop idling", $"{this}");
            if (_imapClient != null)
                _doneToken?.Cancel();
        }

        /// <summary>
        /// Start idling, i.e. listening for events of the imap server
        /// </summary>
        /// <remarks>this method blocks the thread, it should be called from a new thread</remarks>
        public void StartIdling() {
            _config.Tracer?.TraceInformation($"Start idling", $"{this}");
            if (_imapClient != null)
                return;

            DisconnectImapIfNeeded();

            _doneToken?.Cancel();
            _doneToken?.Dispose();
            _doneToken = new CancellationTokenSource();
            _anthenticatedSuccessfully = false;

            do {
                try {
                    if (!ConnectAndAuthenticate()) {
                        // retry in X seconds
                        if (_retryCount++ > 0 && _retryCount <= IncreaseRetryTimingUntil) {
                            _retryTimingInMinutes = _retryTimingInMinutes * 2;
                        }

                        Task.Delay(TimeSpan.FromMinutes(_retryTimingInMinutes)).Wait();
                        continue;
                    }

                    _anthenticatedSuccessfully = true;
                    _retryCount = 0;
                    _retryTimingInMinutes = InitialRetryTimingInMinutes;

                    SetupEventHandlers();

                    try {
                        IdleAndWaitForExceptionsOrCancel();
                    } catch (Exception e) {
                        // IOException: Read/write pb on the socket
                        // ImapProtocolException: he IMAP server sent garbage in a response and the ImapClient was unable to deal with it
                        // ImapCommandException: The IMAP server responded with "NO" or "BAD" to either the IDLE command or the NOOP command
                        _config.Tracer?.TraceError($"{this} Catched idle exception : {e}");
                        Task.Delay(TimeSpan.FromMinutes(_retryTimingInMinutes)).Wait();
                    }
                } finally {
                    DisconnectImapIfNeeded();
                }
            } while (!_doneToken.IsCancellationRequested);
        }

        private bool ConnectAndAuthenticate() {
            try {
                // connexion and authentication
                _imapClient = _factory.GetImapClient();
                _config.Tracer?.TraceVerbose($"Connected and authenticated successfully", $"{this}");

                CurrentFolder.Open(FolderAccess.ReadOnly);
                _config.Tracer?.TraceVerbose($"Inbox opened", $"{this}");

                _imapClientCanIdle = _imapClient.Capabilities.HasFlag(ImapCapabilities.Idle);
                _config.Tracer?.TraceVerbose($"{(_imapClientCanIdle ? "server accept IDLE" : "server doesn't accept IDLE")}", $"{this}");
            } catch (Exception e) {
                if (!_anthenticatedSuccessfully) {
                    // we never authenticated successfully, probably a credential error
                    throw new Exception($"{this}: authentication failed (credential error?)", e);
                }

                if (_imapClient?.IsConnected ?? false) {
                    _config.Tracer?.TraceError($"Authentication failed - {e}", $"{this}");
                } else {
                    _config.Tracer?.TraceError($"Connection failed - {e}", $"{this}");
                }

                return false;
            }

            return true;
        }

        private void SetupEventHandlers() {
            _imapClient.Disconnected -= ImapClientOnDisconnected;
            _imapClient.Disconnected += ImapClientOnDisconnected;

            CurrentFolder.MessageExpunged -= InboxOnMessageExpunged;
            CurrentFolder.MessageExpunged += InboxOnMessageExpunged;

            CurrentFolder.CountChanged -= InboxOnCountChanged;
            CurrentFolder.CountChanged += InboxOnCountChanged;

            CurrentFolder.UidValidityChanged -= InboxOnUidValidityChanged;
            CurrentFolder.UidValidityChanged += InboxOnUidValidityChanged;
        }

        private void IdleAndWaitForExceptionsOrCancel() {
            using (_doneToken = new CancellationTokenSource()) {
                // Note: when the 'done' CancellationTokenSource is cancelled, it ends to IDLE loop.
                //var job = Task.Factory.StartNew(IdleLoop, new IdleState(client, done.Token), done.Token);
                _config.Tracer?.TraceVerbose($"Entering idle loop", $"{this}");
                IdleLoop(new IdleState(_imapClient, _doneToken.Token));
                _config.Tracer?.TraceVerbose($"Exiting idle loop", $"{this}");
            }
        }

        private void IdleLoop(IdleState idle) {
            lock (idle.Client.SyncRoot) {
                // Note: since the IMAP server will drop the connection after 30 minutes, we must loop sending IDLE commands that
                // last ~29 minutes or until the user has requested that they do not want to IDLE anymore.
                // For GMail, we use a 9 minute interval because they do not seem to keep the connection alive for more than ~10 minutes.
                while (!idle.IsCancellationRequested) {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(_imapClientCanIdle ? 9 : 1))) {
                        try {
                            // We set the timeout source so that if the idle.DoneToken is cancelled, it can cancel the timeout
                            idle.SetTimeoutSource(timeout);

                            if (_imapClientCanIdle) {
                                // The Idle() method will not return until the timeout has elapsed or idle.CancellationToken is cancelled
                                idle.Client.Idle(timeout.Token);
                            } else {
                                // The IMAP server does not support IDLE, so send a NOOP command instead
                                idle.Client.NoOp(idle.DoneToken);

                                // Wait for the timeout to elapse or the cancellation token to be cancelled
                                WaitHandle.WaitAny(new[] {
                                    timeout.Token.WaitHandle, idle.DoneToken.WaitHandle
                                });
                            }
                        } finally {
                            // we will dispose timeout, dont throw an exeption because we are trying to cancel it
                            idle.SetTimeoutSource(null);
                        }

                        if (timeout.IsCancellationRequested) {
                            _config.Tracer?.TraceVerbose($"Idle timed out, looping", $"{this}");
                        }
                    }
                }
            }
        }

        public override string ToString() {
            return $"{nameof(ImapInboxIdler)}.{MailBoxName}";
        }

        private class IdleState {
            readonly object _mutex = new object();

            CancellationTokenSource _timeout;

            /// <summary>
            /// Get the done token.
            /// </summary>
            /// <remarks>
            /// <para>The done token tells the <see cref="ImapInboxIdler.IdleLoop"/> that the user has requested to end the loop.</para>
            /// <para>When the done token is cancelled, the <see cref="ImapInboxIdler.IdleLoop"/> will gracefully come to an end by
            /// cancelling the timeout and then breaking out of the loop.</para>
            /// </remarks>
            /// <value>The done token.</value>
            public CancellationToken DoneToken { get; }

            /// <summary>
            /// Get the IMAP client.
            /// </summary>
            /// <value>The IMAP client.</value>
            public ImapClient Client { get; }

            /// <summary>
            /// Check whether or not either of the CancellationToken's have been cancelled.
            /// </summary>
            /// <value><c>true</c> if cancellation was requested; otherwise, <c>false</c>.</value>
            public bool IsCancellationRequested {
                get { return DoneToken.IsCancellationRequested; }
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="IdleState"/> class.
            /// </summary>
            /// <param name="client">The IMAP client.</param>
            /// <param name="doneToken">The user-controlled 'done' token.</param>
            public IdleState(ImapClient client, CancellationToken doneToken) {
                DoneToken = doneToken;
                Client = client;

                // end the current timeout as well
                doneToken.Register(CancelTimeout);
            }

            /// <summary>
            /// Cancel the timeout token source, forcing ImapClient.Idle() to gracefully exit.
            /// </summary>
            void CancelTimeout() {
                lock (_mutex) {
                    if (_timeout != null)
                        _timeout.Cancel();
                }
            }

            /// <summary>
            /// Set the timeout source.
            /// </summary>
            /// <param name="source">The timeout source.</param>
            public void SetTimeoutSource(CancellationTokenSource source) {
                lock (_mutex) {
                    _timeout = source;
                    if (_timeout != null && IsCancellationRequested) {
                        _timeout.Cancel();
                    }
                }
            }
        }
    }
}