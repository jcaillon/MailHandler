#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (MailActuator.cs) is part of MailHandler.
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
using MailKit;
using MimeKit;

namespace MailHandler {
    public class MailActuator : IMailActuator {
        
        private ImapInboxClient _imapClient;
        private SmtpBasicClient _smtpClient;

        internal MailActuator(ImapInboxClient imapClient, SmtpBasicClient smtpClient) {
            _imapClient = imapClient;
            _smtpClient = smtpClient;
        }

        public MimeMessage DownloadMimeMessage(IMessageSummary messageSummary) {
            return _imapClient.DownloadMimeMessage(messageSummary.UniqueId);
        }

        public void DownloadThenForwardMessage(IMessageSummary messageSummary, List<string> toMailAddresses) {
            ForwardMessage(DownloadMimeMessage(messageSummary), toMailAddresses);
        }

        public void ForwardMessage(MimeMessage message, List<string> toMailAddresses) {
            _smtpClient.ForwardMessage(message, toMailAddresses.Select(s => new MailboxAddress(s, s)).ToList());
        }

        public void SendMail(MimeMessage message) {
            _smtpClient.SendMessage(message);
        }

        public string DownloadMessageBody(IMessageSummary messageSummary) {
            return _imapClient.DownloadBody(messageSummary);
        }

        public List<string> SaveAttachments(MimeMessage mail, string directory, Action<Exception> onException = null) {
            return _imapClient.SaveAttachments(mail, directory, onException);
        }

        public void DoForEachAttachments(MimeMessage mail, Action<string, byte[]> attachmentHandler, Action<Exception> onException = null) {
            _imapClient.DoForEachAttachments(mail, attachmentHandler, onException);
        }

        public void DownloadThenDoForEachAttachments(IMessageSummary mail, Action<string, byte[]> attachmentHandler, Action<Exception> onException = null) {
            _imapClient.DownloadThenDoForEachAttachments(mail, attachmentHandler, onException);
        }

        public List<string> DownloadThenSaveAttachments(IMessageSummary message, string directory, Action<Exception> onException = null) {
            return _imapClient.DownloadThenSaveAttachments(message, directory, onException);
        }
    }
}