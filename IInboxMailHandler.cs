using System;
using MailHandler.Events;

namespace MailHandler {
    public interface IInboxMailHandler {
        string MailBoxName { get; }

        /// <summary>
        /// The UID of the last handled mail in a previous session that should be used when we start the mail handler. 
        /// IF its value is 0, all the mails in the inbox folder will be handled when the handler is started, otherwise
        /// all the mail with a UID inferior to this value will be ignored for the whole session
        /// </summary>
        uint LastHandledUid { get; set; }

        /// <summary>
        /// Published when a new mail arrives in the watched folder
        /// </summary>
        event EventHandler<NewMailEventArgs> NewMessage;

        /// <summary>
        /// Published when a new batch of mails is handled
        /// </summary>
        event EventHandler NewMessageBatchStarting;

        /// <summary>
        /// Published when a new batch of mails has been handled
        /// </summary>
        event EventHandler NewMessageBatchEnding;

        /// <summary>
        /// Start to listen for events from the imap server and publish new message events when needed
        /// </summary>
        /// <remarks>This does not blocks the thread and it can throw exception</remarks>
        /// <exception cref="Exception"></exception>
        void StartListening();

        /// <summary>
        /// Call this method to stop listening to imap server events
        /// </summary>
        void StopListening();
    }
}