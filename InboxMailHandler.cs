#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (InboxMailHandler.cs) is part of MailHandler.
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
using System.Threading;
using System.Threading.Tasks;
using MailHandler.Events;
using MailHandler.Utilities;
using MailKit;

namespace MailHandler {
    public class InboxMailHandler : IDisposable, IInboxMailHandler {

        private const int MinimumDelayBeforeHandlingNewMails = 2000; //ms
        private const int MaximumDelayBeforeHandlingNewMails = 2000 * 10; //ms
        
        private IMailHandlerConfiguration _config;
        private ImapInboxIdler _imapInboxIdler;
        private Task _imapIdlerTask;
        private ImapInboxClient _imapClient;
        private SmtpBasicClient _smtpClient;
        private long _nbMails; // accessed from multiple threads
        private long _treatmentRequestFailed;
        private object _newMailTaskLock = new object();
        private AsapButDelayableAction _newMailHandler;

        public string MailBoxName { get; }
        
        private AsapButDelayableAction CheckForNewMail => _newMailHandler ?? (_newMailHandler = new AsapButDelayableAction(_config.MinimumDelayBeforeHandlingNewMails > 0 ? _config.MinimumDelayBeforeHandlingNewMails : MinimumDelayBeforeHandlingNewMails, _config.MaximumDelayBeforeHandlingNewMails > 0 ? _config.MaximumDelayBeforeHandlingNewMails : MaximumDelayBeforeHandlingNewMails, CheckForNewMailTick));

        /// <summary>
        /// The UID of the last handled mail in a previous session that should be used when we start the mail handler. 
        /// IF its value is 0, all the mails in the inbox folder will be handled when the handler is started, otherwise
        /// all the mail with a UID inferior to this value will be ignored for the whole session
        /// </summary>
        public uint LastHandledUid { get; set; }

        /// <summary>
        /// Published when a new mail arrives in the watched folder
        /// </summary>
        public event EventHandler<NewMailEventArgs> NewMessage;
        
        /// <summary>
        /// Published when a new batch of mails is handled
        /// </summary>
        public event EventHandler<BatchEventArgs> NewMessageBatchStarting;
        
        /// <summary>
        /// Published when a new batch of mails has been handled
        /// </summary>
        public event EventHandler<BatchEventArgs> NewMessageBatchEnding;

        public InboxMailHandler(IMailHandlerConfiguration config) {
            _config = config;
            MailBoxName = _config.MailBoxId;
        }

        public void Dispose() {
            _config.Tracer?.TraceVerbose($"{nameof(InboxMailHandler)} is disposing", $"{this}");

            _newMailHandler?.Dispose();

            DisconnectClientsIfNeeded();

            NewMessage = null;
            NewMessageBatchStarting = null;
            NewMessageBatchEnding = null;
            _config = null;
        }

        /// <summary>
        /// Start to listen for events from the imap server and publish new message events when needed
        /// </summary>
        /// <remarks>This does not blocks the thread and it can throw exception</remarks>
        /// <exception cref="Exception"></exception>
        public void StartListening() {
            if (_imapIdlerTask != null)
                throw new Exception($"{this}: Use {nameof(StopListening)} to stop listening before trying to {nameof(StartListening)}");

            _config.Tracer?.TraceVerbose($"{nameof(StartListening)}", $"{this}");

            DisconnectClientsIfNeeded();

            _imapInboxIdler = new ImapInboxIdler(_config);
            _imapClient = new ImapInboxClient(_config);
            _smtpClient = new SmtpBasicClient(_config);

            ConnectClients();

            SynchronizeMailCount(_imapClient.Count());
        }

        /// <summary>
        /// Call this method to stop listening to imap server events
        /// </summary>
        public void StopListening() {
            _config.Tracer?.TraceVerbose($"{nameof(StopListening)}", $"{this}");

            _imapInboxIdler.StopIdling();
            _imapIdlerTask.Wait();

            DisconnectClientsIfNeeded();
        }

        private void ConnectClients() {
            _imapInboxIdler.CountChanged += ImapInboxIdlerOnCountChanged;
            _imapInboxIdler.MessageExpunged += ImapInboxIdlerOnMessageExpunged;
            _imapInboxIdler.UidValidityChanged += ImapInboxIdlerOnUidValidityChanged;
            _imapIdlerTask = Task.Factory.StartNew(_imapInboxIdler.StartIdling);
            _imapClient.Connect();
            _smtpClient.Connect();
        }

        private void DisconnectClientsIfNeeded() {
            try {
                _newMailHandler?.Cancel();

                if (_imapInboxIdler != null) {
                    _imapInboxIdler.CountChanged -= ImapInboxIdlerOnCountChanged;
                    _imapInboxIdler.MessageExpunged -= ImapInboxIdlerOnMessageExpunged;
                    _imapInboxIdler.UidValidityChanged -= ImapInboxIdlerOnUidValidityChanged;
                }

                _imapInboxIdler?.Dispose();
                _imapInboxIdler = null;

                if (_imapIdlerTask != null && _imapIdlerTask.IsCanceled)
                    _imapIdlerTask?.Dispose();

                _imapClient?.Dispose();
                _imapClient = null;

                _smtpClient?.Dispose();
                _smtpClient = null;
            } catch (Exception e) {
                _config.Tracer?.TraceError($"An error has occured while disconnecting clients - {e}", $"{this}");
            }
        }

        private void ImapInboxIdlerOnUidValidityChanged(object sender, EventArgs e) {
            _config.Tracer?.TraceWarning($"The UidValidity of the mailbox has changed, dropping local cached data", $"{this}");
            LastHandledUid = 0;
            CheckForNewMail.DoDelayable();
        }

