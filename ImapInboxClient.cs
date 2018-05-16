#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (ImapInboxClient.cs) is part of MailHandler.
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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace MailHandler {
    internal class ImapInboxClient : IDisposable {
        private const int IncreaseRetryTimingUntil = 10;
        private const int InitialRetryTimingInMinutes = 1;
        private const int KeepAliveTimeout = 9 * 60 * 1000;

        private int _retryTimingInMinutes = InitialRetryTimingInMinutes;
        private int _retryCount;
        private ImapClient _imapClient;
        private readonly IClientFactory _factory;
        private readonly IMailHandlerConfiguration _config;
        private bool _anthenticatedSuccessfully;
        private System.Timers.Timer _timeoutKeepAlive;
        private IMailFolder _currentFolder;

        protected IMailFolder CurrentFolder => _currentFolder ?? (_currentFolder = _imapClient?.Inbox);

        protected string MailBoxName => _config.MailBoxId;

        protected ImapClient Client {
            get { return _imapClient; }
            set { _imapClient = value; }
        }

        protected IClientFactory Factory => _factory;

        public ImapInboxClient(IMailHandlerConfiguration config) {
            _config = config;
            _factory = _config.ClientFactory ?? new ClientFactory(_config);
            _timeoutKeepAlive = new System.Timers.Timer(KeepAliveTimeout) {
                AutoReset = true
            };
            _timeoutKeepAlive.Elapsed += TimeoutKeepAliveOnElapsed;
        }

        public virtual void Dispose() {
            _config.Tracer?.TraceVerbose($"{nameof(ImapInboxClient)} is disposing", $"{this}");
            _timeoutKeepAlive?.Close();
            _timeoutKeepAlive?.Dispose();
            DisconnectIfNeeded();
        }

        protected void DisconnectIfNeeded() {
            try {
                _timeoutKeepAlive?.Stop();
                if (Client != null) {
                    Client.Disconnected -= ImapClientOnDisconnected;
                }

                if (Client != null && Client.IsConnected) {
                    Client.Disconnect(true);
                    Client.Dispose();
                }
            } catch (Exception e) {
                _config.Tracer?.TraceError($"An error has occured while disconnecting - {e}", $"{this}");
            }
        }

        private void ImapClientOnDisconnected(object o, EventArgs eventArgs) {
            _config.Tracer?.TraceVerbose($"Client disconnected", $"{this}");
        }

        private bool ConnectAndAuthenticate() {
            try {
                // connexion and authentication
                Client = Factory.GetImapClient();
                _config.Tracer?.TraceInformation($"Connected and authenticated successfully", $"{this}");

                CurrentFolder.Open(FolderAccess.ReadWrite);
                _config.Tracer?.TraceVerbose($"Inbox opened", $"{this}");
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
                _timeoutKeepAlive?.Stop();
                try {
                    action?.Invoke();
                } catch (Exception e) {
                    if (!Client.IsConnected) {
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

        private void TimeoutKeepAliveOnElapsed(object sender, System.Timers.ElapsedEventArgs e) {
            DoOnLock(() => {
                CurrentFolder.Close();
                CurrentFolder.Open(FolderAccess.ReadWrite);
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

                Client.Disconnected -= ImapClientOnDisconnected;
                Client.Disconnected += ImapClientOnDisconnected;
                break;
            }

            _retryCount = 0;
            _retryTimingInMinutes = InitialRetryTimingInMinutes;
            _timeoutKeepAlive?.Start();
        }

        public int Count() {
            int count = -1;
            DoOnLock(() => {
                CurrentFolder.Check(); // needed to update the folder mail count
                count = CurrentFolder.Count;
            });
            return count;
        }

        public IList<UniqueId> GetUis() {
            IList<UniqueId> list = null;
            DoOnLock(() => {
                // FYI, this is equivalent (but faster/less consuming than) to
                // var fetch = folder.Fetch(min, -1, MailKit.MessageSummaryItems.UniqueId);
                list = CurrentFolder.Search(SearchQuery.All);
                // ex of another query : CurrentFolder.Search(SearchQuery.DeliveredAfter(deliverAfter));
            });
            return list ?? new List<UniqueId>();
        }

        public IList<IMessageSummary> GetFullMessagesSummaries(IList<UniqueId> uids) {
            IList<IMessageSummary> messages = null;
            DoOnLock(() => {
                messages = CurrentFolder.Fetch(uids, MessageSummaryItems.Full | MessageSummaryItems.BodyStructure);
            });
            return messages ?? new List<IMessageSummary>();
        }

        public MimeMessage DownloadMimeMessage(UniqueId uid) {
            MimeMessage message = null;
            DoOnLock(() => {
                message = CurrentFolder.GetMessage(uid);
            });
            return message;
        }

        public void DeleteMessage(IList<UniqueId> uids) {
            DoOnLock(() => {
                CurrentFolder.AddFlags(uids, MessageFlags.Deleted, null, true);
                CurrentFolder.Expunge();
            });
        }

        public void MoveMessagesToInboxSubFolder(IList<UniqueId> uids, string folder) {
            DoOnLock(() => {
                IMailFolder destFolder;
                try {
                    destFolder = CurrentFolder.GetSubfolder(folder);
                } catch (FolderNotFoundException) {
                    destFolder = CurrentFolder.Create(folder, true);
                }

                CurrentFolder.MoveTo(uids, destFolder);
            });
        }

        public string DownloadBody(IMessageSummary mail) {
            string output = null;
            DoOnLock(() => {
                // download the 'text/plain' body part, .Text is a convenience property that decodes the content and converts the result to a string for us
                if (mail.TextBody != null) {
                    output = (CurrentFolder.GetBodyPart(mail.UniqueId, mail.TextBody) as TextPart)?.Text;
                }

                if (string.IsNullOrEmpty(output) && mail.HtmlBody != null) {
                    output = (CurrentFolder.GetBodyPart(mail.UniqueId, mail.HtmlBody) as TextPart)?.Text;
                }
            });
            return output;
        }

        public List<string> SaveAttachments(MimeMessage mail, string directory, Action<Exception> onException) {
            if (!CreateDirectoryIfNeeded(directory, onException))
                return null;
            var output = new List<string>();
            DoOnLock(() => {
                output.AddRange(mail.Attachments.Select(attachment => PersistMimeEntity(attachment, directory, onException)).Where(path => !string.IsNullOrEmpty(path)));
            });
            return output;
        }

        public List<string> DownloadThenSaveAttachments(IMessageSummary message, string directory, Action<Exception> onException) {
            if (!CreateDirectoryIfNeeded(directory, onException))
                return null;
            var output = new List<string>();
            DoOnLock(() => {
                foreach (var attachment in message.Attachments) {
                    MimeEntity entity = null;
                    try {
                        entity = CurrentFolder.GetBodyPart(message.UniqueId, attachment);
                    } catch (Exception e) {
                        _config.Tracer?.TraceError($"Failed to save attachment an attachment - {e}", $"{this}");
                        onException?.Invoke(e);
                    }

                    if (entity == null)
                        continue;
                    var path = PersistMimeEntity(entity, directory, onException);
                    if (!string.IsNullOrEmpty(path)) {
                        output.Add(path);
                    }
                }
            });
            return output;
        }

        private string PersistMimeEntity(MimeEntity entity, string directory, Action<Exception> onException) {
            try {
                var fileName = entity.ContentDisposition?.FileName ?? Path.GetRandomFileName();
                var path = Path.Combine(directory, fileName);
                using (var stream = File.Create(path)) {
                    if (entity is MessagePart) {
                        var rfc822 = (MessagePart) entity;
                        rfc822.Message.WriteTo(stream);
                    } else {
                        var part = (MimePart) entity;
                        part.Content.DecodeTo(stream);
                    }
                }
                return path;
            } catch (Exception e) {
                _config.Tracer?.TraceError($"Failed to save attachment an attachment - {e}", $"{this}");
                onException?.Invoke(e);
            }
            return null;
        }
        
        public void DoForEachAttachments(MimeMessage mail, Action<string, byte[]> attachmentHandler, Action<Exception> onException) {
            DoOnLock(() => {
                mail.Attachments.ToList().ForEach(entity => DoWithMimeEntity(entity, attachmentHandler, onException));
            });
        }

        public void DownloadThenDoForEachAttachments(IMessageSummary message, Action<string, byte[]> attachmentHandler, Action<Exception> onException) {
            DoOnLock(() => {
                foreach (var attachment in message.Attachments) {
                    try {
                        var entity = CurrentFolder.GetBodyPart(message.UniqueId, attachment);
                        if (entity != null) {
                            DoWithMimeEntity(entity, attachmentHandler, onException);
                        }
                    } catch (Exception e) {
                        _config.Tracer?.TraceError($"Failed to get an attachment - {e}", $"{this}");
                        onException?.Invoke(e);
                    }
                }
            });
        }

        private void DoWithMimeEntity(MimeEntity entity, Action<string, byte[]> attachmentHandler, Action<Exception> onException) {
            try {
                using (var stream = new MemoryStream()) {
                    if (entity is MessagePart) {
                        var rfc822 = (MessagePart) entity;
                        rfc822.Message.WriteTo(stream);
                    } else {
                        var part = (MimePart) entity;
                        part.Content.DecodeTo(stream);
                    }
                    attachmentHandler?.Invoke(entity.ContentDisposition?.FileName ?? Path.GetRandomFileName(), stream.ToArray());
                }
            } catch (Exception e) {
                _config.Tracer?.TraceError($"Failed to stream an attachment - {e}", $"{this}");
                onException?.Invoke(e);
            }
        }

        private bool CreateDirectoryIfNeeded(string directory, Action<Exception> onException) {
            try {
                if (!Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }

                return true;
            } catch (Exception e) {
                _config.Tracer?.TraceError($"Failed to save attachment an attachment - {e}", $"{this}");
                onException?.Invoke(e);
            }

            return false;
        }

        public override string ToString() {
            return $"{nameof(ImapInboxClient)}.{MailBoxName}";
        }
    }
}