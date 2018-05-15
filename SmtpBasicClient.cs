#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (SmtpBasicClient.cs) is part of MailHandler.
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
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Timers;
using MailHandler.Utilities;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Utils;

namespace MailHandler {
    internal class SmtpBasicClient : IDisposable {
        protected const int IncreaseRetryTimingUntil = 10;
        protected const int InitialRetryTimingInMinutes = 1;
        protected const int KeepAliveTimeout = 2 * 60 * 1000;

        private int _retryTimingInMinutes = InitialRetryTimingInMinutes;
        private int _retryCount;
        private bool _anthenticatedSuccessfully;
        private Timer _timeoutKeepAlive;

        private SmtpClient _smtpClient;
        private readonly IClientFactory _factory;
        private IMailHandlerConfiguration _config;

        protected IClientFactory Factory => _factory;

        protected string MailBoxName => _config.MailBoxId;

        protected SmtpClient Client {
            get { return _smtpClient; }
            set { _smtpClient = value; }
        }

        public SmtpBasicClient(IMailHandlerConfiguration config) {
            _config = config;
            _factory = _config.ClientFactory ?? new ClientFactory(_config);
            _timeoutKeepAlive = new Timer(KeepAliveTimeout) {
                AutoReset = true
            };
            _timeoutKeepAlive.Elapsed += TimeoutKeepAliveOnElapsed;
        }

        public virtual void Dispose() {
            _config.Tracer?.TraceVerbose($"{nameof(SmtpBasicClient)} is disposing", $"{this}");
            _timeoutKeepAlive?.Close();
            _timeoutKeepAlive?.Dispose();
            DisconnectIfNeeded();
        }

        protected void DisconnectIfNeeded() {
            try {
                _timeoutKeepAlive?.Stop();
                if (Client != null) {
                    Client.Disconnected -= SmtpClientOnDisconnected;
                }

                if (Client != null && Client.IsConnected) {
                    Client.Disconnect(true);
                    Client.Dispose();
                }
            } catch (Exception e) {
                _config.Tracer?.TraceError($"An error has occured while disconnecting - {e}", $"{this}");
            }
        }

        private void SmtpClientOnDisconnected(object o, EventArgs eventArgs) {
            _config.Tracer?.TraceVerbose($"Client disconnected", $"{this}");
        }

        private bool ConnectAndAuthenticate() {
            try {
                // connexion and authentication
                Client = Factory.GetSmtpClient();
                _config.Tracer?.TraceInformation($"Connected and authenticated successfully", $"{this}");
            } catch (Exception e) {
                if (!_anthenticatedSuccessfully) {
                    // we never authenticated successfully, probably a credential error
                    throw new Exception($"{this}: authentication failed (credential error?)", e);
                }

                if (Client?.IsConnected ?? false) {
                    _config.Tracer?.TraceError($"Authentication failed - {e}", $"{this}");
                } else {
                    _config.Tracer?.TraceError($"Connection failed - {e}", $"{this}");
                }

                return false;
            }

            return true;
        }

        protected void DoOnLock(Action action) {
            lock (Client.SyncRoot) {
                try {
                    action?.Invoke();
                } catch (Exception e) {
                    if (!Client.IsConnected || e.Message.ToLower().Contains("connection timed out")) {
                        _config.Tracer?.TraceInformation($"Client appears disconnected, trying to reconnect", $"{this}");
                        Connect();
                        try {
                            action?.Invoke();
                        } catch (Exception e2) {
                            _config.Tracer?.TraceError($"Command failed - {e2}", $"{this}");
                            throw new Exception($"{this}: command failed - {e2}", e2);
                        }
                    } else {
                        _config.Tracer?.TraceError($"Command failed - {e}", $"{this}");
                        throw new Exception($"{this}: command failed - {e}", e);
                    }
                } finally {
                    _timeoutKeepAlive?.Start();
                }
            }
        }

        private void TimeoutKeepAliveOnElapsed(object sender, ElapsedEventArgs e) {
            DoOnLock(() => {
                Client.NoOp();
                _config.Tracer?.TraceVerbose($"NoOp command sent", $"{this}");
            });
        }

        public void Connect() {
            _anthenticatedSuccessfully = false;

            while (_retryCount <= IncreaseRetryTimingUntil) {
                DisconnectIfNeeded();
                if (!ConnectAndAuthenticate()) {
                    // retry in X seconds
                    if (_retryCount++ > 0) {
                        _retryTimingInMinutes = _retryTimingInMinutes * 2;
                    }

                    Task.Delay(TimeSpan.FromMinutes(_retryTimingInMinutes)).Wait();
                    continue;
                }

                _anthenticatedSuccessfully = true;

                Client.Disconnected -= SmtpClientOnDisconnected;
                Client.Disconnected += SmtpClientOnDisconnected;
                break;
            }

            _retryCount = 0;
            _retryTimingInMinutes = InitialRetryTimingInMinutes;
            _timeoutKeepAlive?.Start();
        }

        public void SendMessage(MimeMessage message) {
            DoOnLock(() => {
                _config.Tracer?.TraceVerbose($"Sending new mail to {message.To.Format()}", $"{this}");

                // corrects the sender other addresses
                message.Sender = new MailboxAddress(_config.SenderName, _config.SenderMailAddress);
                message.ReplyTo.AddRange(message.From.Mailboxes);
                message.ReplyTo.AddRange(message.Cc.Mailboxes);
                Client.Send(message);
            });
        }

        public void ForwardMessage(MimeMessage original, List<MailboxAddress> to) {
            if (to == null || to.Count == 0) {
                throw new Exception($"{this}: {nameof(SendMessage)} the to addresses can not be empty");
            }

            var forwardedMessage = new MimeMessage {
                Subject = original.Subject
            };

            // set the forwarded subject
            if (!original.Subject.StartsWith("FW:", StringComparison.OrdinalIgnoreCase))
                forwardedMessage.Subject = "FW: " + original.Subject;
            else
                forwardedMessage.Subject = original.Subject;

            var builder = new BodyBuilder();

            foreach (var bodyPart in original.BodyParts.Where(bodyPart => !bodyPart.ContentType.MediaType.Equals("text"))) {
                builder.LinkedResources.Add(bodyPart);
            }

            if (original.TextBody != null) {
                builder.TextBody = $"{GetMessageHeader(original)}{original.TextBody}";
            }

            if (original.HtmlBody != null) {
                builder.HtmlBody = AppendAfterBody(original.HtmlBody, GetMessageHeader(original));
            }

            forwardedMessage.Body = builder.ToMessageBody();
            forwardedMessage.To.AddRange(to);

            SendMessage(forwardedMessage);
        }

        private string GetMessageHeader(MimeMessage original) {
            if (original.HtmlBody != null) {
                return $"<hr>\n<div>From: {original.From.Format()}<br>\nSent: {DateUtils.FormatDate(original.Date)}<br>\nTo: {original.To.Format()}<br>\nCc: {original.Cc.Format()}<br>\nSubject: {original.Subject}</div>\n<br>\n<br>\n";
            }

            return $"__________________________________________\nFrom: {original.From.Format()}\nSent: {DateUtils.FormatDate(original.Date)}\nTo: {original.To.Format()}\nCc: {original.Cc.Format()}\nSubject: {original.Subject}\n\n";
        }

        private string AppendAfterBody(string originalHtmlBody, string toAppend) {
            var rgx = new Regex("(<body[^>]*>)");
            return rgx.Replace(originalHtmlBody, $"$1{toAppend}");
        }

        public override string ToString() {
            return $"{nameof(SmtpBasicClient)}.{MailBoxName}";
        }
    }
}