        private void ImapInboxIdlerOnMessageExpunged(object sender, MessageEventArgs e) {
            _config.Tracer?.TraceVerbose($"A mail was expunged at index {e.Index}", $"{this}");
            Interlocked.Decrement(ref _nbMails);
        }

        private void ImapInboxIdlerOnCountChanged(object sender, FolderEventArgs eventArgs) {
            var folder = eventArgs.Folder;
            var cacheMailBoxCount = Interlocked.Read(ref _nbMails);
            _config.Tracer?.TraceVerbose($"The number of messages in {folder} has changed to {folder.Count} emails and we have {cacheMailBoxCount} in cache", $"{this}");

            if (folder.Count > cacheMailBoxCount) {
                _config.Tracer?.TraceVerbose($"New messages have arrived!", $"{this}");
                SynchronizeMailCount(folder.Count);
            } else if (cacheMailBoxCount > folder.Count) {
                _config.Tracer?.TraceWarning($"Incoherence of mail count between the distant mail box ({folder.Count}) and local cache ({cacheMailBoxCount})!", $"{this}");
                SynchronizeMailCount(folder.Count);
            }
        }

        private void SynchronizeMailCount(int newCount) {
            Interlocked.Exchange(ref _nbMails, newCount);
            CheckForNewMail.DoDelayable();
        }

        private void CheckForNewMailTick() {
            if (Monitor.TryEnter(_newMailTaskLock)) {
                try {
                    do {
                        Interlocked.Exchange(ref _treatmentRequestFailed, 0);
                        CheckUnhandledMail();
                    } while (Interlocked.Read(ref _treatmentRequestFailed) > 0);
                } finally {
                    Monitor.Exit(_newMailTaskLock);
                }
            } else {
                Interlocked.Increment(ref _treatmentRequestFailed);
            }
        }

        private void CheckUnhandledMail() {
            _config.Tracer?.TraceVerbose($"Checking for new UIDs", $"{this}");

            var uids = _imapClient.GetUis();
            var unhandledUids = uids.Where(uid => uid.Id > LastHandledUid).OrderBy(uid => uid.Id).ToList();

            int maxMailPerBatch = int.MaxValue;
            while (unhandledUids.Count > 0) {
                maxMailPerBatch = Math.Min(maxMailPerBatch, unhandledUids.Count);
                var unhandledUidsBatch = unhandledUids.GetRange(0, maxMailPerBatch);

                _config.Tracer?.TraceVerbose($"Getting message summaries for a new batch of {maxMailPerBatch} mails", $"{this}");

                IList<IMessageSummary> messages;
                try {
                    messages = _imapClient.GetFullMessagesSummaries(unhandledUidsBatch);
                } catch (Exception e) {
                    _config.Tracer?.TraceError($"An error has occured while getting full messages summaries - {e}", $"{this}");

                    if (maxMailPerBatch > 1) {
                        // try again but fetch only 1 message at a time
                        maxMailPerBatch = 1;
                        continue;
                    }

                    messages = new List<IMessageSummary>();
                }
                
                NewMessageBatchStarting?.Invoke(this, new BatchEventArgs(new MailSimpleSmtpActuator(_smtpClient), _config.Tracer));

                foreach (var message in messages) {
                    _config.Tracer?.TraceVerbose($"New mail with subject -> {message.Envelope.Subject}", $"{this}");

                    var newMessageEvent = new NewMailEventArgs(message, new MailActuator(_imapClient, _smtpClient), _config.Tracer);
                    try {
                        //MulticastDelegate m = (MulticastDelegate)myEvent;  
                        ////Then you can get the delegate list...  
                        //Delegate [] dlist = m.GetInvocationList();  
                        ////and then iterate through to find and invoke the ones you want...  
                        //foreach(Delegate d in dlist)  
                        //{  
                        //    if(aListOfObjects.Contains(d.Target))  
                        //    {  
                        //        object [] p = { /*put your parameters here*/ };  
                        //        d.DynamicInvoke(p);  
                        //    }  
                        //    else  
                        //        MessageBox.Show("Not Invoking the Event for " + d.Target.ToString() + ":" + d.Target.GetHashCode().ToString());  
                        //}
                        NewMessage?.Invoke(this, newMessageEvent);
                    } catch (Exception e) {
                        _config.Tracer?.TraceError($"An error has occured in the {nameof(NewMessage)} event handler - {e}", $"{this}");
                    }

                    try {
                        if (newMessageEvent.ToDelete) {
                            _config.Tracer?.TraceVerbose($"Deleting message -> {message.Envelope.Subject}", $"{this}");
                            _imapClient.DeleteMessage(new List<UniqueId> {
                                message.UniqueId
                            });
                        } else if (!string.IsNullOrEmpty(newMessageEvent.MoveToSubfolder)) {
                            _config.Tracer?.TraceVerbose($"Moving message to {newMessageEvent.MoveToSubfolder} for subject -> {message.Envelope.Subject}", $"{this}");
                            _imapClient.MoveMessagesToInboxSubFolder(new List<UniqueId> {
                                message.UniqueId
                            }, newMessageEvent.MoveToSubfolder);
                        }
                    } catch (Exception e) {
                        _config.Tracer?.TraceError($"An error has occured after the {nameof(NewMessage)} event handler - {e}", $"{this}");
                    }

                    LastHandledUid = message.UniqueId.Id;
                }
                
                NewMessageBatchEnding?.Invoke(this, new BatchEventArgs(new MailSimpleSmtpActuator(_smtpClient), _config.Tracer));
               

                unhandledUids.RemoveRange(0, maxMailPerBatch);
            }
        }

        public override string ToString() {
            return $"{nameof(InboxMailHandler)}.{MailBoxName}";
        }

    }
}